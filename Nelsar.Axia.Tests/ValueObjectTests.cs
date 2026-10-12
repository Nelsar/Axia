using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Tests;

public class EmailTests
{
    [Fact]
    public void Create_NormalisesWhitespaceAndCasing()
    {
        var email = Email.Create("  Ivan.Ivanov@Example.COM  ");

        Assert.Equal("ivan.ivanov@example.com", email.Value);
    }

    [Fact]
    public void Create_SplitsLocalAndDomainParts()
    {
        var email = Email.Create("Ivan.Ivanov@Example.com");

        Assert.Equal("ivan.ivanov", email.LocalPart);
        Assert.Equal("example.com", email.DomainPart);
    }

    [Fact]
    public void Addresses_DifferingOnlyByCaseOrPadding_AreEqual()
    {
        Assert.Equal(Email.Create("User@Example.com"), Email.Create(" user@example.COM "));
        Assert.True(Email.Create("User@Example.com") == Email.Create("user@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@leading.com")]
    [InlineData("trailing@")]
    [InlineData("two@@example.com")]
    [InlineData("one@example.com@again")]
    public void TryCreate_RejectsMalformedAddresses(string? value)
    {
        Assert.False(Email.TryCreate(value, out var email));
        Assert.Null(email);
        Assert.Throws<DomainValidationException>(() => Email.Create(value));
    }

    [Fact]
    public void TryCreate_AcceptsWhateverTheEmailAddressAttributeAccepts()
    {
        // [EmailAddress] only requires a single '@' that is neither first nor last, so an
        // intranet-style address with no dotted domain is valid. The value object matches
        // that rule on purpose: anything the API layer lets through must not blow up deeper.
        Assert.True(Email.TryCreate("admin@localhost", out _));
        Assert.True(Email.TryCreate("a@b", out _));
    }
}

public class DistinguishedNameTests
{
    [Theory]
    [InlineData("CN=Administrators,OU=Groups,DC=example,DC=com", "Administrators")]
    // Directories are inconsistent about attribute-type casing.
    [InlineData("cn=Admins,ou=Groups,dc=example,dc=com", "Admins")]
    // Components often arrive padded.
    [InlineData("CN=Help Desk , OU=Groups , DC=example", "Help Desk")]
    // Only the first CN component counts.
    [InlineData("CN=First,CN=Second,DC=example", "First")]
    public void Cn_ExtractsTheFirstCommonNameComponent(string dn, string expected)
    {
        Assert.Equal(expected, DistinguishedName.Create(dn).Cn);
    }

    [Fact]
    public void Cn_FallsBackToTheWholeValue_WhenThereIsNoCnComponent()
    {
        Assert.Equal("OU=Groups,DC=example", DistinguishedName.Create("OU=Groups,DC=example").Cn);
    }

    [Fact]
    public void Comparison_IsCaseInsensitive()
    {
        var fromSearch = DistinguishedName.Create("CN=Admins,OU=Groups,DC=example,DC=com");
        var fromMemberOf = DistinguishedName.Create("cn=admins,ou=groups,dc=EXAMPLE,dc=com");

        Assert.Equal(fromSearch, fromMemberOf);
        Assert.Equal(fromSearch.GetHashCode(), fromMemberOf.GetHashCode());
    }

    [Fact]
    public void Create_TrimsSurroundingWhitespace()
    {
        Assert.Equal("CN=Admins,DC=example", DistinguishedName.Create("  CN=Admins,DC=example  ").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_RejectsBlankValues(string? value)
    {
        Assert.False(DistinguishedName.TryCreate(value, out var dn));
        Assert.Null(dn);
        Assert.Throws<DomainValidationException>(() => DistinguishedName.Create(value));
    }

    [Theory]
    [InlineData("OU=Финансы,DC=example,DC=com", "Финансы", "DC=example,DC=com")]
    [InlineData("OU=Almaty,OU=Sales,DC=example,DC=com", "Almaty", "OU=Sales,DC=example,DC=com")]
    [InlineData("DC=example,DC=com", "example", "DC=com")]
    // A single component has no parent — not an empty-named one.
    [InlineData("DC=com", "com", "")]
    public void RdnAndParent_SplitTheEntryFromItsContainer(string dn, string expectedRdn, string expectedParent)
    {
        var parsed = DistinguishedName.Create(dn);

        Assert.Equal(expectedRdn, parsed.Rdn);
        Assert.Equal(expectedParent, parsed.Parent);
    }

    [Fact]
    public void RdnAndParent_HonourEscapedCommas()
    {
        // Directories really do hold entries with commas in the name. Splitting on every comma
        // turns this one entry into two that do not exist.
        var parsed = DistinguishedName.Create("OU=Отдел кадров\\, филиал,DC=example,DC=com");

        Assert.Equal("Отдел кадров, филиал", parsed.Rdn);
        Assert.Equal("DC=example,DC=com", parsed.Parent);
    }

    [Theory]
    [InlineData("Sales", "Sales")]
    [InlineData("Отдел кадров, филиал", "Отдел кадров\\, филиал")]
    [InlineData("A+B", "A\\+B")]
    [InlineData("#hash", "\\#hash")]
    public void EscapeRdnValue_KeepsAValueInsideOneDnComponent(string value, string expected)
    {
        Assert.Equal(expected, DistinguishedName.EscapeRdnValue(value));
    }

    [Fact]
    public void EscapeRdnValue_RoundTripsThroughRdn()
    {
        const string name = "Отдел кадров, филиал +1";

        var dn = DistinguishedName.Create($"OU={DistinguishedName.EscapeRdnValue(name)},DC=example,DC=com");

        Assert.Equal(name, dn.Rdn);
    }
}

public class LdapPortTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(389)]
    [InlineData(636)]
    [InlineData(3268)]
    [InlineData(65535)]
    public void Create_AcceptsTheWholeValidRange(int value)
    {
        Assert.Equal(value, LdapPort.Create(value).Value);
    }

    [Theory]
    // 0 is what a non-nullable int defaults to, and [Required] never rejected it — this is the
    // value that used to reach LdapConnection.Connect unchallenged.
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(65536)]
    [InlineData(999999)]
    public void Create_RejectsAnythingOutsideIt(int value)
    {
        Assert.False(LdapPort.TryCreate(value, out var port));
        Assert.Null(port);
        Assert.Throws<DomainValidationException>(() => LdapPort.Create(value));
    }

    [Fact]
    public void PortsAreComparedByValue()
    {
        Assert.Equal(LdapPort.Create(389), LdapPort.Create(389));
        Assert.True(LdapPort.Create(636) != LdapPort.Create(389));
        Assert.Equal(LdapPort.Create(389), LdapPort.Default);
    }
}
