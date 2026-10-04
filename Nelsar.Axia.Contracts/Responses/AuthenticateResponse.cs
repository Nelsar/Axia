using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Contracts.Responses;

public class AuthenticateResponse
{
    public string Token { get; set; } = string.Empty;
    public UserDto User { get; set; } = new UserDto();
}
