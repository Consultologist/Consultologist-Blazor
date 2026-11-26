using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1507 (specVersion 31, area 10; part 07 § 8.1, § 8.2; part 10 § 3.1): what an offline run is given. <paramref name="Inputs"/>
/// each input's raw value by id, as sent to a start (<see cref="RunInputsBuilder"/> makes the run's inputs of them);
/// <paramref name="Profile"/> the profile's fields; <paramref name="RunFacts"/> the run facts by name with <c>run.</c> dropped;
/// <paramref name="Outputs"/> the replies to serve, by 31 row key (part 07 § 8.2's stub keys; a text format's a JSON string,
/// another format's its JSON value), a key missing generated (<see cref="OfflineStubs"/>); <paramref name="StartedAtUtc"/>
/// the run's one time; <paramref name="Limits"/> the engine's limits (the defaults when null); and <paramref name="StubSeed"/>
/// the seed a missing reply is generated from, by row key (the row key itself when null; D12: the comparison passes 30's key).
/// #1508 (area 11; D6): when <paramref name="GenerateMissing"/> is false a call with no stub is not generated for: it fails its
/// node as a call that could not be made does (no retry, so its dependants never dispatch) and its key is in
/// <see cref="OfflineResult.MissingOutputs"/>; a stub that does not fit is served nowhere, fails its node the same way, and is in
/// <see cref="OfflineResult.InvalidOutputs"/>. Neither is asked again, whatever the node's <c>onInvalid</c>. #1508 (comment 6
/// item 2; R14): a pinned reply is served exactly as written; one whose macro markers are wrong is not a stub fault but the
/// node's <c>onInvalid</c>'s, as live, and the attempt a <c>retry</c> asks for has no stub (missing, when not generating).
/// </summary>
public sealed record OfflineRequest(
    IReadOnlyDictionary<string, JsonNode?> Inputs,
    IReadOnlyDictionary<string, string?> Profile,
    JsonObject RunFacts,
    IReadOnlyDictionary<string, JsonNode?> Outputs,
    DateTime StartedAtUtc,
    RunLimits? Limits = null,
    Func<string, string>? StubSeed = null,
    bool GenerateMissing = true);

/// <summary>
/// #1508 (specVersion 31, area 11; part 07 § 8.2; D6): a stub that does not fit its node's format (or whose markers are
/// wrong), found by a run that does not generate: the row key, the format as sentences print one (the bundle key, else
/// <c>its format</c>), and the first fault as its path and keyword (<c>/concepts/0/term required</c>; <c>/</c> is the root),
/// never the value.
/// </summary>
public sealed record StubFault(string Key, string Format, string Error);

