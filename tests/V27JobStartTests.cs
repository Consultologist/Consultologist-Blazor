using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1235 (package-format-v27-design.md § 5): the start-time side of a flow node
/// inside a for — the fire set leaves an inner if/case undecided (the engine
/// decides it once per item), its row carries in and the loop's fan, a branch
/// node fans under the loop two levels up, the steps hold the branch nodes, the
/// skeleton is the 26 one (the count kept). Through the seam until the bump.
/// </summary>
public class V27JobStartTests
{
    /// <summary>The 27 package as the store hands it to the starter — the 26 harness over <see cref="V27Package"/>.</summary>
    internal static WorkflowPackage Package(
        Func<(WorkflowPackageManifest Manifest, Dictionary<string, string> Files), (WorkflowPackageManifest Manifest, Dictionary<string, string> Files)>? change = null)
    {
        var bundle = change?.Invoke(V27Package.Minimal()) ?? V27Package.Minimal();
        var (manifest, files) = bundle;

        Assert.Equal(new List<string>(), V27Package.Errors((manifest, files)));

        var errors = new List<string>();
        var texts = WorkflowTextHeaders.Resolve(manifest, files, errors);
        var data = WorkflowDataResolver.ResolveContent(texts.Manifest, texts, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            Data: data,
            SourceFiles: files,
            Results: WorkflowPackageStore.ResolveResultSet(manifest),
            Formats: V25Package.Catalog(),
            FormatsRef: V25Package.Catalog().ResolvedRef,
            Texts: texts);
    }

    internal static WorkflowPackage Triaged(string condition = "item.id == \"hpi\"") =>
        Package(b => V27Package.WithTriageInLoop(b, condition));

    [Fact]
    public void TheFireSet_LeavesTheInnerFlowUndecided_AndPrunesNothing()
    {
        var package = Triaged();

        var decision = ConsultGenerationJobStarter.DecideFireSet(package, V26JobStartTests.NewPatient, null, V26JobStartTests.Scope(package, V26JobStartTests.NewPatient));

        Assert.Equal(new[] { "consult" }, decision.Firing.Select(r => r.Id));
        Assert.Empty(decision.Skipped);
        Assert.DoesNotContain("triage", decision.Branches?.Keys ?? Enumerable.Empty<string>());
        Assert.Contains(decision.Package.Nodes!, n => n.Id == "triage");
        Assert.Contains(decision.Package.Nodes!, n => n.Id == "patient_draft");
        Assert.Contains(decision.Package.Nodes!, n => n.Id == "table_draft");
    }

    [Fact]
    public void TheRows_ABranchNodeFansUnderTheLoop_AndTheInnerFlowRowCarriesInAndTheFan()
    {
        var package = Triaged();
        var flows = ConsultGenerationJobStarter.FlowsOf(package);
        var derived = ConsultGenerationJobStarter.DeriveEdges(package)!;

        var branchNode = ConsultGenerationJobStarter.DescribeNode(package.Nodes!.First(n => n.Id == "patient_draft"), null, formats: package.Formats, reads: derived["patient_draft"], flows: flows);
        Assert.Equal(("snippet.sections", "triage", "prose"), (branchNode.ForEach, branchNode.In, branchNode.Format));
        Assert.Equal(new[] { "section_notes" }, branchNode.Reads);

        var inner = ConsultGenerationJobStarter.DescribeNode(package.Nodes!.First(n => n.Id == "triage"), null, formats: package.Formats, reads: derived["triage"], flows: flows);
        Assert.Equal(("if", "item.id == \"hpi\"", "sections", "snippet.sections"), (inner.Kind, inner.Expression, inner.In, inner.ForEach));
        Assert.Equal(new[] { "patient_draft", "table_draft" }, inner.Body);
        Assert.Null(inner.Ret);
        Assert.Null(inner.Branch);
        Assert.Null(inner.Branches);
        Assert.Null(inner.Reads);

        var loop = ConsultGenerationJobStarter.DescribeNode(package.Nodes!.First(n => n.Id == "sections"), null, formats: package.Formats, reads: derived["sections"], flows: flows);
        Assert.Equal(("for", "snippet.sections", "triage"), (loop.Kind, loop.Collection, loop.Ret));
        Assert.Equal(new[] { "section_notes", "triage" }, loop.Body);
        Assert.Null(loop.In);
        Assert.Null(loop.ForEach);

        // The served row (storage 9 = the 8 row + Branches): the working ForEach is projected away, In stays.
        Assert.Null(inner.AsRecordRowAt8().ForEach);
        Assert.Equal("sections", inner.AsRecordRowAt8().In);
    }

    [Fact]
    public void TheSteps_HoldTheBranchNodes_NotTheInnerFlow_AndTheSkeletonIsThe26One()
    {
        var package = Triaged();

        Assert.Equal(new[] { "section_notes", "patient_draft", "table_draft" }, ConsultGenerationJobStarter.ChainSteps(package).Select(step => step.Id));

        var skeleton = ConsultGenerationJobStarter.ResolveSkeleton(package, V26JobStartTests.NewPatient, V26JobStartTests.Scope(package, V26JobStartTests.NewPatient));
        Assert.Equal(new[] { "consult:assessment", "consult:sections:hpi", "consult:sections:plan" }, skeleton.Items.Select(item => item["id"]));
        Assert.Equal(new[] { "sections" }, skeleton.CollectionSets.Keys.Order());
    }

    [Fact]
    public void AnInnerConditionReadingANodesValue_IsNoGatingRead_AndTheBoundaryLeavesItUndecided()
    {
        var package = Triaged("node.scope.value == \"in_scope\"");

        Assert.Empty(ConsultGenerationJobStarter.GatingReads(package)!);
        Assert.Equal(new[] { "scope" }, ConsultGenerationJobStarter.DeriveEdges(package)!["triage"].Order());

        var decision = DecideActivity.Decide(package, V26JobStartTests.NewPatient, new Dictionary<string, string>(), effectiveInputs: V26JobStartTests.Effective(V26JobStartTests.NewPatient),
            nodeTexts: new Dictionary<string, string> { ["scope"] = "in_scope" });
        var row = decision.Nodes.First(n => n.Id == "triage");
        Assert.Null(row.Branch);
        Assert.Null(row.Branches);
        Assert.Contains(decision.Nodes, n => n.Id == "patient_draft");
        Assert.Contains(decision.Nodes, n => n.Id == "table_draft");
    }

    [Fact]
    public void ACaseOverTheItemsField_InALoopOverAnArrayInput_FansTheBranchNodes()
    {
        var package = Package(b => V27Package.WithRouteInLoop(b));
        var flows = ConsultGenerationJobStarter.FlowsOf(package);

        var plan = ConsultGenerationJobStarter.DescribeNode(package.Nodes!.First(n => n.Id == "plan"), null, formats: package.Formats, flows: flows);
        Assert.Equal(("input.topics", "route"), (plan.ForEach, plan.In));
        var route = ConsultGenerationJobStarter.DescribeNode(package.Nodes!.First(n => n.Id == "route"), null, formats: package.Formats, flows: flows);
        Assert.Equal(("case", "item.value.kind", "topic_sections", "input.topics"), (route.Kind, route.Expression, route.In, route.ForEach));
    }
}
