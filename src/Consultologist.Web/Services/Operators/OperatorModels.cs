namespace Consultologist.Web.Services.Operators;

// #553: mirrors the Api's OperatorUsageRowResponse — numbers, the account id,
// and the display name the account already carries. Rows link nowhere.
public sealed record OperatorUsageRowResponse(
    string AppUserId,
    string DisplayName,
    string? AccountKind,
    string? TenantId,
    int ConsultsCompleted,
    int TokensIn,
    int TokensOut);

// #732: mirrors the Api's OperatorUsageDayResponse — one day's totals across
// all accounts, for the time-series chart.
public sealed record OperatorUsageDayResponse(
    string Day,
    int ConsultsCompleted,
    int TokensIn,
    int TokensOut);

// #553: mirrors the Api's OperatorUsageResponse. Days added in #732 — nullable
// on the client because the frontend and API deploy independently, so a new
// client can meet an API build that predates the field (then no daily series).
public sealed record OperatorUsageResponse(
    string From,
    string To,
    IReadOnlyList<OperatorUsageRowResponse> Rows,
    IReadOnlyList<OperatorUsageDayResponse>? Days);

/// <summary>
/// #553: the caller is signed in but not on Operators__AppUserIds — the 403
/// is bodiless by design, so the page's named sentence hangs on this type.
/// </summary>
public sealed class OperatorAccessException : Exception
{
    public OperatorAccessException()
        : base("This page is for operators — your account is not on the allowlist.")
    {
    }
}
