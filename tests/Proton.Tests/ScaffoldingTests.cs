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
        Assert.True(Directory.Exists(Paths.Data));
        Assert.True(result.CreatedApp);
        Assert.True(result.CreatedData);
    }

    [Fact]
    public void Ne_cree_jamais_le_dossier_config()
    {
        Scaffolding.Ensure(Paths, hasEmbeddedApp: false);

        // Le dossier `config` est un outil de personnalisation : un démarrage normal
        // ne doit pas le faire apparaître dans le dossier de l'utilisateur (§8).
        Assert.False(Directory.Exists(Paths.Config));
    }

    [Fact]
    public void Engendre_une_page_daccueil_autonome()
    {
        Scaffolding.Result result = Scaffolding.Ensure(Paths, hasEmbeddedApp: false);

        string page = File.ReadAllText(Path.Combine(Paths.App, "index.html"));

        Assert.True(result.CreatedIndex);
        Assert.Contains("<!doctype html>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Proton", page, StringComparison.Ordinal);

        // La page se présente et interroge les API du moteur : c'est ce qui la
        // distingue d'une page Web ordinaire.
        Assert.Contains("/api/app", page, StringComparison.Ordinal);

        // Aucune ressource externe : la page doit s'afficher sans réseau.
        Assert.DoesNotContain("http://", page, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", page, StringComparison.Ordinal);
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
