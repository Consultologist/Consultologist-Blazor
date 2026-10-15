using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// The derived edges of a specVersion 25 package (package-format-v25-design.md
/// § 2; B3 #1002): what each node reads. ONE walk of every template — each
/// prompt once per node with that node's fan, every snippet and folder item,
/// every macro text — and two callers: the validator at publish and load
/// (which also takes the sentences) and the job starter (which takes the edges
/// for the scheduler and the fire-set prune), so the scheduler can only see
/// the graph the validator saw. A node's edges close over the snippets and
/// macro texts its prompt reads: a snippet that reads <c>node.y.text</c> is
/// rendered when the prompt is, so the prompt waits on y (design § 7, "through
/// a snippet it reads"), and over the texts composed at its completion — its
/// instances and the fills of the slots its prompt sanctions (B5 #1004).
/// Aggregate sources join in
/// <see cref="WorkflowNodeClosure.Edges(IReadOnlyList{WorkflowNodeSpec}, IReadOnlyDictionary{string, IReadOnlySet{string}})"/>.
/// </summary>
public static class WorkflowDerivedEdges
{
    /// <summary>A node as the walk needs it: its spec, its format's shape and parameters, and the fan its template sees.</summary>
    public sealed record WalkNode(
        WorkflowNodeSpec Node,
        TemplateNodeShape? Shape,
        IReadOnlyDictionary<string, WorkflowFormatParameterSpec>? Parameters,
        TemplateFan? Fan);

    /// <summary>A template walked: who it is, what it read, and its text and context for the render probe.</summary>
    public sealed record WalkedTemplate(TemplateKind Kind, string Id, string Subject, string Text, TemplateReferences References, TemplateReferenceContext Context);

    /// <summary>The key a folder item walks under: <c>&lt;folder&gt;/&lt;item&gt;</c>.</summary>
    public static string SnippetItemKey(string folder, string item) => $"{folder}/{item}";

    /// <summary>The walk, frozen: every template walked and each node's <c>with</c> references.</summary>
    public sealed class Walk
    {
        private readonly Dictionary<string, WalkNode> _nodes;
        private readonly Dictionary<(TemplateKind, string), TemplateReferences> _byKey;
        private readonly Dictionary<string, IReadOnlyList<string>> _folders;

