using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v25 (B8 #1027, design § 5.1): a deliverable's placed macros, as the
/// orchestrator asks for them — the placements that survived their gates,
/// and the scope only the orchestrator holds: the effective inputs, the
/// outputs of the nodes the macros read (the deliverable's edges) and of its
/// aggregator, the folders as settled at job start, the run and profile facts.
/// The package the activity re-resolves supplies the rest (declarations,
/// format types, the macro texts and instructions).
/// </summary>
public sealed record ConsultDeliverableMacrosActivityInput(
    string ResultId,
    string WorkflowPackage,
    IReadOnlyList<ConsultMacroPlacement> Placements,
    Dictionary<string, string> Variables,
    Dictionary<string, string>? VariableTypes,
    IReadOnlyDictionary<string, string> NodeTexts,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? Folders,
    TemplateRunFacts Run,
    TemplateProfileFacts Profile,
    // v26 (#1155, design § 3.1): the deliverables the placed macros read as
    // deliverable.<id>.content — composed; null for one skipped by its if.
    // Null below 26 and when none. Appended last, positional rule.
    IReadOnlyDictionary<string, string?>? Deliverables = null);

/// <summary>One placed macro rendered: its id and its text.</summary>
public sealed record ConsultRenderedMacro(string Id, string Text);

/// <summary>
/// What the deliverable composes around its sections: the prepend texts in
/// placement order, the append texts in theirs, and whether a placed macro
/// read <c>profile.signature</c> (the 25 signature carrier the finish reads).
/// </summary>
public sealed record ConsultDeliverableMacrosResult(
    IReadOnlyList<ConsultRenderedMacro> Prepend,
    IReadOnlyList<ConsultRenderedMacro> Append,
    bool SignatureRead);

/// <summary>
/// v25 (B8 #1027): a deliverable's prepend/append macros rendered over the
/// job's 25 scope — in an activity, as every 25 template renders, because the
/// orchestrator cannot build the scope and a Scriban builtin with a clock
/// would not replay. Pure over its payload and the package it re-resolves; a
/// render failure throws without a Durable retry (the same bytes would fail
/// again).
/// </summary>
public sealed class RenderDeliverableMacrosActivity
{
    private readonly IWorkflowPackageStore _packageStore;
    private readonly ILogger<RenderDeliverableMacrosActivity> _logger;

    public RenderDeliverableMacrosActivity(IWorkflowPackageStore packageStore, ILogger<RenderDeliverableMacrosActivity> logger)
    {
        _packageStore = packageStore;
        _logger = logger;
    }

    [Function(ConsultGenerationActivityNames.RenderDeliverableMacros)]
    public async Task<ConsultDeliverableMacrosResult> RunAsync(
        [ActivityTrigger] ConsultDeliverableMacrosActivityInput input,
        CancellationToken cancellationToken)
    {
        if (!WorkflowPackageRef.TryParse(input.WorkflowPackage, out var packageRef))
        {
            throw new InvalidOperationException($"Deliverable '{input.ResultId}' has no usable workflow package ref ('{input.WorkflowPackage}').");
        }

        var package = await _packageStore.ResolveAsync(packageRef!, cancellationToken);
        var result = Render(input, package);

        _logger.LogInformation(
            "Deliverable macros rendered. ResultId={ResultId}, Prepend={Prepend}, Append={Append}, SignatureRead={SignatureRead}",
            input.ResultId,
            result.Prepend.Count,
            result.Append.Count,
            result.SignatureRead);

        return result;
    }

    /// <summary>The pure half: every placement's macro text rendered over the scope, sorted into its placement.</summary>
    internal static ConsultDeliverableMacrosResult Render(ConsultDeliverableMacrosActivityInput input, WorkflowPackage package)
    {
        var scope = ConsultRenderPayload.ScopeFor(
            input.Variables, input.VariableTypes, input.NodeTexts, input.Folders, item: null, forEach: null, input.Run, input.Profile, package,
            deliverables: input.Deliverables);
        // v26 (#1177, design § 2.2): a macro gated off is off everywhere — a placed
        // macro's text reading an inert macro's .content gets empty. (An inert
        // macro's own placement never reaches here: the fire set excluded it.)
        if (package.Manifest.SpecVersion >= 26)
        {
            scope = scope with { InertMacros = ConsultGatedScope.InertMacroKeys(scope) };
            // v26 (#1179): and the snippets' — an inert snippet or a gated-off item is off here too.
            scope = ConsultGatedScope.JudgeSnippets(scope, package, input.Folders);
        }

        var prepend = new List<ConsultRenderedMacro>();
        var append = new List<ConsultRenderedMacro>();

        foreach (var placement in input.Placements)
        {
            if (!scope.Macros.TryGetValue(placement.Id, out var source))
            {
                throw new InvalidOperationException($"Deliverable '{input.ResultId}' places macro '{placement.Id}', which the package does not carry.");
            }

            var rendered = new ConsultRenderedMacro(placement.Id, TemplateRenderer.Render($"Macro '{placement.Id}'", source.Text, scope, TemplatePipes.Engine));
            (string.Equals(placement.At, WorkflowDeliverableMacroSpec.Prepend, StringComparison.Ordinal) ? prepend : append).Add(rendered);
        }

        var signatureReaders = package.SourceFiles is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : WorkflowDerivedEdges.SignatureReaders(package.Manifest, package.SourceFiles, package.Formats);

        return new ConsultDeliverableMacrosResult(prepend, append, input.Placements.Any(p => signatureReaders.Contains(p.Id)));
    }
}
