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
}

/// <summary>#517: the two ways a delivery address gets verified.</summary>
public static class DeliveryAddressVerifiedBy
{
    public const string Code = "code";
    public const string Tenant = "tenant";
}

public static class IdentityProviders
{
    // The credential authority: only entra-external-id identities can sign in.
    public const string EntraExternalId = "entra-external-id";
    // Verification signal only (#133): linked for proof of account control,
    // never accepted as a bearer credential.
    public const string LinkedIn = "linkedin";
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
    string? AccountKind = null);

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
