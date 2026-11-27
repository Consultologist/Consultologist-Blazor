using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): the production drawing (part 09 §§ 10.2 to 10.4). Each node of <c>nodes</c> is a box
/// in the frame of its scope: an agent a rectangle (<c>agent → &lt;format&gt; · &lt;producer&gt;</c>), a <c>rendered</c>
/// node one too (<c>rendered → …</c>), a <c>computed</c> node a double box (<c>computed → &lt;format&gt;</c>), any other
/// kind (a <c>call</c> until Task 4) a plain rectangle. A flow node is its frames (a <c>for_</c> or <c>while_</c> body;
/// an <c>if_…_then</c>, <c>if_…_else</c>, <c>case_…_branch_N</c> or <c>case_…_else</c> per body that holds a node) and,
/// after them, its summary box carrying the loop or condition line, every condition and list printed by
/// <see cref="JsonLogicPrinter.Print"/>. A deliverable is a hexagon. Every <c>input.*</c>, <c>run.*</c>,
/// <c>profile.*</c> and <c>snippet.*</c> read is one pill at the top level, in order of first read; an input nothing
/// reads follows, in <c>inputOrder</c>.
/// <para>
/// Edges come from the entries (D6), not from <c>graph.edges</c>: a read is <c>--&gt;</c> labelled with the member read
/// (D4; none for a pill but an input's); a condition's read (an <c>if</c>, <c>while</c>, <c>until</c>, a <c>case</c>
/// subject, a part's or snippet's placement rule) is <c>-.-&gt;|if|</c> into the box it decides; a loop's list is
/// <c>--&gt;|for|</c> into its frame; a body's result is <c>--&gt;|ret|</c> (<c>ret (&lt;branch&gt;)</c> for an
/// <c>if</c> or <c>case</c>) into the summary box. <c>reads</c> does not say which of its paths a condition made, so a
/// path a condition reads draws only the <c>if</c> edge, unless a <c>computed</c> expression reads it too. Reads of
/// <c>item</c>, <c>pass</c>, <c>renderer.*</c>, <c>macro.*</c>, <c>function.*</c> and <c>param.*</c> draw nothing
/// here; a macro's placement rule is its own box's (Task 3). A body's edges are built before its flow's, and the
/// flow's own (<c>for</c>, then <c>if</c>, then <c>ret</c>) after them.
/// </para>
/// </summary>
public sealed partial record DiagramModel
{
    private sealed partial class Builder
    {
        /// <summary>Deeper than any compiled package nests; a document that nests further (or names itself) stops here.</summary>
        private const int MaxDepth = 64;

        private readonly HashSet<string> placedKeys = new(StringComparer.Ordinal);

        private void Production()
        {
            var order = Strings(document["graph"]?["order"]).ToList();
            var bodies = nodes.Select(n => n.Value as JsonObject).OfType<JsonObject>().SelectMany(Children).ToHashSet(StringComparer.Ordinal);
            foreach (var key in InOrder(nodes, order).Where(k => !bodies.Contains(k)))
                Node(key, root, 0);
            foreach (var key in InOrder(deliverables, order))
                Deliverable(key);
            foreach (var id in InputIds().Where(id => !pillKeys.Contains("input." + id)))
                Pill("input." + id);
        }

        /// <summary>The keys of <paramref name="map"/>: those <c>graph.order</c> lists, in its order, then the rest in the map's.</summary>
        private static IEnumerable<string> InOrder(JsonObject map, List<string> order) =>
            order.Where(map.ContainsKey).Concat(map.Select(p => p.Key).Where(k => !order.Contains(k))).Distinct(StringComparer.Ordinal);

        private IEnumerable<string> InputIds()
        {
            var inputs = document["inputs"] as JsonObject ?? new JsonObject();
            return Strings(document["inputOrder"]).Where(inputs.ContainsKey).Concat(inputs.Select(p => p.Key)).Distinct(StringComparer.Ordinal);
        }

        /// <summary>The node <paramref name="key"/> drawn into <paramref name="scope"/>, once.</summary>
        private void Node(string key, Scope scope, int depth)
        {
            if (depth > MaxDepth || nodes[key] is not JsonObject entry || !placedKeys.Add(key))
                return;

            var id = IdOf(key);
            var kind = Text(entry["kind"]);
            if (kind is "for" or "while" or "if" or "case")
            {
                Flow(key, id, kind, entry, scope, depth);
                return;
            }

            var lines = Head(key, entry);
            var shape = BoxShape.Rectangle;
            switch (kind)
            {
                case "agent" or "rendered":
                    lines.Add(Producer($"{kind} → {Format(entry)}", entry));
                    break;
                case "computed":
                    lines.Add($"computed → {Format(entry)}");
                    shape = BoxShape.DoubleBox;
                    break;
            }

            scope.Boxes.Add(new DiagramBox(id, key, shape, lines, Bool(entry["modelDerived"])));
            Reads(id, entry);
        }

