using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Proton.Configuration;

namespace Proton.Personalization;

/// <summary>
/// Contenu embarqué dans l'exécutable par le mode <c>/generate</c> (§39, §39.1).
///
/// Le trailer est annexé <b>après</b> le bundle .NET plutôt que stocké en ressource
/// PE : le bundle ignore ces octets, et aucun de ses décalages n'est affecté. Voir
/// <c>notes/01-personnalisation-executable.md</c>.
///
/// <code>
/// [ ... exécutable ... ][ archive ZIP ][ taille int64 ][ magie 8 octets ]
/// </code>
///
/// L'archive porte la configuration et les dossiers :
///
/// <code>
/// config.json
/// app/...        toujours présent — l'application, servie depuis l'archive
/// data/...       si le développeur l'a demandé — contenu initial de files et db
/// </code>
/// </summary>
public static class EmbeddedPackage
{
    private static readonly byte[] Magic = "PRTNPKG1"u8.ToArray();
    private const int FooterSize = 8 + 8;

    public const string ConfigurationEntry = "config.json";
    public const string AppFolder = "app";
    public const string DataFolder = "data";


    /// <summary>Ouvre l'archive embarquée, ou retourne null si l'exécutable n'en porte pas.</summary>
    /// <remarks>L'appelant est responsable de libérer l'archive retournée.</remarks>
    public static ZipArchive? TryOpen(string executablePath)
    {
        FileStream? file = null;
        try
        {
            file = File.OpenRead(executablePath);

            if (!TryReadFooter(file, out long start, out long length))
            {
                file.Dispose();
                return null;
            }

            // Une fenêtre sur le fichier plutôt qu'une copie en mémoire : l'archive
            // peut peser plusieurs mégaoctets, et seules les entrées réellement lues
            // seront décompressées.
            var window = new WindowStream(file, start, length);
            return new ZipArchive(window, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            file?.Dispose();
            return null;
        }
    }

    /// <summary>Configuration embarquée, ou null.</summary>
    public static AppConfiguration? ReadConfiguration(string executablePath)
    {
        using ZipArchive? archive = TryOpen(executablePath);
        ZipArchiveEntry? entry = archive?.GetEntry(ConfigurationEntry);

        if (entry is null)
            return null;

        try
        {
            using Stream content = entry.Open();
            return JsonSerializer.Deserialize<AppConfiguration>(content, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            // Une configuration illisible ne doit pas empêcher le démarrage : mieux
            // vaut l'identité du moteur qu'un refus de fonctionner (§54).
            return null;
        }
    }

    // --- Écriture --------------------------------------------------------------------

    /// <summary>Un dossier à embarquer.</summary>
    public sealed record FolderSource(string Name, string Path);

    /// <summary>
    /// Annexe une archive, en remplaçant celle qui s'y trouverait déjà.
    /// </summary>
    /// <remarks>
    /// Le retrait de l'archive héritée est indispensable pour que la génération soit
    /// récursive : sans lui, chaque génération empilerait celle de son parent
    /// (§38, CA-17).
    /// </remarks>
    public static byte[] Append(
        byte[] bytes, AppConfiguration configuration, IEnumerable<FolderSource> folders)
    {
        byte[] stripped = Strip(bytes);
        byte[] archive = BuildArchive(configuration, folders);

        byte[] result = new byte[stripped.Length + archive.Length + FooterSize];
        stripped.CopyTo(result, 0);
        archive.CopyTo(result, stripped.Length);

        int footer = stripped.Length + archive.Length;
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(footer), archive.Length);
        Magic.CopyTo(result, footer + 8);

        return result;
    }

    private static byte[] BuildArchive(
        AppConfiguration configuration, IEnumerable<FolderSource> folders)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry config = archive.CreateEntry(ConfigurationEntry, CompressionLevel.Optimal);
            using (Stream stream = config.Open())
            {
                JsonSerializer.Serialize(stream, configuration, JsonOptions);
            }

            foreach (FolderSource folder in folders)
                AddFolder(archive, folder);
        }

        return buffer.ToArray();
    }

    private static void AddFolder(ZipArchive archive, FolderSource folder)
    {
        if (!Directory.Exists(folder.Path))
            return;

        var root = new DirectoryInfo(folder.Path);

        foreach (FileInfo file in root.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root.FullName, file.FullName)
                .Replace(Path.DirectorySeparatorChar, '/');

            archive.CreateEntryFromFile(
                file.FullName, $"{folder.Name}/{relative}", CompressionLevel.Optimal);
        }
    }

    // --- Découpage, pour le patcheur de bundle -----------------------------------------

    /// <summary>Longueur du contenu utile, archive exclue.</summary>
    public static long PayloadLength(byte[] bytes)
    {
        if (bytes.Length < FooterSize) return bytes.Length;

        var footer = bytes.AsSpan(bytes.Length - FooterSize);
        if (!footer[8..].SequenceEqual(Magic)) return bytes.Length;

        long length = BinaryPrimitives.ReadInt64LittleEndian(footer);
        if (length <= 0 || bytes.Length < FooterSize + length) return bytes.Length;

        return bytes.Length - FooterSize - length;
    }

    /// <summary>Retire une éventuelle archive héritée.</summary>
    public static byte[] Strip(byte[] bytes)
    {
        long payload = PayloadLength(bytes);
        return payload == bytes.Length ? bytes : bytes[..(int)payload];
    }

    private static bool TryReadFooter(FileStream file, out long start, out long length)
    {
        start = 0;
        length = 0;

        if (file.Length < FooterSize)
            return false;

        Span<byte> footer = stackalloc byte[FooterSize];
        file.Seek(-FooterSize, SeekOrigin.End);
        file.ReadExactly(footer);

        if (!footer[8..].SequenceEqual(Magic))
            return false;

        length = BinaryPrimitives.ReadInt64LittleEndian(footer);

        if (length <= 0 || file.Length < FooterSize + length)
            return false;

        start = file.Length - FooterSize - length;
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Fenêtre en lecture seule sur une portion d'un flux.
    /// </summary>
    /// <remarks>
    /// <see cref="ZipArchive"/> exige un flux positionnable dont la fin coïncide avec
    /// celle de l'archive — il y cherche son catalogue. Une fenêtre évite de recopier
    /// l'archive en mémoire pour lui donner cette forme.
    /// </remarks>
    private sealed class WindowStream(Stream inner, long offset, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, length);
        }

        public override int Read(byte[] buffer, int start, int count)
        {
            long remaining = length - _position;
            if (remaining <= 0) return 0;

            inner.Position = offset + _position;
            int read = inner.Read(buffer, start, (int)Math.Min(count, remaining));
            _position += read;
            return read;
        }

        public override long Seek(long value, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => value,
                SeekOrigin.Current => _position + value,
                _ => length + value
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
