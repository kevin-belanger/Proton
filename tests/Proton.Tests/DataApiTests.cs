using System.Net;
using System.Text;
using Proton.Bootstrap;
using Proton.Hosting;

namespace Proton.Tests;

/// <summary>
/// API de fichiers, de bout en bout (§13 à §22, CA-05 à CA-10).
/// </summary>
public sealed class DataApiTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "proton-tests", Guid.NewGuid().ToString("N"));

    private LocalWebHost _host = null!;
    private HttpClient _client = null!;

    private string DataDirectory => Path.Combine(_root, "data");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        ApplicationPaths paths = ApplicationPaths.ForRoot(_root);
        Scaffolding.Ensure(paths, hasEmbeddedApp: false);

        _host = await LocalWebHost.StartAsync(paths);
        _client = new HttpClient { BaseAddress = _host.Address, Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static StringContent Body(string text) => new(text, Encoding.UTF8);

    // --- CA-06 puis CA-05 : écrire, puis relire -------------------------------------

    [Fact]
    public async Task Ecrit_puis_relit_un_fichier()
    {
        HttpResponseMessage written = await _client.PutAsync("/data/notes.txt", Body("bonjour"));

        Assert.Equal(HttpStatusCode.Created, written.StatusCode);

        HttpResponseMessage read = await _client.GetAsync("/data/notes.txt");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("bonjour", await read.Content.ReadAsStringAsync());
        Assert.NotNull(read.Headers.ETag);
        Assert.NotNull(read.Content.Headers.LastModified);
    }

    [Fact]
    public async Task Distingue_creation_et_remplacement()
    {
        // C'est le seul avertissement d'écrasement que fournit l'API (§19).
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PutAsync("/data/x.txt", Body("un"))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PutAsync("/data/x.txt", Body("deux"))).StatusCode);

        Assert.Equal("deux", await _client.GetStringAsync("/data/x.txt"));
    }

    [Fact]
    public async Task Cree_les_dossiers_parents_manquants()
    {
        HttpResponseMessage response =
            await _client.PutAsync("/data/a/b/c/note.txt", Body("profond"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(DataDirectory, "a", "b", "c", "note.txt")));
    }

    [Fact]
    public async Task Repond_304_sur_requete_conditionnelle()
    {
        await _client.PutAsync("/data/cache.txt", Body("stable"));

        HttpResponseMessage first = await _client.GetAsync("/data/cache.txt");
        string etag = first.Headers.ETag!.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, "/data/cache.txt");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        Assert.Equal(HttpStatusCode.NotModified, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Determine_le_type_de_contenu()
    {
        await _client.PutAsync("/data/page.html", Body("<p>salut</p>"));
        HttpResponseMessage response = await _client.GetAsync("/data/page.html");

        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Force_le_telechargement_sur_demande()
    {
        // §15.1 — facultatif, à la main de l'application.
        await _client.PutAsync("/data/rapport.pdf", Body("%PDF"));

        HttpResponseMessage sans = await _client.GetAsync("/data/rapport.pdf");
        HttpResponseMessage avec = await _client.GetAsync("/data/rapport.pdf?download=1");

        Assert.Null(sans.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", avec.Content.Headers.ContentDisposition?.DispositionType);
    }

    // --- CA-09 : suppression --------------------------------------------------------

    [Fact]
    public async Task Supprime_un_fichier()
    {
        await _client.PutAsync("/data/jetable.txt", Body("."));

        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.DeleteAsync("/data/jetable.txt")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync("/data/jetable.txt")).StatusCode);
    }

    [Fact]
    public async Task Repond_404_en_supprimant_un_fichier_absent()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.DeleteAsync("/data/fantome.txt")).StatusCode);
    }

    // --- §21 : listing ---------------------------------------------------------------

    [Fact]
    public async Task Liste_un_dossier()
    {
        await _client.PutAsync("/data/dossier/a.txt", Body("aa"));
        await _client.PutAsync("/data/dossier/b.txt", Body("bbbb"));
        await _client.PutAsync("/data/dossier/sous/c.txt", Body("c"));

        string json = await _client.GetStringAsync("/data/dossier/");

        Assert.Contains("\"a.txt\"", json, StringComparison.Ordinal);
        Assert.Contains("\"b.txt\"", json, StringComparison.Ordinal);
        Assert.Contains("\"directory\"", json, StringComparison.Ordinal);
        Assert.Contains("\"size\":4", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repond_404_en_listant_un_dossier_absent()
    {
        // Un dossier absent et un dossier vide sont deux états différents (§21).
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync("/data/jamais-cree/")).StatusCode);
    }

    [Fact]
    public async Task Redirige_un_dossier_demande_sans_barre_oblique()
    {
        await _client.PutAsync("/data/documents/note.txt", Body("."));

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = _host.Address
        };

        HttpResponseMessage response = await client.GetAsync("/data/documents");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/data/documents/", response.Headers.Location?.ToString());
    }

    // --- §22 : dossiers ---------------------------------------------------------------

    [Fact]
    public async Task Cree_un_dossier_de_maniere_idempotente()
    {
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PutAsync("/data/nouveau/", null)).StatusCode);

        // Recommencer n'est pas une erreur (§22.2).
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.PutAsync("/data/nouveau/", null)).StatusCode);

        Assert.True(Directory.Exists(Path.Combine(DataDirectory, "nouveau")));
    }

    [Fact]
    public async Task Refuse_de_supprimer_un_dossier_non_vide_sans_demande_explicite()
    {
        await _client.PutAsync("/data/plein/fichier.txt", Body("."));

        HttpResponseMessage response = await _client.DeleteAsync("/data/plein/");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("directory_not_empty", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        // Rien ne doit avoir été détruit.
        Assert.True(File.Exists(Path.Combine(DataDirectory, "plein", "fichier.txt")));
    }

    [Fact]
    public async Task Supprime_un_dossier_et_son_contenu_sur_demande_explicite()
    {
        await _client.PutAsync("/data/arbre/a.txt", Body("."));
        await _client.PutAsync("/data/arbre/sous/b.txt", Body("."));
        await _client.PutAsync("/data/arbre/sous/encore/c.txt", Body("."));

        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.DeleteAsync("/data/arbre/?recursive=1")).StatusCode);

        Assert.False(Directory.Exists(Path.Combine(DataDirectory, "arbre")));
    }

    [Fact]
    public async Task Refuse_de_supprimer_la_racine_de_data()
    {
        // Le contenu de `data` appartient à l'application, son existence non (§22.4).
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _client.DeleteAsync("/data/?recursive=1")).StatusCode);

        Assert.True(Directory.Exists(DataDirectory));
    }

    // --- CA-10 : confinement ----------------------------------------------------------

    [Theory]
    [InlineData("/data/../../Windows/System32/config")]
    [InlineData("/data/../app/index.html")]
    [InlineData("/data/C:/Windows")]
    public async Task Refuse_de_sortir_du_dossier_data(string chemin)
    {
        HttpResponseMessage response = await _client.GetAsync(chemin);

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"« {chemin} » a répondu {(int)response.StatusCode}.");
    }

    // --- §24.1 : identité de l'application ---------------------------------------------

    [Fact]
    public async Task Expose_la_configuration_de_l_application()
    {
        string json = await _client.GetStringAsync("/api/app");

        Assert.Contains("\"name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"engine\"", json, StringComparison.Ordinal);

        // Ni chemin physique, ni port : l'application doit rester indépendante de son
        // emplacement (§7, §9.2).
        Assert.DoesNotContain(_root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_host.Address.Port.ToString(), json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_configuration_est_en_lecture_seule()
    {
        HttpResponseMessage response = await _client.PutAsync("/api/app", Body("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // --- §24 : format des erreurs ------------------------------------------------------

    [Fact]
    public async Task Une_erreur_inattendue_reste_au_format_json()
    {
        // Un corps JSON malformé traverse la désérialisation et remonte plus loin que
        // les cas prévus. Quelle que soit l'exception, la réponse doit rester
        // interprétable par une application JavaScript (§24) — jamais la page HTML du
        // serveur.
        using var content = new StringContent("{ ceci n'est pas du JSON", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await _client.PostAsync("/api/sqlite/app.db/execute", content);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        string json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Les_erreurs_suivent_le_format_uniforme()
    {
        HttpResponseMessage response = await _client.GetAsync("/data/absent.txt");
        string json = await response.Content.ReadAsStringAsync();

        // Le code est destiné au programme, le message aux humains (§24).
        Assert.Contains("\"error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"not_found\"", json, StringComparison.Ordinal);
        Assert.Contains("\"message\"", json, StringComparison.Ordinal);
    }
}
