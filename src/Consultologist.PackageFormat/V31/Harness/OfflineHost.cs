using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 3.1; the library guide's "The seams"): the <see cref="IRunHost"/> of an offline
/// run. It runs the code the engine's activities run, with no Azure: an agent call is prepared with
/// <see cref="AgentTurn.Prepare"/> (in <see cref="RunLimits.RenderSteps"/> steps, strict mode rejecting
/// <see cref="StrictRejects"/>, as every attested agent's catalogue entry says), given its reply
/// (<see cref="OfflineStubs.Reply"/>) and finished with <see cref="AgentTurn.Finish"/>; a prompt that could not be prepared
/// gets no reply and fails as the engine's does. A render is <see cref="RenderTurn.Render"/>, a rule's evaluation
/// <see cref="RuleTurn.Evaluate"/>. Every answer is given at once. Its clock is the request's start and never moves; a
/// retry's wait ends at once, and the job's own timers (its minutes, its slow notice) end only when the run cancels them,
/// since a clock that does not move never reaches them. It keeps what the run sent and served.
/// </summary>
internal sealed class OfflineHost(RunPlan plan, OfflineRequest request) : IRunHost
{
    /// <summary>The keywords strict mode rejects, as <c>agents.json</c> lists them for every attested agent.</summary>
    public static readonly IReadOnlyList<string> StrictRejects = ["format"];

    private readonly int stepLimit = (request.Limits ?? new RunLimits()).RenderSteps;
    private readonly List<OfflineText> prompts = [];
    private readonly Dictionary<string, int> promptAt = new(StringComparer.Ordinal);
    private readonly List<OfflineText> renders = [];
    private readonly Dictionary<string, JsonNode?> served = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public DateTime UtcNow => request.StartedAtUtc;

    /// <summary>Each agent row's sent prompt, in the order first dispatched; a later attempt's replaces an earlier one's in place.</summary>
    public IReadOnlyList<OfflineText> Prompts => prompts;

    /// <summary>Each rendered node's text, in the order rendered (a deliverable's is its outcome's).</summary>
    public IReadOnlyList<OfflineText> Renders => renders;

    /// <summary>Every reply served, by row key.</summary>
    public IReadOnlyDictionary<string, JsonNode?> Served => served;

    /// <summary>The row keys an agent call was made for.</summary>
    public HashSet<string> Asked { get; } = new(StringComparer.Ordinal);

    /// <summary>D6: the row keys asked for with no stub, when the request does not generate.</summary>
    public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

    /// <summary>D6: the stubs that did not fit, when the request does not generate.</summary>
    public List<StubFault> Invalid { get; } = [];

    /// <inheritdoc/>
    public Task<AgentOutcome> CallAgentAsync(AgentCall call)
    {
        Asked.Add(call.Key);
        var prepared = AgentTurn.Prepare(plan, call, stepLimit, StrictRejects);
        if (prepared.Text is not { } sent)
            return Task.FromResult(AgentTurn.Finish(prepared, ""));

        var text = new OfflineText(call.Key, sent, SourceLayout.Text(prepared.Detail["inputHash"]) ?? CanonicalJson.HashText(sent));
        if (promptAt.TryGetValue(call.Key, out var at))
        {
            prompts[at] = text;
        }
        else
        {
            promptAt[call.Key] = prompts.Count;
            prompts.Add(text);
        }

        // #1508 (comment 6 item 2; R14): a test's stub answers the row's first attempt; the attempt a reply with wrong markers
        // asks for under onInvalid: retry has no stub of its own, so a strict run says it is missing (S07-008), as live a
        // second reply would be needed.
        JsonNode? pinned = null;
        var given = !(call.Repair && !request.GenerateMissing) && request.Outputs.TryGetValue(call.Key, out pinned);
        if (!given && !request.GenerateMissing)
        {
            // D6: no stub is no answer; the call fails as one that could not be made, which no retry changes.
            Missing.Add(call.Key);
            return Task.FromResult(new AgentOutcome(AgentOutcomeKind.Failed, null, 0, "plan"));
        }

        var (value, reply) = OfflineStubs.Reply(plan, call, prepared, given, pinned, (request.StubSeed ?? (key => key))(call.Key));
        var outcome = AgentTurn.Finish(prepared, reply);
        if (!request.GenerateMissing && Fault(call, outcome) is { } fault)
        {
            // D6: a stub is the test's fixed answer: one that does not fit fails the node, and is not asked for again.
            Invalid.Add(fault);
            return Task.FromResult(new AgentOutcome(AgentOutcomeKind.Failed, null, 0, "plan"));
        }

        served[call.Key] = value;
        return Task.FromResult(outcome);
    }

    /// <summary>
    /// D6: what is wrong with the reply <paramref name="outcome"/> carries, as the runner checks a reply (read as its format's
    /// value, then validated, an envelope already taken off): the first fault's path and keyword, never the value; null when
    /// it fits, and null for a reply with wrong macro markers, which the runner judges under the node's <c>onInvalid</c> (R14).
    /// </summary>
    private StubFault? Fault(AgentCall call, AgentOutcome outcome)
    {
        if (outcome.Kind != AgentOutcomeKind.Completed)
            return null;
        var entries = call.Function == null ? plan.Entries : plan.Functions.GetValueOrDefault(call.Function)?.Entries;
        var format = entries?.GetValueOrDefault(call.EntryKey)?.Format;
        var name = format is JsonObject { Count: 1 } site && SourceLayout.Text(site["$ref"]) is { } reference
            && reference.StartsWith("#/$defs/", StringComparison.Ordinal) ? reference["#/$defs/".Length..] : "its format";

        // #1508 (comment 6 item 2; R14): a reply whose markers are wrong is the node's onInvalid's, as live, not the stub's fault.
        if (outcome.Invalid is { Count: > 0 })
            return null;
        var (value, parsed) = outcome.NotOfFormat ? (null, false) : plan.Formats.Read(outcome.Reply ?? "", format);
        var fits = parsed ? plan.Formats.Validate(value, format) : new FormatValueResult(false, [new FormatValueFault("", "type")]);
        if (fits.Valid)
            return null;
        var first = fits.Faults[0];
        return new StubFault(call.Key, name, $"{(first.Pointer.Length == 0 ? "/" : first.Pointer)} {first.Keyword}");
    }

    /// <inheritdoc/>
    public Task<RenderOutcome> RenderAsync(RenderCall call)
    {
        var outcome = RenderTurn.Render(plan, call);
        if (outcome.Text is { } text && !IsDeliverable(call))
            renders.Add(new OfflineText(call.Key, text, SourceLayout.Text(outcome.Detail?["inputHash"]) ?? CanonicalJson.HashText(text)));
        return Task.FromResult(outcome);
    }

    /// <inheritdoc/>
    public Task<RuleOutcome> EvaluateAsync(RuleCall call) => Task.FromResult(RuleTurn.Evaluate(plan, call));

    /// <inheritdoc/>
    public Task DelayAsync(TimeSpan wait, CancellationToken cancel)
    {
        if (cancel.IsCancellationRequested)
            return Task.FromCanceled(cancel);
        if (wait < TimeSpan.FromMinutes(1))
            return Task.CompletedTask;
        var done = new TaskCompletionSource();
        cancel.Register(() => done.TrySetCanceled(cancel));
        return done.Task;
    }

    private bool IsDeliverable(RenderCall call) =>
        call.Function == null && plan.Entries.GetValueOrDefault(call.EntryKey) is { Kind: PlanKind.Deliverable };
}
