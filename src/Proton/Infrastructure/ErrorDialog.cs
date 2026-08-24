namespace Proton.Infrastructure;

/// <summary>
/// Présentation des erreurs critiques à l'utilisateur.
///
/// Une erreur critique ne doit jamais se traduire par la disparition silencieuse du
/// processus (§54). Faute de console dans une application de bureau, l'erreur est
/// portée sur deux canaux : une boîte de dialogue pour l'utilisateur, et un fichier
/// dans le profil de l'utilisateur pour le diagnostic.
///
/// Le fichier compte autant que la boîte de dialogue : celle-ci ne s'affiche pas
/// lorsque le processus est lancé sans session interactive.
/// </summary>
internal static class ErrorDialog
{
    private const string Caption = "Proton";

    public static void ShowStartupFailure(Exception exception) =>
        Report("Proton n'a pas pu démarrer.", exception, $"""
             Proton n'a pas pu démarrer.

             {exception.Message}

             Détail technique :
             {exception}
             """);

    public static void ShowWebViewFailure(Exception exception) =>
        Report("Proton n'a pas pu afficher sa fenêtre.", exception, $"""
             Proton n'a pas pu afficher sa fenêtre.

             {exception.Message}

             Cette application requiert Microsoft Edge WebView2, présent sur la
             plupart des installations récentes de Windows. Il peut être installé
             depuis le site de Microsoft.
             """);

    private static void Report(string summary, Exception exception, string dialogMessage)
    {
        WriteDiagnosticFile(summary, exception);
        MessageBox.Show(dialogMessage, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>
    /// Consigne l'erreur hors du dossier de l'application : les journaux ne doivent
    /// pas encombrer une application en fonctionnement normal (§56).
    /// </summary>
    private static void WriteDiagnosticFile(string summary, Exception exception)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolderOption.Create),
                "Proton",
                "logs");

            Directory.CreateDirectory(folder);

            File.AppendAllText(
                Path.Combine(folder, "startup-error.log"),
                $"""
                 ─────────────────────────────────────────────
                 {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}
                 {summary}
                 Exécutable : {Environment.ProcessPath}

                 {exception}


                 """);
        }
        catch (Exception)
        {
            // L'impossibilité d'écrire le diagnostic ne doit pas masquer l'erreur
            // d'origine, qui reste présentée dans la boîte de dialogue.
        }
    }
}
