using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v25 (#1038, design § 16): what a 25 <c>when</c> is judged over at the start
/// and at the boundary — the render scope a template reads, completed from the
/// package: the effective inputs typed as declared, the node outputs the stage
/// in hand holds (none at a start, every scalar stage-one output at the
/// boundary), the settled folders, the run and profile facts. A node instance's
/// when is judged at the node's own completion over the activity's scope, not
/// here. The judge is <see cref="WorkflowConditionAt25.Holds"/>; the reason the
/// fixed form of <see cref="WorkflowConditionAt25.Reason"/> (provenance@v2026.10.2).
/// </summary>
internal sealed record ConsultConditionScope(
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyDictionary<string, string>? VariableTypes,
    IReadOnlyDictionary<string, string> NodeTexts,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? Folders,
    TemplateRunFacts Run,
    TemplateProfileFacts Profile,
    WorkflowPackage Package)
{
    private TemplateRenderScope? _scope;

    private TemplateRenderScope Scope =>
        _scope ??= ConsultRenderPayload.ScopeFor(Variables, VariableTypes, NodeTexts, Folders, null, null, Run, Profile, Package);

    /// <summary>Whether the condition holds over this scope; loud, with the subject, on anything the validator did not promise.</summary>
    public bool Holds(string subject, string when) => WorkflowConditionAt25.Holds(subject, when, Scope, TemplatePipes.Engine);

    public static string Reason(string when) => WorkflowConditionAt25.Reason(when);

    /// <summary>The scope with the folders settled — folder items are judged over a folder-less scope first (their whens read inputs only), then the fans and the gating whens see the settled set.</summary>
    public ConsultConditionScope WithFolders(IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> folders) =>
        this with { Folders = folders, _scope = null };

    /// <summary>The declared input types, every input (the renderer types a text as text).</summary>
    public static IReadOnlyDictionary<string, string>? TypesOf(WorkflowPackage package) =>
        package.Manifest.Inputs?.ToDictionary(input => input.Id, WorkflowInputTypes.Of, StringComparer.Ordinal);
}
