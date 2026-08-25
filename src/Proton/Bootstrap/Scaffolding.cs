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
                File.WriteAllText(index, WelcomePage, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
            Infrastructure.DiagnosticLog.Error("The initial content could not be extracted.", ex);
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
    /// Page d'accueil engendrée au premier démarrage.
    ///
    /// Elle tient en un seul fichier, sans ressource liée, et reste jetable : le
    /// développeur la remplace par sa propre application. Elle interroge les API de
    /// Proton pour montrer d'emblée ce qu'une page Web ordinaire ne peut pas faire.
    /// </summary>
    /// <remarks>
    /// Elle ne cherche pas à documenter Proton : elle oriente vers la documentation
    /// publiée (§8.1). Un point de départ et un manuel n'ont pas le même travail à
    /// faire, et le second ne tiendrait pas dans le premier sans le noyer.
    ///
    /// Le lien sortant ne l'empêche pas de s'afficher sans réseau : rien n'est
    /// chargé depuis l'extérieur, et un clic part vers le navigateur du système
    /// (§51.1) plutôt que de remplacer la fenêtre.
    /// </remarks>
    private const string WelcomePage = """
        <!doctype html>
        <html lang="en">
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
                font-size: 0.9em;
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
            .manuel {
                display: block;
                margin-top: 2.5rem;
                padding: 1.1rem 1.25rem;
                background: var(--carte);
                border: 1px solid var(--trait);
                border-radius: 10px;
                text-decoration: none;
                color: inherit;
            }
            .manuel:hover { border-color: var(--accent); }
            .manuel strong { display: block; color: var(--accent); font-weight: 600; }
            .manuel span { color: var(--discret); font-size: 0.9rem; }
        </style>
        </head>
        <body>
        <main>
            <h1 id="titre">Proton</h1>
            <p class="version" id="version">generic engine</p>

            <p>
                This window is a Windows desktop application whose entire interface is
                written in HTML, CSS and JavaScript. There is no server to install and
                no runtime to deploy: the executable holds everything, and serves this
                page from a local server it starts itself.
            </p>
            <p>
                Unlike an ordinary web page, a Proton application can read and write
                files on disk and use local SQLite databases.
            </p>
            <p class="discret">
                To begin, replace the contents of the <code>app</code> folder with your
                own. Your files go in <code>data/files</code>, your databases in
                <code>data/db</code>.
            </p>

            <h2>Services</h2>
            <ul id="services">
                <li>
                    <span class="pastille ok"></span>
                    <span class="nom">Application served from <code>app/</code></span>
                    <span class="etat">active</span>
                </li>
            </ul>

            <a class="manuel" href="https://kevin-belanger.github.io/Proton/">
                <strong>Read the documentation &rarr;</strong>
                <span>The APIs in full, and how to turn this into an executable of your own.</span>
            </a>
        </main>

        <script>
        const services = [
            { url: '/api/app',    nom: 'Configuration',    chemin: '/api/app' },
            { url: '/files/',     nom: 'Files',            chemin: '/files' },
            { url: '/api/sqlite', nom: 'SQLite databases', chemin: '/api/sqlite' }
        ];

        const liste = document.getElementById('services');

        for (const service of services) {
            const ligne = document.createElement('li');
            ligne.innerHTML =
                '<span class="pastille"></span>' +
                '<span class="nom">' + service.nom +
                ' <code>' + service.chemin + '</code></span>' +
                '<span class="etat">checking…</span>';
            liste.appendChild(ligne);
            verifier(service, ligne);
        }

        // Une sonde en GET ne convient pas à toutes les routes : `/api/sqlite`
        // n'accepte que POST et répond 405. C'est une réponse du service, donc la
        // preuve qu'il est là — la traiter en erreur ferait passer une route saine
        // pour cassée.
        async function verifier(service, ligne) {
            const pastille = ligne.querySelector('.pastille');
            const etat = ligne.querySelector('.etat');
            try {
                const reponse = await fetch(service.url);
                if (reponse.status === 501) {
                    etat.textContent = 'not yet available';
                } else if (reponse.ok || reponse.status === 405) {
                    pastille.classList.add('ok');
                    etat.textContent = 'active';
                } else {
                    etat.textContent = 'error ' + reponse.status;
                }
            } catch {
                etat.textContent = 'unreachable';
            }
        }

        // La configuration embarquée dans l'exécutable donne son identité à
        // l'application : c'est elle, et non cette page, qui porte le nom et la
        // version affichés ci-dessus.
        //
        // Le moteur générique n'a pas de version applicative — elle n'existe que
        // pour un exécutable produit par /config. On retombe alors sur celle du
        // moteur, plutôt que de laisser la ligne vide.
        fetch('/api/app')
            .then(reponse => reponse.ok ? reponse.json() : null)
            .then(app => {
                if (!app) return;
                document.title = app.name;
                document.getElementById('titre').textContent = app.name;
                document.getElementById('version').textContent = app.version
                    ? 'version ' + app.version
                    : 'generic engine — Proton ' + app.engine.version;
            })
            .catch(() => {});
        </script>
        </body>
        </html>

        """;
}
