using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the deliverable rows, §§ 1.4, 1.10; part 06 §§ 6.1, 6.3 to 6.6, 7, 8,
/// 10; D3, D4, D7, D8): the deliverables pass. Each 30 deliverable becomes <c>deliverables/&lt;id&gt;.md</c>, listed in
/// <c>deliverables/index.json</c> in 30's order (the root folder: <c>delivery: ["pdf"]</c> (R8), <c>bundle: null</c>, as the layout
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
            var rule = condition is null ? null : Condition(m, file, "if", condition, m.Scope((string?)null), ConversionKind.Condition);
            m.Put(file, new JsonObject
            {
                ["label"] = deliverable.Label,
                ["kind"] = "rendered",
                ["description"] = null,
                ["format"] = "markdown_block",
                ["if"] = WhenASourceRan(m, deliverable, file, rule),
                ["check"] = Gate(m, deliverable.Check),
                ["whenEmpty"] = m.Options.WhenEmpty?.GetValueOrDefault(id) ?? DefaultWhenEmpty,
            }, Composition(m, deliverable, file, copies));
            m.Items("deliverables").Add(id);
            m.Keys[DeliverableKey(id)] = DeliverableKey(id);
        }
    }

    /// <summary>
    /// #1508 (ruled by the owner, Q3 of 2026-11-26; R15): the deliverable's <c>if</c>. 30 skipped a deliverable none of
    /// whose sources ran; at 31 it would be composed empty and released (A11-010 warns of it in a new package). So when
    /// every source may not run (<see cref="ConversionRead.AlwaysValued"/> false for each: an <c>if</c> with no
    /// <c>else</c>, a <c>case</c> with an empty branch), the <c>if</c> says that one ran: for each source the term
    /// <see cref="RanTerm"/> gives, the first the deliverable's <c>if</c> can read, by asking its typing
    /// (<see cref="ConditionTyping.ReadsAtDeliverableIf"/> over the top-level scope):
    /// <c>{ "===": [ { "var": "&lt;source&gt;.state" }, "completed" ] }</c>, else R15's fallback
    /// <c>{ "!==": [ { "var": "&lt;source&gt;.value" }, null ] }</c> (#1508, 2026-11-26, item 8), joined by <c>or</c>, after
    /// 30's own <paramref name="rule"/> by <c>and</c> (A10-079 says it). A source the migration does not read (no
    /// <see cref="Migrated.NodeReads"/> entry, or a read where neither term types at a deliverable's <c>if</c>, as a
    /// <c>check.*</c> read would not) leaves whether the deliverable is empty unread, so then, unless another source always
    /// runs, the <c>if</c> keeps <paramref name="rule"/> and is a hand edit (A10-086, item 8) naming the unread sources and
    /// the others' term, so nothing is released empty without a notice. A deliverable with no sources, or one with a source
    /// that always runs (or is a <c>while</c>), keeps <paramref name="rule"/> and is said nothing: it is never empty. A rule
    /// the converter refused (30's text with its
    /// hand edit) is kept too, since nothing is joined to a condition not yet written; where every source may not run it
    /// gets a second hand edit on the same <c>if</c> naming the term to join to it by <c>and</c>, verbatim (A10-084, the
    /// final review of Part F), so the condition the operator writes still skips the deliverable as 30 did. A <c>while</c> source counts as one that runs: 30
    /// composed a while that ran no pass as empty text and released the deliverable, as 31 does, so an <c>if</c> would skip
    /// what 30 released. Beside another source the while's blank-line separator stays, which no <c>if</c> can drop: the
    /// expected difference <c>while-no-pass-separator</c> (the controller's ruling on #1508; part 10 §§ 1.2, 3.2).
    /// </summary>
    private static JsonNode? WhenASourceRan(Migrated m, WorkflowDeliverableSpec deliverable, string file, JsonNode? rule)
    {
        var sources = (deliverable.Sources ?? []).Where(s => s is not null).ToList();
        if (sources.Count == 0)
            return rule;
        var reads = new List<string>();
        var ran = new List<JsonNode>();
        var unread = new List<string>();
        var scope = m.Scope((string?)null);
        foreach (var source in sources)
        {
            var id = source.StartsWith("node.", StringComparison.Ordinal) ? source["node.".Length..] : source;
            var known = m.NodeReads.TryGetValue(id, out var read);
            if ((known && read!.AlwaysValued) || m.Manifest30.Nodes?.FirstOrDefault(n => n.Id == id)?.While is not null)
                return rule;
            if (known && RanTerm(read!.Path, t => ConditionTyping.ReadsAtDeliverableIf(t, scope)) is { } one)
            {
                reads.Add(read.Path);
                ran.Add(one);
            }
            else
            {
                unread.Add(known ? read!.Path : "node." + id);
            }
        }

        JsonNode? term = ran.Count switch { 0 => null, 1 => ran[0], _ => new JsonObject { ["or"] = new JsonArray([.. ran]) } };
        if (unread.Count > 0 || term is null)
        {
            m.HandEdit(file, "if", MigrationSentences.SourceRunNotRead(unread, term?.ToJsonString()));
            return rule;
        }

        if (rule is JsonValue)
        {
            m.HandEdit(file, "if", MigrationSentences.JoinASourceRan(term.ToJsonString()));
            return rule;
        }

        m.Notice(MigrationSentences.SkippedUnlessASourceRan(file, reads));
        if (rule is null)
            return term;
        if (rule is JsonObject { Count: 1 } both && both["and"] is JsonArray terms)
        {
            terms.Add(term);
            return both;
        }

        return new JsonObject { ["and"] = new JsonArray(rule, term) };
    }

    /// <summary>
    /// #1508 (2026-11-26, item 8; R15's fallback, F-D12): the term that says the source at <paramref name="path"/> ran, the
    /// first <paramref name="readable"/> accepts: <c>{ "===": [ { "var": "&lt;path&gt;.state" }, "completed" ] }</c>, else
    /// <c>{ "!==": [ { "var": "&lt;path&gt;.value" }, null ] }</c>, else null (the migration cannot read whether it ran).
    /// </summary>
    internal static JsonNode? RanTerm(string path, Func<JsonNode, bool> readable)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(readable);
        JsonNode state = new JsonObject { ["==="] = new JsonArray(new JsonObject { ["var"] = path + ".state" }, "completed") };
        if (readable(state))
            return state;
        JsonNode value = new JsonObject { ["!=="] = new JsonArray(new JsonObject { ["var"] = path + ".value" }, null) };
        return readable(value) ? value : null;
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
    /// R9 (#1507, 2026-11-22T16:26Z, item 2) and D20 (accepted 2026-11-22T21:38Z): the nodes a deliverable composes. Each
    /// 30 deliverable's <c>sources</c> (read as <see cref="SourceRead"/> reads them) is followed through flows: a source that
    /// is a flow gives itself and every node its <c>ret</c> chain reaches (a loop's or a while's <c>ret</c>, an <c>if</c>'s
    /// <c>then</c> and <c>else</c>, a <c>case</c>'s branches and <c>else</c>). Which leaves change format is
    /// <see cref="AgentFormat"/>'s to say. <c>Direct</c> holds those reached through no <c>for</c>: a deliverable reads a
    /// <c>for</c> as its <c>.content</c>, the standard rendering typed <c>markdown_block</c> (D23), so what lies under a
    /// <c>for</c> is never escaped by the read; a composed <c>if</c> or <c>case</c> in <c>Direct</c> whose branches mix
    /// Markdown and plain text is <see cref="WriteFlow"/>'s hand edit (A10-053, Task 24's fix round 1). Computed before the
    /// nodes pass works out any format.
    /// </summary>
    private static (HashSet<string> All, HashSet<string> Direct) ComposedNodes(Migrated m, NodeTree tree)
    {
        var composed = new HashSet<string>(StringComparer.Ordinal);
        var direct = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<(string, bool)>();
        var pending = new Stack<(string Id, bool UnderFor)>();
        foreach (var deliverable in m.Manifest30.EffectiveDeliverables)
        {
            foreach (var source in deliverable.Sources ?? [])
            {
                if (source is not null)
                    pending.Push((source.StartsWith("node.", StringComparison.Ordinal) ? source["node.".Length..] : source, false));
            }
        }

        while (pending.TryPop(out var next))
        {
            var (id, underFor) = next;
            if (!seen.Add((id, underFor)) || !tree.ById.TryGetValue(id, out var node))
                continue;
            composed.Add(id);
            if (!underFor)
                direct.Add(id);
            if (!WorkflowImpliedPaths.IsFlow(node))
                continue;

            IEnumerable<string?> rets = node switch
            {
                { For: not null } or { While: not null } => [node.Ret],
                { If: not null } => [node.Then?.Ret, node.Else?.Ret],
                _ => [.. (node.Branches ?? []).Select(b => b.Ret), node.Else?.Ret],
            };
            var below = underFor || node.For is not null;
            foreach (var ret in rets)
            {
                if (RetId(ret) is { } child)
                    pending.Push((child, below));
            }
        }

        return (composed, direct);
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
        }, SnippetBody(m, macro, readAsContent: false));
        m.MacroBodyFiles.Add(path);
        m.Items("snippets").Add(chosen);

        var said = $"Hand edit needed in {macro.File}, body: ";
        foreach (var edit in m.HandEditNotices.Where(e => e.StartsWith(said, StringComparison.Ordinal)).ToList())
            m.HandEdit(path, "body", edit[said.Length..^1]);
        copies[macro.Id] = chosen;
        return chosen;
    }
}
