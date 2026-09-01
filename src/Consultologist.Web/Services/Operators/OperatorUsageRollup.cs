namespace Consultologist.Web.Services.Operators;

/// <summary>
/// #553: the panel's grouping, pure. An organisation is the issuer tenant;
/// personal accounts group under the consumers tenant's own label; a row
/// whose tenant could not be read from the record groups under a named
/// state, never into an organisation. Arithmetic is sums only — the window
/// figures reuse UsageSummary where rates are wanted.
/// </summary>
public static class OperatorUsageRollup
{
    public const string ConsumersTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";
    public const string PersonalAccountsLabel = "Personal accounts";
    public const string TenantNotRecordedLabel = "Tenant not recorded";

    public sealed record OrgGroup(
        string? TenantId,
        string Label,
        IReadOnlyList<OperatorUsageRowResponse> Rows,
        int ConsultsCompleted,
        long TokensIn,
        long TokensOut);

    public static string LabelFor(string? tenantId) => tenantId switch
    {
        null => TenantNotRecordedLabel,
        ConsumersTenantId => PersonalAccountsLabel,
        _ => $"Organisation {tenantId}"
    };

    /// <summary>Groups by tenant, biggest consult totals first; rows inside sorted by the given key.</summary>
    public static IReadOnlyList<OrgGroup> Groups(IReadOnlyList<OperatorUsageRowResponse> rows, string sortKey, bool descending)
    {
        return rows
            .GroupBy(row => row.TenantId)
            .Select(group => new OrgGroup(
                group.Key,
                LabelFor(group.Key),
                SortRows(group.ToList(), sortKey, descending),
                group.Sum(row => row.ConsultsCompleted),
                group.Sum(row => (long)row.TokensIn),
                group.Sum(row => (long)row.TokensOut)))
            .OrderByDescending(group => group.ConsultsCompleted)
            .ThenBy(group => group.Label, StringComparer.Ordinal)
            .ToList();
    }

    public const string SortByName = "name";
    public const string SortByConsults = "consults";
    public const string SortByTokens = "tokens";

    public static IReadOnlyList<OperatorUsageRowResponse> SortRows(
        IReadOnlyList<OperatorUsageRowResponse> rows, string sortKey, bool descending)
    {
        var ordered = sortKey switch
        {
            SortByConsults => rows.OrderBy(row => row.ConsultsCompleted),
            SortByTokens => rows.OrderBy(row => (long)row.TokensIn + row.TokensOut),
            _ => rows.OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
        };

        var settled = ordered.ThenBy(row => row.AppUserId, StringComparer.Ordinal).ToList();
        if (descending)
        {
            settled.Reverse();
        }

        return settled;
    }
}
