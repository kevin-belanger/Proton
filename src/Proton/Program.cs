using Proton.Bootstrap;
using Proton.Hosting;
using Proton.Infrastructure;
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
    ///
    /// Le serveur est donc démarré de façon bloquante avant que la main ne soit
    /// passée à la boucle de messages, et l'initialisation de la WebView est confiée
    /// à la fenêtre elle-même, une fois cette boucle en place.
    /// </summary>
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            ApplicationPaths paths = ApplicationPaths.ForCurrentProcess();
            Scaffolding.Ensure(paths);

            LocalWebHost host = LocalWebHost.StartAsync(paths).GetAwaiter().GetResult();

            try
            {
                using var window = new MainWindow(host.Address, title: "Proton");
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
