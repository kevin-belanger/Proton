namespace Proton.Bootstrap;

/// <summary>
/// Emplacements des dossiers significatifs d'une application Proton.
///
/// Tous sont résolus à partir de l'emplacement réel de l'exécutable et jamais du
/// répertoire de travail du processus (§3.3) : une application complète doit pouvoir
/// être déplacée d'un dossier à l'autre, et démarrée depuis n'importe où, sans
/// modification.
/// </summary>
public sealed class ApplicationPaths
{
    private ApplicationPaths(string executablePath, string root)
    {
        ExecutablePath = executablePath;
        Root = root;
        App = Path.Combine(root, "app");
        Data = Path.Combine(root, "data");
        Files = Path.Combine(Data, "files");
        Db = Path.Combine(Data, "db");
        Config = Path.Combine(root, "config");
    }

    /// <summary>Chemin complet de l'exécutable en cours d'exécution.</summary>
    public string ExecutablePath { get; }

    /// <summary>Dossier contenant l'exécutable. Racine de l'application.</summary>
    public string Root { get; }

    /// <summary>Application Web servie à la racine du serveur HTTP (§7).</summary>
    public string App { get; }

    /// <summary>
    /// Racine des données de l'application (§6).
    /// </summary>
    /// <remarks>
    /// Elle n'est jamais exposée telle quelle : seuls ses deux sous-dossiers le sont,
    /// et chacun par sa propre voie. Les regrouper laisse un seul dossier à côté de
    /// l'exécutable, et fait de sa copie une sauvegarde complète.
    /// </remarks>
    public string Data { get; }

    /// <summary>Fichiers de l'application, exposés par <c>/files</c> (§13).</summary>
    public string Files { get; }

    /// <summary>
    /// Bases SQLite, exposées par <c>/api/sqlite</c> (§26).
    /// </summary>
    /// <remarks>
    /// Séparées des fichiers et sans route qui les expose : une base n'est joignable
    /// que par l'API SQLite. Rangée parmi les fichiers, elle serait aussi un fichier
    /// ordinaire — téléchargeable, et surtout écrasable par un <c>PUT</c> maladroit
    /// qui la détruirait.
    /// </remarks>
    public string Db { get; }

    /// <summary>Outil de personnalisation. N'est jamais créé automatiquement (§8).</summary>
    public string Config { get; }

    /// <summary>
    /// Résout les chemins pour le processus courant.
    /// </summary>
    public static ApplicationPaths ForCurrentProcess()
    {
        // Environment.ProcessPath et non Assembly.Location : cette dernière retourne
        // une chaîne vide dans un exécutable single-file.
        string? executablePath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executablePath))
            throw new InvalidOperationException(
                "The location of the executable could not be determined.");

        string? root = Path.GetDirectoryName(executablePath);

        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException(
                $"The executable \"{executablePath}\" has no parent folder.");

        return new ApplicationPaths(executablePath, root);
    }

    /// <summary>Résout les chemins pour un dossier donné. Réservé aux tests.</summary>
    internal static ApplicationPaths ForRoot(string root)
    {
        string full = Path.GetFullPath(root);
        return new ApplicationPaths(Path.Combine(full, "Proton.exe"), full);
    }
}
