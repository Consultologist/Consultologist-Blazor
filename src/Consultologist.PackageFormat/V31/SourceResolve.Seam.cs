using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05, D1): the rules of a function that need what its bodies read, judged by
/// <see cref="SourceDerived.Check"/> once the seam (<see cref="ITemplateReads"/>) is supplied: S04-052 for a package
/// snippet, macro or renderer a body uses unlisted, S04-074 for a listed one nothing uses (only when every body of the
/// function is known), and S04-063 for a placed macro whose <c>if</c> reads a node the function does not have.
/// </summary>
public static partial class SourceResolve
{
    /// <summary>The kinds of definition a body uses, the <c>uses</c> member each is listed under, and its root.</summary>
    private static readonly (string Member, string Kind)[] BodyKinds = [("snippets", "snippet"), ("macros", "macro"), ("renderers", "renderer")];

    /// <summary>
    /// Every function in listed order: each body of it (a template leaf's, an own renderer's, snippet's or macro's), as
    /// <paramref name="bodyOf"/> gives it (null: not known), in source order. A body's uses are its reads with the root
    /// <c>snippet</c>, <c>macro</c> or <c>renderer</c>, the macros it places and the renderers it pipes into.
    /// </summary>
    internal static void FunctionBodies(
        SourcePackage package, ScopeTree tree, Func<object, SourceFunction, TemplateReads?> bodyOf,
        List<SourceRefusal> refusals, List<SourceRefusal> warnings)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(bodyOf);

        foreach (var function in package.Functions.Items)
        {
            var bodies = Bodies(function).Select(b => (b.Path, b.Placer, Reads: bodyOf(b.Declaration, function))).ToList();
            var used = new HashSet<(string Kind, string Id)>();
            var said = new HashSet<(string Kind, string Id)>();
            var placed = new HashSet<(string Macro, string Node)>();
            foreach (var (path, placer, reads) in bodies)
            {
                if (reads == null)
                    continue;
                foreach (var (kind, id) in Used(reads))
                {
                    used.Add((kind, id));
                    if (!Own(function, kind + "s").Contains(id) && !Uses(function, kind + "s").Contains(id)
                        && PackageHas(package, kind, id) && said.Add((kind, id)))
                    {
                        refusals.Add(ResolveSentences.UsesUnlisted(function.Id, kind, id, path));
                    }
                }

                if (placer != null)
                    Placed(package, tree, function, placer, reads, placed, refusals);
            }

            if (bodies.Any(b => b.Reads == null))
                continue;
            foreach (var reference in HeaderReferences(function))
            {
                if (ReferenceString.Parse(reference) is { Segments: ["snippet", var id, ..] })
                    used.Add(("snippet", id));
            }

            var index = SourceLayout.Join(function.Path, "index.json");
            foreach (var (member, kind) in BodyKinds)
            {
                foreach (var id in Uses(function, member).Distinct(StringComparer.Ordinal).Where(id => !used.Contains((kind, id))))
                    warnings.Add(ResolveSentences.UsedNever(function.Id, kind, id, index));
            }
        }
    }

    /// <summary>The bodies of <paramref name="function"/>: its template leaves' (each its own placer), then its own renderers', snippets' and macros'.</summary>
    private static IEnumerable<(object Declaration, string Path, SourceLeaf? Placer)> Bodies(SourceFunction function)
    {
        foreach (var leaf in ScopeTree.Walk(function.Body.Nodes).OfType<SourceLeaf>().Where(IsTemplate))
            yield return (leaf, leaf.Path, leaf);
        var d = function.Definitions;
        foreach (var renderer in d.Renderers.Items)
            yield return (renderer, renderer.Path, null);
        foreach (var entry in d.Snippets.Items.Concat(d.Macros.Items))
        {
            foreach (var text in Texts(entry))
                yield return (text, text.Path, null);
        }
    }

    /// <summary>The texts of a snippet or macro entry: the one file, or each item of the folder.</summary>
    internal static IEnumerable<SourceText> Texts(SourceTextEntry entry) => entry switch
    {
        SourceSingleText single => [single.Text],
        SourceTextFolder folder => folder.Items,
        _ => [],
    };

    /// <summary>What a body uses: (<c>snippet</c> | <c>macro</c> | <c>renderer</c>, the top-level id), in the order found.</summary>
    private static IEnumerable<(string Kind, string Id)> Used(TemplateReads reads)
    {
        foreach (var read in reads.Reads.Concat(reads.Places))
        {
            if (ReferenceString.Parse(read) is { Segments: [var root and ("snippet" or "macro" or "renderer"), var id, ..] })
                yield return (root, id);
        }

        foreach (var render in reads.Renders)
            yield return ("renderer", render.Renderer);
    }

    private static bool PackageHas(SourcePackage package, string kind, string id) => kind switch
    {
        "snippet" => package.Definitions.Snippets.Items.Any(s => s.Id == id),
        "macro" => package.Definitions.Macros.Items.Any(m => m.Id == id),
        _ => package.Definitions.Renderers.Items.Any(r => r.Id == id),
    };

    /// <summary>The reference strings of <paramref name="function"/>'s headers that may name a snippet: each loop's <c>in</c>, each call's <c>with</c> values.</summary>
    private static IEnumerable<string> HeaderReferences(SourceFunction function)
    {
        foreach (var node in ScopeTree.Walk(function.Body.Nodes))
        {
            if (node is SourceFor loop)
                yield return loop.In;
            else if (node is SourceLeaf { Kind: "call" } call && call.Header["with"] is JsonObject with)
            {
                foreach (var (_, value) in with)
                {
                    if (SourceLayout.Text(value) is { } text)
                        yield return text;
                }
            }
        }
    }

    /// <summary>
    /// S04-063: each macro <paramref name="placer"/> places (<c>macro.&lt;id&gt;</c>, or <c>macro.&lt;folder&gt;.&lt;item&gt;</c>
    /// with the folder's shared <c>if</c>), the function's own first, then the package's: a <c>node.&lt;id&gt;</c> its
    /// <c>if</c> reads that is not visible where it is placed, said once per macro and node.
    /// </summary>
    private static void Placed(
        SourcePackage package, ScopeTree tree, SourceFunction function, SourceLeaf placer, TemplateReads reads,
        HashSet<(string Macro, string Node)> said, List<SourceRefusal> refusals)
    {
        var frame = tree.FrameOf(placer);
        foreach (var place in reads.Places)
        {
            if (ReferenceString.Parse(place) is not { Segments: ["macro", var id, ..] segments })
                continue;
            var itemId = segments.Count > 2 ? segments[2] : null;
            var entry = function.Definitions.Macros.Items.FirstOrDefault(m => m.Id == id)
                ?? package.Definitions.Macros.Items.FirstOrDefault(m => m.Id == id);
            var (name, rules) = entry switch
            {
                SourceSingleText single => (id, new[] { single.Text.Header["if"] }),
                SourceTextFolder folder when folder.Items.FirstOrDefault(i => i.Id == itemId) is { } item =>
                    ($"{id}.{item.Id}", new[] { folder.Index["if"], item.Header["if"] }),
                SourceTextFolder folder => (id, new[] { folder.Index["if"] }),
                _ => (id, Array.Empty<JsonNode?>()),
            };
            foreach (var path in rules.SelectMany(JsonLogicShape.Paths))
            {
                if (path.Split('.') is ["node", var node, ..] && frame.Visible("node", node) == null && said.Add((name, node)))
                    refusals.Add(ResolveSentences.PlacedMacroReadsNothing(name, function.Id, node, placer.Path));
            }
        }
    }
}
