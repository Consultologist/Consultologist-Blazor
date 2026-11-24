using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the node and flow rows; part 04 §§ 1 to 5; part 01 § 9.3): the flow
/// nodes. Each 30 node with <c>for</c>, <c>while</c>, <c>if</c> or <c>case</c> (<see cref="WorkflowImpliedPaths.IsFlow"/>)
/// becomes the folder <c>nodes/…/&lt;id&gt;/</c> whose <c>index.json</c> is its header, with its body's nodes inside it.
/// Every node's 31 rooted key is its chain of flow ids (<c>node.sections_draft.route.narrative_draft</c>), which is what
/// a <c>ret</c> writes (part 04 § 1), and where 31 reads it is <c>node.&lt;id&gt;</c>, a reference resolving outward by
/// bare id (part 01 § 9). This file holds the tree, what each node is read as (<see cref="Migrated.NodeReads"/>, with its
/// format and whether it is always valued), each condition site's place (<see cref="Migrated.Places"/>), and the flow
/// headers.
/// </summary>
internal static partial class Migrate
{
    /// <summary>The 30 nodes as a tree: each by id, each body node's flow node, the top level in 30's order.</summary>
    private sealed class NodeTree
    {
        public NodeTree(IReadOnlyList<WorkflowNodeSpec> nodes)
        {
            foreach (var node in nodes)
                ById.TryAdd(node.Id, node);
            foreach (var node in ById.Values.Where(WorkflowImpliedPaths.IsFlow))
            {
                foreach (var child in Children(node))
                    Owner.TryAdd(child, node.Id);
            }

            Top = [.. ById.Keys.Where(id => !Owner.ContainsKey(id))];
        }

