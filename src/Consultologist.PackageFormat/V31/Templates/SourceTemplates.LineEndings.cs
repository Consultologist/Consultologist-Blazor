using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1510 (the owner's third fix-first group, item 4; G-D11, with G-D8's sites): a single line ending inside a paragraph is
/// not a break at 31, so publish warns (S05-065, W-3) once per line ending <see cref="MarkdownLineEndings.Find"/> names in a
/// Markdown body a reader sees. The sites are the migration's (<c>Migrate.HardBreaks</c>), taken from what the dialect read:
/// deliverables, <c>rendered</c> nodes, macros, snippets and renderers whose <c>format</c> is <c>markdown_block</c> or
/// <c>markdown_inline</c>, and every snippet folder's item (its body is <c>markdown_block</c>, part 05 § 6.2). A body whose
/// text reaches a prompt (a snippet a prompt, a part or a macro's instruction places, a folder item it reads as
/// <c>item.content</c>, a rendered node whose text it reads, and what those place or read in turn) is warned only when it is
/// also shown: placed or read by a deliverable, a macro's body or a rendered node no prompt reads, or by what those place or
/// read. That is where the migration keeps the line endings and says A10-092; everywhere else it writes the <c>\</c>, so a
/// migrated package is warned exactly where A10-092 was said. A prompt and a part are never sites.
/// </summary>
public static partial class SourceTemplates
{
    private sealed partial class Context
    {
        /// <summary>
        /// S05-065 for each line ending to break in each site of the package, in the order of the sites (deliverables,
        /// rendered nodes, macros, snippets, renderers), then of the line endings; <paramref name="bodies"/> are the template
        /// bodies <see cref="Run"/> checked.
        /// </summary>
        public void LineEndings(IReadOnlyList<CheckedBody> bodies)
        {
            var found = Sites().Select(site => (site.Subject, site.Path, site.Body, Hits: MarkdownLineEndings.Find(site.Body)))
                .Where(site => site.Hits.Count > 0)
                .ToList();
            if (found.Count == 0)
                return;

            var rendered = new Dictionary<string, CheckedBody>(StringComparer.Ordinal);
            foreach (var body in bodies.Where(b => b.Host.Site == TemplateSite.Rendered))
                rendered.TryAdd(body.Host.Path, body);

            var prompted = Closure(bodies.Where(b => b.Host.Site == TemplateSite.Prompt), rendered);
            var shown = Closure(
                bodies.Where(b => b.Host.Site == TemplateSite.Deliverable || (b.Host.Site == TemplateSite.Rendered && !prompted.Contains(b.Host.Path)))
                    .Concat(placedMacros.Values.SelectMany(m => m.Values).Select(m => m.Body).OfType<CheckedBody>()),
                rendered);

            foreach (var (subject, path, text, hits) in found)
            {
                if (prompted.Contains(path) && !shown.Contains(path))
                    continue;
                foreach (var at in hits)
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

        /// <summary>
        /// The files whose text <paramref name="readers"/> reach (<see cref="Reach"/>), and what those reach in turn through
        /// the rendered nodes among them, outward until none is new.
        /// </summary>
        private HashSet<string> Closure(IEnumerable<CheckedBody> readers, IReadOnlyDictionary<string, CheckedBody> rendered)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            var work = new Queue<string>();
            var walked = new HashSet<CheckedBody>(ReferenceEqualityComparer.Instance);
            foreach (var reader in readers)
            {
                foreach (var path in Reach(reader, walked))
                {
                    if (found.Add(path))
                        work.Enqueue(path);
                }
            }

            while (work.TryDequeue(out var next))
            {
                if (!rendered.TryGetValue(next, out var body))
                    continue;
                foreach (var path in Reach(body, walked))
                {
                    if (found.Add(path))
                        work.Enqueue(path);
                }
            }

            return found;
        }

        /// <summary>
        /// The files <paramref name="body"/>'s text reaches, itself or through the units it places (expanded, <see cref="CheckedBody.Units"/>;
        /// a macro's instruction, never its body, which is the reply's): each snippet placed, each folder item read as
        /// <c>item.content</c>, each snippet read in an expression, and each rendered node whose text is read (a flow's
        /// <c>.content</c> is the <c>ret</c>s it returns; a <c>.state</c> is no text, an agent's text its reply).
        /// </summary>
        private IEnumerable<string> Reach(CheckedBody body, HashSet<CheckedBody> walked)
        {
            foreach (var text in Units(body))
            {
                if (!ReferenceEquals(text, body) && text.Host.Site == TemplateSite.SnippetBody)
                    yield return text.Host.Path;
                // A unit's body checked at one place is shared by every placement there: its reads are walked once.
                if (!walked.Add(text))
                    continue;
                foreach (var item in text.FolderItems)
                    yield return item.Host.Path;
                foreach (var read in text.Reads.Concat(text.FolderItems.SelectMany(i => i.Reads)))
                {
                    foreach (var path in ReadFiles(read, text.Host.Function))
                        yield return path;
                }
            }
        }

        /// <summary><paramref name="body"/> and the bodies of the units it places.</summary>
        private static IEnumerable<CheckedBody> Units(CheckedBody body) => [body, .. body.Units.Select(u => u.Body)];

        /// <summary>The snippet or rendered node files <paramref name="read"/> reads the text of, as <see cref="Reach"/> says.</summary>
        private IEnumerable<string> ReadFiles(TypedRead read, SourceFunction? function)
        {
            var s = read.Canonical.Split('.');
            if (s[0] == "snippet")
            {
                if (Lookup(read.Canonical, function) is { } snippet)
                    yield return snippet.Text.Path;
                yield break;
            }

            if (read.RootedHead is not { } head || NodeAt(head, function) is not { } node)
                yield break;
            var member = read.Canonical.Length > head.Length ? read.Canonical[(head.Length + 1)..].Split('.')[0] : string.Empty;
            if (member == "state")
                yield break;
            foreach (var path in Returned(node, function, 0))
                yield return path;
        }

        /// <summary><paramref name="node"/>'s file when it is a rendered node; for a flow, those of the <c>ret</c>s it returns, outward.</summary>
        private IEnumerable<string> Returned(SourceNode node, SourceFunction? function, int depth)
        {
            if (node is SourceLeaf leaf)
            {
                if (leaf.Kind == "rendered")
                    yield return leaf.Path;
                yield break;
            }

            if (depth > SourceBounds.JsonDepth)
                yield break;
            foreach (var block in ScopeTree.Blocks(node))
            {
                if (block.Ret is { } ret && NodeAt(ret, function) is { } returned)
                {
                    foreach (var path in Returned(returned, function, depth + 1))
                        yield return path;
                }
            }
        }

        private readonly Dictionary<SourceFunction, Dictionary<string, SourceNode>> functionNodes = new(ReferenceEqualityComparer.Instance);
        private Dictionary<string, SourceNode>? packageNodes;

        /// <summary>The node whose rooted key is <paramref name="key"/>, in <paramref name="function"/>'s body or else the package's.</summary>
        private SourceNode? NodeAt(string key, SourceFunction? function)
        {
            if (function is not null)
            {
                if (!functionNodes.TryGetValue(function, out var own))
                    functionNodes[function] = own = Keyed(ScopeTree.Walk(function.Body.Nodes));
                if (own.TryGetValue(key, out var node))
                    return node;
            }

            packageNodes ??= Keyed(ScopeTree.Walk(package.Nodes.Nodes).Concat(ScopeTree.Walk(package.Checks.Nodes)));
            return packageNodes.GetValueOrDefault(key);
        }

        private static Dictionary<string, SourceNode> Keyed(IEnumerable<SourceNode> nodes)
        {
            var keyed = new Dictionary<string, SourceNode>(StringComparer.Ordinal);
            foreach (var node in nodes)
                keyed.TryAdd(node.Key, node);
            return keyed;
        }
    }
}
