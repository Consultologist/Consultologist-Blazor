namespace Consultologist.PackageFormat;

/// <summary>
/// #1100 (package-format-v26-design.md § 1): the flow nodes of a 26 manifest
/// resolved — one walk's worth of structure, shared by the validator (which
/// keeps the sentences) and the derived-edges walk the engine and the editor
/// run (which discards them): a node carrying <c>for</c>, <c>if</c> or
/// <c>case</c>, its bodies of plain nodes attached by id, its returned node,
/// and — for a loop — the fan its body runs under. B3 #1002's rule holds: the
/// scheduler never sees a graph the validator did not. #1234 (v27 design § 1.1,
/// § 4): at 27 a loop's body may hold an if/case node, whose branches' nodes are
/// body nodes of the loop too — the owner map keeps two levels (a branch node →
/// its flow node → the loop); a branch holds plain nodes (one level).
/// </summary>
internal static class WorkflowFlowNodes
{
    public enum Kind { For, If, Case }

    /// <summary>One body of a flow node: the loop's, an if's then/else, a case's branch (with its values) or else.</summary>
    public sealed record Body(string Label, IReadOnlyList<string> Nodes, string? Ret, IReadOnlyList<string>? When)
    {
        /// <summary>The returned node's id, when <c>ret</c> is spelled <c>node.&lt;id&gt;</c>.</summary>
        public string? RetNodeId => TemplateObjectReference.IsBare(Ret, TemplateObjects.Node, out var id) ? id : null;
    }

    /// <summary>A flow node resolved: its kind, bodies, and — for a loop — the fan its body runs under.</summary>
    public sealed record Flow(
        WorkflowNodeSpec Node,
        Kind Kind,
        IReadOnlyList<Body> Bodies,
        TemplateFan? Fan,
        WorkflowDataCollection? Folder,
        string? Expression)
    {
        /// <summary>"Loop 'l'" for a for node, "Node 'x'" for an if or case node — the sentences' subject.</summary>
        public string Subject => Kind == Kind.For ? $"Loop '{Node.Id}'" : $"Node '{Node.Id}'";

        /// <summary>"loop 'l'" / "node 'x'" — where a body node runs.</summary>
        public string Inside => Kind == Kind.For ? $"loop '{Node.Id}'" : $"node '{Node.Id}'";

        public IEnumerable<string> BodyNodeIds => Bodies.SelectMany(b => b.Nodes);

        public string BodySubject(Body body) => Kind == Kind.For ? Subject : $"{Subject} {body.Label}";

        /// <summary>
        /// #1154: a loop's collection as a 25 <c>forEach</c> spells it —
        /// <c>snippet.&lt;folder&gt;</c> or <c>input.&lt;id&gt;</c> — the fan string the
        /// engine's skeleton keys by, so a body node fans as a 25 node did; null
        /// for an if or case node.
        /// </summary>
        public string? Collection =>
            Kind == Kind.For && Node.For is { } text && text.StartsWith(LoopPrefix, StringComparison.Ordinal) && text.Length > LoopPrefix.Length
                ? text[LoopPrefix.Length..]
                : null;

        /// <summary>#1154: the node a case switches on (<c>node.&lt;id&gt;.value</c>), or null — an enum input, or not a case.</summary>
        public string? SubjectNodeId => Kind == Kind.Case && Expression is { } subject ? CaseNodeSubjectOf(subject) : null;

        public TemplateFlowKind TemplateKind => Kind switch { Kind.For => TemplateFlowKind.For, Kind.If => TemplateFlowKind.If, _ => TemplateFlowKind.Case };
    }