        internal Walk(
            IReadOnlyList<WalkNode> nodes,
            IReadOnlyList<WalkedTemplate> templates,
            IReadOnlyDictionary<string, TemplateReferences> parameters,
            IReadOnlyDictionary<string, IReadOnlyList<string>> folders)
        {
            _nodes = nodes.ToDictionary(n => n.Node.Id, StringComparer.Ordinal);
            Templates = templates;
            Parameters = parameters;
            _byKey = new Dictionary<(TemplateKind, string), TemplateReferences>();
            foreach (var template in templates)
            {
                _byKey.TryAdd((template.Kind, template.Id), template.References);
            }

            _folders = folders.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        /// <summary>Every template walked: prompts (Id = the node id), snippets (Id = the snippet id or folder/item), macro texts (Id = the macro id).</summary>
        public IReadOnlyList<WalkedTemplate> Templates { get; }

        /// <summary>Each node's <c>with</c> walked against its format's parameters — only nodes whose format declares parameters.</summary>
        public IReadOnlyDictionary<string, TemplateReferences> Parameters { get; }

        /// <summary>
        /// The node ids a node depends on: its prompt's and its parameters' node
        /// reads, closed over the snippets and macro texts the prompt reads and,
        /// on a snippet fan whose prompt reads <c>item.text</c>, the folder's
        /// items — and (B5 #1004, design §§ 5–7) over the texts composed at this
        /// node when it completes: its before/after instances and the fills of
        /// the slots its prompt sanctions (a <c>macro.&lt;id&gt;.prompt</c> read),
        /// which render over the same outputs, so the node waits on what they read.
        /// </summary>
        public IReadOnlySet<string> EdgesOf(string nodeId)
        {
            var edges = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<(TemplateKind, string)>();
            var slots = new HashSet<string>(StringComparer.Ordinal);

            void Take(TemplateReferences refs)
            {
                edges.UnionWith(refs.Edges);
                slots.UnionWith(refs.MacroPrompts);
            }

            if (_byKey.TryGetValue((TemplateKind.Prompt, nodeId), out var prompt))
            {
                Visit(prompt, seen, Take);

                foreach (var item in FanItemsReadBy(nodeId, prompt, null))
                {
                    VisitKey((TemplateKind.Snippet, item), seen, Take);
                }
            }

            if (_nodes.TryGetValue(nodeId, out var node))
            {
                foreach (var instance in node.Node.Macros ?? new List<WorkflowNodeMacroSpec>())
                {
                    VisitKey((TemplateKind.Macro, instance.Id), seen, Take);
                }
            }

            // A fill's text may itself read snippets and macro texts (never a
            // .prompt — § 5.2 refuses it — so this settles); visit until no new slot.
            var pending = new Queue<string>(slots);
            while (pending.TryDequeue(out var slot))
            {
                var before = slots.Count;
                VisitKey((TemplateKind.Macro, slot), seen, Take);
                foreach (var added in slots.Skip(before))
                {
                    pending.Enqueue(added);
                }
            }

            if (Parameters.TryGetValue(nodeId, out var parameters))
            {
                edges.UnionWith(parameters.Edges);
            }

            return edges;
        }

        /// <summary>Every node's edges, keyed by node id — every node the walk was given, reads or none.</summary>
        public IReadOnlyDictionary<string, IReadOnlySet<string>> Edges =>
            _nodes.Keys.ToDictionary(id => id, EdgesOf, StringComparer.Ordinal);

        /// <summary>Whether the walk holds a prompt for this node (a node without one — an aggregator, a computed node — reads nothing).</summary>
        public bool HasPrompt(string nodeId) => _byKey.ContainsKey((TemplateKind.Prompt, nodeId));

        /// <summary>
        /// B8 #1027 (design § 2, § 5.1): the node ids a macro's TEXT reads, closed
        /// over the snippets and macro texts it reads — the edges a deliverable
        /// instance of that macro gives the deliverable (its release waits on
        /// them, the fire-set cascade skips through them). Empty for a macro the
        /// walk did not see.
        /// </summary>
        public IReadOnlySet<string> MacroEdges(string macroId)
        {
            var edges = new HashSet<string>(StringComparer.Ordinal);
            VisitKey((TemplateKind.Macro, macroId), new HashSet<(TemplateKind, string)>(), refs => edges.UnionWith(refs.Edges));
            return edges;
        }

        /// <summary>Whether a macro's text reads <c>profile.signature</c> (the 25 signature carrier, where ≤24 read a token).</summary>
        public bool MacroReadsSignature(string macroId) =>
            _byKey.TryGetValue((TemplateKind.Macro, macroId), out var refs) && refs.ProfileFacts.Contains("signature");

        /// <summary>
        /// The slots a node's prompt sanctions (design § 7): every
        /// <c>macro.&lt;id&gt;.prompt</c> read its template reaches — directly,
        /// through a snippet or macro text it reads (transitively), through the
        /// items of a folder it iterates, and — on a snippet fan whose prompt reads
        /// <c>item.text</c> — through the fan item's own snippet: every item's when
        /// <paramref name="itemId"/> is null (the publish rule's "at least one
        /// item"), that item's alone when given (the instance that ran for it).
        /// The validator's rule and the engine's completion read ONE closure.
        /// </summary>
        public IReadOnlySet<string> PromptReadsOf(string nodeId, string? itemId = null)
        {
            var reads = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<(TemplateKind, string)>();

            if (!_byKey.TryGetValue((TemplateKind.Prompt, nodeId), out var prompt))
            {
                return reads;
            }

            void Take(TemplateReferences refs) => reads.UnionWith(refs.MacroPrompts);

            Visit(prompt, seen, Take);

            foreach (var item in FanItemsReadBy(nodeId, prompt, itemId))
            {
                VisitKey((TemplateKind.Snippet, item), seen, Take);
            }

            return reads;
        }

        /// <summary>
        /// C6 (#1012): the first fan-item member a text reaches — the macro or snippet
        /// itself and everything it reads — with the subject that reads it, or null.
        /// A text is walked with no fan of its own; the reading node's fan decides.
        /// </summary>
        public (string Member, string Subject)? TextItemReadOf(TemplateKind kind, string id)
        {
            var seen = new HashSet<(TemplateKind, string)>();
            (string, string)? found = null;
            var key = (kind, id);

            if (!_byKey.TryGetValue(key, out var refs))
            {
                return null;
            }

            void Take(TemplateReferences r)
            {
                if (found is null && r.ItemMembers.Count > 0)
                {
                    var subject = Templates.FirstOrDefault(t => ReferenceEquals(t.References, r))?.Subject ?? $"{kind} '{id}'";
                    found = (r.ItemMembers.Order(StringComparer.Ordinal).First(), subject);
                }
            }

            seen.Add(key);
            Visit(refs, seen, Take);
            return found;
        }

        /// <summary>
        /// C6 (#1012): the first fan-item member a node's prompt reaches THROUGH what it
        /// reads (a snippet, a macro text — the prompt's own reads are the walker's to
        /// refuse), with the subject, or null.
        /// </summary>
        public (string Member, string Subject)? PromptItemReadThroughOf(string nodeId)
        {
            if (!_byKey.TryGetValue((TemplateKind.Prompt, nodeId), out var prompt))
            {
                return null;
            }

            foreach (var snippet in prompt.Snippets)
            {
                if (TextItemReadOf(TemplateKind.Snippet, snippet) is { } read) { return read; }
            }

            foreach (var folder in prompt.SnippetFolders)
            {
                if (_folders.TryGetValue(folder, out var items))
                {
                    foreach (var item in items)
                    {
                        if (TextItemReadOf(TemplateKind.Snippet, item) is { } read) { return read; }
                    }
                }
            }

            foreach (var macro in prompt.MacroTexts)
            {
                if (TextItemReadOf(TemplateKind.Macro, macro) is { } read) { return read; }
            }

            return null;
        }

        /// <summary>The folder-item keys a fanned prompt renders through <c>item.text</c>: all of them, or the one named.</summary>
        private IEnumerable<string> FanItemsReadBy(string nodeId, TemplateReferences prompt, string? itemId)
        {
            if (prompt.ItemMembers.Contains(TemplateMembers.Text)
                && _nodes.TryGetValue(nodeId, out var node)
                && node.Fan is SnippetFolderFan fan
                && _folders.TryGetValue(fan.Folder, out var items))
            {
                foreach (var item in items)
                {
                    if (itemId is null || string.Equals(item, SnippetItemKey(fan.Folder, itemId), StringComparison.Ordinal))
                    {
                        yield return item;
                    }
                }
            }
        }

        /// <summary>The closure a template renders: itself, the snippets and folder items it reads, the macro texts it reads — each once.</summary>
        private void Visit(TemplateReferences refs, HashSet<(TemplateKind, string)> seen, Action<TemplateReferences> take)
        {
            take(refs);

            foreach (var snippet in refs.Snippets)
            {
                VisitKey((TemplateKind.Snippet, snippet), seen, take);
            }

            foreach (var folder in refs.SnippetFolders)
            {
                if (_folders.TryGetValue(folder, out var items))
                {
                    foreach (var item in items)
                    {
                        VisitKey((TemplateKind.Snippet, item), seen, take);
                    }
                }
            }

            foreach (var macro in refs.MacroTexts)
            {
                VisitKey((TemplateKind.Macro, macro), seen, take);
            }
        }

        private void VisitKey((TemplateKind, string) key, HashSet<(TemplateKind, string)> seen, Action<TemplateReferences> take)
        {
            if (seen.Add(key) && _byKey.TryGetValue(key, out var refs))
            {
                Visit(refs, seen, take);
            }
        }
    }

