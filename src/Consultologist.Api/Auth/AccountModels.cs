using Azure;
using Azure.Data.Tables;

namespace Consultologist.Api.Auth;

public static class AccountStatuses
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    // #195: activated once, on evidence since withdrawn — the account
    // disconnected the LinkedIn identity that justified activating it. Distinct
    // from Pending, which was never activated: an Unverified account keeps
    // everything it already made and may not make more.
    public const string Unverified = "Unverified";
    public const string Disabled = "Disabled";
}

public static class AccountSettingKeys
{
    /// <summary>
    /// #159: the encrypted-document delivery password. Write-only through the
    /// dedicated Account/DeliveryPassword endpoints — the generic settings
    /// routes refuse it, and only Account/Me's DocumentPasswordSet flag ever
    /// reveals anything about it.
    /// </summary>
    public const string DeliveryPassword = "delivery.documentPassword";

    /// <summary>
    /// #486: the confirmed delivery address — the only address app-submitted
    /// jobs are ever sent to. Written by Account/DeliveryAddress/Confirm alone;
    /// the generic settings routes refuse it so nothing can plant an address
    /// nobody confirmed.
    /// </summary>
    public const string DeliveryAddress = "delivery.address";

    /// <summary>
    /// #486: the address a confirmation code was sent to, with the code's hash,
    /// expiry and attempts (JSON, <see cref="PendingDeliveryAddress"/>). Same
    /// refusal on the generic routes.
    /// </summary>
    public const string DeliveryAddressPending = "delivery.addressPending";

    /// <summary>
    /// #517: how the delivery address was verified — <see cref="DeliveryAddressVerifiedBy.Code"/>
    /// (it answered a code) or <see cref="DeliveryAddressVerifiedBy.Tenant"/> (an
    /// organisation's sign-in vouched for it). Same refusal on the generic
    /// routes: a route that could write "tenant" onto a code-verified address
    /// would be a way to claim a trust nobody extended. Absent on an address
    /// set before #517, which only the code could have set.
    /// </summary>
    public const string DeliveryAddressVerifiedBy = "delivery.addressVerifiedBy";

    /// <summary>
    /// #518: whether app-initiated runs email the PDF at all — "true" | "false";
    /// absent = not chosen, which sends, as before. A preference, not an
    /// identity: it rides the generic settings routes and is read at start.
    /// </summary>
    public const string EmailPdf = "delivery.emailPdf";

    /// <summary>
    /// #516: the profile's signature blocks and the chosen one, as one JSON
    /// row — a preference-shaped identity artifact on the generic routes.
    /// The Web writes it (Services/Accounts/SignatureBlocks.cs); the job
    /// starter reads it at start when a package marks a deliverable signed
    /// (Auth/SignatureBlocks.cs is the read half).
    /// </summary>
    public const string ProfileSignatures = "profile.signatures";

    /// <summary>
    /// #561: the profile's snippet library — clinician-owned canned text the
    /// setup form inserts into text inputs, one JSON row on the generic
    /// routes. The Web owns it (Services/Accounts/Snippets.cs); the Api
    /// mirror (Auth/Snippets.cs) exists to pin the wire format — nothing
    /// server-side acts on a snippet: inserted text is ordinary typed text.
    /// </summary>
    public const string ProfileSnippets = "profile.snippets";

    /// <summary>
    /// #548: how many days produced text (outputs) is kept after completion —
    /// a whole number of days; absent = not chosen, which keeps the
    /// deployment default (TextRetention__Days). Rides the generic routes;
    /// saves are validated by <see cref="RetentionSettings"/>
    /// (1 ≤ inputDays ≤ outputDays ≤ 30) and the sweep clamps on read.
    /// </summary>
    public const string RetentionOutputDays = "retention.outputDays";

    /// <summary>
    /// #548: how many days held inputs are kept after completion. Same shape
    /// and routes as <see cref="RetentionOutputDays"/>; never longer than the
    /// outputs clock — inputs are the more sensitive class.
    /// </summary>
    public const string RetentionInputDays = "retention.inputDays";

    /// <summary>
    /// #543: what the intake door does with a pushed form response —
    /// <see cref="FormResponseModes"/>' two words; absent = not chosen,
    /// which holds for review (the #518 rule: nothing but the user's own
    /// word changes behaviour). Rides the generic routes; saves refuse any
    /// other value by name; read at the door per push.
    /// </summary>
    public const string FormResponseMode = "forms.responseMode";
}

/// <summary>#517: the two ways a delivery address gets verified.</summary>
public static class DeliveryAddressVerifiedBy
{
    public const string Code = "code";
    public const string Tenant = "tenant";
}

/// <summary>
/// #543: the two answers to "how does a form response become a consult".
/// Hold is what unset means; ONLY the exact run-at-once word ever starts a
/// job — the tolerant read's whole point.
/// </summary>
public static class FormResponseModes
{
    public const string Hold = "hold";
    public const string RunAtOnce = "runAtOnce";

    /// <summary>The stored word (trimmed, case-insensitive), or null — which behaves as Hold.</summary>
    public static string? Of(string? value) => value?.Trim() switch
    {
        { } word when string.Equals(word, Hold, StringComparison.OrdinalIgnoreCase) => Hold,
        { } word when string.Equals(word, RunAtOnce, StringComparison.OrdinalIgnoreCase) => RunAtOnce,
        _ => null,
    };