/// <summary>
/// #1507 (specVersion 31, area 10): one text an offline run produced: its row key, the text, and its hash (the row's
/// <c>inputHash</c>, <see cref="CanonicalJson.HashText"/> of the text).
/// </summary>
public sealed record OfflineText(string Key, string Text, string Hash);

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 3.1): what an offline run gave. <paramref name="State"/> and
/// <paramref name="Sentence"/> are the run's (<see cref="RunResult"/>), both <c>null</c> when the start refused
/// (<paramref name="StartRefusals"/>, then nothing ran). <paramref name="Prompts"/> is each agent row's sent prompt in the
/// order first dispatched (a repaired call's last attempt, whose detail is the row's); <paramref name="Renders"/> each
/// rendered node's text, in the order rendered (a deliverable's is in <paramref name="Deliverables"/>, the run's outcomes in
/// plan order). <paramref name="Outputs"/> every reply served, by row key, as served (a generated one's markers appended, D12);
/// <paramref name="Rows"/> and <paramref name="Decisions"/> as recorded; <paramref name="UnusedOutputs"/> the keys of
/// <see cref="OfflineRequest.Outputs"/> no agent call asked for, in ordinal order (part 07 § 8.2's warning; Review Focus 5:
/// a key that cannot be mapped is reported, not dropped). #1508 (area 11; D6): <paramref name="MissingOutputs"/> the row keys
/// an agent call asked for that had no stub, in ordinal order, and <paramref name="InvalidOutputs"/> the stubs that did not fit,
/// by key in ordinal order; both empty unless <see cref="OfflineRequest.GenerateMissing"/> is false (a generated reply that does
/// not fit is the run's own failure, as before).
/// </summary>
public sealed record OfflineResult(
    RunState? State,
    string? Sentence,
    IReadOnlyList<SourceRefusal> StartRefusals,
    IReadOnlyList<OfflineText> Prompts,
    IReadOnlyList<OfflineText> Renders,
    IReadOnlyList<DeliverableOutcome> Deliverables,
    IReadOnlyDictionary<string, JsonNode?> Outputs,
    IReadOnlyList<RunRow> Rows,
    IReadOnlyList<RunDecision> Decisions,
    IReadOnlyList<string> UnusedOutputs,
    IReadOnlyList<string> MissingOutputs,
    IReadOnlyList<StubFault> InvalidOutputs)
{
    /// <summary>The ten-member form area 10's callers use: nothing missing, nothing invalid.</summary>
    public OfflineResult(
        RunState? state,
        string? sentence,
        IReadOnlyList<SourceRefusal> startRefusals,
        IReadOnlyList<OfflineText> prompts,
        IReadOnlyList<OfflineText> renders,
        IReadOnlyList<DeliverableOutcome> deliverables,
        IReadOnlyDictionary<string, JsonNode?> outputs,
        IReadOnlyList<RunRow> rows,
        IReadOnlyList<RunDecision> decisions,
        IReadOnlyList<string> unusedOutputs)
        : this(state, sentence, startRefusals, prompts, renders, deliverables, outputs, rows, decisions, unusedOutputs, [], [])
    {
    }
}

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 3.1; part 07 § 8; D12): the offline, deterministic run of a compiled 31 package,
/// which the migration's snapshot test, the byte-equivalence comparison and area 11's in-package tests run on. The start's
/// inputs are built as the engine's start builds them (<see cref="RunInputsBuilder"/>; a refusal ends it there, and so does
/// a start no deliverable can exist for, A10-062); then
/// <see cref="PlanRunner"/> runs the plan on <see cref="OfflineHost"/>, which calls the engine's own turns
/// (<see cref="Templates.AgentTurn"/>, <see cref="Templates.RenderTurn"/>, <see cref="RuleTurn"/>) and serves each agent call
/// its pinned or generated reply (<see cref="OfflineStubs"/>). Its clock is <see cref="OfflineRequest.StartedAtUtc"/> and
/// never moves, so the run reads no clock and ends under <see cref="RunLimits"/>: a <c>while</c> at its <c>max</c>, a reply
/// that does not fit with its node's failure. The same request gives the same result, row for row and hash for hash.
/// </summary>
public static class OfflineRun
{
    /// <summary>
    /// A10-062 (#1507, Task 15): the id of the start refusal when no deliverable can exist on the inputs
    /// (<see cref="RunInputsBuilder.NoDeliverableCanExist"/>), whose sentence is <see cref="RunInputsBuilder.NoneApply"/>.
    /// </summary>
    public const string NoApplicableDeliverable = "A10-062";

    /// <summary>Runs <paramref name="plan"/> once on <paramref name="request"/>.</summary>
    public static async Task<OfflineResult> RunAsync(RunPlan plan, OfflineRequest request)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Inputs);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.RunFacts);
        ArgumentNullException.ThrowIfNull(request.Outputs);

        var (inputs, refusals) = RunInputsBuilder.Build(plan, request.Inputs, request.RunFacts, request.Profile);

        // #1507 (Task 15; R8, #1505): the engine's start refuses a job no deliverable can exist for, as a 30 start does.
        if (inputs != null && RunInputsBuilder.NoDeliverableCanExist(plan, inputs))
        {
            refusals = [new SourceRefusal(NoApplicableDeliverable, RunInputsBuilder.NoneApply(plan), "")];
            inputs = null;
        }

        if (inputs == null)
        {
            return new OfflineResult(
                null, null, refusals, [], [], [], new Dictionary<string, JsonNode?>(StringComparer.Ordinal), [], [],
                request.Outputs.Keys.Order(StringComparer.Ordinal).ToList(), [], []);
        }

        var host = new OfflineHost(plan, request);
        var recorder = new OfflineRecorder();
        var result = await new PlanRunner(plan, request.Limits ?? new RunLimits(), host, recorder).RunAsync(inputs, request.StartedAtUtc);

        return new OfflineResult(
            result.State,
            result.Sentence,
            [],
            host.Prompts,
            host.Renders,
            result.Deliverables,
            host.Served,
            recorder.Rows,
            recorder.Decisions,
            request.Outputs.Keys.Where(key => !host.Asked.Contains(key)).Order(StringComparer.Ordinal).ToList(),
            host.Missing.Order(StringComparer.Ordinal).ToList(),
            host.Invalid.OrderBy(f => f.Key, StringComparer.Ordinal).ToList());
    }

    /// <summary>The rows and decisions of one offline run, in the order recorded; delivery rows and notices are not kept.</summary>
    private sealed class OfflineRecorder : IRunRecorder
    {
        public List<RunRow> Rows { get; } = [];

        public List<RunDecision> Decisions { get; } = [];

        public Task RowAsync(RunRow row)
        {
            Rows.Add(row);
            return Task.CompletedTask;
        }

        public Task DecisionAsync(RunDecision decision)
        {
            Decisions.Add(decision);
            return Task.CompletedTask;
        }

        public Task DeliveryAsync(DeliveryRow row) => Task.CompletedTask;

        public Task NoticeAsync(RunNotice notice) => Task.CompletedTask;

        public Task FlushAsync() => Task.CompletedTask;
    }
}