    /// <summary>
    /// The walk: each node's prompt once with that node's fan (a node with no
    /// prompt file in <paramref name="files"/> or no format shape walks
    /// nothing), its <c>with</c> against its format's parameters (a format
    /// without parameters walks no <c>with</c>; the validator refuses a stray
    /// one by name), every snippet, every folder item, every macro text.
    /// </summary>
    public static Walk WalkAll(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyList<WalkNode> nodes,
        WorkflowPackageData snippets,
        Func<TemplateFan?, TemplateReferenceContext> contextFor)
    {
        var promptsById = (manifest.Prompts ?? new List<WorkflowPromptSpec>()).ToDictionary(p => p.Id, StringComparer.Ordinal);
        var templates = new List<WalkedTemplate>();
        var parameters = new Dictionary<string, TemplateReferences>(StringComparer.Ordinal);

        foreach (var walkNode in nodes)
        {
            var node = walkNode.Node;
            var context = contextFor(walkNode.Fan);

            if (node.Prompt is not null
                && walkNode.Shape is not null
                && promptsById.TryGetValue(node.Prompt, out var promptSpec)
                && files.TryGetValue(promptSpec.File, out var text))
            {
                var subject = $"Prompt '{promptSpec.Id}'";
                templates.Add(new WalkedTemplate(TemplateKind.Prompt, node.Id, subject, text, TemplateReferenceWalker.Walk(subject, text, context), context));
            }

            if (walkNode.Parameters is { Count: > 0 })
            {
                parameters[node.Id] = TemplateReferenceWalker.WalkParameters(
                    $"Node '{node.Id}'",
                    node.With ?? new Dictionary<string, JsonNode?>(StringComparer.Ordinal),
                    walkNode.Parameters,
                    context);
            }
        }

        // A snippet or macro text runs under whatever fans the reader — item reads
        // are recorded here and judged at the reading node (C6 #1012).
        var snippetContext = contextFor(null) with { ItemDeferred = true };

        foreach (var (id, text) in snippets.Scalars)
        {
            var subject = $"Snippet '{id}'";
            templates.Add(new WalkedTemplate(TemplateKind.Snippet, id, subject, text, TemplateReferenceWalker.Walk(subject, text, snippetContext), snippetContext));
        }

        var folders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (folder, collection) in snippets.Collections)
        {
            var keys = new List<string>();

            foreach (var item in collection.Items)
            {
                var text = item.Fields.GetValueOrDefault("content") ?? string.Empty;
                var subject = $"Snippet '{folder}/{item.Id}'";
                var key = SnippetItemKey(folder, item.Id);
                keys.Add(key);
                templates.Add(new WalkedTemplate(TemplateKind.Snippet, key, subject, text, TemplateReferenceWalker.Walk(subject, text, snippetContext), snippetContext));
            }

            folders[folder] = keys;
        }

