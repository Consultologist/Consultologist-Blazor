using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the loops of a package (part 04 §§ 2, 3; § 10.1's <c>for</c> and <c>while</c> rows):
/// each <c>for</c>'s source (<see cref="ScopeTree.Source"/>) and its <c>itemLabel</c>, and each <c>while</c>'s body
/// nodes that nothing returns or reads (S04-035). #1503 (area 06): and a <c>checkFirst</c> while whose check reads its
/// own body (A06-002).
/// </summary>
public static partial class SourceResolve
{
    /// <summary>The kinds of leaf whose body is a template, whose reads are unknown until <c>ITemplateReads</c> is supplied (D1).</summary>
    private static readonly string[] TemplateKinds = ["agent", "rendered"];

    /// <summary>Every loop's source and <c>itemLabel</c>, then every while's body, in source order.</summary>
    private static void Loops(ScopeTree tree, List<SourceRefusal> refusals)
    {
        foreach (var node in tree.All())
        {
            switch (node)
            {
                case SourceFor loop:
                    var source = tree.Source(loop);
                    if (source.Resolved.Refusal is { } refusal)
                        refusals.Add(refusal);
                    else if (loop.ItemLabel is { } field && source.Element is { } element
                        && ItemLabel(loop, field, element, source.Reference, tree.FormatsOf(tree.FrameOf(loop))) is { } label)
                        refusals.Add(label);
                    break;
                case SourceWhile loop:
                    CheckedFirst(tree, loop, refusals);
                    Unused(tree, loop, refusals);
                    break;
            }
        }
    }

    /// <summary>
    /// #1503 (R3; #1507, item 4): A06-002 once for each node of <paramref name="loop"/>'s own body that its <c>while</c>
    /// rule reads, as this pass's (<c>node.&lt;id&gt;</c> or <c>pass.node.&lt;id&gt;</c>), when it declares
    /// <c>checkFirst</c> true: such a check has no value to read before the first pass. One pass back
    /// (<c>pass.previous.node.&lt;id&gt;</c>, <c>null</c> on the first pass by part 04 § 3.3) is not this pass's.
    /// </summary>
    private static void CheckedFirst(ScopeTree tree, SourceWhile loop, List<SourceRefusal> refusals)
    {
        if (!loop.CheckFirst || tree.BodyOf(loop) is not { } frame)
            return;
        var body = new HashSet<SourceNode>(ScopeTree.Walk(loop.Body.Nodes), ReferenceEqualityComparer.Instance);
        var said = new HashSet<SourceNode>(ReferenceEqualityComparer.Instance);
        foreach (var path in JsonLogicShape.Paths(loop.While))
        {
            var named = path.Split('.') switch
            {
                ["node", var id, ..] => frame.Visible("node", id),
                ["pass", "node", var id, ..] => ScopeTree.Walk(loop.Body.Nodes).FirstOrDefault(n => n.Id == id),
                _ => null,
            };
            if (named != null && body.Contains(named) && said.Add(named))
                refusals.Add(ResolveSentences.CheckFirstReadsBody(loop.Id, named.Id, SourceLayout.Join(loop.Path, "index.json")));
        }
    }

    /// <summary>
    /// part 04 § 2.7: <paramref name="field"/> is a property of <paramref name="element"/> (an object, or an object that may
    /// be null; S04-028), required and never null (S04-029), and a string: a <c>string</c>, an enum or a text format
    /// (S04-030). Over a loop's <c>.items</c> the element's own <c>label</c> is all three. Null when it is.
    /// </summary>
    private static SourceRefusal? ItemLabel(SourceFor loop, string field, JsonNode element, string reference, FormatSet formats)
    {
        var path = SourceLayout.Join(loop.Path, "index.json");
        var (schema, _) = ExpressionChecker.CoreWithDefs(element, formats);
        var properties = FormatTypes.Shape(element, formats) == FormatShape.Object ? ExpressionChecker.PropertiesOf(element, formats) : [];
        if (!properties.TryGetValue(field, out var property) || property is null)
        {
            var fields = properties.Count == 0 ? "none" : string.Join(", ", properties.Keys);
            return ResolveSentences.ItemLabelNotAField(loop.Id, field, reference, fields, path);
        }

        var required = schema?["required"] is JsonArray names && names.Any(n => JsonLogicShape.TextOf(n) == field);
        if (!required || FormatTypes.IsNullable(element, formats) || FormatTypes.IsNullable(property, formats))
            return ResolveSentences.ItemLabelMayBeMissing(loop.Id, field, path);
        if (FormatTypes.Shape(property, formats) is not (FormatShape.String or FormatShape.Enum or FormatShape.Text))
            return ResolveSentences.ItemLabelNotAString(loop.Id, field, TypeStrings.Print(property, formats), path);
        return null;
    }

    /// <summary>
    /// #1502 (Task 5): S04-035 re-judged once the seam is supplied (D1): every while that holds an agent or a rendered
    /// node, which <see cref="Check"/> left unjudged, counting what each body reads (<paramref name="bodies"/>: the paths
    /// a template leaf's body reads, null when they are not known). A while one of whose bodies is not known stays
    /// unjudged.
    /// </summary>
    internal static void UnusedWithBodies(ScopeTree tree, Func<SourceLeaf, IReadOnlyList<string>?> bodies, List<SourceRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(bodies);
        foreach (var loop in tree.All().OfType<SourceWhile>())
        {
            if (ScopeTree.Walk(loop.Body.Nodes).Any(IsTemplate))
                Unused(tree, loop, refusals, bodies);
        }
    }

    private static bool IsTemplate(SourceNode node) => node is SourceLeaf leaf && TemplateKinds.Contains(leaf.Kind);

    /// <summary>
    /// S04-035: each body node of <paramref name="loop"/> that is not its <c>ret</c> and is read neither by its
    /// <c>while</c> or <c>until</c> rule nor by another node of the body's header (an <c>if</c>, an <c>expression</c>,
    /// an <c>in</c>, an <c>until</c>, a <c>while</c>, a <c>case</c>, a <c>with</c>), nor, when <paramref name="bodies"/> is
    /// given, by another node's body. Without <paramref name="bodies"/> it is not judged while a node anywhere in the body
    /// is an agent or a rendered node, whose template's reads are unknown (D1); with them, while one of those is unknown.
    /// </summary>
    private static void Unused(ScopeTree tree, SourceWhile loop, List<SourceRefusal> refusals, Func<SourceLeaf, IReadOnlyList<string>?>? bodies = null)
    {
        var body = ScopeTree.Walk(loop.Body.Nodes).ToList();
        if (tree.BodyOf(loop) is not { } frame)
            return;
        var read = new HashSet<SourceNode>(ReferenceEqualityComparer.Instance);
        foreach (var leaf in body.OfType<SourceLeaf>().Where(IsTemplate))
        {
            if (bodies?.Invoke(leaf) is not { } paths)
                return;
            var found = new HashSet<SourceNode>(ReferenceEqualityComparer.Instance);
            Read(tree, tree.FrameOf(leaf), paths, found);
            found.Remove(leaf);
            read.UnionWith(found);
        }

        foreach (var rule in new[] { loop.While, loop.Until })
            Read(tree, frame, JsonLogicShape.Paths(rule), read);
        foreach (var reader in body)
        {
            var found = new HashSet<SourceNode>(ReferenceEqualityComparer.Instance);
            foreach (var (at, paths) in Headers(tree, reader))
                Read(tree, at, paths, found);
            found.Remove(reader);
            read.UnionWith(found);
        }

        foreach (var node in loop.Body.Nodes)
        {
            if (node.Key != loop.Body.Ret && !read.Contains(node))
            {
                var path = node is SourceLeaf ? node.Path : SourceLayout.Join(node.Path, "index.json");
                refusals.Add(ResolveSentences.WhileNodeUnused(node.Id, loop.Id, path));
            }
        }
    }

    /// <summary>What <paramref name="node"/>'s header reads, with the body each is read in: its rules' paths and its reference strings.</summary>
    private static IEnumerable<(ScopeFrame At, IReadOnlyList<string> Paths)> Headers(ScopeTree tree, SourceNode node)
    {
        var frame = tree.FrameOf(node);
        switch (node)
        {
            case SourceLeaf leaf:
                yield return (frame, JsonLogicShape.Paths(leaf.Header["if"]));
                yield return (frame, JsonLogicShape.Paths(leaf.Header["expression"]));
                if (leaf.Header["with"] is JsonObject with)
                    yield return (frame, with.Select(p => JsonLogicShape.TextOf(p.Value)).OfType<string>().ToList());
                break;
            case SourceFor loop:
                yield return (frame, [loop.In]);
                if (tree.BodyOf(loop) is { } body)
                    yield return (body, JsonLogicShape.Paths(loop.Until));
                break;
            case SourceWhile loop when tree.BodyOf(loop) is { } inner:
                yield return (inner, JsonLogicShape.Paths(loop.While));
                yield return (inner, JsonLogicShape.Paths(loop.Until));
                break;
            case SourceIf branch:
                yield return (frame, JsonLogicShape.Paths(branch.If));
                break;
            case SourceCase choice:
                yield return (frame, [choice.Case]);
                break;
        }
    }

    /// <summary>The nodes <paramref name="paths"/>, read at <paramref name="frame"/>, name: <c>node.&lt;id&gt;</c> and <c>pass.…node.&lt;id&gt;</c>.</summary>
    private static void Read(ScopeTree tree, ScopeFrame frame, IReadOnlyList<string> paths, HashSet<SourceNode> read)
    {
        foreach (var path in paths)
        {
            var s = path.Split('.');
            if (s is ["node", var id, ..] && frame.Visible("node", id) is { } node)
            {
                read.Add(node);
                continue;
            }

            if (s.Length < 3 || s[0] != "pass")
                continue;
            var at = 1;
            var up = 0;
            while (at < s.Length && s[at] == "parent")
            {
                at++;
                up++;
            }

            if (at < s.Length && s[at] == "previous")
                at++;
            if (at + 1 < s.Length && s[at] == "node" && frame.While(up) is { } loop
                && ScopeTree.Walk(loop.Body.Nodes).FirstOrDefault(n => n.Id == s[at + 1]) is { } named)
            {
                read.Add(named);
            }
        }
    }
}
