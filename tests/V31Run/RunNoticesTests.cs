using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): the runner's two notices for the operator. <c>capacity-wait</c> is
/// raised once, when the job's 429 waits pass <see cref="PlanRunner.CapacityAlertMinutes"/> in all, across its nodes;
/// <c>job-slow</c> once, at <see cref="PlanRunner.SlowJobPercent"/> of <c>jobMinutes</c>, on a host timer cancelled when the
/// run ends. A notice carries a kind, a key and a count of minutes, never a value. Content is fictional (Margaret Okafor).
/// </summary>
public class RunNoticesTests
{
    [Fact]
    public async Task CapacityWaitsPastFiveMinutes_RaiseOneNotice()
    {
        // two nodes each wait 3 minutes on a 429: 3 minutes in all after the first, 6 after the second
        var plan = RunFixture.Plan(PlanRunnerCeilingTests.Independent(2), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true)
            .Agent("node.n1", Throttled(180_000), Reply("A first part for Margaret Okafor."))
            .Agent("node.n2", Throttled(180_000), Reply("A second part for Margaret Okafor."));
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            Assert.Equal([new RunNotice("capacity-wait", "node.n2", 6)], recorder.Notices);
            host.Advance(TimeSpan.FromMinutes(3));
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal([new RunNotice("capacity-wait", "node.n2", 6)], recorder.Notices);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task AJobAtEightyPercent_RaisesOneNotice()
    {
        var plan = RunFixture.Plan(PlanRunnerCeilingTests.Independent(2), StubTemplates.Empty());
        var host = new FakeRunHost();
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            host.Advance(TimeSpan.FromMinutes(23));
            Assert.Empty(recorder.Notices);
            host.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal([new RunNotice("job-slow", null, 24)], recorder.Notices);
            host.Advance(TimeSpan.FromMinutes(5));
            host.ReleaseAll();
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal([new RunNotice("job-slow", null, 24)], recorder.Notices);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task AQuickJob_RaisesNone_AndLeavesNoTimer()
    {
        var plan = RunFixture.Plan(PlanRunnerCeilingTests.Independent(2), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Empty(recorder.Notices);
        Assert.Equal(2, host.DelaysMade); // the job's timer and the slow-job timer, both cancelled
        Assert.Equal(0, host.OutstandingDelays);
    }

    private static AgentOutcome Reply(string reply) => new(AgentOutcomeKind.Completed, reply, 0, null);

    private static AgentOutcome Throttled(int waitMilliseconds) => new(AgentOutcomeKind.Throttled, null, waitMilliseconds, null);
}
