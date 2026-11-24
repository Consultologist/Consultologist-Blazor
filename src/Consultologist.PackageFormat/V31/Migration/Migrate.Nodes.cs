using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the node rows; part 02 §§ 1, 11; part 05 § 1.2): the nodes pass. Every
/// 30 node is written under <c>nodes/</c> at its nested path (a body node in its flow's folder, <see cref="WriteFlow"/>),
/// its kind following the member it has: a flow member makes a flow node; a <c>let</c> a <c>computed</c> node; a format
/// of the <c>rendered</c> producer a <c>rendered</c> node; an attested or custom one an <c>agent</c> node. A
/// <c>terms_subset</c> node (the <c>computed</c> producer) gets no file: it is read at <c>check.&lt;id&gt;</c> and Task 11
/// writes the check. Every condition is converted (<see cref="Condition"/>), a single snippet's <c>if</c> among them. The
/// flags are placeholders, which <see cref="Derived"/> rewrites; an agent's <c>parts</c> is Task 9's and its macros Task 10's.
/// </summary>
internal static partial class Migrate
{
    /// <summary>
    /// The nodes pass: the tree and the nodes a deliverable composes (<see cref="ComposedNodes"/>, R9), then every
    /// node's key, read and place (<see cref="PlaceNodes"/>), the cases' branches (<see cref="CaseBranches"/>, after
    /// which what is always valued is known), then each top-level node in 30's order
    /// (listed in <c>nodes/index.json</c>), each flow's children inside it; then each single snippet's <c>if</c>.
    /// </summary>
    public static void Nodes(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var tree = new NodeTree(m.Manifest30.Nodes ?? []);
        var (composed, direct) = ComposedNodes(m, tree);
        tree.Composed.UnionWith(composed);
        tree.ComposedDirect.UnionWith(direct);
        var cases = new Dictionary<string, CaseShape>(StringComparer.Ordinal);
        PlaceNodes(m, tree, cases);
        foreach (var (id, shape) in CaseBranches(m, tree))
            cases[id] = shape;
        foreach (var id in tree.ById.Keys)
            m.NodeReads[id] = m.NodeReads[id] with { AlwaysValued = AlwaysValued(tree, id, cases, 0) };

        foreach (var id in tree.Top)
        {
            if (IsCheck(m, tree.ById[id]))
                continue;
            WriteNode(m, tree, id, cases);
            m.Items("nodes").Add(id);
        }

        SnippetConditions(m);
    }

    /// <summary>Writes node <paramref name="id"/>: a flow node's folder (<see cref="WriteFlow"/>) or a leaf's file; nothing for a check.</summary>
    private static void WriteNode(Migrated m, NodeTree tree, string id, Dictionary<string, CaseShape> cases)
    {
        if (!tree.ById.TryGetValue(id, out var node) || IsCheck(m, node))
            return;
        if (WorkflowImpliedPaths.IsFlow(node))
        {
            WriteFlow(m, tree, id, cases);
            return;
        }

        var file = $"{FolderOf(tree, id)}/{id}.md";
        var kind = LeafKind(m, node);
        var header = new JsonObject
        {
            ["kind"] = kind,
            ["producer"] = kind == "agent" ? "generated" : "computed",
            ["label"] = node.Label,
            ["description"] = null,
            ["format"] = m.NodeReads[id].Format.DeepClone(),
            ["reproducible"] = Reproducible(m, tree, id, 0),
            ["modelDerived"] = false,
            ["runDependent"] = false,
            ["profileCarrying"] = false,
            ["if"] = null,
        };

        switch (kind)
        {
            case "agent":
                header["onInvalid"] = OnInvalid(m, node);
                header["failIfEmpty"] = node.FailIfEmpty;
                header["parts"] = null;
                header["ordered"] = false;
                break;
            case "computed":
                header["expression"] = Condition(m, file, "expression", node.Let!, m.Scope(id), ConversionKind.Value, m.NodeReads[id].Format);
                break;
        }

        // D16: a member 31 has no counterpart for is dropped with a notice: a node's with (a classifier's values); a
        // computed or rendered node's onInvalid and failIfEmpty (only an agent node has them at 31).
        if (node.With is { Count: > 0 })
            m.Notice(MigrationSentences.MemberDropped(id, "with"));
        if (kind != "agent" && node.OnInvalid is not null)
            m.Notice(MigrationSentences.MemberDropped(id, "onInvalid"));
        if (kind != "agent" && node.FailIfEmpty is not null)
            m.Notice(MigrationSentences.MemberDropped(id, "failIfEmpty"));

        m.Put(file, header, kind == "computed" ? "" : PromptOf(m, node));
    }

