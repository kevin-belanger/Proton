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
        bool CreatedApp, bool CreatedData, bool CreatedDb, bool CreatedIndex, bool ExtractedFolders);

    /// <summary>
    /// Prépare les dossiers de l'application (§8, §39.1).
    ///
    /// Deux situations, selon que l'exécutable porte une application embarquée.
    ///
    /// <b>Exécutable personnalisé.</b> Son application est servie depuis l'archive :
    /// aucun dossier <c>app</c> n'est créé. Seuls <c>data</c> et <c>db</c> le sont, et
    /// l'archive y dépose son contenu initial si elle en porte — mais uniquement
    /// lorsque <b>aucun des deux</b> n'existe déjà. Une extraction partielle
    /// mélangerait des données livrées et des données de l'utilisateur, ce qui ne se
    /// diagnostique plus.
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
                File.WriteAllText(index, WelcomePage, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                createdIndex = true;
            }
        }

        bool extracted = TryExtractUserFolders(paths);

        bool createdData = CreateDirectoryIfMissing(paths.Data);
        bool createdDb = CreateDirectoryIfMissing(paths.Db);

        return new Result(createdApp, createdData, createdDb, createdIndex, extracted);
    }

    /// <summary>
    /// Dépose le contenu initial de <c>data</c> et <c>db</c>, si l'archive en porte.
    /// </summary>
    /// <remarks>
    /// Tout ou rien : dès qu'un seul des deux dossiers existe, rien n'est extrait. Ce
    /// que l'utilisateur a commencé lui appartient, et un mélange avec des données
    /// livrées produirait des situations inexplicables.
    /// </remarks>
    private static bool TryExtractUserFolders(ApplicationPaths paths)
    {
        if (Directory.Exists(paths.Data) || Directory.Exists(paths.Db))
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

        // Une entrée ne doit jamais désigner autre chose que data ou db : le contenu
        // de l'archive provient d'une machine de développement, pas d'une source sûre.
        if (name.Contains("../", StringComparison.Ordinal))
            return null;

        string prefix = Personalization.EmbeddedPackage.DataFolder + "/";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(paths.Data, name[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));

        prefix = Personalization.EmbeddedPackage.DbFolder + "/";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(paths.Db, name[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));

        return null;
    }

    private static bool CreateDirectoryIfMissing(string path)
    {
        if (Directory.Exists(path))
            return false;

        Directory.CreateDirectory(path);
        return true;
    }

    /// <summary>
    /// Page d'accueil engendrée au premier démarrage.
    ///
    /// Elle tient en un seul fichier, sans dépendance, et reste jetable : le
    /// développeur la remplace par sa propre application. Elle interroge les API de
    /// Proton pour montrer d'emblée ce qu'une page Web ordinaire ne peut pas faire,
    /// et se contente d'annoncer « à venir » pour celles qui ne répondent pas encore.
    /// </summary>
    private const string WelcomePage = """
        <!doctype html>
        <html lang="fr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Proton</title>
        <style>
            :root {
                color-scheme: light dark;
                --fond: #fbfbfd;
                --carte: #ffffff;
                --texte: #1c1c1e;
                --discret: #6b6b70;
                --trait: #e4e4e8;
                --accent: #3b6ef0;
                --ok: #21a45d;
                --attente: #b0b0b6;
            }
            @media (prefers-color-scheme: dark) {
                :root {
                    --fond: #1c1c1e;
                    --carte: #252528;
                    --texte: #f5f5f7;
                    --discret: #9a9aa0;
                    --trait: #38383c;
                    --accent: #6f9bff;
                    --ok: #43c97f;
                    --attente: #5c5c62;
                }
            }
            * { box-sizing: border-box; }
            body {
                margin: 0;
                padding: 3rem 1.5rem;
                background: var(--fond);
                color: var(--texte);
                font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
                font-size: 15px;
                line-height: 1.6;
                display: flex;
                justify-content: center;
            }
            main { width: 100%; max-width: 40rem; }
            h1 {
                margin: 0;
                font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif;
                font-size: 2.25rem;
                font-weight: 600;
                letter-spacing: -0.02em;
            }
            .version { margin: 0.25rem 0 2rem; color: var(--discret); font-size: 0.9rem; }
            p { margin: 0 0 1rem; }
            .discret { color: var(--discret); }
            code {
                font-family: "Cascadia Code", Consolas, monospace;
                font-size: 0.85em;
                background: var(--carte);
                border: 1px solid var(--trait);
                border-radius: 4px;
                padding: 0.1em 0.4em;
            }
            h2 {
                margin: 2.5rem 0 0.75rem;
                font-size: 0.8rem;
                font-weight: 600;
                text-transform: uppercase;
                letter-spacing: 0.06em;
                color: var(--discret);
            }
            ul { list-style: none; margin: 0; padding: 0; }
            li {
                display: flex;
                align-items: baseline;
                gap: 0.75rem;
                padding: 0.7rem 0;
                border-bottom: 1px solid var(--trait);
            }
            li:last-child { border-bottom: none; }
            .pastille {
                flex: none;
                width: 8px;
                height: 8px;
                border-radius: 50%;
                background: var(--attente);
                transform: translateY(-1px);
            }
            .pastille.ok { background: var(--ok); }
            .nom { flex: 1; }
            .nom code { background: none; border: none; padding: 0; color: var(--discret); }
            .etat { color: var(--discret); font-size: 0.85rem; }
        </style>
        </head>
        <body>
        <main>
            <h1 id="titre">Proton</h1>
            <p class="version" id="version">moteur générique</p>

            <p>
                Cette fenêtre est une application de bureau Windows dont toute
                l'interface est écrite en HTML, CSS et JavaScript. Il n'y a ni
                serveur à installer, ni runtime à déployer : l'exécutable contient
                tout, et sert cette page depuis un serveur local qu'il démarre
                lui-même.
            </p>
            <p>
                Contrairement à une page Web ordinaire, une application Proton peut
                lire et écrire des fichiers sur le disque et utiliser des bases de
                données SQLite locales.
            </p>
            <p class="discret">
                Pour commencer, remplacez le contenu du dossier <code>app</code> par
                le vôtre. Vos fichiers vont dans <code>data</code>, vos bases de
                données dans <code>db</code>.
            </p>

            <h2>Services</h2>
            <ul id="services">
                <li>
                    <span class="pastille ok"></span>
                    <span class="nom">Application servie depuis <code>app/</code></span>
                    <span class="etat">actif</span>
                </li>
            </ul>
        </main>

        <script>
        const services = [
            { url: '/api/app',            nom: 'Configuration',   chemin: '/api/app' },
            { url: '/data/',              nom: 'Fichiers',        chemin: '/data' },
            { url: '/api/sqlite',         nom: 'Bases SQLite',    chemin: '/api/sqlite' }
        ];

        const liste = document.getElementById('services');

        for (const service of services) {
            const ligne = document.createElement('li');
            ligne.innerHTML =
                '<span class="pastille"></span>' +
                '<span class="nom">' + service.nom +
                ' <code>' + service.chemin + '</code></span>' +
                '<span class="etat">vérification…</span>';
            liste.appendChild(ligne);
            verifier(service, ligne);
        }

        async function verifier(service, ligne) {
            const pastille = ligne.querySelector('.pastille');
            const etat = ligne.querySelector('.etat');
            try {
                const reponse = await fetch(service.url);
                if (reponse.status === 501) {
                    etat.textContent = 'à venir';
                } else if (reponse.ok) {
                    pastille.classList.add('ok');
                    etat.textContent = 'actif';
                } else {
                    etat.textContent = 'erreur ' + reponse.status;
                }
            } catch {
                etat.textContent = 'injoignable';
            }
        }

        // La configuration embarquée dans l'exécutable donne son identité à
        // l'application : c'est elle, et non cette page, qui porte le nom et la
        // version affichés ci-dessus.
        fetch('/api/app')
            .then(reponse => reponse.ok ? reponse.json() : null)
            .then(app => {
                if (!app) return;
                document.title = app.name;
                document.getElementById('titre').textContent = app.name;
                document.getElementById('version').textContent =
                    app.version ? 'version ' + app.version : '';
            })
            .catch(() => {});
        </script>
        </body>
        </html>

        """;
}
