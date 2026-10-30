using System.Text;
using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Services.Provenance;

/// <summary>
/// #642: the run's own graph, drawn live — a pure function from the job
/// response's node snapshot and row states to Mermaid text, in the rails'
/// own colours. Pure and deterministic on purpose: the pages' polls replace
/// the response every tick, and a tick that changes no status produces
/// byte-identical text, which the renderer's change guard skips.
/// </summary>
public static class RunDagDiagram
{
    /// <summary>The rails' colours, verbatim (Mermaid cannot read CSS vars, so
    /// this mirrors the brand accent #0078d4 by hand — keep it in step with
    /// --consultologist-accent): muted #5d6a7c, success #107c10, error #a4262c,
    /// accent #0078d4. Fills are hex, never rgba(): Mermaid's classDef parser
    /// splits its style list on commas, so the commas inside rgba(...) are a
    /// syntax error that fails the whole diagram — the running fill is the
    /// accent flattened onto white at ~8% (#eaf3fb).</summary>
    private const string ClassDefs =
        "classDef ranNot fill:#f7f9fc,stroke:#5d6a7c,color:#5d6a7c,opacity:0.6\n"
        + "classDef skipped fill:#f7f9fc,stroke:#5d6a7c,color:#5d6a7c,stroke-dasharray:4 3\n"
        + "classDef done fill:#f7f9fc,stroke:#107c10,color:#107c10,stroke-width:2px\n"
        + "classDef failed fill:#f7f9fc,stroke:#a4262c,color:#a4262c,stroke-width:2px\n"
        + "classDef running fill:#eaf3fb,stroke:#0078d4,color:#0078d4,stroke-width:2.5px";

    public static string Build(ConsultGenerationJobResponse detail)
    {
        var nodes = detail.Nodes ?? Array.Empty<ConsultGenerationNodeDescriptor>();
        var outputs = detail.NodeOutputs ?? new Dictionary<string, ConsultGenerationNodeStatus>();
        var body = new StringBuilder("flowchart TD\n");
        var byClass = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["ranNot"] = new(), ["skipped"] = new(), ["done"] = new(), ["failed"] = new(), ["running"] = new()
        };
        var statusById = nodes.ToDictionary(node => node.Id, node => StatusOf(node, outputs, detail.Collections, nodes), StringComparer.Ordinal);

        // v26 (#1208, record storage 8): a flow row draws its body as a subgraph — a loop as the
        // fan it is with its count, an if/case as the decided set with the branch taken and the
        // pruned bodies dashed inside it; a body node never sits at the top level. A record
        // without flow rows draws exactly as it did.
        var flowAware = nodes.Any(RecordFlow.IsFlow);
        var bodyIds = flowAware ? RecordFlow.BodyIds(nodes) : new HashSet<string>(StringComparer.Ordinal);
        var pruned = flowAware ? RecordFlow.PrunedOf(nodes, outputs) : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var nodesById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        // External sources first, in first-seen order: the stadiums.
        var sources = new List<string>();
        foreach (var node in nodes)
        {
            foreach (var binding in node.Bindings ?? new Dictionary<string, ConsultNodeBindingDescriptor>())
            {
                if ((binding.Value.From.StartsWith("input:", StringComparison.Ordinal)
                        || binding.Value.From.StartsWith("data:", StringComparison.Ordinal))
                    && !sources.Contains(binding.Value.From, StringComparer.Ordinal))
                {
                    sources.Add(binding.Value.From);
                }
            }
        }

        foreach (var source in sources)
        {
            body.Append(($"src_{Sanitize(source)}([\"{source}\"])\n"));
        }

