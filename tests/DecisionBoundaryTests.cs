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
        var entity = new ConsultGenerationJobEntity(index, Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
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
    public async Task ADecidingJob_KeepsTheStartersRecord_ThroughTheOrchestratorsInitialize()
    {
        // Initialize is called twice: the starter's rich signal, then the
        // orchestrator's sparse one. A deciding job has no blocks until the
        // boundary, and testing Blocks.Count for "fresh" made the second call
        // re-create the state — silently dropping everything only the starter
        // stamps. #623 found it live: MacroChoices, title, tags, spec version
        // all null on every deciding job's record.
        var (entity, state, _) = Fresh();

        await entity.Initialize(Deciding() with
        {
            PackageTitle = "The demo",
            PackageTags = new[] { "demo" },
            PackageSpecVersion = 12,
            MacroChoices = new Dictionary<string, ConsultMacroChoice>(StringComparer.Ordinal)
            {
                ["followup"] = new(true, ConsultMacroChoiceOrigins.Chosen)
            }
        });
        // The orchestrator's shape: items empty (deciding), none of the
        // starter-only members.
        await entity.Initialize(Deciding());

        Assert.Equal("The demo", state().PackageTitle);
        Assert.Equal(new[] { "demo" }, state().PackageTags);
        Assert.Equal(12, state().PackageSpecVersion);
        var choice = Assert.Single(state().MacroChoices!);
        Assert.Equal("followup", choice.Key);
        Assert.Equal(new ConsultMacroChoice(true, ConsultMacroChoiceOrigins.Chosen), choice.Value);
        var response = state().ToResponse();
        Assert.Equal("The demo", response.PackageTitle);
        Assert.NotNull(response.MacroChoices);
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
    public void TheBoundary_CarriesTheSignatureFlag_OntoTheDescriptors()
    {
        // v11 #516: a deciding job's deliverables come from Decide — the
        // signed flag must survive this door too.
        var package = ClassifierPackage("node:scope == in_scope");
        package = package with
        {
            Results = package.Results!.Select(r => r with { Signature = true }).ToList()
        };

        var decision = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.True(Assert.Single(decision.Results).Signature);
    }

    [Fact]
    public void TheBoundary_CarriesTheMacroIds_OntoTheDescriptors()
    {
        // v11 #513: a deciding job's deliverables come from Decide, not the
        // starter — the macro list must survive this door too. The texts
        // themselves are on the orchestration input (top-level manifest facts,
        // untouched by the fire-set narrowing).
        var package = ClassifierPackage("node:scope == in_scope");
        package = package with
        {
            Results = package.Results!.Select(r => r with { Macros = new[] { "closing" } }).ToList()
        };

        var decision = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal(new[] { "closing" }, Assert.Single(decision.Results).Macros);
    }

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

    private static WorkflowPackage WithCheckChain(WorkflowPackage package, bool onFiring)
    {
        // The § 13 chain: a document-terms extraction over the aggregator and
        // a check gating one of the two results.
        var nodes = new List<WorkflowNodeSpec>(package.Manifest.Nodes!)
        {
            new("extract-document-terms", "Extracting document terms",
                Prompt: "extract-patient-concepts",
                Bindings: new Dictionary<string, WorkflowBindingValue> { ["consult_draft"] = new("node:assemble-note") },
                Output: new WorkflowNodeOutputSpec("concept-list")),
            new("coverage", "Coverage check",
                Kind: WorkflowNodeKinds.Check,
                Op: WorkflowCheckOps.TermsSubset,
                Of: "node:extract-patient-concepts",
                In: "node:extract-document-terms",
                FailWith: "The note does not cover every clinical term found in the referral.")
        };
        var manifest = package.Manifest with { Nodes = nodes };

        return package with
        {
            Manifest = manifest,
            Nodes = nodes,
            Results = package.Results!
                .Select(r => (onFiring ? r.Id == "consult" : r.Id == "letter") ? r with { Check = "node:coverage" } : r)
                .ToList()
        };
    }

    [Fact]
    public void ThePrune_KeepsTheCheckChain_OfAFiringResult()
    {
        // v12 #624: the check hangs OFF the firing result (of/in edges point
        // check → operands), so without its own roots the prune would drop
        // the whole chain the moment anything is skipped — an ungated
        // document, silently.
        var package = WithCheckChain(
            ClassifierPackage("node:scope == in_scope", "node:scope == out_of_scope"),
            onFiring: true);

        var decision = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal("consult", Assert.Single(decision.Results).Id);
        Assert.Equal("node:coverage", decision.Results[0].Check);
        Assert.Contains(decision.Nodes, node => node.Id == "coverage");
        Assert.Contains(decision.Nodes, node => node.Id == "extract-document-terms");
    }

    [Fact]
    public void ThePrune_DropsTheCheckChain_OfASkippedResult()
    {
        // Skip stays skip: a when-excluded deliverable never runs its check,
        // and its private chain goes with it.
        var package = WithCheckChain(
            ClassifierPackage("node:scope == in_scope", "node:scope == out_of_scope"),
            onFiring: false);

        var decision = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal("consult", Assert.Single(decision.Results).Id);
        Assert.Equal("letter", Assert.Single(decision.Skipped).ResultId);
        Assert.DoesNotContain(decision.Nodes, node => node.Id == "coverage");
        Assert.DoesNotContain(decision.Nodes, node => node.Id == "extract-document-terms");
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

/// <summary>
/// v12 rung (i) (#631, design § 14): the boundary judges every macro when —
/// classifier and input-only clauses alike — over the firing results, strips
/// in lockstep, records what it excluded, and never judges a skipped
/// result's entries.
/// </summary>
public class DecisionBoundaryMacroWhenTests
{
    private static readonly Dictionary<string, ConsultInputValue> Supplied = new(StringComparer.Ordinal)
    {
        ["consult_draft"] = "65M, adenocarcinoma of the lung, for chemoradiation.",
        ["seen_on"] = "2026-08-10",
        ["encounter_kind"] = "follow_up"
    };

    private static WorkflowPackage MatchCasePackage(
        string? resultWhen = null,
        string? letterWhen = null,
        params (string MacroId, string When)[] gates)
    {
        var (manifest, files) = V10Fixtures.WithClassifier();
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        var conditions = gates
            .Select(gate =>
            {
                Assert.True(WorkflowResultConditions.TryParseExpression(gate.When, out var condition, out var error), error);
                return new WorkflowResolvedMacroCondition(gate.MacroId, condition!);
            })
            .ToList();
        var results = new List<WorkflowResolvedResult>
        {
            new("consult", "assemble-note", "Consultation note",
                Parse(resultWhen),
                Macros: gates.Select(g => g.MacroId).ToList(),
                MacroPlacements: gates.Select(g => new ConsultMacroPlacement(g.MacroId, Before: "node:section-instructions")).ToList(),
                MacroConditions: conditions.Count > 0 ? conditions : null)
        };

        if (letterWhen != null)
        {
            results.Add(new("letter", "assemble-note", "Decline letter",
                Parse(letterWhen),
                Macros: new[] { "letter_closing" },
                MacroConditions: new[] { new WorkflowResolvedMacroCondition("letter_closing", Parse("node:scope == in_scope")!) }));
        }

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            SchemaContracts: TestOutputContracts.CatalogSchemas,
            Data: data,
            Results: results);
    }

    private static WorkflowConditionExpression? Parse(string? when)
    {
        if (when is null)
        {
            return null;
        }

        Assert.True(WorkflowResultConditions.TryParseExpression(when, out var condition, out var error), error);
        return condition;
    }

    [Fact]
    public void TheBoundary_PicksOneArm_NamesTheOther_AndStripsInLockstep()
    {
        var package = MatchCasePackage(gates: new[]
        {
            ("arm_in", "node:scope == in_scope"),
            ("arm_out", "node:scope == out_of_scope")
        });

        var decision = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        var descriptor = Assert.Single(decision.Results);
        Assert.Equal(new[] { "arm_in" }, descriptor.Macros);
        Assert.Equal("arm_in", Assert.Single(descriptor.MacroPlacements!).Id);
        var excluded = Assert.Single(decision.ExcludedMacros!);
        Assert.Equal(("consult", "arm_out"), (excluded.ResultId, excluded.MacroId));
        Assert.Equal("needs node:scope to be 'out_of_scope'; it is 'in_scope'", excluded.Reason);
    }

    [Fact]
    public void AnInputOnlyWhen_IsJudgedAtTheBoundary_Too()
    {
        // One construct, one evaluation moment: a deciding job's input-only
        // clauses wait for the boundary with the classifier ones.
        var package = MatchCasePackage(gates: new[]
        {
            ("follow_up_note", "encounter_kind == follow_up"),
            ("new_patient_note", "encounter_kind == new_patient")
        });

        var decision = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal(new[] { "follow_up_note" }, Assert.Single(decision.Results).Macros);
        var excluded = Assert.Single(decision.ExcludedMacros!);
        Assert.Equal("new_patient_note", excluded.MacroId);
        Assert.Equal("needs encounter_kind to be 'new_patient'; it is 'follow_up'", excluded.Reason);
    }

    [Fact]
    public void ASkippedResult_NeverEvaluatesItsMacros_AndNothingIsRecorded()
    {
        // Skip stays skip: the letter leaves by its when, and its macro gate
        // — which would fail — is never judged, never recorded.
        var package = MatchCasePackage(
            resultWhen: "node:scope == in_scope",
            letterWhen: "node:scope == out_of_scope",
            gates: ("opening", "node:scope == in_scope"));

        var decision = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Equal("consult", Assert.Single(decision.Results).Id);
        Assert.Equal("letter", Assert.Single(decision.Skipped).ResultId);
        Assert.Null(decision.ExcludedMacros);
    }

    [Fact]
    public void NothingGated_RecordsNothing_TheControl()
    {
        var package = MatchCasePackage();
        var decision = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Null(decision.ExcludedMacros);
    }

    [Fact]
    public void ThePrune_KeepsAClassifier_ReferencedOnlyByAMacroWhen()
    {
        // No result when reads node:scope — only a macro gate does. The
        // classifier exemption keeps it through the prune all the same, and
        // the § 14 pin is that this stays true.
        var package = MatchCasePackage(gates: ("arm_in", "node:scope == in_scope"));

        var decision = DecideActivity.Decide(package, Supplied, new Dictionary<string, string> { ["scope"] = "in_scope" });

        Assert.Contains(decision.Nodes, node => node.Id == "scope" && node.OutputContract == OutputContracts.Classification);
    }

    [Fact]
    public async Task TheExclusions_RideTheDecisionSignal_IntoStateAndResponse()
    {
        var stateProperty = typeof(ConsultGenerationJobEntity).GetProperty(
            "State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(),
            Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), "general@v2026.09.1", "hash",
            Deciding: true));

        await entity.Decide(new ConsultGenerationDecision(
            new[] { (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "consult:s0", ["name"] = "Section 0" } },
            null,
            new[] { new ConsultResultDescriptor("consult", "assemble-note", "Consultation note") },
            null,
            null,
            null,
            new Dictionary<string, string> { ["scope"] = "in_scope" },
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
            new[] { new ConsultExcludedMacro("consult", "arm_out", "needs node:scope to be 'out_of_scope'; it is 'in_scope'") }));

        var state = (ConsultGenerationJobState)stateProperty.GetValue(entity)!;
        Assert.Equal("arm_out", Assert.Single(state.ExcludedMacros!).MacroId);
        Assert.Equal("arm_out", Assert.Single(state.ToResponse().ExcludedMacros!).MacroId);
    }
}
