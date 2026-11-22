using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1506 (specVersion 31, area 09; D10, D12): a round of a 31 job's record rows: the rows (<see cref="RecordDocument.Of(RunRow)"/>
/// and its twins, in the order recorded) and the settled text of each row that has one, by row key (<see cref="RunRow.Text"/>),
/// for the job's text blobs under <paramref name="AppUserId"/> in <paramref name="AccountKind"/>'s outputs container.
/// </summary>
public sealed record WriteRecordRowsV31Input(
    string JobId,
    string AppUserId,
    string? AccountKind,
    IReadOnlyList<RecordRow> Rows,
    IReadOnlyDictionary<string, JsonObject> Settled);

/// <summary>#1506 (area 09; D11): the keys of the rows a stored row differed from (left as they were); empty when none did.</summary>
public sealed record WriteRecordRowsV31Result(IReadOnlyList<string> Conflicts);

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 1.1; D2, D10, D11, D12): writes a round of rows. Each row's settled text first
/// (<c>settled</c>, overwritten: a retry or a replay writes the same text), so a stored row always has its text; then the
/// rows, each inserted if absent (<see cref="IRecordRowStore.WriteAsync"/>): a replay's row is <see cref="RecordWrite.AlreadyThere"/>,
/// and a row that differs from the stored one is a conflict, logged at error level by the job and the key only, and named
/// in the result for the orchestration to fail the job with A06-010 (<see cref="RecordConflictException"/>). A text or a
/// row that cannot be written fails the activity, and the job with it (A06-010): a row whose text was never stored would
/// be a silent hole. No Durable retry policy. Telemetry: keys and counts, never text.
/// </summary>
public sealed class WriteRecordRowsV31Activity(IRecordRowStore rows, IRecordBlobStore blobs, ILogger<WriteRecordRowsV31Activity> logger)
{
    [Function(ConsultGenerationActivityNames.WriteRecordRowsV31)]
    public async Task<WriteRecordRowsV31Result> RunAsync([ActivityTrigger] WriteRecordRowsV31Input input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var settled = input.Settled ?? new Dictionary<string, JsonObject>();
        foreach (var key in settled.Keys.Order(StringComparer.Ordinal))
            await blobs.WriteTextAsync(input.AccountKind, input.AppUserId, input.JobId, key, "settled", settled[key], ct);

        var results = await rows.WriteAsync(input.JobId, input.Rows ?? [], ct);
        var conflicts = new List<string>();
        foreach (var (key, result) in results)
        {
            if (result != RecordWrite.Conflict)
                continue;
            logger.LogError("V31 record conflict. JobId={JobId}, Key={Key}", input.JobId, key);
            conflicts.Add(key);
        }

        logger.LogInformation(
            "V31 record rows. JobId={JobId}, Rows={Rows}, Written={Written}, AlreadyThere={AlreadyThere}, Conflicts={Conflicts}, Texts={Texts}",
            input.JobId,
            results.Count,
            results.Count(r => r.Result == RecordWrite.Written),
            results.Count(r => r.Result == RecordWrite.AlreadyThere),
            conflicts.Count,
            settled.Count);
        return new WriteRecordRowsV31Result(conflicts);
    }
}

/// <summary>
/// #1506 (specVersion 31, area 09; D11): a row the record already holds differently was written again. Thrown in the
/// orchestration by <see cref="StoredRunRecorder.FlushAsync"/>; the orchestration fails the job with A06-010. Its message
/// holds the keys only.
/// </summary>
public sealed class RecordConflictException : Exception
{
    public RecordConflictException(IReadOnlyList<string> keys)
        : base($"The record already holds other rows for: {string.Join(", ", keys)}.") =>
        Keys = keys;

    public RecordConflictException()
        : this([])
    {
    }

    public RecordConflictException(string message)
        : base(message) =>
        Keys = [];

    public RecordConflictException(string message, Exception innerException)
        : base(message, innerException) =>
        Keys = [];

    /// <summary>The keys of the rows that differed.</summary>
    public IReadOnlyList<string> Keys { get; }
}
