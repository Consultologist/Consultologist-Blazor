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
        if (call.Function == null && plan.Entries.GetValueOrDefault(call.EntryKey) is { Kind: PlanKind.Deliverable } deliverable
            && plan.Formats.IsObject(deliverable.Format))
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
            return new RenderOutcome(null, false, composition.Failure);

        var detail = new JsonObject
        {
            ["inputHash"] = CanonicalJson.HashText(text),
            ["runNeutralHash"] = composition.NeutralText is { } neutral ? CanonicalJson.HashText(neutral) : null,
            ["runSpots"] = AgentTurn.SpotRows(call.Key, composition),
            ["renders"] = AgentTurn.RenderRows(call.Key, composition),
            ["units"] = AgentTurn.UnitRows(composition),
        };
        return new RenderOutcome(text, false, null, composition.NeutralText, detail);
    }

    /// <summary>
    /// D8: the object deliverable <paramref name="entry"/>'s text: the RFC 8785 JSON of the value its one output tag reads
    /// (<c>node.&lt;x&gt;.value</c>, its head rooted through <c>names</c>, D1) in <paramref name="scope"/>, with no final line
    /// end; <c>null</c> writes <c>null</c>. No run spot, renderer or unit. A body not of <see cref="SourceTemplates.OneValueRead"/>'s form is not
    /// what publish wrote (S07-006); a read the scope lacks fails as an absent member does in any body (A06-008).
    /// </summary>
    private static Composition ObjectText(RunPlan plan, PlanEntry entry, JsonObject scope)
    {
        // The form publish held the body to (S06-023), by the same definition.
        if ((SourceLayout.Text(entry.Json["body"]) is { } body ? SourceTemplates.OneValueRead(TemplateSyntax.Parse(body)) : null) is not { } path)
            return Failed(ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);

        var segments = path.Split('.');
        var head = string.Join('.', segments[..2]);
        var rooted = SourceLayout.Text((entry.Json["names"] as JsonObject)?[head]) ?? head;
        JsonNode? at = scope;
        foreach (var segment in rooted.Split('.').Concat(segments[2..]))
        {
            if (at is not JsonObject holder || !holder.TryGetPropertyValue(segment, out at))
                return Failed(RunSentences.CouldNotRender(RunSentences.Subject(entry, null)));
        }

        return new Composition(CanonicalJson.Write(at), null, null, false, [], [], [], []);
    }

    private static Composition Failed(string sentence) => new(null, null, sentence, false, [], [], [], []);
}
