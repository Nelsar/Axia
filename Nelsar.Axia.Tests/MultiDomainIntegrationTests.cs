using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Tests;

/// <summary>
/// End-to-end tests for serving two LDAP domains from one API instance: an Active
/// Directory-flavored domain (CN= DNs, sAMAccountName logins, "Domain Admins" group)
/// and an OpenLDAP-flavored one (uid= DNs, uid logins, groupOfNames-style groups),
/// selected per request via the X-LDAP-Provider-Id header.
///
/// The two directories are simulated at the ILdapProvider seam — the exact boundary
/// where the real ActiveDirectoryProvider/StandardLdapProvider implementations differ —
/// because neither a Windows AD controller nor a live OpenLDAP container is available
/// to the test run. Everything above that seam is real: ProviderHeaderMiddleware,
/// per-request provider routing, LdapService, RoleMappingResolver (with per-provider
/// mapping scope), UserService, JWT issuance, and the controllers.
/// </summary>
public class MultiDomainIntegrationTests
{
    private static readonly Guid AdProviderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OpenLdapProviderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string ProviderHeader = "X-LDAP-Provider-Id";

    /// <summary>
    /// Test stand-in for DefaultLdapProviderFactory: routes to one of the two domain
    /// providers by the same HttpContext.Items key ProviderHeaderMiddleware populates.
    /// </summary>
    private sealed class RoutingLdapProviderFactory : ILdapProviderFactory
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly IReadOnlyDictionary<Guid, ILdapProvider> _providers;
        private readonly ILdapProvider _defaultProvider;

        public RoutingLdapProviderFactory(
            IHttpContextAccessor accessor,
            IReadOnlyDictionary<Guid, ILdapProvider> providers,
            ILdapProvider defaultProvider)
        {
            _accessor = accessor;
            _providers = providers;
            _defaultProvider = defaultProvider;
        }

        public ILdapProvider Resolve(HttpContext? context = null)
        {
            context ??= _accessor.HttpContext;
            var id = context?.Items["LdapProviderId"] as string;
            return Resolve(id ?? string.Empty);
        }

