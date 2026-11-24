using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 3.2): one compared text of <c>migration-report.json</c>: its site (D11), the
/// hash of the text at 30 and at 31 (the hash of the empty text for a side that lacks it), whether the two are equal (both
/// present, the same bytes), whether the difference is expected, and why (<c>null</c> unless <paramref name="Expected"/>).
/// </summary>
public sealed record ComparedText(string Site, string OldHash, string NewHash, bool Equal, bool Expected, string? Explanation);

/// <summary>#1507 (D11): what a compared text is: the job's state, a sent prompt, a rendered node's text, a deliverable's text.</summary>
public enum ComparedKind
{
    /// <summary>The job's state (<c>#status</c>): 30's <c>Completed</c>, <c>Failed</c> or <c>Refused</c> beside 31's.</summary>
    Status,

    /// <summary>An agent node's sent prompt (<c>#prompts</c>).</summary>
    Prompt,

    /// <summary>A rendered node's text (<c>#renders</c>).</summary>
    Render,

    /// <summary>A deliverable's text (<c>#deliverables</c>).</summary>
    Deliverable,
}

/// <summary>
/// #1507 (part 10 § 3.2): one difference, as <see cref="ExpectedDifferences.Explain"/> judges it: where it is
/// (<paramref name="Package"/>, <paramref name="Scenario"/>, <paramref name="Site"/>, <paramref name="Kind"/>, the 30 key and
/// the 31 key, each <c>null</c> for a side that lacks the text), both texts (<c>null</c> for a side that lacks it; a status is
/// its word), and the whole of both runs: the 30 capture, the 31 offline run, the migration and the plan it ran, and the
/// request the 31 run was given (<c>null</c> when the caller did not pass it: a rule that reads it then explains nothing).
/// </summary>
public sealed record DifferenceContext(
    string Package,
    string Scenario,
    string Site,
    ComparedKind Kind,
    string? Key30,
    string? Key31,
    string? OldText,
    string? NewText,
    JsonObject Capture30,
    OfflineResult Run31,
    Migration31Result Migrated,
    RunPlan Plan,
    OfflineRequest? Request = null);

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 §§ 3.1, 3.2; D10 to D12; the fixture README's "What area 10 does with the
/// captures"): a 30 capture document held against the offline run of the migrated package. <see cref="RequestFor"/> makes
/// the 31 request of a 30 scenario and its capture: the scenario's inputs and macro choices through 31's start, its profile,
/// 30's run date, and the capture's replies replayed under 31's row keys (<see cref="KeyMap"/>), a reply the capture lacks
/// generated from 30's key. <see cref="Compare"/> lists every compared text: the job's state, every prompt, every rendered
/// node's text and every deliverable's, each at its site (D11), a difference marked expected only when a rule of
/// <see cref="ExpectedDifferences"/> explains it ("when in doubt, a difference is unexpected").
/// </summary>
public static class ByteEquivalence
{
    /// <summary>30's clock in every capture (<c>CaptureRun</c>'s): 2026-11-10 at 12:00 UTC.</summary>
    public static readonly DateTime CapturedAtUtc = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>30's <c>run.date</c> in every capture: the date of <see cref="CapturedAtUtc"/>.</summary>
    public const string CapturedDate = "2026-11-10";

    /// <summary>The status a refused start gets (the capture's word for it; a 31 start refusal is said alike).</summary>
    public const string Refused = "Refused";

    /// <summary>The hash a side that lacks a text gets (D11): the hash of the empty text.</summary>
    public static string EmptyHash { get; } = CanonicalJson.HashText("");

