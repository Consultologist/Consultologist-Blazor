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
    public void TheGenericSettingsRoutes_RefuseBothAddressKeys()
    {
        Assert.True(Account.IsSecretSettingKey(AccountSettingKeys.DeliveryAddress));
        Assert.True(Account.IsSecretSettingKey(AccountSettingKeys.DeliveryAddressPending));
        Assert.False(Account.IsSecretSettingKey("consult.scheduleTime"));
    }
}
