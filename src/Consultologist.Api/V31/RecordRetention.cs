using Consultologist.Api.Auth;
using Microsoft.DurableTask.Client;

namespace Consultologist.Api.V31;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 11; D2, D10, D13): the record's share of the 7-day text drop. A 31 job's text rests in
/// <c>{appUserId}/{jobId}/text/…</c> blobs; they go here, after the record is sealed and before the entity stamps
/// <c>TextDroppedAtUtc</c>. The rows and <c>record.json</c> stay. A job below record storage 12 is not touched: its entity is
/// read (the one thing known of it) and no record store is called.
/// </summary>
public interface IRecordTextDrop
{
    /// <summary>
    /// Seals the job's record, then deletes its <c>text/</c> blobs; nothing for a job that is not a terminal 31 job.
    /// Idempotent. Throws <see cref="RecordNotSealedException"/> when the seal fails (nothing is deleted).
    /// </summary>
    Task DropAsync(DurableTaskClient client, string jobId, CancellationToken ct);
}

/// <summary>#1506: the seal failed, so the job's text was kept. Carries the inner exception's type name only (no text).</summary>
public sealed class RecordNotSealedException(string jobId, Exception inner)
    : Exception($"The record of job {jobId} could not be sealed ({inner.GetType().FullName}).", inner)
{
    public string JobId { get; } = jobId;

    public string ExceptionType { get; } = inner.GetType().FullName ?? inner.GetType().Name;
}

public sealed class RecordTextDrop(RecordSealers sealers, IRecordBlobStore blobs, IAccountStore accounts) : IRecordTextDrop
{
    public Task DropAsync(DurableTaskClient client, string jobId, CancellationToken ct) =>
        DropAsync(sealers.For(client), new DurableRecordJobs(client, accounts), blobs, jobId, ct);

    internal static async Task DropAsync(IRecordSealer sealer, IRecordJobs jobs, IRecordBlobStore blobs, string jobId, CancellationToken ct)
    {
        var job = await jobs.ReadAsync(jobId, ct);
        if (job == null || job.SchemaVersion < RecordSealer.SealedFrom)
            return;

        try
        {
            // null: not terminal (the sweep only lists terminal jobs; if one is not, its text is not this pass's to drop)
            if (await sealer.EnsureSealedAsync(jobId, ct) == null)
                return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RecordNotSealedException(jobId, ex);
        }

        await blobs.DeleteTextAsync(await jobs.AccountKindAsync(job, ct), job.AppUserId, jobId, ct);
    }
}
