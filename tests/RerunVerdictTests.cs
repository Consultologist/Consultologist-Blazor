using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #582: the rerun verdict. The baseline is seeded once at Initialize; the
/// verdict is computed at completion over the package's own reproducible
/// claims and stamped on the record.
/// </summary>
public class RerunVerdictTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) Entity()
    {
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(),
            Substitute.For<IJobOutputsBlobStore>(),
            Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    private static ConsultGenerationJobInitialize Init(ConsultRerunBaseline? baseline = null) =>
        new("job-1", "user-1",
            new[] { new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" } },
            RerunBaseline: baseline);

    private static ConsultRerunBaseline Baseline(params (string Key, string In, string Out)[] nodes) =>
        new("source-job-1", "aaaa", 5,
            nodes.ToDictionary(n => n.Key, n => new ConsultRerunBaselineNode(n.In, n.Out, 5), StringComparer.Ordinal));

    [Fact]
    public async Task TheBaseline_SeedsOnce_AndALaterInitializeCannotReplaceIt()
    {
        var (entity, state) = Entity();
        var baseline = Baseline(("extract", "in1", "out1"));

        await entity.Initialize(Init(baseline));
        Assert.Same(baseline, state().RerunBaseline);

        // The engine's replay-safe second Initialize carries no baseline —
        // the ??= keeps the seeded one, exactly as InputsBlob works.
        await entity.Initialize(Init());
        Assert.Same(baseline, state().RerunBaseline);
    }

    [Fact]
    public async Task AnOrdinaryInitialize_SeedsNoBaseline()
    {
        var (entity, state) = Entity();

        await entity.Initialize(Init());

        Assert.Null(state().RerunBaseline);
        Assert.Null(state().RerunVerdict);
    }

    // ----- the rule, directly -----

    private static ConsultNodeDescriptor Node(string id, bool? reproducible = null) =>
        new(id, id, Reproducible: reproducible);

    private static ConsultNodeOutputState Output(string key, string? inputHash, string? outputHash, int? hashVersion = 5)
    {
        var colon = key.IndexOf(':');
        return new ConsultNodeOutputState
        {
            NodeId = colon < 0 ? key : key[..colon],
            ItemId = colon < 0 ? null : key[(colon + 1)..],
            Label = key,
            Status = ConsultGenerationNodeStatuses.Completed,
            InputHash = inputHash,
            OutputHash = outputHash,
            HashVersion = hashVersion
        };
    }

    private static Dictionary<string, ConsultNodeOutputState> Outputs(params (string Key, string? In, string? Out)[] entries) =>
        entries.ToDictionary(e => e.Key, e => Output(e.Key, e.In, e.Out), StringComparer.Ordinal);

    [Fact]
    public void EveryReproducibleStageMatching_IsAPass_CountingWhatItHeld()
    {
        var baseline = Baseline(("extract", "in1", "out1"), ("draft", "in2", "out2"));
        var nodes = new[] { Node("extract", true), Node("draft", true), Node("polish") };
        var outputs = Outputs(("extract", "in1", "out1"), ("draft", "in2", "out2"), ("polish", "in3", "DIFFERENT"));

        var (verdict, divergence, counted) = ConsultGenerationJobEntity.RerunVerdictOf(baseline, nodes, outputs, "aaaa", 5);

        // The non-reproducible node's divergence is shown, not counted.
        Assert.Equal((RerunVerdicts.Pass, null, 2), (verdict, divergence, counted));
    }

    [Fact]
    public void TheFirstDivergentStage_InManifestOrder_IsNamed()
    {
        // Manifest order is the nodes list, not alphabetical: "zeta" comes
        // first in the package, so its divergence is the one named.
        var baseline = Baseline(("zeta", "in1", "out1"), ("alpha", "in2", "out2"));
        var nodes = new[] { Node("zeta", true), Node("alpha", true) };
        var outputs = Outputs(("zeta", "in1", "CHANGED"), ("alpha", "in2", "ALSO_CHANGED"));

        var (verdict, divergence, _) = ConsultGenerationJobEntity.RerunVerdictOf(baseline, nodes, outputs, "aaaa", 5);

        Assert.Equal(RerunVerdicts.Fail, verdict);
        Assert.Equal("zeta", divergence);
    }

    [Fact]
    public void AFannedItemsDivergence_NamesTheCompositeKey()
    {
        var baseline = Baseline(("section:hpi", "in1", "out1"), ("section:plan", "in2", "out2"));
        var nodes = new[] { Node("section", true) };
        var outputs = Outputs(("section:hpi", "in1", "out1"), ("section:plan", "in2", "CHANGED"));

        var (verdict, divergence, _) = ConsultGenerationJobEntity.RerunVerdictOf(baseline, nodes, outputs, "aaaa", 5);

        Assert.Equal(RerunVerdicts.Fail, verdict);
        Assert.Equal("section:plan", divergence);
    }

    [Fact]
    public void AnEffectiveInputBreach_FailsBeforeAnyStageIsLookedAt()
    {
        var baseline = Baseline(("extract", "in1", "out1"));
        var nodes = new[] { Node("extract", true) };
        var outputs = Outputs(("extract", "in1", "out1"));

        var byHash = ConsultGenerationJobEntity.RerunVerdictOf(baseline, nodes, outputs, "zzzz", 5);
        var byVersion = ConsultGenerationJobEntity.RerunVerdictOf(baseline, nodes, outputs, "aaaa", 4);

        Assert.Equal((RerunVerdicts.Fail, RerunVerdicts.EffectiveInputsDivergence, 0), byHash);
        Assert.Equal((RerunVerdicts.Fail, RerunVerdicts.EffectiveInputsDivergence, 0), byVersion);
    }

    [Fact]
    public void AStageFailingItsPreconditions_IsNotCounted_AndCannotFail()
    {
        // inputHash drift, hashVersion drift, missing on the source, no hash
        // at all: each divergent outputHash below would "fail" if compared —
        // none may be, so with nothing else comparable the verdict is the
        // named third state, never pass.
        var baseline = new ConsultRerunBaseline("source-job-1", "aaaa", 5,
            new Dictionary<string, ConsultRerunBaselineNode>
            {
                ["drifted"] = new("inX", "out1", 5),
                ["laddered"] = new("in2", "out2", 4),
                ["hashless"] = new("in4", null, 5)
            });
        var nodes = new[] { Node("drifted", true), Node("laddered", true), Node("fresh", true), Node("hashless", true) };
        var outputs = Outputs(("drifted", "in1", "CHANGED"), ("laddered", "in2", "CHANGED"), ("fresh", "in3", "CHANGED"), ("hashless", "in4", "CHANGED"));

        var result = ConsultGenerationJobEntity.RerunVerdictOf(baseline, nodes, outputs, "aaaa", 5);

        Assert.Equal((RerunVerdicts.NoReproducibleStages, null, 0), result);
    }

    [Fact]
    public void APackageClaimingNothingReproducible_SaysSo()
    {
        var baseline = Baseline(("extract", "in1", "out1"));
        var nodes = new[] { Node("extract") };
        var outputs = Outputs(("extract", "in1", "out1"));

        var (verdict, _, _) = ConsultGenerationJobEntity.RerunVerdictOf(baseline, nodes, outputs, "aaaa", 5);

        Assert.Equal(RerunVerdicts.NoReproducibleStages, verdict);
    }

    // ----- the stamp at completion -----

    private static ConsultGenerationJobInitialize InitWithNodes(ConsultRerunBaseline? baseline, params ConsultNodeDescriptor[] nodes) =>
        new("job-1", "user-1",
            new[] { new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" } },
            Nodes: nodes,
            EffectiveInputHash: "aaaa",
            EffectiveInputHashVersion: 5,
            RerunBaseline: baseline);

    private static async Task RunAsync(ConsultGenerationJobEntity entity, string inputHash, string outputHash)
    {
        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate("extract", "Extract", null, inputHash, outputHash, 1, 1, 5));
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0));
    }

    [Fact]
    public async Task ACompletedRerun_StampsTheVerdict_AndHistorySaysSo()
    {
        var (entity, state) = Entity();
        await entity.Initialize(InitWithNodes(Baseline(("extract", "in1", "out1")), Node("extract", true)));
        await RunAsync(entity, "in1", "out1");

        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        Assert.Equal(RerunVerdicts.Pass, state().RerunVerdict);
        Assert.Null(state().RerunDivergence);
        var line = Assert.Single(state().History, h => h.Kind == "rerun");
        Assert.Contains("pass — 1 reproducible stage(s) matched", line.Label);

        var response = state().ToResponse();
        Assert.Equal("source-job-1", response.RerunOf);
        Assert.Equal(RerunVerdicts.Pass, response.RerunVerdict);
        Assert.Null(response.RerunDivergence);
    }

    [Fact]
    public async Task ADivergentRerun_FailsNamingTheStage()
    {
        var (entity, state) = Entity();
        await entity.Initialize(InitWithNodes(Baseline(("extract", "in1", "out1")), Node("extract", true)));
        await RunAsync(entity, "in1", "CHANGED");

        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        Assert.Equal(RerunVerdicts.Fail, state().RerunVerdict);
        Assert.Equal("extract", state().RerunDivergence);
        var line = Assert.Single(state().History, h => h.Kind == "rerun");
        Assert.Equal("Rerun verdict: fail", line.Label);
        Assert.Equal("extract", line.Detail);
    }

    [Fact]
    public async Task AFailedRerun_StampsNoVerdict()
    {
        var (entity, state) = Entity();
        await entity.Initialize(InitWithNodes(Baseline(("extract", "in1", "out1")), Node("extract", true)));

        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Failed, "boom"));

        Assert.Null(state().RerunVerdict);
        Assert.DoesNotContain(state().History, h => h.Kind == "rerun");
    }

    [Fact]
    public async Task ACompletedNonRerun_StampsNothing()
    {
        var (entity, state) = Entity();
        await entity.Initialize(InitWithNodes(baseline: null, Node("extract", true)));
        await RunAsync(entity, "in1", "out1");

        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        Assert.Null(state().RerunVerdict);
        Assert.Null(state().ToResponse().RerunOf);
        Assert.DoesNotContain(state().History, h => h.Kind == "rerun");
    }
}
