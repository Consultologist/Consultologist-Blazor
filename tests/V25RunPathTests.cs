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

    private static readonly Dictionary<string, ConsultInputValue> NewPatient = new(StringComparer.Ordinal)
    {
        ["notes"] = "Referral.",
        ["kind"] = "new",
    };

    /// <summary>The package as the store would hand it to the starter: results resolved by the store, the design catalogue, every file.</summary>
    internal static WorkflowPackage Package((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
    {
        var errors = new List<string>();
        var data = WorkflowDataResolver.ResolveContent(bundle.Manifest, bundle.Files, errors);
        Assert.Empty(errors);
        return new WorkflowPackage(
            bundle.Manifest,
            Nodes: bundle.Manifest.Nodes,
            Data: data,
            SourceFiles: bundle.Files,
            Results: WorkflowPackageStore.ResolveResultSet(bundle.Manifest),
            Formats: V25Package.Catalog(),
            FormatsRef: V25Package.Catalog().ResolvedRef);
    }

    [Fact]
    public void A25Classifier_IsARole_NotAKind_AndThePackageDecides()
    {
        var package = Package(V25Package.Minimal());
        WorkflowNodeSpec Node(string id) => package.Nodes!.First(n => n.Id == id);

        Assert.True(WorkflowNodeRoles.IsClassifierOf(Node("scope"), package));
        Assert.False(WorkflowNodeRoles.IsClassifierOf(Node("assessment"), package));
        Assert.True(WorkflowNodeRoles.IsCheckOf(Node("coverage"), package));
        Assert.False(WorkflowNodeRoles.IsCheckOf(Node("extract"), package));
        Assert.True(package.Nodes!.Any(node => WorkflowNodeRoles.IsClassifierOf(node, package)));
        Assert.Equal("coverage", ConsultCheckRefs.NodeIdOf("node.coverage"));
        Assert.Equal("coverage", ConsultCheckRefs.NodeIdOf("node:coverage"));
    }

    [Fact]
    public void TheFireSet_KeepsTheClassifierAndTheCheck_ThroughThePrune()
    {
        // Gate a node nobody needs so the prune runs: the enum-format
        // classifier survives as a declared one did, and the check rooted
        // through node.coverage (the dot spelling) survives with it.
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Prompts = manifest.Prompts!.Append(new WorkflowPromptSpec("extra", "prompts/extra.md", new List<string>())).ToList(),
            Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("extra", "Extra", Prompt: "extra", Format: "prose", When: "kind == follow_up")).ToList(),
            Deliverables = manifest.Deliverables!.Append(new WorkflowDeliverableSpec("extra_doc", "node:extra_all", "Extra", When: "kind == follow_up")).ToList(),
        };
        manifest = manifest with { Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("extra_all", "Extra all", Aggregate: new List<string> { "node:extra" })).ToList() };
        files = new Dictionary<string, string>(files, StringComparer.Ordinal) { ["prompts/extra.md"] = "{{ input.notes }}" };
        var package = Package((manifest, files));

        var decision = ConsultGenerationJobStarter.DecideFireSet(package, NewPatient, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal(new[] { "consult" }, decision.Firing.Select(r => r.Id));
        var live = decision.Package.Nodes!.Select(n => n.Id).ToList();
        Assert.Contains("scope", live);
        Assert.Contains("coverage", live);
        Assert.DoesNotContain("extra", live);
    }

    [Fact]
    public void AGatedComputedCheck_IsNotContent_AndItsDeliverableStillFires()
    {
        var package = Package((V25Package.WithNode(V25Package.Minimal().Manifest, "coverage", n => n with { When = "kind == follow_up" }), V25Package.Minimal().Files));

        var decision = ConsultGenerationJobStarter.DecideFireSet(package, NewPatient, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal(new[] { "consult" }, decision.Firing.Select(r => r.Id));
        Assert.DoesNotContain("coverage", decision.Package.Nodes!.Select(n => n.Id));
    }

    [Fact]
    public void ADeliverableWhoseMacroReadsAGatedNode_IsSkipped_NamingIt()
    {
        // scope_line (prepended on the deliverable) reads node.scope; gate
        // scope... a classifier may not carry a when, so gate a prose node the
        // macro reads instead.
        var (manifest, files) = WithDeliverableMacros();
        manifest = V25Package.WithNode(manifest, "assessment", n => n with { When = "kind == follow_up" });
        files["macros/scope_line.md"] = "Scope: {{ node.assessment.text }}.";
        var package = Package((manifest, files));

        var decision = ConsultGenerationJobStarter.DecideFireSet(package, NewPatient, new Dictionary<string, string> { ["scope"] = "in_scope" });

        var skipped = Assert.Single(decision.Skipped);
        Assert.Contains("needs node 'assessment'", skipped.Reason);
    }

    [Fact]
    public void PlacementWhens_AreJudgedByIdentity_OneRefusalPerInstance()
    {
        var (manifest, files) = WithDeliverableMacros();
        var result = Assert.Single(WorkflowPackageStore.ResolveResultSet(manifest)!);

        var (macros, excluded, placements) = ConsultGenerationJobStarter.DecidePlacementWhens(result, NewPatient, new Dictionary<string, string> { ["scope"] = "in_scope" });

        // The prepend of closing is gated on follow_up and refused; its append and scope_line stay.
        Assert.Equal(new[] { ("closing", "append"), ("scope_line", "prepend") }, placements!.Select(p => (p.Id, p.At!)));
        Assert.Equal(new[] { "closing", "scope_line" }, macros);
        var refusal = Assert.Single(excluded);
        Assert.Equal(("consult", "closing", "needs kind to be 'follow_up'; it is 'new'"), (refusal.ResultId, refusal.MacroId, refusal.Reason));

        var described = ConsultGenerationJobStarter.DescribeResult(result, macros, placements, null, new HashSet<string> { "scope" });
        Assert.Equal(new[] { "scope" }, described.Reads);
        Assert.Equal("node.coverage", described.Check);
        Assert.Equal(2, described.MacroPlacements!.Count);
    }

    [Fact]
    public void TheBoundary_DescribesStageTwo_AsTheStarterDescribedStageOne()
    {
        var package = Package(WithDeliverableMacros());
        var effective = new Dictionary<string, string> { ["notes"] = "Referral.", ["kind"] = "new" };

        var decision = DecideActivity.Decide(package, NewPatient, new Dictionary<string, string> { ["scope"] = "in_scope" }, effectiveInputs: effective);

        var consult = Assert.Single(decision.Results);
        Assert.Equal(new[] { "scope" }, consult.Reads);
        Assert.Equal("node.coverage", consult.Check);
        Assert.Equal(new[] { ("closing", "append"), ("scope_line", "prepend") }, consult.MacroPlacements!.Select(p => (p.Id, p.At!)));
        var assessment = decision.Nodes.First(n => n.Id == "assessment");
        Assert.Equal(new[] { "extract" }, assessment.Reads);
        Assert.NotNull(assessment.FormatHash);
        var coverage = decision.Nodes.First(n => n.Id == "coverage");
        Assert.Equal("The note names problems the source does not.", coverage.With!["failWith"]);
        var scope = decision.Nodes.First(n => n.Id == "scope");
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, scope.Values);
        Assert.Equal(new[] { "closing" }, decision.ExcludedMacros!.Select(e => e.MacroId));
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
