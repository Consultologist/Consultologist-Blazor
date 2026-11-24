using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 4; part 07 §§ 8.1 to 8.4; R3; D13): the migration's snapshot test,
/// <c>tests/migration_snapshot.json</c>, the one test a migrated package carries. <see cref="Fictional"/> gives D13's
/// request: every input given a value generated from its format (<see cref="WorkflowStubOutputs"/>, seed
/// <c>input.&lt;id&gt;</c>) but a macro's per-run choice, left <c>null</c>; Dr A. Sample's fixed fictional values for
/// exactly <c>needs.profile</c>; the run facts the plan reads, fixed; every reply generated. <see cref="Write"/> turns
/// that request and its offline run (<see cref="OfflineRun"/>) into the test file: the request's <c>inputs</c>,
/// <c>profile</c> and <c>run</c>, the replies as served (D12's markers included, so the same run serves them again) as
/// <c>outputs</c>, and every prompt and deliverable <b>by hash</b> in <c>expect</c> (a deliverable skipped by its
/// <c>if</c> as <c>{ "state": "skipped" }</c> alone, R3). <see cref="Request"/> reads such a file back into a request.
/// No value in it comes from a person: inputs and replies are generated stubs, the profile is fictional.
/// </summary>
public static class SnapshotTest
{
    /// <summary>The test's id (part 10 § 4).</summary>
    public const string Id = "migration_snapshot";

    /// <summary>The test's file in the package.</summary>
    public const string File = "tests/" + Id + ".json";

    /// <summary>The run's date (part 07 § 8.1's example; D13).</summary>
    public const string Date = "2026-11-16";

    /// <summary>The zone the run's date is taken in (D13).</summary>
    public const string TimeZone = "America/Toronto";

    /// <summary>The run's one time: 10:00 in <see cref="TimeZone"/> on <see cref="Date"/>.</summary>
    public static readonly DateTime StartedAtUtc = new(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc);

    /// <summary>Dr A. Sample's fixed fictional profile (part 07 § 8.1; D13), every field a package may need.</summary>
    public static IReadOnlyDictionary<string, string> Profile { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["name"] = "Dr A. Sample",
        ["credentials"] = "MD (fictional)",
        ["organisation"] = "Sample Clinic (fictional)",
        ["signature"] = "Dr A. Sample\nInternal Medicine",
        ["closing"] = "Kind regards,",
    };

    /// <summary>The run facts other than the date: fixed strings (D13); <c>package</c> is the plan's <c>&lt;name&gt;@&lt;version&gt;</c>.</summary>
    private const string RunId = "migration_snapshot";

    private const string Engine = "offline";

    private static readonly string[] DescriptionLines =
    [
        "The migration's snapshot (part 10 § 4): the migrated package run offline on fictional inputs and generated replies,",
        "its prompts and deliverables pinned by hash.",
    ];

    /// <summary>
    /// D13's request for <paramref name="plan"/>: each input in <c>inputOrder</c> given a value of its format, generated
    /// from the seed <c>input.&lt;id&gt;</c> (a text format's <see cref="WorkflowStubOutputs.Text"/>, another's
    /// <see cref="WorkflowStubOutputs.FromSchema"/> of its expanded schema), except an optional boolean input that a macro's
    /// <c>if</c> reads as its one boolean input (a per-run choice, part 10 § 1.3, R7), left <c>null</c>; <see cref="Profile"/>'s
    /// value for each field of <c>needs.profile</c>; the facts of <c>run</c>, the date <see cref="Date"/>; no reply pinned,
    /// so every one is generated from its row key; the clock <see cref="StartedAtUtc"/>.
    /// </summary>
    public static OfflineRequest Fictional(RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var choices = Choices(plan);
        var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var id in plan.InputOrder)
        {
            if (choices.Contains(id))
            {
                inputs[id] = null;
                continue;
            }

            var format = plan.Inputs[id]["format"];
            var seed = "input." + id;
            inputs[id] = plan.Formats.IsText(format)
                ? JsonValue.Create(WorkflowStubOutputs.Text(seed))
                : WorkflowStubOutputs.FromSchema(plan.Formats.Expand(format), seed);
        }