        foreach (var macro in manifest.Macros ?? new List<WorkflowMacroSpec>())
        {
            if (macro.File is null || !files.TryGetValue(macro.File, out var text))
            {
                continue;
            }

            var subject = $"Macro '{macro.Id}'";
            templates.Add(new WalkedTemplate(TemplateKind.Macro, macro.Id, subject, text, TemplateReferenceWalker.Walk(subject, text, snippetContext), snippetContext));
        }

        return new Walk(nodes, templates, parameters, folders);
    }

    /// <summary>
    /// The edges of a package at 25 from its manifest and files alone — the
    /// job starter's call. Shapes, fans and snippets are resolved without
    /// sentences (the validator already refused anything malformed at publish
    /// and load); whatever does not resolve contributes no reads.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Derive(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor);

        return WalkAll(manifest, files, nodes, snippets, contextFor).Edges;
    }

    /// <summary>
    /// B8 #1027: each deliverable's reads — the union of its placed macros'
    /// <see cref="Walk.MacroEdges"/>, sorted — keyed by deliverable id, for
    /// every deliverable of the manifest (a deliverable placing no macro reads
    /// nothing and maps to an empty set). The job starter's and the boundary's
    /// call, beside <see cref="Derive"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> DeliverableReads(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor);
        var walk = WalkAll(manifest, files, nodes, snippets, contextFor);
        var reads = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var deliverable in manifest.EffectiveDeliverables)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var entry in deliverable.Macros ?? new List<WorkflowDeliverableMacroSpec>())
            {
                set.UnionWith(walk.MacroEdges(entry.Id));
            }

            reads[deliverable.Id] = set;
        }

        return reads;
    }

    /// <summary>B8 #1027: the macro ids whose text reads <c>profile.signature</c> — the 25 signature carrier the starter's snapshot gate reads.</summary>
    public static IReadOnlySet<string> SignatureReaders(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor);
        var walk = WalkAll(manifest, files, nodes, snippets, contextFor);
        return (manifest.Macros ?? new List<WorkflowMacroSpec>())
            .Select(macro => macro.Id)
            .Where(walk.MacroReadsSignature)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The nodes as the walk needs them, resolved silently: local formats from
    /// <c>manifest.formats[]</c>, catalogue formats from <paramref name="formats"/>,
    /// fans from <c>forEach</c> (<c>snippet:&lt;folder&gt;</c> | <c>input:&lt;id&gt;</c>),
    /// snippets through <see cref="WorkflowDataResolver.ResolveContent"/>.
    /// </summary>
    public static IReadOnlyList<WalkNode> PrepareNodes(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats,
        out WorkflowPackageData snippets,
        out Func<TemplateFan?, TemplateReferenceContext> contextFor)
    {
        var discard = new List<string>();
        var content = WorkflowDataResolver.ResolveContent(manifest, files, discard);
        snippets = content;

        var inputsById = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .Where(input => WorkflowDeclaredIds.IsValid(input.Id))
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var inputShapes = inputsById.ToDictionary(pair => pair.Key, pair => WorkflowDeclarationNode.Of(pair.Value), StringComparer.Ordinal);

        var localSpecs = new Dictionary<string, WorkflowFormatSpec>(StringComparer.Ordinal);

        foreach (var format in manifest.Formats ?? new List<WorkflowFormatReference>())
        {
            if (format.File is not null
                && files.TryGetValue(format.File, out var json)
                && WorkflowFormatSpecJson.TryRead(json, $"Format '{format.Id}'", discard, out var spec))
            {
                localSpecs[format.Id] = spec!;
            }
        }

        WorkflowFormatSpec? SpecOf(string? formatId) =>
            formatId is null ? null
            : localSpecs.TryGetValue(formatId, out var local) ? local
            : formats is not null && formats.TryGet(formatId, out var catalogue) ? catalogue!.Spec
            : null;

        var specs = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToList();
        var shapes = new Dictionary<string, TemplateNodeShape>(StringComparer.Ordinal);

        foreach (var node in specs)
        {
            if (node.Format is { } id && SpecOf(id) is { } spec)
            {
                shapes[node.Id] = new TemplateNodeShape(id, spec.Type, spec.Schema);
            }
        }

        var snippetIds = content.Scalars.Keys.ToHashSet(StringComparer.Ordinal);
        var folderIds = content.Collections.Keys.ToHashSet(StringComparer.Ordinal);
        var macroIds = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        contextFor = fan => new TemplateReferenceContext(inputShapes, shapes, snippetIds, folderIds, macroIds, TemplateReferenceContext.NoIds, fan);

        return specs
            .Select(node => new WalkNode(
                node,
                shapes.GetValueOrDefault(node.Id),
                SpecOf(node.Format)?.Parameters,
                FanOf(node, content, inputsById)))
            .ToList();
    }

    /// <summary>The fan a node's template sees: a snippet folder, an array input's element, or nothing.</summary>
    public static TemplateFan? FanOf(WorkflowNodeSpec node, WorkflowPackageData content, IReadOnlyDictionary<string, WorkflowInputSpec> inputsById)
    {
        if (node.ForEach is null)
        {
            return null;
        }

        if (node.ForEach.StartsWith(TemplateObjects.Snippet + ":", StringComparison.Ordinal))
        {
            var folder = node.ForEach[(TemplateObjects.Snippet.Length + 1)..];
            return content.Collections.ContainsKey(folder) ? new SnippetFolderFan(folder) : null;
        }

        if (node.ForEach.StartsWith(WorkflowNodeBindingSources.InputPrefix, StringComparison.Ordinal)
            && inputsById.TryGetValue(node.ForEach[WorkflowNodeBindingSources.InputPrefix.Length..], out var input)
            && WorkflowInputTypes.Of(input) == WorkflowInputTypes.Array)
        {
            var element = WorkflowDeclarationNode.Of(input).Items
                ?? new WorkflowDeclarationNode(WorkflowInputTypes.Text, input.Label, false, null, null, null);
            return new InputArrayFan(element);
        }

        return null;
    }
}
