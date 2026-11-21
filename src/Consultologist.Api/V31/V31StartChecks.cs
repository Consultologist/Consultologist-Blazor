using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.2 step 6, 5.3, 7.2; part 02 § 8; part 06 § 10.4; R1, R4, R5; D6): what a 31
/// job's start checks against its plan, pure, so area 12's Create page says the same sentences before submission as the
/// engine says at the start: the account (<see cref="Account"/>: the profile fields the package needs, then the time zone
/// every 31 job needs), the inputs (<see cref="Inputs"/>), and the run facts that follow from the start
/// (<see cref="RunFacts"/>). No sentence quotes a supplied value or a profile's: only input ids, format names and fixed text.
/// </summary>
public static class V31StartChecks
{
    /// <summary>S06-008: the account has no time zone this engine knows.</summary>
    public const string NoTimeZone = "Set your time zone in your account settings before running a workflow.";

    /// <summary>
    /// The account's refusals for <paramref name="plan"/>: <see cref="RequiresCheck.Profile"/>'s for each field
    /// <c>needs.profile</c> lists that <paramref name="profile"/> has missing or blank (S06-031, S06-032), then S06-008
    /// when <paramref name="timeZone"/> is blank or is not an IANA zone this machine's zone database finds.
    /// </summary>
    public static IReadOnlyList<SourceRefusal> Account(RunPlan plan, IReadOnlyDictionary<string, string?> profile, string? timeZone)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(profile);

        var refusals = RequiresCheck.Profile(plan.Document, profile).ToList();
        if (!IsZone(timeZone))
            refusals.Add(new SourceRefusal("S06-008", NoTimeZone, ""));
        return refusals;
    }

    /// <summary>
    /// The run's inputs of <paramref name="supplied"/> (each input's value by id, as sent) for <paramref name="plan"/>, or the
    /// refusals of starting with them. For each input in <c>inputOrder</c>: a value absent, <c>null</c>, or a string that is
    /// blank (as <c>blank()</c> judges, R5) is left blank, which a required input refuses with A06-011 and an optional one
    /// reads as <c>null</c> (absent from the inputs); any other value is held to the input's format (numeric enums
    /// included, R4), and one that does not fit is A06-012. An empty list is a value like any other (R1). A supplied id the
    /// package does not declare is refused as today's start refuses it (A06-013, every such id at once). An input's read
    /// form is <c>{ value, content, title }</c> (part 02 § 8): <c>content</c> the text of a text format, <c>title</c> the label
    /// of the labelled choice chosen, each else <c>null</c>.
    /// </summary>
    public static (RunInputs? Inputs, IReadOnlyList<SourceRefusal> Refusals) Inputs(
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

            if (!plan.Formats.Validate(value, format).Valid)
            {
                refusals.Add(new SourceRefusal("A06-012", $"Input '{id}' was given a value that isn't {FormatName(format)}.", ""));
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

    /// <summary>
    /// The run facts of <paramref name="plan"/> that follow from the plan and the start, by name with <c>run.</c> dropped
    /// (part 03 § 4.3; D6): <c>run.date</c>, the ISO date of <paramref name="startedAtUtc"/> in <paramref name="timeZone"/>
    /// (UTC with no zone or one this machine does not know: the start refuses a job without one before it gets here), and
    /// <c>run.package</c>, <c>&lt;name&gt;@&lt;version&gt;</c>. A fact the plan does not read is not given; <c>run.id</c> and
    /// <c>run.engine</c> are the job's and the engine's, which the start supplies.
    /// </summary>
    public static JsonObject RunFacts(RunPlan plan, DateTimeOffset startedAtUtc, string? timeZone)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var facts = new JsonObject();
        foreach (var fact in plan.RunFacts)
        {
            switch (fact)
            {
                case "run.date":
                    facts["date"] = DateIn(startedAtUtc, timeZone);
                    break;
                case "run.package":
                    facts["package"] = $"{plan.Package}@{plan.Version}";
                    break;
            }
        }

        return facts;
    }

    /// <summary>The ISO date (<c>yyyy-MM-dd</c>) of <paramref name="at"/> in <paramref name="timeZone"/>, or in UTC.</summary>
    public static string DateIn(DateTimeOffset at, string? timeZone)
    {
        var local = timeZone is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? TimeZoneInfo.ConvertTime(at, zone)
            : at.ToUniversalTime();
        return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Whether <paramref name="timeZone"/> names an IANA zone this machine's zone database has.</summary>
    private static bool IsZone(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
        && zone.HasIanaId;

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
