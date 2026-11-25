using System.Text;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 1.2 step 6; part 02 § 8; R1, R4, R5), moved into the library by #1507 (area
/// 10, Task 13) from the Api's <c>V31StartChecks.Inputs</c>, which now calls it: a 31 job's raw inputs made into the run's
/// inputs, with the same refusals (A06-011 to A06-013, and A10-052 for a <c>markdown_inline</c> text holding a line ending),
/// so the engine's start, the offline run (<see cref="Harness.OfflineRun"/>) and area 11's in-package tests build them
/// alike. No sentence quotes a supplied value: only input ids and format names.
/// </summary>
public static class RunInputsBuilder
{
    /// <summary>
    /// The run's inputs of <paramref name="supplied"/> (each input's value by id, as sent) for <paramref name="plan"/>, or the
    /// refusals of starting with them. For each input in <c>inputOrder</c>: a value absent, <c>null</c>, or a string that is
    /// blank (as <c>blank()</c> judges, R5) is left blank, which a required input refuses with A06-011 and an optional one
    /// reads as <c>null</c> (absent from the inputs); any other value is held to the input's format (numeric enums
    /// included, R4), and one that does not fit is A06-012 (A10-052 when only a <c>markdown_inline</c> text's line ending
    /// keeps it from fitting, D21). An empty list is a value like any other (R1). A supplied id the package does not
    /// declare is refused as today's start refuses it (A06-013, every such id at once). An input's read form is
    /// <c>{ value, content, title }</c> (part 02 § 8): <c>content</c> the text of a text format, <c>title</c> the label of
    /// the labelled choice chosen, each else <c>null</c>. <paramref name="runFacts"/> and <paramref name="profile"/> are
    /// carried as given (copied).
    /// </summary>
    public static (RunInputs? Inputs, IReadOnlyList<SourceRefusal> Refusals) Build(
        RunPlan plan, IReadOnlyDictionary<string, JsonNode?> supplied, JsonObject runFacts, IReadOnlyDictionary<string, string?> profile)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(supplied);
        ArgumentNullException.ThrowIfNull(runFacts);
        ArgumentNullException.ThrowIfNull(profile);

        var refusals = new List<SourceRefusal>();
        var unknown = supplied.Keys.Where(id => !plan.Inputs.ContainsKey(id)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            refusals.Add(new SourceRefusal(
                "A06-013",
                $"Unknown input(s) {string.Join(", ", unknown.Select(id => $"'{id}'"))} (declared: {string.Join(", ", plan.InputOrder.Order(StringComparer.Ordinal))}).",
                ""));
        }