    /// <summary>The flow nodes by id, and each body node's owner (the flow node it runs inside).</summary>
    public sealed record Resolved(IReadOnlyDictionary<string, Flow> Flows, IReadOnlyDictionary<string, string> Owner)
    {
        public static readonly Resolved None = new(
            new Dictionary<string, Flow>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

        public bool IsLoop(string id) => Flows.TryGetValue(id, out var flow) && flow.Kind == Kind.For;

        /// <summary>#1234: a flow node listed in a loop's body — an if/case decided per item at 27.</summary>
        public bool IsInner(string flowId) => Flows.ContainsKey(flowId) && Owner.ContainsKey(flowId);

        /// <summary>
        /// #1234 (v27 design § 4): the loop a node runs in — its owner when that is
        /// a loop, its owner's owner when it runs in a branch of an inner flow node
        /// (two levels; a branch holds plain nodes); null for a top-level node and
        /// for a node of a top-level if/case.
        /// </summary>
        public string? LoopOf(string nodeId)
        {
            var id = nodeId;

            for (var depth = 0; depth < 2 && Owner.TryGetValue(id, out var ownerId); depth++)
            {
                if (IsLoop(ownerId))
                {
                    return ownerId;
                }

                id = ownerId;
            }

            return null;
        }

        /// <summary>The walker's view of the bodies: body node id → where it runs (the chain is read through the map — a scope's own entry names its owner).</summary>
        public IReadOnlyDictionary<string, TemplateBodyScope> BodyScopes =>
            Owner.ToDictionary(pair => pair.Key, pair => new TemplateBodyScope(pair.Value, IsLoop(pair.Value)), StringComparer.Ordinal);

        /// <summary>The fan a node's template runs under: its loop's (through its flow node at 27), or none.</summary>
        public (TemplateFan? Fan, WorkflowDataCollection? Folder) FanOf(string nodeId) =>
            LoopOf(nodeId) is { } loopId && Flows.TryGetValue(loopId, out var loop)
                ? (loop.Fan, loop.Folder)
                : (null, null);

        /// <summary>#1234: the flow nodes in the order their shapes are built — an if/case before a loop, so a loop returning one finds its branch shape (§ 1.5).</summary>
        public IEnumerable<Flow> InShapeOrder => Flows.Values.OrderBy(f => f.Kind == Kind.For ? 1 : 0);
    }

