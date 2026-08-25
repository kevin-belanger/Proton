using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Proton.Infrastructure;

namespace Proton.FileApi;

/// <summary>
/// Routage HTTP de l'API de fichiers (§13 à §22).
///
/// Cette couche traduit : elle valide le chemin, appelle le service, et transforme
/// le résultat — ou l'échec — en code HTTP et en corps de réponse. Aucune logique de
/// fichier ne lui appartient (§47).
/// </summary>
public static class FilesEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>
    /// Branche l'API sur <c>/data</c>.
    /// </summary>
    /// <remarks>
    /// Un middleware plutôt qu'un endpoint routé : l'ordre du pipeline devient celui
    /// qu'on lit, et l'API passe donc avant le service de fichiers statiques. Un
    /// endpoint s'exécuterait en fin de chaîne, et un fichier <c>app/data/x.html</c>
    /// aurait alors pu capturer la route (§49).
    /// </remarks>
    public static void Map(WebApplication application, DataFileService service)
    {
        application.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/files"))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            await HandleAsync(context, service).ConfigureAwait(false);
        });
    }

    private static async Task HandleAsync(HttpContext context, DataFileService service)
    {
        // Le chemin complet est utilisé plutôt que le paramètre de route : celui-ci
        // perd la barre oblique finale, qui distingue un dossier d'un fichier (§22.1).
        string requestPath = context.Request.Path.Value ?? string.Empty;
        string relative = requestPath.Length > "/files".Length
            ? requestPath["/files".Length..]
            : string.Empty;

        DataPathResult path = service.Paths.Resolve(relative);

        if (!path.IsValid)
        {
            await ApiError.WriteAsync(context, StatusCodes.Status403Forbidden,
                ApiError.InvalidPath,
                "The path is outside the data directory or contains invalid characters.");
            return;
        }

        try
        {
            switch (context.Request.Method)
            {
                case "GET":
                case "HEAD":
                    await GetAsync(context, path);
                    return;
                case "PUT":
                    await PutAsync(context, path);
                    return;
                case "DELETE":
                    await DeleteAsync(context, path);
                    return;
                default:
                    context.Response.Headers.Allow = "GET, HEAD, PUT, DELETE";
                    await ApiError.WriteAsync(context, StatusCodes.Status405MethodNotAllowed,
                        ApiError.MethodNotAllowed, "This method is not supported on /files.");
                    return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Disque plein, fichier verrouillé, droits insuffisants, nom refusé par
            // Windows : tous relèvent du même traitement (§17.1). Les distinguer par
            // avance produirait un code fragile pour un bénéfice nul.
            await WriteOperationFailure(context, ex);
        }
    }

    // --- Lecture --------------------------------------------------------------------

    private static async Task GetAsync(HttpContext context, DataPathResult path)
    {
        EntryKind kind = DataFileService.Inspect(path.FullPath);

        if (path.IsDirectoryRequest)
        {
            if (kind != EntryKind.Directory)
            {
                await NotFound(context, path);
                return;
            }

            await WriteListing(context, path);
            return;
        }

        // Un chemin sans barre oblique finale désignant un dossier est redirigé vers
        // sa forme canonique plutôt que traité comme introuvable (§22.1).
        if (kind == EntryKind.Directory)
        {
            context.Response.Redirect($"/files/{path.RelativePath}/", permanent: true);
            return;
        }

        if (kind == EntryKind.File)
        {
            await WriteFile(context, path);
            return;
        }

        await NotFound(context, path);
    }

    private static async Task WriteFile(HttpContext context, DataPathResult path)
    {
        var file = new FileInfo(path.FullPath);
        string etag = DataFileService.ComputeETag(file);

        context.Response.Headers.ETag = etag;
        context.Response.Headers.LastModified =
            file.LastWriteTimeUtc.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        // Requête conditionnelle : rien n'a changé, le corps n'a pas à être renvoyé.
        if (context.Request.Headers.IfNoneMatch.Contains(etag))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        context.Response.ContentType =
            ContentTypes.TryGetContentType(path.FullPath, out string? type)
                ? type
                : "application/octet-stream";

        context.Response.ContentLength = file.Length;

        // §15.1 — téléchargement explicite, à la main de l'application.
        if (context.Request.Query.ContainsKey("download"))
        {
            string name = Path.GetFileName(path.FullPath);
            context.Response.Headers.ContentDisposition =
                $"attachment; filename*=UTF-8''{Uri.EscapeDataString(name)}";
        }

        if (HttpMethods.IsHead(context.Request.Method))
            return;

        await using FileStream source = DataFileService.OpenRead(path.FullPath);
        await source.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static async Task WriteListing(HttpContext context, DataPathResult path)
    {
        IReadOnlyList<DataEntry> entries = DataFileService.List(path.FullPath);

        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(
            new { path = path.RelativePath, entries },
            context.RequestAborted);
    }

    // --- Écriture -------------------------------------------------------------------

    private static async Task PutAsync(HttpContext context, DataPathResult path)
    {
        if (path.IsDirectoryRequest)
        {
            await CreateDirectory(context, path);
            return;
        }

        if (DataFileService.Inspect(path.FullPath) == EntryKind.Directory)
        {
            await ApiError.WriteAsync(context, StatusCodes.Status409Conflict,
                ApiError.NotAFile, "A directory already exists at this path.");
            return;
        }

        WriteOutcome outcome = await DataFileService.WriteAsync(
            path.FullPath, context.Request.Body, context.RequestAborted);

        var written = new FileInfo(path.FullPath);
        context.Response.Headers.ETag = DataFileService.ComputeETag(written);

        context.Response.StatusCode = outcome == WriteOutcome.Created
            ? StatusCodes.Status201Created
            : StatusCodes.Status204NoContent;
    }

    private static async Task CreateDirectory(HttpContext context, DataPathResult path)
    {
        if (DataFileService.Inspect(path.FullPath) == EntryKind.File)
        {
            await ApiError.WriteAsync(context, StatusCodes.Status409Conflict,
                ApiError.NotADirectory, "A file already exists at this path.");
            return;
        }

        bool existed = Directory.Exists(path.FullPath);
        Directory.CreateDirectory(path.FullPath);

        // La création est idempotente : recommencer n'est pas une erreur (§22.2).
        context.Response.StatusCode = existed
            ? StatusCodes.Status204NoContent
            : StatusCodes.Status201Created;
    }

    // --- Suppression ----------------------------------------------------------------

    private static async Task DeleteAsync(HttpContext context, DataPathResult path)
    {
        EntryKind kind = DataFileService.Inspect(path.FullPath);

        if (kind == EntryKind.Missing)
        {
            await NotFound(context, path);
            return;
        }

        if (path.IsDirectoryRequest || kind == EntryKind.Directory)
        {
            await DeleteDirectory(context, path);
            return;
        }

        File.Delete(path.FullPath);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task DeleteDirectory(HttpContext context, DataPathResult path)
    {
        // La racine de `data` est l'espace de stockage de l'application : son contenu
        // lui appartient, mais pas son existence (§22.4).
        if (path.RelativePath.Length == 0)
        {
            await ApiError.WriteAsync(context, StatusCodes.Status403Forbidden,
                ApiError.InvalidPath, "The data directory itself cannot be deleted.");
            return;
        }

        bool recursive = context.Request.Query.ContainsKey("recursive");

        if (!recursive && !DataFileService.IsEmpty(path.FullPath))
        {
            // Détruire un contenu ne peut jamais résulter d'un oubli (§22.3).
            await ApiError.WriteAsync(context, StatusCodes.Status409Conflict,
                ApiError.DirectoryNotEmpty,
                "The directory is not empty. Add ?recursive=1 to delete its contents.");
            return;
        }

        if (recursive)
            DataFileService.DeleteDirectoryRecursive(path.FullPath);
        else
            Directory.Delete(path.FullPath, recursive: false);

        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    // --- Réponses communes ----------------------------------------------------------

    private static Task NotFound(HttpContext context, DataPathResult path) =>
        ApiError.WriteAsync(context, StatusCodes.Status404NotFound,
            ApiError.NotFound, "No such file or directory.",
            new Dictionary<string, string> { ["path"] = path.RelativePath });

    private static Task WriteOperationFailure(HttpContext context, Exception exception)
    {
        string code = context.Request.Method switch
        {
            "PUT" => ApiError.WriteFailed,
            "DELETE" => ApiError.DeleteFailed,
            _ => ApiError.ReadFailed
        };

        // Le message du système peut contenir un chemin physique ; l'application Web
        // raisonne en chemins relatifs à `data` et n'a pas à le connaître (§7, §17.1).
        return ApiError.WriteAsync(context, StatusCodes.Status500InternalServerError,
            code, "The operation could not be completed.",
            new Dictionary<string, string> { ["reason"] = exception.GetType().Name });
    }
}
