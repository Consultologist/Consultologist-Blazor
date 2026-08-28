using Consultologist.Api;
using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

/// <summary>
/// #486: the verified delivery address — the rules the endpoints sequence.
/// </summary>
public class DeliveryAddressTests
{
    private const string UserId = "user-1";
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("dr.a@clinic.example")]
    [InlineData("  dr.a@clinic.example  ")]
    [InlineData("first.last+tag@sub.clinic.example")]
    public void Validate_AcceptsAPlainAddress(string address)
    {
        Assert.Null(DeliveryAddress.Validate(address));
    }

    [Theory]
    [InlineData(null, "Address is required.")]
    [InlineData("", "Address is required.")]
    [InlineData("not-an-address", "Address is not a valid email address.")]
    [InlineData("Dr A <dr.a@clinic.example>", "Address is not a valid email address.")]
    [InlineData("dr.a@localhost", "Address is not a valid email address.")]
    public void Validate_RefusesWhatItCannotSendTo(string? address, string expected)
    {
        Assert.Equal(expected, DeliveryAddress.Validate(address));
    }

    [Fact]
    public void Validate_RefusesAnOverlongAddress()
    {
        Assert.Equal("Address is too long.", DeliveryAddress.Validate(new string('a', 250) + "@x.example"));
    }

    [Fact]
    public void CreateCode_IsSixDigits()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = DeliveryAddress.CreateCode();
            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.True(char.IsAsciiDigit(c)));
        }
    }

    [Fact]
    public void HashCode_IsSaltedByTheAccount()
    {
        Assert.NotEqual(DeliveryAddress.HashCode("user-1", "123456"), DeliveryAddress.HashCode("user-2", "123456"));
        Assert.Equal(DeliveryAddress.HashCode("user-1", "123456"), DeliveryAddress.HashCode("user-1", " 123456 "));
        Assert.True(DeliveryAddress.CodeMatches("user-1", "123456", DeliveryAddress.HashCode("user-1", "123456")));
        Assert.False(DeliveryAddress.CodeMatches("user-1", "123457", DeliveryAddress.HashCode("user-1", "123456")));
    }

    [Fact]
    public void CreatePending_NormalizesTheAddress_AndSetsTheLifetime()
    {
        var pending = DeliveryAddress.CreatePending(UserId, " Dr.A@Clinic.Example ", "123456", Now);

        Assert.Equal("dr.a@clinic.example", pending.Address);
        Assert.Equal(Now, pending.SentAtUtc);
        Assert.Equal(Now + DeliveryAddress.CodeLifetime, pending.ExpiresAtUtc);
        Assert.Equal(0, pending.Attempts);
        Assert.DoesNotContain("123456", DeliveryAddress.Serialize(pending));
    }

    private static PendingDeliveryAddress Pending(int attempts = 0) =>
        DeliveryAddress.CreatePending(UserId, "dr.a@clinic.example", "123456", Now) with { Attempts = attempts };

    [Fact]
    public void Decide_NothingPending_IsNone()
    {
        Assert.Equal(ConfirmOutcome.None, DeliveryAddress.Decide(null, UserId, "123456", Now).Outcome);
    }

    [Fact]
    public void Decide_TheRightCode_Confirms()
    {
        var decision = DeliveryAddress.Decide(Pending(), UserId, "123456", Now.AddMinutes(14));

        Assert.Equal(ConfirmOutcome.Confirmed, decision.Outcome);
        Assert.Equal("dr.a@clinic.example", decision.Pending!.Address);
    }

    [Fact]
    public void Decide_TheRightCodeForAnotherAccount_IsWrong()
    {
        Assert.Equal(ConfirmOutcome.Wrong, DeliveryAddress.Decide(Pending(), "user-2", "123456", Now).Outcome);
    }

    [Fact]
    public void Decide_AWrongCode_SpendsOneAttempt()
    {
        var decision = DeliveryAddress.Decide(Pending(), UserId, "000000", Now);

        Assert.Equal(ConfirmOutcome.Wrong, decision.Outcome);
        Assert.Equal(1, decision.Pending!.Attempts);
    }

    [Fact]
    public void Decide_TheLastWrongAttempt_ExhaustsTheCode()
    {
        var decision = DeliveryAddress.Decide(Pending(DeliveryAddress.MaxAttempts - 1), UserId, "000000", Now);

        Assert.Equal(ConfirmOutcome.TooManyAttempts, decision.Outcome);
        Assert.Null(decision.Pending);
    }

    [Fact]
    public void Decide_AnExhaustedCode_RefusesEvenTheRightOne()
    {
        Assert.Equal(ConfirmOutcome.TooManyAttempts, DeliveryAddress.Decide(Pending(DeliveryAddress.MaxAttempts), UserId, "123456", Now).Outcome);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void Decide_AtOrPastExpiry_IsExpired(int minutes)
    {
        Assert.Equal(ConfirmOutcome.Expired, DeliveryAddress.Decide(Pending(), UserId, "123456", Now.AddMinutes(minutes)).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Decide_NoCode_IsWrongNotConfirmed(string? code)
    {
        Assert.Equal(ConfirmOutcome.Wrong, DeliveryAddress.Decide(Pending(), UserId, code, Now).Outcome);
    }

    [Fact]
    public void TheCodeMessage_CarriesTheCodeAndNothingAboutTheAccount()
    {
        var (subject, body) = DeliveryAddress.ComposeCodeMessage("123456");

        Assert.Equal("Confirm your Consultologist delivery address", subject);
        Assert.Contains("123456", body);
        Assert.Contains("15 minutes", body);
        Assert.Contains("ignore this message", body);
        Assert.DoesNotContain("@", body);
    }

    [Fact]
    public void Deserialize_ToleratesGarbage()
    {
        Assert.Null(DeliveryAddress.Deserialize(null));
        Assert.Null(DeliveryAddress.Deserialize("{not json"));
        Assert.Equal(Pending(), DeliveryAddress.Deserialize(DeliveryAddress.Serialize(Pending())));
    }

    [Fact]
    public void TheGenericSettingsRoutes_RefuseEveryAddressKey()
    {
        Assert.True(Account.IsSecretSettingKey(AccountSettingKeys.DeliveryAddress));
        Assert.True(Account.IsSecretSettingKey(AccountSettingKeys.DeliveryAddressPending));
        // #517: how it was verified is a claim of trust, not a preference.
        Assert.True(Account.IsSecretSettingKey(AccountSettingKeys.DeliveryAddressVerifiedBy));
        Assert.False(Account.IsSecretSettingKey("consult.scheduleTime"));
    }

    // ----- #517: the signed-in address, on an organisation's token only -----

    private static AuthenticatedUser User(string? tenantId, string? email) =>
        new("entra-external-id", "https://login.microsoftonline.com/x/v2.0", "sub-1", "A Clinician", email, Array.Empty<string>(), tenantId);

    private const string OrgTenant = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

    [Fact]
    public void AnOrganisationsToken_MayUseItsOwnEmail_Normalised()
    {
        var decision = DeliveryAddress.SignedInEligibility(User(OrgTenant, "  Dr.A@Clinic.Example "));

        Assert.Equal(SignedInOutcome.Eligible, decision.Outcome);
        Assert.Equal("dr.a@clinic.example", decision.Address);
        Assert.Equal(SignInKinds.Organisation, DeliveryAddress.SignInKindOf(User(OrgTenant, "a@b.example")));
    }

    [Theory]
    [InlineData("9188040d-6c67-4c5b-b112-36a304b66dad")]
    [InlineData("9188040D-6C67-4C5B-B112-36A304B66DAD")]
    [InlineData(null)]
    [InlineData("   ")]
    public void APersonalAccount_OrATokenWithNoTenant_KeepsTheCode(string? tenantId)
    {
        // The consumers tenant is a personal Microsoft account; a token with no
        // tenant had no organisation vouch for it. Both keep the code path,
        // whatever email the token carries.
        var decision = DeliveryAddress.SignedInEligibility(User(tenantId, "someone@outlook.example"));

        Assert.Equal(SignedInOutcome.PersonalAccount, decision.Outcome);
        Assert.Null(decision.Address);
        Assert.Equal(SignInKinds.Personal, DeliveryAddress.SignInKindOf(User(tenantId, "someone@outlook.example")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("Dr A <a@b.example>")]
    public void AnOrganisationsToken_WithoutAUsableEmail_IsNamed(string? email)
    {
        var decision = DeliveryAddress.SignedInEligibility(User(OrgTenant, email));

        Assert.Equal(SignedInOutcome.NoEmailClaim, decision.Outcome);
        Assert.Null(decision.Address);
    }

    [Fact]
    public void TheTwoWaysOfVerifying_AreTheTwoWords()
    {
        Assert.Equal("code", DeliveryAddressVerifiedBy.Code);
        Assert.Equal("tenant", DeliveryAddressVerifiedBy.Tenant);
        Assert.Equal("delivery.addressVerifiedBy", AccountSettingKeys.DeliveryAddressVerifiedBy);
    }
}