    /// <summary>A 30 node's prompt text (its <c>prompt</c>, or the file its id implies), as 30 holds it; <c>""</c> when it has none. Task 9 rewrites it.</summary>
    private static string PromptOf(Migrated m, WorkflowNodeSpec node)
    {
        var path = node.Prompt ?? WorkflowImpliedPaths.PromptOf(node.Id);
        return m.Resolved30.Files.GetValueOrDefault(path) ?? m.Files30.GetValueOrDefault(path) ?? "";
    }

    /// <summary>
    /// Part 05 § 1.2: <c>fail</c> and <c>retry</c> are kept; none is <c>fail</c>, 30's default as the engine applies it
    /// (<c>ConsultNodeDispatch</c>); <c>flag</c>, which kept a reply that did not fit, has no 31 form: <c>fail</c>, with
    /// A10-013 (D16).
    /// </summary>
    private static string OnInvalid(Migrated m, WorkflowNodeSpec node)
    {
        if (node.OnInvalid == WorkflowOnInvalid.Flag)
        {
            m.Notice(MigrationSentences.FlagBecomesFail(node.Id));
            return WorkflowOnInvalid.Fail;
        }

        return node.OnInvalid == WorkflowOnInvalid.Retry ? WorkflowOnInvalid.Retry : WorkflowOnInvalid.Fail;
    }

    /// <summary>Whether 30 node <paramref name="node"/> is a check: a format of the <c>computed</c> producer (<c>terms_subset</c>), which becomes a file in <c>checks/</c> (Task 11).</summary>
    private static bool IsCheck(Migrated m, WorkflowNodeSpec node) =>
        !WorkflowImpliedPaths.IsFlow(node) && node.Let is null && node.Format is { } format && ProducerOf(m, format) == WorkflowFormatProducers.Computed;

    /// <summary>A 30 format's producer: a package format's is <c>custom</c>; a catalogue format's its file's; null when neither knows it.</summary>
    private static string? ProducerOf(Migrated m, string format)
    {
        if (m.Manifest30.Formats?.Any(f => f.Id == format) == true)
            return WorkflowFormatProducers.Custom;
        return m.Options.Formats30.TryGet(format, out var entry) ? entry!.Spec.Producer : null;
    }

    /// <summary>A leaf's 31 kind (part 02 § 11): a <c>let</c> is <c>computed</c>; a format of the <c>rendered</c> producer <c>rendered</c>; any other (attested, custom) <c>agent</c>.</summary>
    private static string LeafKind(Migrated m, WorkflowNodeSpec node) =>
        node.Let is not null ? "computed"
        : node.Format is { } format && ProducerOf(m, format) == WorkflowFormatProducers.Rendered ? "rendered"
        : "agent";

    /// <summary>
    /// A leaf's 31 format (part 02 § 11, part 10 § 1.2): a <c>let</c>'s from its <c>type</c> (<c>text</c> is
    /// <c>plaintext</c>; <c>number</c>, <c>boolean</c> and a format id are kept); a <c>rendered</c> node's
    /// <c>plaintext</c>, the text 30 rendered; an agent's the plain format of the same shape (<see cref="AgentFormat"/>),
    /// prose a deliverable composes being <c>markdown_block</c> (R9: <paramref name="composed"/>, D20).
    /// </summary>
    private static JsonNode LeafFormat(Migrated m, WorkflowNodeSpec node, bool composed)
    {
        if (node.Let is not null)
            return JsonValue.Create(node.Type == WorkflowLetAt30.Text ? "plaintext" : node.Type ?? "plaintext");
        if (LeafKind(m, node) == "rendered")
            return JsonValue.Create("plaintext");
        return JsonValue.Create(AgentFormat(node.Format, composed));
    }

