using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.1, 3.4; D7, D12): one attempt of a 31 agent call: the prompt sent is the
/// entry's composed body (#1504: as <see cref="Consultologist.PackageFormat.V31.Templates.AgentTurn.Prepare"/> prepares it), to the agent the plan's bindings name for its format and producer; a 429 or another service
/// failure is answered with the wait before the next attempt, the service's or the back-off's with its jitter.
/// Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class RunAgentNodeV31ActivityTests
{
    private static readonly JsonObject DraftScope = new()
    {
        ["input"] = new JsonObject { ["consult_draft"] = new JsonObject { ["content"] = PlanRunnerLeavesTests.Draft } },
    };

    [Fact]
    public async Task ThePromptSent_IsTheComposedBody_ToTheBindingsAgent()
    {
        var (activity, agent, job) = Activity("nested-note");

        var outcome = await activity.RunAsync(Input(job, new AgentCall("node.plan", "node.plan", null, 1, false, DraftScope)), CancellationToken.None);

        Assert.Equal((AgentOutcomeKind.Completed, "A reply for Dr A. Sample.", 0, null), Of(outcome));
        var sent = Assert.Single(agent.Sent);
        Assert.Equal("node.plan", sent.Stage);
        // #1504 (D8): a custom binding's prompt ends with the format sent, after the trailer
        Assert.StartsWith($"Plan the sections of a consultation note for the draft below, one section for each heading.\n\n{PlanRunnerLeavesTests.Draft}", sent.Message, StringComparison.Ordinal);
        Assert.Contains(Consultologist.PackageFormat.V31.Templates.AgentTurn.CustomTrailer, sent.Message, StringComparison.Ordinal);
        // bindings.section_plan.generated
        Assert.Equal(("custom-json", "1"), (sent.AgentName, sent.AgentVersion));
    }

    [Fact]
    public async Task AFunctionsNode_IsComposedFromItsFunctionsEntry_ToItsFormatsAgent()
    {
        var (activity, agent, job) = Activity("functions");
        var scope = new JsonObject { ["param"] = new JsonObject { ["draft"] = new JsonObject { ["content"] = "Margaret Okafor reports fatigue (fictional)." } } };

        await activity.RunAsync(Input(job, new AgentCall("node.standards.section.patient_draft:hpi", "node.patient_draft", "draft_section", 1, false, scope)),
            CancellationToken.None);

        var sent = Assert.Single(agent.Sent);
        Assert.Equal("node.standards.section.patient_draft:hpi", sent.Stage);
        Assert.Equal("Using the consult draft below, write what it says about Margaret Okafor for this section.\n\nMargaret Okafor reports fatigue (fictional).\n", sent.Message);
        // bindings.plaintext.generated
        Assert.Equal(("test-json", "48"), (sent.AgentName, sent.AgentVersion));
    }

    [Fact]
    public async Task A429_WithRetryAfterMs_IsThrottled_ForThatWait()
    {
        var (activity, agent, job) = Activity("nested-note");
        agent.Throw = AgentAttemptTests.Failed(429, ("retry-after-ms", "1500"));

        var outcome = await activity.RunAsync(Input(job, Plan(1)), CancellationToken.None);

        Assert.Equal((AgentOutcomeKind.Throttled, (string?)null, 1500, (string?)null), Of(outcome));
    }

    [Fact]
    public async Task A429_WithNoWait_BacksOff_WithJitter()
    {
        foreach (var seed in new[] { 1, 31, 1503 })
        {
            var (activity, agent, job) = Activity("nested-note", new Random(seed));
            agent.Throw = AgentAttemptTests.Failed(429);

            var first = await activity.RunAsync(Input(job, Plan(1)), CancellationToken.None);
            var second = await activity.RunAsync(Input(job, Plan(2)), CancellationToken.None);
            var late = await activity.RunAsync(Input(job, Plan(12)), CancellationToken.None);

            Assert.Equal(AgentOutcomeKind.Throttled, first.Kind);
            Assert.InRange(first.WaitMilliseconds, 5000, 6000);
            Assert.InRange(second.WaitMilliseconds, 10_000, 12_000);
            Assert.InRange(late.WaitMilliseconds, 60_000, 72_000);
        }

        // the jitter comes from the random given, so a test can pin it
        var (pinned, pinnedAgent, pinnedJob) = Activity("nested-note", new Random(7));
        pinnedAgent.Throw = AgentAttemptTests.Failed(429);
        var expected = AgentBackoff.Wait(1, new Random(7).NextDouble());
        Assert.Equal(expected, (await pinned.RunAsync(Input(pinnedJob, Plan(1)), CancellationToken.None)).WaitMilliseconds);
    }

    [Fact]
    public async Task A503_IsAServiceFailure_Error()
    {
        var (activity, agent, job) = Activity("nested-note", new Random(1));
        agent.Throw = AgentAttemptTests.Failed(503);

        var outcome = await activity.RunAsync(Input(job, Plan(1)), CancellationToken.None);

        Assert.Equal(AgentOutcomeKind.ServiceFailure, outcome.Kind);
        Assert.Equal("error", outcome.ErrorKind);
        Assert.Null(outcome.Reply);
        Assert.InRange(outcome.WaitMilliseconds, 5000, 6000);
    }

    [Fact]
    public async Task ATimeOut_IsAServiceFailure_Timeout()
    {
        var (activity, agent, job) = Activity("nested-note");
        agent.Throw = new TaskCanceledException("The operation was cancelled because it exceeded the configured timeout.", new TimeoutException());

        var outcome = await activity.RunAsync(Input(job, Plan(1)), CancellationToken.None);

        Assert.Equal((AgentOutcomeKind.ServiceFailure, "timeout"), (outcome.Kind, outcome.ErrorKind));
    }

    [Fact]
    public async Task NoConnection_IsAServiceFailure_Unreachable()
    {
        var (activity, agent, job) = Activity("nested-note");
        agent.Throw = new HttpRequestException("No such host is known.");

        var outcome = await activity.RunAsync(Input(job, Plan(1)), CancellationToken.None);

        Assert.Equal((AgentOutcomeKind.ServiceFailure, "unreachable"), (outcome.Kind, outcome.ErrorKind));
    }

    [Fact]
    public async Task AnEmptyReply_SettlesAsACompletedOutcome_WithEmptyText()
    {
        var (activity, agent, job) = Activity("nested-note");
        agent.Throw = new InvalidOperationException("No assistant response found");

        var outcome = await activity.RunAsync(Input(job, Plan(1)), CancellationToken.None);

        Assert.Equal((AgentOutcomeKind.Completed, "", 0, (string?)null), Of(outcome));
    }

    [Fact]
    public async Task AnyOtherFailure_Propagates()
    {
        // as today (the brief): a 401 is not a service failure a 31 call retries; the orchestration fails the job with A06-010
        var (activity, agent, job) = Activity("nested-note");
        agent.Throw = AgentAttemptTests.Failed(401);

        await Assert.ThrowsAsync<System.ClientModel.ClientResultException>(() => activity.RunAsync(Input(job, Plan(1)), CancellationToken.None));
    }

    [Fact]
    public async Task TheHostsOwnCancellation_Propagates()
    {
        var (activity, agent, job) = Activity("nested-note");
        agent.Throw = new TaskCanceledException();

        await Assert.ThrowsAsync<TaskCanceledException>(() => activity.RunAsync(Input(job, Plan(1)), new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task APlanThatIsNotTheJobs_IsACallThatCouldNotBeMade_Plan()
    {
        var (activity, agent, job) = Activity("nested-note");
        var input = Input(job, Plan(1)) with { ResolveHash = "sha256:" + new string('1', 64) };

        var outcome = await activity.RunAsync(input, CancellationToken.None);

        Assert.Equal((AgentOutcomeKind.Failed, (string?)null, 0, "plan"), Of(outcome));
        Assert.Empty(agent.Sent);
    }

    [Fact]
    public async Task AnEntryThePlanLacks_IsACallThatCouldNotBeMade_Plan()
    {
        var (activity, agent, job) = Activity("nested-note");

        var outcome = await activity.RunAsync(Input(job, new AgentCall("node.letter", "node.letter", null, 1, false, DraftScope)), CancellationToken.None);

        Assert.Equal((AgentOutcomeKind.Failed, (string?)null, 0, "plan"), Of(outcome));
        Assert.Empty(agent.Sent);
    }

    [Fact]
    public async Task ABodyThatCannotBeComposed_IsACallThatCouldNotBeMade_Compose()
    {
        var (activity, agent, job) = Activity("nested-note");

        var outcome = await activity.RunAsync(Input(job, Plan(1) with { Scope = new JsonObject() }), CancellationToken.None);

        Assert.Equal((AgentOutcomeKind.Failed, (string?)null, 0, "compose"), Of(outcome));
        Assert.Empty(agent.Sent);
    }

    [Fact]
    public async Task ACallThatCouldNotBeComposed_WritesNoText()
    {
        // #1506 (Task 10): nothing was produced (Produced is null), so nothing is written
        var (activity, _, job) = Activity("nested-note");

        var outcome = await activity.RunAsync(Stored(Input(job, Plan(1) with { Scope = new JsonObject() })), CancellationToken.None);

        Assert.Equal("compose", outcome.ErrorKind);
        Assert.Empty(job.Blobs.Blobs.Names());
    }

    [Fact]
    public async Task ACallsText_IsWrittenForItsKey_AndOverwrittenByALaterAttempt()
    {
        var (activity, _, job) = Activity("nested-note");

        var first = await activity.RunAsync(Stored(Input(job, Plan(1))), CancellationToken.None);
        var second = await activity.RunAsync(Stored(Input(job, Plan(2) with { Repair = true, Faults = ["The reply was not valid."] })), CancellationToken.None);

        Assert.Null(first.Produced);
        Assert.Null(second.Produced);
        var name = Assert.Single(job.Blobs.Blobs.Names());
        Assert.EndsWith(".produced.json", name.Name, StringComparison.Ordinal);
        var produced = (await job.Blobs.ReadTextAsync(V31Job.AccountKind, "user-1", V31Job.JobId, "node.plan", CancellationToken.None))!;
        Assert.Equal(Consultologist.PackageFormat.V31.CanonicalJson.HashText((string)produced["sent"]!), (string?)second.Detail!["inputHash"]);
    }

    [Fact]
    public async Task ATextThatCannotBeWritten_FailsTheActivity()
    {
        // a row whose text was never stored would be a silent hole: the activity fails, and with it the job (A06-010)
        var blobs = Substitute.For<IRecordBlobStore>();
        blobs.WriteTextAsync(default, default!, default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("The blob service is down.")));
        var (activity, _, job) = Activity("nested-note", blobs: blobs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => activity.RunAsync(Stored(Input(job, Plan(1))), CancellationToken.None));
    }

    private static RunAgentNodeV31Input Stored(RunAgentNodeV31Input input) =>
        input with { JobId = V31Job.JobId, AppUserId = "user-1", AccountKind = V31Job.AccountKind };

    private static AgentCall Plan(int attempt) => new("node.plan", "node.plan", null, attempt, false, DraftScope);

    private static RunAgentNodeV31Input Input(V31Job job, AgentCall call) =>
        new(job.Ref.Name, job.Ref.Version, job.Pair.ResolveHash, new RunLimits().RenderSteps, call);

    /// <summary>What an outcome says, its detail and faults aside.</summary>
    private static (AgentOutcomeKind Kind, string? Reply, int WaitMilliseconds, string? ErrorKind) Of(AgentOutcome outcome) =>
        (outcome.Kind, outcome.Reply, outcome.WaitMilliseconds, outcome.ErrorKind);

    private static (RunAgentNodeV31Activity Activity, FakeAttempts Agent, V31Job Job) Activity(string golden, Random? random = null, IRecordBlobStore? blobs = null)
    {
        var job = V31Job.Of(golden);
        var agent = new FakeAttempts();
        var activity = new RunAgentNodeV31Activity(job.Store, agent, new Lazy<V31AgentsCatalogue>(() => V31AgentsCatalogue.Load()),
            blobs ?? job.Blobs, NullLogger<RunAgentNodeV31Activity>.Instance, random);
        return (activity, agent, job);
    }

    /// <summary>
    /// An agent that records what it was sent and replies, or throws what the SDK would, classified as
    /// <see cref="AgentSectionGenerator.TryPromptOnceAsync"/> classifies it.
    /// </summary>
    private sealed class FakeAttempts : IAgentAttempts
    {
        public List<(string Stage, string Message, string AgentName, string AgentVersion)> Sent { get; } = [];

        public Exception? Throw { get; set; }

        public Task<AgentAttempt> TryPromptOnceAsync(string stage, string userMessage, string agentName, string agentVersion, CancellationToken cancellationToken)
        {
            Sent.Add((stage, userMessage, agentName, agentVersion));
            if (Throw is not { } failure)
                return Task.FromResult(new AgentAttempt(AgentOutcomeKind.Completed, "A reply for Dr A. Sample.", null, null, null));
            return AgentAttempt.Of(failure, cancellationToken, DateTimeOffset.UtcNow) is { } failed
                ? Task.FromResult(failed)
                : Task.FromException<AgentAttempt>(failure);
        }
    }
}
