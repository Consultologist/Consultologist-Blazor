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
    /// which what is always valued is known), the leaves only deliverables with a condition need and their gates
    /// (<see cref="DeliverableGates"/>, #1508 item 1, after which a gated leaf is not always valued), then each top-level node in 30's order
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
            m.NodeReads[id] = m.NodeReads[id] with { AlwaysValued = AlwaysValued(tree, id, cases, 0, NoGates) };

        // #1508 (item 1; F-D4): R15 judges a source by 30's always-valued; the checker and the rewriter see a gated leaf
        // as nullable.
        foreach (var id in tree.ById.Keys.Where(id => m.NodeReads[id].AlwaysValued))
            m.AlwaysValued30.Add(id);
        DeliverableGates(m, tree);
        var gated = m.DeliverableGates.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var id in tree.ById.Keys)
            m.NodeReads[id] = m.NodeReads[id] with { AlwaysValued = AlwaysValued(tree, id, cases, 0, gated) };

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
            ["if"] = m.DeliverableGates.GetValueOrDefault(id)?.DeepClone(),
        };
        if (m.GatingDeliverables.TryGetValue(id, out var gating))
            m.Notice(MigrationSentences.RunsOnlyWhen(file, gating));

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
            m.Notice(kind == "agent" && ClassifierValues(node) is not null
                ? MigrationSentences.ValuesInAnswerFormat(id)
                : MigrationSentences.MemberDropped(id, "with"));
        if (kind != "agent" && node.OnInvalid is not null)
            m.Notice(MigrationSentences.MemberDropped(id, "onInvalid"));
        if (kind != "agent" && node.FailIfEmpty is not null)
            m.Notice(MigrationSentences.MemberDropped(id, "failIfEmpty"));

        m.Put(file, header, kind == "computed" ? "" : PromptOf(m, node));
    }

    /// <summary>
    /// A 30 classifier's answers: its <c>with</c> values in written order, when <paramref name="node"/> is a leaf of the
    /// <c>classification</c> format whose values are all 30 ids (the enum of its answer format; 30's answer line is not written, #1508 item 7); else null.
    /// </summary>
    internal static IReadOnlyList<string>? ClassifierValues(WorkflowNodeSpec node)
    {
        if (WorkflowImpliedPaths.IsFlow(node) || node.Let is not null || node.Format != "classification"
            || node.With?.GetValueOrDefault("values") is not JsonArray values)
        {
            return null;
        }

        var names = values.Select(SourceLayout.Text).ToList();
        if (names.Count == 0 || names.Any(n => n is null || !WorkflowDeclaredIds.IsValid(n)))
            return null;
        return [.. names.Select(n => n!)];
    }

    /// <summary>
    /// #1508 (comment 6, item 1; R13; part 10 § 1.2's model-chosen row): whether macro <paramref name="key"/> is one 30's
    /// model chose: it migrates to an offered macro with a non-empty instruction (30's mechanics sentence begins "If so,
    /// write …", so every such macro was optional at 30, where 31 requires an offered macro's marker exactly once). Not one
    /// the engine placed (its instruction is written <c>[]</c>, D3), one whose placements disagree (its <c>if</c> is a
    /// string, A10-036), one with no 30 instruction, or one whose instruction is a template (<c>{{ "" }}</c>, another
    /// macro's text: A10-042, the operator writes its lines): those keep area 10's hand edits.
    /// </summary>
    private static bool ModelChosen(string key, string? instruction30, HashSet<string> byTheEngine, HashSet<string> disagreeing) =>
        !byTheEngine.Contains(key) && !disagreeing.Contains(key) && !string.IsNullOrWhiteSpace(instruction30)
        && !instruction30.Contains("{{", StringComparison.Ordinal);

    /// <summary>
    /// #1508 (comment 6, item 1; R13): for macro <paramref name="key"/> (labelled <paramref name="label"/>, offered when
    /// <paramref name="offer"/> holds), one <c>judgement</c> node per agent that places it (in the order of its placements:
    /// a prompt's own, or the agents whose prompts place the snippet holding it; a snippet folder's item places none at 31,
    /// A10-051), written as the placing node's sibling immediately before it (inside its loop or branch when it is in one):
    /// <c>&lt;macro id&gt;_applies</c>, fitted to 64 characters (A10-034) and numbered when a node already has the id
    /// (S10-006), its <c>if</c> <paramref name="offer"/>, its prompt the reads the placing node's prompt makes outside any
    /// <c>if</c> or <c>for</c> (each on a line of its own, in order, once) followed by an empty question line, which is a
    /// hand edit (A10-077). Returns each node's read where the macro is placed (<c>node.&lt;id&gt;</c>, its sibling's read: the
    /// macro's <c>if</c> is evaluated in the placing scope, part 05 § 8.2), in order.
    /// </summary>
    private static List<string> ChoiceClassifiers(Migrated m, NodeTree tree, string key, string label, JsonNode? offer)
    {
        var placers = new List<string>();
        foreach (var placement in m.MacroPlacements.Where(p => p.Macro == key))
        {
            if (placement.Owner is not null)
                placers.Add(placement.Owner);
            else if (SnippetOf(placement.File) is { } snippet && placement.File.Count(c => c == '/') == 1)
                placers.AddRange(PlacerIdsOf(m, snippet));
        }

        var keys = new List<string>();
        foreach (var placer in placers.Distinct(StringComparer.Ordinal).ToList())
        {
            var placerFile = $"{FolderOf(tree, placer)}/{placer}.md";
            if (!tree.ById.ContainsKey(placer) || !m.Sources.TryGetValue(placerFile, out var placing) || (string?)placing.Json["kind"] != "agent")
                continue;

            var id = ClassifierId(m, tree, key[(key.LastIndexOf('/') + 1)..]);
            var file = $"{FolderOf(tree, placer)}/{id}.md";
            m.Put(file, new JsonObject
            {
                ["kind"] = "agent",
                ["producer"] = "generated",
                ["label"] = $"Whether {label} applies",
                ["description"] = null,
                ["format"] = "judgement",
                ["reproducible"] = false,
                ["modelDerived"] = false,
                ["runDependent"] = false,
                ["profileCarrying"] = false,
                ["if"] = offer?.DeepClone(),
                ["onInvalid"] = WorkflowOnInvalid.Fail,
                ["failIfEmpty"] = null,
                ["parts"] = null,
                ["ordered"] = false,
            }, QuestionPrompt(m, placing.Body ?? ""));
            SiblingBefore(m, tree, placer, id);
            m.HandEdit(file, "body", MigrationSentences.WriteTheQuestion(id, key));
            keys.Add("node." + id);
        }

        return keys;
    }

    /// <summary>The agents whose 30 prompts place snippet (or snippet folder) <paramref name="snippet"/>, by id (<see cref="PlacersOf"/>' nodes).</summary>
    private static IEnumerable<string> PlacerIdsOf(Migrated m, string snippet) =>
        m.Walk30.Templates
            .Where(t => t.Kind == TemplateKind.Prompt && (t.References.Snippets.Contains(snippet) || t.References.SnippetFolders.Contains(snippet)))
            .Where(t => m.Keys.TryGetValue(t.Id, out var rooted) && rooted.StartsWith("node.", StringComparison.Ordinal))
            .Select(t => t.Id);

    /// <summary>
    /// A classifier's id for macro <paramref name="macro"/> (its bare id): <c>&lt;macro&gt;_applies</c> fitted (A10-034 when cut),
    /// or with the first free number, <c>_applies_2</c>, … (S10-006) when a node, a check or a classifier already written has it.
    /// </summary>
    private static string ClassifierId(Migrated m, NodeTree tree, string macro)
    {
        var taken = new HashSet<string>(tree.ById.Keys, StringComparer.Ordinal);
        foreach (var path in m.Sources.Keys.Where(p => p.StartsWith("nodes/", StringComparison.Ordinal) || p.StartsWith("checks/", StringComparison.Ordinal)))
        {
            var segments = path.Split('/');
            taken.Add(segments[^1] == WorkflowDataResolver.IndexFileName ? segments[^2] : segments[^1].Replace(".md", "", StringComparison.Ordinal));
        }

        var wanted = $"{macro}_applies";
        var suffixed = Renames.Fit(macro, "_applies");
        var id = suffixed;
        for (var n = 2; taken.Contains(id); n++)
            id = Renames.Fit(macro, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"_applies_{n}"));
        if (wanted.Length > SourceBounds.IdLength)
            m.Notice(MigrationSentences.RenamedTooLong(Renames.Node, wanted, suffixed));
        if (id != suffixed)
            m.Notice(MigrationSentences.RenameTaken(suffixed, id, suffixed));
        return id;
    }

    /// <summary>
    /// A classifier's prompt: each read <paramref name="placing"/> (the placing node's 31 prompt) writes outside any <c>if</c>
    /// or <c>for</c> (an input, a node, a check, the item), as written, once each in order, on lines of their own, then an
    /// empty line and the empty question line the operator writes (A10-077). Never a part, a macro or a snippet: a snippet's
    /// text may place a macro, which a <c>judgement</c> node may not (part 05 § 8.3), and any text it holds would count as
    /// the question, so the prompt would publish asking nothing (A11-013 counts placed snippets' text, fix round 1).
    /// </summary>
    private static string QuestionPrompt(Migrated m, string placing)
    {
        var syntax = V31.Templates.TemplateSyntax.Parse(placing);
        var reads = new List<string>();
        foreach (var output in syntax.Outputs)
        {
            if (output.Depth > 0 || output.Path is not { } path || path.Split('.')[0] is not ("input" or "node" or "check" or "item"))
                continue;
            var tag = placing[output.Tag.Start..output.Tag.End];
            var inner = tag[2..^2].Trim().Trim('~', '-').Trim();
            var read = $"{{{{ {inner} }}}}";
            if (!reads.Contains(read))
                reads.Add(read);
        }

        return reads.Count == 0 ? "\n" : string.Join("\n\n", reads) + "\n\n";
    }

    /// <summary>
    /// Lists node <paramref name="id"/> immediately before <paramref name="sibling"/> where <paramref name="sibling"/> is listed:
    /// <c>nodes/index.json</c>'s items for a top-level node; else its flow's <c>index.json</c>, in the body that holds it (a
    /// loop's <c>nodes</c>, an <c>if</c>'s <c>then</c> or <c>else</c>, a <c>case</c> branch's or its <c>else</c>'s).
    /// </summary>
    private static void SiblingBefore(Migrated m, NodeTree tree, string sibling, string id)
    {
        var lists = new List<JsonArray>();
        if (!tree.Owner.TryGetValue(sibling, out var flow))
        {
            lists.Add(m.Items("nodes"));
        }
        else if (m.Sources.TryGetValue(FlowFile(tree, flow), out var index))
        {
            var header = index.Json;
            foreach (var body in new[] { header, header["then"], header["else"] }.Concat((header["branches"] as JsonArray ?? []).Select(b => b)))
            {
                if (body?["nodes"] is JsonArray nodes)
                    lists.Add(nodes);
            }
        }

        foreach (var list in lists)
        {
            var at = list.Select(n => (string?)n).ToList().IndexOf(sibling);
            if (at < 0)
                continue;
            list.Insert(at, id);
            return;
        }
    }

    /// <summary>A 30 node's prompt text (its <c>prompt</c>, or the file its id implies), as 30 holds it; <c>""</c> when it has none. Task 9 rewrites it.</summary>
    private static string PromptOf(Migrated m, WorkflowNodeSpec node)
    {
        var path = node.Prompt ?? WorkflowImpliedPaths.PromptOf(node.Id);
        return m.Resolved30.Files.GetValueOrDefault(path) ?? m.Files30.GetValueOrDefault(path) ?? "";
    }

    /// <summary>
    /// Part 05 § 1.2: <c>fail</c> and <c>retry</c> are kept; none is <c>fail</c>, 30's default as the engine applies it
    /// (<c>ConsultNodeDispatch</c>); <c>flag</c>, which kept a reply that did not fit, has no 31 form: <c>retry</c> (one
    /// more attempt with the validation error, then the node fails), with A10-013 (#1508, comment 5, item 3, where D16
    /// wrote <c>fail</c>).
    /// </summary>
    private static string OnInvalid(Migrated m, WorkflowNodeSpec node)
    {
        if (node.OnInvalid == WorkflowOnInvalid.Flag)
        {
            m.Notice(MigrationSentences.FlagBecomesRetry(node.Id));
            return WorkflowOnInvalid.Retry;
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
    /// prose a deliverable composes being <c>markdown_block</c> (R9: <paramref name="composed"/>, D20), and a classifier's
    /// the package format of its answers (R12, <see cref="Migrated.AnswerFormats"/>).
    /// </summary>
    private static JsonNode LeafFormat(Migrated m, WorkflowNodeSpec node, bool composed)
    {
        if (node.Let is not null)
            return JsonValue.Create(node.Type == WorkflowLetAt30.Text ? "plaintext" : node.Type ?? "plaintext");
        if (LeafKind(m, node) == "rendered")
            return JsonValue.Create("plaintext");
        if (m.AnswerFormats.TryGetValue(node.Id, out var answer))
            return JsonValue.Create(answer);
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
    /// <c>x</c> listed (A10-050), each text compared with a literal written <c>in</c> said (A10-074), each text input
    /// read whole compared with <c>""</c> written with its null test said (A10-075), and each <c>size</c> of
    /// a list that can be null said (A10-027, the Task 4 ruling: 31's <c>size(null)</c> is null where 30 counted 0; a
    /// compared one is written with its null test, #1508, comment 5, item 2). One the converter refuses stays 30's text, a JSON string the
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
        foreach (var literal in result.TextLiterals)
            m.Notice(MigrationSentences.TextLiteralBecameIn(file, member, literal));
        foreach (var empty in result.EmptyComparisons)
            m.Notice(MigrationSentences.EmptyBecameNullTested(file, member, empty));
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
