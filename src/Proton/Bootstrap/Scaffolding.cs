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
    public sealed record Result(bool CreatedApp, bool CreatedData, bool CreatedIndex);

    /// <summary>
    /// Vérifie l'existence de <c>app</c> et <c>data</c>, et les crée au besoin.
    /// Le dossier <c>config</c> n'est jamais créé lors d'un démarrage normal (§8).
    /// </summary>
    public static Result Ensure(ApplicationPaths paths)
    {
        bool createdApp = CreateDirectoryIfMissing(paths.App);
        bool createdData = CreateDirectoryIfMissing(paths.Data);

        // L'index n'est engendré que si le dossier `app` ne contient aucune page
        // d'accueil — y compris lorsque le dossier existait déjà, mais vide.
        bool createdIndex = false;
        string index = Path.Combine(paths.App, "index.html");

        if (!File.Exists(index))
        {
            File.WriteAllText(index, HelloWorld, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            createdIndex = true;
        }

        return new Result(createdApp, createdData, createdIndex);
    }

    private static bool CreateDirectoryIfMissing(string path)
    {
        if (Directory.Exists(path))
            return false;

        Directory.CreateDirectory(path);
        return true;
    }

    private const string HelloWorld = """
        <!doctype html>
        <html lang="fr">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Proton</title>
            <style>
                :root { color-scheme: light dark; }
                body {
                    margin: 0;
                    min-height: 100vh;
                    display: grid;
                    place-content: center;
                    gap: 0.5rem;
                    text-align: center;
                    font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif;
                }
                h1 { margin: 0; font-size: 2.5rem; font-weight: 600; }
                p  { margin: 0; opacity: 0.6; }
                code {
                    font-family: "Cascadia Code", Consolas, monospace;
                    font-size: 0.9em;
                }
            </style>
        </head>
        <body>
            <h1>Hello World</h1>
            <p>Cette page est servie depuis <code>app/index.html</code>.</p>
        </body>
        </html>

        """;
}
