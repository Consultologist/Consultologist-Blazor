using System.Reflection;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// v10 step (e) (#496, package-format-v10-design.md § 5): the boundary. The
/// entity's Decide and RecordDecisionFailure, the decision as a pure
/// function, and what a job carries before and after.
/// </summary>
public class DecisionBoundaryEntityTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static readonly DateTimeOffset At = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State, IConsultGenerationJobIndexStore Index) Fresh()
    {
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var entity = new ConsultGenerationJobEntity(index);
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!, index);
    }

    private static ConsultGenerationJobInitialize Deciding() =>
        new("job-1", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), "general@v2026.09.1", "hash",
            Nodes: new[] { new ConsultNodeDescriptor("scope", "Scope", "classify", OutputContract: OutputContracts.Classification, Values: new[] { "in_scope", "out_of_scope" }) },
            Deciding: true);

    private static ConsultGenerationDecision Decision(int blocks = 2) =>
        new(
            Enumerable.Range(0, blocks).Select(i => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = $"consult:s{i}", ["name"] = $"Section {i}" }).ToList(),
            new[] { new ConsultNodeDescriptor("assemble-note", "Assemble", Aggregate: new[] { "node:s0" }) },
            new[] { new ConsultResultDescriptor("consult", "assemble-note", "Consultation note") },
            new[] { new ConsultSkippedDocument("letter", "Decline letter", "needs node:scope to be 'out_of_scope'; it is 'in_scope'") },
            null,
            null,
            new Dictionary<string, string> { ["scope"] = "in_scope" },
            At);

    [Fact]
    public async Task ADecidingJob_StartsWithNoCount_AndSaysSo()
    {
        var (entity, state, index) = Fresh();

        await entity.Initialize(Deciding());

        Assert.True(state().Deciding);
        Assert.Null(state().DecidedAtUtc);
        Assert.Equal(0, state().TotalBlockCount);
        Assert.Empty(state().Blocks);
        var response = state().ToResponse();
        Assert.True(response.Deciding);
        Assert.Null(response.DecidedAtUtc);
        Assert.Null(response.StartFailure);
        await index.Received(1).UpsertAsync(Arg.Is<ConsultGenerationJobIndexEntry>(e => e.Deciding && !e.FailedAtStart), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AJobWithoutClassifiers_IsDecidedAtStart()
    {
        var (entity, state, _) = Fresh();

        await entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1",
            new[] { (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "a", ["name"] = "A" } }));

        Assert.Null(state().Deciding);
        Assert.Equal(state().CreatedAtUtc, state().DecidedAtUtc);
        Assert.Equal(1, state().TotalBlockCount);
        Assert.False(state().ToIndexEntry().Deciding);
    }

    [Fact]
    public async Task Decide_StampsEverythingOnce()
    {
        var (entity, state, index) = Fresh();
        await entity.Initialize(Deciding());
        index.ClearReceivedCalls();

        await entity.Decide(Decision());

        var s = state();
        Assert.Equal(2, s.TotalBlockCount);
        Assert.Equal(2, s.Blocks.Count);
        Assert.Equal(At, s.DecidedAtUtc);
        Assert.Equal("assemble-note", Assert.Single(s.Nodes!).Id);
        Assert.Equal("letter", Assert.Single(s.SkippedDocuments!).ResultId);
        Assert.Equal("in_scope", s.Classifications!["scope"]);
        Assert.Contains(s.History, e => e.Kind == "decided" && e.Label == "Decided: 2 sections in 1 documents");
        Assert.False(s.ToIndexEntry().Deciding);
        Assert.Equal(2, s.ToResponse().TotalBlockCount);
        await index.Received(1).UpsertAsync(Arg.Is<ConsultGenerationJobIndexEntry>(e => e.TotalBlockCount == 2 && !e.Deciding), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Decide_IsWriteOnce_TheFirstWriterWins()
    {
        var (entity, state, _) = Fresh();
        await entity.Initialize(Deciding());

        await entity.Decide(Decision(blocks: 2));
        await entity.Decide(Decision(blocks: 5) with { DecidedAtUtc = At.AddMinutes(1) });

        Assert.Equal(2, state().TotalBlockCount);
        Assert.Equal(At, state().DecidedAtUtc);
        Assert.Single(state().History, e => e.Kind == "decided");
    }

    [Fact]
    public async Task ADecisionFailure_IsBornFailed_WithItsKindAndTheAnswers()
    {
        var (entity, state, index) = Fresh();
        await entity.Initialize(Deciding());

        await entity.RecordDecisionFailure(new ConsultGenerationDecisionFailure(
            ConsultGenerationDecisionFailureKinds.NothingApplied,
            "No document applies after classification. 'Consultation note' needs node:scope to be 'in_scope'; it is 'out_of_scope'.",
            new Dictionary<string, string> { ["scope"] = "out_of_scope" },
            new[] { new ConsultSkippedDocument("consult", "Consultation note", "needs node:scope to be 'in_scope'; it is 'out_of_scope'") }));

        var s = state();
        Assert.Equal(ConsultGenerationJobStatuses.Failed, s.Status);
        Assert.NotNull(s.StartFailure);
        Assert.Null(s.FailureError);
        Assert.Equal(ConsultGenerationDecisionFailureKinds.NothingApplied, s.DecisionFailureKind);
        Assert.Equal("out_of_scope", s.Classifications!["scope"]);
        Assert.Equal(0, s.TotalBlockCount);
        Assert.NotNull(s.CompletedAtUtc);
        var response = s.ToResponse();
        Assert.Equal(ConsultGenerationDecisionFailureKinds.NothingApplied, response.DecisionFailureKind);
        Assert.Equal("out_of_scope", response.Classifications!["scope"]);
        await index.Received().UpsertAsync(Arg.Is<ConsultGenerationJobIndexEntry>(e => e.FailedAtStart && !e.Deciding && e.DecisionFailureKind == "nothing-applied"), Arg.Any<CancellationToken>());

        // Nothing moves it afterwards.
        await entity.Decide(Decision());
        Assert.Equal(0, state().TotalBlockCount);
        Assert.Null(state().DecidedAtUtc);
    }

    [Fact]
    public void MarkNodeCompleted_RecordsAClassifiersAnswer()
    {
        var (entity, state, _) = Fresh();
        StateProperty.SetValue(entity, ConsultGenerationJobState.Create("job-1", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>()));

        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate("scope", "Scope", null, "in", "out", 1, 3, 5, Classification: "in_scope"));

        Assert.Equal("in_scope", state().NodeOutputs!["scope"].Classification);
        Assert.Equal("in_scope", state().Classifications!["scope"]);
        Assert.Equal("in_scope", state().ToResponse().NodeOutputs!["scope"].Classification);
    }
}

