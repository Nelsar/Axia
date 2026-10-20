using System.Net;

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

    /// <summary>
    /// Whether any website may call the API from a browser. Off by default, and deliberately a
    /// switch of its own rather than a <c>*</c> in <see cref="AllowedOrigins"/>.
    ///
    /// A <c>*</c> in the list used to be accepted without comment and meant exactly this. It is
    /// a bigger decision than it looks: with the API reachable only on an internal network, any
    /// page an employee opens can use their browser to send sign-in attempts to it and read the
    /// answers — a token included, when a guess is right. Bearer tokens keep it from acting as the
    /// employee; they do not keep it from guessing.
    /// </summary>
    public bool AllowAnyOrigin { get; set; }

    /// <summary>
    /// <see cref="AllowedOrigins"/> as the exact strings a browser sends in its Origin header.
    ///
    /// Every entry is checked, and one that cannot match anything stops startup. A trailing slash,
    /// a path, a <c>*.</c> wildcard or an empty value all used to be accepted and then silently
    /// match no origin at all — the console reported the API unreachable, and nothing pointed at
    /// the setting. An empty entry also switched off the loopback default, since the list was no
    /// longer empty. Same reasoning as <see cref="ReverseProxyOptions"/>: a typo in an access
    /// control should be loud.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entry is not a bare origin.</exception>
    public IReadOnlyList<string> ParseAllowedOrigins()
    {
        if (AllowAnyOrigin && AllowedOrigins.Length > 0)
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(AllowAnyOrigin)} is on and {SectionName}:{nameof(AllowedOrigins)} "
                + "names origins as well. The switch already admits every origin, so the list would "
                + "mean nothing — set one or the other.");

        var parsed = new List<string>(AllowedOrigins.Length);

        foreach (var entry in AllowedOrigins)
        {
            var origin = entry?.Trim() ?? string.Empty;

            if (origin.Length == 0)
                throw Invalid(entry, "the entry is empty. Remove it, or leave the whole list empty to "
                    + "allow only this machine's loopback addresses");

            if (origin == "*")
                throw Invalid(entry, $"to admit every website, set {SectionName}:{nameof(AllowAnyOrigin)} "
                    + "to true instead — and read what it allows first");

            if (origin.Contains('*'))
                throw Invalid(entry, "wildcards are not supported. List each origin the console is "
                    + "served from, as in https://console.example.com");

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw Invalid(entry, "expected an http or https origin, as in https://console.example.com");

            // An origin is scheme, host and port and nothing else. Anything after it — a slash,
            // a path, a query — means no Origin header a browser sends will ever equal it.
            var bare = uri.GetLeftPart(UriPartial.Authority);
            if (!string.Equals(origin, bare, StringComparison.OrdinalIgnoreCase))
                throw Invalid(entry, $"an origin has no path, query or trailing slash; write {bare}");

            parsed.Add(bare);
        }

        return parsed;
    }

    private static InvalidOperationException Invalid(string? value, string expectation) =>
        new($"{SectionName}:{nameof(AllowedOrigins)} contains '{value}', which can never match a "
            + $"browser's origin: {expectation}.");
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

    /// <summary>Whether any proxy was named at all.</summary>
    public bool IsConfigured => KnownProxies.Length > 0 || KnownNetworks.Length > 0;

    /// <summary>
    /// <see cref="KnownProxies"/> as addresses.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entry is not an IP address.</exception>
    public IReadOnlyList<IPAddress> ParseKnownProxies()
    {
        var parsed = new List<IPAddress>(KnownProxies.Length);

        foreach (var proxy in KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
                throw Invalid(
                    nameof(KnownProxies),
                    proxy,
                    "expected an IP address, as in 10.0.0.7 — a host name will not do, because the "
                    + "check happens per request against the address the connection actually came from");

            parsed.Add(address);
        }

        return parsed;
    }

    /// <summary>
    /// <see cref="KnownNetworks"/> as networks.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entry is not a CIDR network.</exception>
    public IReadOnlyList<IPNetwork> ParseKnownNetworks()
    {
        var parsed = new List<IPNetwork>(KnownNetworks.Length);

        foreach (var network in KnownNetworks)
        {
            if (!IPNetwork.TryParse(network, out var parsedNetwork))
                throw Invalid(
                    nameof(KnownNetworks),
                    network,
                    "expected CIDR notation, as in 10.0.0.0/8");

            // TryParse masks off bits set below the prefix, so 10.0.0.7/8 is accepted and quietly
            // becomes the whole of 10.0.0.0/8. Refused, because the two readings differ by sixteen
            // million trusted addresses and the text does not say which was meant.
            var slash = network.IndexOf('/');
            if (slash > 0
                && IPAddress.TryParse(network.AsSpan(0, slash), out var written)
                && !written.Equals(parsedNetwork.BaseAddress))
            {
                throw Invalid(
                    nameof(KnownNetworks),
                    network,
                    $"it has bits set below the /{parsedNetwork.PrefixLength} prefix, so it reads "
                    + $"either as the single address {written}, which belongs under "
                    + $"{nameof(KnownProxies)}, or as the whole of {parsedNetwork} — write whichever "
                    + "was meant");
            }

            parsed.Add(parsedNetwork);
        }

        return parsed;
    }

    // Refusing to start is deliberate. The alternative — skipping the entry — leaves an instance
    // running that looks configured and is not: forwarded headers quietly ignored, every client
    // in the proxy's single rate-limit bucket. A typo in a security control should be loud.
    private static InvalidOperationException Invalid(string key, string value, string expectation) =>
        new($"{SectionName}:{key} contains '{value}', which could not be read: {expectation}.");
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

/// <summary>
/// Whether this process serves the console's files as well as the API, and from where.
/// </summary>
public sealed class ConsoleOptions
{
    public const string SectionName = "Console";

    /// <summary>The folder looked in when <see cref="Path"/> is not set, next to the binaries.</summary>
    public const string DefaultFolder = "wwwroot";

    /// <summary>
    /// The folder holding the built console — <c>index.html</c> and what Vite emitted with it.
    /// Relative paths are relative to the folder the API's binaries are in.
    ///
    /// Unset, the API looks for <c>wwwroot</c> next to its binaries and serves the console from it
    /// when it is there, which is how a package installs it; when it is not — the container image,
    /// a development checkout — it serves the API alone, as it always has. Set, the folder has to
    /// hold the console, or startup stops: a console that was asked for and is quietly missing
    /// would surface as a 404 at the address users were given, with nothing pointing here.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// The folder to serve the console from, or <see langword="null"/> when there is none to
    /// serve.
    /// </summary>
    /// <param name="baseDirectory">The folder the binaries are in.</param>
    /// <exception cref="InvalidOperationException"><see cref="Path"/> does not hold a console.</exception>
    public string? ResolveRoot(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            var beside = System.IO.Path.Join(baseDirectory, DefaultFolder);
            return HoldsConsole(beside) ? beside : null;
        }

        var root = System.IO.Path.GetFullPath(Path.Trim(), baseDirectory);
        if (!HoldsConsole(root))
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(Path)} is '{Path}', and '{root}' has no index.html. Point it at "
                + "the folder `npm run build` produced (frontend/dist), or remove the setting to serve "
                + "the API alone.");

        return root;
    }

    private static bool HoldsConsole(string folder) =>
        File.Exists(System.IO.Path.Join(folder, "index.html"));
}