    /// <summary>The save-side refusal, by name; null when the value is one of the two words.</summary>
    public static string? Validate(string? value) =>
        Of(value) == null
            ? $"forms.responseMode must be '{Hold}' or '{RunAtOnce}'."
            : null;
}

public static class IdentityProviders
{
    // The credential authority: only entra-external-id identities can sign in.
    public const string EntraExternalId = "entra-external-id";
    // Verification signal only (#133): linked for proof of account control,
    // never accepted as a bearer credential.
    public const string LinkedIn = "linkedin";
    // #654: the clinician's Epic identity, bound from the SMART panel as
    // proof of Epic-account control. Like LinkedIn, never a bearer
    // credential — but, unlike LinkedIn, NOT an activation signal (it
    // proves Epic control, a different bar than the eligibility gate the
    // LinkedIn link / operator flip clears).
    public const string Epic = "epic";

    /// <summary>
    /// #654: which providers, when linked, activate a Pending account
    /// (#191/#195). LinkedIn does — it is the eligibility signal. Epic does
    /// not; its link is proof/display only. Extracted so the boundary can be
    /// asserted directly.
    /// </summary>
    public static bool ActivatesAccount(string provider) =>
        string.Equals(provider, LinkedIn, StringComparison.Ordinal);
}

public sealed class AppUserEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "app-user";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Status { get; set; } = AccountStatuses.Pending;
    // #556 (storage-separation.md § 2.5): organisation | personal, decided
    // once — stamped at creation from the sign-in tenant, back-filled at
    // sign-in for rows from before. Null = a pre-#556 row not yet seen.
    // Every text container is an org-/personal- pair keyed by this.
    public string? AccountKind { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}

public sealed class IdentityLinkEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string AppUserId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string SubjectHash { get; set; } = string.Empty;
    public DateTimeOffset LinkedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? PictureUrl { get; set; }
    public string? VerifiedCategories { get; set; }
}

public sealed class UserIdentityLinkEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string SubjectHash { get; set; } = string.Empty;
    public DateTimeOffset LinkedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? PictureUrl { get; set; }
    public string? VerifiedCategories { get; set; }
}

public sealed record AuthenticatedUser(
    string Provider,
    string Issuer,
    string Subject,
    string DisplayName,
    string? Email,
    IReadOnlyList<string> Scopes,
    // #517: the token's tenant (tid). The consumers tenant is a personal
    // Microsoft account; any other is an organisation whose sign-in vouches
    // for the mailbox in Email. This token's, never the stored identity's.
    string? TenantId = null);

public sealed record AppAccount(
    string AppUserId,
    string DisplayName,
    string? Email,
    string Status,
    AccountIdentity CurrentIdentity,
    IReadOnlyList<AccountIdentity> LinkedIdentities,
    // #556: the account's kind (storage-separation.md § 2.5).
    string? AccountKind = null);

public sealed record AccountIdentity(
    string Provider,
    string Issuer,
    string Subject,
    DateTimeOffset LinkedAt,
    DateTimeOffset LastSeenAt,
    string? DisplayName = null,
    string? Email = null,
    string? PictureUrl = null,
    string? VerifiedCategories = null);

public sealed record AccountMeResponse(
    string AppUserId,
    string DisplayName,
    string? Email,
    string Status,
    AccountIdentity CurrentIdentity,
    IReadOnlyList<AccountIdentity> LinkedIdentities,
    // #159: the only readable signal about the write-only delivery password.
    bool DocumentPasswordSet = false,
    // #486: the confirmed delivery address, and the one a code is out to.
    string? DeliveryAddress = null,
    string? DeliveryAddressPending = null,
    // #517: how the address was verified ("code" | "tenant"; null = set
    // before, by the code), and two facts about THIS request's token, never
    // stored: the email it carries and whether an organisation signed it
    // ("organisation" | "personal") — what the card needs to offer, or not,
    // the one-click choice.
    string? DeliveryAddressVerifiedBy = null,
    string? SignInEmail = null,
    string? SignInKind = null,
    // #556: the ACCOUNT's kind ("organisation" | "personal") — decided once
    // and stored, where SignInKind above is this token's. The two agree by
    // construction for a single-identity account, and the account's kind is
    // what the store keys on. Null on a pre-#556 row not yet back-filled.
    string? AccountKind = null,
    // #553: whether this account is on Operators__AppUserIds — a fact about
    // the caller, computed at the endpoint, leaking nothing. The nav shows
    // the Operators link on it; the server gate stays the real one.
    bool IsOperator = false);

/// <summary>#517: what kind of account signed the token — an organisation's, or a personal Microsoft account.</summary>
public static class SignInKinds
{
    public const string Organisation = "organisation";
    public const string Personal = "personal";
}

public sealed class AccountSettingEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Value { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/plain";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed record AccountSetting(
    string Key,
    string Value,
    string ContentType,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountSettingResponse(
    string Key,
    string Value,
    string ContentType,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveDeliveryPasswordRequest(string? Password);

public sealed record SaveAccountSettingRequest(
    string? Value,
    string? ContentType);
