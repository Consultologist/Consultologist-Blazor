using System.Text;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat.V31.Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): a run's additions to one box (part 09 § 10.6): label lines after the box's own,
/// a class, and the History anchor the app binds a click to (D12). It never adds, removes or renames a box.
/// </summary>
public sealed record BoxOverlay(IReadOnlyList<string> Lines, string? Class, string? Anchor);

/// <summary>#1510: the Mermaid text, and the History anchor of each box an overlay links, keyed by box id.</summary>
public sealed record DiagramText(string Mermaid, IReadOnlyDictionary<string, string> Anchors);

/// <summary>
/// #1510 (specVersion 31, area 13): writes a <see cref="DiagramModel"/> as Mermaid (part 09 §§ 10.1 to 10.4). The text
/// opens with the ELK frontmatter and <c>flowchart TD</c> (D2); <c>\n</c> line ends, four spaces of indent per level.
/// Each frame writes its boxes in order, a flow's frames just before its summary box (the frames whose id names the
/// box: <c>for_</c>, <c>while_</c>, <c>if_…_then</c>, <c>if_…_else</c>, <c>case_…_branch_N</c>, <c>case_…_else</c>),
/// then its other frames, then its own edges; the model's edges follow every frame, so Mermaid pulls no box into the
/// frame an edge is written in. At the top level a blank line parts the pills, each flow and the runs of plain boxes.
/// Last come the model-derived class (D9) and an overlay's classes. Every label line is escaped (D8): <c>"</c>,
/// <c>&lt;</c> and <c>&gt;</c> as <c>#quot;</c>, <c>#lt;</c> and <c>#gt;</c>, a line break as one space; the only
/// markup a label holds is the writer's own <c>&lt;br/&gt;</c>.
/// </summary>
public static partial class DiagramWriter
{
    private const string Indent = "    ";
    private const string Frontmatter = "---\nconfig:\n  layout: elk\n---\nflowchart TD\n";

    private static readonly IReadOnlyDictionary<string, BoxOverlay> NoOverlay = new Dictionary<string, BoxOverlay>(StringComparer.Ordinal);

    /// <summary>The Mermaid text of <paramref name="model"/>.</summary>
    public static string Write(DiagramModel model) => Write(model, NoOverlay, []).Mermaid;

    /// <summary>
    /// The Mermaid text of <paramref name="model"/> with <paramref name="overlay"/> (keyed by <see cref="DiagramBox.Key"/>)
    /// laid over its boxes, and <paramref name="classDefs"/> written as <c>classDef</c> lines; the anchors of the boxes the
    /// overlay links.
    /// </summary>
    public static DiagramText Write(DiagramModel model, IReadOnlyDictionary<string, BoxOverlay> overlay,
        IReadOnlyList<(string Name, string Style)> classDefs)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(classDefs);

        var text = new StringBuilder(Frontmatter);
        var boxes = new List<DiagramBox>();
        Contents(text, model.Root, 1, overlay, boxes, top: true);
        if (model.Edges.Count > 0)
        {
            text.Append('\n');
            foreach (var edge in model.Edges)
                Edge(text, edge, 1);
        }

        var derived = boxes.Where(b => b.ModelDerived).Select(b => b.Id).ToList();
        if (derived.Count > 0)
        {
            text.Append(Indent).Append("classDef modelDerived stroke-dasharray: 5 5\n");
            text.Append(Indent).Append("class ").Append(string.Join(',', derived)).Append(" modelDerived\n");
        }

        foreach (var (name, style) in classDefs)
            text.Append(Indent).Append("classDef ").Append(name).Append(' ').Append(style).Append('\n');

        var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
        var classes = new List<(string Class, List<string> Ids)>();
        foreach (var box in boxes)
        {
            if (!overlay.TryGetValue(box.Key, out var over))
                continue;
            if (over.Anchor is { } anchor)
                anchors[box.Id] = anchor;
            if (over.Class is { } name)
            {
                var index = classes.FindIndex(c => c.Class == name);
                if (index < 0)
                    classes.Add((name, [box.Id]));
                else
                    classes[index].Ids.Add(box.Id);
            }
        }

        foreach (var (name, ids) in classes)
            text.Append(Indent).Append("class ").Append(string.Join(',', ids)).Append(' ').Append(name).Append('\n');

