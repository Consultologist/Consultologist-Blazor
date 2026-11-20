using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: what the JSON Logic tests share: a rule from its text, the published formats (read off the
/// submodules by <see cref="V31Contract.PublishedTypes"/>, failing loudly when they are not checked
/// out), a site of each kind, a dictionary-backed <see cref="IExpressionScope"/>, and the mutants of a
/// rule (each position of its tree replaced by garbage) that no service may throw on.
/// </summary>
internal static class LogicFixture
{
    private static readonly Lazy<TypeRegistry> Registry = new(V31Contract.PublishedTypes);

    /// <summary>A rule from its JSON text; <c>null</c> for the text <c>null</c>.</summary>
    public static JsonNode? Rule(string json) => JsonNode.Parse(json);

    /// <summary>The published registry's formats, plus package formats given as (id, schema JSON).</summary>
    public static FormatSet Formats(params (string Id, string Json)[] package) =>
        FormatSet.ForPackage(
            Registry.Value,
            package.Select(p => new SourceFormat(p.Id, $"formats/{p.Id}.json", JsonNode.Parse(p.Json)!.AsObject())));

    /// <summary>
    /// A site of <paramref name="kind"/>: <c>Node 'gate'</c> in <c>nodes/gate.md</c>, the member its kind
    /// gives; a deliverable's is <c>Deliverable 'note'</c>, a macro's <c>Macro 'footer'</c>, and an
    /// <see cref="ExpressionSiteKind.Expression"/> is <c>Computed node 'total'</c> declaring <paramref name="declared"/>.
    /// </summary>
    public static ExpressionSite Site(ExpressionSiteKind kind, JsonNode? declared = null) => kind switch
    {
        ExpressionSiteKind.Expression => new(kind, "Computed node 'total'", "expression", "nodes/total.md", "total", declared),
        ExpressionSiteKind.DeliverableIf => new(kind, "Deliverable 'note'", "if", "deliverables/note.md", null, null),
        ExpressionSiteKind.MacroIf => new(kind, "Macro 'footer'", "if", "macros/footer.md", null, null),
        ExpressionSiteKind.While => new(kind, "Node 'gate'", "while", "nodes/gate.md", null, null),
        ExpressionSiteKind.Until => new(kind, "Node 'gate'", "until", "nodes/gate.md", null, null),
        _ => new(kind, "Node 'gate'", "if", "nodes/gate.md", null, null),
    };

    /// <summary>A head read through <c>.value</c>, <c>.content</c> or <c>.title</c>, of <paramref name="format"/> (a name or a schema's JSON).</summary>
    public static ScopeHead FormatHead(string format, bool nullable = false) =>
        new(ScopeHeadKind.Format, 0, Format(format), nullable, null);

    /// <summary>A head that is already a value of <paramref name="format"/>.</summary>
    public static ScopeHead ValueHead(string format, bool nullable = false) =>
        new(ScopeHeadKind.Value, 0, Format(format), nullable, null);

    /// <summary>A head with no type yet (D2).</summary>
    public static ScopeHead Deferred() => new(ScopeHeadKind.Deferred, 0, null, false, null);

    /// <summary>A head the scope refuses with <paramref name="refusal"/>.</summary>
    public static ScopeHead Refused(SourceRefusal refusal) => new(ScopeHeadKind.Refused, 0, null, false, refusal);

    /// <summary>A name (<c>number</c>) as its text; anything starting with <c>{</c> as a schema.</summary>
    private static JsonNode Format(string format) =>
        format.StartsWith('{') ? JsonNode.Parse(format)! : JsonValue.Create(format);

    /// <summary>
    /// An <see cref="IExpressionScope"/> over a dictionary from a head (<c>input.kind</c>, <c>node.draft</c>,
    /// <c>run.date</c>, <c>item</c>) to what it is. The longest head that starts the path wins; its
    /// <see cref="ScopeHead.Length"/> is the head's segment count. Anything else is <see cref="ScopeHeadKind.Unknown"/>.
    /// </summary>
    public sealed class MapScope(IReadOnlyDictionary<string, ScopeHead> heads) : IExpressionScope
    {
        public ScopeHead Head(IReadOnlyList<string> segments)
        {
            for (var length = segments.Count; length > 0; length--)
            {
                if (heads.TryGetValue(string.Join('.', segments.Take(length)), out var head))
                    return head with { Length = length };
            }

            return new ScopeHead(ScopeHeadKind.Unknown, 0, null, false, null);
        }
    }

    /// <summary>Every rule made from <paramref name="rule"/> by replacing one position of its tree (the root included) with a piece of garbage.</summary>
    public static IEnumerable<JsonNode?> Mutants(JsonNode? rule)
    {
        var positions = new List<List<object>>();
        Positions(rule, [], positions);
        foreach (var position in positions)
            foreach (var replacement in Replacements())
                yield return Replace(rule, position, replacement);
    }

    private static IEnumerable<JsonNode?> Replacements() =>
    [
        null,
        JsonValue.Create(true),
        JsonValue.Create(5),
        JsonValue.Create("x"),
        new JsonArray(),
        new JsonObject(),
        new JsonObject { ["a"] = 1, ["b"] = 2 },
        new JsonObject { ["var"] = null },
        new JsonObject { [""] = new JsonArray() },
    ];

    private static void Positions(JsonNode? node, List<object> path, List<List<object>> into)
    {
        into.Add([.. path]);
        switch (node)
        {
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    path.Add(i);
                    Positions(array[i], path, into);
                    path.RemoveAt(path.Count - 1);
                }
                break;
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    path.Add(key);
                    Positions(value, path, into);
                    path.RemoveAt(path.Count - 1);
                }
                break;
        }
    }

    private static JsonNode? Replace(JsonNode? root, List<object> path, JsonNode? replacement)
    {
        if (path.Count == 0)
            return replacement?.DeepClone();
        var copy = root!.DeepClone();
        var parent = copy;
        for (var i = 0; i < path.Count - 1; i++)
            parent = path[i] is int index ? parent![index] : parent![(string)path[i]];
        if (path[^1] is int last)
            parent![last] = replacement?.DeepClone();
        else
            parent![(string)path[^1]] = replacement?.DeepClone();
        return copy;
    }
}
