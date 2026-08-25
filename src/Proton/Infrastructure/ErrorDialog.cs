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
        Report("Proton could not start.", exception, $"""
             Proton could not start.

             {exception.Message}

             Technical detail:
             {exception}
             """);

    public static void ShowWebViewFailure(Exception exception) =>
        Report("Proton could not display its window.", exception, $"""
             Proton could not display its window.

             {exception.Message}

             This application requires Microsoft Edge WebView2, present on most
             recent Windows installations. It can be installed from Microsoft's
             website.
             """);

    private static void Report(string summary, Exception exception, string dialogMessage)
    {
        DiagnosticLog.Error(summary, exception);
        MessageBox.Show(dialogMessage, Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

}
