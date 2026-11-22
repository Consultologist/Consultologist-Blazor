using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the template rows, § 1.4; part 05 §§ 2, 3, 6, 7; R6): the templates
/// pass. Every 30 body is rewritten into the 31 dialect (<see cref="TemplateRewriter"/>): each agent node's prompt (its
/// lifted <c>if</c>s written as its <c>parts</c>) and each <c>rendered</c> node's body, in 30's node order; each snippet's
/// body and each folder item's; each macro's body, kept for the macros pass (<see cref="Migrated.MacroBodies"/>), a macro it
/// places a hand edit (A10-041: a 31 macro body places none). A macro
/// whose placements (in prompts and snippet bodies, <see cref="Migrated.MacroPlacements"/>) carry different conditions, one
/// of them a hoisted body <c>if</c>, is a hand edit on its <c>if</c> (A10-036): a macro has one <c>if</c>.
/// </summary>
internal static partial class Migrate
{
    /// <summary>The templates pass, after the nodes pass (it reads the node files, their keys and their loops' <c>itemLabel</c>).</summary>
    public static void Templates(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var tree = new NodeTree(m.Manifest30.Nodes ?? []);

        foreach (var id in tree.ById.Keys)
        {
            var file = $"{FolderOf(tree, id)}/{id}.md";
            if (!m.Sources.TryGetValue(file, out var source) || source.Body is not { } body)
                continue;
            var site = (string?)source.Json["kind"] switch
            {
                "agent" => TemplateSite.Prompt,
                "rendered" => TemplateSite.Rendered,
                _ => (TemplateSite?)null,
            };
            if (site is not { } kind)
                continue;

            var rewritten = TemplateRewriter.Rewrite(body, new TemplateSite30(kind, file, id, ItemLabelOf(m, tree, id)), m);
            source.Body = rewritten.Text;
            if (kind == TemplateSite.Prompt)
                source.Json["parts"] = rewritten.Parts.Count > 0 ? rewritten.Parts : null;
            m.MacroPlacements.AddRange(rewritten.Macros);
        }

        foreach (var (id, path) in m.Manifest30.Snippets ?? [])
        {
            IEnumerable<string> files = path.EndsWith('/')
                ? m.Items($"snippets/{id}").Select(item => $"snippets/{id}/{(string)item!}.md")
                : [$"snippets/{id}.md"];
            foreach (var file in files)
            {
                if (m.Sources.TryGetValue(file, out var snippet) && snippet.Body is { } body)
                {
                    var rewritten = TemplateRewriter.Rewrite(body, new TemplateSite30(TemplateSite.SnippetBody, file, null), m);
                    snippet.Body = rewritten.Text;
                    m.MacroPlacements.AddRange(rewritten.Macros);
                }
            }
        }

        foreach (var file in MacroFiles(m))
        {
            if (!m.Resolved30.Files.TryGetValue(file, out var body))
                continue;

            // A macro that becomes a snippet (D4) is written at snippets/<id>.md, so its hand edits name that file.
            var snippet = m.Manifest30.Macros?.FirstOrDefault(x => x.File == file) is { } single && Renames.BecomesSnippet(m, single.Id)
                ? $"snippets/{single.Id}.md"
                : null;
            var rewritten = TemplateRewriter.Rewrite(body, new TemplateSite30(TemplateSite.MacroBody, snippet ?? file, null), m);
            m.MacroBodies[file] = rewritten.Text;

            // Part 05 § 8.3, § 8.7: a macro's body places no macro (S05-042), so one 30 placed there is a hand edit, never
            // counted among the placed macro's placements. (30 refuses it too: a macro's text is placed after a reply.)
            foreach (var placed in snippet is null ? rewritten.Macros.Select(p => p.Macro).Distinct(StringComparer.Ordinal) : [])
                m.HandEdit(file, "body", MigrationSentences.BodyPlacesMacro(placed));
        }

        PlacementConditions(m);
    }

    /// <summary>Every 30 macro file (also the 31 file of a macro that stays one): a single macro's, then each folder's items in its index's order.</summary>
    private static IEnumerable<string> MacroFiles(Migrated m)
    {
        foreach (var macro in m.Manifest30.Macros ?? [])
        {
            if (!macro.File.EndsWith('/'))
            {
                yield return macro.File;
                continue;
            }

            if (!m.Files30.TryGetValue(macro.File + WorkflowDataResolver.IndexFileName, out var index))
                continue;
            foreach (var (_, item) in WorkflowTextHeaders.ReadIndex(index, macro.Id, "Macro", m.Manifest30.SpecVersion, [])?.Items ?? [])
                yield return macro.File + item;
        }
    }

    /// <summary>
    /// Part 04 § 2.7: the <c>itemLabel</c> of the innermost <c>for</c> node <paramref name="id"/> runs in (its 31 header's),
    /// which 30's <c>item.label</c> reads at 31; null when there is none or it names no field.
    /// </summary>
    private static string? ItemLabelOf(Migrated m, NodeTree tree, string id)
    {
        var loop = tree.Ancestors(id).FirstOrDefault(a => tree.ById[a].For is not null);
        return loop is not null && m.Sources.TryGetValue(FlowFile(tree, loop), out var header) && header.Json["itemLabel"] is JsonValue label
            && label.TryGetValue<string>(out var field) ? field : null;
    }

    /// <summary>
    /// R6 (part 10 § 1.2's hoist row): each macro's placements (<see cref="Migrated.MacroPlacements"/>, in prompts and
    /// snippet bodies). When one carries a hoisted condition and all carry the same one, it is the macro's
    /// (<see cref="Migrated.PlacementRules"/>); when they differ, within one body or across several (a hoisted condition on
    /// one, another or none on another), the macro's one <c>if</c> cannot hold them: one hand edit on it (A10-036). With
    /// <paramref name="only"/>, just those macros are judged (the macros pass, once a node instance added a placement).
    /// </summary>
    private static void PlacementConditions(Migrated m, IReadOnlySet<string>? only = null)
    {
        foreach (var placements in m.MacroPlacements.GroupBy(p => p.Macro, StringComparer.Ordinal))
        {
            if (only is not null && !only.Contains(placements.Key))
                continue;
            m.PlacementRules.Remove(placements.Key);
            var rules = placements.Select(p => p.Rule).ToList();
            if (rules.All(r => r is null))
                continue;
            if (rules.Select(r => r is null ? "" : CanonicalJson.Write(r.Logic)).Distinct(StringComparer.Ordinal).Count() == 1)
            {
                m.PlacementRules[placements.Key] = rules[0]!;
                continue;
            }

            var macro = placements.Key;
            var file = m.Manifest30.Macros?.FirstOrDefault(x => x.Id == macro && !x.File.EndsWith('/'))?.File ?? $"macros/{macro}.md";
            m.HandEdit(file, "if", MigrationSentences.PlacedUnderConditions(macro, placements.Select(p => p.File).Distinct(StringComparer.Ordinal).ToList()));
        }
    }
}
