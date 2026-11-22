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
            }, Composition(m, deliverable, file));
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
    private static string Composition(Migrated m, WorkflowDeliverableSpec deliverable, string file)
    {
        var macros = deliverable.Macros ?? [];
        var pieces = new List<string>();
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placement in macros.Where(x => x?.At == WorkflowDeliverableMacroSpec.Prepend))
            pieces.Add(DeliverableMacro(m, placement, file, said));
        foreach (var source in deliverable.Sources ?? [])
            pieces.Add($"{{{{ {SourceRead(m, source)}.content }}}}");
        foreach (var placement in macros.Where(x => x is not null && x.At != WorkflowDeliverableMacroSpec.Prepend))
            pieces.Add(DeliverableMacro(m, placement, file, said));
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
    /// D3, D4 (part 05 § 10; part 06 § 6.3: a deliverable reads no <c>macro.*</c>): one deliverable macro at its place. With
    /// no condition anywhere it is a snippet read: the snippet the macros pass made of it, or, when the macro stays one
    /// because a prompt or a node also places it, a snippet written here from its body (<see cref="SnippetCopy"/>). With a
    /// condition (the placement's <c>if</c>, the macro's own, or optional) it is a hand edit (A10-002, said once per macro
    /// on the deliverable), written <c>{{ macro.&lt;id&gt;.fixed_text }}</c>, which 31 refuses (S05-011) until it is made.
    /// </summary>
    private static string DeliverableMacro(Migrated m, WorkflowDeliverableMacroSpec placement, string file, HashSet<string> said)
    {
        var id = placement.Id;
        var macro = m.Manifest30.Macros?.FirstOrDefault(x => x.Id == id && !x.File.EndsWith('/'));
        var header = macro is null ? null : m.Resolved30.Headers.GetValueOrDefault(macro.File);
        var conditional = macro is null || placement.If is not null || placement.When is not null || header?.If is not null
            || macro.Optional == true || header?.Optional == true;
        if (conditional)
        {
            if (said.Add(id))
                m.HandEdit(file, "body", MigrationSentences.DeliverableMacroWithCondition(id));
            return $"{{{{ macro.{id.Replace('/', '.')}.{TemplateRewriter.FixedText} }}}}";
        }

        if (!Renames.BecomesSnippet(m, id))
            SnippetCopy(m, macro!, header);
        return $"{{{{ snippet.{id}.content }}}}";
    }

    /// <summary>
    /// D4: the single snippet <c>snippets/&lt;id&gt;.md</c> of a deliverable macro that stays a macro (a prompt or a node
    /// places it too): its label, <c>description: null</c>, <c>if: null</c>, <c>format: "markdown_block"</c>, as the macros
    /// pass writes a fixed-text macro, and the macro's body; written once, listed after the snippets already there.
    /// </summary>
    private static void SnippetCopy(Migrated m, WorkflowMacroSpec macro, WorkflowTextHeader? header)
    {
        var path = $"snippets/{macro.Id}.md";
        if (m.Sources.ContainsKey(path))
            return;
        m.Put(path, new JsonObject
        {
            ["label"] = macro.Label ?? header?.Label ?? macro.Id,
            ["description"] = null,
            ["if"] = null,
            ["format"] = "markdown_block",
        }, MacroBody(m, macro.File));
        m.Items("snippets").Add(macro.Id);
    }
}
