using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the loops of a package (part 04 §§ 2, 3; § 10.1's <c>for</c> and <c>while</c> rows):
/// each <c>for</c>'s source (<see cref="ScopeTree.Source"/>) and its <c>itemLabel</c>, and each <c>while</c>'s body
/// nodes that nothing returns or reads (S04-035).
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
                    Unused(tree, loop, refusals);
                    break;
            }
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
    /// S04-035: each body node of <paramref name="loop"/> that is not its <c>ret</c> and is read neither by its
    /// <c>while</c> or <c>until</c> rule nor by another node of the body's header (an <c>if</c>, an <c>expression</c>,
    /// an <c>in</c>, an <c>until</c>, a <c>while</c>, a <c>case</c>, a <c>with</c>). Not judged while a node anywhere in
    /// the body is an agent or a rendered node, whose template's reads are unknown (D1).
    /// </summary>
    private static void Unused(ScopeTree tree, SourceWhile loop, List<SourceRefusal> refusals)
    {
        var body = ScopeTree.Walk(loop.Body.Nodes).ToList();
        if (body.Any(n => n is SourceLeaf leaf && TemplateKinds.Contains(leaf.Kind)) || tree.BodyOf(loop) is not { } frame)
            return;

        var read = new HashSet<SourceNode>(ReferenceEqualityComparer.Instance);
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
