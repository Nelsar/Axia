using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Application.Features.Users.Commands;
using MediatR;
using Nelsar.Axia.Domain.Entities;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Infrastructure.Persistence;
using Xunit;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Tests;

public class UsersControllerIntegrationTests
{
    [Fact]
    public async Task GetAll_ReturnsUsersFromLdapService()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Complete(new[]
            {
                new LdapUserInfo
                {
                    Email = "alice@example.com",
                    DisplayName = "Alice"
                },
                new LdapUserInfo
                {
                    Email = "bob@example.com",
                    DisplayName = "Bob"
                }
            }));
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>())).ReturnsAsync(UserRoles.User);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var response = await client.GetAsync("/api/v1/users");

        response.EnsureSuccessStatusCode();
        var users = await response.Content.ReadFromJsonAsync<UserListingResponse>();

        Assert.NotNull(users);
        Assert.True(users.IsComplete);
        Assert.Collection(users.Items,
            user => Assert.Equal("alice@example.com", user.Email),
            user => Assert.Equal("bob@example.com", user.Email));
    }

    [Fact]
    public async Task GetAll_ReturnsUnauthorized_WithoutToken()
    {
        var ldapServiceMock = new Mock<ILdapService>();

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsForbidden_WithoutTheCreatePermission()
    {
        var ldapServiceMock = new Mock<ILdapService>();

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
            // Holds everything except the one this endpoint asks for.
            services.RemoveAll(typeof(IPermissionResolver));
            services.AddScoped<IPermissionResolver>(_ => new FixedPermissionResolver(
                Permissions.All.Where(p => p != Permissions.UsersCreate).ToArray()));
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.User));

        var response = await client.PostAsJsonAsync("/api/v1/users", new UserCreateRequest
        {
            Email = "newuser@example.com",
            Name = "New User",
            Password = "Password123!"
        });

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_ReturnsAccepted_WhenBulkJobIsQueued()
    {
        var enqueueHandler = new Mock<IRequestHandler<EnqueueBulkUserJobCommand, string>>();
        enqueueHandler.Setup(x => x.Handle(It.IsAny<EnqueueBulkUserJobCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("job123");

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<EnqueueBulkUserJobCommand, string>));
            services.AddSingleton(enqueueHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var response = await client.PostAsJsonAsync("/api/v1/users/bulk", new[]
        {
            new BulkUserRequest
            {
                Operation = "create",
                Email = "bulk@example.com",
                Name = "Bulk User",
                Password = "Pass123!"
            }
        });

        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(payload);
        Assert.Equal("job123", payload["jobId"]);
    }

    [Fact]
    public async Task BulkSync_ReturnsMultiStatus_WithPerItemReport()
    {
        var bulkHandler = new Mock<IRequestHandler<ProcessBulkUsersCommand, BulkOperationResponse<BulkUserResult>>>();
        bulkHandler.Setup(x => x.Handle(It.IsAny<ProcessBulkUsersCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkOperationResponse<BulkUserResult>
            {
                Items = new[]
                {
                    new BulkUserResult { Email = "ok@example.com", Status = "completed", Message = "User created successfully." },
                    new BulkUserResult { Email = "dup@example.com", Status = "failed", Message = "User already exists." }
                },
                CompletedCount = 1,
                FailedCount = 1
            });

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<ProcessBulkUsersCommand, BulkOperationResponse<BulkUserResult>>));
            services.AddSingleton(bulkHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.PostAsJsonAsync("/api/v1/users/bulk/sync", new[]
        {
            new BulkUserRequest { Operation = "create", Email = "ok@example.com", Name = "Ok User", Password = "Pass123!" },
            new BulkUserRequest { Operation = "create", Email = "dup@example.com", Name = "Dup User", Password = "Pass123!" }
        });

        Assert.Equal(207, (int)response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BulkOperationResponse<BulkUserResult>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload.CompletedCount);
        Assert.Equal(1, payload.FailedCount);
        Assert.Collection(payload.Items,
            item => Assert.Equal("completed", item.Status),
            item => Assert.Equal("failed", item.Status));
    }

    [Fact]
    public async Task BulkSync_ReturnsBadRequest_WhenEmpty()
    {
        // No stub: the real handler is never reached. ProcessBulkUsersCommandValidator rejects
        // an empty batch in the pipeline, and GlobalExceptionHandler renders that as the 400
        // this endpoint used to write by hand.
        using var factory = new ApiTestFactory(_ => { });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.PostAsJsonAsync("/api/v1/users/bulk/sync", Array.Empty<BulkUserRequest>());

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Generate_ReturnsPatternPreview()
    {
        var generateHandler = new Mock<IRequestHandler<GenerateUserPatternsQuery, PatternGeneratorResponse>>();
        generateHandler.Setup(x => x.Handle(It.IsAny<GenerateUserPatternsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatternGeneratorResponse
            {
                Items = new[] { "node-01", "node-02" },
                Count = 2
            });

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<GenerateUserPatternsQuery, PatternGeneratorResponse>));
            services.AddSingleton(generateHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var response = await client.PostAsJsonAsync("/api/v1/users/generate", new PatternGeneratorRequest
        {
            Pattern = "node-{01..02}",
            MaxItems = 10
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PatternGeneratorResponse>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains("node-01", result.Items);
    }

    [Fact]
    public async Task Search_ReturnsPagedUsersFromManagementService()
    {
        var searchHandler = new Mock<IRequestHandler<SearchUsersQuery, PagedResponse<UserDto>>>();
        searchHandler.Setup(x => x.Handle(
                It.Is<SearchUsersQuery>(q => q.Filter == "alice" && q.PageSize == 10 && q.PageToken == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResponse<UserDto>
            {
                Items = new[]
                {
                    new UserDto
                    {
                        Id = Guid.NewGuid(),
                        Email = "alice@example.com",
                        Name = "Alice",
                        Role = UserRoles.User
                    }
                },
                PageSize = 10,
                PageToken = null,
                HasMore = false
            });

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<SearchUsersQuery, PagedResponse<UserDto>>));
            services.AddSingleton(searchHandler.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var response = await client.GetAsync("/api/v1/users/search?filter=alice&pageSize=10");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PagedResponse<UserDto>>();

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("alice@example.com", result.Items.Single().Email);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task Create_ReturnsCreatedUser_WhenLdapServiceCreatesUser()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("newuser@example.com"))
            .ReturnsAsync((LdapUserInfo?)null);
        ldapServiceMock.Setup(x => x.CreateUserAsync("New User", "newuser@example.com", "Password123!"))
            .Returns(Task.CompletedTask);
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>())).ReturnsAsync(UserRoles.User);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var request = new UserCreateRequest
        {
            Email = "newuser@example.com",
            Name = "New User",
            Password = "Password123!"
        };

        var response = await client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(created);
        Assert.Equal(request.Email, created.Email);
        Assert.Equal(request.Name, created.Name);
        Assert.Equal(UserRoles.User, created.Role);
    }

    [Fact]
    public async Task AuthLogin_ReturnsUnauthorized_ForInvalidCredentials()
    {
        var authHandler = new Mock<IRequestHandler<AuthenticateCommand, AuthenticateResponse?>>();
        authHandler.Setup(x => x.Handle(It.IsAny<AuthenticateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthenticateResponse?)null);

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>()))
            .ReturnsAsync(UserRoles.User);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<AuthenticateCommand, AuthenticateResponse?>));
            services.AddSingleton(authHandler.Object);
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        var request = new AuthenticateRequest
        {
            Email = "invalid@example.com",
            Password = "wrong"
        };

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsUser_WhenUserExists()
    {
        var userId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Complete(new[]
            {
                new LdapUserInfo
                {
                    Dn = "cn=alice,ou=users,dc=example,dc=com",
                    Email = "alice@example.com",
                    DisplayName = "Alice"
                }
            }));
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>())).ReturnsAsync(UserRoles.User);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        // First load users to populate cache
        var getAllResponse = await client.GetAsync("/api/v1/users");
        getAllResponse.EnsureSuccessStatusCode();
        var allUsers = await getAllResponse.Content.ReadFromJsonAsync<UserListingResponse>();
        var userId_actual = allUsers?.Items.FirstOrDefault()?.Id ?? Guid.NewGuid();

        // Then get by ID
        var response = await client.GetAsync($"/api/v1/users/{userId_actual}");

        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(user);
        Assert.Equal("alice@example.com", user.Email);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Complete(Array.Empty<LdapUserInfo>()));
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>())).ReturnsAsync(UserRoles.User);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var nonExistentId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/v1/users/{nonExistentId}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsUpdatedUser_WhenUserExists()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Complete(new[]
            {
                new LdapUserInfo
                {
                    Dn = "cn=alice,ou=users,dc=example,dc=com",
                    Email = "alice@example.com",
                    DisplayName = "Alice"
                }
            }));
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>())).ReturnsAsync(UserRoles.User);
        ldapServiceMock.Setup(x => x.UpdateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        // First load users to populate cache
        var getAllResponse = await client.GetAsync("/api/v1/users");
        getAllResponse.EnsureSuccessStatusCode();
        var allUsers = await getAllResponse.Content.ReadFromJsonAsync<UserListingResponse>();
        var userId = allUsers?.Items.FirstOrDefault()?.Id ?? Guid.NewGuid();

        // Then update
        var updateRequest = new UserUpdateRequest
        {
            Name = "Alice Updated",
            Email = "alice_new@example.com",
            Password = "NewPassword123!"
        };

        var response = await client.PutAsJsonAsync($"/api/v1/users/{userId}", updateRequest);

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(updated);
        Assert.Equal("Alice Updated", updated.Name);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenUserExists()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Complete(new[]
            {
                new LdapUserInfo
                {
                    Dn = "cn=bob,ou=users,dc=example,dc=com",
                    Email = "bob@example.com",
                    DisplayName = "Bob"
                }
            }));
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>())).ReturnsAsync(UserRoles.User);
        ldapServiceMock.Setup(x => x.DeleteUserAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        // First load users to populate cache
        var getAllResponse = await client.GetAsync("/api/v1/users");
        getAllResponse.EnsureSuccessStatusCode();
        var allUsers = await getAllResponse.Content.ReadFromJsonAsync<UserListingResponse>();
        var userId = allUsers?.Items.FirstOrDefault()?.Id ?? Guid.NewGuid();

        // Then delete
        var response = await client.DeleteAsync($"/api/v1/users/{userId}");

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Complete(Array.Empty<LdapUserInfo>()));
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>())).ReturnsAsync(UserRoles.User);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        var nonExistentId = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/v1/users/{nonExistentId}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AuthRegister_IsNotThere_UnlessADeploymentAsksForIt()
    {
        // The default, and the reason the switch exists. This endpoint writes to the directory
        // with no authentication in front of it: left open, anyone who can reach the port can
        // create accounts in the organisation's AD. The rate limiter caps that at a few a minute,
        // which makes it slow rather than impossible.
        //
        // 404 rather than 403 on purpose — see the comment in AuthController.Register.
        var ldap = new Mock<ILdapService>();

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldap.Object);
        });

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new UserCreateRequest
        {
            Email = "intruder@example.com",
            Name = "Intruder",
            Password = "Password123!"
        });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        // Nothing reached the directory — the refusal happens before the use case runs.
        ldap.Verify(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AuthRegister_ReturnsCreated_WhenRegistrationSucceeds()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync(It.IsAny<string>()))
            .ReturnsAsync((LdapUserInfo?)null);
        ldapServiceMock.Setup(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>())).ReturnsAsync(UserRoles.User);

        // Self-registration is off unless a deployment asks for it, so this test has to ask.
        using var factory = new ApiTestFactory(
            services =>
            {
                services.RemoveAll(typeof(ILdapService));
                services.AddSingleton(ldapServiceMock.Object);
            },
            new Dictionary<string, string?> { ["Auth:AllowSelfRegistration"] = "true" });

        var client = factory.CreateClient();
        var request = new UserCreateRequest
        {
            Email = "newuser@example.com",
            Name = "New User",
            Password = "Password123!"
        };

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(created);
        Assert.Equal(request.Email, created.Email);
    }

    [Fact]
    public async Task AuthLogin_ReturnsOk_WhenCredentialsAreValid()
    {
        var authHandler = new Mock<IRequestHandler<AuthenticateCommand, AuthenticateResponse?>>();
        var authResponse = new AuthenticateResponse
        {
            Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test.test",
            User = new UserDto
            {
                Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
                Email = "valid@example.com",
                Name = "Valid User",
                Role = UserRoles.User
            }
        };

        authHandler.Setup(x => x.Handle(It.IsAny<AuthenticateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResponse);

        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserRoleAsync(It.IsAny<string>()))
            .ReturnsAsync(UserRoles.User);

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(IRequestHandler<AuthenticateCommand, AuthenticateResponse?>));
            services.AddSingleton(authHandler.Object);
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        var request = new AuthenticateRequest
        {
            Email = "valid@example.com",
            Password = "correct_password"
        };

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Token);
        Assert.Equal("valid@example.com", result.User.Email);
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenTheUserAlreadyExistsInTheDirectory()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("existing@example.com"))
            .ReturnsAsync(new LdapUserInfo { Email = "existing@example.com", DisplayName = "Existing" });

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));

        var response = await client.PostAsJsonAsync("/api/v1/users", new UserCreateRequest
        {
            Email = "existing@example.com",
            Name = "Existing",
            Password = "Password1!"
        });

        // The controller no longer catches anything here — the status comes from the domain
        // exception type via GlobalExceptionHandler.
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already exists", await response.Content.ReadAsStringAsync());
    }

    private static ApiTestFactory FactoryWithExistingUser()
    {
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetUserDetailsAsync("existing@example.com"))
            .ReturnsAsync(new LdapUserInfo { Email = "existing@example.com", DisplayName = "Existing" });

        return new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });
    }

    [Theory]
    [InlineData(null, "already exists")]
    [InlineData("en", "already exists")]
    [InlineData("ru", "уже существует")]
    // Quality values and region subtags are ordinary Accept-Language, and must resolve to ru.
    [InlineData("ru-RU,ru;q=0.9,en;q=0.8", "уже существует")]
    // A language we do not carry falls back to the neutral resource rather than failing.
    [InlineData("ja-JP", "already exists")]
    public async Task ErrorBody_IsLocalisedFromAcceptLanguage(string? acceptLanguage, string expected)
    {
        using var factory = FactoryWithExistingUser();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        if (acceptLanguage != null)
            client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);

        var response = await client.PostAsJsonAsync("/api/v1/users", new UserCreateRequest
        {
            Email = "existing@example.com",
            Name = "Existing",
            Password = "Password1!"
        });

        // This body is written by GlobalExceptionHandler while an exception unwinds the pipeline.
        // It only comes out translated if UseRequestLocalization sits outside UseExceptionHandler.
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null, "An unexpected error occurred")]
    [InlineData("en", "An unexpected error occurred")]
    [InlineData("ru", "Произошла непредвиденная ошибка")]
    [InlineData("de", "Ein unerwarteter Fehler ist aufgetreten")]
    public async Task UnexpectedErrorBody_IsLocalisedFromAcceptLanguage(string? acceptLanguage, string expected)
    {
        // Unlike a domain exception's message, which was resolved where it was thrown, the 500
        // text is looked up by GlobalExceptionHandler itself — outside the request's culture. It
        // came out in the host's own language whatever the client asked for.
        var ldapServiceMock = new Mock<ILdapService>();
        ldapServiceMock.Setup(x => x.GetAllUsersAsync()).ThrowsAsync(new InvalidOperationException("boom"));

        using var factory = new ApiTestFactory(services =>
        {
            services.RemoveAll(typeof(ILdapService));
            services.AddSingleton(ldapServiceMock.Object);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        if (acceptLanguage != null)
            client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expected, body);
        Assert.DoesNotContain("boom", body);
    }

    [Theory]
    [InlineData("en", "must contain at least one item")]
    [InlineData("ru", "хотя бы один элемент")]
    public async Task ControllerWrittenBody_IsAlsoLocalised(string acceptLanguage, string expected)
    {
        using var factory = FactoryWithExistingUser();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.GenerateToken(UserRoles.Admin));
        client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);

        // Returned straight from the action, not through the exception handler — the other path.
        var response = await client.PostAsJsonAsync("/api/v1/users/bulk", Array.Empty<BulkUserRequest>());

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
    }
}
