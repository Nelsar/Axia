using Microsoft.Extensions.FileProviders;

namespace Nelsar.Axia.Api.Middleware;

/// <summary>
/// The console's static files, served by the API itself, so that one process on one port is the
/// whole application — which is what an installed package or a Windows service needs. The
/// container image still serves them from nginx; this is for where there is no nginx.
///
/// The console routes in the URL fragment, so every page of it is <c>/</c> and nothing needs to
/// fall back to <c>index.html</c>: a path that is not a file is a 404, as from nginx's
/// <c>try_files</c> in all but name. Which headers these responses carry is decided in
/// <see cref="SecurityHeadersMiddleware"/>, by <see cref="IsConsolePath"/>.
/// </summary>
internal static class ConsoleFiles
{
    /// <summary>
    /// Whether a path belongs to the console rather than the API. Everything but <c>/api</c> and
    /// Swagger, which leaves the API's own policy on every answer the API gives.
    /// </summary>
    public static bool IsConsolePath(PathString path) =>
        !path.StartsWithSegments("/api") && !path.StartsWithSegments("/swagger");

    /// <summary>
    /// Whether a console file is one Vite named after its content. Those never change under the
    /// same name, so a browser may keep them for good; <c>index.html</c>, which names them, is the
    /// one file that has to be asked for again after an upgrade.
    /// </summary>
    public static bool IsFingerprinted(PathString path) =>
        path.StartsWithSegments("/assets");

    public static WebApplication UseConsoleFiles(this WebApplication app, string root)
    {
        var files = new PhysicalFileProvider(root);

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files });

        return app;
    }
}
