using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Consultologist.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Workflow;

/// <summary>
/// Which account owns which account package — a record, not a substring of
/// the name (#447, #371 step 2). Before this an account had exactly one
/// package and the name WAS the authorization; now an account holds many,
/// and every read that used to compare a name to
/// <see cref="WorkflowPackageNaming.ForAccount(string)"/> asks here instead.
/// </summary>
public interface IWorkflowPackageOwnership
{
    Task<bool> OwnsAsync(string appUserId, string name, CancellationToken cancellationToken);

    /// <summary>Every package name recorded for the account, ordinal order.</summary>
    Task<IReadOnlyList<string>> ListAsync(string appUserId, CancellationToken cancellationToken);

    /// <summary>Idempotent: recording twice is one record.</summary>
    Task RecordAsync(string appUserId, string name, CancellationToken cancellationToken);

    /// <summary>#559: drops the account's ownership rows. Returns how many went.</summary>
    Task<int> DeleteAllAsync(string appUserId, CancellationToken cancellationToken);
}

/// <summary>
/// The acct-* access rule, in one place for its six enforcement points:
/// repo-owned names are open to everyone; an account package is readable by
/// the account that owns it, by record. #462 retired the derived-name
/// fallback #447 had kept until every existing package was recorded.
/// </summary>
public static class WorkflowPackageAccess
{
    public static async Task<bool> CanAccessAsync(
        this IWorkflowPackageOwnership ownership,
        string name,
        string appUserId,
        CancellationToken cancellationToken)
    {
        if (!WorkflowPackageNaming.IsAccountPackage(name))
        {
            return true;
        }

        return await ownership.OwnsAsync(appUserId, name, cancellationToken);
    }
}

public sealed class WorkflowPackageOwnership : IWorkflowPackageOwnership
{
    private const string TableName = "PackageOwners";
    private readonly TableClient _owners;
    private readonly ILogger<WorkflowPackageOwnership> _logger;

    public WorkflowPackageOwnership(IConfiguration configuration, TokenCredential credential, ILogger<WorkflowPackageOwnership> logger)
    {
        _owners = StorageTables.CreateClient(configuration, credential, TableName, "AccountStorage");
        _logger = logger;
    }

    public async Task<bool> OwnsAsync(string appUserId, string name, CancellationToken cancellationToken)
    {
        try
        {
            await _owners.CreateIfNotExistsAsync(cancellationToken);
            await _owners.GetEntityAsync<PackageOwnerEntity>(appUserId, PackageOwnerEntity.KeyFor(name), cancellationToken: cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (RequestFailedException ex)
        {
            // Fail closed: an unreadable record is no record. The caller's
            // sentence is the same refusal; the log says why.
            _logger.LogError(ex, "Package ownership lookup failed; refusing. Package={Package}", name);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(string appUserId, CancellationToken cancellationToken)
    {
        await _owners.CreateIfNotExistsAsync(cancellationToken);
        var names = new List<string>();

        await foreach (var entity in _owners.QueryAsync<PackageOwnerEntity>(
            entity => entity.PartitionKey == appUserId, cancellationToken: cancellationToken))
        {
            names.Add(PackageOwnerEntity.NameOf(entity.RowKey));
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    public async Task<int> DeleteAllAsync(string appUserId, CancellationToken cancellationToken)
    {
        await _owners.CreateIfNotExistsAsync(cancellationToken);

        var deleted = 0;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {appUserId}");
        await foreach (var entity in _owners.QueryAsync<TableEntity>(filter, select: new[] { "PartitionKey", "RowKey" }, cancellationToken: cancellationToken))
        {
            try
            {
                await _owners.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: cancellationToken);
                deleted++;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
            }
        }

        return deleted;
    }

    public async Task RecordAsync(string appUserId, string name, CancellationToken cancellationToken)
    {
        await _owners.CreateIfNotExistsAsync(cancellationToken);
        await _owners.UpsertEntityAsync(
            new PackageOwnerEntity { PartitionKey = appUserId, RowKey = PackageOwnerEntity.KeyFor(name), RecordedAtUtc = DateTimeOffset.UtcNow },
            TableUpdateMode.Merge,
            cancellationToken);
    }
}

internal sealed class PackageOwnerEntity : ITableEntity
{
    /// <summary>
    /// #448: a nested name holds '/', which a table key may not. '|' is
    /// legal in a key and illegal in a name, so the mapping is exact both
    /// ways; flat names (every pre-#448 row) are their own key.
    /// </summary>
    public static string KeyFor(string name) => name.Replace('/', '|');

    public static string NameOf(string rowKey) => rowKey.Replace('|', '/');

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
