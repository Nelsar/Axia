using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Nelsar.Axia.Api.Resources;
using Nelsar.Axia.Contracts.Identity;

namespace Nelsar.Axia.Api.Middleware;

/// <summary>
/// Decides which directory the request is for, and writes it where everything downstream looks
/// for it — <c>HttpContext.Items["LdapProviderId"]</c>, read by DefaultLdapProviderFactory to pick
/// the connection and by HttpCurrentUser to scope permissions.
///
/// It used to take the header and nothing else, which left the directory as per-request input
/// from the caller, unrelated to the one they had proved anything against. A token minted by
/// signing in to one domain could then be presented with a header naming another: the identity
/// was checked against the first directory, and the operation — along with the group membership
/// the permissions were computed from — happened in the second. Whether that let a caller do
/// something they could not otherwise do depended on whether the same login existed on the other
/// side, which is not a property to leave authorisation resting on.
///
/// So a session names its directory, and it is the one it was opened against. The header is still
/// how that directory is chosen, but only where there is no session to contradict: signing in. An
/// endpoint that allows anonymous callers has no session speaking for it, and the header is the
/// only way to say which directory to authenticate against. This is also what lets a caller
/// holding a token for one domain sign in to another.
///
/// With a session, the header is optional and merely has to agree. Sending none is the normal
/// case and resolves to the session's own directory rather than to the default, which is what a
/// caller meant all along.
///
/// There used to be a second exception, for LDAP credentials sent in a Basic header, on the
/// reasoning that they were checked against the named directory on that very request. They were
/// not: authentication runs before this middleware, so the password was checked against the
/// default directory and the request then served from the one in the header. That scheme is gone,
/// and any authenticated principal now counts as a session, so a scheme added later cannot reopen
/// the gap by being left off a list.
/// </summary>
public class ProviderHeaderMiddleware
{
    public const string HeaderName = "X-LDAP-Provider-Id";

    /// <summary>Where the decision lands. The key is the one this codebase already reads.</summary>
    public const string ItemsKey = "LdapProviderId";

    private readonly RequestDelegate _next;

    public ProviderHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requested = Trimmed(context.Request.Headers[HeaderName].ToString());

        if (SpeaksForASession(context))
        {
            var bound = Trimmed(context.User.FindFirstValue(AxiaClaims.LdapProviderId));

            if (requested is not null && !SameDirectory(requested, bound))
            {
                await RefuseAsync(context);
                return;
            }

            // Absent claim means the session was opened against the default directory, so leaving
            // the key unset is the correct answer rather than a missing one.
            if (bound is not null)
                context.Items[ItemsKey] = bound;
        }
        else if (requested is not null)
        {
            context.Items[ItemsKey] = requested;
        }

        await _next(context);
    }

    /// <summary>
    /// Whether the directory is already settled by who the caller is, rather than still open for
    /// them to state.
    ///
    /// Anonymous endpoints are excluded before anything else: sign-in is the one operation whose
    /// whole purpose is to choose a directory, and refusing it for disagreeing with a token the
    /// caller is in the middle of replacing would make switching domains impossible.
    /// </summary>
    private static bool SpeaksForASession(HttpContext context)
    {
        if (IsOpenToAnyone(context.GetEndpoint()))
            return false;

        return context.User.Identity?.IsAuthenticated == true;
    }

    /// <summary>
    /// Whether the endpoint demands nothing of the caller — sign-in and registration, which carry
    /// no authorisation metadata at all rather than an explicit <c>[AllowAnonymous]</c>.
    ///
    /// Stated as "positively known to be open" and not as "not known to be guarded", so that an
    /// endpoint this cannot see — no routing has run, metadata missing — is treated as guarded
    /// and its directory settled by the token. An access control that reads an absence as
    /// permission is one reordering of the pipeline away from letting everything through.
    /// </summary>
    private static bool IsOpenToAnyone(Endpoint? endpoint)
    {
        if (endpoint is null)
            return false;

        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return true;

        return endpoint.Metadata.GetMetadata<IAuthorizeData>() is null;
    }

    /// <summary>
    /// A null <paramref name="bound"/> is a session on the default directory, and naming any
    /// directory at that point is a disagreement — that is the case this exists to catch, not an
    /// omission to be lenient about.
    /// </summary>
    private static bool SameDirectory(string requested, string? bound)
    {
        if (bound is null)
            return false;

        // A Guid has more than one spelling — braces, casing — and two spellings of one directory
        // are not a disagreement worth a 403.
        if (Guid.TryParse(requested, out var left) && Guid.TryParse(bound, out var right))
            return left == right;

        return string.Equals(requested, bound, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RefuseAsync(HttpContext context)
    {
        // Written here rather than thrown, following the rate limiter: this is the middleware's
        // own refusal and not a failure surfacing from a use case. The shape matches what
        // GlobalExceptionHandler writes, so a client parses one thing either way.
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(
            new { message = ApiMessages.SessionBoundToAnotherDirectory() });
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
