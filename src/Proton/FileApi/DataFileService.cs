namespace Proton.FileApi;

/// <summary>Métadonnées d'une entrée de <c>data</c>.</summary>
public sealed record DataEntry(string Name, string Type, long? Size, DateTimeOffset? LastModified);

/// <summary>Ce qu'a produit une écriture (§19).</summary>
public enum WriteOutcome { Created, Replaced }

/// <summary>Nature d'une entrée existante.</summary>
public enum EntryKind { Missing, File, Directory }

/// <summary>
/// Opérations sur les fichiers de <c>data</c> (§13 à §22).
///
/// Cette classe ne connaît rien de HTTP : elle reçoit des chemins déjà validés par
/// <see cref="DataPath"/> et lève des exceptions d'entrée-sortie ordinaires. La
/// traduction en codes et en réponses appartient à la couche HTTP (§47).
/// </summary>
public sealed class DataFileService(DataPath paths)
{
    private readonly DataPath _paths = paths;

    public DataPath Paths => _paths;

    /// <summary>Nature de l'entrée située au chemin indiqué.</summary>
    public static EntryKind Inspect(string fullPath)
    {
        if (Directory.Exists(fullPath)) return EntryKind.Directory;
        if (File.Exists(fullPath)) return EntryKind.File;
        return EntryKind.Missing;
    }

    // --- Lecture --------------------------------------------------------------------

    /// <summary>Ouvre un fichier en lecture. Le flux est à libérer par l'appelant.</summary>
    /// <remarks>
    /// Le partage en lecture est autorisé : plusieurs lectures simultanées du même
    /// fichier sont un cas normal, et rien ne justifie de les sérialiser.
    /// </remarks>
    public static FileStream OpenRead(string fullPath) =>
        new(fullPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

    /// <summary>
    /// Validateur de cache d'un fichier (§15).
    /// </summary>
    /// <remarks>
    /// Dérivé de la taille et de la date de modification, il ne coûte aucune lecture
    /// du contenu. C'est un validateur faible : il ne sert pas de contrôle de
    /// concurrence, la V1 n'en fournissant pas (§16).
    /// </remarks>
    public static string ComputeETag(FileInfo file) =>
        $"W/\"{file.Length:x}-{file.LastWriteTimeUtc.Ticks:x}\"";

    // --- Écriture -------------------------------------------------------------------

    /// <summary>
    /// Écrit un fichier de manière atomique (§59).
    /// </summary>
    /// <remarks>
    /// Le contenu passe par un fichier temporaire du même dossier — donc du même
    /// volume, condition pour que le remplacement final soit atomique. Une
    /// interruption laisse l'ancien contenu intact plutôt qu'un fichier tronqué.
    /// </remarks>
    public static async Task<WriteOutcome> WriteAsync(
        string fullPath, Stream content, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        bool existed = File.Exists(fullPath);
        string temporary = Path.Combine(directory, $".proton-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var destination = new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            }))
            {
                await content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, fullPath, overwrite: true);
            return existed ? WriteOutcome.Replaced : WriteOutcome.Created;
        }
        catch
        {
            // Le temporaire ne doit jamais survivre à un échec : il encombrerait
            // `data` sans que l'application sache d'où il vient.
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception) { /* Rien à faire de plus : l'erreur d'origine prime. */ }
    }

    // --- Listing --------------------------------------------------------------------

    /// <summary>Contenu d'un dossier (§21).</summary>
    /// <remarks>
    /// Seules des métadonnées déjà connues du système de fichiers sont retournées :
    /// aucun contenu n'est lu, aucune empreinte n'est calculée.
    /// </remarks>
    public static IReadOnlyList<DataEntry> List(string fullPath)
    {
        var directory = new DirectoryInfo(fullPath);
        var entries = new List<DataEntry>();

        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
        {
            // Les temporaires d'écriture ne font pas partie des données de
            // l'application : les exposer ne ferait que semer la confusion.
            if (item.Name.StartsWith(".proton-", StringComparison.Ordinal)
                && item.Name.EndsWith(".tmp", StringComparison.Ordinal))
                continue;

            bool isDirectory = item is DirectoryInfo;

            entries.Add(new DataEntry(
                item.Name,
                isDirectory ? "directory" : "file",
                isDirectory ? null : ((FileInfo)item).Length,
                item.LastWriteTimeUtc));
        }

        // Dossiers d'abord, puis par nom : un ordre stable évite qu'une interface
        // réorganise sa liste à chaque rafraîchissement.
        return [.. entries
            .OrderByDescending(e => e.Type == "directory")
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    }

    // --- Suppression ----------------------------------------------------------------

    /// <summary>
    /// Supprime un dossier et tout son contenu, sans jamais descendre dans un lien.
    /// </summary>
    /// <remarks>
    /// Écrite à la main plutôt que confiée à <c>Directory.Delete(recursive)</c> :
    /// mise à l'épreuve sur un dossier contenant une jonction, celle-ci supprime les
    /// fichiers, retire le lien, puis échoue en laissant le dossier — un état partiel
    /// accompagné d'une erreur (§22.4).
    ///
    /// Un lien rencontré est retiré en tant que lien ; sa cible n'est pas touchée.
    /// Un utilisateur ayant redirigé un sous-dossier vers un autre disque ne s'attend
    /// pas à ce qu'une suppression emporte son contenu.
    /// </remarks>
    public static void DeleteDirectoryRecursive(string fullPath)
    {
        var directory = new DirectoryInfo(fullPath);

        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
        {
            if (IsLink(item))
            {
                DeleteLink(item);
                continue;
            }

            if (item is DirectoryInfo child)
                DeleteDirectoryRecursive(child.FullName);
            else
                item.Delete();
        }

        directory.Delete(recursive: false);
    }

    private static bool IsLink(FileSystemInfo item) =>
        (item.Attributes & FileAttributes.ReparsePoint) != 0;

    private static void DeleteLink(FileSystemInfo item)
    {
        // Un lien de dossier se retire par une suppression non récursive, qui ne
        // franchit pas le lien ; un lien de fichier se retire comme un fichier.
        if (item is DirectoryInfo link)
            link.Delete(recursive: false);
        else
            item.Delete();
    }

    /// <summary>Le dossier contient-il au moins une entrée ?</summary>
    public static bool IsEmpty(string fullPath) =>
        !Directory.EnumerateFileSystemEntries(fullPath).Any();
}
