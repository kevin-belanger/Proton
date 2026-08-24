using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Proton.Configuration;

namespace Proton.Personalization;

/// <summary>
/// Configuration embarquée dans l'exécutable (§39).
///
/// Elle est annexée <b>après</b> le bundle .NET plutôt que stockée en ressource PE :
/// le bundle ignore ces octets, et aucun de ses décalages n'est affecté. Voir
/// <c>docs/01-personnalisation-executable.md</c>.
///
/// <code>
/// [ ... exécutable ... ][ JSON UTF-8 ][ longueur int32 ][ magie 8 octets ]
/// </code>
/// </summary>
public static class EmbeddedConfig
{
    private static readonly byte[] Magic = "PRTNCFG1"u8.ToArray();
    private const int FooterSize = 4 + 8;
    private const int MaxConfigSize = 1_000_000;

    /// <summary>Lit la configuration embarquée dans le fichier indiqué, ou null.</summary>
    public static AppConfiguration? TryRead(string executablePath)
    {
        using var file = File.OpenRead(executablePath);
        if (file.Length < FooterSize) return null;

        Span<byte> footer = stackalloc byte[FooterSize];
        file.Seek(-FooterSize, SeekOrigin.End);
        file.ReadExactly(footer);

        if (!footer[4..].SequenceEqual(Magic)) return null;

        int length = BinaryPrimitives.ReadInt32LittleEndian(footer);
        if (length <= 0 || length > MaxConfigSize || file.Length < FooterSize + length) return null;

        byte[] json = new byte[length];
        file.Seek(-(FooterSize + length), SeekOrigin.End);
        file.ReadExactly(json);

        try
        {
            return JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Une configuration illisible ne doit pas empêcher le démarrage : mieux
            // vaut l'identité du moteur qu'un refus de fonctionner (§54).
            return null;
        }
    }

    /// <summary>Longueur du contenu utile, trailer de configuration exclu.</summary>
    public static long PayloadLength(byte[] bytes)
    {
        if (bytes.Length < FooterSize) return bytes.Length;

        var footer = bytes.AsSpan(bytes.Length - FooterSize);
        if (!footer[4..].SequenceEqual(Magic)) return bytes.Length;

        int length = BinaryPrimitives.ReadInt32LittleEndian(footer);
        if (length <= 0 || bytes.Length < FooterSize + length) return bytes.Length;

        return bytes.Length - FooterSize - length;
    }

    /// <summary>
    /// Retire un éventuel trailer hérité.
    /// </summary>
    /// <remarks>
    /// Indispensable pour que la génération soit récursive : sans cela, chaque
    /// génération empilerait la configuration de son parent (§38, CA-17).
    /// </remarks>
    public static byte[] Strip(byte[] bytes)
    {
        long payload = PayloadLength(bytes);
        return payload == bytes.Length ? bytes : bytes[..(int)payload];
    }

    /// <summary>Annexe une configuration, en remplaçant celle qui s'y trouverait déjà.</summary>
    public static byte[] Append(byte[] bytes, AppConfiguration configuration)
    {
        byte[] stripped = Strip(bytes);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(configuration, JsonOptions);

        byte[] result = new byte[stripped.Length + json.Length + FooterSize];
        stripped.CopyTo(result, 0);
        json.CopyTo(result, stripped.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(stripped.Length + json.Length), json.Length);
        Magic.CopyTo(result, stripped.Length + json.Length + 4);

        return result;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