        // The nodes, in package order, each shaped and classed by its state.
        void EmitNode(ConsultGenerationNodeDescriptor node)
        {
            var id = Sanitize(node.Id);
            var status = statusById[node.Id];
            byClass[status].Add(id);

            var label = new StringBuilder($"{node.Id}<br/>{node.Label}");
            if (node.ForEach != null)
            {
                var (doneItems, totalItems) = ItemTally(node, outputs, detail.Collections, nodes);
                label.Append(($"<br/>per {node.ForEach} item ({doneItems}/{totalItems})"));
            }
            else if (node.In != null)
            {
                // v26 (#1208): a body node — its items' tally; the loop's subgraph names the fan.
                var (doneItems, totalItems) = ItemTally(node, outputs, detail.Collections, nodes);
                label.Append(($"<br/>({doneItems}/{totalItems}) items"));
            }

            if (node.Check is { } check)
            {
                label.Append(($"<br/>check: {check.Op}"));
            }

            if (node.Template == true)
            {
                label.Append("<br/>template");
            }

            // B7 #1006: a 25 node's format and producer, where a contract would read.
            if (node.Format is { Length: > 0 } format)
            {
                label.Append(($"<br/>{format} · {node.Producer ?? "—"}"));
            }

            if (node.Aggregate != null)
            {
                label.Append("<br/>aggregate");
            }

            // v26 (#1208): the node's prompt by path (the storage-8 row's field).
            if (node.Prompt is { Length: > 0 } promptPath)
            {
                label.Append(($"<br/>{promptPath}"));
            }

            body.Append(($"{id}[\"{Label(label.ToString())}\"]\n"));

            // Edges into the node — dashed when either endpoint was skipped,
            // the rails' "dashed lines for skipped" read.
            foreach (var binding in node.Bindings ?? new Dictionary<string, ConsultNodeBindingDescriptor>())
            {
                var from = binding.Value.From;
                if (from.StartsWith("node:", StringComparison.Ordinal))
                {
                    AppendEdge(body, statusById, from["node:".Length..], node.Id);
                }
                else if (from.StartsWith("input:", StringComparison.Ordinal) || from.StartsWith("data:", StringComparison.Ordinal))
                {
                    var dashed = status == "skipped";
                    body.Append(($"src_{Sanitize(from)} {(dashed ? "-.->" : "-->")} {id}\n"));
                }
            }

            // B7 #1006: at 25 the edges are the derived reads — what the
            // template and parameters read — where bindings were below 25.
            foreach (var read in node.Reads ?? Array.Empty<string>())
            {
                AppendEdge(body, statusById, read, node.Id);
            }

            foreach (var sourceRef in node.Aggregate ?? Array.Empty<string>())
            {
                // node:x below 25, node.x at 25 (#1037).
                if (Consultologist.PackageFormat.TemplateObjectReference.NodeIdOf(sourceRef) is { } sourceId)
                {
                    AppendEdge(body, statusById, sourceId, node.Id, "aggregate");
                }
            }

            if (node.Check is { } checkEdges)
            {
                foreach (var (member, operand) in new[] { ("of", checkEdges.Of), ("in", checkEdges.In) })
                {
                    if (Consultologist.PackageFormat.TemplateObjectReference.NodeIdOf(operand) is { } operandId)
                    {
                        body.Append(($"{Sanitize(operandId)} -.->|\"{member}\"| {id}\n"));
                    }
                }
            }
        }

