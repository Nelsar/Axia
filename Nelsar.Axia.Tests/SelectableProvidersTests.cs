using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The directory picker a sign-in page reads, and the line it must not cross.
///
/// These drive the real handler over a mocked repository rather than mocking the handler: what is
/// under test is the projection itself — which fields of a Provider survive into an anonymous
/// response — and a mocked handler returning a hand-built DTO would assert nothing about that.
/// </summary>
public class SelectableProvidersTests
{
    private const string Endpoint = "/api/v1/providers/selectable";

    private static ApiTestFactory FactoryFor(params Provider[] providers)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(x => x.GetAllAsync()).ReturnsAsync(providers);

        return new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IProviderRepository));
            services.AddSingleton(repo.Object);
        });
    }

    [Fact]
    public async Task ThePicker_IsReadableWithoutSigningIn()
    {
        // The whole point. It is read before anyone has credentials accepted — a caller must name
        // a directory to be authenticated against, so needing a session to learn which directories
        // exist is a circle with only one way out: an administrator pasting a Guid.
        using var factory = FactoryFor(TestProvider.Create(name: "Head office"));
        var client = factory.CreateClient();

        var response = await client.GetAsync(Endpoint);

        response.EnsureSuccessStatusCode();
        var directories = await response.Content.ReadFromJsonAsync<List<SelectableProviderDto>>();
        Assert.Equal("Head office", Assert.Single(directories!).Name);
    }

    [Fact]
    public async Task ThePicker_TellsNobodyHowADirectoryIsReached()
    {
        // The assertion this file exists for, made against the wire bytes rather than against a
        // deserialised type — a field added to the projection later would be invisible to a typed
        // assertion, and this is the one response on the controller that anyone at all can read.
        using var factory = FactoryFor(TestProvider.Create(
            name: "Branch",
            host: "dc01.internal.example.kz",
            port: 636,
            failoverHosts: "dc02.internal.example.kz",
            baseDn: "dc=branch,dc=example,dc=kz",
            adminUsername: "svc-bind",
            encryptedAdminPassword: "enc:should-never-appear",
            userSearchFilter: "(&(objectClass=person)(uid={0}))"));

        var client = factory.CreateClient();

        var body = await (await client.GetAsync(Endpoint)).Content.ReadAsStringAsync();

        Assert.Contains("Branch", body);
        foreach (var secret in new[]
                 {
                     "dc01.internal.example.kz",
                     "dc02.internal.example.kz",
                     "dc=branch",
                     "svc-bind",
                     "should-never-appear",
                     "objectClass",
                     "636"
                 })
        {
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ADeactivatedDirectory_IsNotOffered()
    {
        // Offering it would be offering a choice that cannot work: naming a deactivated directory
        // is refused with a 400 the moment it is used.
        using var factory = FactoryFor(
            TestProvider.Create(name: "Live"),
            TestProvider.Create(name: "Retired", isActive: false));

        var client = factory.CreateClient();

        var directories = await client.GetFromJsonAsync<List<SelectableProviderDto>>(Endpoint);

        Assert.Equal("Live", Assert.Single(directories!).Name);
    }

    [Fact]
    public async Task DirectoriesComeBackInAnOrderAPersonCanScan()
    {
        using var factory = FactoryFor(
            TestProvider.Create(name: "Warehouse"),
            TestProvider.Create(name: "Branch"),
            TestProvider.Create(name: "Head office"));

        var client = factory.CreateClient();

        var directories = await client.GetFromJsonAsync<List<SelectableProviderDto>>(Endpoint);

        Assert.Equal(["Branch", "Head office", "Warehouse"], directories!.Select(d => d.Name));
    }

    [Fact]
    public async Task TheAdministrativeListing_IsStillBehindItsPermission()
    {
        // [AllowAnonymous] on one action of a controller guarded at the class level is exactly the
        // kind of edit that opens its neighbours by accident. This is the neighbour.
        using var factory = FactoryFor(TestProvider.Create());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ThePicker_DoesNotBindTheCallersSessionToADirectory()
    {
        // It is an anonymous endpoint, so ProviderHeaderMiddleware must leave it alone even when
        // a stale token is still attached — otherwise reading the picker in order to switch
        // domains would be refused by the session being switched away from.
        using var factory = FactoryFor(TestProvider.Create(name: "Branch"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestAuth.GenerateToken(UserRoles.Admin, providerId: Guid.NewGuid()));
        client.DefaultRequestHeaders.Add("X-LDAP-Provider-Id", Guid.NewGuid().ToString());

        var response = await client.GetAsync(Endpoint);

        response.EnsureSuccessStatusCode();
    }
}
