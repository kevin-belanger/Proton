using Proton.Bootstrap;

namespace Proton.Tests;

/// <summary>
/// Initialisation automatique des dossiers (§8).
/// </summary>
public sealed class ScaffoldingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "proton-tests", Guid.NewGuid().ToString("N"));

    public ScaffoldingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private ApplicationPaths Paths => ApplicationPaths.ForRoot(_root);

    [Fact]
    public void Cree_app_et_data_lorsquils_sont_absents()
    {
        Scaffolding.Result result = Scaffolding.Ensure(Paths, hasEmbeddedApp: false);

        Assert.True(Directory.Exists(Paths.App));
        Assert.True(Directory.Exists(Paths.Files));
        Assert.True(result.CreatedApp);
        Assert.True(result.CreatedData);
    }

    [Fact]
    public void Depose_un_modele_de_configuration_pret_a_modifier()
    {
        Scaffolding.Result result = Scaffolding.Ensure(Paths, hasEmbeddedApp: false);

        Assert.True(result.CreatedConfig);
        Assert.True(Directory.Exists(Paths.Config));

        string configuration = File.ReadAllText(Path.Combine(Paths.Config, "config.json"));

        // Les deux champs obligatoires sont renseignés : `/generate` aboutit sans
        // qu'on touche au fichier (§8.2).
        Assert.Contains("\"name\"", configuration, StringComparison.Ordinal);
        Assert.Contains("\"executableName\"", configuration, StringComparison.Ordinal);

        // Le modèle s'explique lui-même — le format accepte les commentaires.
        Assert.Contains("//", configuration, StringComparison.Ordinal);

        // L'icône doit garder ses neuf résolutions : extraite du PE, elle n'en aurait
        // qu'une, et l'exécutable produit hériterait d'une icône dégradée (§8.2).
        byte[] icon = File.ReadAllBytes(Path.Combine(Paths.Config, "icon.ico"));

        Assert.Equal(0, icon[0]);            // en-tête ICO : réservé
        Assert.Equal(1, icon[2]);            // type 1 = icône
        Assert.True(icon[4] >= 5, $"L'icône ne porte que {icon[4]} résolution(s).");
    }

    [Fact]
    public void Un_executable_personnalise_ne_recoit_aucun_dossier_config()
    {
        Scaffolding.Result result = Scaffolding.Ensure(Paths, hasEmbeddedApp: true);

        // `config` est un outil de fabrication. Un exécutable produit s'adresse à un
        // utilisateur final, à qui il n'apprendrait rien et ne ferait qu'encombrer
        // (§8.2).
        Assert.False(result.CreatedConfig);
        Assert.False(Directory.Exists(Paths.Config));
    }

    [Fact]
    public void Ne_remplace_jamais_une_configuration_existante()
    {
        Directory.CreateDirectory(Paths.Config);
        string configuration = Path.Combine(Paths.Config, "config.json");
        File.WriteAllText(configuration, "{ \"name\": \"À moi\" }");

        Scaffolding.Ensure(Paths, hasEmbeddedApp: false);

        // Règle absolue de §8 : ce que l'utilisateur a écrit lui appartient.
        Assert.Equal("{ \"name\": \"À moi\" }", File.ReadAllText(configuration));

        // L'icône manquante est tout de même déposée : les fichiers sont traités
        // séparément.
        Assert.True(File.Exists(Path.Combine(Paths.Config, "icon.ico")));
    }

    [Fact]
    public void Engendre_une_page_daccueil_autonome()
    {
        Scaffolding.Result result = Scaffolding.Ensure(Paths, hasEmbeddedApp: false);

        string page = File.ReadAllText(Path.Combine(Paths.App, "index.html"));

        Assert.True(result.CreatedIndex);
        Assert.Contains("<!doctype html>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Proton", page, StringComparison.Ordinal);

        // Elle interroge les API du moteur : c'est ce qui la distingue d'une page
        // Web ordinaire, et ce qui en fait un premier diagnostic.
        Assert.Contains("/api/app", page, StringComparison.Ordinal);
        Assert.Contains("/api/sqlite", page, StringComparison.Ordinal);
        Assert.Contains("/files", page, StringComparison.Ordinal);

        // Une sonde en GET sur `/api/sqlite` reçoit 405 : la route n'accepte que
        // POST. C'est la preuve qu'elle répond, et non une panne — la page doit la
        // compter comme active (§8.1).
        Assert.Contains("405", page, StringComparison.Ordinal);

        // Elle oriente vers la documentation plutôt que de la reprendre (§8.1).
        Assert.Contains("https://kevin-belanger.github.io/Proton/", page, StringComparison.Ordinal);

        // Elle doit s'afficher sans réseau. Ce sont les ressources liées qui en
        // décideraient — feuille de style, image, police, script distant — et non
        // les liens, qu'un clic confie au navigateur du système (§51.1).
        Assert.DoesNotMatch("src\\s*=\\s*[\"']?(https?:)?//", page);
        Assert.DoesNotMatch("<(link|img|iframe|source|video|audio|embed|object)\\b", page);

        Assert.DoesNotContain("@import", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ne_remplace_jamais_un_index_existant()
    {
        Directory.CreateDirectory(Paths.App);
        string index = Path.Combine(Paths.App, "index.html");
        File.WriteAllText(index, "<h1>Test Proton</h1>");

        Scaffolding.Result result = Scaffolding.Ensure(Paths, hasEmbeddedApp: false);

        // « Proton ne doit jamais écraser automatiquement un fichier utilisateur
        // déjà existant » (§8).
        Assert.False(result.CreatedIndex);
        Assert.Equal("<h1>Test Proton</h1>", File.ReadAllText(index));
    }

    [Fact]
    public void Engendre_la_page_daccueil_dans_un_dossier_app_existant_mais_vide()
    {
        Directory.CreateDirectory(Paths.App);

        Scaffolding.Result result = Scaffolding.Ensure(Paths, hasEmbeddedApp: false);

        Assert.False(result.CreatedApp);
        Assert.True(result.CreatedIndex);
    }

    [Fact]
    public void Est_idempotent()
    {
        Scaffolding.Ensure(Paths, hasEmbeddedApp: false);
        Scaffolding.Result second = Scaffolding.Ensure(Paths, hasEmbeddedApp: false);

        Assert.False(second.CreatedApp);
        Assert.False(second.CreatedData);
        Assert.False(second.CreatedIndex);
    }
}
