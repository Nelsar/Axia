namespace Nelsar.Axia.Api.Middleware;

/// <summary>
/// The headers every API response carries, whatever produced it — a controller, the exception
/// handler, the rate limiter's refusal.
///
/// The API sent none of them. Its answers hold a session token, directory entries and the audit
/// trail, and without Cache-Control a shared cache or the browser's own was free to keep them;
/// without nosniff a browser could be talked into reading a JSON body as something else. These are
/// for the API, reached through a proxy or directly. Where this process serves the console as well
/// (see <see cref="ConsoleFiles"/>), the console's responses get the headers the container image's
/// nginx sends for it instead — the two have to stay the same.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    // As in frontend/nginx.conf, which says why each source is what it is.
    internal const string ConsoleContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; "
        + "connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    private readonly RequestDelegate _next;
    private readonly bool _servesConsole;

    public SecurityHeadersMiddleware(RequestDelegate next, bool servesConsole = false)
    {
        _next = next;
        _servesConsole = servesConsole;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var path = context.Request.Path;

            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";

            if (_servesConsole && ConsoleFiles.IsConsolePath(path))
            {
                // The same files for everyone, so caching them is fine — but index.html is
                // revalidated every time, or an upgrade would not reach a browser that has the
                // previous one.
                headers.CacheControl = ConsoleFiles.IsFingerprinted(path)
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
                headers.ContentSecurityPolicy = ConsoleContentSecurityPolicy;
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                return Task.CompletedTask;
            }

            // Per-user data, every response of it: no cache anywhere may keep it.
            headers.CacheControl = "no-store";
            headers.Pragma = "no-cache";

            // JSON is never a document to render. Swagger UI is, and only in Development; it
            // loads its own script and styles, so it is left without this policy.
            if (!path.StartsWithSegments("/swagger"))
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
