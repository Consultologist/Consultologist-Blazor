using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1154 (package-format-v26-design.md § 1, § 3, § 14): a 26 package at job
/// start over the SAME skeleton — a loop is the fan it replaces (its body
/// nodes fan under its collection), an if/case is decided where a 25 when was
/// (at start over inputs, at the boundary over a node's value), the untaken
/// body pruned and the flow row kept with its branch, a flow node's edges its
/// decision's reads, the blocks from the deliverables' sources named by the
/// loop, a folder item's `if` judged with the item in hand.
/// </summary>
public class V26JobStartTests
{
    internal static readonly Dictionary<string, ConsultInputValue> NewPatient = new(StringComparer.Ordinal)
    {
        ["notes"] = "Referral.",
        ["kind"] = "new",
    };

    internal static readonly Dictionary<string, ConsultInputValue> FollowUp = new(StringComparer.Ordinal)
    {
        ["notes"] = "Referral.",
        ["kind"] = "follow_up",
    };

    internal static IReadOnlyDictionary<string, string> Effective(IReadOnlyDictionary<string, ConsultInputValue> supplied) =>
        supplied.ToDictionary(p => p.Key, p => p.Value.HasCanonical ? p.Value.Canonical : p.Value.AsJson(), StringComparer.Ordinal);

    /// <summary>The start-time scope — the effective map from the supplied one, no node texts, the folders settled.</summary>
    internal static ConsultConditionScope Scope(WorkflowPackage package, IReadOnlyDictionary<string, ConsultInputValue> supplied) =>
        ConsultGenerationJobStarter.StartScope(package, Effective(supplied), supplied, "job00000000");

