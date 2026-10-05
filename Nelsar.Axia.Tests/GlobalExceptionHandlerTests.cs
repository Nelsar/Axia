using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nelsar.Axia.Api.Middleware;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Exceptions;

namespace Nelsar.Axia.Tests;

public class GlobalExceptionHandlerTests
{
    private static async Task<(int Status, string Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/users";

        using var body = new MemoryStream();
        context.Response.Body = body;

        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(body).ReadToEndAsync());
    }

    [Fact]
    public async Task ValidationFailure_Becomes400_AndKeepsTheMessage()
    {
        var (status, body) = await HandleAsync(
            new DomainValidationException("'nope' is not a valid email address."));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("'nope' is not a valid email address.", body);
    }

    [Fact]
    public async Task Conflict_Becomes409_AndKeepsTheMessage()
    {
        var (status, body) = await HandleAsync(
            new DomainConflictException("User with this email already exists in LDAP."));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("already exists", body);
    }

    [Fact]
    public async Task AnythingElse_Becomes500_AndLeaksNothing()
    {
        var (status, body) = await HandleAsync(
            new InvalidOperationException("ldap://internal-dc-01.corp:389 refused the connection"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("internal-dc-01", body);
        Assert.Contains("An unexpected error occurred", body);
    }

    [Fact]
    public async Task ArgumentNullException_Becomes500_NotAConflict()
    {
        // ArgumentNullException derives from ArgumentException, so while "already exists" was
        // signalled with a plain ArgumentException the controllers turned a genuine null-argument
        // bug into a 409 — telling the client to fix a request that was never the problem.
        var (status, _) = await HandleAsync(new ArgumentNullException("someParameter"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
    }

    [Fact]
    public async Task NotFound_Becomes404_AndKeepsTheMessage()
    {
        var (status, body) = await HandleAsync(
            new DomainNotFoundException("User 'ghost' not found in LDAP."));

        // Previously this rode on InvalidOperationException and came back as 409 Conflict —
        // a client could not tell "no such user" from "that user already exists".
        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Contains("not found", body);
    }

    [Fact]
    public async Task DirectoryFailure_Becomes502_NotAClientError()
    {
        var (status, body) = await HandleAsync(
            new DirectoryUnavailableException(
                "The directory could not be reached.",
                new InvalidOperationException("ldap://internal-dc-01.corp:389 timed out")));

        Assert.Equal(StatusCodes.Status502BadGateway, status);
        Assert.Contains("could not be reached", body);
        // Our own wording is returned; the underlying client library's text is not.
        Assert.DoesNotContain("internal-dc-01", body);
    }
}
