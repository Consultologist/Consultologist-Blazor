using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.PackageFormat;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06): a 31 job's inputs, held by reference so they never ride in its orchestration input. The
/// document is <c>{ "inputs": { &lt;id&gt;: { value, content, title } }, "profile": { &lt;field&gt;: text | null } }</c>: each
/// supplied input's read form (part 02) and the profile fields the package needs. It is the job's held inputs: the job's
/// start writes it and records the pointer as the job's <c>InputsBlob</c>, so the retention drop (<c>DropInputs</c>) and
/// account closure delete it as they delete a 5–30 job's; the orchestration reads it through
/// <see cref="ConsultGenerationActivityNames.LoadRunInputsV31"/>.
/// </summary>
public interface IRunInputsStore
{
    /// <summary>Writes the document where the job's held inputs live and returns the pointer the job records.</summary>
    Task<ConsultInputsBlobPointer> WriteAsync(string? accountKind, string appUserId, string jobId, JsonObject document, CancellationToken ct);

    /// <summary>The document, or null when it is gone (dropped, or never written).</summary>
    Task<JsonObject?> ReadAsync(ConsultInputsBlobPointer pointer, CancellationToken ct);
}

/// <summary>
/// #1509 (D8): a 31 job's held inputs document, read for History and for a rerun: each supplied input's <c>value</c> (the
/// document's <c>inputs.&lt;id&gt;.value</c>), as the typed value a request carries. Never the profile half, which is the
/// account's and is read afresh by every start.
/// </summary>
public static class RunInputsDocument
{
    /// <summary>
    /// The value of each input the document holds, by id. An input with no value (an absent optional one, or a JSON null)
    /// is left out, as a 5–30 job's held <c>Supplied</c> leaves it out.
    /// </summary>
    public static IReadOnlyDictionary<string, ConsultInputValue> ValuesOf(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var values = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal);
        if (document["inputs"] is not JsonObject inputs)
            return values;

        foreach (var (id, readForm) in inputs)
        {
            if (readForm is JsonObject form && form["value"] is { } value)
                values[id] = ConsultInputValue.FromJson(value.ToJsonString());
        }

        return values;
    }
}

/// <summary>
/// #1503: <see cref="IRunInputsStore"/> over the job inputs' own storage (<see cref="JobInputsBlobStore"/>): the kind's
/// container (<c>org-job-inputs</c> or <c>personal-job-inputs</c>), <c>{appUserId}/{jobId}.json</c>. No container of its own.
/// </summary>
public sealed class RunInputsBlobStore(JobInputsBlobStore inputs) : IRunInputsStore
{
    /// <summary>Where the document of <paramref name="jobId"/> lives: the job's held inputs.</summary>
    public static ConsultInputsBlobPointer PointerFor(string? accountKind, string appUserId, string jobId) =>
        JobInputsBlobStore.PointerFor(accountKind, appUserId, jobId);

    /// <summary>The pointer of the held inputs named <paramref name="inputsBlob"/> (an orchestration input's <c>InputsBlob</c>) in the kind's container.</summary>
    public static ConsultInputsBlobPointer PointerOf(string? accountKind, string inputsBlob) =>
        new(JobInputsBlobStore.ContainerFor(accountKind), inputsBlob);

    public Task<ConsultInputsBlobPointer> WriteAsync(string? accountKind, string appUserId, string jobId, JsonObject document, CancellationToken ct) =>
        inputs.WriteDocumentAsync(accountKind, appUserId, jobId, document, ct);

    public Task<JsonObject?> ReadAsync(ConsultInputsBlobPointer pointer, CancellationToken ct) => inputs.ReadDocumentAsync(pointer, ct);
}

/// <summary>
/// #1503 (specVersion 31, area 06): what <see cref="LoadRunInputsV31Activity"/> is given: the job's held inputs, its package
/// version and <c>resolveHash</c> (for the plan's run facts), when its orchestration started, the account's zone, and the run
/// facts only the start knows.
/// </summary>
public sealed record LoadRunInputsV31Input(
    ConsultInputsBlobPointer InputsBlob,
    string PackageName,
    string PackageVersion,
    string ResolveHash,
    DateTimeOffset StartedAtUtc,
    string? TimeZone,
    JsonObject RunFactsBase);

/// <summary>
/// #1503: the job's inputs document and its run facts, or the sentence the job fails with. #1506 (area 09; part 08 §§ 3,
/// 8.1; R4): with them, what the header records: <paramref name="VersionsRan"/>, the versions this process runs the job
/// with, and <paramref name="RecordedRunFacts"/>, the raw value of each run fact the package reads (<c>date</c> as
/// <c>{ value, timeZone }</c>). Trailing and optional: a history written before reads as it did.
/// </summary>
public sealed record LoadRunInputsV31Result(
    JsonObject? Document,
    JsonObject? RunFacts,
    string? Refusal,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    JsonObject? VersionsRan = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    JsonObject? RecordedRunFacts = null);

/// <summary>
/// #1503 (specVersion 31, area 06; part 03 § 4.3; D6): reads a 31 job's inputs document for its orchestration and works out
/// its run facts there, so they are in the history and the orchestration never reads the time zone database: the start's
/// (<see cref="LoadRunInputsV31Input.RunFactsBase"/>), then <see cref="V31StartChecks.RunFacts"/>'s over the job's plan (from the
/// store's cache, held to its <c>resolveHash</c>). A document that is gone (the retention drop deleted it) is A06-009; a plan
/// that is not the job's is the load's sentence. #1506 (area 09; R4): and what the header records, worked out here for the
/// reason the run facts are: <c>versionsRan</c>, from the version's verified <c>lock.json</c> and this process's
/// <paramref name="toolchain"/> (assembly versions can differ between workers), and the run facts the package reads.
/// </summary>
public sealed class LoadRunInputsV31Activity(
    IRunInputsStore inputs, ICompiledPackageStore store, EngineToolchain toolchain, ILogger<LoadRunInputsV31Activity>? logger = null)
{
    [Function(ConsultGenerationActivityNames.LoadRunInputsV31)]
    public async Task<LoadRunInputsV31Result> RunAsync([ActivityTrigger] LoadRunInputsV31Input input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await inputs.ReadAsync(input.InputsBlob, cancellationToken) is not { } document)
            return new LoadRunInputsV31Result(null, null, NoLongerHeld);

        var (plan, lockDocument, refusal) = await V31Plans.LoadWithLockAsync(store, input.PackageName, input.PackageVersion, input.ResolveHash,
            (ILogger?)logger ?? NullLogger.Instance, cancellationToken);
        if (plan == null)
            return new LoadRunInputsV31Result(null, null, refusal);

        var facts = (JsonObject)input.RunFactsBase.DeepClone();
        foreach (var (name, value) in V31StartChecks.RunFacts(plan, input.StartedAtUtc, input.TimeZone))
            facts[name] = value?.DeepClone();
        return new LoadRunInputsV31Result(
            document, facts, null,
            lockDocument == null ? null : toolchain.VersionsRanFor(lockDocument),
            V31StartChecks.RecordedRunFacts(plan, facts, input.TimeZone));
    }

    /// <summary>A06-009: the job's held inputs are gone.</summary>
    public const string NoLongerHeld = "This job's inputs are no longer held, so it can't run. Run the workflow again.";
}