        /// <summary>A flow node: its frames, then its summary box; its bodies' edges, then its own.</summary>
        private void Flow(string key, string id, string kind, JsonObject entry, Scope scope, int depth)
        {
            var name = Name(key, entry);
            var lines = Head(key, entry);
            var bodies = new List<(string? Title, string? Ret)>();
            // The flow's own reads are resolved before its bodies, so a pill its list or a condition reads comes before theirs.
            var list = kind == "for" ? Text(entry["in"]) : null;
            var feed = Source(list);
            var conditions = ConditionPaths(entry).Select(Source).OfType<Read>().ToList();

            if (kind == "for")
            {
                lines.Add(Until($"item in {Printed(list)}", entry));
                Body(Frame(scope, "for_" + id, $"{name}: per {Printed(list)} item"), Strings(entry["nodes"]), depth);
            }
            else if (kind == "while")
            {
                var line = $"while {JsonLogicPrinter.Print(entry["while"])} — at most {Bound(entry["max"])}";
                lines.Add(Until(line, entry));
                Body(Frame(scope, "while_" + id, $"{name}: {line}"), Strings(entry["nodes"]), depth);
            }
            else if (kind == "if")
            {
                lines.Add($"if {JsonLogicPrinter.Print(entry["if"])}");
                bodies.Add(Branch(scope, $"if_{id}_then", name, "then", entry["then"], depth));
                bodies.Add(Branch(scope, $"if_{id}_else", name, "else", entry["else"], depth));
            }
            else
            {
                lines.Add($"case {Printed(Text(entry["case"]))}");
                var branches = (entry["branches"] as JsonArray ?? []).ToList();
                for (var i = 0; i < branches.Count; i++)
                {
                    var when = string.Join(", ", Strings((branches[i] as JsonObject)?["when"]));
                    bodies.Add(Branch(scope, $"case_{id}_branch_{i + 1}", name, when, branches[i], depth));
                }

                bodies.Add(Branch(scope, $"case_{id}_else", name, "else", entry["else"], depth));
            }

            scope.Boxes.Add(new DiagramBox(id, key, BoxShape.Rectangle, lines, Bool(entry["modelDerived"])));
            if (feed is { } source)
                Edge(source.Id, "for_" + id, EdgeStyle.Read, "for");
            Conditions(id, conditions);
            if (kind is "for" or "while")
                Ret(Text(entry["ret"]), id, "ret");
            foreach (var (title, ret) in bodies.Where(b => b.Title != null))
                Ret(ret, id, $"ret ({title})");
        }

        /// <summary>An <c>if</c>'s or <c>case</c>'s body: a frame when it holds a node (an explicitly empty one draws none); its title and <c>ret</c>, or a null title when it is not a body.</summary>
        private (string? Title, string? Ret) Branch(Scope scope, string frameId, string name, string title, JsonNode? body, int depth)
        {
            if (body is not JsonObject block)
                return (null, null);
            var inner = Strings(block["nodes"]).ToList();
            if (inner.Count > 0)
                Body(Frame(scope, frameId, $"{name}: {title}"), inner, depth);
            return (title, Text(block["ret"]));
        }

        private static Scope Frame(Scope scope, string id, string title)
        {
            var frame = new Scope(id, title);
            scope.Frames.Add(frame);
            return frame;
        }

        private void Body(Scope frame, IEnumerable<string> keys, int depth)
        {
            foreach (var key in keys)
                Node(key, frame, depth + 1);
        }

        private void Ret(string? ret, string id, string label)
        {
            if (ret != null && nodes.ContainsKey(ret))
                Edge(IdOf(ret), id, EdgeStyle.Read, label);
        }

        private void Deliverable(string key)
        {
            if (deliverables[key] is not JsonObject entry)
                return;
            var id = IdOf(key);
            root.Boxes.Add(new DiagramBox(id, key, BoxShape.Hexagon, Head(key, entry), false));
            Reads(id, entry);
        }

        /// <summary>A leaf's or deliverable's edges: its content reads, labelled by member (D4), then its conditions'.</summary>
        private void Reads(string id, JsonObject entry)
        {
            var conditions = ConditionPaths(entry).Concat(MacroRulePaths(entry)).Select(Source).OfType<Read>().ToHashSet();
            var expression = JsonLogicShape.Paths(entry["expression"]).Select(Source).OfType<Read>().ToHashSet();
            foreach (var read in Strings(entry["reads"]).Select(Source).OfType<Read>())
            {
                if (!conditions.Contains(read) || expression.Contains(read))
                    Edge(read.Id, id, EdgeStyle.Read, read.Member);
            }

            Conditions(id, ConditionPaths(entry).Select(Source).OfType<Read>());
        }

        private void Conditions(string id, IEnumerable<Read> reads)
        {
            foreach (var read in reads)
                Edge(read.Id, id, EdgeStyle.If, "if");
        }

