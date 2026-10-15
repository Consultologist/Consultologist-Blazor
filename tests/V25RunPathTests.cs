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
                new("consult", "node.all", "Consultation note", Check: "node.coverage", Macros: new List<WorkflowDeliverableMacroSpec>
                {
                    new("closing", At: WorkflowDeliverableMacroSpec.Prepend, When: "input.kind == \"follow_up\""),
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
            Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("extra", "Extra", Prompt: "extra", Format: "prose", When: "input.kind == \"follow_up\"")).ToList(),
            Deliverables = manifest.Deliverables!.Append(new WorkflowDeliverableSpec("extra_doc", "node:extra_all", "Extra", When: "input.kind == \"follow_up\"")).ToList(),
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
        var package = Package((V25Package.WithNode(V25Package.Minimal().Manifest, "coverage", n => n with { When = "input.kind == \"follow_up\"" }), V25Package.Minimal().Files));

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
        manifest = V25Package.WithNode(manifest, "assessment", n => n with { When = "input.kind == \"follow_up\"" });
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

    // ----- the orchestrator's seams -----

    private static readonly Dictionary<string, ConsultNodeDescriptor> RunNodes = new(StringComparer.Ordinal)
    {
        ["extract"] = new("extract", "Extract", Format: "concept-list", Producer: "attested"),
        ["scope"] = new("scope", "Scope", Format: "classification", Producer: "attested", Values: new[] { "in_scope", "out_of_scope" }),
        ["coverage"] = new("coverage", "Coverage", Format: "terms-subset", Producer: "computed", With: new Dictionary<string, string> { ["of"] = "extract", ["in"] = "extract", ["failWith"] = "The note names problems the source does not." }),
        ["all"] = new("all", "Everything", Aggregate: new List<string> { "node:assessment" }),
    };

    [Fact]
    public void TheDeliverableTable_ReadsTheCheckInEitherSpelling_AndDropsOneNotInTheRun()
    {
        var at25 = Assert.Single(ConsultDeliverables.Resolve(
            new[] { new ConsultResultDescriptor("consult", "all", "Consultation note", Check: "node.coverage", Reads: new[] { "scope" }) }, null, RunNodes));
        Assert.Equal("coverage", at25.CheckNodeId);
        Assert.Equal(new[] { "scope" }, at25.Reads);

        var below = Assert.Single(ConsultDeliverables.Resolve(
            new[] { new ConsultResultDescriptor("consult", "all", "Consultation note", Check: "node:coverage") }, null, RunNodes));
        Assert.Equal("coverage", below.CheckNodeId);

        // A check gated out of the run (its when refused) leaves the table's
        // reference null: the deliverable composes unchecked instead of waiting forever.
        var gatedOut = Assert.Single(ConsultDeliverables.Resolve(
            new[] { new ConsultResultDescriptor("consult", "all", "Consultation note", Check: "node:coverage") }, null,
            RunNodes.Where(p => p.Key != "coverage").ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)));
        Assert.Null(gatedOut.CheckNodeId);
    }

    [Fact]
    public void ACheckSettles_FromTheComputedNodesRecord_At25_AndSpeaksItsFailWith()
    {
        var outcome = new ConsultCheckOutcome(false, new[] { "Cough" }, Array.Empty<string>());
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["coverage"] = new("""{"pass":false,"uncovered":["Cough"],"untested":[]}""", null, "i", "o", 6, Check: outcome),
            ["extract"] = new("[]", Array.Empty<ClinicalConcept>(), "i", "o"),
        };

        Assert.Same(outcome, ConsultCheckSettlement.RecordedOutcomeOf(RunNodes["coverage"], outputs));
        Assert.Null(ConsultCheckSettlement.RecordedOutcomeOf(RunNodes["extract"], outputs));
        Assert.Equal("The note names problems the source does not.", ConsultCheckSettlement.FailWithOf(RunNodes["coverage"]));
        Assert.Equal("Fails.", ConsultCheckSettlement.FailWithOf(new ConsultNodeDescriptor("c", "C", Check: new ConsultCheckDescriptor("terms-subset", "node:a", "node:b", "Fails."))));
    }

    [Fact]
    public void ADeliverableReleases_OnlyOnceItsReadsAndItsCheckHaveSettled()
    {
        var checked_ = new ConsultDeliverables.Deliverable("consult", "Consultation note", "consult:", "all", new[] { "assessment" }, 0, CheckNodeId: "coverage", Reads: new[] { "scope" });
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal) { ["all"] = new("The note.", null, "i", "o") };
        var settled = new Dictionary<string, ConsultCheckOutcome>(StringComparer.Ordinal);

        // A read not yet settled holds it.
        Assert.False(ConsultCheckSettlement.IsReleaseReady(checked_, outputs, settled, out _));
        outputs["scope"] = new("in_scope", null, "i", "o", Classification: "in_scope");
        // The check not yet settled holds it.
        Assert.False(ConsultCheckSettlement.IsReleaseReady(checked_, outputs, settled, out _));
        // A check settled BEFORE the deliverable was stashed releases it on the next pass (the ≤24 hang).
        settled["coverage"] = new ConsultCheckOutcome(true, null, null);
        Assert.True(ConsultCheckSettlement.IsReleaseReady(checked_, outputs, settled, out var outcome));
        Assert.True(outcome!.Passed);

        var unchecked_ = checked_ with { CheckNodeId = null, Reads = null };
        Assert.True(ConsultCheckSettlement.IsReleaseReady(unchecked_, outputs, settled, out var none));
        Assert.Null(none);
    }

    [Fact]
    public void TheActivity_RendersEachPlacement_OverTheJobsScope_AndSortsItByItsAt()
    {
        var package = Package(WithDeliverableMacros());
        var input = new ConsultDeliverableMacrosActivityInput(
            "consult", package.Ref,
            new[] { new ConsultMacroPlacement("closing", At: "append"), new ConsultMacroPlacement("scope_line", At: "prepend") },
            new Dictionary<string, string> { ["notes"] = "Referral.", ["kind"] = "new" }, null,
            new Dictionary<string, string> { ["scope"] = "in_scope", ["all"] = "The note." }, null,
            new TemplateRunFacts("2026-10-14", "job", package.Ref, "api.example.test"),
            new TemplateProfileFacts("Dr Example", "Dr Example, FRCPC"));

        var result = RenderDeliverableMacrosActivity.Render(input, package);

        Assert.Equal(new[] { ("scope_line", "Scope: in_scope.") }, result.Prepend.Select(m => (m.Id, m.Text)));
        Assert.Equal(new[] { ("closing", "Closing: Dr Example.") }, result.Append.Select(m => (m.Id, m.Text)));
        Assert.False(result.SignatureRead);

        // A read the orchestrator did not thread is a loud render failure, never an empty block.
        var missing = input with { NodeTexts = new Dictionary<string, string>() };
        Assert.Throws<InvalidOperationException>(() => RenderDeliverableMacrosActivity.Render(missing, package));
    }

    [Fact]
    public void TheActivity_ReportsAPlacedMacroReadingTheSignature()
    {
        var (manifest, files) = WithDeliverableMacros();
        files["macros/closing.md"] = "{{ profile.signature }}";
        var package = Package((manifest, files));
        var input = new ConsultDeliverableMacrosActivityInput(
            "consult", package.Ref, new[] { new ConsultMacroPlacement("closing", At: "append") },
            new Dictionary<string, string> { ["notes"] = "Referral.", ["kind"] = "new" }, null,
            new Dictionary<string, string>(), null,
            new TemplateRunFacts("2026-10-14", "job", package.Ref, "api.example.test"),
            new TemplateProfileFacts("Dr Example", "Dr Example, FRCPC"));

        var result = RenderDeliverableMacrosActivity.Render(input, package);

        Assert.True(result.SignatureRead);
        Assert.Equal("Dr Example, FRCPC", Assert.Single(result.Append).Text);
    }

    [Fact]
    public void ComposeAt25_PlacesPrependThenSectionsThenAppend_AndAttributesInDocumentOrder()
    {
        var parts = new ConsultAggregateRenderer.Part[] { new ConsultAggregateRenderer.ScalarPart("The plan.") };

        var (text, appended) = ConsultMacroExpander.ComposeAt25(
            new[] { new ConsultRenderedMacro("scope_line", "Scope: in_scope.\n") },
            parts,
            new[] { new ConsultRenderedMacro("closing", "Closing.") });

        Assert.Equal("Scope: in_scope.\n\nThe plan.\n\nClosing.", text);
        Assert.Equal(new[] { "scope_line", "closing" }, appended!.Select(e => e.Id));
        Assert.All(appended, e => Assert.Equal("macro", e.Kind));

        var (bare, none) = ConsultMacroExpander.ComposeAt25(Array.Empty<ConsultRenderedMacro>(), parts, Array.Empty<ConsultRenderedMacro>());
        Assert.Equal(ConsultAggregateRenderer.Render(parts), bare);
        Assert.Null(none);
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
