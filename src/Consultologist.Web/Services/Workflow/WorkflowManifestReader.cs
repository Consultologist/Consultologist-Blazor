using Consultologist.PackageFormat;
using System.Text.Json;

namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// Read-only views over the opaque manifest JsonElement for the editor's
/// display: which prompt texts and data items are editable, and the node
/// summary. Purely presentational — the manifest itself round-trips to the
/// publish endpoint untouched.
/// </summary>
public static class WorkflowManifestReader
{
    public sealed record PromptView(string Id, string File, IReadOnlyList<string> Variables, string? Prelude, bool Raw = false);

    /// <summary>One binding: a prompt variable, its source, and the optional concept renderer.</summary>
    public sealed record BindingView(string Variable, string From, string? As);

    public sealed record NodeView(
        string Id,
        string Label,
        string? Prompt,
        string? ForEach,
        IReadOnlyList<BindingView> Bindings,
        bool IsResult,
        bool HasOutput,
        IReadOnlyList<string>? Aggregate = null,
        string? OutputSchema = null,
        string? FailIfEmpty = null,
        // v10 (#498): a classifying node — kind "classifier" and the values it
        // may answer. Null on every node before 10.
        string? Kind = null, IReadOnlyList<string>? Values = null,
        // v11 #564: the package's reproducibility claim (record § 6) —
        // false when absent or not literally true.
        bool Reproducible = false,
        // v12 § 13 (#621): the check node's members — carried whole so no
        // editor pass can erase them; surfaced by rung (e)'s node card.
        string? Op = null, string? Of = null, string? In = null, string? FailWith = null,
        // v18 (#822): the node's own condition gate. Null on every node
        // before 18 and on any node without one.
        string? When = null,
        // v23 (#955): the macros composed into this node's prompt, before/after,
        // each with its optional node-level when. Null before 23 and on any node
        // without one.
        IReadOnlyList<NodeMacroView>? Macros = null,
        // v25 (C1 #1007, design § 3): the node's format, its parameters as written
        // (`with`, each value the JSON the manifest holds) and its invalid-reply
        // policy. Null on every node below 25; the format replaces kind, output.schema,
        // values and the check's members, which stay readable for the ≤24 panes.
        string? Format = null,
        IReadOnlyDictionary<string, JsonElement>? With = null,
        string? OnInvalid = null,
        // v26 (#1160, design § 1): a loop — `for` ("item in snippet.<folder>" |
        // "item in input.<id>"), its body node ids and the node it returns
        // ("node.<id>"). Null on every node below 26 and on a plain node at 26.
        string? For = null,
        IReadOnlyList<string>? Nodes = null,
        string? Ret = null,
        // v26 (#1159, design § 1): an if node — its condition (the 25 Scriban condition
        // grammar) with then/else bodies — and a case node — its subject (input.<id> or
        // node.<id>.value) with branches, each over one or more values, and an optional
        // else. Null on every node below 26 and on any other node at 26.
        string? If = null,
        string? Case = null,
        BodyView? Then = null,
        BodyView? Else = null,
        IReadOnlyList<BranchView>? Branches = null)
    {
        public bool IsClassifier => Kind == Consultologist.PackageFormat.WorkflowNodeKinds.Classifier;

        /// <summary>v26 (#1160): a flow node of kind for.</summary>
        public bool IsLoop => For is not null;

        /// <summary>v26 (#1159): a flow node of kind if.</summary>
        public bool IsIf => If is not null;

        /// <summary>v26 (#1159): a flow node of kind case.</summary>
        public bool IsCase => Case is not null;

        /// <summary>v26: any flow node — structural, never prompted; at 26 never placed in a body, at 27 (#1236) an if/case may sit in a loop's body.</summary>
        public bool IsFlow => IsLoop || IsIf || IsCase;

        /// <summary>
        /// v26 (#1159): the node's bodies, addressed — a loop's one body, an if's then and
        /// else, a case's branches and else — each with its label, its nodes and its ret.
        /// </summary>
        public IReadOnlyList<FlowBodyView> Bodies()
        {
            var bodies = new List<FlowBodyView>();
            if (IsLoop)
            {
                bodies.Add(new FlowBodyView(new FlowBodyRef(Id, FlowBodyRef.Loop), $"{Label} ({Collection ?? For})", Nodes ?? Array.Empty<string>(), Ret));
            }
            else if (IsIf)
            {
                bodies.Add(new FlowBodyView(new FlowBodyRef(Id, FlowBodyRef.Then), $"{Label} · then", Then?.Nodes ?? Array.Empty<string>(), Then?.Ret));
                if (Else != null)
                {
                    bodies.Add(new FlowBodyView(new FlowBodyRef(Id, FlowBodyRef.Else), $"{Label} · else", Else.Nodes, Else.Ret));
                }
            }
            else if (IsCase)
            {
                var branches = Branches ?? Array.Empty<BranchView>();
                for (var i = 0; i < branches.Count; i++)
                {
                    var values = branches[i].When.Count == 0 ? $"branch {i + 1} (no values)" : string.Join(", ", branches[i].When);
                    bodies.Add(new FlowBodyView(new FlowBodyRef(Id, FlowBodyRef.Branch(i)), $"{Label} · {values}", branches[i].Nodes, branches[i].Ret));
                }
                if (Else != null)
                {
                    bodies.Add(new FlowBodyView(new FlowBodyRef(Id, FlowBodyRef.Else), $"{Label} · else", Else.Nodes, Else.Ret));
                }
            }

            return bodies;
        }

        /// <summary>v26 (#1159): the body (of any flow node) holding the node, or null — a node runs in one body.</summary>
        public static FlowBodyView? BodyOf(IEnumerable<NodeView> nodes, string nodeId) =>
            nodes.Where(candidate => candidate.IsFlow)
                .SelectMany(candidate => candidate.Bodies())
                .FirstOrDefault(body => body.Nodes.Contains(nodeId, StringComparer.Ordinal));

        /// <summary>v26: the grammar's prefix — spelled once client-side (the library's reader is internal).</summary>
        public const string LoopPrefix = "item in ";

        /// <summary>v26: what the loop iterates in the 25 fan spelling (snippet.&lt;folder&gt; / input.&lt;id&gt;), or null.</summary>
        public string? Collection =>
            For is { } text && text.StartsWith(LoopPrefix, StringComparison.Ordinal) && text.Length > LoopPrefix.Length
                ? text[LoopPrefix.Length..].Trim()
                : null;

        /// <summary>v26: the returned body node's id (ret is "node.&lt;id&gt;"), or null.</summary>
        public string? RetNodeId =>
            Ret is { } ret && ret.StartsWith("node.", StringComparison.Ordinal) && ret.Length > 5 ? ret[5..] : null;

        /// <summary>v27 (#1236): the flow node whose body holds the node — a loop, or an if/case — or null.</summary>
        public static NodeView? FlowOf(IEnumerable<NodeView> nodes, string nodeId) =>
            nodes.FirstOrDefault(candidate => candidate.IsFlow && candidate.Bodies().Any(body => body.Nodes.Contains(nodeId, StringComparer.Ordinal)));

        /// <summary>v27 (#1236, design § 1.1): an if/case listed in a loop's body — decided once per item; its branch nodes run under the loop.</summary>
        public static bool IsInner(IEnumerable<NodeView> nodes, string flowId) =>
            nodes.Any(candidate => candidate.IsLoop && candidate.Nodes is { } body && body.Contains(flowId, StringComparer.Ordinal));

        /// <summary>
        /// v26 (#1160): the loop whose body holds the node, or null — a node runs in one body.
        /// v27 (#1236, design § 4): two levels — a branch node of an if/case inside a loop
        /// runs under that loop, so it types item.* and previews per item as a body node does.
        /// </summary>
        public static NodeView? LoopOf(IEnumerable<NodeView> nodes, string nodeId)
        {
            var all = nodes as IReadOnlyCollection<NodeView> ?? nodes.ToList();
            var direct = all.FirstOrDefault(candidate => candidate.IsLoop && candidate.Nodes is { } body && body.Contains(nodeId, StringComparer.Ordinal));
            if (direct is not null)
            {
                return direct;
            }

            return FlowOf(all, nodeId) is { IsLoop: false } flow
                ? all.FirstOrDefault(candidate => candidate.IsLoop && candidate.Nodes is { } body && body.Contains(flow.Id, StringComparer.Ordinal))
                : null;
        }

        /// <summary>
        /// v26 (#1160): what the node fans over, in the 25 spelling — its own forEach below 26,
        /// its loop's collection at 26 — so every site that types item.* reads one answer.
        /// </summary>
        public static string? EffectiveFan(NodeView node, IEnumerable<NodeView> nodes) =>
            node.ForEach ?? LoopOf(nodes, node.Id)?.Collection;

        public bool IsCheck => Kind == Consultologist.PackageFormat.WorkflowNodeKinds.Check;

        public bool IsTemplate => Kind == Consultologist.PackageFormat.WorkflowNodeKinds.Template;
    }

