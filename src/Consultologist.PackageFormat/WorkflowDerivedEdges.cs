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
/// macro texts its prompt reads: a snippet that reads <c>node.y.content</c> is
/// rendered when the prompt is, so the prompt waits on y (design § 7, "through
/// a snippet it reads"), and over the texts composed at its completion — its
/// instances and the fills of the inline macros its prompt instructs (B5 #1004).
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
        TemplateFan? Fan,
        // #1100 (v26 design § 1.2): the flow node whose body this node runs in —
        // its prompt and conditions read that body by bare id; null for the outside.
        string? Scope = null);

    /// <summary>A template walked: who it is, what it read, and its text and context for the render probe.</summary>
    public sealed record WalkedTemplate(TemplateKind Kind, string Id, string Subject, string Text, TemplateReferences References, TemplateReferenceContext Context);

    /// <summary>
    /// #1087: one place a node's prompt reaches <c>macro.&lt;Macro&gt;.prompt</c> — the
    /// template holding the read (the prompt itself, a snippet or folder item, a macro
    /// text, an instruction) and, when the holder is reached only through a fan item's
    /// own closure, that item's id.
    /// </summary>
    public sealed record PromptReadSite(string Macro, TemplateKind HolderKind, string HolderId, string HolderSubject, string? FanItem);

    /// <summary>#1037: where a <c>when</c> sits — and, at 26 (#1100), <c>Flow</c>: an <c>if</c> or <c>case</c> node's own expression.</summary>
    public enum ConditionSite { Node, NodeInstance, Deliverable, DeliverableInstance, FolderItem, Flow }

    /// <summary>
    /// #1037 (design § 16): a <c>when</c> walked — its site, the id of what it gates
    /// (a node, a deliverable, a folder/item), the macro when it gates an instance,
    /// its text, what it read and the sentences, and its context for the probe.
    /// </summary>
    public sealed record WalkedCondition(ConditionSite Site, string OwnerId, string? MacroId, string Subject, string Text, WorkflowConditionAt25.ConditionWalk Walk, TemplateReferenceContext Context);

    /// <summary>The key a folder item walks under: <c>&lt;folder&gt;/&lt;item&gt;</c>.</summary>
    public static string SnippetItemKey(string folder, string item) => $"{folder}/{item}";

    /// <summary>The walk, frozen: every template walked and each node's <c>with</c> references.</summary>
    public sealed class Walk
    {
        private readonly Dictionary<string, WalkNode> _nodes;
        private readonly Dictionary<(TemplateKind, string), TemplateReferences> _byKey;
        private readonly Dictionary<string, IReadOnlyList<string>> _folders;
        private readonly Dictionary<string, IReadOnlyList<string>> _deliverableSources;

        internal Walk(
            IReadOnlyList<WalkNode> nodes,
            IReadOnlyList<WalkedTemplate> templates,
            IReadOnlyDictionary<string, TemplateReferences> parameters,
            IReadOnlyDictionary<string, IReadOnlyList<string>> folders,
            IReadOnlyList<WalkedCondition>? conditions = null,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? deliverableSources = null)
        {
            _nodes = nodes.ToDictionary(n => n.Node.Id, StringComparer.Ordinal);
            _deliverableSources = (deliverableSources ?? new Dictionary<string, IReadOnlyList<string>>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Templates = templates;
            Parameters = parameters;
            Conditions = conditions ?? Array.Empty<WalkedCondition>();
            _byKey = new Dictionary<(TemplateKind, string), TemplateReferences>();
            foreach (var template in templates)
            {
                _byKey.TryAdd((template.Kind, template.Id), template.References);
            }

            _folders = folders.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        /// <summary>#1101: a template's node edges — its node reads, and for each deliverable it reads (deliverable.&lt;id&gt;.content) that deliverable's sources.</summary>
        private IEnumerable<string> EdgesIn(TemplateReferences refs) =>
            refs.Edges.Concat(refs.DeliverableReads.SelectMany(id => _deliverableSources.GetValueOrDefault(id) ?? Array.Empty<string>()));

        /// <summary>Every template walked: prompts (Id = the node id), snippets (Id = the snippet id or folder/item), macro texts and — #1044 — macro instructions (Id = the macro id). Walked whether or not a run reaches them; <see cref="MacrosReached"/> is the reachable view.</summary>
        public IReadOnlyList<WalkedTemplate> Templates { get; }

        /// <summary>Each node's <c>with</c> walked against its format's parameters — only nodes whose format declares parameters.</summary>
        public IReadOnlyDictionary<string, TemplateReferences> Parameters { get; }

        /// <summary>#1037: every <c>when</c> walked — the node's own, each instance's, each deliverable's and its instances', each folder item's.</summary>
        public IReadOnlyList<WalkedCondition> Conditions { get; }

        /// <summary>The conditions gating one thing: the node's own and its instances', or the deliverable's and its instances'.</summary>
        public IEnumerable<WalkedCondition> ConditionsOf(string ownerId, params ConditionSite[] sites) =>
            Conditions.Where(c => c.OwnerId == ownerId && sites.Contains(c.Site));

        /// <summary>
        /// The node ids a node depends on: its prompt's and its parameters' node
        /// reads, closed over the snippets and macro texts the prompt reads and,
        /// on a snippet fan whose prompt reads <c>item.content</c>, the folder's
        /// items — and (B5 #1004, design §§ 5–7) over the texts composed at this
        /// node when it completes: its before/after instances and the fills of
        /// the inline macros its prompt instructs (a <c>macro.&lt;id&gt;.prompt</c> read),
        /// which render over the same outputs, so the node waits on what they read.
        /// </summary>
        public IReadOnlySet<string> EdgesOf(string nodeId)
        {
            var edges = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<(TemplateKind, string)>();
            var slots = new HashSet<string>(StringComparer.Ordinal);

            void Take(TemplateReferences refs)
            {
                edges.UnionWith(EdgesIn(refs));
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

            // #1037: what the node's own when and its instances' whens read — the
            // node waits on them as it waits on what its texts read. #1100: an
            // if/case node's expression likewise — the flow node waits on it.
            foreach (var condition in ConditionsOf(nodeId, ConditionSite.Node, ConditionSite.NodeInstance, ConditionSite.Flow))
            {
                Visit(condition.Walk.References, seen, Take);
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
            VisitKey((TemplateKind.Macro, macroId), new HashSet<(TemplateKind, string)>(), refs => edges.UnionWith(EdgesIn(refs)));
            return edges;
        }

        /// <summary>
        /// #1067: the macro ids a run renders — placed on a node or a deliverable,
        /// or read as <c>.content</c> / <c>.prompt</c> from a template the run
        /// reaches. The roots are what the engine renders: every node's prompt and
        /// the folder items it fans, every instance's text (node and deliverable),
        /// every condition; the closure is <see cref="Visit"/>'s (snippets, folder
        /// items, macro texts, the instructions of inline macros) plus each inline
        /// macro's own text (its fill), as <see cref="EdgesOf"/> closes. An unplaced
        /// macro's own instruction or text, and a snippet nothing reads, are not
        /// roots — what they read does not count. Before this the validator's
        /// unused-macro rule read every walked template, so a macro whose
        /// instruction read its own content was "used" by itself and the refusal
        /// never fired.
        /// </summary>
        public IReadOnlySet<string> MacrosReached(IEnumerable<string> deliverableInstanceMacroIds)
        {
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<(TemplateKind, string)>();
            var slots = new HashSet<string>(StringComparer.Ordinal);

            void Take(TemplateReferences refs)
            {
                reached.UnionWith(refs.MacroContents);
                reached.UnionWith(refs.MacroPrompts);
                slots.UnionWith(refs.MacroPrompts);
            }

            foreach (var nodeId in _nodes.Keys.Order(StringComparer.Ordinal))
            {
                if (_byKey.TryGetValue((TemplateKind.Prompt, nodeId), out var prompt))
                {
                    Visit(prompt, seen, Take);

                    foreach (var item in FanItemsReadBy(nodeId, prompt, null))
                    {
                        VisitKey((TemplateKind.Snippet, item), seen, Take);
                    }
                }

                foreach (var instance in _nodes[nodeId].Node.Macros ?? new List<WorkflowNodeMacroSpec>())
                {
                    reached.Add(instance.Id);
                    VisitKey((TemplateKind.Macro, instance.Id), seen, Take);
                }
            }

            foreach (var id in deliverableInstanceMacroIds)
            {
                reached.Add(id);
                VisitKey((TemplateKind.Macro, id), seen, Take);
            }

            foreach (var condition in Conditions)
            {
                Visit(condition.Walk.References, seen, Take);
            }

            // An inline macro's fill is its text, rendered into the reply — what
            // that text reads is reached too; visit until no new slot.
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

            return reached;
        }

        /// <summary>#1037: the node ids a deliverable's own when and its instances' whens read, closed over the texts they read.</summary>
        public IReadOnlySet<string> DeliverableConditionReads(string deliverableId) =>
            ConditionReads(ConditionsOf(deliverableId, ConditionSite.Deliverable, ConditionSite.DeliverableInstance));

        /// <summary>#1038: the node ids a node's own when and its instances' whens read, closed over the texts they read.</summary>
        public IReadOnlySet<string> NodeConditionReads(string nodeId) =>
            ConditionReads(ConditionsOf(nodeId, ConditionSite.Node, ConditionSite.NodeInstance));

        /// <summary>
        /// #1038 (design § 16): the node ids every GATING when reads — a node's
        /// own, a deliverable's, a deliverable instance's — closed over the texts
        /// they read. The engine's stage one runs their closure before the boundary
        /// judges those whens over their outputs; a node instance's when is judged
        /// at the node's own completion and a folder item's reads inputs only, so
        /// neither gates the boundary.
        /// </summary>
        public IReadOnlySet<string> GatingReads() =>
            ConditionReads(Conditions.Where(c => c.Site is ConditionSite.Node or ConditionSite.Deliverable or ConditionSite.DeliverableInstance or ConditionSite.Flow));

        /// <summary>#1038: whether any gating when reads a profile fact (directly or through a text) — a read only the boundary can serve, since the starter judges before the profile is fetched.</summary>
        public bool GatingReadsProfile()
        {
            var found = false;
            var seen = new HashSet<(TemplateKind, string)>();

            foreach (var condition in Conditions.Where(c => c.Site is ConditionSite.Node or ConditionSite.Deliverable or ConditionSite.DeliverableInstance))
            {
                Visit(condition.Walk.References, seen, refs => found |= refs.ProfileFacts.Count > 0);
            }

            return found;
        }

        private IReadOnlySet<string> ConditionReads(IEnumerable<WalkedCondition> conditions)
        {
            var edges = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<(TemplateKind, string)>();

            foreach (var condition in conditions)
            {
                Visit(condition.Walk.References, seen, refs => edges.UnionWith(EdgesIn(refs)));
            }

            return edges;
        }

        /// <summary>Whether a macro's text reads <c>profile.signature</c> (the 25 signature carrier, where ≤24 read a token).</summary>
        public bool MacroReadsSignature(string macroId) =>
            _byKey.TryGetValue((TemplateKind.Macro, macroId), out var refs) && refs.ProfileFacts.Contains("signature");

        /// <summary>
        /// The inline macros a node's prompt instructs (design § 7): every
        /// <c>macro.&lt;id&gt;.prompt</c> read its template reaches — directly,
        /// through a snippet or macro text it reads (transitively), through the
        /// items of a folder it iterates, and — on a snippet fan whose prompt reads
        /// <c>item.content</c> — through the fan item's own snippet: every item's when
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
        /// #1087: the same closure as <see cref="PromptReadsOf"/>, with the holder of each
        /// read — which template the <c>macro.&lt;id&gt;.prompt</c> read sits in, in walk
        /// order. The prompt's own closure first (a holder it reaches is credited once,
        /// with no fan item: every instance renders it); then each fan item's closure
        /// beyond it, credited to that item — a holder several items reach yields one
        /// site per item. The editor's node pane names where a read lives from this.
        /// </summary>
        public IReadOnlyList<PromptReadSite> PromptReadSitesOf(string nodeId)
        {
            var sites = new List<PromptReadSite>();

            if (!_byKey.TryGetValue((TemplateKind.Prompt, nodeId), out var prompt))
            {
                return sites;
            }

            var holders = new Dictionary<TemplateReferences, WalkedTemplate>(ReferenceEqualityComparer.Instance);

            foreach (var template in Templates)
            {
                holders.TryAdd(template.References, template);
            }

            Action<TemplateReferences> TakeAs(string? fanItem) => refs =>
            {
                if (holders.TryGetValue(refs, out var holder))
                {
                    foreach (var macro in refs.MacroPrompts)
                    {
                        sites.Add(new PromptReadSite(macro, holder.Kind, holder.Id, holder.Subject, fanItem));
                    }
                }
            };

            var seen = new HashSet<(TemplateKind, string)>();
            Visit(prompt, seen, TakeAs(null));

            foreach (var item in FanItemsReadBy(nodeId, prompt, null))
            {
                var slash = item.IndexOf('/');
                var itemId = slash < 0 ? item : item[(slash + 1)..];
                VisitKey((TemplateKind.Snippet, item), new HashSet<(TemplateKind, string)>(seen), TakeAs(itemId));
            }

            return sites;
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

            foreach (var macro in prompt.MacroContents)
            {
                if (TextItemReadOf(TemplateKind.Macro, macro) is { } read) { return read; }
            }

            foreach (var slot in prompt.MacroPrompts)
            {
                if (TextItemReadOf(TemplateKind.Instruction, slot) is { } read) { return read; }
            }

            return null;
        }

        /// <summary>The folder-item keys a fanned prompt renders through <c>item.content</c>: all of them, or the one named.</summary>
        private IEnumerable<string> FanItemsReadBy(string nodeId, TemplateReferences prompt, string? itemId)
        {
            if (prompt.ItemMembers.Contains(TemplateMembers.Content)
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

        /// <summary>The closure a template renders: itself, the snippets and folder items it reads, the macro texts it reads, the instructions of the inline macros it reads (#1044) — each once.</summary>
        private void Visit(TemplateReferences refs, HashSet<(TemplateKind, string)> seen, Action<TemplateReferences> take)
        {
            take(refs);

            foreach (var slot in refs.MacroPrompts)
            {
                VisitKey((TemplateKind.Instruction, slot), seen, take);
            }

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

            foreach (var macro in refs.MacroContents)
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
        // #1105 (v26 design § 5): at 26 the files may still carry headers (the
        // engine's and the editor's entry gives the package as published; the
        // validator gives bodies, on which this is a no-op) — the walk reads the
        // effective manifest and the bodies, as the validator did.
        if (manifest.SpecVersion >= 26)
        {
            var texts = WorkflowTextHeaders.Resolve(manifest, files, new List<string>());
            manifest = texts.Manifest;
            files = texts.Files;
        }

        // #1102 (v26 design § 4): at 26 a node names its prompt FILE; below, the
        // declared prompt's id resolves through prompts[].
        var at26 = manifest.SpecVersion >= 26;
        var promptsById = at26
            ? new Dictionary<string, WorkflowPromptSpec>(StringComparer.Ordinal)
            : (manifest.Prompts ?? new List<WorkflowPromptSpec>()).ToDictionary(p => p.Id, StringComparer.Ordinal);
        var templates = new List<WalkedTemplate>();
        var parameters = new Dictionary<string, TemplateReferences>(StringComparer.Ordinal);
        var conditions = new List<WalkedCondition>();
        var inputTypes = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => WorkflowInputTypes.Of(g.First()), StringComparer.Ordinal);
        string? InputTypeOf(string id) => inputTypes.GetValueOrDefault(id);

        // #1037: a condition under a snippet-folder fan is typed against the folder's item ids.
        TemplateReferenceContext ConditionContext(TemplateFan? fan, string? scope)
        {
            var context = contextFor(fan) with { Scope = scope };
            return fan is SnippetFolderFan folderFan && snippets.Collections.TryGetValue(folderFan.Folder, out var collection)
                ? context with { FanItemIds = collection.Items.Select(i => i.Id).ToList() }
                : context;
        }

        void WalkCondition(ConditionSite site, string ownerId, string? macroId, string subject, string? when, TemplateFan? fan, string? scope = null)
        {
            if (when is null)
            {
                return;
            }

            var context = ConditionContext(fan, scope);
            conditions.Add(new WalkedCondition(site, ownerId, macroId, subject, when, WorkflowConditionAt25.Walk(subject, when, context, InputTypeOf), context));
        }

        foreach (var walkNode in nodes)
        {
            var node = walkNode.Node;
            // #1100: a body node's prompt and its own conditions read its body by bare id.
            var context = contextFor(walkNode.Fan) with { Scope = walkNode.Scope };

            WalkCondition(ConditionSite.Node, node.Id, null, $"Node '{node.Id}' condition", node.When, walkNode.Fan, walkNode.Scope);

            // #1100 (v26 design § 1): an if node's expression is a condition of
            // its own site — walked as a node's when is (one boolean), read as
            // the flow node's edges; a flow node runs in no loop, so no fan. A
            // case node's subject is a value, not a truth — the validator reads
            // it structurally (ValidateCaseSubjectsAt26) and adds its edge itself.
            WalkCondition(ConditionSite.Flow, node.Id, null, $"Node '{node.Id}' if", node.If, null);

            foreach (var instance in node.Macros ?? new List<WorkflowNodeMacroSpec>())
            {
                // #1089 (v26 design § 2.1, § 2.3): the gate keyed if at 26, walked at the
                // same site under the 26 subject; when below, under the 25 subject.
                var instanceSubject = instance.When is null && instance.If is not null
                    ? $"Node '{node.Id}' macro '{instance.Id}' if"
                    : $"Node '{node.Id}' macro '{instance.Id}' condition";
                WalkCondition(ConditionSite.NodeInstance, node.Id, instance.Id, instanceSubject, instance.When ?? instance.If, walkNode.Fan, walkNode.Scope);
            }

            if (node.Prompt is not null && walkNode.Shape is not null)
            {
                if (at26 && files.TryGetValue(node.Prompt, out var promptText))
                {
                    // The prompt is the node's own: its subject is the node's, in the
                    // family of "Node 'x' condition" and "Node 'x' if".
                    var subject = $"Node '{node.Id}' prompt";
                    templates.Add(new WalkedTemplate(TemplateKind.Prompt, node.Id, subject, promptText, TemplateReferenceWalker.Walk(subject, promptText, context), context));
                }
                else if (!at26 && promptsById.TryGetValue(node.Prompt, out var promptSpec) && files.TryGetValue(promptSpec.File, out var text))
                {
                    var subject = $"Prompt '{promptSpec.Id}'";
                    templates.Add(new WalkedTemplate(TemplateKind.Prompt, node.Id, subject, text, TemplateReferenceWalker.Walk(subject, text, context), context));
                }
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
                // #1105: at 26 the item's gate is its header's if (the same site; the effect is #1094's).
                WalkCondition(ConditionSite.FolderItem, key, null, $"Snippet folder '{folder}' item '{item.Id}' condition", item.When ?? item.If, null);
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

            // #1044: the instruction is a template, rendered where macro.<id>.prompt
            // is read — walked under the reader's fan like the text.
            if (!string.IsNullOrWhiteSpace(macro.Instruction))
            {
                var instructionSubject = $"Macro '{macro.Id}' instruction";
                templates.Add(new WalkedTemplate(TemplateKind.Instruction, macro.Id, instructionSubject, macro.Instruction, TemplateReferenceWalker.Walk(instructionSubject, macro.Instruction, snippetContext), snippetContext));
            }
        }

        foreach (var deliverable in manifest.Deliverables ?? new List<WorkflowDeliverableSpec>())
        {
            WalkCondition(ConditionSite.Deliverable, deliverable.Id, null, $"Deliverable '{deliverable.Id}' condition", deliverable.When, null);
            // #1101 (v26 design § 2.1): the gate keyed if, at the same sites.
            WalkCondition(ConditionSite.Deliverable, deliverable.Id, null, $"Deliverable '{deliverable.Id}' if", deliverable.If, null);

            foreach (var instance in deliverable.Macros ?? new List<WorkflowDeliverableMacroSpec>())
            {
                WalkCondition(ConditionSite.DeliverableInstance, deliverable.Id, instance.Id, $"Deliverable '{deliverable.Id}' macro '{instance.Id}' condition", instance.When, null);
                WalkCondition(ConditionSite.DeliverableInstance, deliverable.Id, instance.Id, $"Deliverable '{deliverable.Id}' macro '{instance.Id}' if", instance.If, null);
            }
        }

        return new Walk(nodes, templates, parameters, folders, conditions, DeliverableSourcesOf(manifest));
    }

    /// <summary>#1101: each deliverable's sources as node ids (the node.&lt;id&gt; entries; the validator names a misspelled one).</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> DeliverableSourcesOf(WorkflowPackageManifest manifest)
    {
        var sources = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var deliverable in manifest.Deliverables ?? new List<WorkflowDeliverableSpec>())
        {
            var ids = (deliverable.Sources ?? new List<string>())
                .Select(source => TemplateObjectReference.IsBare(source, TemplateObjects.Node, out var id) ? id : null)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            sources.TryAdd(deliverable.Id, ids);
        }

        return sources;
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

            // #1038: what the deliverable's own when and its instances' whens read
            // is read by the deliverable too — a root of its closure, kept by the prune.
            set.UnionWith(walk.DeliverableConditionReads(deliverable.Id));

            reads[deliverable.Id] = set;
        }

        return reads;
    }

    /// <summary>#1038: the node ids every gating when of the package reads (see <see cref="Walk.GatingReads"/>) — the job starter's call, from the manifest and files alone.</summary>
    public static IReadOnlySet<string> GatingReads(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor);
        return WalkAll(manifest, files, nodes, snippets, contextFor).GatingReads();
    }

    /// <summary>#1038: whether any gating when reads a profile fact (see <see cref="Walk.GatingReadsProfile"/>).</summary>
    public static bool GatingReadsProfile(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor);
        return WalkAll(manifest, files, nodes, snippets, contextFor).GatingReadsProfile();
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
        // #1105 (v26 design § 5): at 26 the files hold headers Scriban never
        // sees — the texts resolve as the validator resolved them (its sentences
        // discarded here): the manifest with each macro's header facts, the
        // files as bodies.
        var texts = manifest.SpecVersion >= 26 ? WorkflowTextHeaders.Resolve(manifest, files, discard) : null;
        if (texts is not null)
        {
            manifest = texts.Manifest;
            files = texts.Files;
        }
        var content = texts is null
            ? WorkflowDataResolver.ResolveContent(manifest, files, discard)
            : WorkflowDataResolver.ResolveContent(manifest, texts, discard);
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
                // #1091: an enum node's answer set rides its shape, as the validator's shapes carry it.
                shapes[node.Id] = new TemplateNodeShape(id, spec.Type, spec.Schema, EnumAnswerSet(node, spec, inputsById));
            }
            else if (node.Aggregate is { Count: > 0 })
            {
                // #1065: an aggregator's composed text is readable (node.<id>.content).
                shapes[node.Id] = TemplateNodeShape.Aggregator();
            }
        }

        // #1100 (v26 design § 1): at 26 the flow nodes' shapes ride beside the plain
        // nodes' — the same resolution the validator runs (its sentences discarded
        // here; the validator said them at publish), so the engine's and the
        // editor's walk see what the validator saw.
        var flows = manifest.SpecVersion >= 26
            ? WorkflowFlowNodes.Resolve(manifest, content, inputsById, discard)
            : WorkflowFlowNodes.Resolved.None;
        var plainShapes = new Dictionary<string, TemplateNodeShape>(shapes, StringComparer.Ordinal);
        foreach (var flow in flows.Flows.Values)
        {
            shapes[flow.Node.Id] = WorkflowFlowNodes.ShapeOf(flow, plainShapes);
        }

        var snippetIds = content.Scalars.Keys.ToHashSet(StringComparer.Ordinal);
        var folderIds = content.Collections.Keys.ToHashSet(StringComparer.Ordinal);
        var macroIds = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var bodies = flows.Flows.Count > 0 ? flows.BodyScopes : null;
        // #1101: at 26 a template reads deliverable.<id>.content.
        var deliverableIds = manifest.SpecVersion >= 26
            ? (manifest.Deliverables ?? new List<WorkflowDeliverableSpec>()).Select(d => d.Id).ToHashSet(StringComparer.Ordinal)
            : null;

        var labelled = manifest.SpecVersion >= 26;

        contextFor = fan => new TemplateReferenceContext(inputShapes, shapes, snippetIds, folderIds, macroIds, TemplateReferenceContext.NoIds, fan, Bodies: bodies, Deliverables: deliverableIds, Labelled: labelled);

        static IReadOnlyList<string>? EnumAnswerSet(WorkflowNodeSpec node, WorkflowFormatSpec spec, IReadOnlyDictionary<string, WorkflowInputSpec> inputs)
        {
            if (spec.Type != WorkflowInputTypes.Enum)
            {
                return null;
            }

            IReadOnlyList<string>? values = spec.Values?.ToList();

            if (spec.ValuesParameter is { } parameter && node.With is { } with && with.TryGetValue(parameter, out var supplied))
            {
                if (supplied is JsonArray array)
                {
                    values = array.Select(v => v?.GetValue<string>() ?? string.Empty).ToList();
                }
                else if (supplied is JsonValue value && value.TryGetValue(out string? reference)
                    && TemplateObjectReference.IsBare(reference, TemplateObjects.Input, out var inputId)
                    && inputs.TryGetValue(inputId, out var input))
                {
                    values = input.Values;
                }
            }

            return values;
        }

        return specs
            .Select(node => new WalkNode(
                node,
                shapes.GetValueOrDefault(node.Id),
                SpecOf(node.Format)?.Parameters,
                FanOf(node, content, inputsById) ?? FanAt26(node, flows),
                flows.Owner.GetValueOrDefault(node.Id)))
            .ToList();
    }

    /// <summary>#1100: at 26 a plain node's fan is its owner loop's, and a loop's its own.</summary>
    private static TemplateFan? FanAt26(WorkflowNodeSpec node, WorkflowFlowNodes.Resolved flows) =>
        flows.Flows.TryGetValue(node.Id, out var flow) ? flow.Fan : flows.FanOf(node.Id).Fan;

    /// <summary>The fan a node's template sees: a snippet folder, an array input's element, or nothing.</summary>
    public static TemplateFan? FanOf(WorkflowNodeSpec node, WorkflowPackageData content, IReadOnlyDictionary<string, WorkflowInputSpec> inputsById)
    {
        if (node.ForEach is null)
        {
            return null;
        }

        if (WorkflowFanSources.IsSnippetFan(node.ForEach))
        {
            var folder = WorkflowFanSources.CollectionIdOf(node.ForEach);
            return content.Collections.ContainsKey(folder) ? new SnippetFolderFan(folder) : null;
        }

        if (WorkflowInputFans.IsInputFan(node.ForEach)
            && inputsById.TryGetValue(WorkflowInputFans.InputIdOf(node.ForEach), out var input)
            && WorkflowInputTypes.Of(input) == WorkflowInputTypes.Array)
        {
            var element = WorkflowDeclarationNode.Of(input).Items
                ?? new WorkflowDeclarationNode(WorkflowInputTypes.Text, input.Label, false, null, null, null);
            return new InputArrayFan(element);
        }

        return null;
    }
}
