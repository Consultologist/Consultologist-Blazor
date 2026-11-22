using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

public sealed partial class PlanRunner
{
    private sealed partial class Run
    {
        /// <summary>
        /// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 8.2, 9; part 08 § 5.2; D3): decides the placements of an agent's,
        /// a rendered node's or a deliverable's body once its gate held (its dependencies, its placements' reads among
        /// them, settled), in <see cref="PlanEntry.Placements"/> order, into <see cref="RunInstance.Placed"/>. A placement
        /// is decided only when the unit that places it (<see cref="PlanPlacement.Within"/>) is placed (a folder's macro,
        /// with its folder), and a site once. A rule's decision is recorded (its owner the placing row's key, its site the
        /// placement's site; #1506, D4: its reads its own rule's, not the entry's) and is its result; a placement with no
        /// rule is placed, with no decision. Each rule reads the instance's own place: its keys are rooted there, and its scope is the instance's frame. A rule that fails fails
        /// the instance with part 03 § 11.3's sentence for the unit's <c>if</c>; false then.
        /// </summary>
        private async Task<bool> PlaceAsync(RunInstance instance)
        {
            var entry = instance.Entry;
            var placed = new Dictionary<string, bool>(StringComparer.Ordinal);
            instance.Placed = placed;
            JsonObject? data = null;
            foreach (var placement in entry.Placements)
            {
                if (placed.ContainsKey(placement.Site) || (placement.Within is { } within && !Holds(placed, within)))
                    continue;
                if (placement.If is not { } rule)
                {
                    placed[placement.Site] = true;
                    continue;
                }

                data ??= scope.Data(entry, instance.Frame, entry.Reads);
                var decision = await DecideRuleAsync(instance, rule, placement.Site, data);
                if (decision.Failure is { } failed)
                {
                    await FailAsync(instance, RuleFailed(UnitSubject(instance, placement.Site), "if", failed));
                    return false;
                }

                await RecordDecisionAsync(instance, instance.Frame, placement.Site, placement.Rule, placement.Printed ?? "", data, decision.Result, null);
                placed[placement.Site] = decision.Result;
            }

            return true;
        }

        /// <summary>
        /// #1504 (part 08 §§ 4, 8.2, 10): adds each member of the detail a call's outcome gave (<paramref name="outcome"/>, in
        /// part 08's names: hashes, ids, lengths, never a value) to the row's <paramref name="detail"/>, a copy of each.
        /// </summary>
        private static void Merge(JsonObject? outcome, JsonObject detail)
        {
            foreach (var (name, member) in outcome ?? [])
                detail[name] = member?.DeepClone();
        }

        /// <summary>
        /// Whether <paramref name="site"/> is placed: decided true, and for a folder's macro (<c>macro.&lt;f&gt;.&lt;id&gt;</c>) its
        /// folder too, as the composer offers it (publish lists the folder before its macro in each unit that places one).
        /// </summary>
        private static bool Holds(Dictionary<string, bool> placed, string site) =>
            placed.GetValueOrDefault(site)
            && (site.Split('.') is not ["macro", var folder, _] || placed.GetValueOrDefault($"macro-folder.{folder}"));

        /// <summary>
        /// Publish's subject of a unit's <c>if</c> (part 03 § 11.3): <c>Part 'p' of node 'x'</c>, <c>Snippet 's'</c>,
        /// <c>Macro 'm'</c> (a folder's: <c>Macro 'f.m'</c>, the folder's own: <c>Macro 'f'</c>), with
        /// <c> in function 'fn'</c> inside one.
        /// </summary>
        private static string UnitSubject(RunInstance instance, string site)
        {
            var dot = site.IndexOf('.');
            var id = site[(dot + 1)..];
            var subject = site[..dot] switch
            {
                "part" => $"Part '{id}' of node '{instance.Entry.Id}'",
                "snippet" => $"Snippet '{id}'",
                _ => $"Macro '{id}'",
            };
            return subject + InFunction(instance);
        }
    }
}
