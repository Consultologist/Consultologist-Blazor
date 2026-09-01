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

// #553: mirrors the Api's OperatorUsageResponse.
public sealed record OperatorUsageResponse(
    string From,
    string To,
    IReadOnlyList<OperatorUsageRowResponse> Rows);

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
