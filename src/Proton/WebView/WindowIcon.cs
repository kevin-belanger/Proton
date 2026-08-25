using Proton.Infrastructure;

namespace Proton.WebView;

/// <summary>
/// Icône que porte l'exécutable, pour la fenêtre (§41).
///
/// Windows Forms ne la reprend pas de lui-même : une fenêtre affiche l'icône par
/// défaut du framework tant qu'aucune autre ne lui est assignée. L'icône posée par le
/// mode <c>/generate</c> serait alors visible dans l'Explorateur, mais ni dans la barre
/// de titre ni dans la barre des tâches.
///
/// Elle doit être fournie par la propriété <c>Icon</c> de la fenêtre, et non par un
/// message envoyé à son handle : Windows Forms applique la sienne après la création
/// du handle, et écraserait le message.
/// </summary>
public static class WindowIcon
{
    /// <summary>
    /// Icône de l'exécutable en cours, ou <c>null</c> s'il n'en porte pas.
    /// </summary>
    /// <remarks>
    /// L'absence d'icône ne justifie pas d'interrompre le démarrage : la fenêtre
    /// s'affichera avec celle du framework.
    /// </remarks>
    public static Icon? Load()
    {
        string? executable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executable))
            return null;

        try
        {
            return Icon.ExtractAssociatedIcon(executable);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Error("The executable icon could not be read.", ex);
            return null;
        }
    }
}