    /// <summary>v26 (#1159): a flow body — the node ids it runs and the one it returns ("node.&lt;id&gt;").</summary>
    public sealed record BodyView(IReadOnlyList<string> Nodes, string? Ret)
    {
        public string? RetNodeId => RetNodeIdOf(Ret);
    }

    /// <summary>v26 (#1159): a case branch — the values it takes, its body and its ret.</summary>
    public sealed record BranchView(IReadOnlyList<string> When, IReadOnlyList<string> Nodes, string? Ret)
    {
        public string? RetNodeId => RetNodeIdOf(Ret);
    }

    /// <summary>The id in a "node.&lt;id&gt;" ret, or null.</summary>
    public static string? RetNodeIdOf(string? ret) =>
        ret is { } text && text.StartsWith("node.", StringComparison.Ordinal) && text.Length > 5 ? text[5..] : null;

    /// <summary>
    /// v26 (#1159): a body's address — the flow node and which of its bodies: a loop's
    /// one body, an if's then/else, a case's branch:&lt;n&gt; (0-based) or else. The key is
    /// what a placement select carries: a loop's bare id (#1160's spelling, kept), else
    /// "&lt;id&gt;/&lt;body&gt;".
    /// </summary>
    public sealed record FlowBodyRef(string FlowId, string Body)
    {
        public const string Loop = "loop";
        public const string Then = "then";
        public const string Else = "else";
        public const string BranchPrefix = "branch:";

        public static string Branch(int index) => BranchPrefix + index;

        public bool IsLoop => Body == Loop;
        public bool IsThen => Body == Then;
        public bool IsElse => Body == Else;
        public int? BranchIndex => Body.StartsWith(BranchPrefix, StringComparison.Ordinal) && int.TryParse(Body[BranchPrefix.Length..], out var i) ? i : null;

        public string Key => IsLoop ? FlowId : $"{FlowId}/{Body}";

        public static FlowBodyRef Parse(string key)
        {
            var slash = key.IndexOf('/');
            return slash < 0 ? new FlowBodyRef(key, Loop) : new FlowBodyRef(key[..slash], key[(slash + 1)..]);
        }
    }

    /// <summary>v26 (#1159): one body of a flow node as the panes and pickers read it.</summary>
    public sealed record FlowBodyView(FlowBodyRef Ref, string Label, IReadOnlyList<string> Nodes, string? Ret)
    {
        public string Key => Ref.Key;
        public string? RetNodeId => RetNodeIdOf(Ret);
    }

    /// <summary>
    /// One declared field of an object (v9 § 4). v10 (#498): a field may
    /// itself be structure — an array with its element in <c>Items</c>, an
    /// object with its <c>Fields</c> — to any depth the manifest declares.
    /// </summary>
    public sealed record FieldView(
        string Id,
        string Label,
        bool Required,
        string? Type = null,
        IReadOnlyList<string>? Values = null,
        ElementView? Items = null,
        IReadOnlyList<FieldView>? Fields = null);

