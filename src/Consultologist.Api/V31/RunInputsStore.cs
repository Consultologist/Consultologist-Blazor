using System.Text.Json.Nodes;
using Azure.Core;
using Consultologist.Api.Jobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06): a 31 job's inputs, held by reference so they never ride in its orchestration input. The
/// document is <c>{ "inputs": { &lt;id&gt;: { value, content, title } }, "profile": { &lt;field&gt;: text | null } }</c>: each
/// supplied input's read form (part 02) and the profile fields the package needs. The job's start writes it; the
/// orchestration reads it through <see cref="ConsultGenerationActivityNames.LoadRunInputsV31"/>.
/// </summary>
public interface IRunInputsStore
{
    Task WriteAsync(string jobId, JsonObject document, CancellationToken ct);

    /// <summary>The job's document, or null when there is none.</summary>
    Task<JsonObject?> ReadAsync(string jobId, CancellationToken ct);
}

/// <summary>
/// #1503: <see cref="IRunInputsStore"/> on the text account, beside the job outputs and inputs (<see cref="TextBlobClientFactory"/>):
/// one JSON blob per job, <c>{jobId}.json</c>, in the container <c>job-run-inputs-v31</c>.
/// </summary>
public sealed class RunInputsBlobStore : IRunInputsStore
{
    /// <summary>The container (operator-created in production, as the text account's others are).</summary>
    public const string Container = "job-run-inputs-v31";

    private readonly TextBlobClientFactory _blobs;

    public RunInputsBlobStore(IConfiguration configuration, TokenCredential credential, ILogger<RunInputsBlobStore> logger)
    {
        _blobs = new TextBlobClientFactory(configuration, credential, logger, "Run inputs store (31)");
    }

    /// <summary>The blob's name for <paramref name="jobId"/>.</summary>
    public static string NameFor(string jobId) => $"{jobId}.json";

    public Task WriteAsync(string jobId, JsonObject document, CancellationToken ct) =>
        _blobs.WriteJsonAsync(Container, NameFor(jobId), document, ct);

    public Task<JsonObject?> ReadAsync(string jobId, CancellationToken ct) =>
        _blobs.ReadJsonAsync<JsonObject>(Container, NameFor(jobId), ct);
}

/// <summary>#1503 (specVersion 31, area 06): the activity that reads a 31 job's inputs document for its orchestration.</summary>
public sealed class LoadRunInputsV31Activity(IRunInputsStore store)
{
    [Function(ConsultGenerationActivityNames.LoadRunInputsV31)]
    public Task<JsonObject?> RunAsync([ActivityTrigger] string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        return store.ReadAsync(jobId, cancellationToken);
    }
}
