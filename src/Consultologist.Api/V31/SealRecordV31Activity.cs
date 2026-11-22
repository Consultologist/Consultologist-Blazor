using Consultologist.Api.Jobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>#1506 (specVersion 31, area 09; D13): the job whose record <c>seal-record-v31</c> seals.</summary>
public sealed record SealRecordV31Input(string JobId);

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 1.1; part 09 §§ 3.7, 3.8; D13): a 31 job's record sealed at the end of its
/// orchestration, after <see cref="ConsultGenerationJobEntity.FinalizeJob"/> on every path (<see cref="IRecordSealer"/> over
/// the activity's own Durable client). Its result is the sealed record, or null when the job is not a terminal 31 job.
/// No Durable retry policy: a seal that failed is the read's and the sweep's to make (<see cref="SealAsync"/>).
/// </summary>
public sealed class SealRecordV31Activity(RecordSealers sealers)
{
    [Function(ConsultGenerationActivityNames.SealRecordV31)]
    public Task<SealedRecord?> RunAsync([ActivityTrigger] SealRecordV31Input input, [DurableClient] DurableTaskClient client, CancellationToken ct) =>
        RunAsync(input, sealers.For(client), ct);

    internal static Task<SealedRecord?> RunAsync(SealRecordV31Input input, IRecordSealer sealer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(sealer);
        return sealer.EnsureSealedAsync(input.JobId, ct);
    }

    /// <summary>
    /// Seals <paramref name="jobId"/>'s record from an orchestration, through the activity, best-effort: a failure is logged
    /// (the fault's type only) and never fails the orchestration, nor changes the job's status or error.
    /// </summary>
    internal static async Task<SealedRecord?> SealAsync(TaskOrchestrationContext context, string jobId, ILogger logger)
    {
        try
        {
            return await context.CallActivityAsync<SealedRecord?>(ConsultGenerationActivityNames.SealRecordV31, new SealRecordV31Input(jobId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "V31 record not sealed at the end of the orchestration. JobId={JobId}, ExceptionType={ExceptionType}",
                jobId, ConsultGenerationOrchestratorV31.FaultType(ex));
            return null;
        }
    }
}