        /// <summary>The paths an entry's conditions read, in order: its <c>if</c>, <c>while</c>, <c>until</c>, <c>case</c> subject, then each part's and snippet's placement rule.</summary>
        private static IEnumerable<string> ConditionPaths(JsonObject entry)
        {
            var rules = new[] { entry["if"], entry["while"], entry["until"] }
                .Concat(Placements(entry).Where(p => Text(p["site"]) is { } site && (site.StartsWith("part.", StringComparison.Ordinal) || site.StartsWith("snippet.", StringComparison.Ordinal)))
                    .Select(p => p["if"]));
            var paths = rules.SelectMany(JsonLogicShape.Paths);
            return Text(entry["case"]) is { } subject ? paths.Prepend(subject) : paths;
        }

        /// <summary>The paths a macro's (or macro folder's) placement rule reads: the macro box's (Task 3), never a read into the node.</summary>
        private static IEnumerable<string> MacroRulePaths(JsonObject entry) =>
            Placements(entry).Where(p => Text(p["site"]) is { } site && site.StartsWith("macro", StringComparison.Ordinal))
                .SelectMany(p => JsonLogicShape.Paths(p["if"]));

        private static IEnumerable<JsonObject> Placements(JsonObject entry) => (entry["placements"] as JsonArray ?? []).OfType<JsonObject>();

        /// <summary>What a read draws from: the box of the key it names and the member it reads (D4).</summary>
        private readonly record struct Read(string Id, string? Member);

        /// <summary>
        /// The box <paramref name="path"/> reads from, with its member (the step after the key), registering a pill; null
        /// for a read that draws no edge (<c>item</c>, <c>pass</c>, <c>renderer.*</c>, …) or a path of no known shape.
        /// </summary>
        private Read? Source(string? path)
        {
            if (ReferenceString.Parse(path) is not { Segments: [var root, var name, ..] segments })
                return null;
            string? Member(int length) => segments.Count > length ? segments[length] : null;
            switch (root)
            {
                case "input":
                    return new Read(Pill($"input.{name}"), Member(2));
                case "run" or "profile":
                    return new Read(Pill($"{root}.{name}"), null);
                case "snippet":
                    return new Read(Pill(Longest(snippets, segments) ?? $"snippet.{name}"), null);
                case "node" or "check" or "deliverable":
                    var map = root == "node" ? nodes : root == "check" ? checks : deliverables;
                    return Longest(map, segments) is { } key ? new Read(IdOf(key), Member(key.Split('.').Length)) : null;
                default:
                    return null;
            }
        }

        /// <summary>The longest leading run of <paramref name="segments"/> that is a key of <paramref name="map"/>.</summary>
        private static string? Longest(JsonObject map, IReadOnlyList<string> segments)
        {
            for (var length = segments.Count; length >= 2; length--)
            {
                var key = string.Join('.', segments.Take(length));
                if (map.ContainsKey(key))
                    return key;
            }

            return null;
        }

        /// <summary>The pill of <paramref name="key"/>, added at its first read; its id.</summary>
        private string Pill(string key)
        {
            var id = IdOf(key);
            if (pillKeys.Add(key))
                pills.Add(new DiagramBox(id, key, BoxShape.Pill, [key], false));
            return id;
        }

        /// <summary>The keys a flow node's bodies hold.</summary>
        private static IEnumerable<string> Children(JsonObject entry) =>
            Strings(entry["nodes"])
                .Concat(new[] { entry["then"], entry["else"] }.Concat(entry["branches"] as JsonArray ?? []).SelectMany(b => Strings((b as JsonObject)?["nodes"])));

        /// <summary>A box's first lines: its id and, when it has one, its label.</summary>
        private static List<string> Head(string key, JsonObject entry)
        {
            var lines = new List<string> { Name(key, entry) };
            if (Text(entry["label"]) is { } label)
                lines.Add(label);
            return lines;
        }

        private static string Name(string key, JsonObject entry) => Text(entry["id"]) ?? key.Split('.')[^1];

        private static string Producer(string line, JsonObject entry) =>
            Text(entry["producer"]) is { } producer ? $"{line} · {producer}" : line;

        private static string Until(string line, JsonObject entry) =>
            entry["until"] is { } until ? $"{line} · until {JsonLogicPrinter.Print(until)}" : line;

        /// <summary>A format's name: a <c>$ref</c>'s definition (<c>#/$defs/x</c> → <c>x</c>) or a bare name; <c>?</c> for an inline schema, as a type string that cannot be printed.</summary>
        private static string Format(JsonObject entry)
        {
            var reference = entry["format"] is JsonObject format ? Text(format["$ref"]) : Text(entry["format"]);
            const string defs = "#/$defs/";
            return reference == null ? "?" : reference.StartsWith(defs, StringComparison.Ordinal) ? reference[defs.Length..] : reference;
        }

        /// <summary>A reference string (a loop's list, a <c>case</c> subject) as the printer writes the path it reads.</summary>
        private static string Printed(string? reference) =>
            reference == null ? "?" : JsonLogicPrinter.Print(new JsonObject { ["var"] = reference });

        private static string Bound(JsonNode? max) =>
            max is JsonValue value && value.TryGetValue<long>(out var bound) ? bound.ToString(CultureInfo.InvariantCulture) : "—";

        private static bool Bool(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
    }
}
