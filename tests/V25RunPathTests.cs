using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// B8 #1027: the 25 run path — a 25 manifest's deliverables[] resolved by the
/// store (placements with their at, whens keyed (id, at), the check as
/// written), a deliverable's reads from its placed macros, the deciding stage
/// over a 25 classifier, the checked deliverable over a computed node, and the
/// deliverable macros rendered and composed.
/// </summary>
public class V25RunPathTests
{
    /// <summary>The design's minimal manifest with a gated prepend and an unconditional append of one macro on its deliverable, and a second macro reading the classifier.</summary>
    internal static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithDeliverableMacros()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Macros = manifest.Macros!.Append(new WorkflowMacroSpec("scope_line", "Scope line", "macros/scope_line.md", Instruction: "x")).ToList(),
            Deliverables = new List<WorkflowDeliverableSpec>
            {
                new("consult", "node:all", "Consultation note", Check: "node.coverage", Macros: new List<WorkflowDeliverableMacroSpec>
                {
                    new("closing", At: WorkflowDeliverableMacroSpec.Prepend, When: "kind == follow_up"),
                    new("closing", At: WorkflowDeliverableMacroSpec.Append),
                    new("scope_line", At: WorkflowDeliverableMacroSpec.Prepend),
                }),
            },
        };
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["macros/scope_line.md"] = "Scope: {{ node.scope.text }}.",
        };
        return (manifest, files);
    }

    [Fact]
    public void TheStore_ResolvesDeliverables_WithTheirPlacementsAndIdentities()
    {
        var (manifest, files) = WithDeliverableMacros();
        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));

        var result = Assert.Single(WorkflowPackageStore.ResolveResultSet(manifest)!);

        Assert.Equal(("consult", "all", "Consultation note"), (result.Id, result.NodeId, result.Label));
        Assert.Equal("node.coverage", result.Check);
        Assert.Equal(new[] { "closing", "scope_line" }, result.Macros);
        Assert.Equal(new[] { ("closing", "prepend"), ("closing", "append"), ("scope_line", "prepend") }, result.MacroPlacements!.Select(p => (p.Id, p.At!)));
        Assert.All(result.MacroPlacements!, p => Assert.Null(p.Before));
        var gate = Assert.Single(result.MacroConditions!);
        Assert.Equal(("closing", "prepend"), (gate.MacroId, gate.At!));
    }

    [Fact]
    public void TheStore_ResolvesTheMinimalPackage_WithNoMacros_AndNullBelowSeven()
    {
        var (manifest, _) = V25Package.Minimal();

        var result = Assert.Single(WorkflowPackageStore.ResolveResultSet(manifest)!);
        Assert.Null(result.Macros);
        Assert.Null(result.MacroPlacements);
        Assert.Null(result.MacroConditions);

        // No results, no deliverables, no result: nothing to resolve, never a null-reference throw.
        Assert.Null(WorkflowPackageStore.ResolveResultSet(manifest with { Deliverables = null }));
    }

    [Fact]
    public void ADeliverablesReads_AreItsPlacedMacrosNodeReads()
    {
        var (manifest, files) = WithDeliverableMacros();

        var reads = WorkflowDerivedEdges.DeliverableReads(manifest, files, V25Package.Catalog());

        Assert.Equal(new[] { "scope" }, reads["consult"]);
        Assert.Empty(WorkflowDerivedEdges.DeliverableReads(V25Package.Minimal().Manifest, V25Package.Minimal().Files, V25Package.Catalog())["consult"]);
    }
}
