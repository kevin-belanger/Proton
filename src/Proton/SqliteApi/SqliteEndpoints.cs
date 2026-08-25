using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Proton.FileApi;
using Proton.Infrastructure;

namespace Proton.SqliteApi;

/// <summary>
/// Routage HTTP de l'API SQLite (§27 à §32).
///
/// <code>
/// POST /api/sqlite/{base}/query
/// POST /api/sqlite/{base}/execute
/// POST /api/sqlite/{base}/transaction
/// </code>
///
/// Le nom de la base est tout ce qui sépare le préfixe de l'action : il peut donc
/// contenir des sous-dossiers, comme <c>bases/inventaire.db</c>. Il est confiné à
/// <c>data</c> par les mêmes règles que l'API de fichiers (§26).
/// </summary>
public static class SqliteEndpoints
{
    private const string Prefix = "/api/sqlite";

    public static void Map(WebApplication application, SqliteService service)
    {
        application.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(Prefix, out PathString rest))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            await HandleAsync(context, service, rest.Value ?? string.Empty).ConfigureAwait(false);
        });
    }

    private static async Task HandleAsync(HttpContext context, SqliteService service, string rest)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.Headers.Allow = "POST";
            await ApiError.WriteAsync(context, StatusCodes.Status405MethodNotAllowed,
                ApiError.MethodNotAllowed, "The SQLite API expects POST.");
            return;
        }

        int separator = rest.LastIndexOf('/');
        if (separator <= 0)
        {
            await ApiError.WriteAsync(context, StatusCodes.Status404NotFound,
                ApiError.NotFound, "Expected /api/sqlite/{database}/{query|execute|transaction}.");
            return;
        }

        string action = rest[(separator + 1)..];
        string database = rest[..separator];

        DataPathResult path = service.Paths.Resolve(database);
        if (!path.IsValid || path.RelativePath.Length == 0)
        {
            // Une base ne peut pas se trouver ailleurs que dans `data` (§26).
            await ApiError.WriteAsync(context, StatusCodes.Status403Forbidden,
                ApiError.InvalidPath, "The database path is outside the data directory.");
            return;
        }

        try
        {
            switch (action)
            {
                case "query":
                    await QueryAsync(context, service, path);
                    return;
                case "execute":
                    await ExecuteAsync(context, service, path);
                    return;
                case "transaction":
                    await TransactionAsync(context, service, path);
                    return;
                default:
                    await ApiError.WriteAsync(context, StatusCodes.Status404NotFound,
                        ApiError.NotFound, $"Unknown action \"{action}\".");
                    return;
            }
        }
        catch (JsonException)
        {
            await ApiError.WriteAsync(context, StatusCodes.Status400BadRequest,
                ApiError.InvalidRequest, "The request body is not valid JSON.");
        }
        catch (SqliteException ex)
        {
            // Le message de SQLite est utile au développeur — « no such table »,
            // « syntax error » — et ne révèle rien du système de fichiers.
            await ApiError.WriteAsync(context, StatusCodes.Status422UnprocessableEntity,
                ApiError.SqlFailed, ex.Message,
                new Dictionary<string, string> { ["sqliteErrorCode"] = ex.SqliteErrorCode.ToString() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ApiError.WriteAsync(context, StatusCodes.Status500InternalServerError,
                ApiError.WriteFailed, "The database could not be opened.",
                new Dictionary<string, string> { ["reason"] = ex.GetType().Name });
        }
    }

    // --- Actions ----------------------------------------------------------------------

    private static async Task QueryAsync(HttpContext context, SqliteService service, DataPathResult path)
    {
        // Une lecture sur une base inexistante ne doit pas en créer une vide (§31).
        if (!SqliteService.Exists(path.FullPath))
        {
            await ApiError.WriteAsync(context, StatusCodes.Status404NotFound,
                ApiError.NotFound, "No such database.",
                new Dictionary<string, string> { ["database"] = path.RelativePath });
            return;
        }

        SqlCommand? command = await ReadCommandAsync(context);
        if (command is null)
        {
            await MissingSql(context);
            return;
        }

        QueryResult result = await service.QueryAsync(path.FullPath, command, context.RequestAborted);

        await context.Response.WriteAsJsonAsync(
            new { columns = result.Columns, rows = result.Rows },
            JsonOptions, context.RequestAborted);
    }

    private static async Task ExecuteAsync(HttpContext context, SqliteService service, DataPathResult path)
    {
        SqlCommand? command = await ReadCommandAsync(context);
        if (command is null)
        {
            await MissingSql(context);
            return;
        }

        ExecuteResult result = await service.ExecuteAsync(path.FullPath, command, context.RequestAborted);

        await context.Response.WriteAsJsonAsync(
            new { rowsAffected = result.RowsAffected, lastInsertRowId = result.LastInsertRowId },
            JsonOptions, context.RequestAborted);
    }

    private static async Task TransactionAsync(HttpContext context, SqliteService service, DataPathResult path)
    {
        TransactionBody? body = await context.Request
            .ReadFromJsonAsync<TransactionBody>(JsonOptions, context.RequestAborted);

        if (body?.Commands is null || body.Commands.Count == 0)
        {
            await ApiError.WriteAsync(context, StatusCodes.Status400BadRequest,
                ApiError.InvalidRequest, "A transaction requires at least one command.");
            return;
        }

        if (body.Commands.Any(c => string.IsNullOrWhiteSpace(c.Sql)))
        {
            await MissingSql(context);
            return;
        }

        var commands = body.Commands
            .Select(c => new SqlCommand(c.Sql!, c.Parameters))
            .ToList();

        ExecuteResult result = await service.TransactionAsync(
            path.FullPath, commands, context.RequestAborted);

        await context.Response.WriteAsJsonAsync(
            new { rowsAffected = result.RowsAffected, lastInsertRowId = result.LastInsertRowId },
            JsonOptions, context.RequestAborted);
    }

    // --- Lecture du corps -------------------------------------------------------------

    private static async Task<SqlCommand?> ReadCommandAsync(HttpContext context)
    {
        CommandBody? body = await context.Request
            .ReadFromJsonAsync<CommandBody>(JsonOptions, context.RequestAborted);

        return string.IsNullOrWhiteSpace(body?.Sql)
            ? null
            : new SqlCommand(body.Sql, body.Parameters);
    }

    private static Task MissingSql(HttpContext context) =>
        ApiError.WriteAsync(context, StatusCodes.Status400BadRequest,
            ApiError.InvalidRequest, "The \"sql\" property is required and cannot be empty.");

    private sealed record CommandBody(string? Sql, Dictionary<string, JsonElement>? Parameters);

    private sealed record TransactionBody(List<CommandBody>? Commands);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
