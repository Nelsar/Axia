namespace Nelsar.Axia.Api.Middleware;

/// <summary>
/// The headers every API response carries, whatever produced it — a controller, the exception
/// handler, the rate limiter's refusal.
///
/// The API sent none of them. Its answers hold a session token, directory entries and the audit
/// trail, and without Cache-Control a shared cache or the browser's own was free to keep them;
/// without nosniff a browser could be talked into reading a JSON body as something else. The
/// console's nginx sets its own headers for the console; these are for the API, reached through
/// that proxy or directly.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Per-user data, every response of it: no cache anywhere may keep it.
            headers.CacheControl = "no-store";
            headers.Pragma = "no-cache";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";

            // JSON is never a document to render. Swagger UI is, and only in Development; it
            // loads its own script and styles, so it is left without this policy.
            if (!context.Request.Path.StartsWithSegments("/swagger"))
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