    /// <summary>
    /// v10 (#498): what one element of an array is — the manifest's
    /// <c>items</c>, whether written as a type name or as a spec. The editor
    /// holds an array-of-object's fields and an array-of-enum's values on the
    /// input (the v9 form, which is what it writes); an element carries its own
    /// only when it is an array, or sits below one.
    /// </summary>
    public sealed record ElementView(
        string Type,
        ElementView? Items = null,
        IReadOnlyList<FieldView>? Fields = null,
        IReadOnlyList<string>? Values = null)
    {
        /// <summary>The v9 form: a type name and nothing of its own.</summary>
        public bool IsBare => Items is null && Fields is null && Values is null;
    }

    /// <summary>
    /// One declared input. v8 types it: <c>Type</c> null means text, which
    /// is what keeps a v7 declaration valid. <c>Values</c> belongs to enum.
    ///
    /// v9 (#429): <c>Items</c> is an array's element type and <c>Fields</c> an
    /// object's (or an array-of-object's) fields. Carried here because the
    /// composer rebuilds the whole inputs list from these records — without
    /// them, relabelling one input stripped every other's structure.
    /// </summary>
    public sealed record InputView(
        string Id,
        string Label,
        bool Required,
        string? Type = null,
        IReadOnlyList<string>? Values = null,
        ElementView? Items = null,
        IReadOnlyList<FieldView>? Fields = null,
        // #745: carried so an inputs recompose does not silently drop them.
        // Types is the union arms (#729·v14) — Type stays the primary/first arm;
        // ExpectedContent is the content channel (#728·v13/#730·v16/#673·v17).
        IReadOnlyList<string>? Types = null,
        string? ExpectedContent = null);

    /// <summary>
    /// One declared deliverable: authored id and label over an aggregator node.
    ///
    /// v8's <c>When</c> is held as the raw string rather than a parsed
    /// structure, so a row nobody edited round-trips exactly as authored — a
    /// manifest written elsewhere as <c>when: billable</c> is not rewritten to
    /// <c>billable == true</c> by an editor that only passed it through.
    /// </summary>
    public sealed record ResultView(
        string Id,
        string Node,
        string Label,
        string? When = null,
        // v11 #564: the deliverable's ordered macro entries and the signed
        // flag (§ 4/§ 5). Signature is tri-state on purpose: presence, not
        // truth, is what the validator refuses below 11 — an authored false
        // is carried as read. v12 (#621): entries carry placement and when —
        // the whole object, so no editor pass can erase them.
        IReadOnlyList<ResultMacroView>? Macros = null,
        bool? Signature = null,
        // v12 § 13 (#621): the deliverable's check gate (node:<id>).
        string? Check = null,
        // v26 (#1161, design § 3): what the deliverable composes — node.<id> refs in
        // order (node and the aggregator fold into it) — and its gate keyed if (when is
        // retired). Null on every deliverable below 26.
        IReadOnlyList<string>? Sources = null,
        string? If = null)
    {
        /// <summary>The node ids the deliverable composes — its sources at 26, its node below.</summary>
        public IReadOnlyList<string> SourceNodeIds =>
            Sources != null
                ? Sources.Select(source => TemplateObjectReference.NodeIdOf(source) ?? source).ToList()
                : TemplateObjectReference.NodeIdOf(Node) is { } nodeId ? new[] { nodeId } : Array.Empty<string>();
    }

    /// <summary>
    /// One entry of a deliverable's macro list (v12 § 4/§ 14) — the mirror
    /// of WorkflowDeliverableMacroSpec, IsBare discipline included: a bare entry
    /// writes the v11 string, an adorned one the object, and When counts.
    /// </summary>
    public sealed record ResultMacroView(string Id, string? Before = null, string? After = null, string? When = null, string? ForItem = null, bool? Slot = null, string? At = null,
        // v26 (#1161): the entry's gate keyed if (when is retired at 26).
        string? If = null)
    {
        public bool IsBare => Before is null && After is null && When is null && ForItem is null && Slot is null && At is null && If is null;

        // v20 (#863): the model places this macro inline via a [[slot:<id>]]
        // marker; it names no anchor. Mirror of WorkflowDeliverableMacroSpec.IsSlot.
        public bool IsSlot => Slot == true;

        // v25 (C3 #1009): a deliverable instance's placement — prepend | append;
        // null on a 25 entry is "not yet chosen" (the registry refuses it).
        public bool IsPrepend => At == Consultologist.PackageFormat.WorkflowDeliverableMacroSpec.Prepend;

        public bool IsAppend => At == Consultologist.PackageFormat.WorkflowDeliverableMacroSpec.Append;
    }

    /// <summary>
    /// v23 (#955): one macro attached to a node — the library id, whether it
    /// lands after the prompt (else before, the bare form), and its optional
    /// node-level when. A bare read writes the string form, an adorned one the
    /// object. Mirror of WorkflowNodeMacroSpec.
    /// </summary>
    /// v26 (#1205): <paramref name="If"/> — the instance's gate keyed if at 26 (when and forItem retired there).
    public sealed record NodeMacroView(string Id, string? At = null, string? When = null, bool? Optional = null, string? ForItem = null, string? If = null)
    {
        public bool IsBare => At is null && When is null && Optional is null && ForItem is null && If is null;

        public bool IsAfter => At == Consultologist.PackageFormat.WorkflowNodeMacroSpec.After;
    }

    /// <summary>One declared macro (v11 § 4): package-owned template text.
    /// v12 § 3 (#621): optional and its default, carried as read.
    /// v25 (C3 #1009): the instruction — mandatory at 25 — carried as read (null when absent).
    /// v26 (#1163): <paramref name="If"/> — the macro's own gate, from its file header (null when absent).</summary>
    public sealed record MacroView(string Id, string Label, string File, bool? Optional = null, bool? Default = null, string? Instruction = null, string? If = null);

    /// <summary>One declared output-contract schema: its id and the package file its JSON body lives in.</summary>
    public sealed record SchemaView(string Id, string File);

    /// <summary>One declared shared prelude: its id and the package file its text lives in.</summary>
    public sealed record PreludeView(string Id, string File);

