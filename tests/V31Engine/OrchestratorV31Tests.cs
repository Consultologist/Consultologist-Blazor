using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.2 step 6, 3.1, 3.2, 3.4; D7, D12): a 31 job's orchestration runs the plan
/// inline, its agent calls and renders as activities, its waits as durable timers: the header is recorded before any node
/// runs, the calls go out in the runner's fixed order and never more than the ceiling at once, a throttled call waits on a
/// timer of the wait its activity gave, and the job ends completed, or failed with the run's sentence.
/// </summary>
public class OrchestratorV31Tests
{
    [Fact]
    public async Task AJob_RunsToCompleted()
    {
        var job = V31Job.Of("nested-note");
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Null(job.State.FailureError);

        // the header: the first entity call (no schedule to wait for here), before every activity call, in one call
        var header = context.Calls.FindIndex(c => c.Kind == "entity");
        Assert.Equal(nameof(ConsultGenerationJobEntity.RecordV31Header), context.Calls[header].Name);
        Assert.True(context.Calls.FindIndex(c => c.Kind == "activity") > header);
        var recorded = JsonNode.Parse(context.Calls[header].InputJson)!;
        Assert.Equal(job.Pair.ResolveHash, (string?)recorded["ResolveHash"]);
        Assert.Equal(job.Pair.LockHash, (string?)recorded["LockHash"]);
        Assert.Equal(new RunLimits().ToJson().ToJsonString(), recorded["Limits"]!.ToJsonString());
        Assert.Equal(new DateTimeOffset(V31Job.Now, TimeSpan.Zero), recorded["StartedAtUtc"]!.GetValue<DateTimeOffset>());
        Assert.Equal(nameof(ConsultGenerationJobEntity.MarkRunning), context.Calls[header + 1].Name);

        // on the entity, write-once
        Assert.Equal(job.Pair.ResolveHash, job.State.ResolveHash);
        Assert.Equal(job.Pair.LockHash, job.State.LockHash);
        Assert.Equal(9, job.State.Limits!.Count);
        Assert.Equal(new DateTimeOffset(V31Job.Now, TimeSpan.Zero), job.State.StartedAtUtc);

        // the agent calls in RunGoldenTests' dispatch order: the runner's, unchanged by Durable
        var golden = await RunGoldenTests.Run(job.Plan, new RunLimits(), seed: null);
        Assert.Equal(golden.Host.Dispatched, V31Job.AgentCalls(context).Select(c => c.Key));

        // every row counted on the entity; the run's timers cancelled when it ended, so the orchestration can complete
        Assert.Equal(golden.Recorder.Rows.Count, job.State.V31Progress!.Rows);
        Assert.Equal(golden.Recorder.Decisions.Count, job.State.V31Progress.Decisions);
        Assert.Contains(context.Calls, c => c.Kind == "timer" && c.InputJson == JsonSerializer.Serialize(V31Job.Now.AddMinutes(30)));
        Assert.Equal(0, context.PendingTimers);
        Assert.Equal(nameof(ConsultGenerationJobEntity.FinalizeJob), context.Calls.Last(c => c.Kind == "entity").Name);
    }

