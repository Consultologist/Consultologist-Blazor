using System.Globalization;
using System.Text.Json.Nodes;
using Consultologist.Api.Auth;
using Consultologist.Api.Models;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.2 step 6, 5.3, 7.2; part 02 § 8; part 06 § 10.4; R1, R4, R5; D6): what a 31
/// job's start checks against its plan, pure, so area 12's Create page says the same sentences before submission as the
/// engine says at the start: the account (<see cref="Account"/>: the profile fields the package needs, then the time zone
/// every 31 job needs), the inputs (<see cref="Inputs"/>), and the run facts that follow from the start
/// (<see cref="RunFacts"/>). #1505 (area 08; D15, R8): the signature block the job signs with (<see cref="Signature"/>), and
/// whether no deliverable can exist on the inputs (<see cref="NoDeliverableCanExist"/>). No sentence quotes a supplied value or
/// a profile's: only input ids, format names, block ids, package text and fixed text.
/// </summary>
public static class V31StartChecks
{
    /// <summary>S06-008: the account has no time zone this engine knows.</summary>
    public const string NoTimeZone = "Set your time zone in your account settings before running a workflow.";

    /// <summary>A08-004 (#1505, area 08; D15): the request names a signature block the account does not have.</summary>
    public const string SignatureNotInProfile = "The signature chosen for this job isn't in your profile. Choose one in Profile.";

    /// <summary><c>signatureChosen</c> for the account's chosen block (part 08 § 3).</summary>
    public const string ChosenByDefault = "default";

    /// <summary><c>signatureChosen</c> for another block of the account, named by the request (part 08 § 3).</summary>
    public const string ChosenForTheJob = "job";

    /// <summary>
    /// #1505 (specVersion 31, area 08; part 06 § 10.3; part 08 § 3; D15): the signature block a job reading
    /// <c>profile.signature</c> signs with, of the account's <paramref name="blocks"/>. With no block <paramref name="requested"/>
    /// (null or blank) it is the chosen one (<c>ChosenId</c>), <c>"default"</c>; none chosen, there is none (both null, and the
    /// start says S06-031). A requested id that is a block of the account is that block: <c>"default"</c> when it is the chosen
    /// one, else <c>"job"</c>. Any other id is refused with A08-004 (both null). A blank block is returned as found: its text
    /// counts as not set, which the profile check says (S06-031). Ids are compared ordinally; no text is read.
    /// </summary>
    public static (SignatureBlocks.SignatureBlock? Block, string? Chosen, string? Refusal) Signature(
        SignatureBlocks.SignatureBlockSet blocks, string? requested)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        if (string.IsNullOrWhiteSpace(requested))
            return SignatureBlocks.Chosen(blocks) is { } chosen ? (chosen, ChosenByDefault, null) : (null, null, null);

