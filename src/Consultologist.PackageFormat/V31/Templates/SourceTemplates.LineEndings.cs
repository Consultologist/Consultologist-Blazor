using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1510 (the owner's third fix-first group, item 4; G-D11, with G-D8's sites): a single line ending inside a paragraph is
/// not a break at 31, so publish warns (S05-065, W-3) once per line ending <see cref="MarkdownLineEndings.Find"/> names in a
/// Markdown body a reader sees. The sites are the migration's (<c>Migrate.HardBreaks</c>), taken from what the dialect read:
/// deliverables, <c>rendered</c> nodes, macros, snippets and renderers whose <c>format</c> is <c>markdown_block</c> or
/// <c>markdown_inline</c>, and every snippet folder's item (its body is <c>markdown_block</c>, part 05 § 6.2). #1543 (ruling
/// 1): a body a prompt reads or places is warned like any other, with no exception, and the migration writes the <c>\</c> in
/// each of these sites, so a migrated package with no hand edit is warned nowhere. A prompt and a part are never sites.
/// #1543 (ruling 2, D2): each tag's role is the checker's own typing of the site's body wherever it read it
/// (<see cref="TagRoles"/>), as the migration's is.
/// </summary>
public static partial class SourceTemplates
{
    private sealed partial class Context
    {
        /// <summary>
        /// The roles of the tags of every site as <paramref name="bodies"/> (the package's top bodies, with what they place)
        /// typed them, a file refused so far typing none: built once, for <see cref="LineEndings"/> and <see cref="BlockReads"/>.
        /// </summary>
        public SiteRoles Roles(IReadOnlyList<CheckedBody> bodies) =>
            TagRoles.Of(bodies, resolution.Tree, Refusals.Select(r => r.Path).ToHashSet(StringComparer.Ordinal));

        /// <summary>
        /// S05-065 for each line ending to break in each site of the package, in the order of the sites (deliverables,
        /// rendered nodes, macros, snippets, renderers), then of the line endings; the tags' roles from <paramref name="roles"/>.
        /// </summary>
        public void LineEndings(SiteRoles roles)
        {
            foreach (var (subject, path, text) in Sites())
            {
                foreach (var at in MarkdownLineEndings.Find(text, roles.Of(path, text)))
                    Warnings.Add(DialectSentences.SingleLineEnding(subject, LineOf(text, at), path));
            }
        }

        /// <summary>Each site's subject, its file and its body: the Markdown ones of the class's remarks.</summary>
        private IEnumerable<(string Subject, string Path, string Body)> Sites()
        {
            var tree = resolution.Tree;
            bool Markdown(JsonNode? format, ScopeFrame frame) =>
                FormatTypes.TextKind(format, tree.FormatsOf(frame)) is "markdown_block" or "markdown_inline";

            foreach (var deliverable in Deliverables(package.Deliverables).Where(d => Markdown(d.Header["format"], tree.PackageRoot)))
                yield return ($"Deliverable '{deliverable.Id}'", deliverable.Path, deliverable.Body);

            var leaves = ScopeTree.Walk(package.Nodes.Nodes).Concat(ScopeTree.Walk(package.Checks.Nodes))
                .Concat(package.Functions.Items.SelectMany(f => ScopeTree.Walk(f.Body.Nodes)));
            foreach (var leaf in leaves.OfType<SourceLeaf>().Where(l => l.Kind == "rendered" && Markdown(l.Header["format"], tree.FrameOf(l))))
                yield return ($"Node '{leaf.Id}'", leaf.Path, leaf.Body);

            var scopes = new List<(SourceDefinitions Definitions, ScopeFrame Frame)> { (package.Definitions, tree.PackageRoot) };
            scopes.AddRange(package.Functions.Items.Select(f => (f.Definitions, tree.FunctionRoot(f))));
            foreach (var (definitions, frame) in scopes)
            {
                foreach (var (text, name) in definitions.Macros.Items.SelectMany(e => Texts(e, '.')).Where(t => Markdown(t.Text.Header["format"], frame)))
                    yield return ($"Macro '{name}'", text.Path, text.Body);
            }

            foreach (var (definitions, frame) in scopes)
            {
                foreach (var entry in definitions.Snippets.Items)
                {
                    foreach (var (text, name) in Texts(entry, '/').Where(t => entry is SourceTextFolder || Markdown(t.Text.Header["format"], frame)))
                        yield return ($"Snippet '{name}'", text.Path, text.Body);
                }
            }

            foreach (var (definitions, frame) in scopes)
            {
                foreach (var renderer in definitions.Renderers.Items.Where(r => Markdown(r.Header["format"], frame)))
                    yield return ($"Renderer '{renderer.Id}'", renderer.Path, renderer.Body);
            }
        }

        /// <summary>A single snippet's or macro's file, or each item of a folder named <c>&lt;folder&gt;&lt;separator&gt;&lt;item&gt;</c>.</summary>
        private static IEnumerable<(SourceText Text, string Name)> Texts(SourceTextEntry entry, char separator) => entry switch
        {
            SourceSingleText single => [(single.Text, single.Id)],
            SourceTextFolder folder => folder.Items.Select(i => (i, $"{folder.Id}{separator}{i.Id}")),
            _ => [],
        };

        /// <summary>The 1-based line of <paramref name="body"/> the line ending at <paramref name="at"/> ends.</summary>
        private static int LineOf(string body, int at) => 1 + body.AsSpan(0, at).Count('\n');
    }
}
