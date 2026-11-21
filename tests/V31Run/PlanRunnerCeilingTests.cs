using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.2, 3.4; part 04 § 8): the per-job ceiling and the job's agent calls. At
/// most <c>MaxAgentCalls</c> are in flight; ready calls go out in <c>graph.order</c>, whatever order their answers come
/// back in; the call that would pass <c>JobAgentCalls</c> is not made, and the job fails with S09-015.
/// </summary>
public class PlanRunnerCeilingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(1503)]
    public async Task NeverMoreThanTheCeiling(int seed)
    {
        var plan = RunFixture.Plan(Independent(10), StubTemplates.Empty());
        var host = new FakeRunHost();
        var runner = new PlanRunner(plan, new RunLimits(MaxAgentCalls: 4), host, new ListRecorder());

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            Assert.Equal(4, host.Dispatched.Count);
            host.ReleaseInOrder(new Random(seed));
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(4, host.InFlightPeak);
        Assert.Equal(4, result.PeakInFlight);
        Assert.Equal(10, result.AgentCalls);
        var order = plan.Entries.Values.Where(e => e.Kind == PlanKind.Agent).OrderBy(e => e.Order).Select(e => e.Key).ToList();
        Assert.Equal(10, order.Count);
        Assert.Equal(order, host.Dispatched);
    }

    [Fact]
    public async Task TheCallLimit_FailsTheJob()
    {
        var plan = RunFixture.Plan(Independent(4), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(JobAgentCalls: 3), host, recorder)
            .RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("The job reached 3 agent calls, the most this engine runs for one job.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S09-015", result.Sentence!, ""));
        Assert.Equal(3, host.Calls.Count);
        Assert.Equal(3, result.AgentCalls);
        Assert.Equal("node.n4", result.FailedKey);
        Assert.Equal(RowState.Failed, recorder.Row("node.n4").State);
        Assert.Empty(host.Renders);
    }

    [Fact]
    public void TheLimits_AreTheHeadersNine()
    {
        Assert.Equal(
            """{"maxAgentCalls":16,"capacityWaitMinutes":10,"serviceAttempts":5,"jobMinutes":30,"deliveryMinutes":15,"deliveryAttempts":3,"loopItems":200,"jobAgentCalls":500,"renderSteps":100000}""",
            new RunLimits().ToJson().ToJsonString());
    }

    [Fact]
    public void RunKeys_WriteItemsAndPasses()
    {
        Assert.Equal("node.plan", RunKeys.Of("node.plan", [], []));
        Assert.Equal("node.refine.judge#2", RunKeys.Of("node.refine.judge", [], [(0, 2)]));
        Assert.Equal("node.sections.paragraphs.draft_paragraph:2/1", RunKeys.Of("node.sections.paragraphs.draft_paragraph", ["2", "1"], []));
        Assert.Equal("node.a.b.c:3#2/1", RunKeys.Of("node.a.b.c", ["3", "1"], [(1, 2)]));
    }

    /// <summary><see cref="SourceFixture.Minimal"/> with <paramref name="count"/> agents <c>n1</c>… that read nothing, and a deliverable that reads none of them.</summary>
    private static Dictionary<string, byte[]> Independent(int count)
    {
        var ids = Enumerable.Range(1, count).Select(i => $"n{i}").ToList();
        var files = Minimal().With("nodes/index.json", Index(ids)).Without("nodes/plan.md")
            .With("deliverables/note.md", Md(Deliverable("Consultation note", PlanRunnerLeavesTests.Plain, "null", "hold"), "Dr A. Sample\n"));
        foreach (var id in ids)
            files.With($"nodes/{id}.md", Agent($"Drafting {id}", PlanRunnerLeavesTests.Plain, $"Draft part {id} of the note for Margaret Okafor.\n"));
        return files;
    }
}
