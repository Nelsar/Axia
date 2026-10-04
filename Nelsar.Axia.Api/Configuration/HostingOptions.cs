namespace Nelsar.Axia.Api.Configuration;

/// <summary>
/// Which browser origins may call this API.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Origins allowed to call the API from a browser, as scheme://host:port with no trailing
    /// slash — for example <c>https://console.example.com</c>.
    ///
    /// Empty means loopback only: any port on localhost, 127.0.0.1 or [::1], which is what the
    /// console needs during development and nothing more. This used to be AllowAnyOrigin, on the
    /// reasoning that authentication is a bearer token rather than a cookie, so no browser would
    /// attach credentials on another site's behalf. That much is true, and it is also not the
    /// whole risk: any page a person visits could reach an API they can route to and read what it
    /// answers without a token — which directory names exist, whether the host is there at all.
    /// A deployment that wants to be callable from the open web can say so here.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>
/// Whether this instance sits behind a reverse proxy, and which one.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// Addresses of the proxies immediately in front of this instance. Empty — the default —
    /// means X-Forwarded-* headers are ignored entirely.
    ///
    /// Ignoring them is the safe default, because honouring them from an untrusted peer is worse
    /// than not honouring them at all: the rate limiter partitions by client address, so a caller
    /// who could set X-Forwarded-For would get a fresh bucket per request and the limit would stop
    /// existing. Naming the proxy here is what makes the header trustworthy — it is then only
    /// believed when the connection itself comes from that address.
    ///
    /// It matters because without it, behind a proxy, every client shares the proxy's address and
    /// therefore one rate-limit bucket: five sign-in attempts a minute for everybody together.
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>
    /// Whole networks to trust instead of individual addresses, in CIDR form
    /// (<c>10.0.0.0/8</c>). Useful where the proxy's address is not fixed, as in a container
    /// network.
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];
}

/// <summary>
/// Transport security for the HTTP surface itself.
/// </summary>
public sealed class HttpsOptions
{
    public const string SectionName = "Https";

    /// <summary>
    /// Whether the application answers plain HTTP with a redirect to HTTPS.
    ///
    /// Off by default, and not because HTTPS is optional. The recommended topology terminates TLS
    /// at a reverse proxy and forwards plain HTTP over a private network; an application that
    /// redirected there would answer the proxy's forwarded request with a redirect the proxy then
    /// follows back to itself. Turn it on when the application is exposed directly and holds its
    /// own certificate.
    /// </summary>
    public bool RedirectToHttps { get; set; }
}
