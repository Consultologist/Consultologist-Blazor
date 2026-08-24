using Azure;
using Consultologist.Api.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Workflow;

/// <summary>
/// #447: writes an ownership record for every account package published
/// before ownership was a record. Runs once at startup, idempotently, and
/// never blocks it: until the operator has seen every package recorded and
/// retired the derived-name fallback (the follow-up to #447), a failed sweep
/// costs nothing but a log line.
/// </summary>
public sealed class PackageOwnershipBackfill : IHostedService
{
    private readonly WorkflowPackageBlobContainerFactory _containers;
    private readonly IAccountStore _accounts;
    private readonly IWorkflowPackageOwnership _ownership;
    private readonly ILogger<PackageOwnershipBackfill> _logger;

    public PackageOwnershipBackfill(
        WorkflowPackageBlobContainerFactory containers,
        IAccountStore accounts,
        IWorkflowPackageOwnership ownership,
        ILogger<PackageOwnershipBackfill> logger)
    {
        _containers = containers;
        _accounts = accounts;
        _ownership = ownership;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            await foreach (var blob in _containers.GetContainer().GetBlobsAsync(cancellationToken: cancellationToken))
            {
                if (blob.Name.Split('/') is { Length: 3 } parts && parts[2] == "manifest.json")
                {
                    names.Add(parts[0]);
                }
            }

            var plan = Plan(names, await _accounts.ListAppUserIdsAsync(cancellationToken));

            foreach (var (appUserId, name) in plan.Records)
            {
                await _ownership.RecordAsync(appUserId, name, cancellationToken);
            }

            foreach (var orphan in plan.Orphans)
            {
                _logger.LogWarning("Account package with no matching account; not recorded. Package={Package}", orphan);
            }

            _logger.LogInformation(
                "Package ownership backfill complete. Recorded={Recorded}, Orphans={Orphans}, RepoOwned={RepoOwned}",
                plan.Records.Count,
                plan.Orphans.Count,
                plan.RepoOwned);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Package ownership backfill failed; the derived-name fallback stands until the next start.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public sealed record BackfillPlan(IReadOnlyList<(string AppUserId, string Name)> Records, IReadOnlyList<string> Orphans, int RepoOwned);

    /// <summary>
    /// Pure: which (account, name) pairs to record, which acct-* names match no
    /// account, and how many names are repo-owned (skipped — they have no
    /// owner). An account is matched by the 12-hex root its derived name
    /// carries, so a slugged package (acct-root-slug) maps too.
    /// </summary>
    public static BackfillPlan Plan(IEnumerable<string> packageNames, IEnumerable<string> appUserIds)
    {
        var byRoot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var appUserId in appUserIds)
        {
            var root = WorkflowPackageNaming.AccountRootOf(WorkflowPackageNaming.ForAccount(appUserId));
            if (root != null)
            {
                byRoot[root] = appUserId;
            }
        }

        var records = new List<(string, string)>();
        var orphans = new List<string>();
        var repoOwned = 0;

        foreach (var name in packageNames.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!WorkflowPackageNaming.IsAccountPackage(name))
            {
                repoOwned++;
                continue;
            }

            var root = WorkflowPackageNaming.AccountRootOf(name);
            if (root != null && byRoot.TryGetValue(root, out var appUserId))
            {
                records.Add((appUserId, name));
            }
            else
            {
                orphans.Add(name);
            }
        }

        return new BackfillPlan(records, orphans, repoOwned);
    }
}
