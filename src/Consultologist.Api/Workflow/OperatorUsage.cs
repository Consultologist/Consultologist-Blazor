using System.Net;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Consultologist.Api.Workflow;

/// <summary>
/// GET Operator/Usage — usage per user for a window, for an account listed
/// in Operators__AppUserIds (#553). Reads only the derived AccountUsage
/// store, never job records; serves numbers, account ids, and the display
/// name the account already carries. Per-user totals — the day-level detail
/// stays the profile's; the client groups by tenant.
/// </summary>
public sealed class OperatorUsage
{
    private readonly IAccountAuthorizer _authorizer;
    private readonly IAccountUsageStore _usageStore;
    private readonly IAccountStore _accounts;
    private readonly TimeProvider _time;

    public OperatorUsage(IAccountAuthorizer authorizer, IAccountUsageStore usageStore, IAccountStore accounts, TimeProvider time)
    {
        _authorizer = authorizer;
        _usageStore = usageStore;
        _accounts = accounts;
        _time = time;
    }

    [Function("OperatorUsage")]
    public async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Operator/Usage")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;
        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account) || !Operators.IsOperator(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var (fromRaw, toRaw) = Account.ParseUsageQueryParams(req.Url);
        var (from, to, windowError) = Account.ResolveUsageWindow(fromRaw, toRaw, _time.GetUtcNow());

        if (windowError != null)
        {
            var refusal = req.CreateResponse(HttpStatusCode.BadRequest);
            FunctionCors.Apply(req, refusal);
            await refusal.WriteStringAsync(windowError, cancellationToken);
            return refusal;
        }

        var usage = await _usageStore.ListAllAsync(from, to, cancellationToken);
        var directory = await _accounts.ListDirectoryAsync(cancellationToken);

        // One tenant read per account WITH usage in the window — not per
        // account that exists.
        var tenants = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var appUserId in usage.Select(day => day.AppUserId).Distinct(StringComparer.Ordinal))
        {
            tenants[appUserId] = await _accounts.GetTenantIdAsync(appUserId, cancellationToken);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteAsJsonAsync(new OperatorUsageResponse(from, to, RowsOf(usage, directory, tenants)), cancellationToken);
        return response;
    }

    /// <summary>
    /// The join, pure: one row per account with usage in the window — its
    /// days summed, the display name and kind from the directory, the tenant
    /// from the identity parse. Accounts without usage do not appear.
    /// Extracted so it can be asserted directly.
    /// </summary>
    internal static IReadOnlyList<OperatorUsageRowResponse> RowsOf(
        IReadOnlyList<AccountUsageDay> usage,
        IReadOnlyList<AccountDirectoryEntry> directory,
        IReadOnlyDictionary<string, string?> tenants)
    {
        var names = directory.ToDictionary(entry => entry.AppUserId, StringComparer.Ordinal);

        return usage
            .GroupBy(day => day.AppUserId, StringComparer.Ordinal)
            .Select(group =>
            {
                names.TryGetValue(group.Key, out var entry);
                tenants.TryGetValue(group.Key, out var tenantId);
                return new OperatorUsageRowResponse(
                    group.Key,
                    entry?.DisplayName ?? string.Empty,
                    entry?.AccountKind,
                    tenantId,
                    group.Sum(day => day.ConsultsCompleted),
                    group.Sum(day => day.TokensIn),
                    group.Sum(day => day.TokensOut));
            })
            .OrderBy(row => row.AppUserId, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>#553: one account's window totals — numbers, its id, and the display name it already carries.</summary>
public sealed record OperatorUsageRowResponse(
    string AppUserId,
    string DisplayName,
    string? AccountKind,
    string? TenantId,
    int ConsultsCompleted,
    int TokensIn,
    int TokensOut);

public sealed record OperatorUsageResponse(
    string From,
    string To,
    IReadOnlyList<OperatorUsageRowResponse> Rows);