    /// <summary>
    /// Every node carrying <c>for</c>, <c>if</c> or <c>case</c>: one flow key, no
    /// format or prompt (a flow node is structural), the keys its kind carries,
    /// a loop's collection resolved to the fan its body runs under, each body a
    /// set of declared plain nodes attached to exactly one body, and <c>ret</c>
    /// required on every body and naming one of its nodes (§ 1.1–1.2).
    /// </summary>
    public static Resolved Resolve(
        WorkflowPackageManifest manifest,
        WorkflowPackageData snippets,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        List<string> errors)
    {
        var declared = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var flows = new Dictionary<string, Flow>(StringComparer.Ordinal);

        foreach (var node in declared.Values)
        {
            var keys = new List<string>();
            if (node.For != null) keys.Add("for");
            if (node.If != null) keys.Add("if");
            if (node.Case != null) keys.Add("case");

            if (keys.Count == 0)
            {
                foreach (var (name, present) in new[] { ("nodes", node.Nodes != null), ("ret", node.Ret != null), ("then", node.Then != null), ("else", node.Else != null), ("branches", node.Branches != null) })
                {
                    if (present)
                    {
                        errors.Add($"Node '{node.Id}' declares {name}, which belongs to a flow node; declare for, if or case.");
                    }
                }

                continue;
            }

            if (keys.Count > 1)
            {
                errors.Add($"Node '{node.Id}' declares {string.Join(", ", keys)}; a node is one flow node — for, if or case.");
                continue;
            }

            var kind = keys[0] switch { "for" => Kind.For, "if" => Kind.If, _ => Kind.Case };

            foreach (var (name, present) in new[]
            {
                ("format", node.Format != null), ("prompt", node.Prompt != null), ("with", node.With != null), ("onInvalid", node.OnInvalid != null),
                ("failIfEmpty", node.FailIfEmpty != null), ("reproducible", node.Reproducible != null), ("macros", node.Macros != null),
            })
            {
                if (present)
                {
                    errors.Add($"Node '{node.Id}' declares {keys[0]} and {name}; a flow node is structural.");
                }
            }

            var foreign = kind switch
            {
                Kind.For => new[] { ("then", node.Then != null), ("else", node.Else != null), ("branches", node.Branches != null) },
                Kind.If => new[] { ("nodes", node.Nodes != null), ("ret", node.Ret != null), ("branches", node.Branches != null) },
                _ => new[] { ("nodes", node.Nodes != null), ("ret", node.Ret != null), ("then", node.Then != null) },
            };
            var kindWord = kind switch { Kind.For => "a for", Kind.If => "an if", _ => "a case" };

            foreach (var (name, present) in foreign)
            {
                if (present)
                {
                    errors.Add($"Node '{node.Id}' declares {name}, which {kindWord} node does not carry.");
                }
            }

            var bodies = new List<Body>();
            TemplateFan? fan = null;
            WorkflowDataCollection? folder = null;
            string? expression = null;

            switch (kind)
            {
                case Kind.For:
                    (fan, folder) = ResolveLoopCollection(node.Id, node.For!, snippets, inputsById, errors);
                    if (node.Nodes is not { Count: > 0 })
                    {
                        errors.Add($"Loop '{node.Id}' has no body; name the nodes that run per item in nodes.");
                    }

                    bodies.Add(new Body("body", node.Nodes ?? new List<string>(), node.Ret, null));
                    break;

                case Kind.If:
                    expression = node.If;
                    if (node.Then is null)
                    {
                        errors.Add($"Node '{node.Id}' declares if without then; an if node runs a then body.");
                    }
                    else
                    {
                        bodies.Add(new Body("then", node.Then.Nodes ?? new List<string>(), node.Then.Ret, null));
                    }

                    if (node.Else is not null)
                    {
                        bodies.Add(new Body("else", node.Else.Nodes ?? new List<string>(), node.Else.Ret, null));
                    }

                    break;

                default:
                    expression = node.Case;
                    if (node.Branches is not { Count: > 0 })
                    {
                        errors.Add($"Node '{node.Id}' declares case without branches; a case node runs one branch per set of values.");
                    }

                    var index = 0;
                    foreach (var branch in node.Branches ?? new List<WorkflowCaseBranchSpec>())
                    {
                        index++;
                        if (branch.When is not { Count: > 0 })
                        {
                            errors.Add($"Node '{node.Id}' branch {index} lists no value; a branch's when names one or more values.");
                        }

                        bodies.Add(new Body($"branch {index}", branch.Nodes ?? new List<string>(), branch.Ret, branch.When));
                    }

                    if (node.Else is not null)
                    {
                        bodies.Add(new Body("else", node.Else.Nodes ?? new List<string>(), node.Else.Ret, null));
                    }

                    break;
            }

            flows[node.Id] = new Flow(node, kind, bodies, fan, folder, expression);
        }

        // The bodies: declared nodes, each attached once; at 26 a body holds
        // plain nodes (nesting waits, § 6); at 27 a loop's body may hold an
        // if/case, whose branches hold plain nodes (#1234, v27 design § 1.1);
        // ret required and a member.
        var at27 = manifest.SpecVersion >= 27;
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        // The loop each if/case is listed in, read before ownership settles so
        // the sentences below do not depend on the manifest's node order.
        var loopListing = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var loop in flows.Values.Where(f => f.Kind == Kind.For))
        {
            foreach (var id in loop.BodyNodeIds)
            {
                if (flows.TryGetValue(id, out var listed) && listed.Kind != Kind.For)
                {
                    loopListing.TryAdd(id, loop.Node.Id);
                }
            }
        }