        /// <summary>The leaves a deliverable composes (R9, D20): filled by <see cref="ComposedLeaves"/> before any format is worked out.</summary>
        public HashSet<string> Composed { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, WorkflowNodeSpec> ById { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Owner { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Top { get; }

        /// <summary>The flow nodes <paramref name="id"/> runs inside, nearest first (guarded against a body holding its holder, which 30 refuses).</summary>
        public IReadOnlyList<string> Ancestors(string id)
        {
            var chain = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            for (var at = id; Owner.TryGetValue(at, out var owner) && seen.Add(owner); at = owner)
                chain.Add(owner);
            return chain;
        }

        /// <summary>
        /// A flow node's children in its folder's order (part 01 § 2.2): a loop's <c>nodes</c>; an <c>if</c>'s
        /// <c>then</c> then <c>else</c>; a <c>case</c>'s branches in order, then its <c>else</c>; each once.
        /// </summary>
        public static IReadOnlyList<string> Children(WorkflowNodeSpec flow)
        {
            IEnumerable<string> ids = flow switch
            {
                { For: not null } or { While: not null } => flow.Nodes ?? [],
                { If: not null } => [.. flow.Then?.Nodes ?? [], .. flow.Else?.Nodes ?? []],
                _ => [.. (flow.Branches ?? []).SelectMany(b => b.Nodes ?? []), .. flow.Else?.Nodes ?? []],
            };
            return ids.Distinct(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>The 30 list a <c>for</c> iterates (its <c>for</c> without <c>item in </c>), or null.</summary>
    private static string? CollectionOf(WorkflowNodeSpec loop) =>
        loop.For is { } text && text.StartsWith("item in ", StringComparison.Ordinal) ? text["item in ".Length..].Trim() : null;

    /// <summary>
    /// Part 04 § 2.2: a loop's <c>in</c>, a reference string: <c>input.&lt;id&gt;</c> is <c>input.&lt;id&gt;.value</c>; a
    /// snippet folder (<c>snippet.&lt;folder&gt;</c>), a list in a node's value (<c>node.&lt;id&gt;.value.&lt;field&gt;</c>)
    /// and a list field of the outer item (<c>item.value.&lt;field&gt;</c>) are written as 30 wrote them.
    /// </summary>
    private static string InOf(string collection)
    {
        var segments = collection.Split('.');
        return segments is ["input", _] ? collection + ".value" : collection;
    }

    /// <summary>
    /// Fills <see cref="Migrated.Keys"/>, <see cref="Migrated.NodeReads"/> and <see cref="Migrated.Places"/> for every 30
    /// node before any condition is converted (a condition may read a node written after it). A <c>terms_subset</c> node
    /// is read at <c>check.&lt;id&gt;</c> as a <c>check_result</c> (Task 11 writes the check). A <c>case</c> is first taken
    /// not to be always valued; once its branches are known (<see cref="CaseBranches"/>), every node's
    /// <see cref="ConversionRead.AlwaysValued"/> is worked out again.
    /// </summary>
    private static void PlaceNodes(Migrated m, NodeTree tree, Dictionary<string, CaseShape> cases)
    {
        foreach (var (id, node) in tree.ById)
        {
            if (IsCheck(m, node))
            {
                m.Keys[id] = "check." + id;
                continue;
            }

            m.Keys[id] = "node." + string.Join('.', [.. tree.Ancestors(id).Reverse(), id]);
        }

        var formats = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var id in tree.ById.Keys)
            FormatOfNode(m, tree, id, formats, 0);

        foreach (var (id, node) in tree.ById)
        {
            var path = IsCheck(m, node) ? "check." + id : "node." + id;
            m.NodeReads[id] = new ConversionRead(path, formats[id].DeepClone()) { AlwaysValued = AlwaysValued(tree, id, cases, 0) };
        }

        // The places: a loop's own until and a while's own checks see its item or its pass; every other site its owner's.
        var set = FormatSet.ForPackage(m.Options.Registry, m.PackageFormats());
        foreach (var id in tree.ById.Keys)
            m.Places[id] = PlaceOf(m, tree, id, set, inner: tree.ById[id].For is not null || tree.ById[id].While is not null);
    }

    /// <summary>
    /// The 31 format of node <paramref name="id"/>, memoised in <paramref name="formats"/>: a leaf's (<see cref="LeafFormat"/>);
    /// a <c>for</c>'s is the list of its <c>ret</c>'s, a <c>while</c>'s its <c>ret</c>'s, an <c>if</c>'s or a
    /// <c>case</c>'s the union of its bodies' <c>ret</c>s (part 02 § 2.2). The flow's format is this
    /// derived one, which the 31 checker holds it to; the derived-flags pass leaves it as written here.
    /// </summary>
    private static JsonNode FormatOfNode(Migrated m, NodeTree tree, string id, Dictionary<string, JsonNode> formats, int depth)
    {
        if (formats.TryGetValue(id, out var known))
            return known;
        var node = tree.ById[id];
        JsonNode format;
        if (!WorkflowImpliedPaths.IsFlow(node))
        {
            format = IsCheck(m, node) ? JsonValue.Create("check_result") : LeafFormat(m, node, tree.Composed.Contains(id));
        }
        else if (depth > SourceBounds.JsonDepth)
        {
            format = JsonValue.Create("plaintext");
        }
        else
        {
            JsonNode? Ret(string? ret) =>
                RetId(ret) is { } child && tree.ById.ContainsKey(child) ? FormatOfNode(m, tree, child, formats, depth + 1) : null;

            var set = FormatSet.ForPackage(m.Options.Registry, m.PackageFormats());
            if (node.For is not null)
            {
                format = Ret(node.Ret) is { } item ? FormatTypes.ListOf(item) : FormatTypes.ListOf(JsonValue.Create("plaintext"));
            }
            else if (node.While is not null)
            {
                format = Ret(node.Ret)?.DeepClone() ?? JsonValue.Create("plaintext");
            }
            else
            {
                IEnumerable<string?> rets = node.If is not null
                    ? [node.Then?.Ret, node.Else?.Ret]
                    : [.. (node.Branches ?? []).Select(b => b.Ret), node.Else?.Ret];
                var members = rets.Select(Ret).OfType<JsonNode>().ToList();
                format = FormatTypes.UnionOf(members, set) ?? JsonValue.Create("plaintext");
            }
        }

        formats[id] = format;
        return format;
    }

    /// <summary>The id a 30 <c>ret</c> (<c>node.&lt;id&gt;</c>) names, or null.</summary>
    private static string? RetId(string? ret) =>
        ret is not null && ret.StartsWith("node.", StringComparison.Ordinal) && ret.IndexOf('.', 5) < 0 && ret.Length > 5 ? ret[5..] : null;

    /// <summary>
    /// Part 04 § 7.6's "always valued", as area 05's <c>ScopeTree.AlwaysValued</c> has it: a leaf (none has an <c>if</c>
    /// at 30, whose node <c>when</c> is retired since 26) and a <c>for</c>, always; a <c>while</c> that checks after its
    /// first pass; an <c>if</c> with an <c>else</c>, a <c>case</c> with no empty branch, when every <c>ret</c> is. A
    /// <c>case</c> not yet in <paramref name="cases"/> is not.
    /// </summary>
    private static bool AlwaysValued(NodeTree tree, string id, Dictionary<string, CaseShape> cases, int depth)
    {
        if (depth > SourceBounds.JsonDepth || !tree.ById.TryGetValue(id, out var node))
            return false;
        bool Ret(string? ret) => RetId(ret) is { } child && AlwaysValued(tree, child, cases, depth + 1);

        return node switch
        {
            { For: not null } => true,
            { While: not null } => node.CheckFirst != true,
            { If: not null } => node.Else is not null && Ret(node.Then?.Ret) && Ret(node.Else.Ret),
            { Case: not null } => cases.TryGetValue(id, out var shape) && shape.Missing.Count == 0
                && (node.Branches ?? []).All(b => Ret(b.Ret)) && (node.Else is null || Ret(node.Else.Ret)),
            _ => true,
        };
    }

    /// <summary>
    /// Where a condition owned by <paramref name="id"/> is judged (<see cref="SitePlace"/>): with <paramref name="inner"/>
    /// (a loop's <c>until</c>, a while's checks) inside the node itself, else where the node sits. The item is the
    /// innermost loop's element (<see cref="ElementOf"/>); <c>pass.number</c> is readable inside a while.
    /// </summary>
    private static SitePlace PlaceOf(Migrated m, NodeTree tree, string id, FormatSet formats, bool inner)
    {
        IReadOnlyList<string> chain = inner ? [id, .. tree.Ancestors(id)] : tree.Ancestors(id);
        var inWhile = chain.Any(a => tree.ById[a].While is not null);
        var loop = chain.FirstOrDefault(a => tree.ById[a].For is not null);
        if (loop is null)
            return new SitePlace(null, false, inWhile);
        var (element, folder) = ElementOf(m, tree, loop, formats, 0);
        return new SitePlace(element, folder, inWhile);
    }

    /// <summary>
    /// The element of the list <paramref name="loop"/> iterates (part 04 § 2.6's <c>item.value</c>): a snippet folder's
    /// item is a value of the folder's format (and a folder item); an array input's, a node list's or an outer item's
    /// list field's element is the list's <c>items</c>. Null when the list does not type.
    /// </summary>
    private static (JsonNode? Element, bool Folder) ElementOf(Migrated m, NodeTree tree, string loop, FormatSet formats, int depth)
    {
        if (depth > SourceBounds.JsonDepth || CollectionOf(tree.ById[loop]) is not { } collection)
            return (null, false);
        var segments = collection.Split('.');
        switch (segments)
        {
            case ["snippet", var folder]:
                return (m.Sources.GetValueOrDefault($"snippets/{folder}/{WorkflowDataResolver.IndexFileName}")?.Json["format"]?.DeepClone(), true);
            case ["input", var input]:
                return (ExpressionChecker.ItemsOf(m.Manifest["inputs"]?[input]?["format"], formats)?.DeepClone(), false);
            case ["node", var node, "value", ..] when m.NodeReads.TryGetValue(node, out var read):
                return (ListItems(read.Format, segments[2..]), false);
            case ["item", "value", ..]:
                var outer = tree.Ancestors(loop).FirstOrDefault(a => tree.ById[a].For is not null);
                return outer is null ? (null, false) : (ListItems(ElementOf(m, tree, outer, formats, depth + 1).Element, segments[1..]), false);
            default:
                return (null, false);
        }

        JsonNode? ListItems(JsonNode? format, IReadOnlyList<string> steps) =>
            FormatReads.Read(format, steps, formats) is { Fault: ReadFault.None, Format: { } list }
                ? ExpressionChecker.ItemsOf(list, formats)?.DeepClone()
                : null;
    }

    /// <summary>What a 30 <c>case</c> becomes: its subject (the reference, or 30's text when it is a hand edit), the values its branches miss, and whether it is a hand edit.</summary>
    private sealed record CaseShape(JsonNode Subject, IReadOnlyList<string> Missing, bool HandEdited);

    /// <summary>
    /// Part 04 § 5.2, part 10 § 1.2's <c>case</c> row: each 30 <c>case</c>'s subject converted as a value (an input read
    /// becomes <c>input.&lt;id&gt;.value</c>; a classifier's <c>node.&lt;id&gt;.value</c> its <c>.value.value</c>), and the
    /// values of its subject's enum (a classifier's, the values its <c>with</c> gave) that no branch lists. A subject the
    /// converter refuses, or that is no single read (S04-017), is a hand edit, written as 30 wrote it; so is one whose
    /// values 31 cannot list (A10-028), a classifier's (whose answers 31 does not type, A10-032; its branches are still
    /// written from 30's values), and one that can be <c>null</c> where the case has no <c>else</c> (A10-029).
    /// </summary>
    private static Dictionary<string, CaseShape> CaseBranches(Migrated m, NodeTree tree)
    {
        var cases = new Dictionary<string, CaseShape>(StringComparer.Ordinal);
        foreach (var (id, node) in tree.ById.Where(n => n.Value.Case is not null && n.Value.For is null && n.Value.While is null && n.Value.If is null))
        {
            var file = FlowFile(tree, id);
            var text = node.Case!;
            var scope = m.Scope(id);
            var result = WorkflowConditionConverter.FromScriban(text, scope, ConversionKind.Value);
            var refusal = CaseSubjectRefusal(id, file, result);
            if (refusal is not null
                || result.Logic is not JsonObject { Count: 1 } read || read["var"] is not JsonValue path || !path.TryGetValue<string>(out var reference))
            {
                m.HandEdit(file, "case", refusal ?? "");
                cases[id] = new CaseShape(JsonValue.Create(text), [], true);
                continue;
            }

            var type = ConditionTyping.TypeOf(read, scope);
            var listed = (node.Branches ?? []).SelectMany(b => b.When ?? []).ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<string>? values = null;
            var edited = false;
            if (WorkflowFlowNodes.CaseNodeSubjectOf(text) is { } subjectNode && tree.ById.TryGetValue(subjectNode, out var classifier)
                && classifier.With?.GetValueOrDefault("values") is JsonArray given)
            {
                // 30's answers give the branches, but 31's classification types value as a plain string (its with is
                // dropped, A10-030), so the checker refuses the subject (S04-044): a hand edit (A10-032).
                values = [.. given.Select(v => v is JsonValue s && s.TryGetValue<string>(out var value) ? value : null).OfType<string>()];
                m.HandEdit(file, "case", MigrationSentences.CaseOnClassifier(subjectNode));
                edited = true;
            }
            else if (type?.Format is { } format && FormatTypes.Shape(format, scope.Formats) == FormatShape.Enum)
            {
                values = ExpressionChecker.ValuesOf(format, scope.Formats);
            }

            if (values is not { Count: > 0 })
            {
                m.HandEdit(file, "case", MigrationSentences.CaseNotEnumerated(reference));
                cases[id] = new CaseShape(JsonValue.Create(reference), [], true);
                continue;
            }

            var nullable = type is null || type.Nullable || FormatTypes.IsNullable(type.Format, scope.Formats);
            if (nullable && node.Else is null)
            {
                m.HandEdit(file, "else", MigrationSentences.CaseSubjectNullable(reference));
                edited = true;
            }

            var missing = node.Else is null ? values.Where(v => !listed.Contains(v)).Distinct(StringComparer.Ordinal).ToList() : [];
            cases[id] = new CaseShape(JsonValue.Create(reference), missing, edited);
        }

        return cases;
    }

    /// <summary>
    /// Why a <c>case</c>'s converted subject <paramref name="result"/> is a hand edit, or null when it is a single read: the
    /// converter's notices when it refused, or the checker's S04-017 when the logic is no single read (part 04 § 5.2: a
    /// subject is a reference string).
    /// </summary>
    internal static string? CaseSubjectRefusal(string id, string file, ConversionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Logic is null)
            return string.Join(" ", result.Notices);
        return result.Logic is JsonObject { Count: 1 } read && read["var"] is JsonValue path && path.TryGetValue<string>(out _)
            ? null
            : ResolveSentences.IsAnExpression("Case", id, "case", file).Sentence;
    }

    /// <summary>The 31 folder of a node's file: <c>nodes</c>, then its chain of flow ids.</summary>
    private static string FolderOf(NodeTree tree, string id) =>
        string.Join('/', ["nodes", .. tree.Ancestors(id).Reverse()]);

    /// <summary>A flow node's header file: <c>nodes/…/&lt;id&gt;/index.json</c>.</summary>
    private static string FlowFile(NodeTree tree, string id) => $"{FolderOf(tree, id)}/{id}/{WorkflowDataResolver.IndexFileName}";

    /// <summary>
    /// A flow node's <c>index.json</c> (part 04 § 1 and its kind's section): § 1's members in <see cref="SourceShapes"/>'
    /// order, <c>producer</c> <c>"computed"</c>, the description as its lines, the format and the flags as placeholders
    /// (<see cref="Derived"/> derives and rewrites them; <c>reproducible</c> is <c>true</c> only when every body node's is), then the
    /// kind's members; each condition converted (<see cref="Condition"/>). Its children are written inside its folder.
    /// </summary>
    private static void WriteFlow(Migrated m, NodeTree tree, string id, Dictionary<string, CaseShape> cases)
    {
        var node = tree.ById[id];
        var file = FlowFile(tree, id);
        var kind = node switch
        {
            { For: not null } => "for",
            { While: not null } => "while",
            { If: not null } => "if",
            _ => "case",
        };

        var header = new JsonObject
        {
            ["kind"] = kind,
            ["label"] = node.Label,
            ["description"] = Lines(node.Description),
            ["format"] = m.NodeReads[id].Format.DeepClone(),
            ["reproducible"] = Reproducible(m, tree, id, 0),
            ["producer"] = "computed",
            ["modelDerived"] = false,
            ["runDependent"] = false,
            ["profileCarrying"] = false,
        };

        var key = m.Keys[id];
        string? Ret(string? ret) => RetId(ret) is { } child ? (m.Keys.TryGetValue(child, out var full) ? full : $"{key}.{child}") : ret;
        JsonObject Body(WorkflowFlowBodySpec body) => new()
        {
            ["nodes"] = Ids(body.Nodes),
            ["ret"] = Ret(body.Ret),
        };

        switch (kind)
        {
            case "for":
                header["in"] = CollectionOf(node) is { } collection ? InOf(collection) : node.For;
                header["parallel"] = node.Until is null;
                header["maxParallel"] = null;
                header["until"] = node.Until is null ? null : Condition(m, file, "until", node.Until, m.Scope(id), ConversionKind.Condition);
                header["max"] = node.Max;
                header["itemLabel"] = node.LabelField ?? InheritedLabel(m, node);
                header["nodes"] = Ids(node.Nodes);
                header["ret"] = Ret(node.Ret);
                break;
            case "while":
                header["while"] = Condition(m, file, "while", node.While!, m.Scope(id), ConversionKind.Condition);
                header["max"] = node.Max;
                header["until"] = node.Until is null ? null : Condition(m, file, "until", node.Until, m.Scope(id), ConversionKind.Condition);
                header["checkFirst"] = node.CheckFirst ?? false;
                header["nodes"] = Ids(node.Nodes);
                header["ret"] = Ret(node.Ret);
                break;
            case "if":
                header["if"] = Condition(m, file, "if", node.If!, m.Scope(id), ConversionKind.Condition);
                header["then"] = Body(node.Then ?? new WorkflowFlowBodySpec());
                header["else"] = node.Else is null ? null : Body(node.Else);
                break;
            default:
                var shape = cases[id];
                var branches = new JsonArray();
                foreach (var branch in node.Branches ?? [])
                {
                    branches.Add(new JsonObject
                    {
                        ["when"] = Ids(branch.When),
                        ["nodes"] = Ids(branch.Nodes),
                        ["ret"] = Ret(branch.Ret),
                    });
                }

                // Part 10 § 1.2: the values 30 skipped get an explicitly empty branch, so every case is exhaustive.
                if (shape.Missing.Count > 0)
                    branches.Add(new JsonObject { ["when"] = Ids(shape.Missing), ["nodes"] = new JsonArray(), ["ret"] = null });

                header["case"] = shape.Subject.DeepClone();
                header["branches"] = branches;
                header["else"] = node.Else is null ? null : Body(node.Else);
                break;
        }

        m.Put(file, header);
        foreach (var child in NodeTree.Children(node))
            WriteNode(m, tree, child, cases);
    }

    /// <summary>A 30 list of ids as a JSON array (<c>[]</c> for none).</summary>
    private static JsonArray Ids(IEnumerable<string>? ids) => new([.. (ids ?? []).Select(i => (JsonNode?)JsonValue.Create(i))]);

    /// <summary>
    /// Part 04 § 2.7 (#1447): a loop with no <c>labelField</c> of its own names its items as 30 did. Over an array input,
    /// by the input's <c>labelField</c>; over a snippet folder, by <c>title</c>, the field of the folder's format that
    /// carries 30's item label (part 01 § 5.3, <c>test/general</c>'s example); otherwise by position (<c>null</c>).
    /// </summary>
    private static string? InheritedLabel(Migrated m, WorkflowNodeSpec loop) => CollectionOf(loop)?.Split('.') switch
    {
        ["input", var id] => m.Manifest30.Inputs?.FirstOrDefault(i => i.Id == id)?.LabelField,
        ["snippet", _] => "title",
        _ => null,
    };

    /// <summary>
    /// The placeholder <c>reproducible</c> (<see cref="Derived"/> derives it): a leaf's kind's value (part 02 § 1.1:
    /// <c>agent</c> <c>false</c>, <c>rendered</c> and <c>computed</c> <c>true</c>, whatever 30 claimed, since the 31 type
    /// rules refuse any other and the derivation would not run; a 30 claim that differs is listed by <see cref="Derived"/>);
    /// a flow node's, <c>true</c> only when every node of its body is.
    /// </summary>
    private static bool Reproducible(Migrated m, NodeTree tree, string id, int depth)
    {
        if (depth > SourceBounds.JsonDepth || !tree.ById.TryGetValue(id, out var node))
            return false;
        if (!WorkflowImpliedPaths.IsFlow(node))
            return LeafKind(m, node) is "rendered" or "computed";
        return NodeTree.Children(node).All(child => Reproducible(m, tree, child, depth + 1));
    }
}
