using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): renderers and macros, drawn inside the frame of what uses them (part 09 § 10.4, D7).
/// An entry (a node, a deliverable; a check's in Task 5, a function's nodes in Task 4) with a <c>renders</c> step or a
/// placed macro is wrapped in a <c>uses_&lt;box id&gt;</c> frame titled <c>&lt;id&gt; · &lt;label&gt;</c>, holding one
/// parallelogram per renderer step (<c>&lt;box id&gt;__render_&lt;n&gt;</c>, <c>renderer.&lt;id&gt;</c> over
/// <c>&lt;input&gt; → &lt;format&gt;</c>), one doc box per placed macro (<c>&lt;box id&gt;__macro_&lt;macro id&gt;</c>,
/// <c>macro.&lt;id&gt;</c> over <c>&lt;format&gt; · placed verbatim</c>; a macro folder's placement draws no box of its
/// own), then the entry's box. A step's source runs into its box with the member it reads (D4), a chain's later step is
/// fed by the step before it, and each last step feeds the entry; a macro box is <c>--&gt;|placed|</c> into the entry,
/// and what its placement's rule (and its folder's) reads is <c>-.-&gt;|if|</c> into the macro box, never the entry.
/// A path a step renders draws no direct read into the entry: the diagram shows which reads are transformed. An entry
/// with neither (or an older document with no <c>renders</c> and no <c>placements</c>) is not framed.
/// </summary>
public sealed partial record DiagramModel
{
    private sealed partial class Builder
    {
        private JsonObject Renderers => document["renderers"] as JsonObject ?? new JsonObject();

        private JsonObject Macros => document["macros"] as JsonObject ?? new JsonObject();

        /// <summary>One pipe step into a renderer: its position, what it renders (a path, <c>render:&lt;n&gt;</c> or a literal) and the renderer's id.</summary>
        private readonly record struct Step(int N, string? Source, string Renderer);

        /// <summary>
        /// The scope the box <paramref name="id"/> of <paramref name="entry"/> goes in: a new <c>uses_</c> frame inside
        /// <paramref name="scope"/>, already holding its renderer and macro boxes, when the entry uses any; else
        /// <paramref name="scope"/>. The caller adds the entry's box, then calls <see cref="UsesEdges"/>.
        /// </summary>
        private Scope Uses(string key, string id, JsonObject entry, Scope scope)
        {
            var steps = Steps(entry);
            var placed = PlacedMacros(entry);
            if (steps.Count == 0 && placed.Count == 0)
                return scope;

            var name = Name(key, entry);
            var frame = Frame(scope, "uses_" + id, Text(entry["label"]) is { } label ? $"{name} · {label}" : name);
            foreach (var step in steps)
            {
                var renderer = Renderers["renderer." + step.Renderer] as JsonObject;
                frame.Boxes.Add(new DiagramBox(RenderId(id, step.N), string.Create(CultureInfo.InvariantCulture, $"{key}#render:{step.N}"), BoxShape.Parallelogram,
                    [$"renderer.{step.Renderer}", $"{FormatOf(renderer?["input"])} → {FormatOf(renderer?["format"])}"], false));
            }

            foreach (var macro in placed)
            {
                var definition = Macros["macro." + macro] as JsonObject;
                frame.Boxes.Add(new DiagramBox(MacroId(id, macro), $"{key}#macro:{macro}", BoxShape.Doc,
                    [$"macro.{macro}", $"{FormatOf(definition?["format"])} · placed verbatim"], false));
            }

            return frame;
        }

