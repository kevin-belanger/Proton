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
        Db = Path.Combine(root, "db");
        Config = Path.Combine(root, "config");
    }

    /// <summary>Chemin complet de l'exécutable en cours d'exécution.</summary>
    public string ExecutablePath { get; }

    /// <summary>Dossier contenant l'exécutable. Racine de l'application.</summary>
    public string Root { get; }

    /// <summary>Application Web servie à la racine du serveur HTTP (§7).</summary>
    public string App { get; }

    /// <summary>Espace de stockage accessible à l'application Web (§13).</summary>
    public string Data { get; }

    /// <summary>
    /// Bases SQLite (§26).
    /// </summary>
    /// <remarks>
    /// Séparé de <see cref="Data"/> et sans route qui l'expose : une base n'est
    /// joignable que par <c>/api/sqlite</c>. Placée dans <c>data</c>, elle serait
    /// aussi un fichier ordinaire — téléchargeable, et surtout écrasable par un
    /// <c>PUT</c> maladroit qui la détruirait.
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
                "L'emplacement de l'exécutable n'a pas pu être déterminé.");

        string? root = Path.GetDirectoryName(executablePath);

        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException(
                $"L'exécutable « {executablePath} » n'a pas de dossier parent.");

        return new ApplicationPaths(executablePath, root);
    }

    /// <summary>Résout les chemins pour un dossier donné. Réservé aux tests.</summary>
    internal static ApplicationPaths ForRoot(string root)
    {
        string full = Path.GetFullPath(root);
        return new ApplicationPaths(Path.Combine(full, "Proton.exe"), full);
    }
}
