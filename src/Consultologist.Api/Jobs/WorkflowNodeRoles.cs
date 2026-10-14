using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v25 (B8 #1027): what a node IS, read the one way at every version. Below 25
/// a role is a declared kind (<c>kind: classifier | check</c>); at 25 the kinds
/// are retired and the role is the format's — an enum format classifies, a
/// computed format checks (the validator's own rule, <c>ClassifiersAt25</c>).
/// The starter's deciding decision, the fire set's prune and the engine's
/// closure read this, so a 25 classifier decides and survives the prune as a
/// declared one did.
/// </summary>
internal static class WorkflowNodeRoles
{
    public static bool IsClassifierOf(WorkflowNodeSpec node, WorkflowPackage package) =>
        WorkflowNodeKinds.IsClassifier(node)
        || (node.Format is { } format && SpecOf(format, package)?.Type == WorkflowInputTypes.Enum);

    public static bool IsCheckOf(WorkflowNodeSpec node, WorkflowPackage package) =>
        WorkflowNodeKinds.IsCheck(node)
        || (node.Format is { } format && SpecOf(format, package)?.Producer == WorkflowFormatProducers.Computed);

    private static WorkflowFormatSpec? SpecOf(string formatId, WorkflowPackage package) =>
        ConsultGenerationJobStarter.FormatSpecFor(formatId, package.Formats, package.SourceFiles, package.Manifest);
}

/// <summary>
/// The node a deliverable's <c>check</c> names, whichever way the manifest
/// spelled it: <c>node:&lt;id&gt;</c> below 25 (the binding grammar),
/// <c>node.&lt;id&gt;</c> at 25 (the object grammar). The resolved result and
/// the durable descriptor carry the reference as written; every reader
/// strips here.
/// </summary>
internal static class ConsultCheckRefs
{
    public static string NodeIdOf(string check) =>
        check.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal) ? check[WorkflowNodeBindingSources.NodePrefix.Length..]
        : check.StartsWith(TemplateObjects.Node + ".", StringComparison.Ordinal) ? check[(TemplateObjects.Node.Length + 1)..]
        : check;
}