    /// <summary>The package's templating engine and pinned version (Scriban) — set by the platform, shown read-only.</summary>
    public sealed record TemplatingView(string Engine, string EngineVersion);

    /// <summary>
    /// One collection (≤24 data) or snippet-folder (25) item. <paramref name="When"/>
    /// is the item's input-only condition as written in index.json (25; null
    /// when absent — the item is always included).
    /// v26 (#1163): at 26 <paramref name="Name"/> is the header's label and <paramref name="When"/>
    /// its <c>if</c>; a macro folder's item also carries its header's instruction, optional and default.
    /// </summary>
    public sealed record DataItemView(string Id, string Name, string File, string? When = null, string? Instruction = null, bool? Optional = null, bool? Default = null);

    /// <summary>A folder; <paramref name="Kind"/> (v26 #1163) follows the declaration — the snippets map or the macros map.</summary>
    public sealed record CollectionView(string Id, string Directory, IReadOnlyList<DataItemView> Items, Consultologist.PackageFormat.WorkflowTextKind Kind = Consultologist.PackageFormat.WorkflowTextKind.Snippet);

    /// <summary>
    /// v26 (#1163): one snippet or macro file's header as loaded — the parsed header (null when the
    /// JSON does not parse), the library's sentences when it is refused (none, an unknown key, no
    /// label …) and the body after it (the whole file when refused, so the editor shows it as held).
    /// </summary>
    public sealed record TextHeaderView(string File, Consultologist.PackageFormat.WorkflowTextKind Kind, Consultologist.PackageFormat.WorkflowTextHeader? Header, IReadOnlyList<string> Errors, string Body)
    {
        public bool Refused => Header is null || Errors.Count > 0;
    }

