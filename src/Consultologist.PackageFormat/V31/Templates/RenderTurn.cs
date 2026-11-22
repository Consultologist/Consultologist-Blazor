using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 4, 9; part 08 §§ 4, 4.2, 8.2, 10): one render of a rendered node or a
/// deliverable, with no Azure in between: its body composed over the call's scope with the call's placements decided, as
/// the run reads it back. Never puts a value in the detail: only ids, keys, hashes and lengths. Never throws on the plan or
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
    /// failed otherwise carries the composer's sentence.
    /// </summary>
    public static RenderOutcome Render(RunPlan plan, RenderCall call)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(call);

        var composition = BodyComposer.Compose(plan, new ComposeRequest(call.EntryKey, call.Function, call.Scope, call.Placed ?? NothingPlaced, call.StepLimit));
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
}
