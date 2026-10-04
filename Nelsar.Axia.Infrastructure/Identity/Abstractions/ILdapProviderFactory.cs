using Microsoft.AspNetCore.Http;

namespace Nelsar.Axia.Infrastructure.Identity.Abstractions;

internal interface ILdapProviderFactory
{
    ILdapProvider Resolve(HttpContext? context = null);
    ILdapProvider Resolve(string providerId);
}