    [Fact]
    public async Task AFailedNode_FailsTheJob_WithItsSentence()
    {
        var job = V31Job.Of("nested-note");
        var stub = job.Agent;
        job.Agent = call => call.Key == "node.plan"
            ? new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 5000, "error")
            : stub(call);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        var sentence = job.State.FailureError!;
        V31Contract.AssertSentence(new SourceRefusal("S09-011", sentence, ""));
        Assert.Equal("The model service returned an error 5 times in a row. Run the workflow again later.", sentence);
        Assert.Equal(5, V31Job.AgentCalls(context).Count(c => c.Key == "node.plan"));
        Assert.Equal(0, context.PendingTimers);
    }

    [Fact]
    public async Task AnEmptyReply_IsModelOutput_RepairedByTheNodesOnInvalid_NeverAnEngineFault()
    {
        // node.plan is onInvalid: retry; an empty reply is invalid, so its repair is asked for, and the repaired reply settles it
        var job = V31Job.Of("nested-note");
        var stub = job.Agent;
        job.Agent = call => call.Key == "node.plan" && !call.Repair
            ? new AgentOutcome(AgentOutcomeKind.Completed, AgentAttempt.EmptyReply.Text, 0, null)
            : stub(call);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Equal([false, true], V31Job.AgentCalls(context).Where(c => c.Key == "node.plan").Select(c => c.Repair));
    }

    [Fact]
    public async Task EmptyReplies_FailTheNodeWithItsSentence_NeverA06010()
    {
        var job = V31Job.Of("nested-note");
        var stub = job.Agent;
        job.Agent = call => call.Key == "node.plan" ? new AgentOutcome(AgentOutcomeKind.Completed, "", 0, null) : stub(call);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        // invalid twice (the reply, then its repair): the node fails with A06-005, the job with it
        V31Contract.AssertSentence(new SourceRefusal("A06-005", job.State.FailureError!, ""));
        Assert.StartsWith("Node 'plan' gave a reply that isn't ", job.State.FailureError, StringComparison.Ordinal);
        Assert.Equal(2, V31Job.AgentCalls(context).Count(c => c.Key == "node.plan"));
    }

    [Fact]
    public async Task AThrottledCall_WaitsOnADurableTimer()
    {
        var job = V31Job.Of("nested-note");
        var stub = job.Agent;
        job.Agent = call => call.Key == "node.plan" && call.Attempt == 1
            ? new AgentOutcome(AgentOutcomeKind.Throttled, null, 1500, null)
            : stub(call);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        var plans = context.Calls
            .Select((call, at) => (call, at))
            .Where(c => c.call.Name == ConsultGenerationActivityNames.RunAgentNodeV31
                && JsonSerializer.Deserialize<RunAgentNodeV31Input>(c.call.InputJson)!.Call.Key == "node.plan")
            .Select(c => c.at)
            .ToList();
        Assert.Equal(2, plans.Count);
        var fireAt = JsonSerializer.Serialize(V31Job.Now.AddMilliseconds(1500));
        Assert.Contains(context.Calls.Skip(plans[0] + 1).Take(plans[1] - plans[0] - 1), c => c.Kind == "timer" && c.InputJson == fireAt);
        Assert.Equal(2, V31Job.AgentCalls(context).Single(c => c.Key == "node.plan" && c.Attempt == 2).Attempt);
    }

    [Fact]
    public async Task NeverMoreThanTheCeiling_InFlight()
    {
        var job = V31Job.Of("nested-note");
        job.Limits = new RunLimits(MaxAgentCalls: 2);
        var context = job.Context();

        await V31Job.DriveAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Equal(2, context.PeakHeld(ConsultGenerationActivityNames.RunAgentNodeV31));
        Assert.Equal(0, context.PendingTimers);
    }

    [Fact]
    public async Task EachAgentCall_IsGivenTheJobsRecordedStepBudget()
    {
        // the activity composes with the renderSteps the job's start recorded, never the live setting
        var job = V31Job.Of("nested-note");
        job.Limits = new RunLimits(RenderSteps: 777);
        var context = job.Context();

        await V31Job.RunAsync(context);

        var budgets = context.Calls.Where(c => c.Kind == "activity" && c.Name == ConsultGenerationActivityNames.RunAgentNodeV31)
            .Select(c => JsonSerializer.Deserialize<RunAgentNodeV31Input>(c.InputJson)!.RenderSteps)
            .ToList();
        Assert.NotEmpty(budgets);
        Assert.All(budgets, budget => Assert.Equal(777, budget));
    }

    [Fact]
    public async Task APlanWhoseLockIsNotTheJobs_FailsTheJob_WithS09014()
    {
        // D5: a lock that is not the one the job's start checked is S09-014's, not the hash's S09-009
        var job = V31Job.Of("nested-note");
        job.LockHash = "sha256:" + new string('0', 64);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        var sentence = job.State.FailureError!;
        V31Contract.AssertSentence(new SourceRefusal("S09-014", sentence, ""));
        Assert.Equal($"The compiled workflow {job.Ref.Name}@{job.Ref.Version} doesn't match its lock, so it can't be run. Tell the operator.", sentence);
        Assert.Empty(V31Job.AgentCalls(context));
    }

    [Fact]
    public async Task APlanWhoseHashIsNotTheJobs_FailsTheJob_WithS09009()
    {
        var job = V31Job.Of("nested-note");
        job.ResolveHash = "sha256:" + new string('0', 64);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        var sentence = job.State.FailureError!;
        V31Contract.AssertSentence(new SourceRefusal("S09-009", sentence, ""));
        Assert.Equal($"The compiled workflow {job.Ref.Name}@{job.Ref.Version} doesn't match its published hash, so it can't be run. Tell the operator.", sentence);
        Assert.Empty(V31Job.AgentCalls(context));
    }

    [Fact]
    public async Task AnActivityFault_FailsTheJob_WithA06010_NeverItsMessage()
    {
        var job = V31Job.Of("nested-note");
        const string response = "Unauthorized: the deployment answered with Margaret Okafor's prompt (fictional)";
        job.Agent = _ => throw new InvalidOperationException(response);
        var context = job.Context().WrapActivityFailures();

        await Assert.ThrowsAsync<Microsoft.DurableTask.TaskFailedException>(() => V31Job.RunAsync(context));

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        Assert.Equal(ConsultGenerationOrchestratorV31.EngineFault, job.State.FailureError);
        V31Contract.AssertSentence(new SourceRefusal("A06-010", job.State.FailureError!, ""));
        Assert.DoesNotContain(context.Calls, c => c.Kind == "entity" && c.InputJson.Contains("Margaret", StringComparison.Ordinal));
        Assert.DoesNotContain("Margaret", JsonSerializer.Serialize(job.State), StringComparison.Ordinal);
        Assert.Equal(typeof(InvalidOperationException).FullName,
            ConsultGenerationOrchestratorV31.FaultType(new Microsoft.DurableTask.TaskFailedException("run-agent-node-v31", 1, new InvalidOperationException(response))));
    }

    [Fact]
    public async Task InputsTheRetentionDropDeleted_FailTheJob_WithA06009()
    {
        var job = V31Job.Of("nested-note");
        job.Inputs.Documents.Clear();
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        Assert.Equal("This job's inputs are no longer held, so it can't run. Run the workflow again.", job.State.FailureError);
        V31Contract.AssertSentence(new SourceRefusal("A06-009", job.State.FailureError!, ""));
        Assert.Empty(V31Job.AgentCalls(context));
    }

    [Fact]
    public async Task TheInputs_AreTheJobsHeldInputs_SweptAsA530JobsAre()
    {
        // where they live: the kind's container, under the account's prefix, as a 5–30 job's held inputs
        Assert.Equal(new Consultologist.Api.Models.ConsultInputsBlobPointer("org-job-inputs", "user-1/job-31-0001.json"),
            RunInputsBlobStore.PointerFor(V31Job.AccountKind, "user-1", V31Job.JobId));
        Assert.Equal(new Consultologist.Api.Models.ConsultInputsBlobPointer("personal-job-inputs", "user-1/job-31-0001.json"),
            RunInputsBlobStore.PointerFor(Consultologist.Api.Auth.SignInKinds.Personal, "user-1", V31Job.JobId));

        // the job's entity holds that pointer, and the retention drop deletes it once the job is over
        var job = V31Job.Of("nested-note");
        var pointer = job.State.InputsBlob!;
        Assert.Equal(RunInputsBlobStore.PointerFor(V31Job.AccountKind, "user-1", V31Job.JobId), pointer);
        Assert.Equal(pointer, RunInputsBlobStore.PointerOf(V31Job.AccountKind, job.Input().InputsBlob));
        await V31Job.RunAsync(job.Context());

        await job.Entity.DropInputs(new ConsultGenerationInputsDrop(new DateTimeOffset(V31Job.Now.AddDays(30), TimeSpan.Zero)));

        await job.HeldInputs.Received(1).DeleteAsync(pointer, Arg.Any<CancellationToken>());
        Assert.NotNull(job.State.InputsDroppedAtUtc);
    }

    [Fact]
    public async Task TheRunFacts_AreWorkedOutInTheInputsActivity_IntoTheHistory()
    {
        var registry = new FakeRegistry();
        var golden = CompiledGoldenTests.Cases().Single(c => c.Name == "nested-note");
        var packageRef = registry.PutChanged(golden, resolve => resolve["run"] = new JsonArray("run.date", "run.id"));
        var store = V31Job.StoreOver(registry);
        var resolveHash = (await store.TryLoadAsync(packageRef, CancellationToken.None))!.Plan!.ResolveHash;
        var inputs = new V31Job.FakeInputs();
        var pointer = await inputs.WriteAsync(null, "user-1", "job-1", new JsonObject { ["inputs"] = new JsonObject() }, CancellationToken.None);

        var held = await new LoadRunInputsV31Activity(inputs, store).RunAsync(
            new LoadRunInputsV31Input(pointer, packageRef.Name, packageRef.Version, resolveHash,
                new DateTimeOffset(2026, 11, 17, 3, 30, 0, TimeSpan.Zero), "America/Toronto", new JsonObject { ["id"] = "job-1" }),
            CancellationToken.None);

        Assert.Null(held.Refusal);
        Assert.Equal("""{"id":"job-1","date":"2026-11-16"}""", held.RunFacts!.ToJsonString());
    }

    [Fact]
    public async Task AReplay_RecordsTheSameCalls_WithAColdPlanCacheOrAWarmOne()
    {
        ConsultGenerationOrchestratorV31.Plans.Clear();
        var cold = V31Job.Of("functions");
        var coldContext = cold.Context();
        await V31Job.RunAsync(coldContext);
        Assert.NotNull(ConsultGenerationOrchestratorV31.Plans.TryGet(cold.Pair.ResolveHash, 2000));

        var warm = V31Job.Of("functions");
        var warmContext = warm.Context();
        await V31Job.RunAsync(warmContext);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, warm.State.Status);
        Assert.Equal(coldContext.Calls, warmContext.Calls);
    }

    [Fact]
    public void ThePlanCache_KeepsTheMostRecent_ByHashAndRegexLimit()
    {
        var job = V31Job.Of("nested-note");
        var cache = new V31PlanCache(2);

        cache.Add("sha256:a", 2000, job.Plan);
        cache.Add("sha256:b", 2000, job.Plan);
        Assert.Null(cache.TryGet("sha256:a", 1000));
        Assert.Same(job.Plan, cache.TryGet("sha256:a", 2000));
        cache.Add("sha256:c", 2000, job.Plan);

        Assert.Equal(2, cache.Count);
        Assert.Null(cache.TryGet("sha256:b", 2000));
        Assert.NotNull(cache.TryGet("sha256:a", 2000));
        Assert.NotNull(cache.TryGet("sha256:c", 2000));
    }

    [Fact]
    public void TheHeader_IsWriteOnce_AndAbsentBelow31()
    {
        var job = V31Job.Of("nested-note");
        var limits = new RunLimits().ToJson().ToDictionary(m => m.Key, m => (int)m.Value!);
        job.Entity.RecordV31Header(new ConsultGenerationV31Header("sha256:first", "sha256:lock", limits, new DateTimeOffset(V31Job.Now, TimeSpan.Zero)));
        job.Entity.RecordV31Header(new ConsultGenerationV31Header("sha256:second", "sha256:other", limits, new DateTimeOffset(V31Job.Now.AddHours(1), TimeSpan.Zero)));

        Assert.Equal(("sha256:first", "sha256:lock"), (job.State.ResolveHash, job.State.LockHash));
        Assert.Equal(new DateTimeOffset(V31Job.Now, TimeSpan.Zero), job.State.StartedAtUtc);

        // a 5–30 job's state is written as it was: none of the 31 members
        var below = JsonSerializer.Serialize(ConsultGenerationJobState.Create("job-30", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>()));
        foreach (var member in new[] { "ResolveHash", "LockHash", "Limits", "V31Progress" })
            Assert.DoesNotContain($"\"{member}\"", below, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFunctionsJob_RunsItsCallsInline()
    {
        var job = V31Job.Of("functions");
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        var golden = await RunGoldenTests.Run(job.Plan, new RunLimits(), seed: null);
        Assert.Equal(golden.Host.Dispatched, V31Job.AgentCalls(context).Select(c => c.Key));
        Assert.Contains(V31Job.AgentCalls(context), c => c.Function == "draft_section");
    }
}