    /// <summary>
    /// The 31 request of the 30 scenario <paramref name="scenario30"/> and its capture <paramref name="capture30"/> for the
    /// migrated package (<paramref name="migrated"/>, compiled to <paramref name="plan"/>).
    /// <list type="bullet">
    /// <item><b>Inputs:</b> each of the scenario's <c>inputs</c> under its 31 id (one the migration renamed, A10-006, under
    /// its new id); a scalar given to an input whose 31 format is a list (blank text aside) given as a list of that one value (part 10 § 1.2:
    /// 30's one-or-many inputs take their array form); an object given every field its 31 format lists
    /// (<see cref="Fit"/>: part 10 § 1.8, a field 30 left out is an explicit <c>null</c>); then the scenario's
    /// <c>macroChoices</c> read in as 31's start reads them (<see cref="RunInputsBuilder.Choices"/>, R7; a choice it refuses
    /// is left out, so the comparison shows what follows). 31's start (<see cref="OfflineRun"/>) then judges every value, so
    /// a supplied <c>" "</c> is not given, as the fixture README's scopes say.</item>
    /// <item><b>Profile:</b> the scenario's <c>profile</c> fields as given.</item>
    /// <item><b>Run facts:</b> <c>date</c> <see cref="CapturedDate"/>, <c>package</c> the plan's; the clock
    /// <see cref="CapturedAtUtc"/>.</item>
    /// <item><b>Outputs</b> (D12): each of the capture's <c>outputs</c> under its 31 row key (<see cref="KeyMap.To31"/>; one
    /// that cannot be mapped is left out, and the comparison reports the prompt it served under <c>#prompts@30/</c>), with
    /// what 30's engine added to the reply after it came (a <c>concept_list</c>'s <c>source</c>, #1504 ruling 3), and every
    /// member its 31 format lists (<see cref="Fit"/>). A reply the capture lacks is generated, seeded with 30's key
    /// (<see cref="KeyMap.To30"/>).</item>
    /// </list>
    /// </summary>
    public static OfflineRequest RequestFor(JsonObject scenario30, JsonObject capture30, Migration31Result migrated, RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(scenario30);
        ArgumentNullException.ThrowIfNull(capture30);
        ArgumentNullException.ThrowIfNull(migrated);
        ArgumentNullException.ThrowIfNull(plan);

        var renamed = RenamedInputs(migrated);
        var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (id30, value) in scenario30["inputs"] as JsonObject ?? [])
        {
            var id = renamed.GetValueOrDefault(id30, id30);
            var given = value?.DeepClone();
            if (plan.Inputs.TryGetValue(id, out var declared))
            {
                if (given is JsonValue && !Blank(given) && IsList(plan, declared["format"]))
                    given = new JsonArray(given);
                given = Fit(given, plan.Formats.Expand(declared["format"]), "input." + id);
            }

            inputs[id] = given;
        }

