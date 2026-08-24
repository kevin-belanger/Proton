using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proton.AppApi;
using Proton.Bootstrap;
using Proton.Configuration;
using Proton.FileApi;
using Proton.SqliteApi;

namespace Proton.Hosting;

/// <summary>
/// Serveur HTTP embarqué d'une application Proton.
///
/// Kestrel est lancé dans le processus Proton, sur un port choisi par le système et
/// sur la seule interface de boucle locale (§9, §10). L'adresse retenue est exposée
/// après démarrage : l'application Web n'a jamais à la connaître, seule la fenêtre
/// hôte s'en sert.
/// </summary>
public sealed class LocalWebHost : IAsyncDisposable
{
    private readonly WebApplication _application;

    private LocalWebHost(WebApplication application, Uri address)
    {
        _application = application;
        Address = address;
    }

    /// <summary>Adresse effective du serveur, port compris.</summary>
    public Uri Address { get; }

    /// <summary>
    /// Démarre le serveur et retourne une instance prête à servir.
    /// </summary>
    public static async Task<LocalWebHost> StartAsync(
        ApplicationPaths paths,
        CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();

        builder.WebHost.ConfigureKestrel(options =>
        {
            // 127.0.0.1 explicitement, et non AnyIP : une autre machine du réseau ne
            // doit pas pouvoir joindre l'application (§10, CA-04).
            //
            // Listen et non ListenLocalhost : ce dernier refuse le port dynamique,
            // « localhost » désignant potentiellement deux adresses à la fois.
            //
            // Port 0 : le système attribue un port libre au moment de l'écoute (§9.2).
            options.Listen(IPAddress.Loopback, port: 0);

            // La limite par défaut protège un serveur exposé au public ; elle n'a pas
            // lieu d'être ici. Elle est levée globalement puis rétablie sur /api, seul
            // espace dont le corps est chargé en mémoire (§58.1).
            options.Limits.MaxRequestBodySize = null;
        });

        WebApplication application = builder.Build();

        MapApiBodyLimit(application);
        MapAppApi(application);
        MapSqliteApi(application, paths);
        MapDataApi(application, paths);
        MapReservedApiSpace(application);
        MapStaticApplicationFiles(application, paths);

        await application.StartAsync(cancellationToken).ConfigureAwait(false);

        Uri address = ResolveAddress(application);
        return new LocalWebHost(application, address);
    }

    /// <summary>
    /// Rétablit une limite de corps sur <c>/api</c>, dont les requêtes sont
    /// désérialisées en mémoire avant d'être exécutées (§58.1).
    /// </summary>
    private static void MapApiBodyLimit(WebApplication application)
    {
        const long ApiBodyLimit = 32L * 1024 * 1024;

        application.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var feature = context.Features
                    .Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();

                if (feature is { IsReadOnly: false })
                    feature.MaxRequestBodySize = ApiBodyLimit;
            }

            await next(context).ConfigureAwait(false);
        });
    }

    private static void MapAppApi(WebApplication application) =>
        AppEndpoints.Map(application, AppConfiguration.Load());

    private static void MapSqliteApi(WebApplication application, ApplicationPaths paths) =>
        SqliteEndpoints.Map(application, new SqliteService(new DataPath(paths.Data)));

    private static void MapDataApi(WebApplication application, ApplicationPaths paths) =>
        DataEndpoints.Map(application, new DataFileService(new DataPath(paths.Data)));

    /// <summary>
    /// Réserve ce qui reste de <c>/data</c> et <c>/api</c> avant tout service de
    /// fichier statique : les routes non encore implémentées doivent répondre
    /// explicitement plutôt que de retomber sur `app` (§49).
    /// </summary>
    private static void MapReservedApiSpace(WebApplication application)
    {
        application.Use(async (context, next) =>
        {
            if (!ReservedSpaces.Contains(context.Request.Path.Value ?? string.Empty))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status501NotImplemented;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(
                """{"error":{"code":"not_implemented","message":"This API is not available yet."}}""",
                context.RequestAborted).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Expose le contenu de <c>app</c> directement à la racine du serveur (§7).
    /// </summary>
    private static void MapStaticApplicationFiles(WebApplication application, ApplicationPaths paths)
    {
        var files = new PhysicalFileProvider(paths.App);

        application.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = files,
            RequestPath = PathString.Empty
        });

        application.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            RequestPath = PathString.Empty,
            // Le contenu servi est local et change pendant le développement :
            // laisser la validation par ETag faire son travail à chaque requête.
            OnPrepareResponse = context =>
                context.Context.Response.Headers.CacheControl = "no-cache"
        });
    }

    private static Uri ResolveAddress(WebApplication application)
    {
        // Après démarrage, Urls reflète les adresses réellement retenues par Kestrel,
        // port attribué par le système compris.
        string? address = application.Urls.FirstOrDefault();

        if (string.IsNullOrEmpty(address))
            throw new InvalidOperationException(
                "Kestrel a démarré sans exposer d'adresse d'écoute.");

        // Kestrel annonce « http://127.0.0.1:48723 ». L'URI de base doit se terminer
        // par une barre oblique pour que la WebView charge bien la racine.
        return new Uri(address.EndsWith('/') ? address : address + "/");
    }

    /// <summary>
    /// Arrête le serveur proprement et libère le port (§12, CA-13).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _application.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // L'arrêt du serveur ne doit jamais empêcher la fermeture du processus.
        }

        await _application.DisposeAsync().ConfigureAwait(false);
    }
}