        var named = blocks.Blocks.FirstOrDefault(block => string.Equals(block.Id, requested, StringComparison.Ordinal));
        if (named == null)
            return (null, null, SignatureNotInProfile);
        return (named, string.Equals(named.Id, blocks.ChosenId, StringComparison.Ordinal) ? ChosenByDefault : ChosenForTheJob, null);
    }

    /// <summary>
    /// #1505 (specVersion 31, area 08; R8): whether no deliverable of <paramref name="plan"/> can exist on
    /// <paramref name="inputs"/>, so the start is refused as a 30 start is (<c>NoApplicableDeliverable</c>): the plan has a
    /// deliverable, every deliverable has an <c>if</c>, each reads only <c>input.*</c> and the run facts
    /// <paramref name="inputs"/> gives, and each decides false or <c>null</c> on them. An <c>if</c> that reads anything else
    /// (a node, a run fact the start does not know), or that fails, is left to the run. #1507 (area 10, Task 15): decided in
    /// the library by <see cref="RunInputsBuilder.NoDeliverableCanExist"/>, so the offline run refuses alike.
    /// </summary>
    public static bool NoDeliverableCanExist(RunPlan plan, RunInputs inputs) => RunInputsBuilder.NoDeliverableCanExist(plan, inputs);

    /// <summary>
    /// #1505 (R8): the deliverables of <paramref name="plan"/> in <c>graph.order</c>, each by key and label with the reason it
    /// is not produced: its <c>if</c> as S03-001 prints it (<c>skipped: input.kind.value = "table" is false</c>), the
    /// record's sentence for a deliverable its <c>if</c> skips. Package text only. Read after
    /// <see cref="NoDeliverableCanExist"/>.
    /// </summary>
    public static IReadOnlyList<ConsultSkippedDocument> NotProduced(RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return RunInputsBuilder.NotProduced(plan).Select(d => new ConsultSkippedDocument(d.Key, d.Label, d.Reason)).ToList();
    }

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
    /// of the labelled choice chosen, each else <c>null</c>. #1507 (area 10, Task 13): built in the library by
    /// <see cref="RunInputsBuilder.Build"/>, so the offline run builds them alike. #1509 (area 12, D7): <paramref name="modelDerived"/>
    /// names the inputs holding an earlier run's text (<see cref="RunInputs.ModelDerivedInputs"/>).
    /// </summary>
    public static (RunInputs? Inputs, IReadOnlyList<SourceRefusal> Refusals) Inputs(
        RunPlan plan, IReadOnlyDictionary<string, JsonNode?> supplied, JsonObject runFacts, IReadOnlyDictionary<string, string?> profile,
        IReadOnlySet<string>? modelDerived = null) =>
        RunInputsBuilder.Build(plan, supplied, runFacts, profile, modelDerived);

    /// <summary>A10-043 (#1507; part 10 § 1.3, R7): a <c>MacroChoices</c> entry names no macro of the package.</summary>
    public static string ChoiceNamesNoMacro(string id) => RunInputsBuilder.ChoiceNamesNoMacro(id);

    /// <summary>A10-044 (#1507; part 10 § 1.3, R7): a <c>MacroChoices</c> entry names a macro whose <c>if</c> reads no boolean input, or several.</summary>
    public static string ChoiceReadsNoOneInput(string id) => RunInputsBuilder.ChoiceReadsNoOneInput(id);

    /// <summary>
    /// #1507 (specVersion 31, area 10; ruled by the owner on #1507, 2026-11-22, 3; part 10 § 1.3): <paramref name="supplied"/>
    /// with the request's <paramref name="choices"/> (<c>MacroChoices</c>) read in, for <see cref="Inputs"/> to check
    /// (A10-043, A10-044, A07-016). #1507 (area 10, Task 15): built in the library by <see cref="RunInputsBuilder.Choices"/>,
    /// so the byte-equivalence comparison reads a scenario's choices alike.
    /// </summary>
    public static (IReadOnlyDictionary<string, JsonNode?> Supplied, IReadOnlyList<SourceRefusal> Refusals) Choices(
        RunPlan plan, IReadOnlyDictionary<string, JsonNode?> supplied, IReadOnlyDictionary<string, bool>? choices) =>
        RunInputsBuilder.Choices(plan, supplied, choices);

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

    /// <summary>
    /// #1506 (specVersion 31, area 09; part 08 § 8.1): the header's <c>runFacts</c>: of <paramref name="facts"/> (the run's,
    /// by name with <c>run.</c> dropped), exactly those <paramref name="plan"/> reads, in its order; <c>date</c> recorded as
    /// <c>{ value, timeZone }</c>, the zone's IANA name the date was taken in (<c>UTC</c> when the job had none this machine
    /// knows, as <see cref="DateIn"/> takes it). <c>{}</c> when it reads none.
    /// </summary>
    public static JsonObject RecordedRunFacts(RunPlan plan, JsonObject facts, string? timeZone)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(facts);
        var recorded = new JsonObject();
        foreach (var fact in plan.RunFacts)
        {
            var name = fact.StartsWith("run.", StringComparison.Ordinal) ? fact["run.".Length..] : fact;
            if (recorded.ContainsKey(name) || !facts.TryGetPropertyValue(name, out var value))
                continue;
            recorded[name] = name == "date"
                ? new JsonObject { ["value"] = value?.DeepClone(), ["timeZone"] = ZoneOf(timeZone) }
                : value?.DeepClone();
        }

        return recorded;
    }

    /// <summary>The IANA name of the zone <see cref="DateIn"/> takes a date in: <paramref name="timeZone"/>'s, or <c>UTC</c>.</summary>
    private static string ZoneOf(string? timeZone) =>
        timeZone is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? (zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id)
            : "UTC";

    /// <summary>The ISO date (<c>yyyy-MM-dd</c>) of <paramref name="at"/> in <paramref name="timeZone"/>, or in UTC.</summary>
    public static string DateIn(DateTimeOffset at, string? timeZone)
    {
        var local = timeZone is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? TimeZoneInfo.ConvertTime(at, zone)
            : at.ToUniversalTime();
        return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Whether <paramref name="timeZone"/> names an IANA zone this machine's zone database has (what S06-008 asks of an account).</summary>
    public static bool IsZone(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
        && zone.HasIanaId;
}
