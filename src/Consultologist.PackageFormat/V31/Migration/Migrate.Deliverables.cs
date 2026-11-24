using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the deliverable rows, §§ 1.4, 1.10; part 06 §§ 6.1, 6.3 to 6.6, 7, 8,
/// 10; D3, D4, D7, D8): the deliverables pass. Each 30 deliverable becomes <c>deliverables/&lt;id&gt;.md</c>, listed in
/// <c>deliverables/index.json</c> in 30's order (the root folder: <c>delivery: []</c>, <c>bundle: null</c>, as the layout
/// wrote it). Its header is part 06 § 6.1's: its label, <c>kind: rendered</c>, <c>description: null</c>,
/// <c>format: markdown_block</c> (D8), its <c>if</c> converted (<see cref="Condition"/>), its <c>check</c> the gate's
/// <c>check.&lt;id&gt;</c> or <c>null</c>, and <c>whenEmpty</c> from <see cref="Migration31Options.WhenEmpty"/>
/// (§ 1.10, D7; <c>"release"</c> when unsaid). Its body is the composition template D8 names: the prepended macros, the
/// sources, the appended macros and the signature, in that order, one blank line between each. A source is read as its
/// <c>.content</c> (a loop's is 30's blocks, part 05 § 5.3); a macro with no condition is a snippet read
/// (<c>{{ snippet.&lt;id&gt;.content }}</c>, D4: the snippet the macros pass wrote, or one written here from the macro's
/// body when a prompt also places the macro); a macro with a condition (its own <c>if</c>, the placement's, or an
/// optional one) is a hand edit (D3, A10-002), written <c>{{ macro.&lt;id&gt;.fixed_text }}</c>, which 31 refuses; 30's
/// <c>signature: true</c> is <c>{{ profile.signature }}</c>.
/// </summary>
internal static partial class Migrate
{
    /// <summary>The release when a deliverable has no <see cref="Migration31Options.WhenEmpty"/> entry (part 10 § 1.10, D7).</summary>
    public const string DefaultWhenEmpty = "release";

    /// <summary>The deliverables pass, after the macros and checks passes (it reads the snippets and the checks they wrote).</summary>
    public static void Deliverables(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var copies = new Dictionary<string, string>(StringComparer.Ordinal);   // a macro kept as one → the snippet a deliverable reads it as
        foreach (var deliverable in m.Manifest30.EffectiveDeliverables)
        {
            var id = deliverable.Id;
            var file = $"deliverables/{id}.md";
            var condition = deliverable.If ?? deliverable.When;
            m.Put(file, new JsonObject
            {
                ["label"] = deliverable.Label,
                ["kind"] = "rendered",
                ["description"] = null,
                ["format"] = "markdown_block",
                ["if"] = condition is null ? null : Condition(m, file, "if", condition, m.Scope((string?)null), ConversionKind.Condition),
                ["check"] = Gate(m, deliverable.Check),
                ["whenEmpty"] = m.Options.WhenEmpty?.GetValueOrDefault(id) ?? DefaultWhenEmpty,
            }, Composition(m, deliverable, file, copies));
            m.Items("deliverables").Add(id);
            m.Keys[DeliverableKey(id)] = DeliverableKey(id);
        }
    }

    /// <summary>
    /// A deliverable's entry in <see cref="Migrated.Keys"/> (D1): <c>deliverable.&lt;id&gt;</c> by its 30 key, so that a
    /// deliverable never takes the entry of a node with the same id (<c>composer-sources</c>' <c>plan</c>).
    /// </summary>
    internal static string DeliverableKey(string id) => "deliverable." + id;

    /// <summary>The 30 gate (<c>node.&lt;id&gt;</c> of a <c>terms_subset</c> node) as the check it became: <c>check.&lt;id&gt;</c>.</summary>
    private static string? Gate(Migrated m, string? check30)
    {
        if (check30 is null)
            return null;
        var id = check30.StartsWith("node.", StringComparison.Ordinal) ? check30["node.".Length..] : check30;
        return m.NodeReads.TryGetValue(id, out var read) && read.Path.StartsWith("check.", StringComparison.Ordinal) ? read.Path : "check." + id;
    }

