using System.Reflection;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1303 (package-format-v29-design.md § 1, § 2.2–§ 2.3): the scheduler core, in
/// its pure halves — a loop over a node's value lays out nothing at start and is
/// listed unresolved; its items come from the source's recorded output (id the
/// position, name the label and the position, value the element); the entity
/// settles it once; and a body template reads the element's fields.
/// </summary>
public class V29RunPathTests
{
    private const string Collection = "node.find_problems.value.problems";

    private static NodeRunResult Output(string raw) => new(raw, null, "in-hash", "out-hash", 6);

    // ----- the items, from the recorded output: § 2.2–§ 2.3 --------------------------------

    [Fact]
    public void TheItems_AreTheListAtThePath_InOrder_NamedByTheLoop()
    {
        var resolution = ConsultNodeValueLoops.ItemsOf(
            Output("""{ "summary": "Two problems.", "problems": [ { "name": "Asthma", "severity": "low" }, { "name": "Sepsis", "severity": "high" } ] }"""),
            Collection, "Problems");

        Assert.Null(resolution.Failure);
        Assert.Null(resolution.Reason);
        Assert.Equal(new[] { "1", "2" }, resolution.Items.Select(item => item["id"]));
        Assert.Equal(new[] { "Problems 1", "Problems 2" }, resolution.Items.Select(item => item["name"]));
        Assert.Contains("\"Sepsis\"", resolution.Items[1]["value"], StringComparison.Ordinal);
        Assert.Equal(ConsultNodeValueLoops.Json, resolution.Items[0][ConsultNodeValueLoops.ValueKind]);
    }

    [Fact]
    public void AListOfStrings_CarriesEachAsText()
    {
        var resolution = ConsultNodeValueLoops.ItemsOf(Output("""{ "concepts": [ "asthma", "copd" ] }"""), "node.extract.value.concepts", "Concepts");

        Assert.Equal(new[] { "asthma", "copd" }, resolution.Items.Select(item => item["value"]));
        Assert.All(resolution.Items, item => Assert.False(item.ContainsKey(ConsultNodeValueLoops.ValueKind)));
        Assert.Null(ConsultNodeValueLoops.ElementOf(resolution.Items[0]));
    }

    [Fact]
    public void AnEmptyList_IsALoopWithNoItems_AndAReason_NotAFailure()
    {
        var resolution = ConsultNodeValueLoops.ItemsOf(Output("""{ "summary": "", "problems": [] }"""), Collection, "Problems");

        Assert.Empty(resolution.Items);
        Assert.Null(resolution.Failure);
        Assert.Equal("no items: node.find_problems.value.problems was empty", resolution.Reason);
    }

    [Theory]
    [InlineData("""{ "summary": "x" }""", "Problems could not read its list: node.find_problems.value has no field 'problems'.")]
    [InlineData("""{ "problems": "none" }""", "Problems could not read its list: node.find_problems.value.problems is not a list.")]
    [InlineData("not json", "Problems could not read its list: find_problems's value is not JSON.")]
    public void AListThatCannotBeRead_IsAFailureSentence(string raw, string sentence)
    {
        var resolution = ConsultNodeValueLoops.ItemsOf(Output(raw), Collection, "Problems");

        Assert.Equal(sentence, resolution.Failure);
        Assert.Empty(resolution.Items);
    }

    [Fact]
    public void AListLongerThanAnArrayInputMayBe_FailsByName_NotTruncated()
    {
        var many = string.Join(", ", Enumerable.Range(1, ConsultGenerationJobs.MaxArrayElements + 1).Select(i => $"\"p{i}\""));
        var resolution = ConsultNodeValueLoops.ItemsOf(Output($$"""{ "problems": [ {{many}} ] }"""), Collection, "Problems");

        Assert.Empty(resolution.Items);
        Assert.Equal($"Problems could not run: node.find_problems.value.problems has {ConsultGenerationJobs.MaxArrayElements + 1} items, more than {ConsultGenerationJobs.MaxArrayElements}.", resolution.Failure);
    }

    [Fact]
    public void TheSource_IsReadFromTheCollection()
    {
        var source = ConsultNodeValueLoops.SourceOf(Collection)!.Value;
        Assert.Equal("find_problems", source.NodeId);
        Assert.Equal(new[] { "problems" }, source.Fields);
        Assert.Null(ConsultNodeValueLoops.SourceOf("snippet.sections"));
        Assert.Null(ConsultNodeValueLoops.SourceOf("node.find_problems.content"));
    }

    // ----- the start: § 1.1 -----------------------------------------------------------------

    private static WorkflowPackage Load() => Load(V29Package.Minimal());

