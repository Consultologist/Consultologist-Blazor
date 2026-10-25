namespace Consultologist.PackageFormat;

/// <summary>
/// The validator at specVersion 26 (package-format-v26-design.md; #1150, the
/// 26 counterpart of A4 #999). At 26 the manifest is a program over the objects
/// the templates read: flow nodes (<c>if</c>, <c>case</c>, <c>for</c>) in place
/// of <c>forEach</c> and a node's <c>when</c>, every gate keyed <c>if</c>, the
/// aggregator folded into <c>deliverables[].sources</c>, a node naming its
/// prompt file. This partial is the path those rules attach to; it opens with
/// the § 8 retirements whose replacement is a manifest shape and hands the rest
/// to the 25 pipeline, which holds at ≥ 25 — so, until #1100 (flow nodes) and
/// #1101 (sources) replace its steps, no 26 manifest is valid and the path is
/// inert, as A4's was behind the unchanged accepted set. The retirements whose
/// replacement does not exist yet follow with it: the folder index's
/// <c>name</c>/<c>when</c> and the manifest's macro fields with the file header
/// (#1105), a prompt id with the prompt path (#1102), <c>.name</c> and the pipe
/// operand with the walker (#1105, #1059).
/// </summary>
public static partial class WorkflowPackageValidator
{
    /// <summary>The replacement sentences of § 8, one per retired key.</summary>
    private static class RetiredAt26
    {
        public const string ForEach = "a for node whose body holds the node (nodes[].for: \"item in snippet.<folder>\" | \"item in input.<id>\")";
        public const string NodeWhen = "an if node whose body holds the node (nodes[].if)";
        public const string Aggregate = "a deliverable names its sources (deliverables[].sources)";
        public const string DeliverableNode = "a deliverable names its sources (deliverables[].sources)";
        public const string Prompts = "a node names its prompt file (nodes[].prompt: \"<path>\")";
        public const string ForItem = "the instance's if on item.id (if: \"item.id == \\\"<item>\\\"\")";
        public const string GateWhen = "a gate is keyed if; when is a case branch's label only";
    }

    private static void ValidateAt26(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? catalog,
        List<string> errors,
        List<string> warnings)
    {
        ValidateRetiredAt26(manifest, files, errors);

        // The 25 pipeline holds at ≥ 25: a 25 retirement is still retired, a
        // 25 rule still rules — until a 26 issue replaces the step (#1100 the
        // nodes and the fan edges, #1101 the deliverables and the reachability,
        // #1102 the prompt, #1105 the macro library and the snippets, #1059 the
        // pipe operand). Its sentences keep the 25 wording, which is the 25 form.
        ValidateAt25(manifest, files, catalog, errors, warnings);
    }

    // ----- § 8: refused by name, with the replacement -------------------------

    private static void ValidateRetiredAt26(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files, List<string> errors)
    {
        if (manifest.Prompts != null)
        {
            errors.Add($"prompts is retired at specVersion 26; {RetiredAt26.Prompts}.");
        }

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (node.ForEach != null)
            {
                errors.Add($"Node '{node.Id}' declares forEach, which is retired at specVersion 26; {RetiredAt26.ForEach}.");
            }

            if (node.When != null)
            {
                errors.Add($"Node '{node.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.NodeWhen}.");
            }

            if (node.Aggregate != null)
            {
                errors.Add($"Node '{node.Id}' declares aggregate, which is retired at specVersion 26; {RetiredAt26.Aggregate}.");
            }

            foreach (var instance in node.Macros ?? new List<WorkflowNodeMacroSpec>())
            {
                if (instance.ForItem != null)
                {
                    errors.Add($"Node '{node.Id}' macro '{instance.Id}' declares forItem, which is retired at specVersion 26; {RetiredAt26.ForItem}.");
                }

                if (instance.When != null)
                {
                    errors.Add($"Node '{node.Id}' macro '{instance.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.GateWhen}.");
                }
            }
        }

        foreach (var deliverable in manifest.Deliverables ?? new List<WorkflowDeliverableSpec>())
        {
            if (deliverable.Node != null)
            {
                errors.Add($"Deliverable '{deliverable.Id}' declares node, which is retired at specVersion 26; {RetiredAt26.DeliverableNode}.");
            }

            if (deliverable.When != null)
            {
                errors.Add($"Deliverable '{deliverable.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.GateWhen}.");
            }

            foreach (var instance in deliverable.Macros ?? new List<WorkflowDeliverableMacroSpec>())
            {
                if (instance.When != null)
                {
                    errors.Add($"Deliverable '{deliverable.Id}' macro '{instance.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.GateWhen}.");
                }
            }
        }

        // A folder item's when sits in the folder's index.json, not the manifest:
        // resolve the snippet table as ResolveSnippetsAt25 will (its own sentences
        // are that step's — a folder that does not resolve is simply absent here).
        var snippets = WorkflowDataResolver.Resolve(manifest.Snippets, SnippetNoun, files, new List<string>());

        foreach (var (folder, collection) in snippets.Collections)
        {
            foreach (var item in collection.Items)
            {
                if (item.When != null)
                {
                    errors.Add($"Snippet folder '{folder}' item '{item.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.GateWhen}.");
                }
            }
        }
    }
}