        // v26 (#1208): a flow row — its body inside a subgraph, its own box after, the ret edge.
        void EmitFlow(ConsultGenerationNodeDescriptor flow)
        {
            var id = Sanitize(flow.Id);
            var status = statusById[flow.Id];
            var kind = flow.Kind ?? "flow";
            var subgraphId = $"{Sanitize(kind)}_{id}";
            var bodies = (flow.Body ?? Array.Empty<string>()).Where(nodesById.ContainsKey).Select(bodyId => nodesById[bodyId]).ToList();

            if (RecordFlow.IsLoop(flow))
            {
                var collection = flow.Collection ?? string.Empty;
                var retNode = RetIdOf(flow) is { } retId && nodesById.TryGetValue(retId, out var ret) ? ret : bodies.FirstOrDefault();
                var (doneItems, totalItems) = retNode is null ? (0, flow.Count ?? 0) : ItemTally(retNode, outputs, detail.Collections, nodes);
                body.Append(($"subgraph {subgraphId}[\"{flow.Id}: per {collection} item ({doneItems}/{totalItems})\"]\n"));
                foreach (var bodyNode in bodies)
                {
                    // v27 (#1236, record storage 9): an if/case inside the loop is a subgraph of its own, one level deeper.
                    if (RecordFlow.IsBranching(bodyNode))
                    {
                        EmitFlow(bodyNode);
                    }
                    else
                    {
                        EmitNode(bodyNode);
                    }
                }

                body.Append("end\n");
                if (collection.Length > 0)
                {
                    body.Append(($"src_{Sanitize(collection)}([\"{collection}\"])\n"));
                    body.Append(($"src_{Sanitize(collection)} -->|\"for\"| {subgraphId}\n"));
                }

                byClass[status].Add(id);
                body.Append(($"{id}[\"{Label($"{flow.Id}<br/>{flow.Label}<br/>for item in {collection}")}\"]\n"));
            }
            else
            {
                // v27 (#1236): an if/case inside a loop decides per item — the branches tallied, no pruned rows.
                var inner = RecordFlow.IsInner(flow, nodes);
                var branch = inner ? BranchesTally(flow) : RecordFlow.BranchText(flow);
                // #1247: the expression is package-authored text — its quotes would close the label.
                body.Append(($"subgraph {subgraphId}[\"{Label($"{flow.Id}: {kind} {flow.Expression} — {(inner ? "per item" : $"branch: {branch}")}")}\"]\n"));
                foreach (var bodyNode in bodies)
                {
                    EmitNode(bodyNode);
                }

                foreach (var prunedId in pruned.GetValueOrDefault(flow.Id) ?? Array.Empty<string>())
                {
                    byClass["skipped"].Add(Sanitize(prunedId));
                    body.Append(($"{Sanitize(prunedId)}[\"{prunedId}<br/>pruned\"]\n"));
                }

                // v27 (storage 9): the items the inner flow skipped by condition — the rail's ⊘
                // rows and History's struck-through rows — as one dashed box inside the flow's
                // subgraph, named by item, with the condition's reason when it is one; the
                // diamond's dashed "none" edge leads to it.
                var skippedItems = inner ? RecordFlow.SkippedItemsOf(flow, outputs) : null;
                var skippedId = $"{id}_skipped";
                if (skippedItems is { Count: > 0 })
                {
                    var names = skippedItems.Keys.Select(itemId => ItemNameOf(flow, itemId, detail.Collections, nodes)).ToList();
                    var reasons = skippedItems.Values.Distinct(StringComparer.Ordinal).ToList();
                    byClass["skipped"].Add(skippedId);
                    body.Append(($"{skippedId}[\"{Label($"skipped ({names.Count})<br/>{string.Join("<br/>", names)}{(reasons.Count == 1 ? $"<br/>{reasons[0]}" : string.Empty)}")}\"]\n"));
                }

                body.Append("end\n");
                byClass[status].Add(id);
                body.Append(($"{id}{{\"{Label($"{flow.Id}<br/>{flow.Label}<br/>{kind}: {flow.Expression}<br/>{(inner ? branch : $"branch: {branch}")}")}\"}}\n"));
                // A case over a node's value (node.<id>.value) draws the dotted edge from that node.
                if (kind == "case" && SubjectNodeOf(flow.Expression) is { } subjectId)
                {
                    body.Append(($"{Sanitize(subjectId)} -.->|\"case\"| {id}\n"));
                }

                if (skippedItems is { Count: > 0 })
                {
                    body.Append(($"{id} -.->|\"none\"| {skippedId}\n"));
                }
            }

            if (RetIdOf(flow) is { } retEdgeId)
            {
                AppendEdge(body, statusById, retEdgeId, flow.Id, "ret");
            }

            foreach (var read in flow.Reads ?? Array.Empty<string>())
            {
                AppendEdge(body, statusById, read, flow.Id);
            }
        }

        foreach (var node in nodes)
        {
            if (flowAware && bodyIds.Contains(node.Id))
            {
                continue;
            }

            if (flowAware && RecordFlow.IsFlow(node))
            {
                EmitFlow(node);
            }
            else
            {
                EmitNode(node);
            }
        }

