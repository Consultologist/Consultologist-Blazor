using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): calls and functions (part 09 § 10.4). A <c>call</c> is one <c>tag-rect</c> box per
/// call, <c>&lt;id&gt;</c> over <c>call &lt;function&gt;</c>; its reads (the <c>with</c> arguments) run into it, and a
/// <c>-.-o|calls|</c> edge runs from it to the function's frame. Each function of <c>functions</c> is drawn once, at the
/// top level, in a frame <c>fn_&lt;function&gt;</c> titled <c>function &lt;id&gt; (&lt;inputs&gt;) → &lt;format&gt;</c>
/// (the inputs in <c>inputOrder</c>): first a pill per parameter, <c>fn_&lt;function&gt;__param__&lt;id&gt;</c> labelled
/// <c>param.&lt;id&gt;</c> (D3: not the example's bare <c>param__&lt;id&gt;</c>, which two functions would share), in
/// <c>inputOrder</c>; then the body's nodes in its <c>body</c> order, each drawn as a node outside is (loops, branches,
/// uses frames) with its id prefixed <c>fn_&lt;function&gt;__</c>.
/// <para>
/// The inside is drawn from the entry's <c>nodes</c> and their <c>reads</c> (R1: <c>graph.edges</c> holds no edge
/// between a function's own nodes), so a path a condition reads draws its <c>if</c> edge alone. A parameter's read is
/// unlabelled (D4). The function's <c>ret</c> draws no edge of its own: the read into the returned node is drawn, as
/// § 10.5's example draws it. An edge with both ends in the frame is the frame's; one from outside (a <c>run.*</c> pill,
/// which stays the package's one pill at the top level) or to another function's frame is the model's. A function
/// box's key (<see cref="DiagramBox.Key"/>) is <c>function.&lt;function&gt;#&lt;local key&gt;</c>, which no record row
/// carries: the shared frame shows no per-call status (§ 10.6).
/// </para>
/// </summary>
public sealed partial record DiagramModel
{
    private sealed partial class Builder
    {
        /// <summary>The function whose body is being drawn; null at the package's top level.</summary>
        private FunctionBody? function;

        /// <summary>A function's body while it is drawn: its map of nodes, its parameters' pills, the edges built inside it.</summary>
        private sealed class FunctionBody(string name, JsonObject nodes)
        {
            public string Name { get; } = name;
            public JsonObject Nodes { get; } = nodes;
            public string Prefix { get; } = $"fn_{IdOf(name)}__";
            public List<DiagramBox> Params { get; } = [];
            public List<DiagramEdge> Edges { get; } = [];
        }

        /// <summary>The map the entries being drawn are keyed in: <c>checks</c> while the checks are drawn (<c>DiagramModel.Checks.cs</c>), else <see cref="NodeMap"/>.</summary>
        private JsonObject Map => drawingChecks ? checks : NodeMap;

        /// <summary>The map a <c>node.*</c> read resolves in: the package's <c>nodes</c>, or the function's own.</summary>
        private JsonObject NodeMap => function?.Nodes ?? nodes;

        /// <summary>A node's Mermaid id: <see cref="IdOf"/>, prefixed <c>fn_&lt;function&gt;__</c> inside a function (D3).</summary>
        private string BoxId(string key) => function is { } body ? body.Prefix + IdOf(key) : IdOf(key);

        /// <summary>A node's box key: its rooted key, or inside a function <c>function.&lt;function&gt;#&lt;key&gt;</c>.</summary>
        private string BoxKey(string key) => function is { } body ? $"function.{body.Name}#{key}" : key;

        /// <summary>A call's lines (§ 10.4): its id, then <c>call &lt;function&gt;</c>.</summary>
        private static List<string> CallLines(string key, JsonObject entry) => [Name(key, entry), $"call {Text(entry["function"]) ?? "?"}"];

        /// <summary>A call's reference to the frame of the function it calls.</summary>
        private void Calls(string id, JsonObject entry)
        {
            if (Text(entry["kind"]) == "call" && Text(entry["function"]) is { } called)
                Edge(id, "fn_" + IdOf(called), EdgeStyle.Reference, "calls");
        }

        /// <summary>Every function of <c>functions</c>, each once, in the map's order.</summary>
        private void Functions()
        {
            foreach (var (name, value) in document["functions"] as JsonObject ?? new JsonObject())
            {
                if (value is JsonObject entry)
                    Function(name, entry);
            }
        }

        private void Function(string name, JsonObject entry)
        {
            var map = entry["nodes"] as JsonObject ?? new JsonObject();
            var inputs = entry["inputs"] as JsonObject ?? new JsonObject();
            var order = Strings(entry["inputOrder"]).Where(inputs.ContainsKey).Concat(inputs.Select(p => p.Key)).Distinct(StringComparer.Ordinal).ToList();
            var frame = Frame(root, "fn_" + IdOf(name), $"function {name} ({string.Join(", ", order)}) → {Format(entry)}");
            var body = new FunctionBody(name, map);
            function = body;
            foreach (var id in order)
                Param(body, id);

            var children = map.Select(n => n.Value as JsonObject).OfType<JsonObject>().SelectMany(Children).ToHashSet(StringComparer.Ordinal);
            foreach (var key in Strings(entry["body"]).Where(map.ContainsKey).Concat(map.Select(n => n.Key)).Distinct(StringComparer.Ordinal).Where(k => !children.Contains(k)))
                Node(key, frame, 0);
            function = null;

            frame.Boxes.InsertRange(0, body.Params);
            // The boxes and frames inside the function's frame (not the frame itself, which only a call refers to).
            var inside = new HashSet<string>(StringComparer.Ordinal);
            void Collect(Scope scope)
            {
                foreach (var box in scope.Boxes)
                    inside.Add(box.Id);
                foreach (var inner in scope.Frames)
                {
                    inside.Add(inner.Id);
                    Collect(inner);
                }
            }

            Collect(frame);
            foreach (var edge in body.Edges)
                (inside.Contains(edge.From) && inside.Contains(edge.To) ? frame.Edges : edges).Add(edge);
        }

        /// <summary>The pill of <paramref name="body"/>'s parameter <paramref name="id"/>, added at its first mention; its id.</summary>
        private static string Param(FunctionBody body, string id)
        {
            var pill = body.Prefix + "param__" + IdOf(id);
            if (body.Params.All(p => p.Id != pill))
                body.Params.Add(new DiagramBox(pill, $"function.{body.Name}#param.{id}", BoxShape.Pill, ["param." + id], false));
            return pill;
        }
    }
}