    /// <summary>
    /// D8: the template, its pieces one blank line apart: each prepended macro (placement order), each source's
    /// <c>.content</c> (<c>sources</c> order), each appended macro, then <c>{{ profile.signature }}</c> when 30 signed it.
    /// </summary>
    private static string Composition(Migrated m, WorkflowDeliverableSpec deliverable, string file, Dictionary<string, string> copies)
    {
        var macros = deliverable.Macros ?? [];
        var pieces = new List<string>();
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placement in macros.Where(x => x?.At == WorkflowDeliverableMacroSpec.Prepend))
            pieces.Add(DeliverableMacro(m, placement, file, said, copies));
        foreach (var source in deliverable.Sources ?? [])
            pieces.Add($"{{{{ {SourceRead(m, source)}.content }}}}");
        foreach (var placement in macros.Where(x => x is not null && x.At != WorkflowDeliverableMacroSpec.Prepend))
            pieces.Add(DeliverableMacro(m, placement, file, said, copies));
        if (deliverable.Signature == true)
            pieces.Add("{{ profile.signature }}");
        return string.Join("\n\n", pieces);
    }

    /// <summary>A 30 source (<c>node.&lt;id&gt;</c>) as 31 reads it (<see cref="Migrated.NodeReads"/>): <c>node.&lt;id&gt;</c> by its bare id.</summary>
    private static string SourceRead(Migrated m, string source)
    {
        var id = source.StartsWith("node.", StringComparison.Ordinal) ? source["node.".Length..] : source;
        return m.NodeReads.TryGetValue(id, out var read) ? read.Path : "node." + id;
    }

    /// <summary>
    /// R9 (#1507, 2026-11-22T16:26Z, item 2) and D20 (accepted 2026-11-22T21:38Z): the leaves a deliverable composes. Each
    /// 30 deliverable's <c>sources</c> (read as <see cref="SourceRead"/> reads them) is followed through flows: a source that
    /// is a flow gives every leaf its <c>ret</c> chain reaches (a loop's or a while's <c>ret</c>, an <c>if</c>'s
    /// <c>then</c> and <c>else</c>, a <c>case</c>'s branches and <c>else</c>); a leaf is itself. Which of them changes
    /// format is <see cref="AgentFormat"/>'s to say. Computed before the nodes pass works out any format.
    /// </summary>
    private static HashSet<string> ComposedLeaves(Migrated m, NodeTree tree)
    {
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        foreach (var deliverable in m.Manifest30.EffectiveDeliverables)
        {
            foreach (var source in deliverable.Sources ?? [])
            {
                if (source is not null)
                    pending.Push(source.StartsWith("node.", StringComparison.Ordinal) ? source["node.".Length..] : source);
            }
        }

        while (pending.TryPop(out var id))
        {
            if (!seen.Add(id) || !tree.ById.TryGetValue(id, out var node))
                continue;
            if (!WorkflowImpliedPaths.IsFlow(node))
            {
                leaves.Add(id);
                continue;
            }

            IEnumerable<string?> rets = node switch
            {
                { For: not null } or { While: not null } => [node.Ret],
                { If: not null } => [node.Then?.Ret, node.Else?.Ret],
                _ => [.. (node.Branches ?? []).Select(b => b.Ret), node.Else?.Ret],
            };
            foreach (var ret in rets)
            {
                if (RetId(ret) is { } child)
                    pending.Push(child);
            }
        }

        return leaves;
    }

    /// <summary>
    /// D3, D4 (part 05 § 10; part 06 § 6.3: a deliverable reads no <c>macro.*</c>): one deliverable macro at its place. With
    /// no condition anywhere it is a snippet read: the snippet the macros pass made of it, or, when the macro stays one
    /// because a prompt or a node also places it, a snippet written here from its body (<see cref="SnippetCopy"/>). With a
    /// condition (the placement's <c>if</c>, the macro's own, or optional) it is a hand edit (A10-002, said once per macro
    /// on the deliverable), written <c>{{ macro.&lt;id&gt;.fixed_text }}</c>, which 31 refuses (S05-047: a deliverable
    /// places no macro) until it is made. One naming no single macro of the package (30 refuses it first) is A10-047,
    /// written the same way.
    /// </summary>
    private static string DeliverableMacro(Migrated m, WorkflowDeliverableMacroSpec placement, string file, HashSet<string> said, Dictionary<string, string> copies)
    {
        var id = placement.Id;
        var written = $"{{{{ macro.{id.Replace('/', '.')}.{TemplateRewriter.FixedText} }}}}";
        var macro = m.Manifest30.Macros?.FirstOrDefault(x => x.Id == id && !x.File.EndsWith('/'));
        if (macro is null)
        {
            if (said.Add(id))
                m.HandEdit(file, "body", MigrationSentences.NotASingleMacro(id));
            return written;
        }

        var header = m.Resolved30.Headers.GetValueOrDefault(macro.File);
        if (placement.If is not null || placement.When is not null || header?.If is not null || macro.Optional == true || header?.Optional == true)
        {
            if (said.Add(id))
                m.HandEdit(file, "body", MigrationSentences.DeliverableMacroWithCondition(id));
            return written;
        }

        var snippet = Renames.BecomesSnippet(m, id) ? id : SnippetCopy(m, macro, header, copies);
        return $"{{{{ snippet.{snippet}.content }}}}";
    }

    /// <summary>
    /// D4: the single snippet of a deliverable macro that stays a macro (a prompt or a node places it too), written once:
    /// its label, <c>description: null</c>, <c>if: null</c>, <c>format: "markdown_block"</c>, as the macros pass writes a
    /// fixed-text macro, and the macro's body, listed after the snippets already there. Its id is the macro's, or, when a
    /// snippet already has it, the first free <c>&lt;id&gt;_snippet</c>, <c>_snippet_2</c>, … (A10-048). Each hand edit
    /// already said on the macro's body is said again on the copy, which holds the same text (one notice per file).
    /// </summary>
    private static string SnippetCopy(Migrated m, WorkflowMacroSpec macro, WorkflowTextHeader? header, Dictionary<string, string> copies)
    {
        if (copies.TryGetValue(macro.Id, out var copied))
            return copied;

        bool Taken(string id) => m.Sources.ContainsKey($"snippets/{id}.md") || m.Sources.ContainsKey($"snippets/{id}/{WorkflowDataResolver.IndexFileName}");
        var chosen = macro.Id;
        if (Taken(chosen))
        {
            chosen = Renames.Fit(macro.Id, "_snippet");
            for (var n = 2; Taken(chosen); n++)
                chosen = Renames.Fit(macro.Id, string.Create(CultureInfo.InvariantCulture, $"_snippet_{n}"));
            m.Notice(MigrationSentences.SnippetCopyRenamed(macro.Id, chosen));
        }

        var path = $"snippets/{chosen}.md";
        m.Put(path, new JsonObject
        {
            ["label"] = macro.Label ?? header?.Label ?? macro.Id,
            ["description"] = null,
            ["if"] = null,
            ["format"] = "markdown_block",
        }, MacroBody(m, macro.File));
        m.Items("snippets").Add(chosen);

        var said = $"Hand edit needed in {macro.File}, body: ";
        foreach (var edit in m.HandEditNotices.Where(e => e.StartsWith(said, StringComparison.Ordinal)).ToList())
            m.HandEdit(path, "body", edit[said.Length..^1]);
        copies[macro.Id] = chosen;
        return chosen;
    }
}
