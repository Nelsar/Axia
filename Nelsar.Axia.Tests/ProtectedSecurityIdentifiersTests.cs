using Nelsar.Axia.Infrastructure.Identity;

namespace Nelsar.Axia.Tests;

/// <summary>
/// Which SIDs make an entry privileged, read from the binary form a directory returns. The values
/// are a Samba domain's own, from tokenGroups and objectSid; <see cref="SambaAdTests"/> asks a
/// live one.
/// </summary>
public class ProtectedSecurityIdentifiersTests
{
    private const string DomainPrefix = "AQUAAAAAAAUVAAAAWpUB8FmzDPBjITs1";

    [Theory]
    [InlineData(DomainPrefix + "AAIAAA==", "Domain Admins (512)")]
    [InlineData(DomainPrefix + "9AEAAA==", "Administrator (500)")]
    [InlineData(DomainPrefix + "9gEAAA==", "krbtgt (502)")]
    [InlineData(DomainPrefix + "BwIAAA==", "Enterprise Admins (519)")]
    [InlineData("AQIAAAAAAAUgAAAAIAIAAA==", "BUILTIN\\Administrators (544)")]
    [InlineData("AQIAAAAAAAUgAAAAJwIAAA==", "BUILTIN\\Backup Operators (551)")]
    public void TheProtectedOnes_AreRecognised(string sid, string what) =>
        Assert.True(ProtectedSecurityIdentifiers.IsProtected(Convert.FromBase64String(sid)), what);

    [Theory]
    [InlineData(DomainPrefix + "AQIAAA==", "Domain Users (513)")]
    [InlineData(DomainPrefix + "UwQAAA==", "an ordinary account (1107)")]
    [InlineData("AQIAAAAAAAUgAAAAIQIAAA==", "BUILTIN\\Users (545)")]
    [InlineData("AQEAAAAAAAUSAAAA", "SYSTEM, S-1-5-18")]
    public void OrdinaryOnes_AreNot(string sid, string what) =>
        Assert.False(ProtectedSecurityIdentifiers.IsProtected(Convert.FromBase64String(sid)), what);

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 5, 0, 0, 0, 0, 0, 5, 21 })] // claims five sub-authorities, has none
    [InlineData(new byte[] { 2, 1, 0, 0, 0, 0, 0, 5, 32, 2, 0, 0 })] // revision 2
    public void AnythingButASid_IsNotProtected(byte[]? value) =>
        Assert.False(ProtectedSecurityIdentifiers.IsProtected(value));

    [Fact]
    public void TheGroupFilter_AsksForEachProtectedGroupBySid_InTheBinaryFormEveryDirectoryReads()
    {
        // The domain's SID: the prefix above, which is that SID with its count of sub-authorities
        // already raised to five for the RID that follows it; four without one.
        var domain = Convert.FromBase64String(DomainPrefix);
        domain[1] = 4;

        var filter = ProtectedSecurityIdentifiers.GroupFilter(domain);

        Assert.StartsWith("(|", filter);
        // Domain Admins: the domain's SID with 512 (00 02 00 00, little-endian) after it.
        Assert.Contains(@"(objectSid=\01\05\00\00\00\00\00\05\15\00\00\00\5a\95\01\f0\59\b3\0c\f0\63\21\3b\35\00\02\00\00)", filter);
        // BUILTIN\Administrators.
        Assert.Contains(@"(objectSid=\01\02\00\00\00\00\00\05\20\00\00\00\20\02\00\00)", filter);
        // Accounts are not groups, and are not looked for here.
        Assert.DoesNotContain(@"\f4\01\00\00)", filter);
    }

    [Fact]
    public void TheGroupFilter_WantsADomainSid() =>
        Assert.Throws<ArgumentException>(() =>
            ProtectedSecurityIdentifiers.GroupFilter(Convert.FromBase64String("AQIAAAAAAAUgAAAAIAIAAA==")));

    [Theory]
    [InlineData("jdoe", true)]
    [InlineData("ivan.petrov", true)]
    [InlineData("a234567890b234567890", true)]
    [InlineData("a234567890b234567890c", false)] // 21 characters
    [InlineData("jdoe+sales", false)]
    [InlineData("a=b", false)]
    [InlineData("...", false)]
    [InlineData("", false)]
    public void AnActiveDirectoryAccountName_IsTwentyCharactersOfTheAllowedOnes(string login, bool allowed) =>
        Assert.Equal(allowed, LdapUserMapper.IsActiveDirectoryAccountName(login));
}
