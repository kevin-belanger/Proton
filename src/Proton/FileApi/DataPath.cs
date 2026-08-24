namespace Proton.FileApi;

/// <summary>Motif de rejet d'un chemin.</summary>
public enum PathRejection
{
    None,
    /// Le chemin contient une séquence de remontée.
    Traversal,
    /// Le chemin est absolu, ou désigne un lecteur ou un partage réseau.
    Absolute,
    /// Le chemin contient un caractère que le système de fichiers n'accepte pas.
    InvalidCharacter,
    /// Le chemin normalisé sort du dossier `data`.
    OutsideRoot,
    /// Le chemin traverse un lien qui sort du dossier `data`.
    LinkEscape
}

/// <summary>Chemin validé, ou motif du rejet.</summary>
public readonly record struct DataPathResult
{
    public required bool IsValid { get; init; }
    public required PathRejection Rejection { get; init; }
    /// Chemin physique complet. Renseigné uniquement si <see cref="IsValid"/>.
    public required string FullPath { get; init; }
    /// Chemin normalisé relatif à `data`, avec des barres obliques.
    public required string RelativePath { get; init; }
    /// Le chemin demandé se terminait par une barre oblique : il désigne un dossier (§22.1).
    public required bool IsDirectoryRequest { get; init; }

    internal static DataPathResult Rejected(PathRejection rejection) => new()
    {
        IsValid = false,
        Rejection = rejection,
        FullPath = string.Empty,
        RelativePath = string.Empty,
        IsDirectoryRequest = false
    };
}

/// <summary>
/// Confinement des chemins de l'API de fichiers (§14).
///
/// Toute opération de <c>/data</c> doit rester à l'intérieur du dossier physique
/// <c>data</c>. C'est la seule barrière entre une application Web et le reste du
/// disque : une faiblesse ici annule tout le reste.
///
/// La validation procède en trois temps, chacun suffisant à lui seul dans la plupart
/// des cas, mais aucun ne l'étant dans tous :
///
///   1. rejet syntaxique des remontées, chemins absolus et caractères interdits;
///   2. normalisation, puis vérification que le résultat appartient bien à `data`;
///   3. vérification qu'aucun composant existant n'est un lien menant hors de `data`.
///
/// La troisième étape est indispensable : un lien placé dans `data` produirait un
/// chemin parfaitement conforme aux deux premières, tout en donnant accès à un autre
/// endroit du disque.
/// </summary>
public sealed class DataPath
{
    private readonly string _root;

    /// <param name="dataRoot">Dossier physique <c>data</c>.</param>
    public DataPath(string dataRoot)
    {
        // La racine est canonisée une fois pour toutes : les comparaisons qui
        // suivent n'ont de sens que sur une forme stable.
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
    }

    /// <summary>Dossier physique <c>data</c>, sous forme canonique.</summary>
    public string Root => _root;

    /// <summary>
    /// Valide un chemin issu d'une URL et le traduit en chemin physique.
    /// </summary>
    /// <param name="requestPath">
    /// Portion de l'URL qui suit <c>/data</c>, par exemple <c>/notes/liste.txt</c>.
    /// </param>
    public DataPathResult Resolve(string requestPath)
    {
        string path = requestPath ?? string.Empty;

        bool directoryRequest = path.Length == 0 || path.EndsWith('/');

        // --- 1. Rejet syntaxique -----------------------------------------------

        if (path.Contains('\0'))
            return DataPathResult.Rejected(PathRejection.InvalidCharacter);

        // Les barres obliques inverses n'ont aucune raison d'apparaître dans une URL,
        // et Windows les traiterait comme des séparateurs : les accepter reviendrait
        // à offrir une seconde syntaxe de remontée.
        if (path.Contains('\\'))
            return DataPathResult.Rejected(PathRejection.Absolute);

        // « C:/... » ou « //serveur/partage ».
        if (path.Contains(':'))
            return DataPathResult.Rejected(PathRejection.Absolute);

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            if (segment == "..")
                return DataPathResult.Rejected(PathRejection.Traversal);

            if (segment == ".")
                continue;

            if (segment.AsSpan().IndexOfAny(InvalidNameCharacters) >= 0)
                return DataPathResult.Rejected(PathRejection.InvalidCharacter);
        }

        string relative = string.Join('/', segments.Where(s => s != "."));

        // --- 2. Normalisation et confinement -----------------------------------

        string candidate;
        try
        {
            candidate = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar))));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return DataPathResult.Rejected(PathRejection.InvalidCharacter);
        }

        if (!IsInsideRoot(candidate))
            return DataPathResult.Rejected(PathRejection.OutsideRoot);

        // --- 3. Aucun lien ne doit mener hors de `data` -------------------------

        if (EscapesThroughLink(candidate))
            return DataPathResult.Rejected(PathRejection.LinkEscape);

        return new DataPathResult
        {
            IsValid = true,
            Rejection = PathRejection.None,
            FullPath = candidate,
            RelativePath = relative,
            IsDirectoryRequest = directoryRequest
        };
    }

    /// <summary>Le chemin est-il la racine elle-même, ou situé dessous ?</summary>
    private bool IsInsideRoot(string fullPath)
    {
        if (string.Equals(fullPath, _root, StringComparison.OrdinalIgnoreCase))
            return true;

        // Le séparateur est déterminant : « data-public » ne doit pas passer pour
        // un descendant de « data ».
        return fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Remonte chaque composant du chemin, de la racine vers la cible, et vérifie
    /// qu'aucun n'est un lien menant hors de <c>data</c>.
    /// </summary>
    /// <remarks>
    /// Seuls les composants existants sont examinés : ceux qui restent à créer ne
    /// peuvent rien détourner.
    /// </remarks>
    private bool EscapesThroughLink(string fullPath)
    {
        string current = fullPath;

        while (current.Length > _root.Length)
        {
            FileSystemInfo? info = ResolveExistingLink(current);

            if (info is not null && !IsInsideRoot(Path.TrimEndingDirectorySeparator(info.FullName)))
                return true;

            string? parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
                break;

            current = Path.TrimEndingDirectorySeparator(parent);
        }

        return false;
    }

    /// <summary>Cible finale d'un composant s'il s'agit d'un lien, sinon null.</summary>
    private static FileSystemInfo? ResolveExistingLink(string path)
    {
        try
        {
            // Le type d'entrée n'est pas connu d'avance ; l'un des deux appels
            // renseigne la cible, l'autre retourne null.
            return File.ResolveLinkTarget(path, returnFinalTarget: true)
                ?? Directory.ResolveLinkTarget(path, returnFinalTarget: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un lien dont la cible est inaccessible ne peut pas servir à sortir :
            // l'accès échouera de toute façon.
            return null;
        }
    }

    /// Caractères refusés par le système de fichiers, complétés des séparateurs :
    /// ceux-ci ne peuvent apparaître qu'entre les segments, jamais à l'intérieur.
    private static readonly char[] InvalidNameCharacters =
        [.. Path.GetInvalidFileNameChars(), '/', '\\'];
}
