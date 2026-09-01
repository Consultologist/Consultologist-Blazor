using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Auth;

public interface IAccountSettingsStore
{
    Task<AccountSetting?> GetAsync(string appUserId, string key, CancellationToken cancellationToken);
    Task SaveAsync(string appUserId, string key, string value, string contentType, CancellationToken cancellationToken);
    Task DeleteAsync(string appUserId, string key, CancellationToken cancellationToken);

    /// <summary>
    /// #559: the account's whole settings partition — a query, not a fixed
    /// key list, so keys from any era go too. Returns how many rows went.
    /// </summary>
    Task<int> DeleteAllAsync(string appUserId, CancellationToken cancellationToken);
}

public sealed class AccountSettingsStore : IAccountSettingsStore
{
    private const string AccountSettingsTableName = "AccountSettings";
    private readonly TableClient _settings;

    public AccountSettingsStore(IConfiguration configuration, TokenCredential credential)
    {
        _settings = StorageTables.CreateClient(configuration, credential, AccountSettingsTableName, "AccountStorage");
    }

    public async Task<AccountSetting?> GetAsync(string appUserId, string key, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        try
        {
            var response = await _settings.GetEntityAsync<AccountSettingEntity>(
                appUserId,
                key,
                cancellationToken: cancellationToken);

            var entity = response.Value;
            return new AccountSetting(entity.RowKey, entity.Value, entity.ContentType, entity.UpdatedAtUtc);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        string appUserId,
        string key,
        string value,
        string contentType,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var entity = new AccountSettingEntity
        {
            PartitionKey = appUserId,
            RowKey = key,
            Value = value,
            ContentType = contentType,
            UpdatedAtUtc = now
        };

        await _settings.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task DeleteAsync(string appUserId, string key, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        try
        {
            await _settings.DeleteEntityAsync(appUserId, key, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    public async Task<int> DeleteAllAsync(string appUserId, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var deleted = 0;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {appUserId}");
        var batch = new List<TableTransactionAction>();
        await foreach (var entity in _settings.QueryAsync<TableEntity>(filter, select: new[] { "PartitionKey", "RowKey" }, cancellationToken: cancellationToken))
        {
            batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity, ETag.All));
            if (batch.Count == 100)
            {
                await _settings.SubmitTransactionAsync(batch, cancellationToken);
                deleted += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _settings.SubmitTransactionAsync(batch, cancellationToken);
            deleted += batch.Count;
        }

        return deleted;
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        await _settings.CreateIfNotExistsAsync(cancellationToken);
    }
}
