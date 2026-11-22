using System.Text;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>#1506 (specVersion 31, area 09; D13): a sealed record: its <c>recordHash</c>, its length in UTF-8 bytes, and the rows it holds.</summary>
public sealed record SealedRecord(string RecordHash, long Bytes, int Rows);

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 1.1; part 09 §§ 3.7, 3.8; D13): seals a 31 job's record once it is terminal.
/// Reached from the orchestration's end (<see cref="ConsultGenerationActivityNames.SealRecordV31"/>), the cancel route, the
/// retention sweep and the first read of a terminal job with no <c>recordHash</c>.
/// </summary>
public interface IRecordSealer
{
    /// <summary>
    /// The job's sealed record. Null, with nothing read or written, when the job is not a terminal 31 job (it does not
    /// exist, is below record storage 12, or is not Completed, Failed or Cancelled: <c>delivering</c> is not terminal).
    /// Idempotent: a job already stamped answers its stamp without reading a row; a document already written is the one
    /// whose hash is stamped.
    /// </summary>
    Task<SealedRecord?> EnsureSealedAsync(string jobId, CancellationToken ct);
}

/// <summary>
/// #1506 (D13): what <see cref="RecordSealer"/> needs of a job: its entity's state, the account kind whose outputs container
/// holds its record, and the seal stamped on its entity (<see cref="ConsultGenerationJobEntity.RecordV31Seal"/>).
/// <see cref="DurableRecordJobs"/> is the Durable client's; the tests give one over a real entity.
/// </summary>
public interface IRecordJobs
{
    /// <summary>The job's entity state; null when there is none.</summary>
    Task<ConsultGenerationJobState?> ReadAsync(string jobId, CancellationToken ct);

    /// <summary>The account kind the job's blobs are written under (<see cref="JobOutputsBlobStore.ContainerFor"/>).</summary>
    Task<string?> AccountKindAsync(ConsultGenerationJobState job, CancellationToken ct);

    /// <summary>Stamps <paramref name="recordHash"/> and <paramref name="bytes"/> on the job's entity, write-once.</summary>
    Task StampAsync(string jobId, string recordHash, long bytes, CancellationToken ct);
}

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 1.1, 3; D10, D13): a terminal 31 job's rows (<see cref="IRecordRowStore.ListAsync"/>)
/// assembled under its header (<see cref="ConsultGenerationJobState.ToRecordHeader"/>, which notes the end state) into the
/// canonical document (<see cref="RecordDocument.Assemble"/>), written once to <c>record.json</c>
/// (<see cref="IRecordBlobStore.WriteRecordAsync"/>); when a document is already there (a seal that raced this one, its stamp
/// not landed yet) that document is the record, and its hash and length are stamped, never this one's. A record the rows
/// cannot make (a differing duplicate, a delivery with no deliverable) is an engine fault: logged by the job and the type,
/// and thrown. Logs carry ids, counts and hashes only.
/// </summary>
public sealed class RecordSealer(IRecordJobs jobs, IRecordRowStore rows, IRecordBlobStore blobs, ILogger logger) : IRecordSealer
{
    /// <summary>Record storage 12: the first whose record is sealed.</summary>
    internal const int SealedFrom = 12;

    public async Task<SealedRecord?> EnsureSealedAsync(string jobId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        var job = await jobs.ReadAsync(jobId, ct);
        if (job == null || job.SchemaVersion < SealedFrom || !ConsultGenerationJobEntity.IsTerminal(job.Status))
            return null;

        if (job.RecordHash is { } stampedHash && job.RecordBytes is { } stampedBytes)
            return new SealedRecord(stampedHash, stampedBytes, (job.V31Progress?.Rows ?? 0) + (job.V31Progress?.Decisions ?? 0));

        var stored = await rows.ListAsync(jobId, ct);
        string document;
        try
        {
            document = RecordDocument.Assemble(job.ToRecordHeader(), stored);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError("V31 record could not be sealed. JobId={JobId}, ExceptionType={ExceptionType}", jobId, ex.GetType().FullName);
            throw;
        }

        var accountKind = await jobs.AccountKindAsync(job, ct);
        if (!await blobs.WriteRecordAsync(accountKind, job.AppUserId, jobId, document, ct))
        {
            document = await blobs.ReadRecordAsync(accountKind, job.AppUserId, jobId, ct)
                ?? throw new InvalidOperationException("The record's document was there to write over, then gone.");
        }

        var recordHash = RecordDocument.Hash(document);
        long bytes = Encoding.UTF8.GetByteCount(document);
        await jobs.StampAsync(jobId, recordHash, bytes, ct);
        logger.LogInformation(
            "V31 record sealed. JobId={JobId}, Rows={Rows}, Bytes={Bytes}, RecordHash={RecordHash}",
            jobId, stored.Count, bytes, recordHash);
        return new SealedRecord(recordHash, bytes, stored.Count);
    }
}

/// <summary>
/// #1506 (D13): <see cref="IRecordJobs"/> through the Durable client, as the job routes read a job: the entity's state
/// (<c>GetEntityAsync</c>), the seal signalled (<see cref="ConsultGenerationJobEntity.RecordV31Seal"/>), and the account kind
/// the job's held inputs were written under, else (a job that failed at its start holds none) the account's own.
/// </summary>
internal sealed class DurableRecordJobs(DurableTaskClient client, IAccountStore accounts) : IRecordJobs
{
    /// <summary>The account kind the job's held inputs' container names; null when it holds none.</summary>
    internal static string? KindOfHeldInputs(ConsultGenerationJobState job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.InputsBlob is not { } pointer)
            return null;
        return string.Equals(pointer.Container, JobInputsBlobStore.ContainerFor(SignInKinds.Organisation), StringComparison.Ordinal)
            ? SignInKinds.Organisation
            : SignInKinds.Personal;
    }

    private static EntityInstanceId EntityOf(string jobId) => new(nameof(ConsultGenerationJobEntity), jobId);

    public async Task<ConsultGenerationJobState?> ReadAsync(string jobId, CancellationToken ct) =>
        (await client.Entities.GetEntityAsync<ConsultGenerationJobState>(EntityOf(jobId), cancellation: ct))?.State;

    public async Task<string?> AccountKindAsync(ConsultGenerationJobState job, CancellationToken ct) =>
        KindOfHeldInputs(job) ?? await accounts.GetAccountKindAsync(job.AppUserId, ct);

    public Task StampAsync(string jobId, string recordHash, long bytes, CancellationToken ct) =>
        client.Entities.SignalEntityAsync(
            EntityOf(jobId), nameof(ConsultGenerationJobEntity.RecordV31Seal), new ConsultGenerationV31Seal(recordHash, bytes), cancellation: ct);
}

/// <summary>
/// #1506 (D13): the host's sealers: <see cref="For"/> gives the sealer over a function's own Durable client (an activity's
/// or a route's binding). Constructing it, or a sealer, touches no storage.
/// </summary>
public sealed class RecordSealers(IRecordRowStore rows, IRecordBlobStore blobs, IAccountStore accounts, ILogger<RecordSealer> logger)
{
    public IRecordSealer For(DurableTaskClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new RecordSealer(new DurableRecordJobs(client, accounts), rows, blobs, logger);
    }
}