        foreach (var flow in flows.Values)
        {
            foreach (var body in flow.Bodies)
            {
                var subject = flow.BodySubject(body);
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var id in body.Nodes)
                {
                    if (!seen.Add(id))
                    {
                        errors.Add($"{subject} lists node '{id}' twice; a body is a set.");
                        continue;
                    }

                    if (!declared.ContainsKey(id))
                    {
                        errors.Add($"{subject} names node '{id}', which the package does not declare.");
                        continue;
                    }

                    if (flows.TryGetValue(id, out var inner))
                    {
                        if (inner.Kind == Kind.For)
                        {
                            errors.Add($"Loop '{id}' runs inside {flow.Inside}; nesting waits for the scheduler redesign.");
                            continue;
                        }

                        if (flow.Kind != Kind.For)
                        {
                            // One level (§ 1.1): a branch holds plain nodes — inside a loop
                            // naming the chain; at the top level the 26 sentence below 27.
                            errors.Add(!at27
                                ? $"Node '{id}' runs inside {flow.Inside}; at specVersion 26 a body holds plain nodes — a flow node inside a body is a later issue."
                                : loopListing.TryGetValue(flow.Node.Id, out var loopId)
                                    ? $"Node '{id}' runs inside {flow.Inside}, which runs inside loop '{loopId}'; at specVersion {manifest.SpecVersion} a branch holds plain nodes."
                                    : $"Node '{id}' runs inside {flow.Inside}; at specVersion {manifest.SpecVersion} a branch holds plain nodes.");
                            continue;
                        }

                        if (!at27)
                        {
                            errors.Add($"Node '{id}' runs inside {flow.Inside}; a flow node inside a loop requires specVersion 27.");
                            continue;
                        }

                        // At 27 the if/case is a body node of the loop; its own bodies
                        // attach their nodes to it below (the second level).
                    }

                    if (owner.TryGetValue(id, out var first))
                    {
                        // #1234 (§ 1.1): a branch node listed in the loop's nodes[] too is
                        // attached twice — named by the pair, whichever body came first.
                        var (innerId, loopId) =
                            flow.Kind == Kind.For && flows[first].Kind != Kind.For && loopListing.GetValueOrDefault(first) == flow.Node.Id ? (first, flow.Node.Id)
                            : flow.Kind != Kind.For && flows[first].Kind == Kind.For && loopListing.GetValueOrDefault(flow.Node.Id) == first ? (flow.Node.Id, first)
                            : (null, null);
                        errors.Add(innerId is not null
                            ? $"Node '{id}' is attached to '{innerId}' and to loop '{loopId}'; a branch node is attached once, through its flow node."
                            : $"Node '{id}' runs inside {flows[first].Inside} and inside {flow.Inside}; a node runs in one body.");
                        continue;
                    }

                    owner[id] = flow.Node.Id;
                }

                if (body.Ret is null)
                {
                    errors.Add($"{subject} returns nothing; name the body node the outside reads with ret.");
                }
                else if (body.RetNodeId is null)
                {
                    errors.Add($"{subject} ret '{body.Ret}' must be node.<id>.");
                }
                else if (!body.Nodes.Contains(body.RetNodeId, StringComparer.Ordinal))
                {
                    errors.Add($"{subject} returns node.{body.RetNodeId}, which is not in its body (nodes: {string.Join(", ", body.Nodes)}).");
                }
            }
        }

