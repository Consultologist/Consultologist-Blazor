using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Diagram;

/// <summary>#1510 (specVersion 31, area 13): a box's shape (part 09 § 10.2).</summary>
public enum BoxShape { Pill, Rectangle, DoubleBox, Parallelogram, Doc, TagRect, Hexagon }

/// <summary>#1510: an edge's line (part 09 § 10.3): <c>--&gt;</c>, <c>-.-&gt;</c>, <c>-.-o</c>, <c>==&gt;</c>.</summary>
public enum EdgeStyle { Read, If, Reference, Gate }

/// <summary>
/// #1510: one box. <paramref name="Id"/> is its Mermaid id; <paramref name="Key"/> the rooted key a record row carries
/// (<c>node.sections.paragraphs.draft</c>, <c>deliverable.note</c>; a pill's is its read, <c>input.x</c>, <c>run.date</c>);
/// <paramref name="Lines"/> its label's lines as package text, unescaped (the writer escapes them, D8).
/// </summary>
public sealed record DiagramBox(string Id, string Key, BoxShape Shape, IReadOnlyList<string> Lines, bool ModelDerived);

/// <summary>
/// #1510: a frame (a subgraph): its boxes and frames, and the edges written inside it (a uses frame's, a function's).
/// <paramref name="At"/> is where it is written in the frame that holds it: before the box of that index in the holder's
/// <see cref="Boxes"/> (a flow's bodies before its summary box, a <c>uses_</c> frame in the place of the box it wraps), or
/// after every box when no box has that index. The top-level frame's is 0.
/// </summary>
public sealed record DiagramFrame(string Id, string Title, IReadOnlyList<DiagramBox> Boxes, IReadOnlyList<DiagramFrame> Frames, IReadOnlyList<DiagramEdge> Edges, int At);

/// <summary>#1510: an edge between two Mermaid ids (a box's or a frame's); <paramref name="Label"/> null for none.</summary>
public sealed record DiagramEdge(string From, string To, EdgeStyle Style, string? Label);

/// <summary>
/// #1510 (specVersion 31, area 13): the one graph model of a 31 package's drawing (part 09 § 10.1), built from the
/// canonical <c>resolve.json</c> text: <paramref name="Root"/> the top level (pills first, in order of first read; the
/// top-level entries in <c>graph.order</c>; deliverables last), <paramref name="Edges"/> the edges written after every
/// frame, in the order built; <paramref name="HasChecks"/> whether a check is drawn (the Checks frame). <c>dag.mmd</c>, the
/// Editor's diagram and the run diagram all write this model (<see cref="DiagramWriter"/>).
/// </summary>
public sealed partial record DiagramModel(DiagramFrame Root, IReadOnlyList<DiagramEdge> Edges, bool HasChecks)
{
    /// <summary>
    /// The model of <paramref name="resolve"/>, read as a JSON object member by member, never through a loader (a stored
    /// older <c>resolve.json</c> is drawn, not refused). What is missing or of another shape is left out (Review Focus 5):
    /// never throws on the document's content; a text that is not a JSON object (or names a member twice) draws nothing.
    /// </summary>
    public static DiagramModel Build(string resolve)
    {
        JsonObject? document;
        try
        {
            // A member named twice is refused here, at any depth, rather than when the builder first reads its object.
            document = JsonNode.Parse(resolve ?? "", documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject;
        }
        catch (JsonException)
        {
            document = null;
        }

        return new Builder(document ?? new JsonObject()).Build();
    }

    /// <summary>Every box, at any depth, frame by frame.</summary>
    public IEnumerable<DiagramBox> AllBoxes() => BoxesOf(Root);

    /// <summary>A key's Mermaid id (part 09 § 10.4): every <c>.</c> replaced by <c>__</c> (<c>node.a.b</c> → <c>node__a__b</c>).</summary>
    public static string IdOf(string key) => key.Replace(".", "__", StringComparison.Ordinal);

    private static IEnumerable<DiagramBox> BoxesOf(DiagramFrame frame) => frame.Boxes.Concat(frame.Frames.SelectMany(BoxesOf));

    /// <summary>A frame while it is built.</summary>
    private sealed class Scope(string id, string title)
    {
        public string Id { get; } = id;

        /// <summary>The index of the box of the holding scope this frame is written before (<see cref="DiagramFrame.At"/>).</summary>
        public int At { get; set; }

        public List<DiagramBox> Boxes { get; } = [];
        public List<Scope> Frames { get; } = [];
        public List<DiagramEdge> Edges { get; } = [];

        /// <summary>Puts <paramref name="boxes"/> before this scope's boxes, its frames keeping their places among the boxes.</summary>
        public void Prepend(IReadOnlyList<DiagramBox> boxes)
        {
            Boxes.InsertRange(0, boxes);
            foreach (var frame in Frames)
                frame.At += boxes.Count;
        }

        public DiagramFrame Freeze() => new(id, title, Boxes.ToList(), Frames.Select(f => f.Freeze()).ToList(), Edges.ToList(), At);
    }

    /// <summary>One build over one document. The production rules are in <c>DiagramModel.Production.cs</c>, renderers and macros in <c>DiagramModel.Uses.cs</c>, calls and functions in <c>DiagramModel.Functions.cs</c>, checks and gates in <c>DiagramModel.Checks.cs</c>.</summary>
    private sealed partial class Builder(JsonObject document)
    {
        private readonly JsonObject document = document;
        private readonly JsonObject nodes = document["nodes"] as JsonObject ?? new JsonObject();
        private readonly JsonObject checks = document["checks"] as JsonObject ?? new JsonObject();
        private readonly JsonObject deliverables = document["deliverables"] as JsonObject ?? new JsonObject();
        private readonly JsonObject snippets = document["snippets"] as JsonObject ?? new JsonObject();
        private readonly Scope root = new("", "");
        private readonly List<DiagramBox> pills = [];
        private readonly HashSet<string> pillKeys = new(StringComparer.Ordinal);
        private readonly List<DiagramEdge> edges = [];
        private readonly HashSet<DiagramEdge> seen = [];

        public DiagramModel Build()
        {
            Production();
            root.Prepend(pills);
            var frozen = root.Freeze();
            return new DiagramModel(frozen, Drawn(frozen), frozen.Frames.Any(f => f.Id == "checks"));
        }

        /// <summary>The edges built whose two ends are drawn (a box or a frame); an edge to something not drawn (a key the document does not hold, a gate naming no check) is left out.</summary>
        private List<DiagramEdge> Drawn(DiagramFrame frozen)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void Collect(DiagramFrame frame)
            {
                ids.Add(frame.Id);
                foreach (var box in frame.Boxes)
                    ids.Add(box.Id);
                foreach (var inner in frame.Frames)
                    Collect(inner);
            }

            Collect(frozen);
            return edges.Where(e => ids.Contains(e.From) && ids.Contains(e.To)).ToList();
        }

        private void Edge(string from, string to, EdgeStyle style, string? label)
        {
            var edge = new DiagramEdge(from, to, style, label);
            // Inside a function body the edge waits until the body is drawn: its frame's own, or the model's (DiagramModel.Functions.cs).
            if (seen.Add(edge))
                (function?.Edges ?? edges).Add(edge);
        }

        private static string? Text(JsonNode? node) =>
            node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

        private static IEnumerable<string> Strings(JsonNode? node) =>
            (node as JsonArray ?? []).Select(Text).OfType<string>();
    }
}
