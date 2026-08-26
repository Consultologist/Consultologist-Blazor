using Consultologist.Api;
using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

public class DeliveryPasswordTests
{
    [Fact]
    public void ValidateDeliveryPassword_SixteenChars_IsValid()
    {
        Assert.Null(Account.ValidateDeliveryPassword(new string('a', 16)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateDeliveryPassword_Missing_IsRejected(string? password)
    {
        Assert.Equal("Password is required.", Account.ValidateDeliveryPassword(password));
    }

    [Fact]
    public void ValidateDeliveryPassword_FifteenChars_IsRejected()
    {
        Assert.Equal(
            "Password must be at least 16 characters.",
            Account.ValidateDeliveryPassword(new string('a', 15)));
    }

    [Fact]
    public void ValidateDeliveryPassword_TooLong_IsRejected()
    {
        Assert.Equal("Password is too long.", Account.ValidateDeliveryPassword(new string('a', 129)));
    }

    [Fact]
    public void IsSecretSettingKey_MatchesTheReservedKeys()
    {
        Assert.True(Account.IsSecretSettingKey(AccountSettingKeys.DeliveryPassword));
        Assert.False(Account.IsSecretSettingKey("consult.workflowPackage"));
        // Ordinal, case-sensitive — a case variant is just an ordinary key.
        Assert.False(Account.IsSecretSettingKey("Delivery.DocumentPassword"));
    }
}