        // The deliverables: hexagons in their three states. The response does
        // not carry the result→aggregator mapping, so with exactly one
        // aggregator the edge is drawn and otherwise the hexagons stand alone
        // — the state is the point.
        var aggregators = nodes.Where(node => node.Aggregate != null).ToList();
        void Deliverable(string resultId, string label, string cls, IReadOnlyList<string>? sources = null)
        {
            var id = $"result_{Sanitize(resultId)}";
            byClass[cls].Add(id);
            body.Append(($"{id}{{{{\"{label}\"}}}}\n"));
            // v26 (#1208): a storage-8 document names its sources — one edge each.
            if (sources is { Count: > 0 })
            {
                foreach (var source in sources)
                {
                    // The served row names a source by its bare id (the storage-8 projection); a
                    // node.<id> spelling is accepted too — as the loop's ret is (#1236).
                    var sourceId = Consultologist.PackageFormat.TemplateObjectReference.NodeIdOf(source) ?? source;
                    var dashed = cls == "skipped" || statusById.GetValueOrDefault(sourceId) == "skipped";
                    body.Append(($"{Sanitize(sourceId)} {(dashed ? "-.->" : "-->")}|\"source\"| {id}\n"));
                }
            }
            else if (aggregators.Count == 1)
            {
                body.Append(($"{Sanitize(aggregators[0].Id)} {(cls == "skipped" ? "-.->" : "-->")} {id}\n"));
            }
        }

        foreach (var document in detail.AssembledDocuments ?? Array.Empty<ConsultGenerationResultDocumentResponse>())
        {
            Deliverable(document.ResultId, document.Label, "done", document.Sources);
        }

        foreach (var skipped in detail.SkippedDocuments ?? Array.Empty<ConsultSkippedDocumentResponse>())
        {
            Deliverable(skipped.ResultId, skipped.Label, "skipped");
        }

        foreach (var failed in detail.FailedDocuments ?? Array.Empty<ConsultFailedDocumentResponse>())
        {
            Deliverable(failed.ResultId, failed.Label, "failed");
        }

        body.Append(ClassDefs);
        foreach (var (cls, members) in byClass)
        {
            if (members.Count > 0)
            {
                body.Append(($"\nclass {string.Join(',', members)} {cls}"));
            }
        }

