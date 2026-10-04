using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Application.Interfaces;

public interface ITokenService
{
    string GenerateToken(UserDto user);
}
