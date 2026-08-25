using System.IO.Compression;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Proton.Personalization;

namespace Proton.Hosting;

/// <summary>
/// Sert les fichiers d'une application embarquée dans l'exécutable (§39.1).
///
/// Le dossier <c>app</c> n'est jamais extrait sur le disque. C'est ce qui garantit
/// qu'une application ne peut pas se désynchroniser de son moteur : livrer une
/// nouvelle version de l'exécutable livre nécessairement l'interface qui va avec.
///
/// Le contenu est chargé en mémoire au démarrage plutôt que lu à la demande dans
/// l'archive : <see cref="ZipArchive"/> ne supporte pas les lectures concurrentes, et
/// une application Web pèse quelques centaines de kilooctets — le jeu n'en vaudrait
/// pas la chandelle.
/// </summary>
public sealed class ArchiveFileProvider : IFileProvider
{
    private readonly Dictionary<string, byte[]> _files;
    private readonly DateTimeOffset _lastModified;

    private ArchiveFileProvider(Dictionary<string, byte[]> files, DateTimeOffset lastModified)
    {
        _files = files;
        _lastModified = lastModified;
    }

    /// <summary>Nombre de fichiers servis.</summary>
    public int Count => _files.Count;

    /// <summary>
    /// Charge le contenu d'un dossier de l'archive, ou retourne null s'il est absent.
    /// </summary>
    public static ArchiveFileProvider? TryLoad(string executablePath, string folder)
    {
        using ZipArchive? archive = EmbeddedPackage.TryOpen(executablePath);

        if (archive is null)
            return null;

        string prefix = folder + "/";
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset modified = DateTimeOffset.MinValue;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Les entrées de dossier n'ont pas de contenu.
            if (entry.FullName.EndsWith('/'))
                continue;

            using Stream content = entry.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);

            files["/" + entry.FullName[prefix.Length..]] = buffer.ToArray();

            if (entry.LastWriteTime > modified)
                modified = entry.LastWriteTime;
        }

        return files.Count == 0 ? null : new ArchiveFileProvider(files, modified);
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        string key = Normalise(subpath);

        return _files.TryGetValue(key, out byte[]? content)
            ? new ArchiveFile(Path.GetFileName(key), content, _lastModified)
            : new NotFoundFileInfo(key);
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        string prefix = Normalise(subpath);
        if (!prefix.EndsWith('/')) prefix += "/";

        var entries = _files
            .Where(f => f.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(f => (IFileInfo)new ArchiveFile(
                Path.GetFileName(f.Key), f.Value, _lastModified))
            .ToList();

        return entries.Count == 0
            ? NotFoundDirectoryContents.Singleton
            : new ArchiveDirectory(entries);
    }

    /// <summary>
    /// Le contenu embarqué ne change jamais pendant la vie du processus : aucun
    /// mécanisme de surveillance n'a lieu d'être.
    /// </summary>
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private static string Normalise(string subpath)
    {
        subpath = subpath.Replace('\\', '/');
        return subpath.StartsWith('/') ? subpath : "/" + subpath;
    }

    private sealed class ArchiveFile(string name, byte[] content, DateTimeOffset modified) : IFileInfo
    {
        public bool Exists => true;
        public bool IsDirectory => false;
        public long Length => content.Length;
        public string Name => name;
        public string? PhysicalPath => null;
        public DateTimeOffset LastModified => modified;

        public Stream CreateReadStream() => new MemoryStream(content, writable: false);
    }

    private sealed class ArchiveDirectory(IReadOnlyList<IFileInfo> entries) : IDirectoryContents
    {
        public bool Exists => true;
        public IEnumerator<IFileInfo> GetEnumerator() => entries.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
