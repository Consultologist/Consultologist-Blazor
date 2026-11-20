using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): the rules that need the whole tree (part 01 §§ 7 and 9). Ids
/// are judged per scope — <c>nodes/</c>, <c>checks/</c> and each function — and never across them
/// (S01-002, S01-036); every expression is held to <see cref="SourceBounds.ExpressionNodes"/>
/// (S01-042). It runs on whatever tree the walk built, which is partial when the walk refused
/// something: a node it could not read is simply absent, and nothing here assumes a child exists.
/// </summary>
internal static class SourceScopes
{
    /// <summary>A node in a scope, with the ids of the flow nodes that enclose it (its folder path).</summary>
    private sealed record Entry(SourceNode Node, IReadOnlyList<string> Folder);

    /// <summary>Judges every scope of <paramref name="package"/> and adds what it finds to <paramref name="refusals"/>.</summary>
    public static void Check(SourcePackage package, List<SourceRefusal> refusals)
    {
        var scopes = new List<IReadOnlyList<Entry>> { Collect(package.Nodes.Nodes), Collect(package.Checks.Nodes) };
        scopes.AddRange(package.Functions.Items.Select(function => Collect(function.Body.Nodes)));

        foreach (var scope in scopes)
        {
            CheckIds(scope, refusals);
            CheckWhiles(scope, refusals);
            foreach (var entry in scope)
                CheckNodeExpressions(entry.Node, refusals);
        }

        CheckDefinitions(package.Definitions, refusals);
        foreach (var function in package.Functions.Items)
            CheckDefinitions(function.Definitions, refusals);
        CheckDeliverables(package.Deliverables, refusals);
    }

    /// <summary>
    /// S04-012: <paramref name="ret"/>, as written, must be one of <paramref name="children"/>, the
    /// full names (keys) of the body's own listed children. The caller judges only a body whose list
    /// it could read and whose <c>ret</c> is text; an empty list has nothing to judge.
    /// </summary>
    public static void CheckReturn(
        string subject, string ret, IReadOnlyList<string> children, string path, List<SourceRefusal> refusals)
    {
        if (children.Count > 0 && !children.Contains(ret))
            refusals.Add(SourceSentences.ReturnsNonChild(subject, ret, string.Join(", ", children), path));
    }

    // ---- Collecting a scope's nodes ---------------------------------------------------------------

    private static IReadOnlyList<Entry> Collect(IReadOnlyList<SourceNode> top)
    {
        var entries = new List<Entry>();
        Visit(top, [], entries);
        return entries;
    }

    /// <summary>Every node under <paramref name="nodes"/> in tree order, a flow node before its children.</summary>
    private static void Visit(IReadOnlyList<SourceNode> nodes, IReadOnlyList<string> folder, List<Entry> entries)
    {
        foreach (var node in nodes)
        {
            entries.Add(new Entry(node, folder));
            var inner = new List<string>(folder) { node.Id };
            foreach (var block in Blocks(node))
                Visit(block.Nodes, inner, entries);
        }
    }

    /// <summary>The bodies of a flow node, in written order; none for a leaf.</summary>
    private static IEnumerable<SourceBlock> Blocks(SourceNode node)
    {
        switch (node)
        {
            case SourceFor f:
                yield return f.Body;
                break;
            case SourceWhile w:
                yield return w.Body;
                break;
            case SourceIf i:
                yield return i.Then;
                if (i.Else != null)
                    yield return i.Else;
                break;
            case SourceCase c:
                foreach (var branch in c.Branches)
                    yield return branch.Body;
                if (c.Else != null)
                    yield return c.Else;
                break;
        }
    }

    /// <summary>The file a refusal about <paramref name="node"/> names: the file, or a flow node's <c>index.json</c>.</summary>
    private static string FileOf(SourceNode node) => node is SourceLeaf ? node.Path : $"{node.Path}/index.json";

    private static string Root(SourceNode node) => node.Key[..node.Key.IndexOf('.')];

