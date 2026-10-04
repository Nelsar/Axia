using MediatR;
using Nelsar.Axia.Application.Features.Providers.Queries;
using Nelsar.Axia.Application.Features.Providers.Commands;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Tests;

public class ProvidersControllerIntegrationTests
{
    [Fact]
    public async Task GetAll_ReturnsProviders()
    {
        var getAllHandler = new Mock<IRequestHandler<GetAllProvidersQuery, IEnumerable<ProviderDto>>>();
        getAllHandler.Setup(x => x.Handle(It.IsAny<GetAllProvidersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ProviderDto(Guid.NewGuid(), "main", "ldap.example.com", 389, "StandardLdap", true)
            });

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<GetAllProvidersQuery, IEnumerable<ProviderDto>>));
            services.AddSingleton(getAllHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var response = await client.GetAsync("/api/v1/providers");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<List<ProviderDto>>();

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("main", result[0].Name);
    }

    [Fact]
    public async Task Create_ReturnsCreatedProvider()
    {
        var created = new ProviderDto(Guid.NewGuid(), "main", "ldap.example.com", 389, "StandardLdap", true);
        var createHandler = new Mock<IRequestHandler<CreateProviderCommand, ProviderDto>>();
        createHandler.Setup(x => x.Handle(It.IsAny<CreateProviderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<CreateProviderCommand, ProviderDto>));
            services.AddSingleton(createHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var response = await client.PostAsJsonAsync("/api/v1/providers", new ProviderCreateRequest
        {
            Name = "main",
            Host = "ldap.example.com",
            Port = 389,
            ProviderType = "StandardLdap",
            IsActive = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProviderDto>();
        Assert.NotNull(payload);
        Assert.Equal(created.Id, payload.Id);
        Assert.Equal("main", payload.Name);
    }

    [Fact]
    public async Task Update_ReturnsUpdatedProvider()
    {
        var providerId = Guid.NewGuid();
        var updated = new ProviderDto(providerId, "main-updated", "ldap2.example.com", 636, "StandardLdap", false);
        var updateHandler = new Mock<IRequestHandler<UpdateProviderCommand, ProviderDto?>>();
        updateHandler.Setup(x => x.Handle(It.IsAny<UpdateProviderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<UpdateProviderCommand, ProviderDto?>));
            services.AddSingleton(updateHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var response = await client.PutAsJsonAsync($"/api/v1/providers/{providerId}", new ProviderUpdateRequest
        {
            Name = "main-updated",
            Host = "ldap2.example.com",
            Port = 636,
            ProviderType = "StandardLdap",
            IsActive = false
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ProviderDto>();
        Assert.NotNull(payload);
        Assert.Equal(providerId, payload.Id);
        Assert.Equal("main-updated", payload.Name);
        Assert.Equal(636, payload.Port);
        Assert.False(payload.IsActive);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenProviderDeleted()
    {
        var providerId = Guid.NewGuid();
        var deleteHandler = new Mock<IRequestHandler<DeleteProviderCommand, bool>>();
        deleteHandler.Setup(x => x.Handle(It.IsAny<DeleteProviderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<DeleteProviderCommand, bool>));
            services.AddSingleton(deleteHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var response = await client.DeleteAsync($"/api/v1/providers/{providerId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}