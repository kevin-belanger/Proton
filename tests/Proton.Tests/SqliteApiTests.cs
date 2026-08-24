using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Proton.Bootstrap;
using Proton.Hosting;

namespace Proton.Tests;

/// <summary>
/// API SQLite, de bout en bout (§25 à §34, CA-11 et CA-12).
/// </summary>
public sealed class SqliteApiTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "proton-tests", Guid.NewGuid().ToString("N"));

    private LocalWebHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        ApplicationPaths paths = ApplicationPaths.ForRoot(_root);
        Scaffolding.Ensure(paths);

        _host = await LocalWebHost.StartAsync(paths);
        _client = new HttpClient { BaseAddress = _host.Address, Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private Task<HttpResponseMessage> Execute(string sql, object? parameters = null) =>
        _client.PostAsJsonAsync("/api/sqlite/app.db/execute", new { sql, parameters });

    private Task<HttpResponseMessage> Query(string sql, object? parameters = null) =>
        _client.PostAsJsonAsync("/api/sqlite/app.db/query", new { sql, parameters });

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // --- CA-11 : le cycle complet -----------------------------------------------------

    [Fact]
    public async Task Cree_insere_lit_modifie_et_supprime()
    {
        // Le tout uniquement par HTTP, comme le ferait une application JavaScript.
        Assert.True((await Execute(
            "CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)")).IsSuccessStatusCode);

        HttpResponseMessage inserted = await Execute(
            "INSERT INTO users(name) VALUES($name)", new Dictionary<string, object> { ["$name"] = "Alice" });

        JsonElement insertion = await Json(inserted);
        Assert.Equal(1, insertion.GetProperty("rowsAffected").GetInt32());
        Assert.Equal(1, insertion.GetProperty("lastInsertRowId").GetInt64());

        JsonElement read = await Json(await Query("SELECT id, name FROM users"));
        Assert.Equal("id", read.GetProperty("columns")[0].GetString());
        Assert.Equal("Alice", read.GetProperty("rows")[0][1].GetString());

        await Execute("UPDATE users SET name = $name WHERE id = $id",
            new Dictionary<string, object> { ["$name"] = "Alice Martin", ["$id"] = 1 });

        JsonElement modified = await Json(await Query("SELECT name FROM users"));
        Assert.Equal("Alice Martin", modified.GetProperty("rows")[0][0].GetString());

        await Execute("DELETE FROM users WHERE id = $id",
            new Dictionary<string, object> { ["$id"] = 1 });

        JsonElement empty = await Json(await Query("SELECT name FROM users"));
        Assert.Empty(empty.GetProperty("rows").EnumerateArray());
    }

    // --- CA-12 : atomicité -------------------------------------------------------------

    [Fact]
    public async Task Une_transaction_echouee_n_applique_rien()
    {
        await Execute("CREATE TABLE comptes (id INTEGER PRIMARY KEY, solde INTEGER)");
        await Execute("INSERT INTO comptes VALUES (1, 100), (2, 100)");

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/sqlite/app.db/transaction",
            new
            {
                commands = new object[]
                {
                    new { sql = "UPDATE comptes SET solde = solde - 100 WHERE id = 1" },
                    // La seconde échoue : la table n'existe pas.
                    new { sql = "UPDATE inexistante SET x = 1" }
                }
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // La première commande ne doit pas avoir survécu.
        JsonElement soldes = await Json(await Query("SELECT solde FROM comptes ORDER BY id"));
        Assert.Equal(100, soldes.GetProperty("rows")[0][0].GetInt32());
    }

    [Fact]
    public async Task Une_transaction_reussie_applique_tout()
    {
        await Execute("CREATE TABLE comptes (id INTEGER PRIMARY KEY, solde INTEGER)");
        await Execute("INSERT INTO comptes VALUES (1, 100), (2, 100)");

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/sqlite/app.db/transaction",
            new
            {
                commands = new object[]
                {
                    new { sql = "UPDATE comptes SET solde = solde - 30 WHERE id = 1" },
                    new { sql = "UPDATE comptes SET solde = solde + 30 WHERE id = 2" }
                }
            });

        Assert.True(response.IsSuccessStatusCode);

        JsonElement soldes = await Json(await Query("SELECT solde FROM comptes ORDER BY id"));
        Assert.Equal(70, soldes.GetProperty("rows")[0][0].GetInt32());
        Assert.Equal(130, soldes.GetProperty("rows")[1][0].GetInt32());
    }

    // --- §29 : sérialisation des valeurs -----------------------------------------------

    [Fact]
    public async Task Serialise_chaque_type_sans_ambiguite()
    {
        await Execute("CREATE TABLE valeurs (t TEXT, i INTEGER, r REAL, b BLOB, n TEXT)");
        await Execute(
            "INSERT INTO valeurs VALUES ($t, $i, $r, $b, NULL)",
            new Dictionary<string, object?>
            {
                ["$t"] = "texte",
                ["$i"] = 42,
                ["$r"] = 1.5,
                // Réciproque de la lecture : un objet « base64 » désigne du binaire.
                ["$b"] = new Dictionary<string, string> { ["base64"] = "AQID" }
            });

        JsonElement row = (await Json(await Query("SELECT t, i, r, b, n FROM valeurs")))
            .GetProperty("rows")[0];

        Assert.Equal("texte", row[0].GetString());
        Assert.Equal(42, row[1].GetInt32());
        Assert.Equal(1.5, row[2].GetDouble());
        Assert.Equal("AQID", row[3].GetProperty("base64").GetString());
        Assert.Equal(JsonValueKind.Null, row[4].ValueKind);
    }

    [Fact]
    public async Task Conserve_les_colonnes_homonymes()
    {
        // La base doit exister : une lecture n'en crée jamais (§31).
        await Execute("CREATE TABLE t (x INTEGER)");

        // Un objet par ligne ne saurait représenter deux colonnes de même nom : c'est
        // la raison du tableau `columns` séparé (§29).
        JsonElement result = await Json(await Query("SELECT 1 AS x, 2 AS x"));

        Assert.Equal(2, result.GetProperty("columns").GetArrayLength());
        Assert.Equal(2, result.GetProperty("rows")[0].GetArrayLength());
    }

    // --- §31 : création de la base -----------------------------------------------------

    [Fact]
    public async Task Une_lecture_ne_cree_pas_de_base()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/sqlite/jamais-vue.db/query", new { sql = "SELECT 1" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(_root, "data", "jamais-vue.db")));
    }

    [Fact]
    public async Task Une_ecriture_cree_la_base()
    {
        await Execute("CREATE TABLE t (x INTEGER)");

        Assert.True(File.Exists(Path.Combine(_root, "data", "app.db")));
    }

    [Fact]
    public async Task Accepte_une_base_dans_un_sous_dossier()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/sqlite/bases/inventaire.db/execute", new { sql = "CREATE TABLE t (x INTEGER)" });

        Assert.True(response.IsSuccessStatusCode);
        Assert.True(File.Exists(Path.Combine(_root, "data", "bases", "inventaire.db")));
    }

    // --- §26 et §34 : confinement --------------------------------------------------------

    [Theory]
    [InlineData("/api/sqlite/C:/temp/ailleurs.db/execute")]
    [InlineData("/api/sqlite//execute")]
    public async Task Refuse_une_base_hors_de_data(string url)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(url, new { sql = "SELECT 1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Une_remontee_dans_l_url_n_atteint_jamais_l_api()
    {
        // Les remontées sont normalisées par la pile HTTP avant d'arriver au
        // gestionnaire : « /api/sqlite/../x.db/execute » devient « /api/x.db/execute »,
        // qui n'est plus une route SQLite. Le confinement de DataPath reste la garde
        // pour tout ce qui parvient jusqu'ici.
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/sqlite/../evasion.db/execute", new { sql = "SELECT 1" });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(_root, "evasion.db")));
    }

    [Fact]
    public async Task Refuse_d_attacher_une_autre_base()
    {
        await Execute("CREATE TABLE t (x INTEGER)");

        // La limite est posée dans le moteur : ATTACH ne peut pas servir de porte
        // dérobée vers le reste du disque (§34).
        HttpResponseMessage response = await Execute("ATTACH DATABASE 'C:\\temp\\autre.db' AS autre");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // --- Erreurs --------------------------------------------------------------------------

    [Fact]
    public async Task Une_erreur_sql_est_signalee_avec_son_message()
    {
        await Execute("CREATE TABLE t (x INTEGER)");

        HttpResponseMessage response = await Query("SELECT * FROM table_absente");
        string json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("sql_failed", json, StringComparison.Ordinal);
        // Le message de SQLite est ce qui aide le développeur à corriger sa requête.
        Assert.Contains("table_absente", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exige_une_requete_sql()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/sqlite/app.db/execute", new { sql = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refuse_une_action_inconnue()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/sqlite/app.db/vacuum", new { sql = "SELECT 1" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refuse_les_methodes_autres_que_post()
    {
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await _client.GetAsync("/api/sqlite/app.db/query")).StatusCode);
    }
}