    private static WorkflowPackage Load((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
    {
        var loaded = WorkflowPackageStore.Materialise("test@v29", bundle.Manifest,
            new Dictionary<string, string>(bundle.Files, StringComparer.Ordinal), null, V29Catalog.Catalog, V25Package.Catalog(), V29Package.Accepting29);
        return WorkflowPackageStore.PackageOf(loaded);
    }

    [Fact]
    public void AtStart_ANodeValuedLoop_LaysOutNoBlocks_AndIsListedUnresolved()
    {
        var package = Load();

        var skeleton = ConsultGenerationJobStarter.ResolveSkeleton(package, V26JobStartTests.NewPatient, V26JobStartTests.Scope(package, V26JobStartTests.NewPatient));

        // The folder loop and the assessment are laid out as at 28; the node loop adds nothing yet.
        Assert.Equal(new[] { "consult:assessment", "consult:sections:hpi", "consult:sections:plan" }, skeleton.Items.Select(item => item["id"]));
        Assert.Equal(new[] { "problems" }, skeleton.UnresolvedLoops);
        Assert.DoesNotContain(skeleton.CollectionRosters, roster => roster.CollectionId == Collection);
    }

    [Fact]
    public void TheCurrentEndpointsBlocks_DoNotTryToReadTheNodeLoopAsAFolder()
    {
        var blocks = WorkflowPackageBlocks.Resolve(Load());

        Assert.DoesNotContain(blocks, block => block.Id.StartsWith("consult:problems", StringComparison.Ordinal));
    }

    // ----- the entity: § 1.2 ------------------------------------------------------------------

    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) Job()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        var state = ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "consult:assessment", ["name"] = "Assessment" }
        });
        state.UnresolvedLoops = new List<string> { "problems" };
        state.Nodes = new List<ConsultNodeDescriptor> { new("problems", "Problems", null, Kind: "for", Collection: Collection, Ret: "draft_problem") };
        StateProperty.SetValue(entity, state);
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    private static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static ConsultGenerationLoopResolution TwoProblems() => new(
        "problems", Collection,
        new List<ConsultCollectionItem> { new("1", "Problems 1"), new("2", "Problems 2") },
        new List<ConsultCollectionItem> { new("consult:problems:1", "Problems 1"), new("consult:problems:2", "Problems 2") },
        3, At);

    [Fact]
    public async Task ResolveLoop_AddsTheBlocksAndRoster_RewritesTheTotal_AndStampsTheRow()
    {
        var (entity, state) = Job();

        await entity.ResolveLoop(TwoProblems());

        Assert.Equal(3, state().TotalBlockCount);
        Assert.Equal(new[] { "consult:assessment", "consult:problems:1", "consult:problems:2" }, state().Blocks.Keys);
        var roster = Assert.Single(state().Collections!);
        Assert.Equal(Collection, roster.CollectionId);
        Assert.Empty(state().UnresolvedLoops!);
        var row = Assert.Single(state().Nodes!);
        Assert.Equal(2, row.Count);
        Assert.Equal(At, row.ResolvedAtUtc);
        Assert.Contains(state().History, e => e.Kind == "loop-resolved" && e.Label == "Problems: 2 items");
    }

    [Fact]
    public async Task ResolveLoop_IsOncePerLoop()
    {
        var (entity, state) = Job();

        await entity.ResolveLoop(TwoProblems());
        await entity.ResolveLoop(TwoProblems() with { Total = 99 });

        Assert.Equal(3, state().TotalBlockCount);
        Assert.Single(state().Collections!);
    }

    [Fact]
    public async Task ResolveLoop_WithAnEmptyList_SaysWhy_AndAddsNothing()
    {
        var (entity, state) = Job();

        await entity.ResolveLoop(new ConsultGenerationLoopResolution(
            "problems", Collection, Array.Empty<ConsultCollectionItem>(), Array.Empty<ConsultCollectionItem>(), 1, At,
            "no items: node.find_problems.value.problems was empty"));

        Assert.Equal(1, state().TotalBlockCount);
        var row = Assert.Single(state().Nodes!);
        Assert.Equal(0, row.Count);
        Assert.Equal("no items: node.find_problems.value.problems was empty", row.Reason);
        Assert.Contains(state().History, e => e.Label == "Problems: no items: node.find_problems.value.problems was empty");
    }

    [Fact]
    public async Task ResolveLoop_AfterTheJobFailed_DoesNothing()
    {
        var (entity, state) = Job();
        state().Status = ConsultGenerationJobStatuses.Failed;

        await entity.ResolveLoop(TwoProblems());

        Assert.Equal(1, state().TotalBlockCount);
        Assert.Equal(new[] { "problems" }, state().UnresolvedLoops);
        Assert.Null(state().Nodes![0].Count);
    }

    // ----- the body reads the element: § 2.2 ----------------------------------------------

    [Fact]
    public void ABodyTemplate_ReadsTheElementsFields()
    {
        var package = Load();
        var item = ConsultNodeValueLoops.ItemsOf(
            Output("""{ "summary": "", "problems": [ { "name": "Asthma", "severity": "low" } ] }"""), Collection, "Problems").Items[0];
        var prompt = package.Nodes!.Single(n => n.Id == "draft_problem").Prompt!;
        var input = new ConsultPromptNodeActivityInput(
            "draft_problem", prompt, new Dictionary<string, string>(StringComparer.Ordinal) { ["notes"] = "Referral.", ["kind"] = "new" },
            "test@v29", Format: "prose", Producer: "attested", NodeTexts: new Dictionary<string, string>(),
            Item: item, ForEach: Collection,
            Run: new TemplateRunFacts("2026-11-07", "abcdef12", "test@v29", "api.example.test"),
            Profile: new TemplateProfileFacts("Dr Example", "Dr Example"));

        var rendered = ConsultRenderPayload.RenderAt25(input, package, package.Prompts![prompt].TemplateText);

        Assert.Equal("Problem 1: Asthma (low).", rendered.Trim());
    }

    // ----- #1304: a folder item gated on a node (design § 3.2) -----------------------------

    private static WorkflowPackage Gated(string @if = "node.scope.value == \"in_scope\"") =>
        Load(V29Package.With(_ => { }, files => files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: @if) + "Plan."));

    [Fact]
    public void AtStart_ANodeGatedItem_IsKept_AndCounted_NotJudged()
    {
        var package = Gated();

        var skeleton = ConsultGenerationJobStarter.ResolveSkeleton(package, V26JobStartTests.NewPatient, V26JobStartTests.Scope(package, V26JobStartTests.NewPatient));

        Assert.Contains("consult:sections:plan", skeleton.Items.Select(item => item["id"]));
        Assert.Equal(new[] { "hpi", "plan" }, skeleton.CollectionSets["sections"].Select(item => item["id"]));
        var gates = ConsultGenerationJobStarter.NodeGatedItems(package);
        Assert.Equal(new[] { "scope" }, gates["sections"]["plan"]);
    }

    [Fact]
    public void AtStart_AnInputGatedItem_IsStillJudgedThere()
    {
        var package = Gated("input.kind == \"follow_up\"");

        var skeleton = ConsultGenerationJobStarter.ResolveSkeleton(package, V26JobStartTests.NewPatient, V26JobStartTests.Scope(package, V26JobStartTests.NewPatient));

        Assert.Equal(new[] { "hpi" }, skeleton.CollectionSets["sections"].Select(item => item["id"]));
        Assert.Empty(ConsultGenerationJobStarter.NodeGatedItems(package));
    }

    private static ConsultFlowItemDecision DecideGate(WorkflowPackage package, string scope) =>
        DecideFlowItemActivity.Decide(package, new ConsultDecideFlowItemInput(
            "test@v29", "sections", "snippet.sections",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = "plan", ["label"] = "Plan", ["content"] = "Plan." },
            V26JobStartTests.Effective(V26JobStartTests.NewPatient),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = scope },
            null,
            new TemplateRunFacts("2026-11-07", "job00000000", "test@v29", "api.example.test"),
            new TemplateProfileFacts("Dr Example", "Dr Example"),
            GateFolder: "sections"));

    [Fact]
    public void TheGate_IsDecidedOverTheNodesValue_HeldOrSkippedWithItsText()
    {
        var package = Gated();

        var held = DecideGate(package, "in_scope");
        Assert.False(held.Skipped);
        Assert.Equal(("sections", "plan", "then"), (held.FlowId, held.ItemId, held.Branch));

        var skipped = DecideGate(package, "out_of_scope");
        Assert.True(skipped.Skipped);
        Assert.Equal("none", skipped.Branch);
        // #369: the condition's text, never the value it read.
        Assert.Equal("needs `node.scope.value == \"in_scope\"`; it does not hold", skipped.Reason);
    }

    // ----- #1305: the deliverable at 29 (design § 4; #1194) --------------------------------

    private static ConsultDeliverables.Deliverable Deliverable(string? check = null, IReadOnlyList<string>? reads = null, IReadOnlyList<string>? deliverableReads = null) =>
        new("consult", "Consultation note", "consult:", "", new[] { "assessment" }, 0,
            CheckNodeId: check, Reads: reads, Sources: new[] { "node.assessment" }, DeliverableReads: deliverableReads);

    private static readonly Dictionary<string, NodeRunResult> NoOutputs = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ConsultCheckOutcome> NoChecks = new(StringComparer.Ordinal);

    [Fact]
    public void AHeldDeliverable_SaysWhichWaitNeverEnded()
    {
        Assert.Equal("node 'scope', which its macros read, did not complete",
            ConsultCheckSettlement.HeldReason(Deliverable(reads: new[] { "scope" }), NoOutputs, NoChecks));
        Assert.Equal("deliverable 'letter', which it reads, was never composed",
            ConsultCheckSettlement.HeldReason(Deliverable(deliverableReads: new[] { "letter" }), NoOutputs, NoChecks,
                runDeliverables: new HashSet<string>(StringComparer.Ordinal) { "consult", "letter" }));
        Assert.Equal("its check 'coverage' never settled",
            ConsultCheckSettlement.HeldReason(Deliverable(check: "coverage"), NoOutputs, NoChecks));
    }

    [Fact]
    public void At29_ProducedMeansReleased_AndAHeldDeliverableIsNamed()
    {
        var deliverables = new[] { Deliverable(check: "coverage") };
        var composed = new HashSet<string>(StringComparer.Ordinal) { "consult" };

        // At 26 composing was producing — the reply leg then threw on the missing text.
        var at26 = ConsultDeliverables.FinalOutcome(deliverables, NoOutputs, new Dictionary<string, string>(), null, composed, null);
        Assert.Equal(ConsultGenerationJobStatuses.Completed, at26.Status);

        // At 29 it was held: the job fails, saying so.
        var at29 = ConsultDeliverables.FinalOutcome(deliverables, NoOutputs, new Dictionary<string, string>(), null, composed, null,
            releasedDeliverables: new HashSet<string>(StringComparer.Ordinal),
            heldDeliverables: new Dictionary<string, string> { ["consult"] = "Consultation note was composed but held: its check 'coverage' never settled." });
        Assert.Equal((ConsultGenerationJobStatuses.Failed, "Consultation note was composed but held: its check 'coverage' never settled."), at29);

        // Released — recorded, or refused by its check — is produced.
        var released = ConsultDeliverables.FinalOutcome(deliverables, NoOutputs, new Dictionary<string, string>(), null, composed, null,
            releasedDeliverables: new HashSet<string>(StringComparer.Ordinal) { "consult" });
        Assert.Equal(ConsultGenerationJobStatuses.Completed, released.Status);
    }

    private static readonly DateTimeOffset Composed = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static ConsultHeldDocument Held(string? reason = null) =>
        new("consult", "Consultation note", 0, "abc123", new[] { "node.assessment" }, Composed, reason);

    [Fact]
    public void MarkDeliverableComposed_WritesTheRow_AndTheReleaseMovesIt()
    {
        var (entity, state) = Job();

        entity.MarkDeliverableComposed(Held());
        var row = Assert.Single(state().HeldDocuments!);
        Assert.Equal(("abc123", (string?)null), (row.ComposedHash, row.Reason));
        Assert.Contains(state().History, e => e.Kind == "composed");

        entity.CompleteResultDocument(new ConsultGenerationResultDocument("consult", "Consultation note", "The note.", 0, ComposedHash: "abc123", Sources: new[] { "node.assessment" })).GetAwaiter().GetResult();
        Assert.Empty(state().HeldDocuments!);
    }

    [Fact]
    public void ACheckRefusal_AlsoMovesTheRowOut()
    {
        var (entity, state) = Job();
        entity.MarkDeliverableComposed(Held());

        entity.RecordFailedDocument(new ConsultFailedDocument("consult", "Consultation note", "The note names problems the source does not."));

        Assert.Empty(state().HeldDocuments!);
    }

    [Fact]
    public void AHeldRow_IsServedOnAFailedJob_WithItsReason()
    {
        var (entity, state) = Job();
        entity.MarkDeliverableComposed(Held());
        entity.MarkDeliverableComposed(Held("its check 'coverage' never settled"));
        state().Status = ConsultGenerationJobStatuses.Failed;

        var response = state().ToResponse();

        var held = Assert.Single(response.HeldDocuments!);
        Assert.Equal("its check 'coverage' never settled", held.Reason);
        Assert.Contains(state().History, e => e.Kind == "held" && e.Label == "Consultation note was composed but held: its check 'coverage' never settled");

        // An ended job takes no more.
        entity.MarkDeliverableComposed(Held("again"));
        Assert.Equal("its check 'coverage' never settled", Assert.Single(state().HeldDocuments!).Reason);
    }
}

/// <summary>The agents catalogue the run-path tests load packages against.</summary>
internal static class V29Catalog
{
    public static readonly OutputContractCatalog Catalog = Load();

    private static OutputContractCatalog Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return OutputContractCatalog.Load(Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents"));
    }
}
