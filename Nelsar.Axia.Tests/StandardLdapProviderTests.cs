using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Identity.Providers;
using Novell.Directory.Ldap;

namespace Nelsar.Axia.Tests;

public class StandardLdapProviderTests
{
    /// <summary>
    /// Points at a port nothing is listening on, so any operation that reaches the directory
    /// fails loudly. That is the point: it lets a test tell "refused before we opened a
    /// connection" apart from "refused by the server".
    /// </summary>
    private static StandardLdapProvider ProviderWithNoDirectory()
    {
        var options = Options.Create(new LdapSettings
        {
            Server = "127.0.0.1",
            Port = 1,
            BaseDn = "DC=example,DC=com"
        });

        return new StandardLdapProvider(
            options, new LdapConnectionFactory(options), NullLogger<StandardLdapProvider>.Instance);
    }

    [Fact]
    public void ADirectoryWithNoServer_IsReportedAsUnavailable_NotAsAnArgumentError()
    {
        var options = Options.Create(new LdapSettings { Server = "", Port = 389, BaseDn = "DC=example,DC=com" });
        var provider = new StandardLdapProvider(
            options, new LdapConnectionFactory(options), NullLogger<StandardLdapProvider>.Instance);

        Assert.Throws<Nelsar.Axia.Contracts.Exceptions.DirectoryUnavailableException>(
            () => provider.Authenticate("someone", "secret"));
        Assert.Throws<Nelsar.Axia.Contracts.Exceptions.DirectoryUnavailableException>(
            () => provider.GetAllUsers());
    }

    [Theory]
    // The directory answered the bind and said no. These stay 401.
    [InlineData(49, true)]  // InvalidCredentials
    [InlineData(48, true)]  // InappropriateAuthentication
    [InlineData(53, true)]  // UnwillingToPerform — AD: disabled / locked / expired / must change password
    // The directory never answered. These must surface as 502, not as a wrong password.
    [InlineData(81, false)] // ServerDown
    [InlineData(85, false)] // LdapTimeout
    [InlineData(91, false)] // ConnectError
    [InlineData(51, false)] // Busy
    [InlineData(52, false)] // Unavailable
    public void IsCredentialRejection_SeparatesARejectedBindFromAnUnreachableServer(int resultCode, bool expected)
    {
        // Novell reports a wrong password and a dead server through the same exception type,
        // so the result code is the only thing that distinguishes "no" from "no answer".
        var exception = new LdapException("bind failed", resultCode, null);

        Assert.Equal(expected, StandardLdapProvider.IsCredentialRejection(exception));
    }

    [Theory]
    // A UPN-shaped login: the account name is what AD would match at bind time, and it is the
    // only thing that finds a user whose entry carries no userPrincipalName attribute.
    [InlineData("jsmith@corp.example.com", "jsmith")]
    [InlineData("first.last@example.org", "first.last")]
    // Not UPN-shaped — searching a broader term than the operator typed is never an improvement.
    [InlineData("jsmith", null)]
    [InlineData("@corp.example.com", null)]
    [InlineData("jsmith@", null)]
    [InlineData("a@b@c", null)]
    [InlineData("", null)]
    public void SplitUpnLocalPart_TakesTheAccountNameOnlyFromAWellFormedUpn(string login, string? expected)
    {
        Assert.Equal(expected, StandardLdapProvider.SplitUpnLocalPart(login));
    }

    [Fact]
    public void CreateGroup_RefusesABlankNameOrAnEmptyMembership_BeforeOpeningAConnection()
    {
        // A groupOfNames must carry a member (RFC 4519 §2.11). Both refusals are ours to make and
        // are worth making early: reaching the directory first would spend a round trip to be told
        // the same thing in the form of an object-class violation. DomainValidationException here
        // rather than DirectoryUnavailableException is what proves nothing was dialled.
        var provider = ProviderWithNoDirectory();

        Assert.Throws<DomainValidationException>(() =>
            provider.CreateGroup("  ", "", "", ["uid=jsmith,DC=example,DC=com"]));

        Assert.Throws<DomainValidationException>(() =>
            provider.CreateGroup("Sales", "", "", []));

        // Blank entries are not members, so a list of them is still an empty group.
        Assert.Throws<DomainValidationException>(() =>
            provider.CreateGroup("Sales", "", "", ["", "   "]));
    }

    [Fact]
    public void UpdateGroup_RefusesToEmptyTheMembership_BeforeRenamingAnything()
    {
        // Ordering matters here and not only for the round trip: the rename runs first, so a
        // membership refused afterwards would leave the group renamed and the caller holding an
        // error — a state nobody asked for.
        var provider = ProviderWithNoDirectory();

        Assert.Throws<DomainValidationException>(() => provider.UpdateGroup(
            "CN=Sales,DC=example,DC=com", "Продажи", null, null, []));
    }
}
