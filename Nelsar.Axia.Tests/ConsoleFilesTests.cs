using System.Net;
using Nelsar.Axia.Api.Configuration;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The API serving the console's files itself, for an installed package or a Windows service,
/// where there is no nginx in front. Its responses have to carry what nginx.conf sends for the
/// console, and the API's own answers must keep theirs.
/// </summary>
public sealed class ConsoleFilesTests : IDisposable
{
    private const string ConsolePolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; "
        + "connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    private readonly string _console = Directory.CreateTempSubdirectory("axia-console-").FullName;

    public ConsoleFilesTests()
    {
        File.WriteAllText(Path.Combine(_console, "index.html"), "<!DOCTYPE html><title>Nelsar.Axia</title>");
        Directory.CreateDirectory(Path.Combine(_console, "assets"));
        File.WriteAllText(Path.Combine(_console, "assets", "index-abc123.js"), "export {};");
    }

    public void Dispose() => Directory.Delete(_console, recursive: true);

    private ApiTestFactory FactoryServingConsole() =>
        new(_ => { }, new Dictionary<string, string?> { ["Console:Path"] = _console });

    [Fact]
    public async Task TheRoot_IsTheConsolesPage_WithNginxsHeaders()
    {
        using var factory = FactoryServingConsole();

        var response = await factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Nelsar.Axia", await response.Content.ReadAsStringAsync());
        Assert.Equal(ConsolePolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Single(response.Headers.GetValues("Permissions-Policy"));

        // Revalidated on every load, so that an upgrade reaches a browser holding the old page.
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task AFingerprintedAsset_MayBeKeptForGood()
    {
        using var factory = FactoryServingConsole();

        var response = await factory.CreateClient().GetAsync("/assets/index-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task TheApi_KeepsItsOwnHeaders_BesideTheConsole()
    {
        using var factory = FactoryServingConsole();

        var response = await factory.CreateClient().GetAsync("/api/v1/providers/selectable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task AnUnknownApiPath_IsNotAnsweredWithTheConsole()
    {
        using var factory = FactoryServingConsole();

        var response = await factory.CreateClient().GetAsync("/api/v1/no-such-thing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task WithoutAConsole_TheRootIsNotFound_AsBefore()
    {
        using var factory = new ApiTestFactory(_ => { });

        var response = await factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public void Unset_AndNothingBesideTheBinaries_ServesTheApiAlone()
    {
        var binaries = Directory.CreateTempSubdirectory("axia-bin-").FullName;
        try
        {
            Assert.Null(new ConsoleOptions().ResolveRoot(binaries));
        }
        finally
        {
            Directory.Delete(binaries, recursive: true);
        }
    }

    [Fact]
    public void Unset_FindsWwwrootBesideTheBinaries()
    {
        var binaries = Directory.CreateTempSubdirectory("axia-bin-").FullName;
        try
        {
            var wwwroot = Directory.CreateDirectory(Path.Combine(binaries, "wwwroot")).FullName;
            File.WriteAllText(Path.Join(wwwroot, "index.html"), "");

            Assert.Equal(wwwroot, new ConsoleOptions().ResolveRoot(binaries));
        }
        finally
        {
            Directory.Delete(binaries, recursive: true);
        }
    }

    [Fact]
    public void ARelativePath_IsTakenFromTheBinariesFolder()
    {
        var binaries = Path.GetDirectoryName(_console)!;

        Assert.Equal(_console, new ConsoleOptions { Path = Path.GetFileName(_console) }.ResolveRoot(binaries));
    }

    [Fact]
    public void APathWithoutAConsole_StopsStartup_SayingWhichSetting()
    {
        var empty = Directory.CreateTempSubdirectory("axia-empty-").FullName;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => new ConsoleOptions { Path = empty }.ResolveRoot(AppContext.BaseDirectory));

            Assert.Contains("Console:Path", error.Message);
            Assert.Contains("index.html", error.Message);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }
}
