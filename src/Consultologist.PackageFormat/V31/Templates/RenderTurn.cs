using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 4, 9; part 08 §§ 4, 4.2, 8.2, 10): one render of a rendered node or a
/// deliverable, with no Azure in between: its body composed over the call's scope with the call's placements decided, as
/// the run reads it back. #1505 (area 08; part 06 § 6.4; D8): a deliverable of an object format is its one value's
/// canonical JSON instead. Never puts a value in the detail: only ids, keys, hashes and lengths. Never throws on the plan or
/// the data.
/// </summary>
public static class RenderTurn
{
    private static readonly IReadOnlyDictionary<string, bool> NothingPlaced = new Dictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>
    /// The render <paramref name="call"/> asks for (<see cref="BodyComposer.Compose"/> of its entry, its
    /// <see cref="RenderCall.Placed"/> decided, in at most its <see cref="RenderCall.StepLimit"/> steps): its text and its
    /// run-neutral text, and the row's detail in part 08's names: <c>inputHash</c> (of the text), <c>runNeutralHash</c>
    /// (<c>null</c> with no run spot), <c>runSpots</c>, <c>renders</c> and <c>units</c>, sub-rows named by the row's key
    /// (<see cref="RenderCall.Key"/>). A render past its budget is <see cref="RenderOutcome.TooManySteps"/>; one that
    /// failed otherwise carries the composer's sentence. A deliverable whose format is an object format is
    /// <see cref="ObjectText"/>'s.
    /// </summary>
    public static RenderOutcome Render(RunPlan plan, RenderCall call)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(call);

        Composition composition;
        var deliverable = call.Function == null && plan.Entries.GetValueOrDefault(call.EntryKey) is { Kind: PlanKind.Deliverable } entry ? entry : null;
        if (deliverable != null && plan.Formats.IsObject(deliverable.Format))
        {
            composition = ObjectText(plan, deliverable, call.Scope);
        }
        else
        {
            composition = BodyComposer.Compose(plan, new ComposeRequest(call.EntryKey, call.Function, call.Scope, call.Placed ?? NothingPlaced, call.StepLimit));
        }

        if (composition.TooManySteps)
            return new RenderOutcome(null, true, null);
        if (composition.Text is not { } text)
            return new RenderOutcome(null, false, composition.Failure) { EngineFault = composition.EngineFault };

        var detail = new JsonObject
        {
            ["inputHash"] = CanonicalJson.HashText(text),
            ["runNeutralHash"] = composition.NeutralText is { } neutral ? CanonicalJson.HashText(neutral) : null,
            ["runSpots"] = AgentTurn.SpotRows(call.Key, composition),
            ["renders"] = AgentTurn.RenderRows(call.Key, composition),
            ["units"] = AgentTurn.UnitRows(composition),
        };

        // #1506 (area 09; D2): the text the render produced beside its text, never in the detail
        var produced = new JsonObject { ["neutral"] = composition.NeutralText, ["renders"] = AgentTurn.RenderTexts(call.Key, composition) };
        if (deliverable != null)
        {
            // #1506 (area 09; part 08 §§ 9, 10; D6): a deliverable's sites and substitutions, and its profile reads by hash;
            // each substitution's value beside the row, at its index
            detail["sites"] = BodyComposer.SiteRows(composition.Sites);
            detail["substitutions"] = BodyComposer.SubstitutionRows(call.Key, composition.Substitutions);
            detail["profileReads"] = ProfileReads(composition.Substitutions, call.Scope);
            produced["substitutions"] = new JsonArray([.. composition.Substitutions.Select(s => (JsonNode)s.Value)]);
        }

        return new RenderOutcome(text, false, null, composition.NeutralText, detail, produced);
    }

    /// <summary>
    /// D8: the object deliverable <paramref name="entry"/>'s text: the RFC 8785 JSON of the value its one output tag reads
    /// (<c>node.&lt;x&gt;.value</c>, its head rooted through <c>names</c>, D1) in <paramref name="scope"/>, with no final line
    /// end; <c>null</c> writes <c>null</c>; past <see cref="BodyComposer.OutputLimit"/> bytes of UTF-8, S05-014, as any
    /// composed text is. No run spot, renderer or unit. A body not of <see cref="SourceTemplates.OneValueRead"/>'s form is not
    /// what publish wrote (S07-006), unless the engine failed reading it (A08-006: A11-008); a read the scope lacks fails as an absent member does in any body (A06-008).
    /// </summary>
    private static Composition ObjectText(RunPlan plan, PlanEntry entry, JsonObject scope)
    {
        // The form publish held the body to (S06-023), by the same definition.
        var syntax = SourceLayout.Text(entry.Json["body"]) is { } body ? TemplateSyntax.Parse(body) : null;
        if ((syntax == null ? null : SourceTemplates.OneValueRead(syntax)) is not { } path)
        {
            // The engine failing to read the body (A08-006) is its own fault, A11-008 (#1508), never A06-008 or S07-006 (#1505 R14).
            return syntax?.Faults.FirstOrDefault(f => f.Id == "A08-006")?.Fill.FirstOrDefault() is { } fault
                ? Failed(RunSentences.EngineFailedRendering(RunSentences.Subject(entry, null))) with { EngineFault = fault }
                : Failed(ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
        }

        var segments = path.Split('.');
        var head = string.Join('.', segments[..2]);
        var rooted = SourceLayout.Text((entry.Json["names"] as JsonObject)?[head]) ?? head;
        JsonNode? at = scope;
        foreach (var segment in rooted.Split('.').Concat(segments[2..]))
        {
            if (at is not JsonObject holder || !holder.TryGetPropertyValue(segment, out at))
                return Failed(RunSentences.CouldNotRender(RunSentences.Subject(entry, null)));
        }

        // Part 05 § 2.3 (S05-014): the text is held to the composition's output limit as any composed text is.
        var text = CanonicalJson.Write(at);
        if (Encoding.UTF8.GetByteCount(text) > BodyComposer.OutputLimit)
            return Failed(RunSentences.OutputPassedLimit(RunSentences.Subject(entry, null)));
        // #1506 (area 09; part 08 § 10): its one tag is its one site, and its whole text that site's one substitution
        return new Composition(text, null, null, false, [], [], [], [])
        {
            Sites = BodyComposer.Sites(syntax!),
            Substitutions = [BodyComposer.Whole(text, string.Join('.', rooted.Split('.').Concat(segments[2..])))],
        };
    }

    /// <summary>
    /// #1506 (area 09; part 08 § 9): each <c>profile.&lt;field&gt;</c> a substitution read, once, in order: <c>{ field, hash }</c>,
    /// the hash of the value <paramref name="scope"/> holds for it (of its text, or of its canonical JSON when it is not text),
    /// as a macro's profile reads are; never the value.
    /// </summary>
    private static JsonArray ProfileReads(IReadOnlyList<Substitution> substitutions, JsonObject scope)
    {
        var reads = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var substitution in substitutions)
        {
            if (substitution.Reads.Split('.') is not ["profile", var field, ..] || !seen.Add(field))
                continue;
            var value = (scope["profile"] as JsonObject)?[field];
            var hash = value is JsonValue text && text.TryGetValue<string>(out var written) ? CanonicalJson.HashText(written) : CanonicalJson.Hash(value);
            reads.Add(new JsonObject { ["field"] = field, ["hash"] = hash });
        }

        return reads;
    }

    private static Composition Failed(string sentence) => new(null, null, sentence, false, [], [], [], []);
}
