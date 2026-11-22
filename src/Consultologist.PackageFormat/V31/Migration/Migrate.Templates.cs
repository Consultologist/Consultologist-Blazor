using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the template rows, § 1.4; part 05 §§ 2, 3, 6, 7; R6): the templates
/// pass. Every 30 body is rewritten into the 31 dialect (<see cref="TemplateRewriter"/>): each agent node's prompt (its
/// lifted <c>if</c>s written as its <c>parts</c>) and each <c>rendered</c> node's body, in 30's node order; each snippet's
/// body and each folder item's; each macro's body, kept for the macros pass (<see cref="Migrated.MacroBodies"/>). A macro
/// the prompts place under different conditions, one of them a hoisted body <c>if</c>, is a hand edit on its <c>if</c>
/// (A10-036): a macro has one <c>if</c>.
/// </summary>
internal static partial class Migrate
{
    /// <summary>The templates pass, after the nodes pass (it reads the node files, their keys and their loops' <c>itemLabel</c>).</summary>
    public static void Templates(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var tree = new NodeTree(m.Manifest30.Nodes ?? []);
        var placed = new Dictionary<string, List<string>>(StringComparer.Ordinal);

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
            foreach (var macro in rewritten.Macros)
            {
                if (!placed.TryGetValue(macro, out var owners))
                    placed[macro] = owners = [];
                owners.Add(id);
            }
        }

        foreach (var (id, path) in m.Manifest30.Snippets ?? [])
        {
            IEnumerable<string> files = path.EndsWith('/')
                ? m.Items($"snippets/{id}").Select(item => $"snippets/{id}/{(string)item!}.md")
                : [$"snippets/{id}.md"];
            foreach (var file in files)
            {
                if (m.Sources.TryGetValue(file, out var snippet) && snippet.Body is { } body)
                    snippet.Body = TemplateRewriter.Rewrite(body, new TemplateSite30(TemplateSite.SnippetBody, file, null), m).Text;
            }
        }

        foreach (var file in MacroFiles(m))
        {
            if (m.Resolved30.Files.TryGetValue(file, out var body))
                m.MacroBodies[file] = TemplateRewriter.Rewrite(body, new TemplateSite30(TemplateSite.MacroBody, file, null), m).Text;
        }

        PlacementConditions(m, placed);
    }

    /// <summary>Every 30 macro file: a single macro's, then each folder's items in its index's order.</summary>
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
    /// R6 (part 10 § 1.2's hoist row): a macro whose placements in the prompts of <paramref name="placed"/> (macro → the
    /// nodes placing it, in 30's order) come under different conditions, a hoisted one among them, cannot take them into
    /// its one <c>if</c>: one hand edit on the macro's <c>if</c> (A10-036).
    /// </summary>
    private static void PlacementConditions(Migrated m, Dictionary<string, List<string>> placed)
    {
        foreach (var (macro, owners) in placed)
        {
            var rules = owners.Select(o => m.PlacementRules.GetValueOrDefault((o, macro))?.Logic).ToList();
            if (rules.All(r => r is null))
                continue;
            if (rules.Select(r => r is null ? "" : CanonicalJson.Write(r)).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                var file = m.Manifest30.Macros?.FirstOrDefault(x => x.Id == macro && !x.File.EndsWith('/'))?.File ?? $"macros/{macro}.md";
                m.HandEdit(file, "if", MigrationSentences.PlacedUnderConditions(macro, owners.Distinct(StringComparer.Ordinal).ToList()));
            }
        }
    }
}
