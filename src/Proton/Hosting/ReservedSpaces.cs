namespace Proton.Hosting;

/// <summary>
/// Espaces d'URL réservés aux API de Proton (§49).
///
/// Ils ont priorité sur les fichiers statiques de <c>app</c> : un fichier
/// <c>app/data/x.html</c> ne doit pas pouvoir prendre le contrôle de la route
/// <c>/data/x.html</c>.
///
/// Cette liste est partagée par le serveur, qui y route les API, et par la fenêtre,
/// qui refuse d'y naviguer. La dupliquer serait la garantie qu'elle diverge.
/// </summary>
public static class ReservedSpaces
{
    public static readonly string[] Prefixes = ["/data", "/api"];

    /// <summary>
    /// Indique si un chemin d'URL appartient à un espace réservé.
    /// </summary>
    /// <remarks>
    /// La comparaison se fait par segment : <c>/data</c> et <c>/data/x</c> sont
    /// réservés, mais pas <c>/database.html</c>, qui appartient à l'application.
    /// </remarks>
    public static bool Contains(string path)
    {
        foreach (string prefix in Prefixes)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (path.Length == prefix.Length || path[prefix.Length] == '/')
                return true;
        }

        return false;
    }
}
