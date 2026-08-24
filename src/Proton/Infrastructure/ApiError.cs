using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Proton.Infrastructure;

/// <summary>
/// Format uniforme des erreurs d'API (§24).
///
/// Le <c>code</c> est stable et destiné au programme : une application JavaScript
/// doit pouvoir réagir sans analyser le texte du message, qui s'adresse aux humains
/// et peut changer.
/// </summary>
public static class ApiError
{
    // --- Codes stables ------------------------------------------------------------

    public const string InvalidPath = "invalid_path";
    public const string NotFound = "not_found";
    public const string DirectoryNotEmpty = "directory_not_empty";
    public const string NotADirectory = "not_a_directory";
    public const string NotAFile = "not_a_file";
    public const string WriteFailed = "write_failed";
    public const string DeleteFailed = "delete_failed";
    public const string ReadFailed = "read_failed";
    public const string PayloadTooLarge = "payload_too_large";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string NotImplemented = "not_implemented";
    public const string InvalidRequest = "invalid_request";
    public const string SqlFailed = "sql_failed";
    public const string Unexpected = "unexpected_error";

    /// <summary>Écrit une réponse d'erreur au format uniforme.</summary>
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, string>? details = null)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new ErrorEnvelope(new ErrorBody(code, message, details));

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, SerializerOptions),
            context.RequestAborted);
    }

    private sealed record ErrorEnvelope(
        [property: JsonPropertyName("error")] ErrorBody Error);

    private sealed record ErrorBody(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("details")] IReadOnlyDictionary<string, string>? Details);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Le message peut contenir des accents ou des noms de fichiers : les échapper
        // rendrait la réponse illisible sans rien apporter.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