    public static IReadOnlyList<PromptView> ReadPrompts(JsonElement manifest)
    {
        var prompts = new List<PromptView>();

        if (!TryGetProperty(manifest, "prompts", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return prompts;
        }

        foreach (var prompt in array.EnumerateArray())
        {
            var variables = new List<string>();

            if (TryGetProperty(prompt, "variables", out var vars) && vars.ValueKind == JsonValueKind.Array)
            {
                variables.AddRange(vars.EnumerateArray().Select(v => v.GetString() ?? string.Empty));
            }

            prompts.Add(new PromptView(
                ReadString(prompt, "id") ?? string.Empty,
                ReadString(prompt, "file") ?? string.Empty,
                variables,
                ReadString(prompt, "prelude"),
                ReadBool(prompt, "raw") == true));
        }

        return prompts;
    }

    public static IReadOnlyList<NodeView> ReadNodes(JsonElement manifest)
    {
        var nodes = new List<NodeView>();
        var resultNodeIds = ReadResultNodeIds(manifest);

        if (!TryGetProperty(manifest, "nodes", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return nodes;
        }

        foreach (var node in array.EnumerateArray())
        {
            var bindings = new List<BindingView>();

            if (TryGetProperty(node, "bindings", out var bindingsElement) && bindingsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var binding in bindingsElement.EnumerateObject())
                {
                    bindings.Add(ReadBinding(binding.Name, binding.Value));
                }
            }

            List<string>? aggregate = null;

            if (TryGetProperty(node, "aggregate", out var aggregateElement) && aggregateElement.ValueKind == JsonValueKind.Array)
            {
                aggregate = aggregateElement.EnumerateArray()
                    .Select(entry => entry.GetString() ?? string.Empty)
                    .Where(entry => entry.Length > 0)
                    .ToList();
            }

            var id = ReadString(node, "id") ?? string.Empty;
            var hasOutput = TryGetProperty(node, "output", out var output) && output.ValueKind == JsonValueKind.Object;
            // v25 (B5 #1004): failIfEmpty is a top-level node key once output is retired;
            // read there when no output object carries it.
            var failIfEmpty = hasOutput ? ReadString(output, "failIfEmpty") : ReadString(node, "failIfEmpty");
            nodes.Add(new NodeView(
                id,
                ReadString(node, "label") ?? id,
                ReadString(node, "prompt"),
                ReadString(node, "forEach"),
                bindings,
                resultNodeIds.Contains(id),
                hasOutput,
                aggregate,
                hasOutput ? ReadString(output, "schema") : null,
                failIfEmpty,
                ReadString(node, "kind"),
                ReadStringArray(node, "values"),
                ReadBool(node, "reproducible") == true,
                ReadString(node, "op"),
                ReadString(node, "of"),
                ReadString(node, "in"),
                ReadString(node, "failWith"),
                ReadString(node, "when"),
                ReadNodeMacroEntries(node),
                ReadString(node, "format"),
                ReadWith(node),
                ReadString(node, "onInvalid"),
                // v26 (#1160): the loop's three members.
                ReadString(node, "for"),
                ReadStringArray(node, "nodes"),
                ReadString(node, "ret"),
                // v26 (#1159): the if and case members.
                ReadString(node, "if"),
                ReadString(node, "case"),
                ReadBody(node, "then"),
                ReadBody(node, "else"),
                ReadBranches(node)));
        }

        return nodes;
    }

    /// <summary>v26 (#1159): a flow body object { nodes, ret }, or null when absent or not an object.</summary>
    private static BodyView? ReadBody(JsonElement node, string property) =>
        TryGetProperty(node, property, out var body) && body.ValueKind == JsonValueKind.Object
            ? new BodyView(ReadStringArray(body, "nodes") ?? Array.Empty<string>(), ReadString(body, "ret"))
            : null;

    /// <summary>v26 (#1159): a case node's branches [{ when[], nodes[], ret }], or null when absent.</summary>
    private static IReadOnlyList<BranchView>? ReadBranches(JsonElement node)
    {
        if (!TryGetProperty(node, "branches", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return array.EnumerateArray()
            .Where(branch => branch.ValueKind == JsonValueKind.Object)
            .Select(branch => new BranchView(
                ReadStringArray(branch, "when") ?? Array.Empty<string>(),
                ReadStringArray(branch, "nodes") ?? Array.Empty<string>(),
                ReadString(branch, "ret")))
            .ToList();
    }

    /// <summary>v25: a node's `with` object as written — parameter name to its JSON value; null when absent or not an object.</summary>
    private static IReadOnlyDictionary<string, JsonElement>? ReadWith(JsonElement node)
    {
        if (!TryGetProperty(node, "with", out var with) || with.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return with.EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.Clone(), StringComparer.Ordinal);
    }

    /// <summary>v25 (C1 #1007): one package-local format — its id and file (`formats[]`).</summary>
    public sealed record FormatView(string Id, string File);

    /// <summary>The declared package-local formats with their file paths; empty below 25 and when none.</summary>
    public static IReadOnlyList<FormatView> ReadFormats(JsonElement manifest)
    {
        if (!TryGetProperty(manifest, "formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<FormatView>();
        }

        return formats.EnumerateArray()
            .Where(entry => entry.ValueKind == JsonValueKind.Object)
            .Select(entry => new FormatView(ReadString(entry, "id") ?? string.Empty, ReadString(entry, "file") ?? string.Empty))
            .Where(entry => entry.Id.Length > 0)
            .ToList();
    }

    /// <summary>The manifest's schema ids — the package's output-contract choices.</summary>
    public static IReadOnlyList<string> ReadSchemaIds(JsonElement manifest)
    {
        if (!TryGetProperty(manifest, "schemas", out var schemas) || schemas.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return schemas.EnumerateObject().Select(entry => entry.Name).ToList();
    }

    /// <summary>The declared schemas with their file paths, which viewing and removal need.</summary>
    public static IReadOnlyList<SchemaView> ReadSchemas(JsonElement manifest)
    {
        if (!TryGetProperty(manifest, "schemas", out var schemas) || schemas.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<SchemaView>();
        }

        return schemas.EnumerateObject()
            .Select(entry => new SchemaView(entry.Name, entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() ?? string.Empty : string.Empty))
            .ToList();
    }

    /// <summary>The declared shared preludes with their file paths, which viewing, editing and removal need.</summary>
    public static IReadOnlyList<PreludeView> ReadPreludes(JsonElement manifest)
    {
        if (!TryGetProperty(manifest, "preludes", out var preludes) || preludes.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<PreludeView>();
        }

        return preludes.EnumerateObject()
            .Select(entry => new PreludeView(entry.Name, entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() ?? string.Empty : string.Empty))
            .ToList();
    }

    /// <summary>The package's templating engine + pinned version, or null when absent — shown read-only.</summary>
    public static TemplatingView? ReadTemplating(JsonElement manifest)
    {
        if (!TryGetProperty(manifest, "templating", out var templating) || templating.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var engine = ReadString(templating, "engine");
        var engineVersion = ReadString(templating, "engineVersion");
        return engine is null && engineVersion is null
            ? null
            : new TemplatingView(engine ?? string.Empty, engineVersion ?? string.Empty);
    }

    /// <summary>
    /// Scalar data entries: values of the data map that are not directories.
    /// Ids only — what the binding dropdown needs.
    /// </summary>
    public static IReadOnlyList<string> ReadScalars(JsonElement manifest, string key = DataKey) =>
        ReadScalarEntries(manifest, key).Select(entry => entry.Id).ToList();

    /// <summary>The ≤24 table: <c>data</c>.</summary>
    public const string DataKey = "data";

    /// <summary>
    /// The 25 table (#1008): <c>snippets</c> — the same shape as data (id → file,
    /// or folder → "dir/" with an index.json), read as <c>{{ snippet.&lt;id&gt;.content }}</c>.
    /// </summary>
    public const string SnippetsKey = "snippets";

    /// <summary>
    /// The same entries with their file paths, which editing needs: a scalar's
    /// value *is* the file at that path, so the path is how the editor loads
    /// and writes it.
    ///
    /// The trailing slash is the whole discriminator — `WorkflowDataResolver`
    /// reads a path ending in '/' as a collection and anything else as a
    /// value. Anything writing these paths has to get that one character
    /// right, in both directions.
    /// </summary>
    public static IReadOnlyList<ScalarView> ReadScalarEntries(JsonElement manifest, string key = DataKey)
    {
        var scalars = new List<ScalarView>();

        if (!TryGetProperty(manifest, key, out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return scalars;
        }

        foreach (var entry in data.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { } value && !value.EndsWith('/'))
            {
                scalars.Add(new ScalarView(entry.Name, value));
            }
        }

        return scalars;
    }

    /// <summary>
    /// v26 (#1163): the same entries with each file's header read — a standalone snippet's
    /// label and <c>if</c> live in its file at 26 (null when the file has no parseable header).
    /// </summary>
    public static IReadOnlyList<ScalarView> ReadScalarEntries(JsonElement manifest, IReadOnlyDictionary<string, string> files, string key = DataKey) =>
        ReadScalarEntries(manifest, key)
            .Select(scalar => ReadHeader(Consultologist.PackageFormat.WorkflowTextKind.Snippet, scalar.Path, files) is { Refused: false, Header: { } header }
                ? scalar with { Label = header.Label, If = string.IsNullOrWhiteSpace(header.If) ? null : header.If }
                : scalar)
            .ToList();

    /// <summary>One single-value data entry: the bindable id and the file holding its text; v26 (#1163) its header's label and <c>if</c>.</summary>
    public sealed record ScalarView(string Id, string Path, string? Label = null, string? If = null);

    /// <summary>v26 (#1163): one file's header, split and parsed with its declared kind; null when the file is absent.</summary>
    public static TextHeaderView? ReadHeader(Consultologist.PackageFormat.WorkflowTextKind kind, string path, IReadOnlyDictionary<string, string> files)
    {
        if (!files.TryGetValue(path, out var text))
        {
            return null;
        }

        var (headerJson, body) = Consultologist.PackageFormat.WorkflowTextHeaders.Split(text);
        if (headerJson is null)
        {
            return new TextHeaderView(path, kind, null, new[] { Consultologist.PackageFormat.WorkflowTextHeaders.MissingHeaderSentence(kind, path) }, text);
        }

        var errors = new List<string>();
        var header = Consultologist.PackageFormat.WorkflowTextHeaders.Parse(kind, path, headerJson, errors);
        return header is null || errors.Count > 0
            ? new TextHeaderView(path, kind, header, errors, text)
            : new TextHeaderView(path, kind, header, errors, body);
    }

    /// <summary>
    /// v26 (#1163): every snippet and macro file the manifest names — the scalars, each folder's
    /// items (the 26 list index) and the macros map's files and folder items — with its header
    /// read by its declared kind, keyed by path. Empty below 26 (no map holds a headed file).
    /// </summary>
    public static IReadOnlyDictionary<string, TextHeaderView> ReadTextHeaders(JsonElement manifest, IReadOnlyDictionary<string, string> files)
    {
        var headers = new Dictionary<string, TextHeaderView>(StringComparer.Ordinal);

        void Take(Consultologist.PackageFormat.WorkflowTextKind kind, string path)
        {
            if (!headers.ContainsKey(path) && ReadHeader(kind, path, files) is { } view)
            {
                headers[path] = view;
            }
        }

        foreach (var scalar in ReadScalarEntries(manifest, SnippetsKey))
        {
            Take(Consultologist.PackageFormat.WorkflowTextKind.Snippet, scalar.Path);
        }

        foreach (var collection in ReadCollections(manifest, files, SnippetsKey))
        {
            foreach (var item in collection.Items)
            {
                Take(Consultologist.PackageFormat.WorkflowTextKind.Snippet, item.File);
            }
        }

        if (TryGetProperty(manifest, "macros", out var macros) && macros.ValueKind == JsonValueKind.Object)
        {
            foreach (var macro in ReadMacros(manifest))
            {
                Take(Consultologist.PackageFormat.WorkflowTextKind.Macro, macro.File);
            }

            foreach (var collection in ReadCollections(manifest, files, "macros", Consultologist.PackageFormat.WorkflowTextKind.Macro))
            {
                foreach (var item in collection.Items)
                {
                    Take(Consultologist.PackageFormat.WorkflowTextKind.Macro, item.File);
                }
            }
        }

        return headers;
    }

    /// <summary>
    /// The manifest's collections joined with each directory's index.json (from
    /// the files dict) so the editor can render one card per item.
    /// </summary>
    public static IReadOnlyList<CollectionView> ReadCollections(JsonElement manifest, IReadOnlyDictionary<string, string> files, string key = DataKey,
        Consultologist.PackageFormat.WorkflowTextKind kind = Consultologist.PackageFormat.WorkflowTextKind.Snippet)
    {
        var collections = new List<CollectionView>();

        if (!TryGetProperty(manifest, key, out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return collections;
        }

        foreach (var entry in data.EnumerateObject())
        {
            var directory = entry.Value.GetString();

            if (string.IsNullOrWhiteSpace(directory) || !directory.EndsWith('/'))
            {
                continue;
            }

            var items = new List<DataItemView>();

            if (files.TryGetValue(directory + "index.json", out var indexJson))
            {
                try
                {
                    using var index = JsonDocument.Parse(indexJson);

                    // v26 (#1160): a folder's index.json is an ordered list [{ id, file }];
                    // name and the gate live in the item file's header (design § 5). #1163:
                    // the header is read by the folder's declared kind (a macro folder's item
                    // carries its instruction, optional and default); the write target is the
                    // header, composed by the editor.
                    if (index.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in index.RootElement.EnumerateArray())
                        {
                            var file = item.ValueKind == JsonValueKind.Object ? ReadString(item, "file") : null;
                            if (string.IsNullOrWhiteSpace(file))
                            {
                                continue;
                            }

                            var id = ReadString(item, "id") ?? file;
                            var header = ReadHeader(kind, directory + file, files) is { Refused: false } view ? view.Header : null;
                            items.Add(new DataItemView(id, header?.Label ?? id, directory + file, string.IsNullOrWhiteSpace(header?.If) ? null : header!.If,
                                header?.Instruction, header?.Optional, header?.Default));
                        }
                    }
                    else if (index.RootElement.ValueKind == JsonValueKind.Object
                        && index.RootElement.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemsElement.EnumerateArray())
                        {
                            var file = ReadString(item, "file");

                            if (string.IsNullOrWhiteSpace(file))
                            {
                                continue;
                            }

                            var id = ReadString(item, "id") ?? file;
                            var when = ReadString(item, "when");
                            items.Add(new DataItemView(id, ReadString(item, "name") ?? id, directory + file, string.IsNullOrWhiteSpace(when) ? null : when));
                        }
                    }
                }
                catch (JsonException)
                {
                    // An unparseable index renders as an empty collection; the
                    // publish validator is the authority on integrity.
                }
            }

            collections.Add(new CollectionView(entry.Name, directory, items, kind));
        }

        return collections;
    }

    /// <summary>The fork's parent ref, or null for root packages.</summary>
    public static string? ReadDerivedFrom(JsonElement manifest) => ReadString(manifest, "derivedFrom");

    /// <summary>v9 § 4 (#432): the package's title, or null when it has none — the ref is the stated fallback.</summary>
    public static string? ReadTitle(JsonElement manifest) => ReadString(manifest, "title");

    public static string? ReadDescription(JsonElement manifest) => ReadString(manifest, "description");

    /// <summary>#453: the package's tags in authored order; null when the section is absent (pre-v9), empty when it states none.</summary>
    public static IReadOnlyList<string>? ReadTags(JsonElement manifest) => ReadStringArray(manifest, "tags");

    /// <summary>The raw result reference ("node:x"), for the deliverable selector.</summary>
    public static string? ReadResultRef(JsonElement manifest) => ReadString(manifest, "result");

    /// <summary>
    /// The declared inputs (specVersion 7). Empty for v5/v6, whose single
    /// input is the frozen consult_draft convention rather than a declaration.
    /// `required` defaults true when absent, matching the server's spec record.
    /// </summary>
    public static IReadOnlyList<InputView> ReadInputs(JsonElement manifest)
    {
        var inputs = new List<InputView>();

        if (!TryGetProperty(manifest, "inputs", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return inputs;
        }

        foreach (var input in array.EnumerateArray())
        {
            var id = ReadString(input, "id") ?? string.Empty;
            var required = !TryGetProperty(input, "required", out var requiredElement)
                || requiredElement.ValueKind != JsonValueKind.False;

            var (items, fields, values) = Hoist(ReadItems(input), ReadFields(input), ReadStringArray(input, "values"));

            // #745: a union `type: [a, b]` (#729) is a JSON array — read the arms,
            // keeping the first as the primary type the structure editors key off.
            // A string `type` yields null arms and the single-string read.
            var typeArms = ReadStringArray(input, "type");

            inputs.Add(new InputView(
                id,
                ReadString(input, "label") ?? id,
                required,
                typeArms is { Count: > 0 } ? typeArms[0] : ReadString(input, "type"),
                values,
                items,
                fields,
                typeArms,
                ReadString(input, "expectedContent")));
        }

        return inputs;
    }

    /// <summary>
    /// v10 (#498): a spec-form element that is an object or an enum is held
    /// the v9 way — its fields or values on the input beside a bare element —
    /// so one place holds them and the composer writes the v9 bytes. An
    /// element that is an array keeps its own. When the input declares fields
    /// or values of its own as well, those win and the element's are dropped:
    /// the server resolves the same declaration either way.
    /// </summary>
    private static (ElementView? Items, IReadOnlyList<FieldView>? Fields, IReadOnlyList<string>? Values) Hoist(
        ElementView? items, IReadOnlyList<FieldView>? fields, IReadOnlyList<string>? values)
    {
        if (items is null || items.IsBare || items.Type == WorkflowInputTypes.Array)
        {
            return (items, fields, values);
        }

        return (new ElementView(items.Type), fields ?? items.Fields, values ?? items.Values);
    }

    /// <summary>
    /// `items`: a type name (v9), or since v10 (#492) an element spec object
    /// with a type and its own items, fields and values, read to any depth.
    /// </summary>
    private static ElementView? ReadItems(JsonElement owner)
    {
        if (!TryGetProperty(owner, "items", out var items))
        {
            return null;
        }

        return items.ValueKind switch
        {
            JsonValueKind.String => new ElementView(items.GetString() ?? WorkflowInputTypes.Text),
            JsonValueKind.Object => new ElementView(
                ReadString(items, "type") ?? WorkflowInputTypes.Text,
                ReadItems(items),
                ReadFields(items),
                ReadStringArray(items, "values")),
            _ => null
        };
    }

    /// <summary>The declared fields of an object, an array of objects or an element (v9; recursive since v10); null when absent.</summary>
    private static IReadOnlyList<FieldView>? ReadFields(JsonElement owner)
    {
        if (!TryGetProperty(owner, "fields", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var fields = new List<FieldView>();

        foreach (var field in array.EnumerateArray())
        {
            var id = ReadString(field, "id") ?? string.Empty;
            var required = !TryGetProperty(field, "required", out var requiredElement)
                || requiredElement.ValueKind != JsonValueKind.False;

            fields.Add(new FieldView(
                id,
                ReadString(field, "label") ?? id,
                required,
                ReadString(field, "type"),
                ReadStringArray(field, "values"),
                // v10 (#498): a field's own structure, to any depth.
                ReadItems(field),
                ReadFields(field)));
        }

        return fields;
    }

    /// <summary>
    /// The declared deliverables (specVersion 7). Empty when the package uses
    /// the string result form, which stays valid as one-entry sugar.
    /// </summary>
    public static IReadOnlyList<ResultView> ReadResults(JsonElement manifest)
    {
        var results = new List<ResultView>();

        // v25 (A3 #998): results[] is spelled deliverables[] — the same record shape.
        // #1066: the worker writes the off-version key as null; TryGetProperty
        // reads a null as absent, so the second key is reached.
        if ((!TryGetProperty(manifest, "results", out var array) && !TryGetProperty(manifest, "deliverables", out array)) || array.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var result in array.EnumerateArray())
        {
            var id = ReadString(result, "id") ?? string.Empty;
            results.Add(new ResultView(
                id,
                ReadString(result, "node") ?? string.Empty,
                ReadString(result, "label") ?? id,
                ReadString(result, "when"),
                ReadMacroEntries(result),
                ReadBool(result, "signature"),
                ReadString(result, "check"),
                // v26 (#1161): sources and the if gate.
                ReadStringArray(result, "sources"),
                ReadString(result, "if")));
        }

        return results;
    }

    /// <summary>The declared macros (v11 § 4). Empty below 11.</summary>
    /// <summary>
    /// v26 (#1163): the macros with each file's header read — label, instruction, optional,
    /// default and the macro's own <c>if</c> live in the file at 26. A folder entry is not a
    /// macro row: <see cref="ReadCollections"/> over the macros key reads it. Below 26 (a list)
    /// the manifest's facts, as <see cref="ReadMacros(JsonElement)"/> reads them.
    /// </summary>
    public static IReadOnlyList<MacroView> ReadMacros(JsonElement manifest, IReadOnlyDictionary<string, string> files)
    {
        if (!TryGetProperty(manifest, "macros", out var table) || table.ValueKind != JsonValueKind.Object)
        {
            return ReadMacros(manifest);
        }

        var macros = new List<MacroView>();
        foreach (var macro in ReadMacros(manifest))
        {
            if (macro.File.EndsWith('/'))
            {
                continue;
            }

            macros.Add(ReadHeader(Consultologist.PackageFormat.WorkflowTextKind.Macro, macro.File, files) is { Refused: false, Header: { } header }
                ? macro with
                {
                    Label = string.IsNullOrWhiteSpace(header.Label) ? macro.Id : header.Label!,
                    Instruction = header.Instruction,
                    Optional = header.Optional,
                    Default = header.Default,
                    If = string.IsNullOrWhiteSpace(header.If) ? null : header.If,
                }
                : macro);
        }

        return macros;
    }

    public static IReadOnlyList<MacroView> ReadMacros(JsonElement manifest)
    {
        var macros = new List<MacroView>();

        if (!TryGetProperty(manifest, "macros", out var array))
        {
            return macros;
        }

        // #1099 (v26 design § 5.3): at 26 macros is a map id → file (a trailing '/' a
        // folder); the header carries the rest — the view holds the id and the path.
        if (array.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in array.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { } file)
                {
                    macros.Add(new MacroView(entry.Name, entry.Name, file, null, null, null));
                }
            }

            return macros;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return macros;
        }

        foreach (var macro in array.EnumerateArray())
        {
            var id = ReadString(macro, "id") ?? string.Empty;
            macros.Add(new MacroView(
                id,
                ReadString(macro, "label") ?? id,
                ReadString(macro, "file") ?? string.Empty,
                ReadBool(macro, "optional"),
                ReadBool(macro, "default"),
                ReadString(macro, "instruction")));
        }

        return macros;
    }

    /// <summary>
    /// The nodes a package names as deliverables: the v7 results list, or the
    /// v5/v6 single result string. A v7 package declaring results carries no
    /// result string, so reading only the latter would mark no node at all.
    /// </summary>
    private static IReadOnlySet<string> ReadResultNodeIds(JsonElement manifest)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        // v25 (A3 #998): results[] is spelled deliverables[] — the same shape.
        // #1066: a null-valued results key (the worker's shape at 25) is absent.
        if ((TryGetProperty(manifest, "results", out var results) || TryGetProperty(manifest, "deliverables", out results))
            && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in results.EnumerateArray())
            {
                if (StripNodePrefix(ReadString(result, "node")) is { } nodeId)
                {
                    ids.Add(nodeId);
                }

                // v26 (#1161): every source is a root.
                foreach (var source in ReadStringArray(result, "sources") ?? Array.Empty<string>())
                {
                    if (StripNodePrefix(source) is { } sourceId)
                    {
                        ids.Add(sourceId);
                    }
                }
            }

            return ids;
        }

        if (StripNodePrefix(ReadString(manifest, "result")) is { } singleNodeId)
        {
            ids.Add(singleNodeId);
        }

        return ids;
    }

    // node:x below 25, node.x at 25 (#1037).
    private static string? StripNodePrefix(string? reference) =>
        TemplateObjectReference.NodeIdOf(reference) ?? reference;

    private static BindingView ReadBinding(string variable, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new BindingView(variable, value.GetString() ?? string.Empty, null),
        JsonValueKind.Object => new BindingView(variable, ReadString(value, "from") ?? string.Empty, ReadString(value, "as")),
        _ => new BindingView(variable, value.ToString(), null)
    };

    /// <summary>
    /// The manifest element's property casing depends on the server's response
    /// serializer (the Functions worker default writes PascalCase; repo manifest
    /// sources are camelCase) — the reader accepts either, like the server's own
    /// case-insensitive parsing does.
    ///
    /// #1066: a key whose value is JSON null is an ABSENT key. The worker's
    /// default serializer writes every member of the manifest record, so a
    /// trailing optional the manifest never had arrives as <c>"Results": null</c>
    /// beside <c>"Deliverables": [ … ]</c> — and a lookup that stopped at the
    /// first key it found read no deliverables at all. The record's own writer
    /// omits nulls (WorkflowPackageManifestJson.WriteOptions), and JsonNode, which
    /// compose reads through, maps a JSON null to C# null too; this makes the
    /// three agree. A null in one casing does not stop the search in the other.
    /// </summary>
    private static bool TryGetProperty(JsonElement element, string camelName, out JsonElement value)
    {
        if (element.TryGetProperty(camelName, out value) && value.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        var pascalName = char.ToUpperInvariant(camelName[0]) + camelName[1..];
        return element.TryGetProperty(pascalName, out value) && value.ValueKind != JsonValueKind.Null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        TryGetProperty(element, property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A boolean property; null when absent or not a JSON boolean.</summary>
    private static bool? ReadBool(JsonElement element, string property) =>
        TryGetProperty(element, property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.ValueKind == JsonValueKind.True
            : null;

    /// <summary>
    /// A deliverable's macro entries, whole (v12 rung (e), #621). Reading the
    /// id alone was #398's bug class waiting to happen: the composer rewrites
    /// the results array from these views, so anything unread here —
    /// before/after/when — vanished on the first Documents-pane edit.
    /// </summary>
    private static IReadOnlyList<ResultMacroView>? ReadMacroEntries(JsonElement result)
    {
        if (!TryGetProperty(result, "macros", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entries = new List<ResultMacroView>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                entries.Add(new ResultMacroView(item.GetString()!));
            }
            else if (item.ValueKind == JsonValueKind.Object && TryGetProperty(item, "id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                entries.Add(new ResultMacroView(
                    id.GetString()!,
                    ReadString(item, "before"),
                    ReadString(item, "after"),
                    ReadString(item, "when"),
                    ReadString(item, "forItem"),
                    ReadBool(item, "slot"),
                    ReadString(item, "at"),
                    ReadString(item, "if")));
            }
        }

        return entries;
    }

    /// <summary>
    /// v23 (#955): a node's macro attachments — a bare id string (before, always)
    /// or a placement object { id, at, when, optional }.
    /// </summary>
    private static IReadOnlyList<NodeMacroView>? ReadNodeMacroEntries(JsonElement node)
    {
        if (!TryGetProperty(node, "macros", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entries = new List<NodeMacroView>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                entries.Add(new NodeMacroView(item.GetString()!));
            }
            else if (item.ValueKind == JsonValueKind.Object && TryGetProperty(item, "id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                entries.Add(new NodeMacroView(
                    id.GetString()!,
                    ReadString(item, "at"),
                    ReadString(item, "when"),
                    ReadBool(item, "optional"),
                    ReadString(item, "forItem"),
                    ReadString(item, "if")));
            }
        }

        return entries;
    }

    /// <summary>An enum input's declared values; null when the property is absent.</summary>
    private static IReadOnlyList<string>? ReadStringArray(JsonElement element, string property)
    {
        if (!TryGetProperty(element, property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }
}