    /// <summary>
    /// Part 02 § 11's format row: an agent node's 30 format as 31 names it, the plain format of the same shape (<c>prose</c>
    /// and <c>agent_plaintext</c> are <c>plaintext</c>, <c>agent_markdown_block</c> and <c>agent_markdown_inline</c> their
    /// markdown formats); any other (<c>classification</c>, <c>concept_list</c>, a package format) keeps its name. R9
    /// (#1507, 2026-11-22T16:26Z, item 2; D20, accepted 2026-11-22T21:38Z; part 10 § 1.2): <c>prose</c> (and no format,
    /// which is prose) that a deliverable composes (<paramref name="composed"/>) is <c>markdown_block</c>, since 30's
    /// composer kept its Markdown and a <c>markdown_block</c> deliverable escapes a plain-text read; <c>agent_plaintext</c>,
    /// declared plain text by its author, stays <c>plaintext</c>.
    /// </summary>
    internal static string AgentFormat(string? format, bool composed = false) => format switch
    {
        "prose" or null => composed ? "markdown_block" : "plaintext",
        "agent_plaintext" => "plaintext",
        "agent_markdown_block" => "markdown_block",
        "agent_markdown_inline" => "markdown_inline",
        var kept => kept,
    };

    /// <summary>
    /// A 30 condition or <c>let</c> <paramref name="text"/> in <paramref name="file"/>'s <paramref name="member"/>,
    /// converted over <paramref name="scope"/> (part 10 § 2): its canonical JSON Logic, each <c>!x</c> on text that became
    /// <c>blank(x)</c> listed (A10-008), each <c>trim(x) == ""</c> written <c>blank(x)</c> that answers otherwise for a null
    /// <c>x</c> listed (A10-050), and each <c>size</c> of a list that can be null said (A10-027, the Task 4 ruling:
    /// 31's <c>size(null)</c> is null where 30 counted 0). One the converter refuses stays 30's text, a JSON string the
    /// checker refuses (D3), with one hand edit whose reason is the converter's notice.
    /// </summary>
    internal static JsonNode Condition(
        Migrated m, string file, string member, string text, ConversionScope scope, ConversionKind kind, JsonNode? declared = null)
    {
        var result = WorkflowConditionConverter.FromScriban(text, scope, kind, declared);
        if (result.Logic is null)
        {
            m.HandEdit(file, member, string.Join(" ", result.Notices));
            return JsonValue.Create(text);
        }

        foreach (var blank in result.Blanks)
            m.Blank(MigrationSentences.BecameBlank(file, member, blank));
        foreach (var trim in result.TrimBlanks)
            m.Blank(MigrationSentences.TrimBecameBlank(file, member, trim));
        foreach (var list in ConditionTyping.NullableSizes(result.Logic, scope))
            m.Notice(MigrationSentences.SizeOfNullable(file, member, list));
        return result.Logic.DeepClone();
    }

    /// <summary>
    /// Part 05 § 6.1: a single snippet's <c>if</c>, which the layout pass wrote as 30's Scriban text (D3's form), converted
    /// at the top level like every other condition (<see cref="Condition"/>).
    /// </summary>
    private static void SnippetConditions(Migrated m)
    {
        foreach (var (id, path) in m.Manifest30.Snippets ?? [])
        {
            if (path.EndsWith('/') || !m.Sources.TryGetValue($"snippets/{id}.md", out var snippet)
                || snippet.Json["if"] is not JsonValue written || !written.TryGetValue<string>(out var text))
            {
                continue;
            }

            snippet.Json["if"] = Condition(m, $"snippets/{id}.md", "if", text, m.Scope((string?)null), ConversionKind.Condition);
        }
    }
}
