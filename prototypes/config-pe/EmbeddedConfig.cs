using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace ProtoPE;

public sealed class AppConfig
{
    public string Name { get; set; } = "";
    public string ExecutableName { get; set; } = "";
    public string? WindowTitle { get; set; }
    public string? Version { get; set; }
    public string? Company { get; set; }
}

/// <summary>
/// Stratégie A : la configuration est annexée en fin de fichier, APRÈS le bundle
/// single-file. Le bundle header offset reste donc valide et l'apphost ignore
/// ces octets supplémentaires.
///
/// Disposition : [ ... exe ... ][ JSON UTF-8 ][ longueur int32 LE ][ magic 8 octets ]
/// </summary>
public static class EmbeddedConfig
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PRTNCFG1");
    private const int FooterSize = 4 + 8; // longueur + magic

    /// <summary>Lit la configuration embarquée dans le fichier indiqué, ou null.</summary>
    public static AppConfig? TryRead(string exePath, out string? rawJson)
    {
        rawJson = null;
        using var fs = File.OpenRead(exePath);
        if (fs.Length < FooterSize) return null;

        Span<byte> footer = stackalloc byte[FooterSize];
        fs.Seek(-FooterSize, SeekOrigin.End);
        fs.ReadExactly(footer);

        if (!footer[4..].SequenceEqual(Magic)) return null;

        int length = BinaryPrimitives.ReadInt32LittleEndian(footer);
        if (length <= 0 || length > 1_000_000 || fs.Length < FooterSize + length) return null;

        byte[] json = new byte[length];
        fs.Seek(-(FooterSize + length), SeekOrigin.End);
        fs.ReadExactly(json);

        rawJson = Encoding.UTF8.GetString(json);
        try { return JsonSerializer.Deserialize<AppConfig>(rawJson, JsonOpts); }
        catch (JsonException) { return null; }
    }

    /// <summary>Longueur du contenu utile du fichier, trailer de configuration exclu.</summary>
    public static long PayloadLength(byte[] bytes)
    {
        if (bytes.Length < FooterSize) return bytes.Length;
        var footer = bytes.AsSpan(bytes.Length - FooterSize);
        if (!footer[4..].SequenceEqual(Magic)) return bytes.Length;
        int length = BinaryPrimitives.ReadInt32LittleEndian(footer);
        if (length <= 0 || bytes.Length < FooterSize + length) return bytes.Length;
        return bytes.Length - FooterSize - length;
    }

    /// <summary>Retire un éventuel trailer existant : indispensable pour que la
    /// génération soit récursive sans accumuler les configurations successives.</summary>
    public static byte[] Strip(byte[] bytes)
    {
        long payload = PayloadLength(bytes);
        return payload == bytes.Length ? bytes : bytes[..(int)payload];
    }

    public static byte[] Append(byte[] bytes, AppConfig config)
    {
        byte[] stripped = Strip(bytes);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(config, JsonOpts);

        byte[] result = new byte[stripped.Length + json.Length + FooterSize];
        stripped.CopyTo(result, 0);
        json.CopyTo(result, stripped.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(stripped.Length + json.Length), json.Length);
        Magic.CopyTo(result, stripped.Length + json.Length + 4);
        return result;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