public class DecideActivityTests
{
    private static WorkflowPackage ClassifierPackage(string when, string? secondWhen = null)
    {
        var (manifest, files) = V10Fixtures.WithClassifier();
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);
        Assert.True(WorkflowResultConditions.TryParseExpression(when, out var condition, out var error), error);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            SchemaContracts: TestOutputContracts.CatalogSchemas,
            Data: data,
            Results: Results(condition, secondWhen));
    }

    private static List<WorkflowResolvedResult> Results(WorkflowConditionExpression? first, string? secondWhen)
    {
        var results = new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Consultation note", first) };
        if (secondWhen != null)
        {
            Assert.True(WorkflowResultConditions.TryParseExpression(secondWhen, out var second, out var error), error);
            results.Add(new("letter", "assemble-note", "Decline letter", second));
        }
        return results;
    }

    private static readonly Dictionary<string, ConsultInputValue> Supplied = new(StringComparer.Ordinal)
    {
        ["consult_draft"] = "65M, adenocarcinoma of the lung, for chemoradiation.",
        ["seen_on"] = "2026-08-10",
        ["encounter_kind"] = "follow_up"
    };

    [Fact]
    public void TheAnswerThatHolds_DecidesTheSkeleton_AndKeepsTheClassifier()
    {
        var decision = DecideActivity.Decide(ClassifierPackage("node:scope == in_scope"), Supplied,
            new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal("consult", Assert.Single(decision.Results).Id);
        Assert.Empty(decision.Skipped);
        Assert.NotEmpty(decision.Items);
        Assert.Contains(decision.Nodes, node => node.Id == "scope" && node.OutputContract == OutputContracts.Classification);
        Assert.Contains(decision.Nodes, node => node.Id == "assemble-note");
        Assert.Empty(decision.EmptyFanLabels);
    }

    [Fact]
    public void TheAnswerThatDoesNotHold_LeavesNothing_AndSaysWhy()
    {
        var decision = DecideActivity.Decide(ClassifierPackage("node:scope == in_scope"), Supplied,
            new Dictionary<string, string> { ["scope"] = "out_of_scope" });

        Assert.Empty(decision.Results);
        Assert.Empty(decision.Items);
        var skipped = Assert.Single(decision.Skipped);
        Assert.Equal("needs node:scope to be 'in_scope'; it is 'out_of_scope'", skipped.Reason);
    }

    [Fact]
    public void ThePrune_KeepsTheClassifier_WhenADocumentIsSkipped()
    {
        // Two documents over one answer: the prune that drops the skipped
        // one's private nodes must not drop the classifier that decided it.
        var decision = DecideActivity.Decide(ClassifierPackage("node:scope == in_scope", "node:scope == out_of_scope"), Supplied,
            new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal("consult", Assert.Single(decision.Results).Id);
        Assert.Equal("letter", Assert.Single(decision.Skipped).ResultId);
        Assert.Contains(decision.Nodes, node => node.Id == "scope");
    }

    [Fact]
    public void TheDecision_IsTheStartersOwnFireSet()
    {
        // The control: the same code path the starter uses at start, so a
        // condition over inputs alone decides identically at the boundary.
        var package = ClassifierPackage("encounter_kind == follow_up");
        var atStart = ConsultGenerationJobStarter.DecideFireSet(package, Supplied, null);
        var atBoundary = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal(atStart.Firing.Select(r => r.Id), atBoundary.Results.Select(r => r.Id));
        Assert.Equal(ConsultGenerationJobStarter.ResolveSkeleton(atStart.Package, Supplied).Items.Select(i => i["id"]), atBoundary.Items.Select(i => i["id"]));
    }
}