    /// <summary>
    /// The design's minimal 26 package as the store will hand it to the starter
    /// once the bump runs it: the manifest and files as published (the walk
    /// resolves the headers itself), the snippets resolved THROUGH the headers
    /// (an item's label and gate on the item — the store's own read is #1157's),
    /// the deliverables resolved by the store's resolver, the fixture catalogue.
    /// </summary>
    internal static WorkflowPackage Package(
        Func<WorkflowPackageManifest, WorkflowPackageManifest>? change = null,
        Action<Dictionary<string, string>>? withFiles = null)
    {
        var (manifest, files) = V26Package.Minimal();
        manifest = change?.Invoke(manifest) ?? manifest;
        withFiles?.Invoke(files);

        Assert.Equal(new List<string>(), V26Package.Errors((manifest, files)));

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
            FormatsRef: V25Package.Catalog().ResolvedRef);
    }

    private static WorkflowFlowBodySpec Body(string? ret, params string[] nodes) => new(nodes.ToList(), ret);

    /// <summary>#1101: a flow node a test adds feeds the deliverable as one more source.</summary>
    internal static WorkflowPackageManifest Sourcing(WorkflowPackageManifest m, string source) =>
        m with { Deliverables = m.Deliverables!.Select(d => d with { Sources = (d.Sources ?? new List<string>()).Append(source).ToList() }).ToList() };

    /// <summary>The minimal with an if node `triage` over two prose nodes, sourced by the deliverable; else optional.</summary>
    internal static WorkflowPackageManifest WithTriage(WorkflowPackageManifest m, string condition = "input.kind == \"new\"", bool withElse = true)
    {
        var nodes = new List<WorkflowNodeSpec> { new("full_workup", "Full work-up", Prompt: "prompts/extract.md", Format: "prose") };
        if (withElse)
        {
            nodes.Add(new("brief_update", "Brief update", Prompt: "prompts/extract.md", Format: "prose"));
        }

        nodes.Add(new("triage", "Triage", If: condition, Then: Body("node.full_workup", "full_workup"), Else: withElse ? Body("node.brief_update", "brief_update") : null));
        return Sourcing(V26Package.WithNodes(m, nodes.ToArray()), "node.triage");
    }

    /// <summary>The minimal with a case node `route` — over input.kind by default (new → plan, follow_up → letter), or over node.scope.value (in_scope → plan, out_of_scope → letter).</summary>
    internal static WorkflowPackageManifest WithRoute(WorkflowPackageManifest m, bool onNode = false, bool withElse = false)
    {
        var (first, second) = onNode ? ("in_scope", "out_of_scope") : ("new", "follow_up");
        var nodes = new List<WorkflowNodeSpec> { new("plan", "Plan", Prompt: "prompts/extract.md", Format: "prose") };
        var branches = new List<WorkflowCaseBranchSpec> { new(new List<string> { first }, new List<string> { "plan" }, "node.plan") };
        WorkflowFlowBodySpec? @else = null;
        if (withElse)
        {
            nodes.Add(new("hold", "Hold", Prompt: "prompts/extract.md", Format: "prose"));
            @else = Body("node.hold", "hold");
        }
        else
        {
            nodes.Add(new("letter", "Letter", Prompt: "prompts/extract.md", Format: "prose"));
            branches.Add(new(new List<string> { second }, new List<string> { "letter" }, "node.letter"));
        }

        nodes.Add(new("route", "Route", Case: onNode ? "node.scope.value" : "input.kind", Branches: branches, Else: @else));
        return Sourcing(V26Package.WithNodes(m, nodes.ToArray()), "node.route");
    }

    // ----- the edges and the gating reads ----------------------------------------------

    [Fact]
    public void TheDerivedEdges_CarryTheCaseSubject_NotTheBodies()
    {
        var package = Package(m => WithTriage(WithRoute(m, onNode: true)));

        Assert.True(ConsultGenerationJobStarter.IsAt26(package));
        Assert.True(ConsultGenerationJobStarter.IsAt25(package));
        var derived = ConsultGenerationJobStarter.DeriveEdges(package)!;

        Assert.Equal(new[] { "scope" }, derived["route"].Order());
        Assert.Empty(derived["triage"]);
        Assert.Empty(derived["sections"]);
    }

    [Fact]
    public void GatingReads_IncludeTheCaseSubject_AndAnIfReadingTheProfile_DecidesAtTheBoundary()
    {
        Assert.Equal(new[] { "scope" }, ConsultGenerationJobStarter.GatingReads(Package(m => WithRoute(m, onNode: true)))!.Order());
        Assert.Empty(ConsultGenerationJobStarter.GatingReads(Package(m => WithRoute(m)))!);
        Assert.Empty(ConsultGenerationJobStarter.GatingReads(Package(m => WithTriage(m)))!);

        Assert.True(ConsultGenerationJobStarter.GatingReadsProfile(Package(m => WithTriage(m, "profile.name != \"\""))));
        Assert.False(ConsultGenerationJobStarter.GatingReadsProfile(Package(m => WithTriage(m))));
    }

    // ----- the rows ---------------------------------------------------------------------

    [Fact]
    public void TheBodyDescriptor_FansUnderItsLoop_AndTheLoopIsAFlowRow()
    {
        var package = Package();
        var flows = ConsultGenerationJobStarter.FlowsOf(package);
        var derived = ConsultGenerationJobStarter.DeriveEdges(package)!;

        var body = ConsultGenerationJobStarter.DescribeNode(
            package.Nodes!.First(n => n.Id == "section_notes"), null, formats: package.Formats, reads: derived["section_notes"], flows: flows);
        Assert.Equal("snippet.sections", body.ForEach);
        Assert.Equal("sections", body.In);
        Assert.Null(body.Flow);
        Assert.Equal("prose", body.Format);

        var loop = ConsultGenerationJobStarter.DescribeNode(
            package.Nodes!.First(n => n.Id == "sections"), null, formats: package.Formats, reads: derived["sections"], flows: flows);
        Assert.Equal(("for", "snippet.sections", "section_notes"), (loop.Flow, loop.Collection, loop.Ret));
        Assert.Equal(new[] { "section_notes" }, loop.Body);
        Assert.Null(loop.Reads);
        Assert.Null(loop.Format);
        Assert.Null(loop.PromptId);
        Assert.Null(loop.ForEach);
        Assert.Null(loop.Branch);

        Assert.Equal(new[] { "section_notes" }, ConsultGenerationJobStarter.ChainSteps(package).Select(step => step.Id));
    }

    // ----- the decision at start -------------------------------------------------------------

    [Fact]
    public void AnIfNode_DecidedAtStart_PrunesTheUntakenBody_AndKeepsTheRow()
    {
        var package = Package(m => WithTriage(m));

        var then = ConsultGenerationJobStarter.DecideFireSet(package, NewPatient, null, Scope(package, NewPatient));
        Assert.Equal(new[] { "consult" }, then.Firing.Select(r => r.Id));
        Assert.Equal("then", then.Branches!["triage"]);
        Assert.Contains(then.Package.Nodes!, n => n.Id == "triage");
        Assert.Contains(then.Package.Nodes!, n => n.Id == "full_workup");
        Assert.DoesNotContain(then.Package.Nodes!, n => n.Id == "brief_update");

        var @else = ConsultGenerationJobStarter.DecideFireSet(package, FollowUp, null, Scope(package, FollowUp));
        Assert.Equal("else", @else.Branches!["triage"]);
        Assert.Contains(@else.Package.Nodes!, n => n.Id == "brief_update");
        Assert.DoesNotContain(@else.Package.Nodes!, n => n.Id == "full_workup");

        var flows = ConsultGenerationJobStarter.FlowsOf(package);
        var row = ConsultGenerationJobStarter.DescribeNode(package.Nodes!.First(n => n.Id == "triage"), null, formats: package.Formats, flows: flows, branch: @else.Branches["triage"]);
        Assert.Equal(("if", "input.kind == \"new\"", "brief_update", "else"), (row.Flow, row.Expression, row.Ret, row.Branch));
        Assert.Equal(new[] { "full_workup", "brief_update" }, row.Body);
    }

    [Fact]
    public void AnIfNode_WithNoElse_TakesNone_AndTheDeliverableSourcingItIsSkipped()
    {
        var package = Package(m => WithTriage(m, withElse: false));

        var decision = ConsultGenerationJobStarter.DecideFireSet(package, FollowUp, null, Scope(package, FollowUp));

        Assert.Empty(decision.Firing);
        var skipped = Assert.Single(decision.Skipped);
        Assert.Equal(("consult", "source node.triage took no branch"), (skipped.ResultId, skipped.Reason));
        Assert.Equal("none", decision.Branches!["triage"]);
    }

    [Fact]
    public void ACaseNode_OverAnInput_TakesTheBranchListingTheValue()
    {
        var package = Package(m => WithRoute(m));

        var decision = ConsultGenerationJobStarter.DecideFireSet(package, FollowUp, null, Scope(package, FollowUp));

        Assert.Equal("follow_up", decision.Branches!["route"]);
        Assert.Contains(decision.Package.Nodes!, n => n.Id == "letter");
        Assert.DoesNotContain(decision.Package.Nodes!, n => n.Id == "plan");
        Assert.Contains(decision.Package.Nodes!, n => n.Id == "route");
    }

    [Fact]
    public void ACaseNode_WithAnElse_TakesIt_WhenNoBranchListsTheValue()
    {
        var package = Package(m => WithRoute(m, withElse: true));

        var decision = ConsultGenerationJobStarter.DecideFireSet(package, FollowUp, null, Scope(package, FollowUp));

        Assert.Equal("else", decision.Branches!["route"]);
        Assert.Contains(decision.Package.Nodes!, n => n.Id == "hold");
        Assert.DoesNotContain(decision.Package.Nodes!, n => n.Id == "plan");
    }

    [Fact]
    public void ACaseOnANodeValue_DecidesAtTheBoundary()
    {
        var package = Package(m => WithRoute(m, onNode: true));
        var facts = (Run: ConsultRenderPayload.RunFactsFor(new DateTime(2026, 1, 2), "job", package.Ref, "host"), Profile: ConsultRenderPayload.ProfileFactsFor("Dr Example", null));

        var decision = DecideActivity.Decide(package, NewPatient, new Dictionary<string, string>(), effectiveInputs: Effective(NewPatient),
            nodeTexts: new Dictionary<string, string> { ["scope"] = "out_of_scope" }, run: facts.Run, profile: facts.Profile);

        var route = decision.Nodes.First(n => n.Id == "route");
        Assert.Equal(("case", "node.scope.value", "letter", "out_of_scope"), (route.Flow, route.Expression, route.Ret, route.Branch));
        Assert.Equal(new[] { "scope" }, route.Reads);
        Assert.Contains(decision.Nodes, n => n.Id == "letter");
        Assert.DoesNotContain(decision.Nodes, n => n.Id == "plan");
        Assert.Equal(new[] { "section_notes" }, decision.ItemSteps.Select(step => step.Id));
        Assert.Contains(decision.Items, item => item["id"] == "consult:route");
        var consult = Assert.Single(decision.Results);
        Assert.Equal(new[] { "assessment", "sections", "route" }, consult.Sources);
    }

    // ----- the skeleton -------------------------------------------------------------------

    [Fact]
    public void TheSkeleton_BlocksComeFromSources_NamedByTheLoop()
    {
        var package = Package();

        var skeleton = ConsultGenerationJobStarter.ResolveSkeleton(package, NewPatient, Scope(package, NewPatient));

        Assert.Equal(new[] { "consult:assessment", "consult:sections:hpi", "consult:sections:plan" }, skeleton.Items.Select(item => item["id"]));
        Assert.Equal(new[] { "Assessment", "HPI", "Plan" }, skeleton.Items.Select(item => item["name"]));
        Assert.Equal(new[] { "sections" }, skeleton.CollectionSets.Keys.Order());
        var roster = Assert.Single(skeleton.CollectionRosters);
        Assert.Equal(new[] { ("hpi", "HPI"), ("plan", "Plan") }, roster.Items.Select(item => (item.Id, item.Name)));
        Assert.Empty(skeleton.EmptyFanLabels);
    }

    [Fact]
    public void AFolderItemIf_IsJudgedAtStart_WithTheItemInHand()
    {
        var package = Package(withFiles: files => files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: "input.kind == \"new\" && item.id == \"plan\"") + "Plan.");

        var settled = ConsultGenerationJobStarter.ResolveSkeleton(package, FollowUp, Scope(package, FollowUp));
        Assert.Equal(new[] { "consult:assessment", "consult:sections:hpi" }, settled.Items.Select(item => item["id"]));
        Assert.Equal(new[] { "hpi" }, settled.CollectionSets["sections"].Select(item => item["id"]));

        var kept = ConsultGenerationJobStarter.ResolveSkeleton(package, NewPatient, Scope(package, NewPatient));
        Assert.Equal(new[] { "hpi", "plan" }, kept.CollectionSets["sections"].Select(item => item["id"]));
    }

    [Fact]
    public void AnInputArrayLoop_FansTheSuppliedElements_AndAnEmptyOneRefusesByLabel()
    {
        var package = Package(
            m => Sourcing(V26Package.WithNodes(
                m with { Inputs = m.Inputs!.Append(new WorkflowInputSpec("prior_notes", "Prior notes", Required: false, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text)).ToList() },
                new("summarise", "Summarise", Prompt: "prompts/summarise.md", Format: "prose"),
                new("notes", "Notes", For: "item in input.prior_notes", Nodes: new List<string> { "summarise" }, Ret: "node.summarise")), "node.notes"),
            files => files["prompts/summarise.md"] = "Summarise: {{ item.value }}");
        var supplied = new Dictionary<string, ConsultInputValue>(NewPatient, StringComparer.Ordinal)
        {
            ["prior_notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("Seen in clinic."), ConsultInputValue.OfText("Follow-up.") }),
        };

        var fanned = ConsultGenerationJobStarter.ResolveSkeleton(package, supplied, Scope(package, supplied));
        Assert.Equal(new[] { "consult:assessment", "consult:sections:hpi", "consult:sections:plan", "consult:notes:0", "consult:notes:1" }, fanned.Items.Select(item => item["id"]));
        Assert.Equal(new[] { "Prior notes 1", "Prior notes 2" }, fanned.Items.Skip(3).Select(item => item["name"]));
        Assert.Equal(new[] { "0", "1" }, fanned.CollectionSets["input.prior_notes"].Select(item => item["id"]));

        var flows = ConsultGenerationJobStarter.FlowsOf(package);
        var body = ConsultGenerationJobStarter.DescribeNode(package.Nodes!.First(n => n.Id == "summarise"), null, formats: package.Formats, flows: flows);
        Assert.Equal(("input.prior_notes", "notes"), (body.ForEach, body.In));

        var empty = ConsultGenerationJobStarter.ResolveSkeleton(package, NewPatient, Scope(package, NewPatient));
        Assert.Equal(new[] { "'Prior notes'" }, empty.EmptyFanLabels);
        Assert.Empty(empty.Items);
    }
}