        return new DiagramText(text.ToString(), anchors);
    }

    /// <summary>A label line as Mermaid reads it inside quotes (D8).</summary>
    private static string Escape(string text) =>
        LineBreak().Replace(text, " ")
            .Replace("\"", "#quot;", StringComparison.Ordinal)
            .Replace("<", "#lt;", StringComparison.Ordinal)
            .Replace(">", "#gt;", StringComparison.Ordinal);

    /// <summary>A frame's contents at <paramref name="level"/>; each box written is added to <paramref name="written"/>, so the class lines follow the text's order.</summary>
    private static void Contents(StringBuilder text, DiagramFrame frame, int level, IReadOnlyDictionary<string, BoxOverlay> overlay,
        List<DiagramBox> written, bool top)
    {
        var placed = new HashSet<DiagramFrame>(ReferenceEqualityComparer.Instance);
        string? last = null;
        void Part(string kind)
        {
            // At the top level a blank line parts the pills, each flow and each run of plain boxes.
            if (top && last != null && (kind != last || kind == "flow"))
                text.Append('\n');
            last = kind;
        }

        foreach (var box in frame.Boxes)
        {
            var own = frame.Frames.Where(f => !placed.Contains(f) && Names(f.Id, box.Id)).ToList();
            Part(own.Count > 0 ? "flow" : box.Shape == BoxShape.Pill ? "pills" : "boxes");
            foreach (var inner in own)
            {
                placed.Add(inner);
                Frame(text, inner, level, overlay, written);
            }

            Box(text, box, level, overlay);
            written.Add(box);
        }

        foreach (var inner in frame.Frames.Where(f => !placed.Contains(f)))
        {
            Part("flow");
            Frame(text, inner, level, overlay, written);
        }

        foreach (var edge in frame.Edges)
            Edge(text, edge, level);
    }

    /// <summary>Whether the frame <paramref name="frameId"/> is a body of the flow whose summary box is <paramref name="boxId"/>.</summary>
    private static bool Names(string frameId, string boxId) =>
        frameId == "for_" + boxId || frameId == "while_" + boxId
        || (frameId.StartsWith("if_" + boxId + "_", StringComparison.Ordinal) && frameId[(boxId.Length + 4)..] is "then" or "else")
        || (frameId.StartsWith("case_" + boxId + "_", StringComparison.Ordinal) && Branch().IsMatch(frameId[(boxId.Length + 6)..]));

    private static void Frame(StringBuilder text, DiagramFrame frame, int level, IReadOnlyDictionary<string, BoxOverlay> overlay, List<DiagramBox> written)
    {
        Pad(text, level).Append("subgraph ").Append(frame.Id).Append("[\"").Append(Escape(frame.Title)).Append("\"]\n");
        Contents(text, frame, level + 1, overlay, written, top: false);
        Pad(text, level).Append("end\n");
    }

    private static void Box(StringBuilder text, DiagramBox box, int level, IReadOnlyDictionary<string, BoxOverlay> overlay)
    {
        var lines = overlay.TryGetValue(box.Key, out var over) ? box.Lines.Concat(over.Lines) : box.Lines;
        var label = string.Join("<br/>", lines.Select(Escape));
        Pad(text, level).Append(box.Id).Append(box.Shape switch
        {
            BoxShape.Pill => $"([\"{label}\"])",
            BoxShape.DoubleBox => $"[[\"{label}\"]]",
            BoxShape.Parallelogram => $"[/\"{label}\"/]",
            BoxShape.Doc => $"@{{ shape: doc, label: \"{label}\" }}",
            BoxShape.TagRect => $"@{{ shape: tag-rect, label: \"{label}\" }}",
            BoxShape.Hexagon => $"{{{{\"{label}\"}}}}",
            _ => $"[\"{label}\"]",
        }).Append('\n');
    }

    private static void Edge(StringBuilder text, DiagramEdge edge, int level)
    {
        Pad(text, level).Append(edge.From).Append(' ').Append(edge.Style switch
        {
            EdgeStyle.If => "-.->",
            EdgeStyle.Reference => "-.-o",
            EdgeStyle.Gate => "==>",
            _ => "-->",
        });
        if (edge.Label != null)
            text.Append("|\"").Append(Escape(edge.Label)).Append("\"|");
        text.Append(' ').Append(edge.To).Append('\n');
    }

    private static StringBuilder Pad(StringBuilder text, int level)
    {
        for (var i = 0; i < level; i++)
            text.Append(Indent);
        return text;
    }

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"^(branch_[0-9]+|else)$")]
    private static partial Regex Branch();
}
