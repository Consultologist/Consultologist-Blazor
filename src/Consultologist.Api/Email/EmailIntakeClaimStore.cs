using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Email;

public sealed record EmailIntakeClaim(
    string ClaimKey,
    string GraphMessageId,
    string? FromAddress,
    DateTimeOffset ClaimedAtUtc,
    string? AppUserId = null,
    string? JobId = null,
    string? Outcome = null);

public static class EmailIntakeOutcomes
{
    public const string Accepted = "accepted";
    public const string RejectedAuth = "rejected-auth";
    public const string RejectedSender = "rejected-sender";
    public const string RejectedEmpty = "rejected-empty";
    // v7 email eligibility: the pinned package's input declaration cannot be
    // satisfied by a body-only email (package-format-v7.md).
    public const string RejectedInputs = "rejected-inputs";
    // #210: an attachment this version cannot read, or one whose slot could
    // not be determined without guessing.
    public const string RejectedAttachments = "rejected-attachments";
    public const string StartFailed = "start-failed";
    public const string Vanished = "vanished";
    // #266: NOT terminal. The account was over its submission limit, so the
    // message is parked in the Queued folder and this row is a marker saying
    // "mid-flight across polls". RepairAsync clears it and the message is
    // retried in full; nothing else in this vocabulary is transient.
    public const string Queued = "queued";
    // #266: terminal, and the only rate-limit outcome that ends a message.
    // Reached when a queued message outlives MaxEmailDeferral.
    public const string RejectedRateLimit = "rejected-rate-limit";
    // #369: every deliverable's condition was false for these inputs, so the
    // package would have produced nothing (#315). Distinct from start-failed:
    // the message parsed, the inputs satisfied the declaration, and the
    // package answered that none of its documents applies.
    public const string RejectedNoDeliverable = "rejected-no-deliverable";
    // #291: the referral was attached as a cloud link, so it never arrived.
    // Its own value because the claim table is the audit surface and this is
    // the disposition anyone will actually come looking for.
    public const string RejectedCloudLink = "rejected-cloud-link";
}

public interface IEmailIntakeClaimStore
{
    /// <summary>Atomic claim: false when the message was already claimed (409).</summary>
    Task<bool> TryClaimAsync(EmailIntakeClaim claim, CancellationToken cancellationToken);

    Task<EmailIntakeClaim?> GetAsync(string claimKey, CancellationToken cancellationToken);

    Task UpdateAsync(EmailIntakeClaim claim, CancellationToken cancellationToken);

    /// <summary>
    /// Releases a claim so the message can be claimed again (#266). Only ever
    /// called for a <see cref="EmailIntakeOutcomes.Queued"/> row, which is by
    /// construction a message that started no job — so this cannot resurrect
    /// one that already ran, and the at-most-once bias survives.
    /// </summary>
    Task DeleteAsync(string claimKey, CancellationToken cancellationToken);
}

/// <summary>
/// The exactly-once ledger for email intake (#158): one row per inbound
/// message (keyed by internetMessageId), claimed atomically BEFORE the job
/// starts so a message can never start two jobs. Rows record disposition
/// metadata only — never subject or body.
/// </summary>
public sealed class TableEmailIntakeClaimStore : IEmailIntakeClaimStore
{
    private const string TableName = "EmailIntakeProcessed";
    internal const string PartitionKey = "email-intake";

    private readonly TableClient _table;
    private bool _tableEnsured;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    public TableEmailIntakeClaimStore(IConfiguration configuration, TokenCredential credential)
    {
        _table = StorageTables.CreateClient(configuration, credential, TableName, "EmailIntakeStorage", "AccountStorage");
    }

    public async Task<bool> TryClaimAsync(EmailIntakeClaim claim, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        try
        {
            await _table.AddEntityAsync(ToEntity(claim), cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }
    }

    public async Task<EmailIntakeClaim?> GetAsync(string claimKey, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        try
        {
            var response = await _table.GetEntityAsync<EmailIntakeClaimEntity>(
                PartitionKey,
                CreateRowKey(claimKey),
                cancellationToken: cancellationToken);
            return ToClaim(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpdateAsync(EmailIntakeClaim claim, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);
        await _table.UpsertEntityAsync(ToEntity(claim), TableUpdateMode.Merge, cancellationToken);
    }

    public async Task DeleteAsync(string claimKey, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        try
        {
            await _table.DeleteEntityAsync(PartitionKey, CreateRowKey(claimKey), cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone: another host cleared the same queued claim this
            // tick. The desired state is "no row", and it holds.
        }
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableEnsured)
        {
            return;
        }

        await _ensureLock.WaitAsync(cancellationToken);
        try
        {
            if (!_tableEnsured)
            {
                await _table.CreateIfNotExistsAsync(cancellationToken);
                _tableEnsured = true;
            }
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    // internetMessageId values contain <> and can contain characters illegal in
    // row keys — hash to base64url like AccountStore.CreateSubjectHash.
    internal static string CreateRowKey(string claimKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(claimKey));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static EmailIntakeClaimEntity ToEntity(EmailIntakeClaim claim)
    {
        return new EmailIntakeClaimEntity
        {
            PartitionKey = PartitionKey,
            RowKey = CreateRowKey(claim.ClaimKey),
            ClaimKey = claim.ClaimKey,
            GraphMessageId = claim.GraphMessageId,
            FromAddress = claim.FromAddress,
            ClaimedAtUtc = claim.ClaimedAtUtc,
            AppUserId = claim.AppUserId,
            JobId = claim.JobId,
            Outcome = claim.Outcome
        };
    }

    private static EmailIntakeClaim ToClaim(EmailIntakeClaimEntity entity)
    {
        return new EmailIntakeClaim(
            entity.ClaimKey,
            entity.GraphMessageId,
            entity.FromAddress,
            entity.ClaimedAtUtc,
            entity.AppUserId,
            entity.JobId,
            entity.Outcome);
    }
}

public sealed class EmailIntakeClaimEntity : ITableEntity
{
    public string PartitionKey { get; set; } = TableEmailIntakeClaimStore.PartitionKey;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string ClaimKey { get; set; } = string.Empty;
    public string GraphMessageId { get; set; } = string.Empty;
    public string? FromAddress { get; set; }
    public DateTimeOffset ClaimedAtUtc { get; set; }
    public string? AppUserId { get; set; }
    public string? JobId { get; set; }
    public string? Outcome { get; set; }
}
