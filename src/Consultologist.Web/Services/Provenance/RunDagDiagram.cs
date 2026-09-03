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
    /// <summary>The rails' colours, verbatim (Mermaid cannot read CSS vars):
    /// muted #5d6a7c, success #107c10, error #a4262c, accent #0067b8.</summary>
    private const string ClassDefs =
        "classDef ranNot fill:#f7f9fc,stroke:#5d6a7c,color:#5d6a7c,opacity:0.6\n"
        + "classDef skipped fill:#f7f9fc,stroke:#5d6a7c,color:#5d6a7c,stroke-dasharray:4 3\n"
        + "classDef done fill:#f7f9fc,stroke:#107c10,color:#107c10,stroke-width:2px\n"
        + "classDef failed fill:#f7f9fc,stroke:#a4262c,color:#a4262c,stroke-width:2px\n"
        + "classDef running fill:rgba(0,103,184,0.08),stroke:#0067b8,color:#0067b8,stroke-width:2.5px";

    public static string Build(ConsultGenerationJobResponse detail)
    {
        var nodes = detail.Nodes ?? Array.Empty<ConsultGenerationNodeDescriptor>();
        var outputs = detail.NodeOutputs ?? new Dictionary<string, ConsultGenerationNodeStatus>();
        var body = new StringBuilder("flowchart TD\n");
        var byClass = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["ranNot"] = new(), ["skipped"] = new(), ["done"] = new(), ["failed"] = new(), ["running"] = new()
        };
        var statusById = nodes.ToDictionary(node => node.Id, node => StatusOf(node, outputs, detail.Collections), StringComparer.Ordinal);

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
        foreach (var node in nodes)
        {
            var id = Sanitize(node.Id);
            var status = statusById[node.Id];
            byClass[status].Add(id);

            var label = new StringBuilder($"{node.Id}<br/>{node.Label}");
            if (node.ForEach != null)
            {
                var (doneItems, totalItems) = ItemTally(node, outputs, detail.Collections);
                label.Append(($"<br/>per {node.ForEach} item ({doneItems}/{totalItems})"));
            }

            if (node.Check is { } check)
            {
                label.Append(($"<br/>check: {check.Op}"));
            }

            if (node.Template == true)
            {
                label.Append("<br/>template");
            }

            if (node.Aggregate != null)
            {
                label.Append("<br/>aggregate");
            }

            body.Append(($"{id}[\"{label}\"]\n"));

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

            foreach (var sourceRef in node.Aggregate ?? Array.Empty<string>())
            {
                if (sourceRef.StartsWith("node:", StringComparison.Ordinal))
                {
                    AppendEdge(body, statusById, sourceRef["node:".Length..], node.Id, "aggregate");
                }
            }

            if (node.Check is { } checkEdges)
            {
                foreach (var (member, operand) in new[] { ("of", checkEdges.Of), ("in", checkEdges.In) })
                {
                    if (operand.StartsWith("node:", StringComparison.Ordinal))
                    {
                        body.Append(($"{Sanitize(operand["node:".Length..])} -.->|\"{member}\"| {id}\n"));
                    }
                }
            }
        }

        // The deliverables: hexagons in their three states. The response does
        // not carry the result→aggregator mapping, so with exactly one
        // aggregator the edge is drawn and otherwise the hexagons stand alone
        // — the state is the point.
        var aggregators = nodes.Where(node => node.Aggregate != null).ToList();
        void Deliverable(string resultId, string label, string cls)
        {
            var id = $"result_{Sanitize(resultId)}";
            byClass[cls].Add(id);
            body.Append(($"{id}{{{{\"{label}\"}}}}\n"));
            if (aggregators.Count == 1)
            {
                body.Append(($"{Sanitize(aggregators[0].Id)} {(cls == "skipped" ? "-.->" : "-->")} {id}\n"));
            }
        }

        foreach (var document in detail.AssembledDocuments ?? Array.Empty<ConsultGenerationResultDocumentResponse>())
        {
            Deliverable(document.ResultId, document.Label, "done");
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
    private static string StatusOf(
        ConsultGenerationNodeDescriptor node,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs,
        IReadOnlyList<ConsultCollectionRoster>? rosters)
    {
        if (node.ForEach != null)
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

            var (done, total) = ItemTally(node, outputs, rosters);
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

    private static (int Done, int Total) ItemTally(
        ConsultGenerationNodeDescriptor node,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs,
        IReadOnlyList<ConsultCollectionRoster>? rosters)
    {
        var items = outputs.Where(pair => pair.Key.StartsWith($"{node.Id}:", StringComparison.Ordinal)).ToList();
        var collectionId = node.ForEach?.Contains(':') == true ? node.ForEach[(node.ForEach.IndexOf(':') + 1)..] : node.ForEach;
        var total = rosters?.FirstOrDefault(roster => roster.CollectionId == collectionId)?.Items.Count ?? items.Count;
        return (items.Count(pair => pair.Value.Status == "Completed"), Math.Max(total, items.Count));
    }

    private static string Sanitize(string raw) => new(raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
