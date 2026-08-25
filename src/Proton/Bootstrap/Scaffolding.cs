using System.IO.Compression;
using System.Text;

namespace Proton.Bootstrap;

/// <summary>
/// Création des dossiers requis au démarrage (§8).
///
/// Règle absolue : Proton ne remplace jamais un fichier existant de l'utilisateur.
/// Chaque opération ci-dessous ne crée que ce qui est absent.
/// </summary>
public static class Scaffolding
{
    /// <summary>Rapport de ce qui a réellement été créé, à des fins de diagnostic.</summary>
    public sealed record Result(
        bool CreatedApp, bool CreatedData, bool CreatedIndex, bool ExtractedData);

    /// <summary>
    /// Prépare les dossiers de l'application (§8, §39.1).
    ///
    /// Deux situations, selon que l'exécutable porte une application embarquée.
    ///
    /// <b>Exécutable personnalisé.</b> Son application est servie depuis l'archive :
    /// aucun dossier <c>app</c> n'est créé. Seul <c>data</c> l'est, avec ses deux
    /// sous-dossiers <c>files</c> et <c>db</c>. L'archive y dépose son contenu initial
    /// si elle en porte — mais uniquement lorsque <c>data</c> n'existe pas encore. Une
    /// extraction partielle mélangerait des données livrées et des données de
    /// l'utilisateur, ce qui ne se diagnostique plus.
    ///
    /// <b>Moteur générique.</b> Sans archive, <c>app</c> est créé sur le disque avec
    /// une page d'accueil : c'est le point de départ du développeur (§8).
    /// </summary>
    public static Result Ensure(ApplicationPaths paths, bool hasEmbeddedApp)
    {
        bool createdApp = false;
        bool createdIndex = false;

        if (!hasEmbeddedApp)
        {
            createdApp = CreateDirectoryIfMissing(paths.App);

            // L'index n'est engendré que si le dossier ne contient aucune page
            // d'accueil — y compris lorsqu'il existait déjà, mais vide.
            string index = Path.Combine(paths.App, "index.html");

            if (!File.Exists(index))
            {
                File.WriteAllText(index, ReadWelcomePage(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                createdIndex = true;
            }
        }

        bool extracted = TryExtractData(paths);

        bool createdData = CreateDirectoryIfMissing(paths.Data);
        CreateDirectoryIfMissing(paths.Files);
        CreateDirectoryIfMissing(paths.Db);

        return new Result(createdApp, createdData, createdIndex, extracted);
    }

    /// <summary>
    /// Dépose le contenu initial de <c>data</c>, si l'archive en porte.
    /// </summary>
    /// <remarks>
    /// Tout ou rien : dès que le dossier existe, rien n'est extrait. Ce que
    /// l'utilisateur a commencé lui appartient, et un mélange avec des données livrées
    /// produirait des situations inexplicables.
    /// </remarks>
    private static bool TryExtractData(ApplicationPaths paths)
    {
        if (Directory.Exists(paths.Data))
            return false;

        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
            return false;

        try
        {
            using var archive = Personalization.EmbeddedPackage.TryOpen(executable);
            if (archive is null)
                return false;

            bool extracted = false;

            foreach (var entry in archive.Entries)
            {
                string? destination = ResolveDestination(paths, entry.FullName);

                if (destination is null || entry.FullName.EndsWith('/'))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: false);
                extracted = true;
            }

            return extracted;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Infrastructure.DiagnosticLog.Error("Le contenu initial n'a pas pu être extrait.", ex);
            return false;
        }
    }

    /// <summary>
    /// Traduit une entrée d'archive en chemin sur le disque, ou null si elle ne
    /// concerne pas les dossiers de l'utilisateur.
    /// </summary>
    private static string? ResolveDestination(ApplicationPaths paths, string entryName)
    {
        string name = entryName.Replace('\\', '/');

        // Une entrée ne doit jamais désigner autre chose que `data` : le contenu de
        // l'archive provient d'une machine de développement, pas d'une source sûre.
        if (name.Contains("../", StringComparison.Ordinal))
            return null;

        string prefix = Personalization.EmbeddedPackage.DataFolder + "/";

        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(paths.Data, name[prefix.Length..].Replace('/', Path.DirectorySeparatorChar))
            : null;
    }

    private static bool CreateDirectoryIfMissing(string path)
    {
        if (Directory.Exists(path))
            return false;

        Directory.CreateDirectory(path);
        return true;
    }

    /// <summary>
    /// Page d'accueil du moteur générique : la documentation officielle elle-même,
    /// embarquée depuis <c>docs/index.html</c> au moment de la compilation.
    /// </summary>
    /// <remarks>
    /// Le fichier est autonome — un seul HTML, sa feuille de style à l'intérieur,
    /// aucune ressource distante — ce qui permet de le servir tel quel. Le dépôt n'en
    /// conserve donc qu'une seule copie : la documentation publiée et la page que
    /// Proton affiche sont littéralement le même fichier et ne peuvent pas diverger.
    ///
    /// Elle reste jetable : le développeur la remplace par sa propre application.
    /// </remarks>
    private static string ReadWelcomePage()
    {
        using Stream? stream = typeof(Scaffolding).Assembly
            .GetManifestResourceStream(WelcomeResource);

        if (stream is null)
            throw new InvalidOperationException(
                $"La ressource « {WelcomeResource} » manque à l'assembly.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private const string WelcomeResource = "Proton.Welcome.html";
}
