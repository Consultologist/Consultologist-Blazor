using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

/// <summary>#384: an operator is an account listed in Operators__AppUserIds; nobody by default.</summary>
public class OperatorsTests
{
    private static AppAccount Account(string id)
    {
        var identity = new AccountIdentity("entra", "https://login.example", id, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        return new AppAccount(id, "Operator", null, AccountStatuses.Active, identity, new[] { identity });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ,; ")]
    public void NobodyByDefault(string? setting)
    {
        Assert.Empty(Operators.Parse(setting));
        Assert.False(Operators.IsOperator(Account("a"), Operators.Parse(setting)));
    }

    [Fact]
    public void ListedIds_AreOperators_ByExactMatch()
    {
        var operators = Operators.Parse(" a1 , b2;c3\nd4");

        Assert.Equal(new[] { "a1", "b2", "c3", "d4" }, operators.Order(StringComparer.Ordinal));
        Assert.True(Operators.IsOperator(Account("b2"), operators));
        Assert.False(Operators.IsOperator(Account("B2"), operators));
        Assert.False(Operators.IsOperator(Account("b"), operators));
    }
}
