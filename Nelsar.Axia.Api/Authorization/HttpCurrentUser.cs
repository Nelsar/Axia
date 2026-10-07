using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Api.Authorization;

/// <summary>
/// Supplies the Application layer with the caller behind the current HTTP request.
///
/// This is the adapter for <see cref="ICurrentUser"/>: the port lives in Contracts so that use
/// cases can authorise themselves without knowing HttpContext exists, and everything
/// ASP.NET-specific about answering "who is asking" stays here.
/// </summary>
internal sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? Subject => RequestPrincipal.Subject(_httpContextAccessor.HttpContext?.User);

    public string? DistinguishedName => RequestPrincipal.DistinguishedName(_httpContextAccessor.HttpContext?.User);

    public Guid? ProviderId => RequestPrincipal.ProviderId(_httpContextAccessor.HttpContext);

    /// <summary>
    /// Never. An HTTP request is always somebody, even when that somebody turns out to be
    /// anonymous — and a request that could declare itself a system context would be a header
    /// away from bypassing every permission check in the pipeline.
    /// </summary>
    public bool IsSystem => false;
}
