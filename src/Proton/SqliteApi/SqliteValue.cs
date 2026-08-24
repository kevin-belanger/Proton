using System.Text.Json;

namespace Proton.SqliteApi;

/// <summary>
/// Traduction des valeurs entre SQLite et JSON (§29).
///
/// SQLite est typé par valeur et non par colonne : une même colonne peut contenir un
/// entier sur une ligne et du texte sur la suivante. La correspondance se fait donc
/// valeur par valeur.
/// </summary>
public static class SqliteValue
{
    /// <summary>Clé qui distingue un contenu binaire d'une chaîne de caractères.</summary>
    public const string BlobProperty = "base64";

    /// <summary>Convertit une valeur lue de SQLite en valeur sérialisable.</summary>
    public static object? ToJson(object? value) => value switch
    {
        null or DBNull => null,
        byte[] blob => new Dictionary<string, string> { [BlobProperty] = Convert.ToBase64String(blob) },
        _ => value
    };

    /// <summary>Convertit un paramètre JSON en valeur acceptable par SQLite.</summary>
    /// <remarks>
    /// Un objet portant la propriété <c>base64</c> est interprété comme du binaire :
    /// c'est la forme réciproque de <see cref="ToJson"/>, ce qui permet de relire une
    /// valeur puis de la réécrire sans perte.
    /// </remarks>
    public static object FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => DBNull.Value,
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number => ToNumber(element),
        JsonValueKind.Object => ToBlobOrText(element),
        // Un tableau n'a pas d'équivalent SQLite : le transmettre tel quel serait
        // plus honnête que de deviner une conversion.
        _ => element.GetRawText()
    };

    private static object ToNumber(JsonElement element) =>
        element.TryGetInt64(out long entier) ? entier : element.GetDouble();

    private static object ToBlobOrText(JsonElement element) =>
        element.TryGetProperty(BlobProperty, out JsonElement blob)
        && blob.ValueKind == JsonValueKind.String
            ? Convert.FromBase64String(blob.GetString()!)
            : element.GetRawText();
}
