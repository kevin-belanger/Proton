using System.Net;
using Proton.Bootstrap;
using Proton.Hosting;

namespace Proton.Tests;

/// <summary>
/// Serveur HTTP local : port automatique, isolation, priorité des routes.
///
/// Ces tests couvrent CA-03, CA-04 et CA-13, ainsi que les §7, §9, §10 et §49.
/// </summary>
public sealed class LocalWebHostTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "proton-tests", Guid.NewGuid().ToString("N"));

    public LocalWebHostTests()
    {
        Directory.CreateDirectory(_root);
        Scaffolding.Ensure(ApplicationPaths.ForRoot(_root), hasEmbeddedApp: false);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private Task<LocalWebHost> StartAsync() =>
        LocalWebHost.StartAsync(ApplicationPaths.ForRoot(_root));

    // --- CA-03 : port automatique -------------------------------------------------

    [Fact]
    public async Task Ecoute_sur_un_port_attribue_par_le_systeme()
    {
        await using LocalWebHost host = await StartAsync();

        Assert.InRange(host.Address.Port, 1, 65535);
        Assert.Equal("127.0.0.1", host.Address.Host);
    }

    [Fact]
    public async Task Deux_instances_simultanees_obtiennent_des_ports_distincts()
    {
        await using LocalWebHost first = await StartAsync();
        await using LocalWebHost second = await StartAsync();

        // Aucun port fixe n'est imposé : deux applications Proton doivent pouvoir
        // coexister sans se disputer une adresse (§9.2).
        Assert.NotEqual(first.Address.Port, second.Address.Port);
    }

    // --- CA-04 : isolation réseau -------------------------------------------------

    [Fact]
    public async Task N_ecoute_pas_sur_une_interface_routable()
    {
        await using LocalWebHost host = await StartAsync();

        IPAddress? routable = Dns.GetHostAddresses(Dns.GetHostName())
            .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                 && !IPAddress.IsLoopback(a));

        // Une machine sans interface routable ne permet pas d'exercer ce critère.
        if (routable is null)
            return;

        using var client = new System.Net.Sockets.TcpClient();

        // Une autre machine du réseau ne doit pas pouvoir joindre l'application (§10).
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await client.ConnectAsync(routable, host.Address.Port).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    // --- §7 : contenu de `app` servi à la racine ----------------------------------

    [Fact]
    public async Task Sert_index_html_a_la_racine()
    {
        await using LocalWebHost host = await StartAsync();
        using HttpClient client = CreateClient(host);

        HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Proton", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sert_les_sous_dossiers_de_app()
    {
        Directory.CreateDirectory(Path.Combine(_root, "app", "css"));
        await File.WriteAllTextAsync(Path.Combine(_root, "app", "css", "style.css"), "body{}");

        await using LocalWebHost host = await StartAsync();
        using HttpClient client = CreateClient(host);

        HttpResponseMessage response = await client.GetAsync("/css/style.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Retourne_404_pour_un_fichier_absent()
    {
        await using LocalWebHost host = await StartAsync();
        using HttpClient client = CreateClient(host);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/absent.html")).StatusCode);
    }

    // --- §49 : priorité des espaces réservés --------------------------------------

    [Theory]
    [InlineData("/api/inconnu")]
    public async Task Les_api_non_encore_livrees_repondent_501(string path)
    {
        await using LocalWebHost host = await StartAsync();
        using HttpClient client = CreateClient(host);

        HttpResponseMessage response = await client.GetAsync(path);

        // Réservées mais pas encore implémentées : elles doivent répondre
        // explicitement plutôt que de retomber sur le service de fichiers statiques.
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task Un_fichier_de_app_ne_peut_pas_capturer_une_route_reservee()
    {
        // Un fichier `app/files/test.html` ne doit pas prendre le contrôle de la
        // route `/files/test.html`, qui appartient à l'API Proton (§49).
        Directory.CreateDirectory(Path.Combine(_root, "app", "data"));
        await File.WriteAllTextAsync(Path.Combine(_root, "app", "data", "test.html"), "détourné");

        await using LocalWebHost host = await StartAsync();
        using HttpClient client = CreateClient(host);

        HttpResponseMessage response = await client.GetAsync("/files/test.html");

        // La route appartient à l'API de fichiers, qui ne trouve rien de ce nom dans
        // `data` : le fichier homonyme de `app` ne doit pas s'y substituer.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("détourné", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ne_reserve_que_les_segments_exacts()
    {
        // `/database.html` commence par « /data » sans appartenir à l'espace réservé :
        // il doit être servi normalement depuis `app`.
        await File.WriteAllTextAsync(Path.Combine(_root, "app", "database.html"), "à moi");

        await using LocalWebHost host = await StartAsync();
        using HttpClient client = CreateClient(host);

        HttpResponseMessage response = await client.GetAsync("/database.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("à moi", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // --- CA-13 : libération du port -----------------------------------------------

    [Fact]
    public async Task Libere_le_port_apres_arret()
    {
        int port;

        await using (LocalWebHost host = await StartAsync())
        {
            port = host.Address.Port;
        }

        // Le port doit pouvoir être réutilisé immédiatement : aucun serveur Proton
        // ne doit demeurer actif en arrière-plan (§12).
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
    }

    private static HttpClient CreateClient(LocalWebHost host) =>
        new() { BaseAddress = host.Address, Timeout = TimeSpan.FromSeconds(10) };
}
