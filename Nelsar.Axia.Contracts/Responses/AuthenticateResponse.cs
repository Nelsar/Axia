using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Contracts.Responses;

public class AuthenticateResponse
{
    public string Token { get; set; } = string.Empty;
    public UserDto User { get; set; } = new UserDto();

    /// <summary>
    /// What the signed-in principal may do right now, in catalogue order — so the console can show
    /// what the API will allow without a second request. A convenience, not an authority: every
    /// request is still checked on its own, and GET /api/v1/me/permissions refreshes it.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; set; } = [];
}
