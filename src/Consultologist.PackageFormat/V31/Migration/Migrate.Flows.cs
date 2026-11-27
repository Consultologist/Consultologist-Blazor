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

        /// <summary>The nodes a deliverable composes, its sources and every node their <c>ret</c>s reach (R9, D20): filled by <see cref="ComposedNodes"/> before any format is worked out.</summary>
        public HashSet<string> Composed { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The part of <see cref="Composed"/> a deliverable reaches through no <c>for</c> (a <c>for</c> itself included):
        /// what its read may escape, as a loop's <c>.content</c> is <c>markdown_block</c> (D23; Task 24's fix round 1).
        /// </summary>
        public HashSet<string> ComposedDirect { get; } = new(StringComparer.Ordinal);

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
            m.NodeReads[id] = new ConversionRead(path, formats[id].DeepClone())
            {
                AlwaysValued = AlwaysValued(tree, id, cases, 0, NoGates),
                Classifier = m.AnswerFormats.ContainsKey(id),
            };
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

    /// <summary>
    /// Whether the <c>if</c> or <c>case</c> <paramref name="id"/>'s branches return both a <c>markdown_block</c> and a
    /// <c>plaintext</c> member (R9's mixed branches): each <c>ret</c>'s format (a union's members each), a <c>ret</c> whose
    /// chain reaches a <c>for</c> counted <c>markdown_block</c>, as its <c>.content</c> is (D23; final review A5).
    /// </summary>
    private static bool MixesMarkdownAndPlainText(Migrated m, NodeTree tree, string id)
    {
        if (m.NodeReads[id].Format is not JsonObject { } union || union["anyOf"] is not JsonArray)
            return false;
        var node = tree.ById[id];
        IEnumerable<string?> rets = node.If is not null
            ? [node.Then?.Ret, node.Else?.Ret]
            : [.. (node.Branches ?? []).Select(b => b.Ret), node.Else?.Ret];
        var set = FormatSet.ForPackage(m.Options.Registry, m.PackageFormats());
        var kinds = new List<string?>();
        foreach (var child in rets.Select(RetId).OfType<string>().Where(m.NodeReads.ContainsKey))
        {
            if (ReachesFor(tree, child, 0))
                kinds.Add("markdown_block");
            else if (m.NodeReads[child].Format is JsonObject { } inner && inner["anyOf"] is JsonArray members)
                kinds.AddRange(members.Select(member => FormatTypes.TextKind(member, set)));
            else
                kinds.Add(FormatTypes.TextKind(m.NodeReads[child].Format, set));
        }

        return kinds.Contains("markdown_block") && kinds.Contains("plaintext");
    }

    /// <summary>Whether node <paramref name="id"/> is a <c>for</c>, or a <c>while</c> whose <c>ret</c> chain reaches one.</summary>
    private static bool ReachesFor(NodeTree tree, string id, int depth)
    {
        if (depth > SourceBounds.JsonDepth || !tree.ById.TryGetValue(id, out var node))
            return false;
        return node.For is not null || (node.While is not null && RetId(node.Ret) is { } child && ReachesFor(tree, child, depth + 1));
    }

    /// <summary>The id a 30 <c>ret</c> (<c>node.&lt;id&gt;</c>) names, or null.</summary>
    private static string? RetId(string? ret) =>
        ret is not null && ret.StartsWith("node.", StringComparison.Ordinal) && ret.IndexOf('.', 5) < 0 && ret.Length > 5 ? ret[5..] : null;

    /// <summary>
    /// Part 04 § 7.6's "always valued", as area 05's <c>ScopeTree.AlwaysValued</c> has it: a leaf (none has an <c>if</c>
    /// at 30, whose node <c>when</c> is retired since 26) and a <c>for</c>, always; a <c>while</c> that checks after its
    /// first pass; an <c>if</c> with an <c>else</c>, a <c>case</c> with no empty branch, when every <c>ret</c> is. A
    /// <c>case</c> not yet in <paramref name="cases"/> is not. #1508 (item 1; F-D4): a leaf in <paramref name="gated"/> (it
    /// carries a deliverable's <c>if</c>, <see cref="Migrated.DeliverableGates"/>) is not.
    /// </summary>
    private static bool AlwaysValued(NodeTree tree, string id, Dictionary<string, CaseShape> cases, int depth, IReadOnlySet<string> gated)
    {
        if (depth > SourceBounds.JsonDepth || !tree.ById.TryGetValue(id, out var node))
            return false;
        bool Ret(string? ret) => RetId(ret) is { } child && AlwaysValued(tree, child, cases, depth + 1, gated);

        return node switch
        {
            { For: not null } => true,
            { While: not null } => node.CheckFirst != true,
            { If: not null } => node.Else is not null && Ret(node.Then?.Ret) && Ret(node.Else.Ret),
            { Case: not null } => cases.TryGetValue(id, out var shape) && shape.Missing.Count == 0
                && (node.Branches ?? []).All(b => Ret(b.Ret)) && (node.Else is null || Ret(node.Else.Ret)),
            _ => !gated.Contains(id),
        };
    }

    /// <summary>No leaf gated: what is always valued before the gates are known (<see cref="AlwaysValued"/>).</summary>
    private static readonly IReadOnlySet<string> NoGates = new HashSet<string>(StringComparer.Ordinal);

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
        var (element, folder, input) = ElementOf(m, tree, loop, formats, 0);
        return new SitePlace(element, folder, inWhile, input);
    }

    /// <summary>
    /// The element of the list <paramref name="loop"/> iterates (part 04 § 2.6's <c>item.value</c>): a snippet folder's
    /// item is a value of the folder's format (and a folder item); an array input's, a node list's or an outer item's
    /// list field's element is the list's <c>items</c>. Null when the list does not type. <c>Input</c> says whether the
    /// element is an input's (an array input's, or a list field of an outer loop's input element), whose text 30
    /// trimmed at its end (#1508, 2026-11-26, item 6).
    /// </summary>
    private static (JsonNode? Element, bool Folder, bool Input) ElementOf(Migrated m, NodeTree tree, string loop, FormatSet formats, int depth)
    {
        if (depth > SourceBounds.JsonDepth || CollectionOf(tree.ById[loop]) is not { } collection)
            return (null, false, false);
        var segments = collection.Split('.');
        switch (segments)
        {
            case ["snippet", var folder]:
                return (m.Sources.GetValueOrDefault($"snippets/{folder}/{WorkflowDataResolver.IndexFileName}")?.Json["format"]?.DeepClone(), true, false);
            case ["input", var input]:
                return (ExpressionChecker.ItemsOf(m.Manifest["inputs"]?[input]?["format"], formats)?.DeepClone(), false, true);
            case ["node", var node, "value", ..] when m.NodeReads.TryGetValue(node, out var read):
                return (ListItems(read.Format, segments[2..]), false, false);
            case ["item", "value", ..]:
                var outer = tree.Ancestors(loop).FirstOrDefault(a => tree.ById[a].For is not null);
                if (outer is null)
                    return (null, false, false);
                var (element, _, fromInput) = ElementOf(m, tree, outer, formats, depth + 1);
                return (ListItems(element, segments[1..]), false, fromInput);
            default:
                return (null, false, false);
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
    /// values 31 cannot list (A10-028), and one that can be <c>null</c> where the case has no <c>else</c> (A10-029). A
    /// classifier's are its answers, the enum of the package format the layout pass wrote for it (R12; A10-032 retired).
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
            // A 30 classifier's answers are its format's enum (R12, Migrate.Layout), so it is listed as any enum is.
            if (type?.Format is { } format && FormatTypes.Shape(format, scope.Formats) == FormatShape.Enum)
                values = ExpressionChecker.ValuesOf(format, scope.Formats);

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

    /// <summary>
    /// The nodes of the while <paramref name="id"/>'s own body (at any depth) that its converted <paramref name="rule"/>
    /// reads as this pass's (<c>node.&lt;id&gt;</c>, <c>pass.node.&lt;id&gt;</c>), each once, in the rule's order, as the
    /// checker's A06-002 finds them. A rule that did not convert (a JSON string, already a hand edit) reads none.
    /// </summary>
    private static List<string> OwnBodyReads(NodeTree tree, string id, JsonNode? rule)
    {
        var own = new List<string>();
        if (rule is not JsonObject)
            return own;
        foreach (var path in JsonLogicShape.Paths(rule))
        {
            var read = path.Split('.') switch
            {
                ["node", var x, ..] => x,
                ["pass", "node", var x, ..] => x,
                _ => null,
            };
            if (read is not null && tree.ById.ContainsKey(read) && tree.Ancestors(read).Contains(id) && !own.Contains(read))
                own.Add(read);
        }

        return own;
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

        // R9, D20: a composed if or case whose branches return 30 prose (markdown_block) and plain text (a let of text, a
        // rendered node, agent_plaintext) would be the union of both, which a markdown_block deliverable escapes as a string,
        // 30's Markdown with it. No 31 format keeps both, so it is a hand edit (A10-053), written markdown_block, which the
        // checker refuses against the union its branches give (S02-015). Only where a deliverable reaches it through no for
        // (D23, Task 24's fix round 1): under a for the deliverable reads the loop's .content, markdown_block and never
        // escaped, so the union is written as derived and 30's bytes are kept.
        if (kind is "if" or "case" && tree.ComposedDirect.Contains(id) && MixesMarkdownAndPlainText(m, tree, id))
        {
            header["format"] = "markdown_block";
            m.HandEdit(file, "format", MigrationSentences.ComposedBranchesMix(kind, id));
        }

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
                // Final review A2 (A06-002, the owner's ruling 4 on #1507): a check made before the first pass that reads
                // the loop's own nodes has nothing to read. checkFirst stays as 30 had it, which the checker refuses.
                if (node.CheckFirst == true && OwnBodyReads(tree, id, header["while"]) is { Count: > 0 } own)
                    m.HandEdit(file, "checkFirst", MigrationSentences.CheckFirstReadsBody(own));
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

    /// <summary>
    /// #1510 (#1509 comment 7207709261, item 1; F-D1, F-D2): the stripping pass, after the deliverables (when every reader is
    /// written) and before <see cref="Derived"/>. 30 trimmed each item of a loop it composed (<c>ConsultFlowCompletion</c>);
    /// at 31 a <c>for</c>'s <c>.content</c> is the engine's rendering of its items as their <c>ret</c> gave them. So every
    /// <c>for</c> whose <c>.content</c> a migrated file reads (<see cref="ContentRead"/>) gets one new <c>rendered</c> node as
    /// the last of its body and its <c>ret</c>: <c>&lt;old ret id&gt;_stripped</c>, fitted (<see cref="FreeNodeId"/>), whose
    /// body is <c>{{ node.&lt;old ret id&gt;.content | string.strip }}</c>, whose <c>if</c> holds only when the old <c>ret</c>
    /// completed (an item's state is its <c>ret</c>'s), each read as the body sees its sibling (a reference resolves outward
    /// by bare id, part 01 § 9: the rooted key is refused there, S03-014), and whose format is the old <c>ret</c>'s text kind
    /// (<see cref="ContentKind"/>), so the engine escapes an item where it did. The reads of the loop's <c>.content</c> stay as
    /// written. A new node reads its old <c>ret</c>'s <c>.content</c>, so a loop returned by a loop is stripped too, as 30
    /// trimmed both: the pass runs until no loop is left. The loop's format and those of the flows that return it follow the
    /// new <c>ret</c> (<see cref="Reformat"/>). Each is said (A10-088) and kept in <see cref="Migrated.StripNodes"/>. A loop whose
    /// old <c>ret</c> has no one text kind (an <c>if</c> or a <c>case</c> of a Markdown and a plain branch) gets no node, since
    /// one format would escape a branch the old <c>ret</c> did not: it keeps its <c>ret</c>, a hand edit (A10-090, fix round 1).
    /// </summary>
    public static void StripLoopItems(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var tree = new NodeTree(m.Manifest30.Nodes ?? []);
        var set = FormatSet.ForPackage(m.Options.Registry, m.PackageFormats());
        var stripped = new HashSet<string>(StringComparer.Ordinal);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (id, node) in tree.ById)
            {
                if (node.For is null || stripped.Contains(id) || !m.Sources.TryGetValue(FlowFile(tree, id), out var loop)
                    || SourceLayout.Text(loop.Json["ret"]) is not { } ret || HeaderAt(m, ret) is not { } old || !ContentRead(m, tree, id))
                {
                    continue;
                }

                stripped.Add(id);
                if (ContentKind(m, ret, set, 0) is not { } kind)
                {
                    // Fix round 1: items of more than one text format, which no one node can strip as each was written.
                    m.HandEdit(FlowFile(tree, id), "ret", MigrationSentences.LoopItemsMixFormats(id));
                    continue;
                }

                changed = true;
                var key = m.Keys[id];
                var retId = ret[(ret.LastIndexOf('.') + 1)..];
                var newId = FreeNodeId(m, tree, retId, "_stripped");
                m.Put($"nodes/{key["node.".Length..].Replace('.', '/')}/{newId}.md", new JsonObject
                {
                    ["kind"] = "rendered",
                    ["producer"] = "computed",
                    ["label"] = old.Json["label"]?.DeepClone(),
                    ["description"] = null,
                    ["format"] = kind,
                    ["reproducible"] = true,
                    ["modelDerived"] = false,
                    ["runDependent"] = false,
                    ["profileCarrying"] = false,
                    ["if"] = new JsonObject { ["==="] = new JsonArray(new JsonObject { ["var"] = $"node.{retId}.state" }, "completed") },
                }, $"{{{{ node.{retId}.content | string.strip }}}}");
                loop.Json["nodes"]!.AsArray().Add(newId);
                loop.Json["ret"] = $"{key}.{newId}";
                m.StripNodes[$"{key}.{newId}"] = ret;
                m.Notice(MigrationSentences.StripsLoopItems(id, newId));
                Reformat(m, tree, id, FormatTypes.ListOf(JsonValue.Create(kind)), set);
            }
        }
    }

    /// <summary>
    /// Whether a migrated file reads <c>.content</c> of the <c>for</c> <paramref name="id"/>: by its rooted key, or by a shorter
    /// form of it that resolves outward (<c>node.&lt;id&gt;</c>, part 01 § 9), behind <c>pass.</c> or not; in a template, a
    /// header (a condition, an expression, a hand edit's text) or a plain JSON file. 30's ids are unique, so the bare form
    /// names this loop alone.
    /// </summary>
    private static bool ContentRead(Migrated m, NodeTree tree, string id)
    {
        string[] chain = [.. tree.Ancestors(id).Reverse(), id];
        var forms = Enumerable.Range(0, chain.Length).Select(i => "node." + string.Join('.', chain[i..]));
        var read = new System.Text.RegularExpressions.Regex(
            $"(?<![A-Za-z0-9_])(?:{string.Join('|', forms.Select(System.Text.RegularExpressions.Regex.Escape))})\\.content(?![A-Za-z0-9_])",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return m.Sources.Values.Any(f => read.IsMatch(f.Json.ToJsonString()) || (f.Body is { } body && read.IsMatch(body)));
    }

    /// <summary>The <c>ret</c>s a flow's migrated header names, in its bodies' order: a loop's own; an <c>if</c>'s <c>then</c> and <c>else</c>; a <c>case</c>'s branches and <c>else</c>.</summary>
    private static List<string> RetsOf(JsonObject header)
    {
        IEnumerable<JsonNode?> bodies = SourceLayout.Text(header["kind"]) switch
        {
            "if" => [header["then"], header["else"]],
            "case" => [.. header["branches"] as JsonArray ?? [], header["else"]],
            _ => [header],
        };
        return bodies.Select(b => SourceLayout.Text(b?["ret"])).OfType<string>().ToList();
    }

    /// <summary>The migrated header of the node at rooted key <paramref name="key"/> (<c>node.a.b</c>): its leaf's file or its flow's <c>index.json</c>.</summary>
    private static MigratedFile? HeaderAt(Migrated m, string key)
    {
        if (!key.StartsWith("node.", StringComparison.Ordinal))
            return null;
        var path = "nodes/" + key["node.".Length..].Replace('.', '/');
        return m.Sources.GetValueOrDefault(path + ".md") ?? m.Sources.GetValueOrDefault($"{path}/{WorkflowDataResolver.IndexFileName}");
    }

    /// <summary>
    /// F-D2: the text kind of <c>.content</c> of the node at <paramref name="key"/>, as the type checker types it
    /// (<c>ScopeTree.RenderedContent</c>) and the engine writes it (<c>PlanRunner.WritesMarkdown</c>): a <c>for</c>'s is
    /// <c>markdown_block</c>; a <c>while</c>'s its <c>ret</c>'s; an <c>if</c>'s or a <c>case</c>'s its <c>ret</c>s' when they are
    /// one kind; a leaf's its format's when that is <c>markdown_block</c> or <c>markdown_inline</c>, else <c>plaintext</c> (plain
    /// text, a value that is not text). Branches of more than one kind have none: <see langword="null"/> (fix round 1).
    /// </summary>
    private static string? ContentKind(Migrated m, string key, FormatSet set, int depth)
    {
        if (depth > SourceBounds.JsonDepth || HeaderAt(m, key)?.Json is not { } header)
            return "plaintext";
        switch (SourceLayout.Text(header["kind"]))
        {
            case "for":
                return "markdown_block";
            case "while" or "if" or "case":
                var kinds = RetsOf(header).Select(r => ContentKind(m, r, set, depth + 1)).Distinct(StringComparer.Ordinal).ToList();
                return kinds switch
                {
                    [] => "plaintext",
                    [var one] => one,
                    _ => null,
                };
            default:
                var text = FormatTypes.TextKind(header["format"], set);
                return text is "markdown_block" or "markdown_inline" ? text : "plaintext";
        }
    }

    /// <summary>
    /// F-D1: the flow <paramref name="id"/>, whose <c>ret</c> changed, takes <paramref name="format"/> (the 31 checker holds a
    /// flow to the format its <c>ret</c>s give, part 02 § 2.2), and so does each flow that returns it, outward: a <c>for</c>
    /// the list of it, a <c>while</c> it, an <c>if</c> or a <c>case</c> the union of its <c>ret</c>s' formats as now written.
    /// A format that is unchanged stops it, and so does a flow whose format is a hand edit (R9's mixed branches, A10-053).
    /// </summary>
    private static void Reformat(Migrated m, NodeTree tree, string id, JsonNode format, FormatSet set)
    {
        var (at, written) = (id, format);
        while (true)
        {
            var file = FlowFile(tree, at);
            if (!m.Sources.TryGetValue(file, out var header) || JsonNode.DeepEquals(header.Json["format"], written)
                || m.HandEditNotices.Any(n => n.StartsWith(MigrationSentences.HandEdit(file, "format", "")[..^1], StringComparison.Ordinal)))
            {
                return;
            }

            header.Json["format"] = written;
            if (!tree.Owner.TryGetValue(at, out var owner) || !m.Sources.TryGetValue(FlowFile(tree, owner), out var outer))
                return;
            var key = m.Keys[at];
            var json = outer.Json;
            var rets = RetsOf(json);
            if (!rets.Contains(key))
                return;
            written = SourceLayout.Text(json["kind"]) switch
            {
                "for" => FormatTypes.ListOf(written),
                "while" => written.DeepClone(),
                _ => FormatTypes.UnionOf([.. rets.Select(r => HeaderAt(m, r)?.Json["format"]).OfType<JsonNode>()], set) ?? JsonValue.Create("plaintext"),
            };
            at = owner;
        }
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