        var profile = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in plan.NeedsProfile.Distinct(StringComparer.Ordinal))
            profile[field] = Profile.GetValueOrDefault(field);

        var facts = new JsonObject();
        foreach (var fact in Facts(plan))
        {
            facts[fact] = fact switch
            {
                "date" => Date,
                "id" => RunId,
                "package" => $"{plan.Package}@{plan.Version}",
                "engine" => Engine,
                _ => null,
            };
        }

        return new OfflineRequest(inputs, profile, facts, new Dictionary<string, JsonNode?>(StringComparer.Ordinal), StartedAtUtc);
    }

    /// <summary>
    /// The test file (part 07 § 8.1) of <paramref name="result"/>, the offline run of <paramref name="plan"/> on
    /// <paramref name="request"/>, which must have completed: every member present, in the grammar's order.
    /// <c>inputs</c> each input in <c>inputOrder</c> as the request gave it (<c>null</c> when not given); <c>profile</c>
    /// exactly <c>needs.profile</c>'s fields; <c>run</c> exactly the plan's facts, the date as
    /// <c>{ "value", "timeZone" }</c> in <see cref="TimeZone"/>; <c>outputs</c> every reply served, by row key in ordinal
    /// order; <c>expect</c> <c>prompts</c> (each agent row's sent text by <c>{ "hash" }</c>, in the order first sent) and
    /// <c>deliverables</c> (each, in plan order, by <c>{ "state", "hash" }</c>, or <c>{ "state": "skipped" }</c> alone, R3).
    /// It holds no prompt or deliverable text.
    /// </summary>
    public static JsonObject Write(RunPlan plan, OfflineRequest request, OfflineResult result)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (result.State != RunState.Completed)
            throw new ArgumentException("A snapshot is written of a run that completed.", nameof(result));

        var inputs = new JsonObject();
        foreach (var id in plan.InputOrder)
            inputs[id] = request.Inputs.GetValueOrDefault(id)?.DeepClone();

        var profile = new JsonObject();
        foreach (var field in plan.NeedsProfile.Distinct(StringComparer.Ordinal))
            profile[field] = request.Profile.GetValueOrDefault(field);

        var run = new JsonObject();
        foreach (var fact in Facts(plan))
        {
            var value = request.RunFacts[fact]?.DeepClone();
            run[fact] = fact == "date" ? new JsonObject { ["value"] = value, ["timeZone"] = TimeZone } : value;
        }

        var outputs = new JsonObject();
        foreach (var key in result.Outputs.Keys.Order(StringComparer.Ordinal))
            outputs[key] = result.Outputs[key]?.DeepClone();

        var prompts = new JsonObject();
        foreach (var prompt in result.Prompts)
            prompts[prompt.Key] = new JsonObject { ["hash"] = prompt.Hash };

        var deliverables = new JsonObject();
        foreach (var deliverable in result.Deliverables)
        {
            var state = deliverable.State.ToString().ToLowerInvariant();
            deliverables[deliverable.Key] = deliverable.State == DeliverableState.Skipped
                ? new JsonObject { ["state"] = state }
                : new JsonObject { ["state"] = state, ["hash"] = deliverable.ContentHash ?? CanonicalJson.HashText(deliverable.Text ?? "") };
        }

        return new JsonObject
        {
            ["description"] = new JsonArray([.. DescriptionLines.Select(line => (JsonNode?)JsonValue.Create(line))]),
            ["inputs"] = inputs,
            ["profile"] = profile,
            ["run"] = run,
            ["outputs"] = outputs,
            ["expect"] = new JsonObject { ["prompts"] = prompts, ["deliverables"] = deliverables },
        };
    }

    /// <summary>
    /// The request a test file <paramref name="test"/> (part 07 § 8.1) states: its <c>inputs</c> as given (a member
    /// <c>null</c> is an input left unset), its <c>profile</c>'s strings, its <c>run</c> with the date's <c>value</c> as
    /// the date, its <c>outputs</c> pinned, the clock <see cref="StartedAtUtc"/>. A member missing or of another type
    /// reads as empty: the run then says what is missing.
    /// </summary>
    public static OfflineRequest Request(JsonObject test)
    {
        ArgumentNullException.ThrowIfNull(test);
        var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (id, value) in test["inputs"] as JsonObject ?? [])
            inputs[id] = value?.DeepClone();

        var profile = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (field, value) in test["profile"] as JsonObject ?? [])
            profile[field] = SourceLayout.Text(value);

        var facts = new JsonObject();
        foreach (var (fact, value) in test["run"] as JsonObject ?? [])
            facts[fact] = fact == "date" && value is JsonObject date ? date["value"]?.DeepClone() : value?.DeepClone();

        var outputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (key, value) in test["outputs"] as JsonObject ?? [])
            outputs[key] = value?.DeepClone();

        return new OfflineRequest(inputs, profile, facts, outputs, StartedAtUtc);
    }

    /// <summary>The run facts <paramref name="plan"/> reads, by name with <c>run.</c> dropped, in its order.</summary>
    private static IEnumerable<string> Facts(RunPlan plan) =>
        plan.RunFacts
            .Select(fact => fact.StartsWith("run.", StringComparison.Ordinal) ? fact["run.".Length..] : fact)
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// The per-run choices of <paramref name="plan"/>: each optional boolean input that is the one boolean input a macro's
    /// <c>if</c> reads (as the 31 start reads a <c>MacroChoices</c> entry, R7).
    /// </summary>
    private static HashSet<string> Choices(RunPlan plan)
    {
        var choices = new HashSet<string>(StringComparer.Ordinal);
        if (plan.Document["macros"] is not JsonObject macros)
            return choices;
        foreach (var (_, macro) in macros)
        {
            var read = JsonLogicShape.Paths(macro?["if"])
                .Select(path => path.Split('.'))
                .Where(path => path is ["input", _, ..])
                .Select(path => path[1])
                .Where(input => plan.Inputs.TryGetValue(input, out var declared) && IsBoolean(plan, declared["format"]))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (read is [var input] && !IsRequired(plan.Inputs[input]))
                choices.Add(input);
        }

        return choices;
    }

    private static bool IsBoolean(RunPlan plan, JsonNode? format) =>
        plan.Formats.Expand(format) is JsonObject expanded && SourceLayout.Text(expanded["type"]) == "boolean";

    private static bool IsRequired(JsonObject input) =>
        input["required"] is JsonValue required && required.TryGetValue<bool>(out var value) && value;
}
