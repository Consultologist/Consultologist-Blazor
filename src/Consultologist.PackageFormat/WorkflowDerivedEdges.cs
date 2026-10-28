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
    // #1090 (v26 design § 2.2): Macro — the macro's own if, in its file's header.
    // #1094 (§ 2.1, § 5.4): Snippet — a standalone snippet's own if, in its header.
    public enum ConditionSite { Node, NodeInstance, Deliverable, DeliverableInstance, FolderItem, Flow, Macro, Snippet }

    /// <summary>
    /// #1037 (design § 16): a <c>when</c> walked — its site, the id of what it gates
    /// (a node, a deliverable, a folder/item), the macro when it gates an instance,
    /// its text, what it read and the sentences, and its context for the probe.
    /// </summary>
    public sealed record WalkedCondition(ConditionSite Site, string OwnerId, string? MacroId, string Subject, string Text, WorkflowConditionAt25.ConditionWalk Walk, TemplateReferenceContext Context);

    /// <summary>The key a folder item walks under: <c>&lt;folder&gt;/&lt;item&gt;</c>.</summary>
    public static string SnippetItemKey(string folder, string item) => $"{folder}/{item}";

    /// <summary>#1227: what a read's source is — a node, or an external object the diagram draws as a stadium.</summary>
    public enum ReadSourceKind { Node, Input, Snippet, SnippetFolder, MacroFolder }

    /// <summary>
    /// #1227: one read a node makes, for the diagram — the source and, for a node, the
    /// members read (<c>content</c> / <c>value</c>) merged over the closure;
    /// <see cref="GateOnly"/> when only the node's own gates (its when, an instance's
    /// when/if, a flow node's expression) reach it; <see cref="Via"/> the carrier the
    /// node's prompt reads directly (<c>snippet.x</c>, <c>macro.y</c>, an instance's
    /// <c>macro.z</c>) through whose closure the source is reached when the prompt itself
    /// never reads it — null for a direct read.
    /// </summary>
    public sealed record NodeRead(ReadSourceKind Kind, string Id, bool Content, bool Value, bool GateOnly, string? Via);

    /// <summary>The walk, frozen: every template walked and each node's <c>with</c> references.</summary>
    public sealed class Walk
    {
        private readonly Dictionary<string, WalkNode> _nodes;
        private readonly Dictionary<(TemplateKind, string), TemplateReferences> _byKey;
        private readonly Dictionary<string, IReadOnlyList<string>> _folders;
        private readonly Dictionary<string, IReadOnlyList<string>> _macroFolders;
        private readonly Dictionary<string, IReadOnlyList<string>> _deliverableSources;

        internal Walk(
            IReadOnlyList<WalkNode> nodes,
            IReadOnlyList<WalkedTemplate> templates,
            IReadOnlyDictionary<string, TemplateReferences> parameters,
            IReadOnlyDictionary<string, IReadOnlyList<string>> folders,
            IReadOnlyList<WalkedCondition>? conditions = null,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? deliverableSources = null,
            // #1099: a macro folder's item keys (<folder>/<item>), as _folders for snippets.
            IReadOnlyDictionary<string, IReadOnlyList<string>>? macroFolders = null)
        {
            _macroFolders = (macroFolders ?? new Dictionary<string, IReadOnlyList<string>>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
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

            VisitNodeClosure(nodeId, refs => edges.UnionWith(EdgesIn(refs)));

            // #1154: a case node switches on a node's value — read structurally
            // (#1100), an edge all the same: the decision waits on it. A flow
            // node's edges are what its DECISION reads, never its bodies (the
            // engine attaches those itself, the taken body only); the validator's
            // own map adds the bodies for the scope and cycle checks.
            if (_nodes.TryGetValue(nodeId, out var node) && node.Node.Case is { } subject
                && WorkflowFlowNodes.CaseNodeSubjectOf(subject) is { } subjectId && _nodes.ContainsKey(subjectId))
            {
                edges.Add(subjectId);
            }

            if (Parameters.TryGetValue(nodeId, out var parameters))
            {
                edges.UnionWith(parameters.Edges);
            }

            return edges;
        }

        /// <summary>
        /// #1227: a node's reads for the diagram, one per (kind, id) in first-read
        /// order, closed as <see cref="EdgesOf"/> closes — the prompt and its fan
        /// items, the instances' texts, the inline fills, the node's gates, then the
        /// <c>with</c> parameters; a <c>deliverable.&lt;id&gt;.content</c> read is that
        /// deliverable's sources, read as content. A case node's subject is
        /// structural, not listed (the diagram draws it on its own).
        /// </summary>
        public IReadOnlyList<NodeRead> ReadsOf(string nodeId)
        {
            var order = new List<(ReadSourceKind Kind, string Id)>();
            var reads = new Dictionary<(ReadSourceKind, string), (bool Content, bool Value, bool GateOnly)>();

            void Take(ReadSourceKind kind, string id, bool content, bool value, bool gate)
            {
                var key = (kind, id);
                if (reads.TryGetValue(key, out var seen))
                {
                    reads[key] = (seen.Content || content, seen.Value || value, seen.GateOnly && gate);
                }
                else
                {
                    order.Add(key);
                    reads[key] = (content, value, gate);
                }
            }

            void TakeRefs(TemplateReferences refs, bool gate)
            {
                foreach (var (id, read) in refs.Nodes)
                {
                    Take(ReadSourceKind.Node, id, read.ReadsContent, read.ReadsValue, gate);
                }

                foreach (var deliverable in refs.DeliverableReads)
                {
                    foreach (var source in _deliverableSources.GetValueOrDefault(deliverable) ?? Array.Empty<string>())
                    {
                        Take(ReadSourceKind.Node, source, content: true, value: false, gate);
                    }
                }

                foreach (var id in refs.Inputs) { Take(ReadSourceKind.Input, id, false, false, gate); }
                foreach (var id in refs.Snippets) { Take(ReadSourceKind.Snippet, id, false, false, gate); }
                foreach (var id in refs.SnippetFolders) { Take(ReadSourceKind.SnippetFolder, id, false, false, gate); }
                foreach (var id in refs.MacroFolders) { Take(ReadSourceKind.MacroFolder, id, false, false, gate); }
            }

            VisitNodeClosure(nodeId, TakeRefs);

            if (Parameters.TryGetValue(nodeId, out var parameters))
            {
                TakeRefs(parameters, gate: false);
            }

            _byKey.TryGetValue((TemplateKind.Prompt, nodeId), out var prompt);

            return order.Select(key =>
            {
                var (content, value, gateOnly) = reads[key];
                var direct = (prompt?.Nodes.ContainsKey(key.Id) ?? false) || (parameters?.Nodes.ContainsKey(key.Id) ?? false);
                var via = key.Kind == ReadSourceKind.Node && !gateOnly && !direct ? CarrierOf(nodeId, prompt, key.Id) : null;
                return new NodeRead(key.Kind, key.Id, content, value, gateOnly, via);
            }).ToList();
        }

        /// <summary>
        /// #1227: the carrier the node reads directly — a snippet or folder item its
        /// prompt reads, a macro whose content or prompt it reads, an instance placed
        /// on it — through whose closure the source node is reached; the first in the
        /// prompt's read order, as <c>snippet.x</c> / <c>macro.y</c>. Null when none.
        /// </summary>
        private string? CarrierOf(string nodeId, TemplateReferences? prompt, string source)
        {
            IEnumerable<((TemplateKind Kind, string Id) Key, string Text)> Carriers()
            {
                if (prompt != null)
                {
                    foreach (var snippet in prompt.Snippets) { yield return ((TemplateKind.Snippet, snippet), TemplateObjectReference.Snippet(snippet)); }
                    foreach (var folder in prompt.SnippetFolders)
                    {
                        foreach (var item in _folders.GetValueOrDefault(folder) ?? Array.Empty<string>()) { yield return ((TemplateKind.Snippet, item), TemplateObjectReference.SnippetPrefix + item); }
                    }

                    foreach (var item in FanItemsReadBy(nodeId, prompt, null)) { yield return ((TemplateKind.Snippet, item), TemplateObjectReference.SnippetPrefix + item); }
                    foreach (var macro in prompt.MacroContents) { yield return ((TemplateKind.Macro, macro), TemplateObjects.Macro + "." + macro); }
                    foreach (var macro in prompt.MacroPrompts) { yield return ((TemplateKind.Instruction, macro), TemplateObjects.Macro + "." + macro); }
                    foreach (var item in MacroFolderItems(prompt.MacroFolderContents)) { yield return ((TemplateKind.Macro, item), TemplateObjects.Macro + "." + item); }
                    foreach (var item in MacroFolderItems(prompt.MacroFolderPrompts)) { yield return ((TemplateKind.Instruction, item), TemplateObjects.Macro + "." + item); }
                }

                if (_nodes.TryGetValue(nodeId, out var node))
                {
                    foreach (var instance in node.Node.Macros ?? new List<WorkflowNodeMacroSpec>()) { yield return ((TemplateKind.Macro, instance.Id), TemplateObjects.Macro + "." + instance.Id); }
                }
            }

            foreach (var (key, text) in Carriers())
            {
                var edges = new HashSet<string>(StringComparer.Ordinal);
                VisitKey(key, new HashSet<(TemplateKind, string)>(), refs => edges.UnionWith(EdgesIn(refs)));
                if (key.Kind == TemplateKind.Instruction)
                {
                    // The instruction's fill renders too (§ 5.2): the inline macro's own text.
                    VisitKey((TemplateKind.Macro, key.Id), new HashSet<(TemplateKind, string)>(), refs => edges.UnionWith(EdgesIn(refs)));
                }

                if (edges.Contains(source))
                {
                    return text;
                }
            }

            return null;
        }

        /// <summary>
        /// #1155 (v26 design § 3.1): the deliverable ids a node's prompt reads as
        /// <c>deliverable.&lt;id&gt;.content</c>, closed exactly as <see cref="EdgesOf"/>
        /// closes (the snippets and macro texts the prompt reads, its instances,
        /// its conditions, the inline fills) — the engine threads each one's
        /// composed text to the render. Empty for a node reading none.
        /// </summary>
        public IReadOnlySet<string> DeliverableReadsOf(string nodeId)
        {
            var reads = new HashSet<string>(StringComparer.Ordinal);
            VisitNodeClosure(nodeId, refs => reads.UnionWith(refs.DeliverableReads));
            return reads;
        }

        /// <summary>#1155: the deliverable ids a macro's TEXT reads, closed as <see cref="MacroEdges"/> closes — a deliverable instance of that macro reads them.</summary>
        public IReadOnlySet<string> MacroDeliverableReads(string macroId)
        {
            var reads = new HashSet<string>(StringComparer.Ordinal);
            VisitKey((TemplateKind.Macro, macroId), new HashSet<(TemplateKind, string)>(), refs => reads.UnionWith(refs.DeliverableReads));
            return reads;
        }

        /// <summary>
        /// The closure of what a node renders, each template once: its prompt and
        /// the fan items it reads, its instances' texts, its own and its instances'
        /// conditions (and a flow node's expression), and the fills of the inline
        /// macros its prompt instructs — visited until no new slot (a fill never
        /// reads a .prompt, § 5.2, so this settles).
        /// </summary>
        private void VisitNodeClosure(string nodeId, Action<TemplateReferences> take) =>
            VisitNodeClosure(nodeId, (refs, _) => take(refs));

        /// <summary>#1227: the closure with each template flagged — <c>gate</c> true for what the node's own gates read (its when, an instance's when/if, a flow's expression), false for its texts.</summary>
        private void VisitNodeClosure(string nodeId, Action<TemplateReferences, bool> take)
        {
            var seen = new HashSet<(TemplateKind, string)>();
            var slots = new HashSet<string>(StringComparer.Ordinal);

            void Take(TemplateReferences refs)
            {
                take(refs, false);
                slots.UnionWith(refs.MacroPrompts);
                slots.UnionWith(MacroFolderItems(refs.MacroFolderPrompts));
            }

            void TakeGate(TemplateReferences refs)
            {
                take(refs, true);
                slots.UnionWith(refs.MacroPrompts);
                slots.UnionWith(MacroFolderItems(refs.MacroFolderPrompts));
            }

            if (_byKey.TryGetValue((TemplateKind.Prompt, nodeId), out var prompt))
            {
                Visit(prompt, seen, Take);

                foreach (var item in FanItemsReadBy(nodeId, prompt, null))
                {
                    VisitKey((TemplateKind.Snippet, item), seen, Take);
                }
            }

            _nodes.TryGetValue(nodeId, out var node);

            foreach (var instance in node?.Node.Macros ?? new List<WorkflowNodeMacroSpec>())
            {
                VisitKey((TemplateKind.Macro, instance.Id), seen, Take);
            }

            // #1037: what the node's own when and its instances' whens read — the
            // node waits on them as it waits on what its texts read. #1100: an
            // if/case node's expression likewise — the flow node waits on it.
            foreach (var condition in ConditionsOf(nodeId, ConditionSite.Node, ConditionSite.NodeInstance, ConditionSite.Flow))
            {
                Visit(condition.Walk.References, seen, TakeGate);
            }

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
                // #1099: a folder iterated is used; its items are reached through the element's reads.
                // #1116: an item read directly (keyed <folder>/<item>) uses its folder too.
                reached.UnionWith(refs.MacroFolders);
                reached.UnionWith(refs.MacroContents.Concat(refs.MacroPrompts).Where(k => k.Contains('/')).Select(k => k[..k.IndexOf('/')]));
                reached.UnionWith(MacroFolderItems(refs.MacroFolderContents));
                reached.UnionWith(MacroFolderItems(refs.MacroFolderPrompts));
                slots.UnionWith(MacroFolderItems(refs.MacroFolderPrompts));
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
        public IReadOnlySet<string> GatingReads()
        {
            var reads = new HashSet<string>(
                ConditionReads(Conditions.Where(c => c.Site is ConditionSite.Node or ConditionSite.Deliverable or ConditionSite.DeliverableInstance or ConditionSite.Flow)),
                StringComparer.Ordinal);

            // #1154: a case node's subject gates its bodies as an if's expression
            // does — a node it switches on is a deciding read.
            reads.UnionWith(_nodes.Values
                .Select(n => n.Node.Case is { } subject ? WorkflowFlowNodes.CaseNodeSubjectOf(subject) : null)
                .Where(id => id is not null && _nodes.ContainsKey(id))!);

            return reads;
        }

        /// <summary>#1038: whether any gating when reads a profile fact (directly or through a text) — a read only the boundary can serve, since the starter judges before the profile is fetched.</summary>
        public bool GatingReadsProfile()
        {
            var found = false;
            var seen = new HashSet<(TemplateKind, string)>();

            // #1154: an if node's expression is a gating read too — decided at the
            // boundary when it reads the profile, as a 25 when is.
            foreach (var condition in Conditions.Where(c => c.Site is ConditionSite.Node or ConditionSite.Deliverable or ConditionSite.DeliverableInstance or ConditionSite.Flow))
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

            void Take(TemplateReferences refs)
            {
                reads.UnionWith(refs.MacroPrompts);
                reads.UnionWith(MacroFolderItems(refs.MacroFolderPrompts));
            }

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
                VisitMacroGate(slot, seen, take);
            }

            foreach (var snippet in refs.Snippets)
            {
                VisitKey((TemplateKind.Snippet, snippet), seen, take);
                VisitSnippetGate(snippet, seen, take);
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
                VisitMacroGate(macro, seen, take);
            }

            // #1099 (v26 design § 5.3): a macro folder's element read is every item's.
            foreach (var item in MacroFolderItems(refs.MacroFolderContents))
            {
                VisitKey((TemplateKind.Macro, item), seen, take);
                VisitMacroGate(item, seen, take);
            }

            foreach (var item in MacroFolderItems(refs.MacroFolderPrompts))
            {
                VisitKey((TemplateKind.Instruction, item), seen, take);
                VisitMacroGate(item, seen, take);
            }
        }

        /// <summary>#1099: the item keys of the macro folders named, in the index's order.</summary>
        private IEnumerable<string> MacroFolderItems(IReadOnlySet<string>? folders) =>
            (folders ?? (IReadOnlySet<string>)new HashSet<string>()).SelectMany(f => _macroFolders.GetValueOrDefault(f) ?? Array.Empty<string>());

        /// <summary>#1099: a macro folder's item keys, in the index's order (empty for a folder the walk did not see).</summary>
        public IReadOnlyList<string> MacroFolderItemsOf(string folder) => _macroFolders.GetValueOrDefault(folder) ?? Array.Empty<string>();

        /// <summary>
        /// #1090 (v26 design § 2.2): a macro's own gate is judged where the macro
        /// is read — the reader waits on what the gate reads as it waits on what
        /// the macro's text reads. Seen under the macro's key with a suffix no id
        /// can carry.
        /// </summary>
        /// <summary>#1094: a standalone snippet's own gate is judged where the snippet is read — the reader waits on what it reads.</summary>
        private void VisitSnippetGate(string snippetId, HashSet<(TemplateKind, string)> seen, Action<TemplateReferences> take)
        {
            if (!seen.Add((TemplateKind.Snippet, snippetId + "/if")))
            {
                return;
            }

            foreach (var gate in ConditionsOf(snippetId, ConditionSite.Snippet))
            {
                Visit(gate.Walk.References, seen, take);
            }
        }

        private void VisitMacroGate(string macroId, HashSet<(TemplateKind, string)> seen, Action<TemplateReferences> take)
        {
            if (!seen.Add((TemplateKind.Macro, macroId + "/if")))
            {
                return;
            }

            foreach (var gate in ConditionsOf(macroId, ConditionSite.Macro))
            {
                Visit(gate.Walk.References, seen, take);
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
        Func<TemplateFan?, TemplateReferenceContext> contextFor,
        // #1090: the headers by path when the caller resolved the texts (the
        // validator); null and the walk resolves them itself at 26.
        IReadOnlyDictionary<string, WorkflowTextHeader>? headers = null,
        // #1099 (v26 design § 5.3): the macro folders resolved (Collections keyed by
        // folder id; each item's header on the item) — null and there are none.
        WorkflowPackageData? macros = null)
    {
        // #1105 (v26 design § 5): at 26 the files may still carry headers (the
        // engine's and the editor's entry gives the package as published; the
        // validator gives bodies, on which this is a no-op) — the walk reads the
        // effective manifest and the bodies, as the validator did.
        if (manifest.SpecVersion >= 26 && headers is null)
        {
            var texts = WorkflowTextHeaders.Resolve(manifest, files, new List<string>());
            manifest = texts.Manifest;
            files = texts.Files;
            headers = texts.Headers;
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

            // #1094 (v26 design § 2.1, § 5.4): a standalone snippet's own gate, its
            // header's if — walked in no fan (item.* refused) and no scope; judged
            // where the snippet is read.
            if (headers is not null
                && manifest.Snippets is { } table && table.TryGetValue(id, out var path)
                && headers.TryGetValue(path, out var header) && !string.IsNullOrWhiteSpace(header.If))
            {
                WalkCondition(ConditionSite.Snippet, id, null, $"Snippet '{id}' if", header.If, null);
            }
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
                // #1105/#1094 (v26 design § 2.1, § 5.4): at 26 the item's gate is its
                // header's if, walked under the folder's fan so its own item.id types
                // against the folder's items (the validator holds it to inputs and
                // item.id); the 25 when walks as ever, in no fan.
                if (item.When is null && item.If is not null)
                {
                    WalkCondition(ConditionSite.FolderItem, key, null, $"Snippet folder '{folder}' item '{item.Id}' if", item.If, new SnippetFolderFan(folder));
                }
                else
                {
                    WalkCondition(ConditionSite.FolderItem, key, null, $"Snippet folder '{folder}' item '{item.Id}' condition", item.When, null);
                }
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

            // #1090 (v26 design § 2.2): the macro's own gate, its header's if —
            // walked in no fan and no scope (inputs, scalar node values, run,
            // profile; never an item, a loop or a deliverable); judged where the
            // macro is read or placed.
            if (headers is not null && headers.TryGetValue(macro.File, out var header) && !string.IsNullOrWhiteSpace(header.If))
            {
                WalkCondition(ConditionSite.Macro, macro.Id, null, $"Macro '{macro.Id}' if", header.If, null);
            }
        }

        // #1099 (v26 design § 5.3): a macro folder's items — each a macro text
        // keyed <folder>/<item>, its instruction, its own gate — walked as the
        // flat macros are; read through {{ for m in macro.<folder> }}.
        var macroFolders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (folder, collection) in macros?.Collections ?? new Dictionary<string, WorkflowDataCollection>())
        {
            var keys = new List<string>();

            foreach (var item in collection.Items)
            {
                var key = SnippetItemKey(folder, item.Id);
                keys.Add(key);
                var text = item.Fields.GetValueOrDefault("content") ?? string.Empty;
                var subject = $"Macro '{key}'";
                templates.Add(new WalkedTemplate(TemplateKind.Macro, key, subject, text, TemplateReferenceWalker.Walk(subject, text, snippetContext), snippetContext));

                if (!string.IsNullOrWhiteSpace(item.Header?.Instruction))
                {
                    var instructionSubject = $"Macro '{key}' instruction";
                    templates.Add(new WalkedTemplate(TemplateKind.Instruction, key, instructionSubject, item.Header.Instruction, TemplateReferenceWalker.Walk(instructionSubject, item.Header.Instruction, snippetContext), snippetContext));
                }

                if (!string.IsNullOrWhiteSpace(item.Header?.If))
                {
                    WalkCondition(ConditionSite.Macro, key, null, $"Macro '{key}' if", item.Header.If, null);
                }
            }

            macroFolders[folder] = keys;
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

        return new Walk(nodes, templates, parameters, folders, conditions, DeliverableSourcesOf(manifest), macroFolders);
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
        WorkflowFormatCatalog? formats) => WalkOf(manifest, files, formats).Edges;

    /// <summary>
    /// #1227: the walk of a package from its manifest and files alone —
    /// <see cref="PrepareNodes(WorkflowPackageManifest, IReadOnlyDictionary{string, string}, WorkflowFormatCatalog?, out WorkflowPackageData, out Func{TemplateFan?, TemplateReferenceContext}, out WorkflowPackageData?)"/>
    /// then <see cref="WalkAll"/>, macro folders included — the one entry the
    /// diagram, the editor and <see cref="Derive"/> share.
    /// </summary>
    public static Walk WalkOf(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor, out var macroFolders);

        return WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);
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
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor, out var macroFolders);
        var walk = WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);
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

    /// <summary>
    /// #1155 (v26 design § 3.1): every node's deliverable reads — node id → the
    /// deliverable ids its prompt reads as <c>deliverable.&lt;id&gt;.content</c>,
    /// sorted, closed as its edges are (see <see cref="Walk.DeliverableReadsOf"/>).
    /// Every node the walk was given, reads or none. Empty sets below 26, where
    /// the object does not exist. The job starter's call.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> DeliverableReadsByNode(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor, out var macroFolders);
        var walk = WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);
        return nodes.ToDictionary(
            node => node.Node.Id,
            node => (IReadOnlySet<string>)new SortedSet<string>(walk.DeliverableReadsOf(node.Node.Id), StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    /// <summary>#1155: each deliverable's deliverable reads — the union of its placed macros' (<see cref="Walk.MacroDeliverableReads"/>), sorted, keyed by deliverable id, every deliverable of the manifest.</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> DeliverableReadsByDeliverable(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor, out var macroFolders);
        var walk = WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);
        var reads = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var deliverable in manifest.EffectiveDeliverables)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var entry in deliverable.Macros ?? new List<WorkflowDeliverableMacroSpec>())
            {
                set.UnionWith(walk.MacroDeliverableReads(entry.Id));
            }

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
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor, out var macroFolders);
        return WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders).GatingReads();
    }

    /// <summary>#1038: whether any gating when reads a profile fact (see <see cref="Walk.GatingReadsProfile"/>).</summary>
    public static bool GatingReadsProfile(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor, out var macroFolders);
        return WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders).GatingReadsProfile();
    }

    /// <summary>B8 #1027: the macro ids whose text reads <c>profile.signature</c> — the 25 signature carrier the starter's snapshot gate reads.</summary>
    public static IReadOnlySet<string> SignatureReaders(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats)
    {
        var nodes = PrepareNodes(manifest, files, formats, out var snippets, out var contextFor, out var macroFolders);
        var walk = WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);
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
        out Func<TemplateFan?, TemplateReferenceContext> contextFor) =>
        PrepareNodes(manifest, files, formats, out snippets, out contextFor, out _);

    /// <summary>#1099: the same, with the macro folders resolved at 26 (null below, or when the package declares none) — hand them to <see cref="WalkAll"/>.</summary>
    public static IReadOnlyList<WalkNode> PrepareNodes(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? formats,
        out WorkflowPackageData snippets,
        out Func<TemplateFan?, TemplateReferenceContext> contextFor,
        out WorkflowPackageData? macroFolders)
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
        // #1099: at 26 a macro entry may be a folder — resolved as the validator resolves it.
        macroFolders = texts is null ? null : WorkflowTextHeaders.ResolveMacroFolders(manifest, texts, discard);
        var macroFolderIds = macroFolders?.Collections.Keys.ToHashSet(StringComparer.Ordinal) ?? TemplateReferenceContext.NoIds;
        var macroIds = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).Where(id => !macroFolderIds.Contains(id)).ToHashSet(StringComparer.Ordinal);
        var bodies = flows.Flows.Count > 0 ? flows.BodyScopes : null;
        // #1101: at 26 a template reads deliverable.<id>.content.
        var deliverableIds = manifest.SpecVersion >= 26
            ? (manifest.Deliverables ?? new List<WorkflowDeliverableSpec>()).Select(d => d.Id).ToHashSet(StringComparer.Ordinal)
            : null;

        var labelled = manifest.SpecVersion >= 26;

        // #1116 (v26 design § 5.4): a folder's items by id at 26, so an item is readable directly.
        var snippetFolderItems = labelled ? content.Collections.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value.Items.Select(i => i.Id).ToList(), StringComparer.Ordinal) : null;
        var macroFolderItems = labelled ? (macroFolders?.Collections ?? new Dictionary<string, WorkflowDataCollection>()).ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value.Items.Select(i => i.Id).ToList(), StringComparer.Ordinal) : null;

        contextFor = fan => new TemplateReferenceContext(inputShapes, shapes, snippetIds, folderIds, macroIds, macroFolderIds, fan, Bodies: bodies, Deliverables: deliverableIds, Labelled: labelled,
            SnippetFolderItems: snippetFolderItems, MacroFolderItems: macroFolderItems);

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
