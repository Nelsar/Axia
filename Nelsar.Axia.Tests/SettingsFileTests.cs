using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Nelsar.Axia.Api.Configuration;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The settings file an installed instance runs with: created by <c>init-settings</c> on every
/// install, upgrade and repair, and read through <c>--SettingsFile</c>. What matters most is what
/// a second run leaves alone — a new Encryption:Key on upgrade would make every stored bind
/// password unreadable.
/// </summary>
public sealed class SettingsFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("axia-settings-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string FilePath => Path.Join(_folder, "appsettings.json");

    private static string? At(JsonObject root, string key)
    {
        JsonNode? node = root;
        foreach (var segment in key.Split(':'))
            node = node?[segment];
        return node?.GetValue<string>();
    }

    [Fact]
    public void AFirstRun_GeneratesKeys_AndKeepsEverythingBesideTheFile()
    {
        var root = new JsonObject();

        Assert.True(SettingsFile.Initialize(root, _folder, []));

        Assert.Equal(48, Convert.FromBase64String(At(root, "Jwt:Key")!).Length);
        Assert.Equal(32, Convert.FromBase64String(At(root, "Encryption:Key")!).Length);
        Assert.Equal("Sqlite", At(root, "Database:Provider"));
        Assert.Equal($"Data Source={Path.Combine(_folder, "nelsar-axia.db")}", At(root, "ConnectionStrings:DefaultConnection"));
        Assert.Equal(Path.Combine(_folder, "logs", "axia-.json"), At(root, "Observability:File:Path"));
        Assert.Equal("http://+:8080", At(root, "Urls"));
    }

    [Fact]
    public void ASecondRun_KeepsTheKeys_AndChangesNothing()
    {
        var root = new JsonObject();
        SettingsFile.Initialize(root, _folder, []);
        var jwt = At(root, "Jwt:Key");
        var encryption = At(root, "Encryption:Key");

        Assert.False(SettingsFile.Initialize(root, _folder, [new("Ldap:Server", "")]));

        Assert.Equal(jwt, At(root, "Jwt:Key"));
        Assert.Equal(encryption, At(root, "Encryption:Key"));
    }

    [Fact]
    public void GivenSettings_AreWritten_OverWhatIsThere_InTheCaseAlreadyUsed()
    {
        var root = JsonNode.Parse("""{ "ldap": { "server": "old.example.org", "Port": 636 }, "Urls": "http://+:9000" }""")!.AsObject();

        SettingsFile.Initialize(root, _folder,
        [
            new("Ldap:Server", "dc01.corp.example.com"),
            new("RoleMappings:GroupDns:Admin", "CN=Axia-Admins,OU=Groups,DC=corp,DC=example,DC=com")
        ]);

        Assert.Equal("dc01.corp.example.com", At(root, "ldap:server"));
        Assert.Equal(636, root["ldap"]!["Port"]!.GetValue<int>());
        Assert.Equal("CN=Axia-Admins,OU=Groups,DC=corp,DC=example,DC=com", At(root, "RoleMappings:GroupDns:Admin"));

        // Set by hand before; the default does not replace it.
        Assert.Equal("http://+:9000", At(root, "Urls"));
    }

    [Fact]
    public void AFileWithComments_IsRead_AndWrittenBack()
    {
        File.WriteAllText(FilePath, """
            {
              // the directory
              "Ldap": { "Server": "dc01", },
            }
            """);

        var root = SettingsFile.Read(FilePath);
        SettingsFile.Initialize(root, _folder, []);
        SettingsFile.Write(FilePath, root);

        Assert.Equal("dc01", At(SettingsFile.Read(FilePath), "Ldap:Server"));
        Assert.False(File.Exists(FilePath + ".new"));
    }

    [Fact]
    public void TheFile_RanksBelowTheEnvironmentAndTheCommandLine()
    {
        File.WriteAllText(FilePath, """{ "SettingsFileTest": { "FromFile": "file", "Overridden": "file" } }""");
        // A key nothing reads, since the environment is shared with every test running alongside.
        const string variable = "SettingsFileTest__Overridden";
        Environment.SetEnvironmentVariable(variable, "environment");
        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddJsonFile(Path.Join(_folder, "absent.json"), optional: true);
            configuration.AddEnvironmentVariables();
            configuration.AddCommandLine(["--SettingsFile", FilePath]);

            SettingsFile.AddTo(configuration);

            Assert.Equal("file", configuration["SettingsFileTest:FromFile"]);
            Assert.Equal("environment", configuration["SettingsFileTest:Overridden"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void ANamedFileThatIsMissing_StopsStartup()
    {
        var configuration = new ConfigurationManager();
        configuration.AddCommandLine(["--SettingsFile", FilePath]);

        var error = Assert.Throws<InvalidOperationException>(() => SettingsFile.AddTo(configuration));

        Assert.Contains("init-settings", error.Message);
    }

    [Fact]
    public void TheCommand_CreatesTheFile_AndASecondRunKeepsIt()
    {
        Assert.Equal(0, InitSettingsCommand.Run(
            ["init-settings", FilePath, "Ldap:BaseDn=DC=corp,DC=example,DC=com", "Ldap:Server="]));
        var first = File.ReadAllText(FilePath);

        Assert.Equal("DC=corp,DC=example,DC=com", At(SettingsFile.Read(FilePath), "Ldap:BaseDn"));
        Assert.Null(SettingsFile.Read(FilePath)["Ldap"]!["Server"]);

        Assert.Equal(0, InitSettingsCommand.Run(["init-settings", FilePath]));
        Assert.Equal(first, File.ReadAllText(FilePath));
    }

    [Fact]
    public void TheCommand_WithoutAPath_PrintsUsage()
    {
        Assert.Equal(2, InitSettingsCommand.Run(["init-settings", "Ldap:Server=dc01"]));
    }
}
