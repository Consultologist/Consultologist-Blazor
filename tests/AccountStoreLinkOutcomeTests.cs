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

    [Theory]
    // #556 (storage-separation.md § 2.5): the account's kind, decided once
    // from the signing tenant — #517's one rule: the consumers tenant, or no
    // tenant at all, is personal; any other tenant is an organisation.
    [InlineData("9188040d-6c67-4c5b-b112-36a304b66dad", SignInKinds.Personal)]
    [InlineData("9188040D-6C67-4C5B-B112-36A304B66DAD", SignInKinds.Personal)]
    [InlineData("11111111-2222-3333-4444-555555555555", SignInKinds.Organisation)]
    [InlineData(null, SignInKinds.Personal)]
    [InlineData("   ", SignInKinds.Personal)]
    public void KindFor_IsTheSignInRule_DecidedOnceForTheAccount(string? tenantId, string expected)
    {
        var user = new AuthenticatedUser(
            IdentityProviders.EntraExternalId,
            "https://login.microsoftonline.com/x/v2.0",
            "sub-1",
            "A Clinician",
            "clinician@example.com",
            Array.Empty<string>(),
            tenantId);

        Assert.Equal(expected, AccountStore.KindFor(user));
        // The two words are SignInKinds' own: the account's kind and the
        // token's kind agree by construction for a single-identity account.
        Assert.Equal(DeliveryAddress.SignInKindOf(user), AccountStore.KindFor(user));
    }

    [Fact]
    public void AStampedKind_IsNeverOverwritten_AndAnEmptyOneFillsOnce()
    {
        // #556: the back-fill's whole rule. An organisation token arriving on
        // an account already stamped personal changes nothing — an account
        // cannot change tenant, and the store keys containers on the kind.
        var organisation = new AuthenticatedUser(
            IdentityProviders.EntraExternalId, "https://login.microsoftonline.com/x/v2.0", "sub-1",
            "A Clinician", "clinician@example.com", Array.Empty<string>(), "11111111-2222-3333-4444-555555555555");

        Assert.Equal(SignInKinds.Personal, AccountStore.StampedKind(SignInKinds.Personal, organisation));
        Assert.Equal(SignInKinds.Organisation, AccountStore.StampedKind(null, organisation));
    }
}
