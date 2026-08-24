using System.Globalization;

namespace Proton.Infrastructure;

/// <summary>
/// Journal de diagnostic (§56).
///
/// La V1 n'a pas besoin d'un système de journalisation complet, mais doit pouvoir
/// expliquer ce qui s'est passé : démarrage, port retenu, arrêt, et les erreurs
/// graves des différentes couches.
///
/// Le journal vit dans le profil de l'utilisateur et non à côté de l'exécutable :
/// une application en fonctionnement normal ne doit rien accumuler dans son propre
/// dossier, qui se distribue par copie (§2).
///
/// Seuls les événements notables y figurent — jamais une ligne par requête, qui
/// ferait grossir le fichier sans rien apprendre.
/// </summary>
public static class DiagnosticLog
{
    private static readonly Lock Gate = new();
    private const long MaxSize = 1024 * 1024;

    /// <summary>
    /// UTF-8 avec marque d'ordre des octets.
    /// </summary>
    /// <remarks>
    /// Sans elle, les outils de Windows — l'Éditeur de texte, PowerShell — lisent le
    /// fichier dans la page de codes ANSI et affichent « dÃ©marrÃ© ». Un journal
    /// illisible ne rend aucun service.
    /// </remarks>
    private static readonly System.Text.UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private static string? _path;

    /// <summary>Emplacement du journal, ou null s'il n'a pas pu être ouvert.</summary>
    public static string? Path => _path ??= Resolve();

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERREUR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        string? path = Path;
        if (path is null) return;

        string line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");

        try
        {
            lock (Gate)
            {
                Rotate(path);
                File.AppendAllText(path, line, Utf8WithBom);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un journal indisponible ne doit jamais empêcher l'application de
            // fonctionner : c'est un outil de diagnostic, pas une dépendance.
        }
    }

    /// <summary>
    /// Conserve une génération précédente lorsque le journal devient volumineux.
    /// </summary>
    /// <remarks>
    /// Une rotation à deux fichiers suffit : l'intérêt d'un journal de diagnostic
    /// est de couvrir la dernière session, pas l'historique complet.
    /// </remarks>
    private static void Rotate(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < MaxSize) return;

        string previous = path + ".1";
        try
        {
            File.Move(path, previous, overwrite: true);
        }
        catch (IOException)
        {
            // Le fichier est peut-être ouvert ailleurs ; il grossira un peu plus.
        }
    }

    private static string? Resolve()
    {
        try
        {
            string folder = System.IO.Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolderOption.Create),
                "Proton",
                "logs");

            Directory.CreateDirectory(folder);
            return System.IO.Path.Combine(folder, "proton.log");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