        /// <summary>
        /// Mirrors DefaultLdapProviderFactory's contract, including the part that matters most:
        /// a named domain that cannot be resolved is refused, never quietly swapped for the
        /// default one. A double that fell back here would let the suite go on describing
        /// behaviour production no longer has.
        /// </summary>
        public ILdapProvider Resolve(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                return _defaultProvider;

            return Guid.TryParse(providerId, out var guid) && _providers.TryGetValue(guid, out var provider)
                ? provider
                : throw new DomainValidationException(
                    ApplicationMessages.DirectoryProviderUnknown(providerId));
        }
    }

    private static Mock<ILdapProvider> BuildAdDomainMock()
    {
        var jsmith = new LdapUserInfo
        {
            Dn = "CN=John Smith,OU=Users,DC=corp,DC=example,DC=com",
            Username = "jsmith",
            DisplayName = "John Smith",
            Email = "jsmith@corp.example.com",
            Groups = ["CN=Domain Admins,CN=Users,DC=corp,DC=example,DC=com"]
        };

        var mock = new Mock<ILdapProvider>();
        mock.Setup(x => x.Authenticate("jsmith@corp.example.com", "AdPassw0rd!")).Returns(jsmith.Dn);
        mock.Setup(x => x.FindUser("jsmith@corp.example.com")).Returns(jsmith);
        mock.Setup(x => x.FindUserByDn(jsmith.Dn)).Returns(jsmith);
        mock.Setup(x => x.GetAllUsers()).Returns(DirectoryUserListing.Complete([jsmith]));
        return mock;
    }

    private static Mock<ILdapProvider> BuildOpenLdapDomainMock()
    {
        var jdoe = new LdapUserInfo
        {
            Dn = "uid=jdoe,ou=people,dc=nelsar,dc=com",
            Username = "jdoe",
            DisplayName = "John Doe",
            Email = "jdoe@nelsar.com",
            Groups = ["cn=UserCreators,ou=groups,dc=nelsar,dc=com"]
        };

        var mock = new Mock<ILdapProvider>();
        mock.Setup(x => x.Authenticate("jdoe@nelsar.com", "Test123!")).Returns(jdoe.Dn);
        mock.Setup(x => x.FindUser("jdoe@nelsar.com")).Returns(jdoe);
        mock.Setup(x => x.FindUserByDn(jdoe.Dn)).Returns(jdoe);
        mock.Setup(x => x.GetAllUsers()).Returns(DirectoryUserListing.Complete([jdoe]));
        return mock;
    }

    /// <summary>
    /// Role mappings as an admin would have configured them for two domains, by group name — the
    /// rows a database upgraded from before DN mappings still carries:
    /// - "Administrators" → Admin globally (applies to every domain);
    /// - "Domain Admins" → Admin only for the AD domain (AD's built-in group name);
    /// - "UserCreators" → UserCreator globally.
    /// </summary>
    private static Mock<IRoleMappingRepository> BuildRoleMappingRepoMock()
    {
        var global = new List<RoleMapping>
        {
            RoleMapping.SeedByGroupName(null, "Administrators", UserRoles.Admin, 0),
            RoleMapping.SeedByGroupName(null, "UserCreators", UserRoles.UserCreator, 10)
        };
        var adScoped = RoleMapping.SeedByGroupName(AdProviderId, "Domain Admins", UserRoles.Admin, 0);

        var mock = new Mock<IRoleMappingRepository>();
        mock.Setup(x => x.GetActiveForProviderAsync(AdProviderId))
            .ReturnsAsync(global.Append(adScoped).ToArray());
        mock.Setup(x => x.GetActiveForProviderAsync(OpenLdapProviderId))
            .ReturnsAsync(global.ToArray());
        mock.Setup(x => x.GetActiveForProviderAsync(null))
            .ReturnsAsync(global.ToArray());
        return mock;
    }

    private static ApiTestFactory BuildTwoDomainFactory(
        Mock<ILdapProvider>? adMock = null,
        Mock<ILdapProvider>? openLdapMock = null,
        Mock<ILdapProvider>? defaultDomainMock = null)
    {
        adMock ??= BuildAdDomainMock();
        openLdapMock ??= BuildOpenLdapDomainMock();
        defaultDomainMock ??= new Mock<ILdapProvider>(); // absent header: everything fails

        var providers = new Dictionary<Guid, ILdapProvider>
        {
            [AdProviderId] = adMock.Object,
            [OpenLdapProviderId] = openLdapMock.Object
        };

        return new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapProviderFactory));
            services.AddSingleton<ILdapProviderFactory>(sp => new RoutingLdapProviderFactory(
                sp.GetRequiredService<IHttpContextAccessor>(), providers, defaultDomainMock.Object));

            services.RemoveAll(typeof(IRoleMappingRepository));
            services.AddSingleton(BuildRoleMappingRepoMock().Object);
        });
    }

    [Fact]
    public async Task Login_AgainstAdDomain_IssuesJwt_WithAdScopedAdminRole()
    {
        using var factory = BuildTwoDomainFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ProviderHeader, AdProviderId.ToString());

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new AuthenticateRequest
        {
            Email = "jsmith@corp.example.com",
            Password = "AdPassw0rd!"
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload.Token);
        Assert.Equal("jsmith@corp.example.com", payload.User.Email);
        // "Domain Admins" maps to Admin only because the mapping is scoped to the AD provider.
        Assert.Equal(UserRoles.Admin, payload.User.Role);
    }

    [Fact]
    public async Task Login_AgainstOpenLdapDomain_IssuesJwt_WithGlobalUserCreatorRole()
    {
        using var factory = BuildTwoDomainFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ProviderHeader, OpenLdapProviderId.ToString());

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new AuthenticateRequest
        {
            Email = "jdoe@nelsar.com",
            Password = "Test123!"
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.NotNull(payload);
        Assert.Equal("jdoe@nelsar.com", payload.User.Email);
        Assert.Equal(UserRoles.UserCreator, payload.User.Role);
    }

    [Fact]
    public async Task Login_AdScopedMapping_DoesNotLeakIntoOpenLdapDomain()
    {
        // A user in the OpenLDAP domain who happens to be in a group named "Domain Admins"
        // must NOT become Admin: that mapping is scoped to the AD provider only.
        var openLdapMock = BuildOpenLdapDomainMock();
        var impostor = new LdapUserInfo
        {
            Dn = "uid=impostor,ou=people,dc=nelsar,dc=com",
            Username = "impostor",
            DisplayName = "Impostor",
            Email = "impostor@nelsar.com",
            Groups = ["cn=Domain Admins,ou=groups,dc=nelsar,dc=com"]
        };
        openLdapMock.Setup(x => x.Authenticate("impostor@nelsar.com", "Test123!")).Returns(impostor.Dn);
        openLdapMock.Setup(x => x.FindUser("impostor@nelsar.com")).Returns(impostor);
        openLdapMock.Setup(x => x.FindUserByDn(impostor.Dn)).Returns(impostor);

        using var factory = BuildTwoDomainFactory(openLdapMock: openLdapMock);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ProviderHeader, OpenLdapProviderId.ToString());

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new AuthenticateRequest
        {
            Email = "impostor@nelsar.com",
            Password = "Test123!"
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.NotNull(payload);
        Assert.Equal(UserRoles.User, payload.User.Role);
    }

    [Fact]
    public async Task Login_WithoutProviderHeader_HitsDefaultDomain_AndFails()
    {
        using var factory = BuildTwoDomainFactory();
        var client = factory.CreateClient();
        // No X-LDAP-Provider-Id: routed to the default provider, where jsmith doesn't exist.

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new AuthenticateRequest
        {
            Email = "jsmith@corp.example.com",
            Password = "AdPassw0rd!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllUsers_RoutesToTheDomainTheSessionWasOpenedAgainst()
    {
        // One API, two sessions, each opened against its own directory — and each seeing only
        // what that directory holds. The header still travels and still agrees; what has changed
        // is that it no longer decides on its own.
        using var factory = BuildTwoDomainFactory();

        var adUsers = await ListUsersAsync(factory, AdProviderId, sendHeader: true);
        var openLdapUsers = await ListUsersAsync(factory, OpenLdapProviderId, sendHeader: true);

        Assert.Equal("jsmith@corp.example.com", Assert.Single(adUsers.Items).Email);
        Assert.Equal("jdoe@nelsar.com", Assert.Single(openLdapUsers.Items).Email);
    }

    [Fact]
    public async Task GetAllUsers_NeedsNoHeader_BecauseTheSessionAlreadyNamesItsDomain()
    {
        // Before the session carried its directory, a request without the header fell to the
        // default domain — so a caller signed in to the branch domain silently read the head
        // office's directory the moment they forgot one header. The session answers it now.
        using var factory = BuildTwoDomainFactory();

        var users = await ListUsersAsync(factory, OpenLdapProviderId, sendHeader: false);

        Assert.Equal("jdoe@nelsar.com", Assert.Single(users.Items).Email);
    }

    [Fact]
    public async Task ASessionCannotReachIntoAnotherDomain_ByNamingItInAHeader()
    {
        // The hole this binding closes. The token proves an identity in the OpenLDAP domain; the
        // header points the operation at the AD one. Both directories are real and reachable
        // here, so nothing but the binding itself stands between this request and the other
        // domain's user list.
        var adDomain = BuildAdDomainMock();

        using var factory = BuildTwoDomainFactory(adMock: adDomain);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestAuth.GenerateToken(UserRoles.Admin, providerId: OpenLdapProviderId));
        client.DefaultRequestHeaders.Add(ProviderHeader, AdProviderId.ToString());

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        adDomain.Verify(x => x.GetAllUsers(), Times.Never);
    }

    [Fact]
    public async Task ASessionOnTheDefaultDomain_CannotNameADomainEither()
    {
        // The same hole by the other door: sign in with no header at all, then start naming
        // directories. An absent claim means the default domain, not "any will do".
        var adDomain = BuildAdDomainMock();

        using var factory = BuildTwoDomainFactory(adMock: adDomain);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        client.DefaultRequestHeaders.Add(ProviderHeader, AdProviderId.ToString());

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        adDomain.Verify(x => x.GetAllUsers(), Times.Never);
    }

    [Fact]
    public async Task SigningIn_IsNotBoundByTheSessionItIsAboutToReplace()
    {
        // Switching domains has to stay possible, and the way to switch is to authenticate
        // against the other one. The old token is still in the Authorization header while that
        // happens — an endpoint that allows anonymous callers is exempt precisely so this works.
        using var factory = BuildTwoDomainFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestAuth.GenerateToken(UserRoles.Admin, providerId: OpenLdapProviderId));
        client.DefaultRequestHeaders.Add(ProviderHeader, AdProviderId.ToString());

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new AuthenticateRequest
        {
            Email = "jsmith@corp.example.com",
            Password = "AdPassw0rd!"
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.NotNull(payload);
        Assert.Equal("jsmith@corp.example.com", payload.User.Email);
    }

    [Fact]
    public async Task ATokenIssuedAtSignIn_CarriesTheDomain_AndWorksWithoutTheHeader()
    {
        // The round trip through the real JwtTokenService rather than TestAuth: sign in to the
        // AD domain, then use the token it handed back with no header at all. If the claim were
        // not being written at issuance, this would read the default domain and find nothing.
        using var factory = BuildTwoDomainFactory();

        var signIn = factory.CreateClient();
        signIn.DefaultRequestHeaders.Add(ProviderHeader, AdProviderId.ToString());

        var login = await signIn.PostAsJsonAsync("/api/v1/auth/login", new AuthenticateRequest
        {
            Email = "jsmith@corp.example.com",
            Password = "AdPassw0rd!"
        });
        login.EnsureSuccessStatusCode();
        var issued = await login.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.NotNull(issued);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);

        var response = await client.GetAsync("/api/v1/users");

        response.EnsureSuccessStatusCode();
        var users = await response.Content.ReadFromJsonAsync<UserListingResponse>();
        Assert.NotNull(users);
        Assert.Equal("jsmith@corp.example.com", Assert.Single(users.Items).Email);
    }

    private static async Task<UserListingResponse> ListUsersAsync(
        ApiTestFactory factory, Guid domain, bool sendHeader)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestAuth.GenerateToken(UserRoles.Admin, providerId: domain));

        if (sendHeader)
            client.DefaultRequestHeaders.Add(ProviderHeader, domain.ToString());

        var response = await client.GetAsync("/api/v1/users");
        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadFromJsonAsync<UserListingResponse>();
        Assert.NotNull(users);
        return users;
    }

    [Fact]
    public async Task Login_SameCredentials_AreNotValidInTheOtherDomain()
    {
        using var factory = BuildTwoDomainFactory();
        var client = factory.CreateClient();
        // AD credentials sent to the OpenLDAP domain must be rejected: each domain
        // authenticates only its own accounts.
        client.DefaultRequestHeaders.Add(ProviderHeader, OpenLdapProviderId.ToString());

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new AuthenticateRequest
        {
            Email = "jsmith@corp.example.com",
            Password = "AdPassw0rd!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ARequestNamingAnUnknownDomain_IsRefused_NotServedByTheDefaultDomain()
    {
        // The regression this suite exists to prevent. A caller names a domain that does not
        // resolve; the API used to carry the operation out against the default domain and answer
        // 200. For a listing that is a confusing answer. For the DELETE next to it, it is a
        // deletion in the wrong directory, reported as success.
        var defaultDomain = new Mock<ILdapProvider>();

        using var factory = BuildTwoDomainFactory(defaultDomainMock: defaultDomain);
        var client = factory.CreateClient();
        var unknownDomain = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestAuth.GenerateToken(UserRoles.Admin, providerId: unknownDomain));
        client.DefaultRequestHeaders.Add(ProviderHeader, unknownDomain.ToString());

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Naming the id in the body is what proves the 400 came from resolving the domain, and
        // not from model binding or some other guard that would pass this test for free.
        Assert.Contains(unknownDomain.ToString(), await response.Content.ReadAsStringAsync());

        // The assertion that actually matters: the default domain was never asked anything.
        defaultDomain.Verify(x => x.GetAllUsers(), Times.Never);
    }

    [Fact]
    public async Task AWriteNamingAnUnknownDomain_NeverReachesTheDefaultDomain()
    {
        // Same check on the destructive verb, because that is where the old behaviour did real
        // damage rather than merely returning the wrong list.
        var defaultDomain = new Mock<ILdapProvider>();

        using var factory = BuildTwoDomainFactory(defaultDomainMock: defaultDomain);
        var client = factory.CreateClient();
        var unknownDomain = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestAuth.GenerateToken(UserRoles.Admin, providerId: unknownDomain));
        client.DefaultRequestHeaders.Add(ProviderHeader, unknownDomain.ToString());

        var response = await client.DeleteAsync($"/api/v1/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(unknownDomain.ToString(), await response.Content.ReadAsStringAsync());
        defaultDomain.Verify(x => x.DeleteUser(It.IsAny<string>()), Times.Never);
    }
}
