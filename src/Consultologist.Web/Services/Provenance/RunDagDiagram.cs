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
        var instances = detail.LoopInstances;
        var finished = detail.Status is "Completed" or "Failed" or "Cancelled";
        var statusById = nodes.ToDictionary(node => node.Id, node => StatusOf(node, outputs, detail.Collections, nodes, instances), StringComparer.Ordinal);

        // v26 (#1208, record storage 8): a flow row draws its body as a subgraph — a loop as the
        // fan it is with its count, an if/case as the decided set with the branch taken and the
        // pruned bodies dashed inside it; a body node never sits at the top level. A record
        // without flow rows draws exactly as it did.
        var flowAware = nodes.Any(RecordFlow.IsFlow);
        var bodyIds = flowAware ? RecordFlow.BodyIds(nodes) : new HashSet<string>(StringComparer.Ordinal);
        var pruned = flowAware ? RecordFlow.PrunedOf(nodes, outputs) : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var nodesById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        // #1321: Mermaid puts every node an edge inside a subgraph names into that subgraph — a
        // top-level node a body node reads (a gate's classifier, an inner case's subject) was drawn
        // inside the loop's box. Nodes are declared where they belong; an edge written while a
        // subgraph is open waits, and is written after the outermost `end`.
        var deferred = new StringBuilder();
        var depth = 0;
        StringBuilder Edges() => depth == 0 ? body : deferred;
        void Open(string line)
        {
            body.Append(line);
            depth++;
        }

        void Close()
        {
            body.Append("end\n");
            depth--;
            if (depth == 0 && deferred.Length > 0)
            {
                body.Append(deferred);
                deferred.Clear();
            }
        }

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
                var (doneItems, totalItems, skippedItems) = ItemTally(node, outputs, detail.Collections, nodes, instances);
                label.Append(($"<br/>per {node.ForEach} item ({TallyText(doneItems, totalItems, skippedItems)})"));
            }
            else if (node.In != null && RecordFlow.RepeatOf(node, nodes) is { Kind: RecordFlow.While })
            {
                // v30 (#1345): a while's body node — its passes, not items.
                var passes = PassRows(node, outputs);
                label.Append(($"<br/>({passes.Count(row => row.Status == "Completed")}/{passes.Count} passes)"));
            }
            else if (node.In != null)
            {
                // v26 (#1208): a body node — its items' tally; the loop's subgraph names the fan.
                var (doneItems, totalItems, skippedItems) = ItemTally(node, outputs, detail.Collections, nodes, instances);
                label.Append(($"<br/>({TallyText(doneItems, totalItems, skippedItems, NotRunOf(node, outputs, nodes))}) items"));
            }

            if (node.Check is { } check)
            {
                label.Append(($"<br/>check: {check.Op}"));
            }

            if (node.Template == true)
            {
                label.Append("<br/>template");
            }

            // v30 (#1345): a let — computed, no model: its type, never a prompt or an agent.
            if (RecordFlow.IsLet(node))
            {
                label.Append(($"<br/>computed · let → {node.LetType ?? "—"}"));
            }
            // B7 #1006: a 25 node's format and producer, where a contract would read.
            else if (node.Format is { Length: > 0 } format)
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

            // v30 (#1345): a let is drawn as a subroutine — computed by the engine, no model call.
            body.Append(RecordFlow.IsLet(node)
                ? ($"{id}[[\"{Label(label.ToString())}\"]]\n")
                : ($"{id}[\"{Label(label.ToString())}\"]\n"));

            // Edges into the node — dashed when either endpoint was skipped,
            // the rails' "dashed lines for skipped" read.
            foreach (var binding in node.Bindings ?? new Dictionary<string, ConsultNodeBindingDescriptor>())
            {
                var from = binding.Value.From;
                if (from.StartsWith("node:", StringComparison.Ordinal))
                {
                    AppendEdge(Edges(), statusById, from["node:".Length..], node.Id);
                }
                else if (from.StartsWith("input:", StringComparison.Ordinal) || from.StartsWith("data:", StringComparison.Ordinal))
                {
                    var dashed = status == "skipped";
                    Edges().Append(($"src_{Sanitize(from)} {(dashed ? "-.->" : "-->")} {id}\n"));
                }
            }

            // B7 #1006: at 25 the edges are the derived reads — what the
            // template and parameters read — where bindings were below 25.
            foreach (var read in node.Reads ?? Array.Empty<string>())
            {
                AppendEdge(Edges(), statusById, read, node.Id);
            }

            foreach (var sourceRef in node.Aggregate ?? Array.Empty<string>())
            {
                // node:x below 25, node.x at 25 (#1037).
                if (Consultologist.PackageFormat.TemplateObjectReference.NodeIdOf(sourceRef) is { } sourceId)
                {
                    AppendEdge(Edges(), statusById, sourceId, node.Id, "aggregate");
                }
            }

            if (node.Check is { } checkEdges)
            {
                foreach (var (member, operand) in new[] { ("of", checkEdges.Of), ("in", checkEdges.In) })
                {
                    if (Consultologist.PackageFormat.TemplateObjectReference.NodeIdOf(operand) is { } operandId)
                    {
                        Edges().Append(($"{Sanitize(operandId)} -.->|\"{member}\"| {id}\n"));
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
            var loopSource = RecordFlow.IsLoop(flow)
                && Consultologist.PackageFormat.TemplateObjectReference.TryParse(flow.Collection, out var sourceObject, out var sourceId, out _)
                && sourceObject == Consultologist.PackageFormat.TemplateObjects.Node
                    ? sourceId
                    : null;

            if (RecordFlow.IsLoop(flow))
            {
                var collection = flow.Collection ?? string.Empty;
                var retNode = RetIdOf(flow) is { } retId && nodesById.TryGetValue(retId, out var ret) ? ret : bodies.FirstOrDefault();
                var (doneItems, totalItems, skippedItems) = retNode is null ? (0, flow.Count ?? 0, 0)
                    // v30 (#1345): a loop returning a loop — its items are the innermost returned node's, by path prefix.
                    : RecordFlow.IsRepeat(retNode) ? LoopReturnTally(flow, nodesById, outputs, nodes, instances)
                    : ItemTally(retNode, outputs, detail.Collections, nodes, instances);
                var nested = RecordFlow.RepeatOf(flow, nodes) is not null;
                var flowInstances = RecordFlow.InstancesOf(flow, instances);
                // v29 (#1303): a node's list not yet known is said so, never "(0/0)". v30 (#1345): a
                // loop inside a loop or a while counts per instance — its rows summed, "counting…"
                // beside them while an outer item or pass has not settled its own.
                var tally = nested
                    ? NestedTally(doneItems, skippedItems, NotRunOf(flow, outputs, nodes), flowInstances, flow, nodes, finished)
                    : LoopCount.IsUnknown(flow)
                        ? LoopCount.Describe(finished)
                        : TallyText(doneItems, totalItems, skippedItems, NotRunOf(flow, outputs, nodes));
                Open($"subgraph {subgraphId}[\"{flow.Id}: per {collection} item ({tally})\"]\n");
                EmitBodies(bodies);

                Close();
                if (loopSource is { } sourceNode)
                {
                    // v29 (#1321): a loop over a node's value — the node is the source, one for edge (as WorkflowDagDiagram draws it).
                    Edges().Append(($"{Sanitize(sourceNode)} -->|\"for\"| {subgraphId}\n"));
                }
                else if (collection.Length > 0)
                {
                    body.Append(($"src_{Sanitize(collection)}([\"{collection}\"])\n"));
                    Edges().Append(($"src_{Sanitize(collection)} -->|\"for\"| {subgraphId}\n"));
                }

                byClass[status].Add(id);
                body.Append(($"{id}[\"{Label($"{flow.Id}<br/>{flow.Label}<br/>for item in {collection}")}\"]\n"));
            }
            else if (RecordFlow.IsWhile(flow))
            {
                // v30 (#1345, design § 10): a while — its body inside a subgraph labelled with its
                // bound and, once it stopped, its passes and why; a loop's box, never a diamond.
                Open($"subgraph {subgraphId}[\"{Label($"{flow.Id}: while {flow.Expression} — {WhileTally(flow, bodies, outputs)}")}\"]\n");
                EmitBodies(bodies);
                Close();
                byClass[status].Add(id);
                body.Append(($"{id}[\"{Label($"{flow.Id}<br/>{flow.Label}<br/>while: {flow.Expression}<br/>at most {flow.Max?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"}")}\"]\n"));
            }
            else
            {
                // v27 (#1236): an if/case inside a loop decides per item — the branches tallied, no pruned rows.
                var inner = RecordFlow.IsInner(flow, nodes);
                var branch = inner ? BranchesTally(flow) : RecordFlow.BranchText(flow);
                // #1247: the expression is package-authored text — its quotes would close the label.
                Open($"subgraph {subgraphId}[\"{Label($"{flow.Id}: {kind} {flow.Expression} — {(inner ? "per item" : $"branch: {branch}")}")}\"]\n");
                EmitBodies(bodies);

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

                Close();
                byClass[status].Add(id);
                body.Append(($"{id}{{\"{Label($"{flow.Id}<br/>{flow.Label}<br/>{kind}: {flow.Expression}<br/>{(inner ? branch : $"branch: {branch}")}")}\"}}\n"));
                // A case over a node's value (node.<id>.value) draws the dotted edge from that node.
                if (kind == "case" && SubjectNodeOf(flow.Expression) is { } subjectId)
                {
                    Edges().Append(($"{Sanitize(subjectId)} -.->|\"case\"| {id}\n"));
                }

                if (skippedItems is { Count: > 0 })
                {
                    Edges().Append(($"{id} -.->|\"none\"| {skippedId}\n"));
                }
            }

            if (RetIdOf(flow) is { } retEdgeId)
            {
                AppendEdge(Edges(), statusById, retEdgeId, flow.Id, "ret");
            }

            foreach (var read in flow.Reads ?? Array.Empty<string>())
            {
                // v29 (#1321): the loop's source node is already its for edge.
                if (!string.Equals(read, loopSource, StringComparison.Ordinal))
                {
                    AppendEdge(Edges(), statusById, read, flow.Id);
                }
            }
        }

        // v30 (#1345): a body draws every flow it holds as a subgraph of its own — a loop in a
        // loop, an if in an if, a while in either — to any depth; edges wait for the outermost end.
        void EmitBodies(IEnumerable<ConsultGenerationNodeDescriptor> bodies)
        {
            foreach (var bodyNode in bodies)
            {
                if (RecordFlow.IsFlow(bodyNode))
                {
                    EmitFlow(bodyNode);
                }
                else
                {
                    EmitNode(bodyNode);
                }
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
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes,
        IReadOnlyList<ConsultLoopInstanceRow>? instances = null)
    {
        // v30 (#1345): a while's body node — its passes rolled up; the pass in flight is running.
        if (!RecordFlow.IsFlow(node) && RecordFlow.RepeatOf(node, nodes) is { Kind: RecordFlow.While })
        {
            var passes = PassRows(node, outputs);
            return passes.Any(row => row.Status == "Failed") ? "failed"
                : passes.Any(row => row.Status == "Running") ? "running"
                : passes.Count > 0 && passes.All(row => row.Status == "Completed") ? "done"
                : passes.Count > 0 ? "running"
                : ClassFor(outputs.GetValueOrDefault(node.Id)?.Status);
        }

        // v26 (#1208): a body node fans through its loop row — the same roll-up.
        if (RecordFlow.FanOf(node, nodes) != null)
        {
            var items = outputs.Where(pair => Consultologist.PackageFormat.WorkflowItemPath.PathFor(pair.Key, node.Id) is not null).ToList();
            if (items.Any(pair => pair.Value.Status == "Failed"))
            {
                return "failed";
            }

            if (items.Any(pair => pair.Value.Status == "Running"))
            {
                return "running";
            }

            var (done, total, skipped) = ItemTally(node, outputs, rosters, nodes, instances);
            if (total > 0 && done >= total)
            {
                return "done";
            }

            // #1331: every item its loop skipped by condition — settled, nothing ran.
            if (total == 0 && skipped > 0)
            {
                return "skipped";
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

    /// <summary>#1331: "1/1", or "1/1 · 1 skipped" when the loop skipped items by condition (the rail's words, #1330).</summary>
    private static string TallyText(int done, int total, int skipped, int notRun = 0) =>
        // v30 (#1345): items an until or a max stopped short of read "not run", apart from skips.
        (skipped - notRun, notRun) switch
        {
            ( > 0, > 0) => $"{done}/{total} · {skipped - notRun} skipped · {notRun} not run",
            (_, > 0) => $"{done}/{total} · {notRun} not run",
            ( > 0, _) => $"{done}/{total} · {skipped} skipped",
            _ => $"{done}/{total}",
        };

    /// <summary>v30 (#1342): the items a loop stopped short of — its own rows Skipped as "not run".</summary>
    private static int NotRunOf(ConsultGenerationNodeDescriptor node, IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs, IReadOnlyList<ConsultGenerationNodeDescriptor> nodes)
    {
        var loop = RecordFlow.IsLoop(node) ? node : RecordFlow.LoopOf(node, nodes);
        return loop is null ? 0 : outputs.Count(pair =>
            Consultologist.PackageFormat.WorkflowItemPath.PathFor(pair.Key, loop.Id) is not null
            && pair.Value.Status == "Skipped"
            && pair.Value.Reason?.StartsWith("not run", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// v30 (#1345): a loop whose returned node is a loop — the record keeps one row per loop, so
    /// its items are read off the innermost returned node's rows: an item (a path prefix at this
    /// loop's depth) is done when every leaf row under it completed. The total is the loop's count
    /// at the top, its instance rows summed when it is nested.
    /// </summary>
    private static (int Done, int Total, int Skipped) LoopReturnTally(
        ConsultGenerationNodeDescriptor loop,
        IReadOnlyDictionary<string, ConsultGenerationNodeDescriptor> nodesById,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs,
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes,
        IReadOnlyList<ConsultLoopInstanceRow>? instances)
    {
        var leaf = loop;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (RecordFlow.IsRepeat(leaf) && seen.Add(leaf.Id) && RetIdOf(leaf) is { } retId && nodesById.TryGetValue(retId, out var next))
        {
            leaf = next;
        }

        var depth = 1;
        for (var owner = RecordFlow.RepeatOf(loop, nodes); owner is not null && depth < 64; owner = RecordFlow.RepeatOf(owner, nodes))
        {
            depth++;
        }

        var byItem = outputs
            .Select(pair => (Path: Consultologist.PackageFormat.WorkflowItemPath.PathFor(pair.Key, leaf.Id), pair.Value.Status))
            .Where(row => row.Path is not null && Consultologist.PackageFormat.WorkflowItemPath.Depth(row.Path) >= depth)
            .GroupBy(row => string.Join('/', Consultologist.PackageFormat.WorkflowItemPath.Segments(row.Path!).Take(depth)), StringComparer.Ordinal)
            .ToList();
        var done = byItem.Count(item => item.All(row => row.Status == "Completed"));
        var total = RecordFlow.RepeatOf(loop, nodes) is null
            ? loop.Count ?? byItem.Count
            : RecordFlow.InstancesOf(loop, instances).Sum(row => row.Count ?? 0);
        return (done, Math.Max(total, byItem.Count), 0);
    }

    /// <summary>v30 (#1342): a while body node's pass rows (node:pass, node:outer/pass).</summary>
    private static IReadOnlyList<ConsultGenerationNodeStatus> PassRows(ConsultGenerationNodeDescriptor node, IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs) =>
        outputs.Where(pair => Consultologist.PackageFormat.WorkflowItemPath.PathFor(pair.Key, node.Id) is not null).Select(pair => pair.Value).ToList();

    /// <summary>
    /// v30 (#1345, design § 10): a while's words — "at most 3", then "pass 2 of at most 3" while it
    /// runs, and once it stopped "3 passes — stopped at its bound" / "— the check stopped holding" /
    /// "— its until held".
    /// </summary>
    private static string WhileTally(ConsultGenerationNodeDescriptor flow, IReadOnlyList<ConsultGenerationNodeDescriptor> bodies, IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs)
    {
        var bound = flow.Max?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        if (flow.Passes is { } passes)
        {
            var why = flow.Stopped switch
            {
                "bound" => "stopped at its bound",
                "until" => "its until held",
                "check" => "the check stopped holding",
                _ => flow.StopReason ?? "stopped",
            };
            return $"{passes} {(passes == 1 ? "pass" : "passes")} — {why}";
        }

        var seen = bodies.SelectMany(body => outputs.Keys.Select(key => Consultologist.PackageFormat.WorkflowItemPath.PathFor(key, body.Id)))
            .OfType<string>().Distinct(StringComparer.Ordinal).Count();
        return seen == 0 ? $"at most {bound}" : $"pass {seen} of at most {bound}";
    }

    /// <summary>
    /// v30 (#1345): a nested loop's tally — every instance's items summed ("3/3 · 2 instances"),
    /// "counting…" beside it while an outer item or pass has no settled instance yet.
    /// </summary>
    private static string NestedTally(
        int done, int skipped, int notRun,
        IReadOnlyList<ConsultLoopInstanceRow> rows,
        ConsultGenerationNodeDescriptor flow,
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes,
        bool finished)
    {
        var counted = rows.Sum(row => row.Count ?? 0);
        var text = TallyText(done, Math.Max(counted - skipped, done), skipped, notRun);
        // An outer for with a settled count knows how many instances to expect; inside a while
        // (or an outer for still counting), any instance may yet open — counting until the run ends.
        var expected = RecordFlow.RepeatOf(flow, nodes) is { Kind: RecordFlow.Loop, Count: { } outerCount } ? outerCount : (int?)null;
        var pending = !finished && (rows.Count == 0 || expected is null || rows.Count < expected);
        var instancesText = $"{rows.Count} {(rows.Count == 1 ? "instance" : "instances")}";
        return pending ? $"{text} · {instancesText}, {LoopCount.Counting}" : $"{text} · {instancesText}";
    }

    private static (int Done, int Total, int Skipped) ItemTally(
        ConsultGenerationNodeDescriptor node,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs,
        IReadOnlyList<ConsultCollectionRoster>? rosters,
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes,
        IReadOnlyList<ConsultLoopInstanceRow>? instances = null)
    {
        var items = outputs.Where(pair => Consultologist.PackageFormat.WorkflowItemPath.PathFor(pair.Key, node.Id) is not null).ToList();
        // v27 (#1236): an if/case inside a loop is done per item decided (its branches map); a branch node over the items it ran.
        if (RecordFlow.IsInner(node, nodes))
        {
            var decided = node.Branches?.Count ?? 0;
            return (decided, Math.Max(RecordFlow.LoopOf(node, nodes)?.Count ?? 0, Math.Max(decided, items.Count)), 0);
        }

        if (RecordFlow.FlowOf(node, nodes) is not null)
        {
            return (items.Count(pair => pair.Value.Status == "Completed"), items.Count, 0);
        }

        // v30 (#1345): a nested loop's body node — its total is the loop's instance rows summed.
        if (RecordFlow.LoopOf(node, nodes) is { } nestedLoop && RecordFlow.InstancesOf(nestedLoop, instances) is { Count: > 0 } rows)
        {
            var nestedSkipped = outputs.Count(pair => Consultologist.PackageFormat.WorkflowItemPath.PathFor(pair.Key, nestedLoop.Id) is not null && pair.Value.Status == "Skipped");
            return (items.Count(pair => pair.Value.Status == "Completed"), Math.Max(rows.Sum(row => row.Count ?? 0), items.Count) - nestedSkipped, nestedSkipped);
        }

        // v26 (#1208): a body node's total is its loop row's settled count; a forEach node's the roster's, as ever.
        if (RecordFlow.LoopOf(node, nodes) is { Count: { } count } loop)
        {
            // #1331 (v29, #1279): a folder item skipped by its node-gated if is skipped by the LOOP —
            // the record keys it <loop>:<item> — so it is settled for the loop's body, not still to run.
            var skipped = outputs.Count(pair => Consultologist.PackageFormat.WorkflowItemPath.PathFor(pair.Key, loop.Id) is not null && pair.Value.Status == "Skipped");
            return (items.Count(pair => pair.Value.Status == "Completed"), Math.Max(count, items.Count) - skipped, skipped);
        }

        var collectionId = node.ForEach?.Contains(':') == true ? node.ForEach[(node.ForEach.IndexOf(':') + 1)..] : node.ForEach;
        var total = rosters?.FirstOrDefault(roster => roster.CollectionId == collectionId)?.Items.Count ?? items.Count;
        return (items.Count(pair => pair.Value.Status == "Completed"), Math.Max(total, items.Count), 0);
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
