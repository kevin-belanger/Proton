using System.Text.Json;
using Microsoft.Data.Sqlite;
using Proton.FileApi;

namespace Proton.SqliteApi;

/// <summary>Une commande SQL et ses paramètres nommés (§28).</summary>
public sealed record SqlCommand(string Sql, IReadOnlyDictionary<string, JsonElement>? Parameters);

/// <summary>Résultat d'une lecture (§29).</summary>
public sealed record QueryResult(IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows);

/// <summary>Résultat d'une écriture (§30).</summary>
public sealed record ExecuteResult(int RowsAffected, long LastInsertRowId);

/// <summary>
/// Accès aux bases SQLite de <c>data</c> (§25 à §34).
///
/// Une connexion est ouverte et refermée pour chaque opération. Les requêtes HTTP
/// pouvant arriver simultanément, partager une connexion exposerait à des états
/// entremêlés ; le coût d'ouverture est négligeable en local (§33).
/// </summary>
public sealed class SqliteService(DataPath paths)
{
    private readonly DataPath _paths = paths;

    public DataPath Paths => _paths;

    /// <summary>Une base inexistante n'est jamais créée par une lecture (§31).</summary>
    public static bool Exists(string fullPath) => File.Exists(fullPath);

    // --- Lecture --------------------------------------------------------------------

    public async Task<QueryResult> QueryAsync(
        string fullPath, SqlCommand command, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(fullPath, create: false, cancellationToken)
            .ConfigureAwait(false);

        await using SqliteCommand statement = Prepare(connection, command);
        await using SqliteDataReader reader = await statement
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        // Les noms de colonnes sont retournés à part : deux colonnes peuvent porter
        // le même nom, ce qu'un objet par ligne ne saurait représenter (§29).
        var columns = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            columns[i] = reader.GetName(i);

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                row[i] = SqliteValue.ToJson(reader.IsDBNull(i) ? null : reader.GetValue(i));
            rows.Add(row);
        }

        return new QueryResult(columns, rows);
    }

    // --- Écriture -------------------------------------------------------------------

    public async Task<ExecuteResult> ExecuteAsync(
        string fullPath, SqlCommand command, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(fullPath, create: true, cancellationToken)
            .ConfigureAwait(false);

        await using SqliteCommand statement = Prepare(connection, command);
        int affected = await statement.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return new ExecuteResult(affected, await LastInsertRowIdAsync(connection, cancellationToken)
            .ConfigureAwait(false));
    }

    // --- Transaction ----------------------------------------------------------------

    /// <summary>
    /// Exécute plusieurs commandes en une seule transaction (§32).
    /// </summary>
    /// <remarks>
    /// Si l'une échoue, aucune ne demeure appliquée. La validation n'a lieu qu'après
    /// la dernière ; toute exception laisse la transaction être annulée à la
    /// libération.
    /// </remarks>
    public async Task<ExecuteResult> TransactionAsync(
        string fullPath, IReadOnlyList<SqlCommand> commands, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(fullPath, create: true, cancellationToken)
            .ConfigureAwait(false);

        await using SqliteTransaction transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        int affected = 0;

        foreach (SqlCommand command in commands)
        {
            await using SqliteCommand statement = Prepare(connection, command);
            statement.Transaction = transaction;
            affected += await statement.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        long lastRowId = await LastInsertRowIdAsync(connection, cancellationToken, transaction)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new ExecuteResult(affected, lastRowId);
    }

    // --- Connexion ------------------------------------------------------------------

    private static async Task<SqliteConnection> OpenAsync(
        string fullPath, bool create, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            // Une lecture ne doit pas créer une base vide par inadvertance (§31).
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false
        };

        if (create)
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Confine(connection);
            await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Interdit à une base d'en attacher une autre (§34).
    /// </summary>
    /// <remarks>
    /// La limite est posée dans le moteur lui-même plutôt qu'en analysant le SQL :
    /// un filtre textuel se contourne, une limite non. Le chargement d'extensions
    /// natives est déjà désactivé par défaut dans Microsoft.Data.Sqlite.
    ///
    /// L'objet n'est pas de se défendre d'une application hostile — elle n'aurait pas
    /// besoin de Proton pour cela (§3.4) — mais de tenir une promesse simple : une
    /// base Proton vit dans `data`, et l'API ne sert pas de porte dérobée vers le
    /// reste du disque.
    /// </remarks>
    private static void Confine(SqliteConnection connection) =>
        SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_ATTACHED, 0);

    private static async Task ConfigureAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand pragma = connection.CreateCommand();

        // WAL : les lectures ne bloquent plus l'écriture, ce qui compte dès que
        // plusieurs requêtes HTTP se présentent ensemble (§33).
        // busy_timeout : une base momentanément verrouillée fait patienter plutôt
        // qu'échouer aussitôt.
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SqliteCommand Prepare(SqliteConnection connection, SqlCommand command)
    {
        SqliteCommand statement = connection.CreateCommand();
        statement.CommandText = command.Sql;

        if (command.Parameters is null)
            return statement;

        // Les paramètres évitent à l'application de concaténer ses données dans le
        // SQL (§28). Le nom est transmis tel quel : SQLite accepte $, @ et :.
        foreach ((string name, JsonElement value) in command.Parameters)
            statement.Parameters.AddWithValue(name, SqliteValue.FromJson(value));

        return statement;
    }

    private static async Task<long> LastInsertRowIdAsync(
        SqliteConnection connection, CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT last_insert_rowid();";
        command.Transaction = transaction;

        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is long id ? id : 0;
    }
}
