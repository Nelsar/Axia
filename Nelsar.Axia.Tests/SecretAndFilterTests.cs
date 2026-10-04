using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;
using Nelsar.Axia.Contracts.Diagnostics;

namespace Nelsar.Axia.Tests;

public class EncryptedSecretTests
{
    [Fact]
    public void ToString_Masks_SoAnInterpolatedSecretCannotLeak()
    {
        var secret = EncryptedSecret.FromCipherText("enc:nonce:cipher:tag");

        // The failure this guards against is not a considered decision to log a password — it is
        // $"...{provider.EncryptedAdminPassword}" written while chasing something else.
        Assert.Equal("***", secret.ToString());
        Assert.Equal("bind: ***", $"bind: {secret}");
        Assert.DoesNotContain("cipher", $"{secret}");
    }

    [Fact]
    public void Reveal_ReturnsTheCipherText_ForTheOneCallerThatNeedsIt()
    {
        Assert.Equal("enc:nonce:cipher:tag",
            EncryptedSecret.FromCipherText("enc:nonce:cipher:tag").Reveal());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullAndEmpty_BothMeanNoCredential(string? cipherText)
    {
        var secret = EncryptedSecret.FromCipherText(cipherText);

        Assert.True(secret.IsEmpty);
        Assert.Equal(EncryptedSecret.None, secret);
    }

    [Fact]
    public void SecretsWithTheSameCipherText_AreEqual()
    {
        Assert.Equal(EncryptedSecret.FromCipherText("enc:a"), EncryptedSecret.FromCipherText("enc:a"));
        Assert.NotEqual(EncryptedSecret.FromCipherText("enc:a"), EncryptedSecret.FromCipherText("enc:b"));
    }
}

public class LdapSearchFilterTests
{
    [Fact]
    public void Create_AcceptsATemplateWithALoginPlaceholder()
    {
        var filter = LdapSearchFilter.Create("(&(objectClass=person)(uid={0}))");

        Assert.Equal("(&(objectClass=person)(uid=jdoe))", filter.Format("jdoe"));
    }

    [Fact]
    public void Create_RejectsATemplateWithNoPlaceholder()
    {
        // Used to be accepted, and then searched for the literal template — matching nobody, on
        // every login, with nothing in the logs to say why.
        var failure = Assert.Throws<DomainValidationException>(
            () => LdapSearchFilter.Create("(&(objectClass=person)(uid=fixed))"));

        Assert.Contains("placeholder", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("(&(objectClass=person)(uid={1}))")]   // threw FormatException mid-search
    [InlineData("(&(objectClass=person)(uid={0})")]    // unbalanced: rejected by the server
    [InlineData("(&(objectClass=person)(uid={0})))")]
    public void Create_RejectsAMalformedTemplate(string template)
    {
        Assert.Throws<DomainValidationException>(() => LdapSearchFilter.Create(template));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsABlankTemplate(string? template)
    {
        Assert.Throws<DomainValidationException>(() => LdapSearchFilter.Create(template));
    }

    [Fact]
    public void Create_RejectsATemplateLongerThanItsColumn()
    {
        var template = "(&(objectClass=person)(uid={0})(description=" + new string('x', 512) + "))";

        Assert.Throws<DomainValidationException>(() => LdapSearchFilter.Create(template));
    }

    [Fact]
    public void FromStorage_IsLenient_SoABadRowCanStillBeReadAndRepaired()
    {
        // Nothing validated the column before this type existed. Rejecting such a row on read
        // would fail the GET an administrator needs in order to see and fix it.
        var stored = LdapSearchFilter.FromStorage("(&(objectClass=person)(uid=fixed))");

        Assert.Equal("(&(objectClass=person)(uid=fixed))", stored.Template);
    }

    [Fact]
    public void FromStorage_FallsBackToTheDefault_WhenTheColumnIsBlank()
    {
        Assert.Equal(LdapSearchFilter.Default, LdapSearchFilter.FromStorage("  "));
    }

    [Fact]
    public void AnEscapedLoginCannotCloseTheFilterAndAppendClauses()
    {
        // The injection LdapConnectionFactory.FindUserDn was open to: this payload used to close
        // the uid clause and add one matching everything, so the search returned the first entry
        // in the tree and that DN was what the connection then bound as.
        var payload = "*)(objectClass=*";
        var formatted = LdapSearchFilter.Default.Format(LdapFilterEscaper.Escape(payload));

        Assert.DoesNotContain("*)(objectClass=*", formatted, StringComparison.Ordinal);
        Assert.Contains(@"\2a\29\28objectClass=\2a", formatted, StringComparison.Ordinal);
    }
}

public class LdapDistinguishedNameEscaperTests
{
    [Fact]
    public void ACommaInAName_CannotEndTheRdnAndAppendComponents()
    {
        var escaped = LdapDistinguishedNameEscaper.Escape("Ivanov, Ivan");

        Assert.Equal(@"Ivanov\, Ivan", escaped);
    }

    [Theory]
    [InlineData("a+b", @"a\+b")]
    [InlineData("a=b", @"a\=b")]
    [InlineData("a<b>c", @"a\<b\>c")]
    [InlineData(@"back\slash", @"back\\slash")]
    [InlineData("#leading", @"\#leading")]
    [InlineData("trailing ", @"trailing\ ")]
    public void EveryCharacterThatChangesADnsMeaning_IsEscaped(string value, string expected)
    {
        Assert.Equal(expected, LdapDistinguishedNameEscaper.Escape(value));
    }

    [Fact]
    public void AnOrdinaryNameIsLeftAlone()
    {
        Assert.Equal("jsmith", LdapDistinguishedNameEscaper.Escape("jsmith"));
    }
}

public class LogMaskTests
{
    [Theory]
    [InlineData("ivan.ivanov@example.com", "i***@example.com")]
    [InlineData("jsmith", "j***")]
    [InlineData("a", "***")]
    [InlineData(null, "(none)")]
    [InlineData("   ", "(none)")]
    public void Login_KeepsWhatOperationsNeeds_AndDropsThePerson(string? login, string expected)
    {
        Assert.Equal(expected, LogMask.Login(login));
    }

    [Fact]
    public void DistinguishedName_MasksOnlyTheRdn_LeavingThePathReadable()
    {
        Assert.Equal(@"CN=I***,OU=Staff,DC=example,DC=com",
            LogMask.DistinguishedName("CN=Ivan Ivanov,OU=Staff,DC=example,DC=com"));
    }

    [Fact]
    public void DistinguishedName_HandlesADnWithNoPath()
    {
        Assert.Equal("CN=I***", LogMask.DistinguishedName("CN=Ivan"));
    }
}
