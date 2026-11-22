using System.Net;
using System.Text.Json;
using Consultologist.Api;
using Consultologist.Api.Auth;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.V31Engine;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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

    // ---- the route (the final review): PUT Account/Settings/{key} answers 400 with the refusal, and saves nothing ----------

    public static TheoryData<string, string, string?> Saves() => new()
    {
        { AccountSettingKeys.ProfileCredentials, new string('a', 121), "profile.credentials must be at most 120 characters." },
        { AccountSettingKeys.ProfileOrganisation, new string('a', 201), "profile.organisation must be at most 200 characters." },
        { AccountSettingKeys.ProfileClosing, new string('a', 4_001), "profile.closing must be at most 4000 characters." },
        { AccountSettingKeys.TimeZone, "Mars/Phobos", "account.timeZone must be an IANA time zone name, such as America/Toronto." },
        { AccountSettingKeys.ProfileCredentials, new string('a', 120), null },
        { AccountSettingKeys.ProfileOrganisation, new string('a', 200), null },
        { AccountSettingKeys.ProfileClosing, new string('a', 4_000), null },
        { AccountSettingKeys.TimeZone, "America/Toronto", null },
    };

    [Theory]
    [MemberData(nameof(Saves))]
    public async Task TheRoute_RefusesWhatIsOverItsLimit_AndSavesWhatFits(string key, string value, string? refusal)
    {
        var settings = Substitute.For<IAccountSettingsStore>();
        var authorizer = Substitute.For<IAccountAuthorizer>();
        authorizer.AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>())
            .Returns(AdminDrainTests.Admin.Account("user-1"));
        var account = new Account(authorizer, settings, Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IGraphMailClient>(),
            new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<Account>.Instance, Substitute.For<IAccountUsageStore>());

        var response = await account.SaveSettingAsync(
            FakeHttp.Request("PUT", JsonSerializer.Serialize(new { value, contentType = "text/plain" })), key);

        if (refusal == null)
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await settings.Received(1).SaveAsync("user-1", key, value, "text/plain", Arg.Any<CancellationToken>());
            return;
        }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(refusal, FakeHttp.TextOf(response));
        await settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!, default!, default);
    }
}
