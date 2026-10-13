using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Identity.Providers;

namespace Nelsar.Axia.Tests;

/// <summary>
/// New and moved units, groups and computers stay inside the base DN.
///
/// Where one went used to be whatever parent DN the request named, so anyone allowed to manage them
/// could write one anywhere the bind account could — into AD's built-in containers, or another
/// team's subtree.
/// </summary>
public class BaseDnConfinementTests
{
    private const string BaseDn = "DC=example,DC=com";
    private const string Outside = "OU=Elsewhere,DC=other,DC=com";
    private const string Inside = "OU=Finance,DC=example,DC=com";

    [Theory]
    [InlineData("OU=Finance,DC=example,DC=com", true)]
    [InlineData("DC=example,DC=com", true)]                      // the base itself
    [InlineData("ou=finance, dc=EXAMPLE , dc=com", true)]        // case and spacing
    [InlineData("OU = Finance,DC = example,DC = com", true)]
    [InlineData(@"OU=Finance\, North,DC=example,DC=com", true)]  // an escaped comma is not a separator
    [InlineData("OU=Finance,DC=notexample,DC=com", false)]       // ends with the same text, not the same entries
    [InlineData("OU=Finance,DC=example,DC=org", false)]
    [InlineData("DC=com", false)]                                // above the base
    public void IsWithin_ComparesComponents_NotText(string dn, bool expected)
    {
        Assert.Equal(expected, DistinguishedName.Create(dn).IsWithin(DistinguishedName.Create(BaseDn)));
    }

    /// <summary>
    /// A provider pointed at a port nothing listens on: refused before a connection is opened
    /// shows as DomainValidationException, let through to the directory as
    /// DirectoryUnavailableException — the same distinction StandardLdapProviderTests draws.
    /// </summary>
    private static StandardLdapProvider Provider(string baseDn = BaseDn)
    {
        var options = Options.Create(new LdapSettings { Server = "127.0.0.1", Port = 1, BaseDn = baseDn });
        return new StandardLdapProvider(options, new LdapConnectionFactory(options), NullLogger<StandardLdapProvider>.Instance);
    }

    private static readonly string[] Members = ["CN=someone,DC=example,DC=com"];

    public static TheoryData<string> Writes =>
        ["create unit", "move unit", "create group", "move group", "create computer", "move computer"];

    private static void Write(string write, StandardLdapProvider provider, string parent)
    {
        switch (write)
        {
            case "create unit": provider.CreateOrganizationalUnit("Finance", parent, ""); break;
            case "move unit": provider.UpdateOrganizationalUnit("OU=Finance,DC=example,DC=com", null, parent, null); break;
            case "create group": provider.CreateGroup("Editors", parent, "", Members); break;
            case "move group": provider.UpdateGroup("CN=Editors,DC=example,DC=com", null, parent, null, null); break;
            case "create computer": provider.CreateComputer("WS01", parent, "", "ws01.example.com"); break;
            case "move computer": provider.UpdateComputer("CN=WS01,DC=example,DC=com", null, parent, null, null, null); break;
            default: throw new ArgumentOutOfRangeException(nameof(write), write, null);
        }
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public void AParentOutsideTheBase_IsRefused_BeforeTheDirectoryIsAsked(string write)
    {
        var refused = Assert.Throws<DomainValidationException>(() => Write(write, Provider(), Outside));

        Assert.Contains(BaseDn, refused.Message);
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public void AParentInsideTheBase_GoesOnToTheDirectory(string write)
    {
        Assert.Throws<DirectoryUnavailableException>(() => Write(write, Provider(), Inside));
    }

    [Fact]
    public void WithNoBaseDn_NothingIsConfined()
    {
        // The whole directory is then the managed part; the write goes on to the directory.
        Assert.Throws<DirectoryUnavailableException>(
            () => Provider(baseDn: "").CreateOrganizationalUnit("Finance", Outside, ""));
    }
}
