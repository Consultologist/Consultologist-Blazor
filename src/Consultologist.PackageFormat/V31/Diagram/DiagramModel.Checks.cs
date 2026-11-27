using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): checks, check loops and gates (part 09 § 10.4). Every entry of <c>checks</c> is drawn
/// in one top-level frame <c>checks</c> titled <c>Checks</c>, apart from production, by the rules a node is drawn by:
/// a check is a <c>computed</c> double box (<c>computed → check_result</c>) whose reads are ordinary data edges (a
/// deliverable's <c>.content</c>, another check's <c>.value</c>, a check loop's <c>.items</c>); a check loop is a
/// <c>for_</c> frame inside Checks with its summary box beside it. A check loop over a production loop's <c>.items</c>
/// is titled <c>&lt;id&gt;: per &lt;list&gt; (aligned)</c> and draws <c>-.-o|aligned|</c> from its frame to that
/// loop's frame, after its <c>-->|for|</c> edge from the list; a production loop over another loop's <c>.items</c>
/// draws no such edge, although <c>graph.edges</c> holds one (D5). A deliverable whose <c>check</c> names a check
/// gets <c>==&gt;|gates|</c> from that check, after every check's edges. The frame's heavier border is the writer's
/// <c>style checks stroke-width:3px</c>, written only when the frame is drawn (D9, <see cref="DiagramModel.HasChecks"/>).
/// </summary>
public sealed partial record DiagramModel
{
    private sealed partial class Builder
    {
        /// <summary>Whether the entries being drawn are checks: their map is <c>checks</c>, not <c>nodes</c>.</summary>
        private bool drawingChecks;

        /// <summary>Every check, in <c>graph.order</c>, in the Checks frame; then each deliverable's gate.</summary>
        private void Checks()
        {
            var order = Strings(document["graph"]?["order"]).ToList();
            var bodies = checks.Select(c => c.Value as JsonObject).OfType<JsonObject>().SelectMany(Children).ToHashSet(StringComparer.Ordinal);
            var frame = new Scope("checks", "Checks");
            drawingChecks = true;
            foreach (var key in InOrder(checks, order).Where(k => !bodies.Contains(k)))
                Node(key, frame, 0);
            drawingChecks = false;
            if (frame.Boxes.Count == 0 && frame.Frames.Count == 0)
                return;

            root.Frames.Add(frame);
            foreach (var key in InOrder(deliverables, order))
            {
                if (deliverables[key] is JsonObject entry && Text(entry["check"]) is { } check && checks.ContainsKey(check))
                    Edge(IdOf(check), IdOf(key), EdgeStyle.Gate, "gates");
            }
        }

        /// <summary>A <c>for</c>'s frame title: <c>(aligned)</c> for a check loop over a production loop's <c>.items</c>, else <c>item</c>.</summary>
        private string LoopTitle(string name, string? list) =>
            drawingChecks && AlignedWith(list) != null ? $"{name}: per {Printed(list)} (aligned)" : $"{name}: per {Printed(list)} item";

        /// <summary>A check loop's reference from <paramref name="from"/> (its frame) to the production loop whose <c>.items</c> it iterates.</summary>
        private void Aligned(string from, string? list)
        {
            if (drawingChecks && AlignedWith(list) is { } loop)
                Edge(from, loop, EdgeStyle.Reference, "aligned");
        }

        /// <summary>
        /// The id <paramref name="list"/> lines up with when it is a production <c>for</c>'s or <c>while</c>'s
        /// <c>.items</c>: that loop's frame (<c>for_</c>, <c>while_</c>), or its summary box when its body holds no node
        /// and so draws no frame; null for any other list.
        /// </summary>
        private string? AlignedWith(string? list)
        {
            if (ReferenceString.Parse(list) is not { Segments: var segments } || Longest(nodes, segments) is not { } key
                || segments.Count != key.Split('.').Length + 1 || segments[^1] != "items" || nodes[key] is not JsonObject loop)
                return null;
            var kind = Text(loop["kind"]);
            if (kind is not ("for" or "while"))
                return null;
            return Strings(loop["nodes"]).Any() ? $"{kind}_{IdOf(key)}" : IdOf(key);
        }
    }
}
