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
    IReadOnlyList<string> Scopes);

public sealed record AppAccount(
    string AppUserId,
    string DisplayName,
    string? Email,
    string Status,
    AccountIdentity CurrentIdentity,
    IReadOnlyList<AccountIdentity> LinkedIdentities);

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
    string? DeliveryAddressPending = null);

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