        return body.ToString();
    }

    private static void AppendEdge(StringBuilder body, IReadOnlyDictionary<string, string> statusById, string fromId, string toId, string? label = null)
    {
        var dashed = statusById.GetValueOrDefault(fromId) == "skipped" || statusById.GetValueOrDefault(toId) == "skipped";
        var arrow = dashed ? "-.->" : "-->";
        var caption = label is null ? "" : $"|\"{label}\"| ";
        body.Append(($"{Sanitize(fromId)} {arrow} {caption}{Sanitize(toId)}\n"));
    }

    /// <summary>The rails' five-way mapping: Completed→done, Failed→failed,
    /// Running→running, Skipped→skipped, absent→ranNot. A forEach node rolls
    /// its items up: any failed → failed; else any running → running; else
    /// every roster item completed → done; else running while some are in
    /// flight is already covered, so a partial set stays ranNot only before
    /// anything of it ran.</summary>
    /// <summary>The returned node a flow row names — spelled node.&lt;id&gt; or, as the engine serves it, the bare id.</summary>
    private static string? RetIdOf(ConsultGenerationNodeDescriptor flow) =>
        flow.Ret is { Length: > 0 } ret ? Consultologist.PackageFormat.TemplateObjectReference.NodeIdOf(ret) ?? ret : null;

    /// <summary>v27 (#1236): an inner if/case's per-item branches tallied — "then 2 · none 1", in first-seen order; "per item" before any item decided.</summary>
    private static string BranchesTally(ConsultGenerationNodeDescriptor flow)
    {
        var tally = RecordFlow.BranchesOf(flow)
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(group => $"{group.Key} {group.Count()}")
            .ToList();
        return tally.Count == 0 ? "per item" : string.Join(" · ", tally);
    }

    private static string StatusOf(
        ConsultGenerationNodeDescriptor node,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs,
        IReadOnlyList<ConsultCollectionRoster>? rosters,
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes)
    {
        // v26 (#1208): a body node fans through its loop row — the same roll-up.
        if (RecordFlow.FanOf(node, nodes) != null)
        {
            var items = outputs.Where(pair => pair.Key.StartsWith($"{node.Id}:", StringComparison.Ordinal)).ToList();
            if (items.Any(pair => pair.Value.Status == "Failed"))
            {
                return "failed";
            }

            if (items.Any(pair => pair.Value.Status == "Running"))
            {
                return "running";
            }

            var (done, total) = ItemTally(node, outputs, rosters, nodes);
            if (total > 0 && done >= total)
            {
                return "done";
            }

            return done > 0 ? "running" : ClassFor(outputs.GetValueOrDefault(node.Id)?.Status);
        }

        return ClassFor(outputs.GetValueOrDefault(node.Id)?.Status);
    }

    private static string ClassFor(string? status) => status switch
    {
        "Completed" => "done",
        "Failed" => "failed",
        "Running" => "running",
        "Skipped" => "skipped",
        _ => "ranNot"
    };

    /// <summary>An item's roster name under the fan the flow runs in, else its id.</summary>
    private static string ItemNameOf(ConsultGenerationNodeDescriptor flow, string itemId, IReadOnlyList<ConsultCollectionRoster>? rosters, IReadOnlyList<ConsultGenerationNodeDescriptor> nodes)
    {
        var collection = RecordFlow.FanOf(flow, nodes);
        var collectionId = collection is null ? null : Consultologist.PackageFormat.WorkflowFanSources.CollectionIdOf(collection);
        var roster = rosters?.FirstOrDefault(r => string.Equals(r.CollectionId, collectionId, StringComparison.Ordinal));
        return roster?.Items.FirstOrDefault(item => string.Equals(item.Id, itemId, StringComparison.Ordinal))?.Name ?? itemId;
    }

    private static (int Done, int Total) ItemTally(
        ConsultGenerationNodeDescriptor node,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs,
        IReadOnlyList<ConsultCollectionRoster>? rosters,
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes)
    {
        var items = outputs.Where(pair => pair.Key.StartsWith($"{node.Id}:", StringComparison.Ordinal)).ToList();
        // v27 (#1236): an if/case inside a loop is done per item decided (its branches map); a branch node over the items it ran.
        if (RecordFlow.IsInner(node, nodes))
        {
            var decided = node.Branches?.Count ?? 0;
            return (decided, Math.Max(RecordFlow.LoopOf(node, nodes)?.Count ?? 0, Math.Max(decided, items.Count)));
        }

        if (RecordFlow.FlowOf(node, nodes) is not null)
        {
            return (items.Count(pair => pair.Value.Status == "Completed"), items.Count);
        }

        // v26 (#1208): a body node's total is its loop row's settled count; a forEach node's the roster's, as ever.
        if (RecordFlow.LoopOf(node, nodes) is { Count: { } count })
        {
            return (items.Count(pair => pair.Value.Status == "Completed"), Math.Max(count, items.Count));
        }

        var collectionId = node.ForEach?.Contains(':') == true ? node.ForEach[(node.ForEach.IndexOf(':') + 1)..] : node.ForEach;
        var total = rosters?.FirstOrDefault(roster => roster.CollectionId == collectionId)?.Items.Count ?? items.Count;
        return (items.Count(pair => pair.Value.Status == "Completed"), Math.Max(total, items.Count));
    }

    /// <summary>v26 (#1208): the node a case subject reads — node.&lt;id&gt;.value, or node.&lt;id&gt;; null for an input.</summary>
    private static string? SubjectNodeOf(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || !expression.StartsWith("node.", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = expression["node.".Length..];
        var dot = rest.IndexOf('.');
        return dot > 0 ? rest[..dot] : rest.Length > 0 ? rest : null;
    }

    private static string Sanitize(string raw) => new(raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    /// <summary>
    /// #1247: package-authored text inside a Mermaid quoted label — an if/case expression
    /// (<c>item.value.kind == "narrative"</c>), a node's label, a prompt path. A double quote
    /// would close the label and fail the whole diagram ("Parse error on line 14"); Mermaid's
    /// entity for it is <c>#quot;</c>. The <c>&lt;br/&gt;</c> breaks are ours and pass through.
    /// </summary>
    private static string Label(string text) => text.Replace("\"", "#quot;", StringComparison.Ordinal);
}
