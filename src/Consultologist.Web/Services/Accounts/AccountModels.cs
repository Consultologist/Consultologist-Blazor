namespace Consultologist.Web.Services.Accounts;

public sealed record AccountMeResponse(
    string AppUserId,
    string DisplayName,
    string? Email,
    string Status,
    AccountIdentity CurrentIdentity,
    IReadOnlyList<AccountIdentity> LinkedIdentities,
    bool DocumentPasswordSet = false);

public sealed record SaveDeliveryPasswordRequest(string Password);

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

public sealed record LinkedInStartResponse(string AuthorizationUrl);

public sealed record AccountSettingResponse(
    string Key,
    string Value,
    string ContentType,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveAccountSettingRequest(
    string Value,
    string ContentType);

public sealed record AccountJobSummaryResponse(
    string JobId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalBlockCount,
    int CompletedBlockCount,
    int FailedBlockCount,
    string? Source = null,
    DateTimeOffset? ScheduledAtUtc = null,
    // #434: mirrors the Api's AccountJobSummaryResponse.FailedAtStart.
    bool FailedAtStart = false);

public sealed record AccountJobsResponse(
    IReadOnlyList<AccountJobSummaryResponse> Jobs,
    string? ContinuationToken);
