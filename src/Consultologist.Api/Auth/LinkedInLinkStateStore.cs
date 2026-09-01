using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Auth;

public interface ILinkedInLinkStateStore
{
    Task<LinkedInLinkState> CreateAsync(string appUserId, string returnOrigin, CancellationToken cancellationToken);

    /// <summary>
    /// Single-use retrieval: returns the state and deletes it atomically
    /// (ETag-conditioned), or null when the state is unknown, expired, or was
    /// already consumed by a concurrent request.
    /// </summary>
    Task<LinkedInLinkState?> TakeAsync(string state, CancellationToken cancellationToken);

    /// <summary>
    /// #558: drops states whose <c>ExpiresAtUtc</c> is behind
    /// <paramref name="now"/> — the abandoned handshakes; a consumed state
    /// already deleted its own row. The table is ONE partition, so this runs
    /// once per sweep, never per account. Returns how many rows went.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class LinkedInLinkStateStore : ILinkedInLinkStateStore
{
    internal const string StatePartitionKey = "linkedin-state";
    private const string TableName = "LinkedInLinkStates";
    private const int DefaultTtlMinutes = 10;

    private readonly TableClient _table;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LinkedInLinkStateStore> _logger;

    public LinkedInLinkStateStore(
        IConfiguration configuration,
        TokenCredential credential,
        TimeProvider timeProvider,
        ILogger<LinkedInLinkStateStore> logger)
    {
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
        _table = StorageTables.CreateClient(configuration, credential, TableName, "LinkedInStateStorage", "AccountStorage");
    }

    public async Task<LinkedInLinkState> CreateAsync(string appUserId, string returnOrigin, CancellationToken cancellationToken)
    {
        await _table.CreateIfNotExistsAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var ttlMinutes = _configuration.GetValue("LinkedIn:StateTtlMinutes", DefaultTtlMinutes);
        var entity = new LinkedInLinkStateEntity
        {
            RowKey = CreateToken(),
            AppUserId = appUserId,
            Nonce = CreateToken(),
            ReturnOrigin = returnOrigin,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(ttlMinutes)
        };

        await _table.AddEntityAsync(entity, cancellationToken);

        return new LinkedInLinkState(entity.RowKey, entity.AppUserId, entity.Nonce, entity.ReturnOrigin);
    }

    public async Task<LinkedInLinkState?> TakeAsync(string state, CancellationToken cancellationToken)
    {
        LinkedInLinkStateEntity entity;

        try
        {
            var response = await _table.GetEntityAsync<LinkedInLinkStateEntity>(
                StatePartitionKey,
                state,
                cancellationToken: cancellationToken);
            entity = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        try
        {
            await _table.DeleteEntityAsync(StatePartitionKey, state, entity.ETag, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412)
        {
            // Another request consumed the state first; single-use means the
            // race loser gets nothing.
            return null;
        }

        if (entity.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _logger.LogInformation("LinkedIn link state expired. AppUserId={AppUserId}", entity.AppUserId);
            return null;
        }

        return new LinkedInLinkState(entity.RowKey, entity.AppUserId, entity.Nonce, entity.ReturnOrigin);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _table.CreateIfNotExistsAsync(cancellationToken);

        var deleted = 0;
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {StatePartitionKey} and ExpiresAtUtc lt {now}");
        await foreach (var entity in _table.QueryAsync<LinkedInLinkStateEntity>(
            filter, select: new[] { "PartitionKey", "RowKey" }, cancellationToken: cancellationToken))
        {
            try
            {
                await _table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: cancellationToken);
                deleted++;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // TakeAsync consumed it between the query and the delete.
            }
        }

        return deleted;
    }

    // ~256 bits from the CSPRNG, base64url. Guid.NewGuid is not a CSPRNG.
    internal static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
