using Consultologist.Api;
using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 10.7; D16): <c>PUT Account/Settings/{key}</c> holds the Profile page's Letter
/// details to their limits (credentials 120 characters, organisation 200, closing 4,000) and the account time zone to what a
/// 31 start accepts. Each refusal names the key and the limit, never the value.
/// </summary>
public class LetterDetailsTests
{
    [Theory]
    [InlineData(AccountSettingKeys.ProfileCredentials, 120)]
    [InlineData(AccountSettingKeys.ProfileOrganisation, 200)]
    [InlineData(AccountSettingKeys.ProfileClosing, 4_000)]
    public void ALetterDetail_OverItsLimit_IsRefused(string key, int limit)
    {
        Assert.Null(Account.ValidateProfileSetting(key, new string('a', limit)));
        Assert.Null(Account.ValidateProfileSetting(key, ""));

        var refusal = Account.ValidateProfileSetting(key, new string('a', limit + 1));

        Assert.Equal(FormattableString.Invariant($"{key} must be at most {limit} characters."), refusal);
        Assert.DoesNotContain("aaa", refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Mars/Phobos")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Eastern Standard Time")]
    public void ATimeZone_ThatIsNotIana_IsRefused(string zone)
    {
        var refusal = Account.ValidateProfileSetting(AccountSettingKeys.TimeZone, zone);

        Assert.Equal("account.timeZone must be an IANA time zone name, such as America/Toronto.", refusal);
        Assert.Null(Account.ValidateProfileSetting(AccountSettingKeys.TimeZone, "America/Toronto"));
        Assert.Null(Account.ValidateProfileSetting(AccountSettingKeys.TimeZone, "Europe/London"));
    }

    [Fact]
    public void AnyOtherKey_IsNotHeldToTheseLimits()
    {
        Assert.Null(Account.ValidateProfileSetting("consult.workflowPackage", new string('a', 5_000)));
        Assert.Null(Account.ValidateProfileSetting(AccountSettingKeys.ProfileSignatures, new string('a', 5_000)));
    }
}
