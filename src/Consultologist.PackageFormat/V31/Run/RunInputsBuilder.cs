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

    /// <summary>A format's name in a sentence: the bundle key a <c>$ref</c> names, else <c>its format</c> (as the runner names one).</summary>
    private static string FormatName(JsonNode? format) =>
        format is JsonObject { Count: 1 } site && SourceLayout.Text(site["$ref"]) is { } reference
        && reference.StartsWith("#/$defs/", StringComparison.Ordinal)
            ? reference["#/$defs/".Length..]
            : "its format";
}
