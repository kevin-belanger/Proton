using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Proton.Configuration;
using Proton.Infrastructure;

namespace Proton.AppApi;

/// <summary>
/// Identité de l'application, exposée en lecture seule (§24.1).
///
/// Sans cette route, une application ne pourrait pas lire le nom et la version que
/// son propre exécutable porte, et devrait les redéclarer dans son code.
/// </summary>
public static class AppEndpoints
{
    public static void Map(WebApplication application, AppConfiguration configuration)
    {
        application.Use(async (context, next) =>
        {
            if (!context.Request.Path.Equals("/api/app", StringComparison.OrdinalIgnoreCase))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.Headers.Allow = "GET, HEAD";
                await ApiError.WriteAsync(context, StatusCodes.Status405MethodNotAllowed,
                    ApiError.MethodNotAllowed,
                    "The application configuration is read-only.");
                return;
            }

            // Ni chemin physique, ni numéro de port, ni information sur la machine :
            // une application Proton doit rester indépendante de son emplacement
            // (§7, §9.2, §24.1).
            await context.Response.WriteAsJsonAsync(new
            {
                name = configuration.Name,
                windowTitle = configuration.WindowTitle,
                version = configuration.Version,
                company = configuration.Company,
                engine = new
                {
                    name = AppConfiguration.EngineName,
                    version = AppConfiguration.EngineVersion,
                    license = AppConfiguration.EngineLicense,
                    copyright = AppConfiguration.EngineCopyright
                }
            }, context.RequestAborted);
        });
    }
}
