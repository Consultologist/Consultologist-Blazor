using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

public class AccountStoreLinkOutcomeTests
{
    [Fact]
    public void DecideLinkOutcome_NoExistingLink_Links()
    {
        Assert.Equal(IdentityLinkOutcome.Linked, AccountStore.DecideLinkOutcome(null, "user-1"));
    }

    [Fact]
    public void DecideLinkOutcome_SameUser_IsIdempotent()
    {
        var existing = new IdentityLinkEntity { AppUserId = "user-1" };

        Assert.Equal(IdentityLinkOutcome.AlreadyLinkedToSelf, AccountStore.DecideLinkOutcome(existing, "user-1"));
    }

    [Fact]
    public void DecideLinkOutcome_OtherUser_Conflicts()
    {
        var existing = new IdentityLinkEntity { AppUserId = "user-2" };

        Assert.Equal(IdentityLinkOutcome.ConflictOtherUser, AccountStore.DecideLinkOutcome(existing, "user-1"));
    }

    [Fact]
    public void CreateSubjectHash_IsStableAndNamespacedByProvider()
    {
        var linkedIn = AccountStore.CreateSubjectHash("linkedin", "https://www.linkedin.com/oauth", "abc123");

        Assert.Equal(linkedIn, AccountStore.CreateSubjectHash("linkedin", "https://www.linkedin.com/oauth", "abc123"));
        Assert.NotEqual(linkedIn, AccountStore.CreateSubjectHash("entra-external-id", "https://www.linkedin.com/oauth", "abc123"));
        Assert.DoesNotContain(linkedIn, c => c is '+' or '/' or '=');
    }

    [Theory]
    // #195: linking is the activation signal, so a link activates — including
    // from Pending, which makes the manual operator flip the fallback rather
    // than the path.
    [InlineData(AccountStatuses.Pending, AccountStatuses.Active)]
    [InlineData(AccountStatuses.Unverified, AccountStatuses.Active)]
    [InlineData(AccountStatuses.Active, AccountStatuses.Active)]
    // Disabled is not something a user may lift by linking.
    [InlineData(AccountStatuses.Disabled, AccountStatuses.Disabled)]
    public void StatusAfterLink_ActivatesButNeverLiftsDisabled(string current, string expected)
    {
        Assert.Equal(expected, AccountStore.StatusAfterLink(current));
    }

    [Theory]
    // Withdrawing the evidence withdraws the activation it justified.
    [InlineData(AccountStatuses.Active, AccountStatuses.Unverified)]
    // But an account that was never activated does not become Unverified by
    // unlinking — Pending means never activated, which is still true.
    [InlineData(AccountStatuses.Pending, AccountStatuses.Pending)]
    [InlineData(AccountStatuses.Disabled, AccountStatuses.Disabled)]
    [InlineData(AccountStatuses.Unverified, AccountStatuses.Unverified)]
    public void StatusAfterUnlink_OnlyDemotesAnActiveAccount(string current, string expected)
    {
        Assert.Equal(expected, AccountStore.StatusAfterUnlink(current));
    }
}
