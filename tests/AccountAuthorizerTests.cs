using Consultologist.Api.Auth;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Consultologist.Api.Tests;

public class AccountAuthorizerTests
{
    private static AuthenticatedUser CreateUser() =>
        new("entra", "https://login.microsoftonline.com/tenant/v2.0", "subject-1",
            "Test User", "user@example.com", new[] { "access_as_user" });

    private static AppAccount CreateAccount(string status = AccountStatuses.Active)
    {
        var identity = new AccountIdentity(
            "entra", "https://login.microsoftonline.com/tenant/v2.0", "subject-1",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        return new AppAccount("user-1", "Test User", "user@example.com", status, identity, new[] { identity });
    }

    [Fact]
    public async Task AuthorizeAsync_ReturnsNull_WhenTokenValidationFails()
    {
        var validator = Substitute.For<IBearerTokenValidator>();
        validator.ValidateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((AuthenticatedUser?)null);
        var accountStore = Substitute.For<IAccountStore>();
        var authorizer = new AccountAuthorizer(validator, accountStore);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer invalid-token";

        var account = await authorizer.AuthorizeAsync(context.Request, CancellationToken.None);

        Assert.Null(account);
        await accountStore.DidNotReceiveWithAnyArgs()
            .ResolveOrCreateAsync(default!, default);
    }

    [Fact]
    public async Task AuthorizeAsync_ResolvesAccount_WhenTokenIsValid()
    {
        var user = CreateUser();
        var expectedAccount = CreateAccount();

        var validator = Substitute.For<IBearerTokenValidator>();
        validator.ValidateAsync("Bearer valid-token", Arg.Any<CancellationToken>())
            .Returns(user);
        var accountStore = Substitute.For<IAccountStore>();
        accountStore.ResolveOrCreateAsync(user, Arg.Any<CancellationToken>())
            .Returns(expectedAccount);
        var authorizer = new AccountAuthorizer(validator, accountStore);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer valid-token";

        var account = await authorizer.AuthorizeAsync(context.Request, CancellationToken.None);

        Assert.Same(expectedAccount, account);
    }

    [Fact]
    public async Task AuthorizeAsync_PassesMissingHeaderToValidator()
    {
        var validator = Substitute.For<IBearerTokenValidator>();
        validator.ValidateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((AuthenticatedUser?)null);
        var accountStore = Substitute.For<IAccountStore>();
        var authorizer = new AccountAuthorizer(validator, accountStore);

        var context = new DefaultHttpContext();

        var account = await authorizer.AuthorizeAsync(context.Request, CancellationToken.None);

        Assert.Null(account);
        await validator.Received(1).ValidateAsync(null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Active", true)]
    // #195: an Unverified account reads everything it already made. A consult
    // does not stop being readable because the evidence behind the account's
    // activation was withdrawn.
    [InlineData("Unverified", true)]
    [InlineData("active", false)]
    [InlineData("Pending", false)]
    [InlineData("Disabled", false)]
    [InlineData("", false)]
    public void CanUseApp_AdmitsUnverified(string status, bool expected)
    {
        Assert.Equal(expected, AccountAuthorizer.CanUseApp(CreateAccount(status)));
    }

    [Theory]
    [InlineData("Active", true)]
    // The one thing it cannot do, and the whole reason the two questions were
    // separated: the single IsActive answered both at once.
    [InlineData("Unverified", false)]
    [InlineData("active", false)]
    [InlineData("Pending", false)]
    [InlineData("Disabled", false)]
    [InlineData("", false)]
    public void CanStartConsults_IsActiveOnly(string status, bool expected)
    {
        Assert.Equal(expected, AccountAuthorizer.CanStartConsults(CreateAccount(status)));
    }
}