        /// <summary>
        /// The edges of <paramref name="entry"/>'s renderer and macro boxes: inside <paramref name="frame"/> (the scope
        /// <see cref="Uses"/> gave) step to step, last steps and macros into the entry; into the boxes, each step's
        /// source and each macro's rules.
        /// </summary>
        private void UsesEdges(string id, JsonObject entry, Scope frame)
        {
            var steps = Steps(entry);
            var numbers = steps.Select(s => s.N).ToHashSet();
            var fed = new HashSet<int>();
            foreach (var step in steps)
            {
                if (Earlier(step.Source) is { } before)
                {
                    if (numbers.Contains(before))
                    {
                        frame.Edges.Add(new DiagramEdge(RenderId(id, before), RenderId(id, step.N), EdgeStyle.Read, null));
                        fed.Add(before);
                    }
                }
                else if (Source(Rooted(step.Source, entry)) is { } read)
                {
                    Edge(read.Id, RenderId(id, step.N), EdgeStyle.Read, read.Member);
                }
            }

            foreach (var step in steps.Where(s => !fed.Contains(s.N)))
                frame.Edges.Add(new DiagramEdge(RenderId(id, step.N), id, EdgeStyle.Read, null));

            foreach (var macro in PlacedMacros(entry))
            {
                frame.Edges.Add(new DiagramEdge(MacroId(id, macro), id, EdgeStyle.Read, "placed"));
                foreach (var read in MacroRules(entry, macro).SelectMany(JsonLogicShape.Paths).Select(Source).OfType<Read>())
                    Edge(read.Id, MacroId(id, macro), EdgeStyle.If, "if");
            }
        }

        /// <summary>The rooted paths <paramref name="entry"/>'s renderer steps render: their reads go into the renderer boxes, not the entry.</summary>
        private static HashSet<string> Transformed(JsonObject entry) =>
            Steps(entry).Where(s => Earlier(s.Source) == null && s.Source != null).Select(s => Rooted(s.Source, entry)!).ToHashSet(StringComparer.Ordinal);

        /// <summary>The entry's <c>renders</c> steps of a known shape (a whole <c>n</c> and a renderer id), once per <c>n</c>, in order.</summary>
        private static List<Step> Steps(JsonObject entry)
        {
            var steps = new List<Step>();
            foreach (var render in (entry["renders"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (render["n"] is JsonValue value && value.TryGetValue<int>(out var n) && Text(render["renderer"]) is { } renderer && steps.All(s => s.N != n))
                    steps.Add(new Step(n, Text(render["source"]), renderer));
            }

            return steps;
        }

        /// <summary>The step a source names (<c>render:&lt;n&gt;</c>), or null for a path or a literal.</summary>
        private static int? Earlier(string? source)
        {
            const string prefix = "render:";
            return source != null && source.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(source[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        }

        /// <summary>
        /// A step's source as a rooted path: a body writes a node as it is named at its place (<c>node.draft</c> in a
        /// loop's body), and the entry's <c>names</c> maps each such head to its rooted key where the two differ.
        /// </summary>
        private static string? Rooted(string? path, JsonObject entry)
        {
            if (path == null || entry["names"] is not JsonObject names)
                return path;
            var segments = path.Split('.');
            for (var length = segments.Length; length >= 2; length--)
            {
                if (Text(names[string.Join('.', segments.Take(length))]) is { } rooted)
                    return string.Join('.', segments.Skip(length).Prepend(rooted));
            }

            return path;
        }

        /// <summary>The ids of the macros <paramref name="entry"/>'s <c>placements</c> place (<c>section_notes.hpi</c>), once each, in order.</summary>
        private static List<string> PlacedMacros(JsonObject entry) =>
            Placements(entry).Select(p => Text(p["site"])).OfType<string>()
                .Where(site => site.StartsWith("macro.", StringComparison.Ordinal)).Select(site => site["macro.".Length..])
                .Distinct(StringComparer.Ordinal).ToList();

        /// <summary>The rules that decide the macro <paramref name="macro"/> at <paramref name="entry"/>'s place: its folder's placement rule, then its own placements'.</summary>
        private static IEnumerable<JsonNode?> MacroRules(JsonObject entry, string macro)
        {
            var folder = macro.Contains('.', StringComparison.Ordinal) ? "macro-folder." + macro[..macro.LastIndexOf('.')] : null;
            var placements = Placements(entry).ToList();
            return placements.Where(p => folder != null && Text(p["site"]) == folder)
                .Concat(placements.Where(p => Text(p["site"]) == "macro." + macro))
                .Select(p => p["if"]);
        }

        private static string RenderId(string id, int n) => string.Create(CultureInfo.InvariantCulture, $"{id}__render_{n}");

        private static string MacroId(string id, string macro) => $"{id}__macro_{IdOf(macro)}";
    }
}
