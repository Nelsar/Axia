using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Features.Users;
using Nelsar.Axia.Application.Features.Users.Commands;
using Nelsar.Axia.Application.Features.Users.Queries;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.Services;
using Nelsar.Axia.Infrastructure.Identity;
using Novell.Directory.Ldap;
using Xunit;
using Nelsar.Axia.Contracts.Common;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Covers the two halves of exposing a directory entry as it actually is: the fields UserDto
/// reports about an account, and the raw attribute list behind them.
/// </summary>
public class UserAttributesTests
{
    private static ISender CreateSender(ILdapService ldapService, IUserLookupRepository? lookup = null)
    {
        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(x => x.GenerateToken(It.IsAny<UserDto>())).Returns("test-token");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddApplicationLayer();
        services.WithAuthorizedCaller();

        services.AddScoped<UserDirectory>();
        services.AddSingleton(ldapService);
        services.AddSingleton(tokenService.Object);
        services.AddSingleton(new Mock<IBulkJobQueue>().Object);
        services.AddSingleton(new Mock<IBulkJobStore>().Object);
        services.AddSingleton(new Mock<IDomainEventPublisher>().Object);
        services.AddSingleton(lookup ?? new Mock<IUserLookupRepository>().Object);

        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static Mock<ILdapService> DirectoryHolding(params LdapUserInfo[] users)
    {
        var ldap = new Mock<ILdapService>();
        ldap.Setup(x => x.GetAllUsersAsync())
            .ReturnsAsync(DirectoryUserListing.Complete(users));
        return ldap;
    }

    // An account as OpenLDAP commonly holds it: no mail attribute at all, and a uid that is a
    // person's name rather than a login-shaped string. This is the entry that used to render as
    // "Email: Anuar Mukatov".
    private static LdapUserInfo AccountWithoutMail() => new()
    {
        Dn = "uid=Anuar Mukatov,ou=people,dc=nelsar,dc=kz",
        Username = "Anuar Mukatov",
        DisplayName = "Anuar Mukatov",
        Email = string.Empty,
        Groups = ["cn=admins,ou=groups,dc=nelsar,dc=kz"]
    };

    [Fact]
    public async Task AnAccountWithNoMailAttribute_ReportsAnEmptyEmail_NotItsLogin()
    {
        var sender = CreateSender(DirectoryHolding(AccountWithoutMail()).Object);

        var user = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        // The whole point of splitting the fields: a caller reading Email gets the mail
        // attribute or nothing, and can tell which. Before, it got the cn and could not.
        Assert.Equal(string.Empty, user.Email);
        Assert.Equal("Anuar Mukatov", user.Username);
    }

    [Fact]
    public async Task SplittingEmailFromLogin_DoesNotChangeTheDerivedId()
    {
        var sender = CreateSender(DirectoryHolding(AccountWithoutMail()).Object);

        var user = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        // The id still derives from mail-or-login, exactly as it did when that value was also
        // what Email reported. If this ever changes, every id already handed out — in tokens,
        // in the lookup index, in a bookmarked URL — silently stops resolving.
        Assert.Equal(DeterministicIdFactory.BuildUserId("Anuar Mukatov"), user.Id);
    }

    [Fact]
    public async Task AUserCarriesItsDirectoryIdentity_DnAndGroups()
    {
        var sender = CreateSender(DirectoryHolding(AccountWithoutMail()).Object);

        var user = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        // Both were already read out of the directory and thrown away at the mapping step.
        Assert.Equal("uid=Anuar Mukatov,ou=people,dc=nelsar,dc=kz", user.Dn);
        Assert.Equal(["cn=admins,ou=groups,dc=nelsar,dc=kz"], user.Groups);
    }

    [Fact]
    public async Task DeletingAnAccountWithMail_AddressesItsEntry_NotItsMail()
    {
        var entry = new LdapUserInfo
        {
            Dn = "uid=amukatov,ou=people,dc=nelsar,dc=kz",
            Username = "amukatov",
            DisplayName = "Anuar Mukatov",
            Email = "a.mukatov@nelsar.kz"
        };

        var ldap = DirectoryHolding(entry);
        ldap.Setup(x => x.DeleteUserAsync(It.IsAny<string>())).ReturnsAsync(true);
        var sender = CreateSender(ldap.Object);

        var user = (await sender.Send(new GetAllUsersQuery())).Items.Single();
        await sender.Send(new DeleteUserCommand(user.Id));

        // By the DN of the entry the id resolved to. Writes used to go by the address, which the
        // directory was then searched for again — and mail is not unique, so the search could
        // return a different entry than the one this id names.
        ldap.Verify(x => x.DeleteUserAsync("uid=amukatov,ou=people,dc=nelsar,dc=kz"), Times.Once);
    }

    [Fact]
    public async Task DeletingAnAccountWithNoMail_AddressesItsEntry_NotAnEmptyString()
    {
        var ldap = DirectoryHolding(AccountWithoutMail());
        ldap.Setup(x => x.DeleteUserAsync(It.IsAny<string>())).ReturnsAsync(true);
        var sender = CreateSender(ldap.Object);

        var user = (await sender.Send(new GetAllUsersQuery())).Items.Single();
        await sender.Send(new DeleteUserCommand(user.Id));

        // Email is empty for this entry. Its DN is not, and is what the delete is addressed by.
        ldap.Verify(x => x.DeleteUserAsync("uid=Anuar Mukatov,ou=people,dc=nelsar,dc=kz"), Times.Once);
    }

    [Fact]
    public async Task GetAttributesAsync_ReadsTheEntryByTheSameAddressTheIdDerivesFrom()
    {
        var ldap = DirectoryHolding(AccountWithoutMail());
        ldap.Setup(x => x.GetUserAttributesAsync("Anuar Mukatov"))
            .ReturnsAsync(new DirectoryEntryAttributes(
                "uid=Anuar Mukatov,ou=people,dc=nelsar,dc=kz",
                [new DirectoryAttribute("cn", ["Anuar Mukatov"])]));

        var sender = CreateSender(ldap.Object);
        var user = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        var attributes = await sender.Send(new GetUserAttributesQuery(user.Id));

        Assert.NotNull(attributes);
        Assert.Equal("uid=Anuar Mukatov,ou=people,dc=nelsar,dc=kz", attributes!.Dn);
        Assert.Equal("cn", attributes.Attributes.Single().Name);
    }

    [Fact]
    public async Task GetAttributesAsync_IsNull_WhenTheEntryVanishedBetweenTheTwoReads()
    {
        var ldap = DirectoryHolding(AccountWithoutMail());
        ldap.Setup(x => x.GetUserAttributesAsync(It.IsAny<string>()))
            .ReturnsAsync((DirectoryEntryAttributes?)null);

        var sender = CreateSender(ldap.Object);
        var user = (await sender.Send(new GetAllUsersQuery())).Items.Single();

        // Null, not an empty list: the controller turns this into 404. An empty attribute list
        // would read as "this account exists and carries nothing", which no real entry does.
        Assert.Null(await sender.Send(new GetUserAttributesQuery(user.Id)));
    }

    [Fact]
    public void CredentialAttributes_AreListedByName_WithTheirValuesWithheld()
    {
        var attributes = new LdapAttributeSet
        {
            new LdapAttribute("userPassword", "{SSHA}s3cr3thash"),
            new LdapAttribute("cn", "Anuar Mukatov")
        };

        var read = DirectoryAttributeReader.Read(attributes);

        var password = read.Single(a => a.Name == "userPassword");
        Assert.True(password.IsRedacted);
        Assert.Empty(password.Values);

        // Redaction is per-attribute, not a reason to drop the whole entry.
        Assert.Equal(["Anuar Mukatov"], read.Single(a => a.Name == "cn").Values);
    }

    [Fact]
    public void RedactionIgnoresAttributeNameCasing()
    {
        // Directories are inconsistent about this: AD reports unicodePwd, some tools uppercase
        // it. A case-sensitive list would leak the value the one time the casing differed.
        var attributes = new LdapAttributeSet { new LdapAttribute("UNICODEPWD", "hash") };

        Assert.True(DirectoryAttributeReader.Read(attributes).Single().IsRedacted);
    }

    [Fact]
    public void BytesThatAreNotText_AreBase64Encoded_AndMarkedBinary()
    {
        // The leading bytes of a real objectSid — invalid UTF-8, and full of control bytes.
        byte[] sid = [0x01, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0xFF, 0xFE];
        var attributes = new LdapAttributeSet { new LdapAttribute("objectSid", sid) };

        var read = DirectoryAttributeReader.Read(attributes).Single();

        Assert.True(read.IsBinary);
        Assert.Equal(Convert.ToBase64String(sid), read.Values.Single());
    }

    [Fact]
    public void TextValues_AreLeftAsText()
    {
        // Including non-ASCII: valid UTF-8 is text, and base64-ing Cyrillic would make an
        // ordinary description unreadable for no reason.
        var attributes = new LdapAttributeSet { new LdapAttribute("description", "Бухгалтерия") };

        var read = DirectoryAttributeReader.Read(attributes).Single();

        Assert.False(read.IsBinary);
        Assert.Equal("Бухгалтерия", read.Values.Single());
    }

    [Fact]
    public void AttributesComeBackSorted_SoTheSameEntryRendersTheSameTwiceRunning()
    {
        var attributes = new LdapAttributeSet
        {
            new LdapAttribute("sn", "Mukatov"),
            new LdapAttribute("cn", "Anuar Mukatov"),
            new LdapAttribute("mail", "a.mukatov@nelsar.kz")
        };

        var names = DirectoryAttributeReader.Read(attributes).Select(a => a.Name).ToArray();

        Assert.Equal(["cn", "mail", "sn"], names);
    }

    [Fact]
    public void AMultiValuedAttribute_KeepsAllItsValues()
    {
        var attributes = new LdapAttributeSet
        {
            new LdapAttribute("memberOf", ["cn=admins,dc=nelsar,dc=kz", "cn=vpn,dc=nelsar,dc=kz"])
        };

        var read = DirectoryAttributeReader.Read(attributes).Single();

        Assert.Equal(2, read.Values.Count);
    }
}