        var choices = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (id, value) in scenario30["macroChoices"] as JsonObject ?? [])
        {
            if (value is JsonValue choice && choice.TryGetValue<bool>(out var on))
                choices[id] = on;
        }

        var (supplied, _) = RunInputsBuilder.Choices(plan, inputs, choices);

        var profile = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (field, value) in scenario30["profile"] as JsonObject ?? [])
            profile[field] = SourceLayout.Text(value);

        var facts = new JsonObject();
        foreach (var fact in plan.RunFacts.Select(f => f.StartsWith("run.", StringComparison.Ordinal) ? f["run.".Length..] : f).Distinct(StringComparer.Ordinal))
        {
            facts[fact] = fact switch
            {
                "date" => CapturedDate,
                "package" => $"{plan.Package}@{plan.Version}",
                _ => null,
            };
        }

        var outputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (key30, value) in capture30["outputs"] as JsonObject ?? [])
        {
            if (KeyMap.To31(key30, migrated, plan) is not { } key31)
                continue;
            outputs[key31] = Fit(WithConceptSource(value?.DeepClone(), key30, plan, key31), FormatOf(plan, key31), key30);
        }

        return new OfflineRequest(
            new Dictionary<string, JsonNode?>(supplied, StringComparer.Ordinal),
            profile,
            facts,
            outputs,
            CapturedAtUtc,
            StubSeed: key31 => KeyMap.To30(key31, migrated, plan) ?? key31);
    }

    /// <summary>
    /// Every compared text of one scenario (D11), in this order: <c>#status</c>; the prompts, in the capture's order, then
    /// those only 31 sent, in 31's order; the rendered nodes' texts alike; the deliverables alike. A 30 text is held against the
    /// 31 text of its mapped key (<see cref="KeyMap.To31"/>); one whose key cannot be mapped keeps its 30 key under
    /// <c>#prompts@30/</c>, <c>#renders@30/</c> or <c>#deliverables@30/</c> (Review Focus 5: reported, not dropped). A
    /// deliverable's text is 30's <c>composed</c> text and 31's text when it was not withheld: a 31 deliverable its check
    /// refused (<see cref="DeliverableState.Blocked"/>) is never delivered, as 30's refused (<c>failed</c>) row holds no text
    /// (R4, ruling 3). A deliverable neither side has text for is not a compared text. <paramref name="request31"/>, the request
    /// the 31 run was given, is passed to the rules (R4 item 6 reads the inputs supplied).
    /// </summary>
    public static IReadOnlyList<ComparedText> Compare(
        string package, string scenario, JsonObject capture30, OfflineResult run31, Migration31Result migrated, RunPlan plan,
        OfflineRequest? request31 = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(capture30);
        ArgumentNullException.ThrowIfNull(run31);
        ArgumentNullException.ThrowIfNull(migrated);
        ArgumentNullException.ThrowIfNull(plan);

        var texts = new List<ComparedText>();
        var at = $"{package}/{scenario}#";

        void Add(ComparedKind kind, string site, string? key30, string? key31, (string Text, string Hash)? old, (string Text, string Hash)? now)
        {
            var equal = old is { } o && now is { } n && o.Hash == n.Hash && o.Text == n.Text;
            string? explanation = null;
            if (!equal)
            {
                explanation = ExpectedDifferences.Explain(new DifferenceContext(
                    package, scenario, at + site, kind, key30, key31, old?.Text, now?.Text, capture30, run31, migrated, plan, request31));
            }

            texts.Add(new ComparedText(at + site, old?.Hash ?? EmptyHash, now?.Hash ?? EmptyHash, equal, explanation != null, explanation));
        }

        var status30 = SourceLayout.Text(capture30["status"]) ?? "";
        var status31 = StatusOf(run31);
        Add(ComparedKind.Status, "status", null, null, (status30, CanonicalJson.HashText(status30)), (status31, CanonicalJson.HashText(status31)));

        var prompts31 = run31.Prompts.ToDictionary(p => p.Key, StringComparer.Ordinal);
        var renders31 = new Dictionary<string, OfflineText>(StringComparer.Ordinal);
        foreach (var render in run31.Renders)
            renders31.TryAdd(render.Key, render);

        foreach (var (section, kind, run) in new[] { ("prompts", ComparedKind.Prompt, prompts31), ("renders", ComparedKind.Render, renders31) })
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Rows(capture30[section]))
            {
                var key30 = SourceLayout.Text(row["key"]) ?? "";
                var old = (SourceLayout.Text(row["text"]) ?? "", SourceLayout.Text(row["hash"]) ?? "");
                if (KeyMap.To31(key30, migrated, plan) is not { } key31)
                {
                    Add(kind, $"{section}@30/{key30}", key30, null, old, null);
                    continue;
                }

                used.Add(key31);
                Add(kind, $"{section}/{key31}", key30, key31, old, run.TryGetValue(key31, out var sent) ? (sent.Text, sent.Hash) : null);
            }

            foreach (var text in kind == ComparedKind.Prompt ? run31.Prompts : run31.Renders)
            {
                if (used.Add(text.Key))
                    Add(kind, $"{section}/{text.Key}", KeyMap.To30(text.Key, migrated, plan), text.Key, null, (text.Text, text.Hash));
            }
        }

        var deliverables31 = run31.Deliverables.ToDictionary(d => d.Key, StringComparer.Ordinal);
        var delivered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows(capture30["deliverables"]))
        {
            var id = SourceLayout.Text(row["id"]) ?? "";
            var key30 = "deliverable." + id;
            (string, string)? old = SourceLayout.Text(row["text"]) is { } text30 ? (text30, SourceLayout.Text(row["hash"]) ?? CanonicalJson.HashText(text30)) : null;
            if (KeyMap.To31(key30, migrated, plan) is not { } key31)
            {
                if (old != null)
                    Add(ComparedKind.Deliverable, $"deliverables@30/{key30}", key30, null, old, null);
                continue;
            }

            delivered.Add(key31);
            var now = TextOf(deliverables31.GetValueOrDefault(key31));
            if (old != null || now != null)
                Add(ComparedKind.Deliverable, $"deliverables/{key31}", key30, key31, old, now);
        }

        foreach (var outcome in run31.Deliverables)
        {
            if (delivered.Add(outcome.Key) && TextOf(outcome) is { } now)
                Add(ComparedKind.Deliverable, $"deliverables/{outcome.Key}", KeyMap.To30(outcome.Key, migrated, plan), outcome.Key, null, now);
        }

        return texts;
    }

    /// <summary>31's status word: the run's state, or <see cref="Refused"/> when its start refused it.</summary>
    public static string StatusOf(OfflineResult run31)
    {
        ArgumentNullException.ThrowIfNull(run31);
        return run31.State?.ToString() ?? Refused;
    }

    /// <summary>A 31 deliverable's compared text and hash: none when skipped, failed with no text, or withheld (blocked).</summary>
    private static (string, string)? TextOf(DeliverableOutcome? outcome) =>
        outcome is { Text: { } text } && outcome.State != DeliverableState.Blocked
            ? (text, outcome.ContentHash ?? CanonicalJson.HashText(text))
            : null;

    private static IEnumerable<JsonObject> Rows(JsonNode? rows) => (rows as JsonArray ?? []).OfType<JsonObject>();

    /// <summary>The inputs the migration renamed (A10-006, A10-034, S10-006's <c>input</c> kind): 30's id to 31's.</summary>
    private static Dictionary<string, string> RenamedInputs(Migration31Result migrated)
    {
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var notice in migrated.Report.Renames)
        {
            const string head = "Renamed input '";
            if (!notice.StartsWith(head, StringComparison.Ordinal))
                continue;
            var end = notice.IndexOf('\'', head.Length);
            const string to = "' to '";
            if (end < 0 || string.CompareOrdinal(notice, end, to, 0, to.Length) != 0)
                continue;
            var newEnd = notice.IndexOf('\'', end + to.Length);
            if (newEnd > end + to.Length)
                renamed[notice[head.Length..end]] = notice[(end + to.Length)..newEnd];
        }

        return renamed;
    }

    /// <summary>Blank text (white space only, as <c>blank()</c> judges), which 31's start leaves not given.</summary>
    private static bool Blank(JsonNode value) =>
        SourceLayout.Text(value) is { } text && text.EnumerateRunes().All(System.Text.Rune.IsWhiteSpace);

    private static bool IsList(RunPlan plan, JsonNode? format) =>
        plan.Formats.Expand(format) is JsonObject expanded && TypeHas(expanded, "array");

    private static bool TypeHas(JsonObject schema, string type) => schema["type"] switch
    {
        JsonValue one => SourceLayout.Text(one) == type,
        JsonArray many => many.Any(t => SourceLayout.Text(t) == type),
        _ => false,
    };

    /// <summary>
    /// #1504 ruling 3 (area 07, R3): a 31 <c>concept_list</c>'s concepts each carry a required <c>source</c>, which 30's
    /// engine stamped on each concept after the reply (<c>ConceptOutputContract</c>: the node's well-known source,
    /// <see cref="WorkflowNodeDefaults.WellKnownConceptSources"/>, else its id), so a 30 reply holds none. The value 30's run
    /// held is replayed: each concept of a <c>concept_list</c> reply that has no <c>source</c> gets 30's stamp for the node of
    /// <paramref name="key30"/>.
    /// </summary>
    private static JsonNode? WithConceptSource(JsonNode? value, string key30, RunPlan plan, string key31)
    {
        if (!IsConceptList(plan, key31) || value is not JsonObject { } reply || reply["concepts"] is not JsonArray concepts)
            return value;
        var colon = key30.IndexOf(':', StringComparison.Ordinal);
        var node = colon < 0 ? key30 : key30[..colon];
        var source = WorkflowNodeDefaults.WellKnownConceptSources.GetValueOrDefault(node, node);
        foreach (var concept in concepts.OfType<JsonObject>())
        {
            if (!concept.ContainsKey("source"))
                concept["source"] = source;
        }

        return reply;
    }

    /// <summary>Whether the agent entry a 31 row key is an instance of has the registry's <c>concept_list</c> format.</summary>
    private static bool IsConceptList(RunPlan plan, string key31)
    {
        var end = key31.IndexOfAny([':', '#']);
        var entryKey = end < 0 ? key31 : key31[..end];
        return plan.Entries.TryGetValue(entryKey, out var entry)
            && entry.Format is JsonObject { Count: 1 } site
            && SourceLayout.Text(site["$ref"]) is { } reference
            && plan.Document["formats"]?["$defs"] is JsonObject bundle
            && reference.StartsWith("#/$defs/", StringComparison.Ordinal)
            && bundle[reference["#/$defs/".Length..]] is JsonObject format
            && SourceLayout.Text(format["$id"]) is { } id
            && id.StartsWith("urn:consultologist:format:concept_list@", StringComparison.Ordinal);
    }

    /// <summary>The expanded format of the agent entry a 31 row key is an instance of, or null.</summary>
    private static JsonNode? FormatOf(RunPlan plan, string key31)
    {
        var end = key31.IndexOfAny([':', '#']);
        var entryKey = end < 0 ? key31 : key31[..end];
        return plan.Entries.TryGetValue(entryKey, out var entry) && !plan.Formats.IsText(entry.Format) ? plan.Formats.Expand(entry.Format) : null;
    }

    /// <summary>
    /// <paramref name="value"/> with every member <paramref name="schema"/> (expanded) lists and the value lacks written in,
    /// at every depth (an object's properties, a list's items, the arm of an <c>anyOf</c> or <c>oneOf</c> of the value's
    /// kind): <c>null</c> where the member's schema takes <c>null</c> (part 10 § 1.8: every property required, an optional
    /// one nullable, so a member 30 left out is an explicit <c>null</c>), else a value generated from the member's schema
    /// (<see cref="WorkflowStubOutputs.FromSchema"/>, seeded by <paramref name="seed"/> and the member's path, as
    /// <c>$.concepts[0].source</c>): a member 31's format has and 30's had not, which no 30 reply can hold. A member the
    /// value has is kept as it is.
    /// </summary>
    internal static JsonNode? Fit(JsonNode? value, JsonNode? schema, string seed, string path = "$")
    {
        if (schema is not JsonObject shape || value is null)
            return value;

        switch (value)
        {
            case JsonObject obj when shape["properties"] is JsonObject properties:
                foreach (var (name, property) in properties)
                {
                    var at = $"{path}.{name}";
                    if (obj.TryGetPropertyValue(name, out var member))
                        obj[name] = Fit(member?.DeepClone(), property, seed, at);
                    else
                        obj[name] = TakesNull(property) ? null : WorkflowStubOutputs.FromSchema(property, seed + at);
                }

                return obj;
            case JsonArray list when shape["items"] is JsonObject items:
                for (var i = 0; i < list.Count; i++)
                    list[i] = Fit(list[i]?.DeepClone(), items, seed, $"{path}[{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}]");
                return list;
        }

        foreach (var arms in new[] { shape["anyOf"], shape["oneOf"], shape["allOf"] })
        {
            foreach (var arm in (arms as JsonArray ?? []).OfType<JsonObject>())
            {
                if ((value is JsonObject && arm["properties"] is JsonObject) || (value is JsonArray && arm["items"] is JsonObject))
                    return Fit(value, arm, seed, path);
            }
        }

        return value;
    }

    /// <summary>Whether <paramref name="schema"/> takes <c>null</c>: its <c>type</c> is or lists <c>"null"</c>, its <c>enum</c> holds it, or an arm of its <c>anyOf</c> or <c>oneOf</c> does.</summary>
    private static bool TakesNull(JsonNode? schema) => schema is JsonObject shape && (
        TypeHas(shape, "null")
        || (shape["enum"] is JsonArray members && members.Any(m => m is null))
        || new[] { shape["anyOf"], shape["oneOf"] }.Any(arms => (arms as JsonArray ?? []).Any(TakesNull)));
}