    // ---- S01-002 ---------------------------------------------------------------------------------

    /// <summary>
    /// Two nodes of one id where one's folder path is a strict prefix of the other's: refused once, on
    /// the deeper node. (Equal paths are one folder, where an id twice is S04-013's.)
    /// </summary>
    private static void CheckIds(IReadOnlyList<Entry> scope, List<SourceRefusal> refusals)
    {
        foreach (var group in scope.GroupBy(e => e.Node.Id, StringComparer.Ordinal))
        {
            var same = group.ToList();
            for (var i = 0; i < same.Count; i++)
            {
                for (var j = i + 1; j < same.Count; j++)
                {
                    var (shallow, deep) = same[i].Folder.Count <= same[j].Folder.Count ? (same[i], same[j]) : (same[j], same[i]);
                    if (!IsStrictPrefix(shallow.Folder, deep.Folder))
                        continue;

                    var root = Root(deep.Node) == "check" ? "check" : null;
                    var other = shallow.Folder.Count == 0 ? "at the top level" : $"in {Dotted(root, shallow.Folder)}";
                    refusals.Add(SourceSentences.NodeReusesVisibleId(
                        group.Key, Dotted(root, deep.Folder), other, FileOf(deep.Node)));
                }
            }
        }
    }

    private static bool IsStrictPrefix(IReadOnlyList<string> shorter, IReadOnlyList<string> longer) =>
        shorter.Count < longer.Count && shorter.SequenceEqual(longer.Take(shorter.Count), StringComparer.Ordinal);

    /// <summary>The ids joined by <c>.</c>, with the root first when there is one (D13).</summary>
    private static string Dotted(string? root, IReadOnlyList<string> folder) =>
        string.Join('.', root == null ? folder : folder.Prepend(root));

    // ---- S01-036 ---------------------------------------------------------------------------------

    /// <summary>
    /// Inside the outermost <c>while</c> that holds them, two nodes of one id at any depth that
    /// S01-002 did not already refuse as a pair (D14), once each, in tree order.
    /// </summary>
    private static void CheckWhiles(IReadOnlyList<Entry> scope, List<SourceRefusal> refusals)
    {
        foreach (var outer in Outermost(scope))
        {
            var inside = new List<Entry>();
            foreach (var block in Blocks(outer.Node))
                Visit(block.Nodes, [.. outer.Folder, outer.Node.Id], inside);

            for (var i = 0; i < inside.Count; i++)
            {
                for (var j = i + 1; j < inside.Count; j++)
                {
                    var (a, b) = (inside[i], inside[j]);
                    if (a.Node.Id != b.Node.Id
                        || IsStrictPrefix(a.Folder, b.Folder) || IsStrictPrefix(b.Folder, a.Folder))
                    {
                        continue;
                    }

                    refusals.Add(SourceSentences.NodeTwiceInWhile(
                        a.Node.Id, outer.Node.Id, WithoutRoot(a.Node.Key), WithoutRoot(b.Node.Key), FileOf(outer.Node)));
                }
            }
        }
    }

    /// <summary>The <c>while</c> nodes that no other <c>while</c> of the scope encloses.</summary>
    private static IEnumerable<Entry> Outermost(IReadOnlyList<Entry> scope)
    {
        var whiles = scope.Where(e => e.Node is SourceWhile).ToList();
        return whiles.Where(w => !whiles.Any(o =>
        {
            // o encloses w when o's own path (its folder, then its id) begins w's folder path.
            var own = o.Folder.Append(o.Node.Id).ToList();
            return !ReferenceEquals(o, w) && own.Count <= w.Folder.Count && own.SequenceEqual(w.Folder.Take(own.Count), StringComparer.Ordinal);
        }));
    }

    private static string WithoutRoot(string key) => key[(key.IndexOf('.') + 1)..];

    // ---- S01-042 ---------------------------------------------------------------------------------

