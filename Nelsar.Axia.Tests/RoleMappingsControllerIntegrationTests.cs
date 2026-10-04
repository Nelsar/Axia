using MediatR;
using Nelsar.Axia.Application.Features.RoleMappings.Queries;
using Nelsar.Axia.Application.Features.RoleMappings.Commands;
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
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Tests;

public class RoleMappingsControllerIntegrationTests
{
    [Fact]
    public async Task GetAll_ReturnsMappings_ForAdmin()
    {
        var getAllHandler = new Mock<IRequestHandler<GetAllRoleMappingsQuery, IEnumerable<RoleMappingDto>>>();
        getAllHandler.Setup(x => x.Handle(It.IsAny<GetAllRoleMappingsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RoleMappingDto(Guid.NewGuid(), null, "Administrators", "Admin", 0) });

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<GetAllRoleMappingsQuery, IEnumerable<RoleMappingDto>>));
            services.AddSingleton(getAllHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.GetAsync("/api/v1/role-mappings");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<List<RoleMappingDto>>();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Administrators", result[0].GroupName);
    }

    [Fact]
    public async Task GetAll_ReturnsForbidden_WithoutThePermission()
    {

        using var factory = new ApiTestFactory(services =>
        {
            // Signed in, but holding nothing — the endpoint must refuse on the permission,
            // not on a role name it no longer looks at.
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver>(_ => new FixedPermissionResolver());
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.User));

        var response = await client.GetAsync("/api/v1/role-mappings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsCreatedMapping()
    {
        var created = new RoleMappingDto(Guid.NewGuid(), null, "ITStaff", "UserEditor", 20);
        var createHandler = new Mock<IRequestHandler<CreateRoleMappingCommand, RoleMappingDto>>();
        createHandler.Setup(x => x.Handle(It.IsAny<CreateRoleMappingCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(created);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<CreateRoleMappingCommand, RoleMappingDto>));
            services.AddSingleton(createHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.PostAsJsonAsync("/api/v1/role-mappings", new RoleMappingCreateRequest
        {
            GroupName = "ITStaff",
            RoleName = "UserEditor",
            Priority = 20
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RoleMappingDto>();
        Assert.NotNull(payload);
        Assert.Equal("ITStaff", payload.GroupName);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenDeleted()
    {
        var mappingId = Guid.NewGuid();
        var deleteHandler = new Mock<IRequestHandler<DeleteRoleMappingCommand, bool>>();
        deleteHandler.Setup(x => x.Handle(It.IsAny<DeleteRoleMappingCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<DeleteRoleMappingCommand, bool>));
            services.AddSingleton(deleteHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.DeleteAsync($"/api/v1/role-mappings/{mappingId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