        var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var id in plan.InputOrder)
        {
            var declared = plan.Inputs[id];
            var format = declared["format"];
            var value = supplied.GetValueOrDefault(id);
            if (LeftBlank(value))
            {
                if (declared["required"] is JsonValue required && required.TryGetValue<bool>(out var isRequired) && isRequired)
                    refusals.Add(new SourceRefusal("A06-011", $"Input '{id}' is required; fill it in to run this workflow.", ""));
                continue;
            }

            if (plan.Formats.Validate(value, format) is { Valid: false } fits)
            {
                // #1507 (R10, D21): a markdown_inline text holding a line ending does not fit; A10-052 says why.
                refusals.Add(fits.BreaksALine
                    ? new SourceRefusal("A10-052", RunSentences.InputBreaksALine(id, FormatName(format)), "")
                    : new SourceRefusal("A06-012", $"Input '{id}' was given a value that isn't {FormatName(format)}.", ""));
                continue;
            }

            inputs[id] = new JsonObject
            {
                ["value"] = value!.DeepClone(),
                ["content"] = plan.Formats.IsText(format) ? value.DeepClone() : null,
                ["title"] = TitleOf(plan.Formats.Expand(format), value),
            };
        }

        return refusals.Count > 0
            ? (null, refusals)
            : (new RunInputs(inputs, (JsonObject)runFacts.DeepClone(), new Dictionary<string, string?>(profile, StringComparer.Ordinal)), refusals);
    }

    /// <summary>A10-043 (#1507; part 10 § 1.3, R7): a <c>MacroChoices</c> entry names no macro of the package.</summary>
    public static string ChoiceNamesNoMacro(string id) => $"MacroChoices names '{id}', which is not a macro of the package.";

    /// <summary>A10-044 (#1507; part 10 § 1.3, R7): a <c>MacroChoices</c> entry names a macro whose <c>if</c> reads no boolean input, or several.</summary>
    public static string ChoiceReadsNoOneInput(string id) =>
        $"MacroChoices names macro '{id}', whose if does not read exactly one boolean input; send the input in Inputs instead.";

    /// <summary>
    /// #1507 (specVersion 31, area 10; ruled by the owner on #1507, 2026-11-22, 3; moved from the Api's <c>V31StartChecks.Choices</c> by Task 15; part 10 § 1.3; part 05 § 8.2: a per-run
    /// choice is an optional boolean input the macro's <c>if</c> reads): <paramref name="supplied"/> with the request's
    /// <paramref name="choices"/> (<c>MacroChoices</c>) read in, for <see cref="Build"/> to check. An entry
    /// <c>&lt;id&gt;: &lt;bool&gt;</c> is keyed by a macro's id, as at 30: it is the value of the one boolean input that
    /// macro's <c>if</c> reads (the plan's <c>macro.&lt;id&gt;</c> entry in <c>resolve.json</c>, the <c>input.*</c> paths of
    /// its <c>if</c>), exactly as if the request had sent that input; the input may have another id (a migrated
    /// <c>letrozole</c>'s is <c>letrozole_macro</c> when an input <c>letrozole</c> exists, which the entry leaves alone). An id
    /// that names no macro is A10-043; a macro whose <c>if</c> reads no boolean input, or more than one, is A10-044; an input
    /// the request also sent (as a value other than <c>null</c>) with a different value is A07-016. Each names an id alone,
    /// never a value; entries are read in ordinal order of their ids.
    /// </summary>
    public static (IReadOnlyDictionary<string, JsonNode?> Supplied, IReadOnlyList<SourceRefusal> Refusals) Choices(
        RunPlan plan, IReadOnlyDictionary<string, JsonNode?> supplied, IReadOnlyDictionary<string, bool>? choices)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(supplied);

        var merged = new Dictionary<string, JsonNode?>(supplied, StringComparer.Ordinal);
        var refusals = new List<SourceRefusal>();
        var macros = plan.Document["macros"] as JsonObject;
        foreach (var (id, choice) in (choices ?? new Dictionary<string, bool>()).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (macros?[$"macro.{id}"] is not JsonObject macro)
            {
                refusals.Add(new SourceRefusal("A10-043", ChoiceNamesNoMacro(id), ""));
                continue;
            }

            var read = JsonLogicShape.Paths(macro["if"])
                .Select(path => path.Split('.'))
                .Where(path => path is ["input", _, ..])
                .Select(path => path[1])
                .Where(input => plan.Inputs.TryGetValue(input, out var declared) && IsBoolean(plan, declared["format"]))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (read is not [var input])
            {
                refusals.Add(new SourceRefusal("A10-044", ChoiceReadsNoOneInput(id), ""));
                continue;
            }

            if (supplied.GetValueOrDefault(input) is { } sent)
            {
                if (!JsonNode.DeepEquals(sent, JsonValue.Create(choice)))
                    refusals.Add(new SourceRefusal("A07-016", $"Input '{input}' is given one value in Inputs and another in MacroChoices; send it once.", ""));
                continue;
            }

            merged[input] = JsonValue.Create(choice);
        }

        return (merged, refusals);
    }

    /// <summary>
    /// #1505 (specVersion 31, area 08; R8), moved from the Api's <c>V31StartChecks.NoDeliverableCanExist</c> by #1507 (area 10,
    /// Task 15) so the offline run refuses as the engine's start does: whether no deliverable of <paramref name="plan"/> can
    /// exist on <paramref name="inputs"/>, so the start is refused as a 30 start is (<c>NoApplicableDeliverable</c>): the plan
    /// has a deliverable, every deliverable has an <c>if</c>, each reads only <c>input.*</c> and the run facts
    /// <paramref name="inputs"/> gives, and each decides false or <c>null</c> on them. An <c>if</c> that reads anything else
    /// (a node, a run fact the start does not know), or that fails, is left to the run.
    /// </summary>
    public static bool NoDeliverableCanExist(RunPlan plan, RunInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inputs);

        var deliverables = DeliverablesOf(plan);
        if (deliverables.Count == 0)
            return false;

        var scope = new RunScope(inputs);
        var top = RunFrame.Top(plan);
        foreach (var deliverable in deliverables)
        {
            if (deliverable.If is not { } rule)
                return false;

            var paths = JsonLogicShape.Paths(deliverable.Json["if"]);
            if (!paths.All(path => KnownAtStart(path, inputs)))
                return false;

            var decision = rule.Decide(scope.Data(deliverable, top, paths));
            if (decision.Failure != null || decision.Result)
                return false;
        }

        return true;
    }

    /// <summary>
    /// #1505 (R8): the deliverables of <paramref name="plan"/> in <c>graph.order</c>, each by key and label with the reason it
    /// is not produced: its <c>if</c> as S03-001 prints it (<c>skipped: input.kind.value = "table" is false</c>), the
    /// record's sentence for a deliverable its <c>if</c> skips. Package text only. Read after
    /// <see cref="NoDeliverableCanExist"/>.
    /// </summary>
    public static IReadOnlyList<(string Key, string Label, string Reason)> NotProduced(RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return DeliverablesOf(plan)
            .Select(d => (d.Key, SourceLayout.Text(d.Json["label"]) ?? d.Id, JsonLogicPrinter.Reason("skipped", d.Json["if"], false)))
            .ToList();
    }

    /// <summary>
    /// #1507 (area 10, Task 15; A10-062): the start's refusal when no deliverable can exist, in the words a 30 start and a
    /// 31 start say it (the Api's <c>ConsultGenerationJobStarter.NoneApplyDetail</c>): <c>No document applies to these
    /// inputs.</c> then <c>'&lt;label&gt;' &lt;reason&gt;.</c> for each of <see cref="NotProduced"/>. Package text only.
    /// </summary>
    public static string NoneApply(RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return "No document applies to these inputs. " + string.Join(" ", NotProduced(plan).Select(d => $"'{d.Label}' {d.Reason}."));
    }

    private static List<PlanEntry> DeliverablesOf(RunPlan plan) =>
        plan.Entries.Values.Where(e => e.Kind == PlanKind.Deliverable).OrderBy(e => e.Order).ToList();

    /// <summary>Whether <paramref name="path"/> is known at the start: an input's, or a run fact <paramref name="inputs"/> holds.</summary>
    private static bool KnownAtStart(string path, RunInputs inputs) =>
        path.Split('.') switch
        {
            ["input", _, ..] => true,
            ["run", var fact, ..] => inputs.RunFacts.ContainsKey(fact),
            _ => false,
        };

    /// <summary>A value left blank: absent or <c>null</c>, or a string of nothing but white space, as <c>blank()</c> judges.</summary>
    private static bool LeftBlank(JsonNode? value) =>
        value == null || (SourceLayout.Text(value) is { } text && text.EnumerateRunes().All(Rune.IsWhiteSpace));

    /// <summary>The title of the labelled-choice arm of <paramref name="expanded"/> whose <c>const</c> is <paramref name="value"/>, else null.</summary>
    private static string? TitleOf(JsonNode expanded, JsonNode? value) =>
        (expanded as JsonObject)?["oneOf"] is JsonArray arms
            ? arms.OfType<JsonObject>().FirstOrDefault(arm => JsonNode.DeepEquals(arm["const"], value)) is { } chosen
                ? SourceLayout.Text(chosen["title"])
                : null
            : null;

    /// <summary>Whether <paramref name="format"/>, expanded against <paramref name="plan"/>'s bundle, is a boolean (part 05 § 8.2).</summary>
    private static bool IsBoolean(RunPlan plan, JsonNode? format) =>
        plan.Formats.Expand(format) is JsonObject expanded && SourceLayout.Text(expanded["type"]) == "boolean";

    /// <summary>A format's name in a sentence: the bundle key a <c>$ref</c> names, else <c>its format</c> (as the runner names one).</summary>
    private static string FormatName(JsonNode? format) =>
        format is JsonObject { Count: 1 } site && SourceLayout.Text(site["$ref"]) is { } reference
        && reference.StartsWith("#/$defs/", StringComparison.Ordinal)
            ? reference["#/$defs/".Length..]
            : "its format";
}
