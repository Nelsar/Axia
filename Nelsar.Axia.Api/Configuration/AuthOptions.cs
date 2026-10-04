namespace Nelsar.Axia.Api.Configuration;

/// <summary>
/// What the sign-in surface is allowed to do before anyone has authenticated.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Whether <c>POST /api/auth/register</c> will create a directory account for an
    /// unauthenticated caller.
    ///
    /// False by default, and the default is the point. This endpoint writes to the directory:
    /// with it open, anyone who can reach the port can create accounts in the organisation's
    /// Active Directory. The rate limiter in front of it caps that at a few per minute, which
    /// makes it slow rather than impossible — a throttle is not an authorisation control.
    ///
    /// Administrators create accounts through <c>POST /api/v1/users</c>, which is the same
    /// operation behind the <c>users.create</c> permission. So nothing is lost by leaving this
    /// off; what it exists for is the deployment that genuinely wants open self-registration and
    /// says so deliberately.
    /// </summary>
    public bool AllowSelfRegistration { get; set; }
}
