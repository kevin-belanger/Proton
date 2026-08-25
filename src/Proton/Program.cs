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
        InstallGlobalHandlers();

        return IsConfigMode(args) ? RunGenerator(args) : RunApplication();
    }

    /// <summary>
    /// Recueille les exceptions qui échapperaient à tout le reste (§54).
    /// </summary>
    /// <remarks>
    /// Sans cela, une exception survenue sur le thread de l'interface ferait
    /// disparaître la fenêtre en affichant la boîte de dialogue de Windows Forms,
    /// que l'utilisateur d'une application de bureau n'a aucune raison de voir.
    /// </remarks>
    private static void InstallGlobalHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) =>
        {
            DiagnosticLog.Error("Exception on the interface thread.", e.Exception);
            ErrorDialog.ShowStartupFailure(e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // Le processus s'arrête juste après : le journal est le seul témoin qui
            // subsistera.
            DiagnosticLog.Error("Unhandled exception.", e.ExceptionObject as Exception);
        };
    }

    /// <summary>
    /// <c>/config</c> est la syntaxe principale ; <c>--config</c> est accepté car
    /// certains interpréteurs réécrivent les arguments commençant par une barre
    /// oblique (§35).
    /// </summary>
    private static bool IsConfigMode(string[] args) =>
        args.Length > 0 && args[0] is "/config" or "--config";

    // --- Mode de personnalisation (§35) -------------------------------------------

    private static int RunGenerator(string[] args)
    {
        bool console = ConsoleAttachment.TryAttach();
        var log = new StringWriter();

        // « data » demande d'embarquer aussi le contenu initial de data et db (§39.1).
        bool embedUserFolders = args.Skip(1)
            .Any(a => a.TrimStart('/', '-').Equals("data", StringComparison.OrdinalIgnoreCase));

        GenerationResult result = ExecutableGenerator.Run(
            Environment.ProcessPath!, Directory.GetCurrentDirectory(), log, embedUserFolders);

        string report = log.ToString() + Environment.NewLine + result.Message;

        if (console)
        {
            Console.WriteLine();
            Console.WriteLine(report);
            return result.Success ? 0 : 2;
        }

        // Lancé par double-clic : sans console, la boîte de dialogue est le seul
        // canal disponible (§54).
        MessageBox.Show(report, "Proton — /config", MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

        return result.Success ? 0 : 2;
    }

    // --- Mode normal ---------------------------------------------------------------

    private static int RunApplication()
    {
        try
        {
            ApplicationPaths paths = ApplicationPaths.ForCurrentProcess();

            // Un exécutable personnalisé sert son application depuis son archive : il
            // ne faut alors créer aucun dossier `app` (§39.1).
            bool embedded = ArchiveFileProvider.TryLoad(
                Environment.ProcessPath ?? string.Empty, EmbeddedPackage.AppFolder) is not null;

            Scaffolding.Ensure(paths, embedded);

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