    /// <summary>
    /// The nodes of an expression (D1): one for each operation (an object with one key, whose
    /// arguments are counted, except a <c>var</c>'s, which are not) and one for each scalar literal
    /// (<c>null</c> included); an array counts only its elements. An object of any other size is not
    /// an operation: it counts one and its members' values. A site that is <c>null</c> counts nothing.
    /// </summary>
    /// <remarks>
    /// The recursion is plain, and bounded: <see cref="StrictJson"/> refuses JSON nested past
    /// <see cref="SourceBounds.JsonDepth"/> (32) before any value reaches here.
    /// </remarks>
    public static int CountExpressionNodes(JsonNode? expression) => expression is null ? 0 : Count(expression);

    private static int Count(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                return array.Sum(Count);
            case JsonObject { Count: 1 } operation:
            {
                var (name, argument) = operation.First();
                return name == "var" ? 1 : 1 + Count(argument);
            }

            case JsonObject other:
                return 1 + other.Sum(member => Count(member.Value));
            default:
                return 1;
        }
    }

    /// <summary>Refuses <paramref name="expression"/> above the bound (S01-042); <paramref name="site"/> is D15's name.</summary>
    private static void CheckExpression(string site, string path, JsonNode? expression, List<SourceRefusal> refusals)
    {
        var count = CountExpressionNodes(expression);
        if (count > SourceBounds.ExpressionNodes)
            refusals.Add(SourceSentences.ExpressionTooLarge(site, path, count));
    }

    /// <summary>A node's expression sites: a leaf's <c>if</c> and <c>expression</c> and each agent part's <c>if</c>; a flow node's <c>if</c>, <c>while</c> and <c>until</c>.</summary>
    private static void CheckNodeExpressions(SourceNode node, List<SourceRefusal> refusals)
    {
        var path = FileOf(node);
        var word = node switch
        {
            SourceFor => "Loop",
            SourceWhile => "While",
            SourceIf => "If",
            SourceCase => "Case",
            _ => Root(node) == "check" ? "Check" : "Node",
        };
        var name = $"{word} '{node.Id}'";

        CheckExpression($"{name} if", path, node.Header["if"], refusals);
        if (node is SourceLeaf)
        {
            CheckExpression($"{name} expression", path, node.Header["expression"], refusals);
            if (node.Header["parts"] is JsonObject parts)
            {
                foreach (var (id, part) in parts)
                    CheckExpression($"{name} part '{id}' if", path, (part as JsonObject)?["if"], refusals);
            }
        }
        else
        {
            CheckExpression($"{name} while", path, node.Header["while"], refusals);
            CheckExpression($"{name} until", path, node.Header["until"], refusals);
        }
    }

    /// <summary>The <c>if</c> of snippets, macros and their folders and items (a snippet folder item's header is any object, D11).</summary>
    private static void CheckDefinitions(SourceDefinitions definitions, List<SourceRefusal> refusals)
    {
        foreach (var (entries, word) in new[] { (definitions.Snippets, "Snippet"), (definitions.Macros, "Macro") })
        {
            foreach (var entry in entries.Items)
            {
                switch (entry)
                {
                    case SourceSingleText single:
                        CheckExpression($"{word} '{single.Id}' if", single.Text.Path, single.Text.Header["if"], refusals);
                        break;
                    case SourceTextFolder folder when word == "Macro":
                        var index = SourceLayout.Join(folder.Path, "index.json");
                        CheckExpression($"{index} if", index, folder.Index["if"], refusals);
                        foreach (var item in folder.Items)
                            CheckExpression($"Item '{folder.Id}.{item.Id}' if", item.Path, item.Header["if"], refusals);
                        break;
                }
            }
        }
    }

    private static void CheckDeliverables(SourceDeliveryFolder folder, List<SourceRefusal> refusals)
    {
        foreach (var deliverable in folder.Deliverables)
            CheckExpression($"Deliverable '{deliverable.Id}' if", deliverable.Path, deliverable.Header["if"], refusals);
        foreach (var inner in folder.Folders)
            CheckDeliverables(inner, refusals);
    }
}