        return new Resolved(flows, owner);
    }

    /// <summary><c>node.&lt;id&gt;.value</c> → the id; anything else → null. A case subject names a node this way or an enum input.</summary>
    public static string? CaseNodeSubjectOf(string subject) =>
        TemplateObjectReference.TryParse(subject, out var obj, out var id, out var rest)
            && obj == TemplateObjects.Node
            && rest.TrimStart('.') == TemplateMembers.Value
            ? id
            : null;

    /// <summary>#1234 (v27 design § 1.2): <c>item.value.&lt;field&gt;</c> → the field; anything else → null. Inside a loop over an array input a case switches on an enum field of the item.</summary>
    public static string? CaseItemSubjectOf(string subject) =>
        TemplateObjectReference.TryParse(subject, out var obj, out var id, out var rest)
            && obj == TemplateObjects.Item
            && id == TemplateMembers.Value
            && rest.Length > 1
            && rest.IndexOf('.', 1) < 0
            ? rest[1..]
            : null;

    private const string LoopPrefix = "item in ";

    /// <summary>#1154: a <c>for</c> text's collection as a 25 <c>forEach</c> spells it, or null when the text is not the grammar.</summary>
    public static string? CollectionOf(string? @for) =>
        @for is { } text && text.StartsWith(LoopPrefix, StringComparison.Ordinal) && text.Length > LoopPrefix.Length ? text[LoopPrefix.Length..] : null;

    /// <summary><c>for: "item in snippet.&lt;folder&gt;"</c> or <c>"item in input.&lt;id&gt;"</c> (an array); the fan the loop's body runs under.</summary>
    private static (TemplateFan? Fan, WorkflowDataCollection? Folder) ResolveLoopCollection(
        string loopId,
        string text,
        WorkflowPackageData snippets,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        List<string> errors)
    {
        const string prefix = LoopPrefix;
        var grammar = $"Loop '{loopId}' iterates '{text}'; a loop iterates \"item in snippet.<folder>\" or \"item in input.<id>\".";

        if (!text.StartsWith(prefix, StringComparison.Ordinal) || text.Length == prefix.Length)
        {
            errors.Add(grammar);
            return (null, null);
        }

        var reference = text[prefix.Length..];

        if (reference.Contains(' ') || TemplateObjectReference.IsColonSpelling(reference)
            || !TemplateObjectReference.TryParse(reference, out var obj, out var id, out var rest))
        {
            errors.Add(grammar);
            return (null, null);
        }

        if (obj == TemplateObjects.Node)
        {
            // § 6: a count the run discovers is the next bump's.
            errors.Add($"Loop '{loopId}' iterates {reference}; a loop over a node's value waits for the scheduler redesign — iterate an input or a snippet folder.");
            return (null, null);
        }

        if (rest.Length > 0 || (obj != TemplateObjects.Snippet && obj != TemplateObjects.Input))
        {
            errors.Add(grammar);
            return (null, null);
        }

        if (obj == TemplateObjects.Snippet)
        {
            if (snippets.Collections.TryGetValue(id, out var folder))
            {
                return (new SnippetFolderFan(id), folder);
            }

            errors.Add(snippets.Scalars.ContainsKey(id)
                ? $"Loop '{loopId}' iterates snippet '{id}', which is a file, not a folder."
                : $"Loop '{loopId}' iterates snippet folder '{id}', which the package does not declare.");
            return (null, null);
        }

        if (!inputsById.TryGetValue(id, out var declaration))
        {
            errors.Add($"Loop '{loopId}' iterates undeclared input '{id}'.");
            return (null, null);
        }

        if (WorkflowInputTypes.Of(declaration) != WorkflowInputTypes.Array)
        {
            errors.Add($"Loop '{loopId}' iterates input '{id}', which is a {WorkflowInputTypes.Of(declaration)}; only an array can be iterated.");
            return (null, null);
        }

        var element = WorkflowDeclarationNode.Of(declaration).Items ?? new WorkflowDeclarationNode(WorkflowInputTypes.Text, declaration.Label, false, null, null, null);
        return (new InputArrayFan(element), null);
    }

    /// <summary>
    /// § 1.3: what a template reads of a flow node — a loop is a list of its
    /// returned node (iterable; <c>.content</c> the concatenation, <c>.value</c> the
    /// array) over the fan its body runs under, with its body nodes as the
    /// element's members; an if/case is its returned node's shape when every
    /// branch returns one format, else text with the disagreement named. #1234
    /// (v27 design § 1.5): <paramref name="shapes"/> holds the shapes built so far
    /// — the plain nodes' and, built first (<see cref="Resolved.InShapeOrder"/>),
    /// the if/case nodes' — so a loop returning an inner flow node takes that
    /// branch shape as its element (one format when the branches agree, else
    /// the disagreement by name); the Aggregator stands in only for a ret with
    /// no shape at all.
    /// </summary>
    public static TemplateNodeShape ShapeOf(Flow flow, IReadOnlyDictionary<string, TemplateNodeShape> shapes)
    {
        var rets = flow.Bodies
            .Select(b => b.RetNodeId is { } id && shapes.TryGetValue(id, out var shape) ? (Id: id, Shape: shape) : (Id: b.RetNodeId, Shape: (TemplateNodeShape?)null))
            .ToList();

        if (flow.Kind == Kind.For)
        {
            var ret = rets.Count > 0 && rets[0].Shape is { } retShape
                ? (rets[0].Id!, retShape)
                : (rets.FirstOrDefault().Id ?? string.Empty, TemplateNodeShape.Aggregator());
            var body = flow.BodyNodeIds
                .Distinct(StringComparer.Ordinal)
                .Where(shapes.ContainsKey)
                .ToDictionary(id => id, id => shapes[id], StringComparer.Ordinal);
            return TemplateNodeShape.Loop(ret.Item1, ret.Item2, flow.Fan, body);
        }

        var known = rets.Where(r => r.Shape is not null).ToList();

        if (known.Count > 0 && known.Count == rets.Count
            && known.Select(r => r.Shape!.Format).Distinct(StringComparer.Ordinal).Count() == 1)
        {
            return TemplateNodeShape.Branch(flow.TemplateKind, known[0].Id!, known[0].Shape!);
        }

        var disagreement = known.Select(r => r.Shape!.Type).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return TemplateNodeShape.Branch(flow.TemplateKind, disagreement);
    }
}
