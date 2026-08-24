using Proton.Bootstrap;
using Proton.Configuration;
using Proton.Hosting;
using Proton.Infrastructure;
using Proton.Personalization;
using Proton.WebView;

namespace Proton;

internal static class Program
{
    /// <summary>
    /// Point d'entrée.
    ///
    /// La méthode est volontairement synchrone. Windows Forms exige que sa boucle de
    /// messages s'exécute sur le thread STA principal ; placer <c>Application.Run</c>
    /// dans une méthode <c>async</c> l'exposerait à reprendre sur un thread du pool
    /// après le premier <c>await</c>, ce qui est invalide.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        return IsConfigMode(args) ? RunGenerator() : RunApplication();
    }

    /// <summary>
    /// <c>/config</c> est la syntaxe principale ; <c>--config</c> est accepté car
    /// certains interpréteurs réécrivent les arguments commençant par une barre
    /// oblique (§35).
    /// </summary>
    private static bool IsConfigMode(string[] args) =>
        args.Length > 0 && args[0] is "/config" or "--config";

    // --- Mode de personnalisation (§35) -------------------------------------------

    private static int RunGenerator()
    {
        bool console = ConsoleAttachment.TryAttach();
        var log = new StringWriter();

        GenerationResult result = ExecutableGenerator.Run(
            Environment.ProcessPath!, Directory.GetCurrentDirectory(), log);

        string report = log.ToString() + Environment.NewLine + result.Message;

        if (console)
        {
            Console.WriteLine();
            Console.WriteLine(report);
            return result.Success ? 0 : 2;
        }

        // Lancé par double-clic : sans console, la boîte de dialogue est le seul
        // canal disponible (§54).
        MessageBox.Show(report, "Proton — mode /config", MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

        return result.Success ? 0 : 2;
    }

    // --- Mode normal ---------------------------------------------------------------

    private static int RunApplication()
    {
        try
        {
            ApplicationPaths paths = ApplicationPaths.ForCurrentProcess();
            Scaffolding.Ensure(paths);

            AppConfiguration configuration = AppConfiguration.Load();
            LocalWebHost host = LocalWebHost.StartAsync(paths).GetAwaiter().GetResult();

            try
            {
                using var window = new MainWindow(host.Address, configuration);
                Application.Run(window);
                return window.ExitCode;
            }
            finally
            {
                // La WebView a été libérée à la fermeture de la fenêtre ; le serveur
                // s'arrête ensuite et le port est rendu au système (§12, CA-13).
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            ErrorDialog.ShowStartupFailure(ex);
            return 1;
        }
    }
}
