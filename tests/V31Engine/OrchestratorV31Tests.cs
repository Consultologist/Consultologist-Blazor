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
    [Theory]
    [InlineData(RowState.Released, "released")]
    [InlineData(RowState.Held, "held")]
    [InlineData(RowState.Blocked, "blocked")]
    public void ADeliverablesRowState_IsNamedForTheEntity(RowState state, string name) =>
        // #1506 (final review M6): the recorder counts by the runner's own word (PlanRunner.StateWord), not a copy of it
        Assert.Equal(name, PlanRunner.StateWord(state));

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
        Assert.Equal(10, job.State.Limits!.Count);
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

        var held = await new LoadRunInputsV31Activity(inputs, store, V31Job.Toolchain.Value).RunAsync(
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
    public async Task AReplay_ReadsTheStoredDecision()
    {
        // #1505 (R6): the note's if holds a pattern, so it is decided in evaluate-rule-v31. The first run's outcome is in the
        // history; the replay reads it, though the activity would now answer differently (the pattern timed out, say), and
        // dispatches what the first run did.
        var golden = new CompiledGoldenTests.GoldenCase("pattern-note",
            () => CompiledGoldenTests.Cases().Single(c => c.Name == "nested-note").Source()
                .WithJson("deliverables/note.md", h => h["if"] = JsonNode.Parse("{ \"matches\": [{ \"var\": \"input.consult_draft.content\" }, \"Okafor\"] }")),
            "nested-note, its note's if a pattern on the draft");
        var history = new Queue<RuleOutcome>();
        var evaluations = 0;

        var first = V31Job.Of(golden);
        var firstContext = first.Context();
        var activity = new EvaluateRuleV31Activity(first.Store, Microsoft.Extensions.Logging.Abstractions.NullLogger<EvaluateRuleV31Activity>.Instance);
        firstContext.On(ConsultGenerationActivityNames.EvaluateRuleV31, payload =>
        {
            evaluations++;
            var outcome = activity.RunAsync((EvaluateRuleV31Input)payload!, CancellationToken.None).GetAwaiter().GetResult();
            history.Enqueue(outcome);
            return outcome;
        });
        await V31Job.RunAsync(firstContext);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, first.State.Status);
        Assert.Equal(1, evaluations);
        Assert.Equal(true, (bool?)history.Single().Value);
        Assert.Contains(firstContext.Calls, c => c.Name == ConsultGenerationActivityNames.RenderNodeV31 && c.InputJson.Contains("\"deliverable.note\"", StringComparison.Ordinal));

        var replay = V31Job.Of(golden);
        var replayContext = replay.Context().On(ConsultGenerationActivityNames.EvaluateRuleV31, _ =>
        {
            if (history.TryDequeue(out var stored))
                return stored;
            evaluations++;
            return new RuleOutcome(null, "MatchTooSlow", 2, "matches");
        });
        await V31Job.RunAsync(replayContext);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, replay.State.Status);
        Assert.Equal(1, evaluations);
        Assert.Equal(firstContext.Calls, replayContext.Calls);
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
    public async Task TheHeader_RecordsWhatRan()
    {
        // #1506 (area 09; part 08 §§ 3, 8.1; R4): the versions that ran and the run facts, recorded once, before any node runs
        var job = V31Job.Of("nested-note", resolve => resolve["run"] = new JsonArray("run.date", "run.id"));
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        var headers = context.Calls.Select((call, at) => (call, at))
            .Where(c => c.call.Kind == "entity" && c.call.Name == nameof(ConsultGenerationJobEntity.RecordV31Header))
            .Select(c => c.at).ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal(nameof(ConsultGenerationJobEntity.MarkRunning), context.Calls[headers[0] + 1].Name);
        Assert.True(headers[1] > context.Calls.FindIndex(c => c.Name == ConsultGenerationActivityNames.LoadRunInputsV31));
        var firstNode = context.Calls.FindIndex(c => c.Name is ConsultGenerationActivityNames.RunAgentNodeV31 or ConsultGenerationActivityNames.RenderNodeV31);
        Assert.True(firstNode > headers[1]);
        var second = JsonNode.Parse(context.Calls[headers[1]].InputJson)!;
        Assert.Equal(job.Pair.ResolveHash, (string?)second["ResolveHash"]);
        Assert.Equal(new DateTimeOffset(V31Job.Now, TimeSpan.Zero), second["StartedAtUtc"]!.GetValue<DateTimeOffset>());
        // the first call is written as it was: no versionsRan or run facts in it
        var first = JsonNode.Parse(context.Calls[headers[0]].InputJson)!.AsObject();
        Assert.False(first.ContainsKey("VersionsRan") || first.ContainsKey("RunFacts"));

        // versionsRan: lock.json's nested shape without documentVersion, package and resolve (R4)
        var ran = job.State.VersionsRan!;
        Assert.Equal(["resolver", "libraries", "evaluator", "operations", "builtins", "culture", "runtime", "catalogues"], ran.Select(m => m.Key));
        var lockDocument = JsonNode.Parse(job.Pair.Lock)!.AsObject();
        Assert.Equal(lockDocument["operations"]!.AsObject().Select(m => m.Key), ran["operations"]!.AsObject().Select(m => m.Key));
        Assert.Equal(lockDocument["builtins"]!.AsObject().Select(m => m.Key), ran["builtins"]!.AsObject().Select(m => m.Key));
        Assert.Equal(["Corvus.JsonSchema", "Corvus.Text.Json.JsonLogic", "Markdig", "Scriban"], ran["libraries"]!.AsObject().Select(m => m.Key));
        Assert.Equal("invariant", (string?)ran["culture"]);
        Assert.Equal(["dotnet", "unicode"], ran["runtime"]!.AsObject().Select(m => m.Key));
        Assert.Equal(["packageFormat", "dialect"], ran["resolver"]!.AsObject().Select(m => m.Key));
        Assert.StartsWith("agents@", (string?)ran["catalogues"]!["agents"], StringComparison.Ordinal);
        Assert.StartsWith("formats@", (string?)ran["catalogues"]!["formats"], StringComparison.Ordinal);
        Assert.Null(lockDocument["catalogues"]!["outputContracts"]);
        Assert.Null(ran["catalogues"]!["outputContracts"]);

        // the run facts: exactly the ones the package reads, the date beside its zone (part 08 § 8.1)
        Assert.Equal(
            """{"date":{"timeZone":"America/Toronto","value":"2026-11-16"},"id":"job-31-0001"}""",
            CanonicalJson.Write(job.State.RunFacts));
        Assert.Equal(10, job.State.Limits!.Count);

        // a second header call changes nothing
        var before = JsonSerializer.Serialize(job.State);
        job.Entity.RecordV31Header(new ConsultGenerationV31Header(
            "sha256:other", "sha256:other", new Dictionary<string, int> { ["maxAgentCalls"] = 1 }, new DateTimeOffset(V31Job.Now.AddHours(1), TimeSpan.Zero),
            new JsonObject { ["culture"] = "other" }, new JsonObject { ["id"] = "other" }));
        Assert.Equal(before, JsonSerializer.Serialize(job.State));
    }

    [Fact]
    public async Task APackageReadingNoRunFacts_RecordsNone()
    {
        var job = V31Job.Of("nested-note");

        await V31Job.RunAsync(job.Context());

        Assert.Equal("{}", job.State.RunFacts!.ToJsonString());
        Assert.NotNull(job.State.VersionsRan);
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
        foreach (var member in new[] { "ResolveHash", "LockHash", "Limits", "V31Progress", "VersionsRan", "RunFacts", "RecordHash", "RecordBytes", "InputHashes" })
            Assert.DoesNotContain($"\"{member}\"", below, StringComparison.Ordinal);
    }

    // ---- #1505 (area 08; part 06 § 8; part 09 § 3.8): delivery at the end --------------------------------------------

    [Fact]
    public async Task ACompletedJob_Delivers_ThenCompletes()
    {
        // checked-note on the default stubs: the patient letter is released (email and PDF), the note blocked
        var job = V31Job.Of("checked-note");
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        var delivering = context.Calls.FindIndex(c => c.Kind == "entity" && c.Name == nameof(ConsultGenerationJobEntity.MarkDelivering));
        var finalize = context.Calls.FindLastIndex(c => c.Kind == "entity" && c.Name == nameof(ConsultGenerationJobEntity.FinalizeJob));
        var sends = Sends(context);
        Assert.True(delivering >= 0);
        Assert.All(sends, s => Assert.InRange(s.At, delivering + 1, finalize - 1));
        // #1506 (D12, D13): the last send is the last activity but the delivery row's write and the seal after FinalizeJob
        Assert.Equal(context.Calls.FindLastIndex(c => c.Kind == "activity"
            && c.Name is not (ConsultGenerationActivityNames.WriteRecordRowsV31 or ConsultGenerationActivityNames.SealRecordV31)), sends[^1].At);
        Assert.Contains(context.Calls.Skip(sends[^1].At + 1), c => c.Name == ConsultGenerationActivityNames.WriteRecordRowsV31);
        Assert.Equal([("email", 1), ("pdf", 1)], sends.Select(s => (s.Input.Send.Kind, s.Input.Send.Attempt)));
        Assert.All(sends, s =>
        {
            Assert.Equal((V31Job.JobId, "user-1", "patient"), (s.Input.JobId, s.Input.AppUserId, s.Input.Send.Folder));
            var document = Assert.Single(s.Input.Send.Documents);
            Assert.Equal(("deliverable.patient.patient_letter", "Letter to the patient", "plaintext"), (document.Deliverable, document.Label, document.Format));
            Assert.StartsWith("Dear Margaret Okafor,", document.Text, StringComparison.Ordinal);
        });

        // the status: delivering in between, then Completed
        Assert.Equal(
            [ConsultGenerationJobStatuses.Running, ConsultGenerationJobStatuses.Delivering, ConsultGenerationJobStatuses.Completed],
            Statuses(context));

        // each destination's row on the entity; the window's timer cancelled when delivery ended
        Assert.Equal(2, job.State.V31Progress!.States["delivery:sent"]);
        Assert.Equal("delivery:deliverable.patient.patient_letter/pdf", job.State.V31Progress.CurrentKey);
        Assert.Contains(context.Calls, c => c.Kind == "timer" && c.InputJson == JsonSerializer.Serialize(V31Job.Now.AddMinutes(15)));
        Assert.Equal(0, context.PendingTimers);
    }

    [Theory]
    [InlineData("nested-note")]
    [InlineData("checked-note, the letter delivered nowhere")]
    public async Task AJobWithNothingToDeliver_NeverEntersDelivering(string package)
    {
        // nested-note delivers to []; R3: with the letter delivered nowhere, checked-note's only other deliverable is blocked
        var job = package == "nested-note"
            ? V31Job.Of("nested-note")
            : V31Job.Of(CheckedNote(package, files => files.WithJson("deliverables/patient/index.json", h => h["delivery"] = new JsonArray())));
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.DoesNotContain(context.Calls, c => c.Name == nameof(ConsultGenerationJobEntity.MarkDelivering));
        Assert.Empty(Sends(context));
        Assert.Equal([ConsultGenerationJobStatuses.Running, ConsultGenerationJobStatuses.Completed], Statuses(context));
        Assert.DoesNotContain(job.State.V31Progress!.States.Keys, state => state.StartsWith("delivery:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedDelivery_LeavesTheJobCompleted()
    {
        // Review Focus 4: the letter's email fails at every attempt (the activity fails, which the host makes a failed send);
        // its PDF is still in flight when the window closes. Both are failed, the job is Completed, and no timer is left.
        var job = V31Job.Of("checked-note");
        job.Deliver = input => input.Send.Kind == "email"
            ? throw new InvalidOperationException("Graph mail request failed with status 503.")
            : new DeliverySendOutcome(DeliverySendResult.Sent, null);
        var context = job.Context().WrapActivityFailures();

        await Task.Run(() =>
        {
            var run = V31Job.RunAsync(context.HoldActivities());
            while (context.ReleaseOldest((name, json) =>
                !(name == ConsultGenerationActivityNames.DeliverV31 && json.Contains("\"Kind\":\"pdf\"", StringComparison.Ordinal))))
            {
            }

            Assert.False(run.IsCompleted, "The PDF's send is in flight: delivery waits for it, or for the window.");
            context.EndHeldTimers();
            while (context.HeldActivities > 0)
                context.ReleaseOldest();
            Assert.True(run.IsCompleted, "The orchestration has not ended: something it waits on was never released.");
            return run;
        });

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Null(job.State.FailureError);
        Assert.Equal([("email", 1), ("pdf", 1), ("email", 2), ("email", 3)], Sends(context).Select(s => (s.Input.Send.Kind, s.Input.Send.Attempt)));
        Assert.Equal(2, job.State.V31Progress!.States["delivery:failed"]);
        Assert.Equal(
            [ConsultGenerationJobStatuses.Running, ConsultGenerationJobStatuses.Delivering, ConsultGenerationJobStatuses.Completed],
            Statuses(context));
        Assert.Equal(0, context.PendingTimers);
    }

    [Fact]
    public async Task AFailedJob_DeliversNothing_AndRecordsNotSent()
    {
        // the letter reads nothing, so it is released before the plan's agent fails the job: it is recorded not sent
        var job = V31Job.Of(CheckedNote("checked-note, the letter fixed text", files => files.With(
            "deliverables/patient/patient_letter.md",
            SourceFixture.Text(files, "deliverables/patient/patient_letter.md").Replace("{{ for s in node.sections.value }}{{ s }}\n\n{{ end }}", "", StringComparison.Ordinal))));
        var stub = job.Agent;
        job.Agent = call => call.Key == "node.plan" ? new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 5000, "error") : stub(call);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        V31Contract.AssertSentence(new SourceRefusal("S09-011", job.State.FailureError!, ""));
        Assert.DoesNotContain(context.Calls, c => c.Name == nameof(ConsultGenerationJobEntity.MarkDelivering));
        Assert.Empty(Sends(context));
        Assert.DoesNotContain(ConsultGenerationJobStatuses.Delivering, Statuses(context));
        Assert.Equal(2, job.State.V31Progress!.States["delivery:not sent"]);
        Assert.Equal("delivery:deliverable.patient.patient_letter/pdf", job.State.V31Progress.CurrentKey);
        Assert.Equal(0, context.PendingTimers);
    }

    [Fact]
    public async Task ABlockedDeliverable_IsNeverSent()
    {
        // R2: checked-note's note is blocked by proofread on the default stubs; its EMR destination is never sent to
        var job = V31Job.Of("checked-note");
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Equal(1, job.State.V31Progress!.States["blocked"]);
        var sends = Sends(context);
        Assert.NotEmpty(sends);
        Assert.DoesNotContain(sends, s => s.Input.Send.Kind == "emr");
        Assert.DoesNotContain(sends, s => s.Input.Send.Documents.Any(d => d.Deliverable == "deliverable.note"));
    }

    // ---- #1505 (area 08; part 09 § 3.9; D14): alerts for the operator -------------------------------------------------

    [Theory]
    [InlineData("a failed node")]
    [InlineData("a failed node, the alert failing too")]
    [InlineData("a timed-out run")]
    [InlineData("an engine fault")]
    public async Task AFailedJob_AlertsTheOperator(string how)
    {
        var job = V31Job.Of("nested-note");
        var stub = job.Agent;
        var context = job.Context().WrapActivityFailures();
        switch (how)
        {
            case "a timed-out run":
                // every call held; the job's timer (and the slow-job timer) end, as their time coming would
                await Task.Run(() =>
                {
                    var run = V31Job.RunAsync(context.HoldActivities());
                    while (context.ReleaseOldest((name, _) => name != ConsultGenerationActivityNames.RunAgentNodeV31))
                    {
                    }

                    Assert.False(run.IsCompleted, "The run waits on its agent calls.");
                    Assert.Equal(2, context.PendingTimers); // the job's and the slow-job timer
                    context.EndHeldTimers();
                    while (context.HeldActivities > 0)
                        context.ReleaseOldest();
                    Assert.True(run.IsCompleted, "The orchestration has not ended: something it waits on was never released.");
                    return run;
                });
                break;
            case "an engine fault":
                job.Agent = _ => throw new InvalidOperationException("Unauthorized: Margaret Okafor's prompt (fictional)");
                await Assert.ThrowsAsync<Microsoft.DurableTask.TaskFailedException>(() => V31Job.RunAsync(context));
                break;
            default:
                job.Agent = call => call.Key == "node.plan" ? new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 5000, "error") : stub(call);
                if (how.EndsWith("too", StringComparison.Ordinal))
                    job.Alert = _ => throw new InvalidOperationException("The mailbox is down.");
                await V31Job.RunAsync(context);
                break;
        }

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        Assert.Equal(
            how switch
            {
                "a timed-out run" => "The job didn't finish within 30 minutes. Run the workflow again later.",
                "an engine fault" => ConsultGenerationOrchestratorV31.EngineFault,
                _ => "The model service returned an error 5 times in a row. Run the workflow again later.",
            },
            job.State.FailureError);
        var alert = Assert.Single(context.Calls, c => c.Name == ConsultGenerationActivityNames.AlertOperatorV31);
        Assert.Equal(new OperatorAlert(OperatorAlertKinds.JobFailed, V31Job.JobId, how.StartsWith("a failed node", StringComparison.Ordinal) ? "node.plan" : null, null),
            JsonSerializer.Deserialize<OperatorAlert>(alert.InputJson));
        Assert.DoesNotContain("Margaret", alert.InputJson, StringComparison.Ordinal);
        // raised once the job is final, and after nothing else
        Assert.True(context.Calls.IndexOf(alert) > context.Calls.FindLastIndex(c => c.Name == nameof(ConsultGenerationJobEntity.FinalizeJob)));
        Assert.Equal(0, context.PendingTimers);
    }

    [Fact]
    public async Task AFailedDelivery_AlertsTheOperator()
    {
        // the letter's email fails at each of its 3 attempts; its PDF is sent: one alert, naming the failed row and the count
        var job = V31Job.Of("checked-note");
        job.Deliver = input => new DeliverySendOutcome(input.Send.Kind == "email" ? DeliverySendResult.Failed : DeliverySendResult.Sent, input.Send.Kind == "email" ? "error" : null);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Equal(1, job.State.V31Progress!.States["delivery:failed"]);
        Assert.Equal([new OperatorAlert(OperatorAlertKinds.DeliveryFailed, V31Job.JobId, "delivery:deliverable.patient.patient_letter/email", 1)], job.Alerts);
        var alert = context.Calls.FindIndex(c => c.Name == ConsultGenerationActivityNames.AlertOperatorV31);
        Assert.True(alert > Sends(context)[^1].At);
        Assert.True(alert < context.Calls.FindLastIndex(c => c.Name == nameof(ConsultGenerationJobEntity.FinalizeJob)));
        Assert.Equal(0, context.PendingTimers);
    }

    [Fact]
    public async Task ALongCapacityWait_AlertsTheOperatorOnce_ThroughTheActivity()
    {
        // the run's notice (StoredRunRecorder.NoticeAsync) is the activity: node.plan waits 3 minutes twice, 6 in all
        var job = V31Job.Of("nested-note");
        var stub = job.Agent;
        job.Agent = call => call.Key == "node.plan" && call.Attempt <= 2 ? new AgentOutcome(AgentOutcomeKind.Throttled, null, 180_000, null) : stub(call);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Equal([new OperatorAlert(OperatorAlertKinds.CapacityWait, V31Job.JobId, "node.plan", 6)], job.Alerts);
    }

    [Theory]
    [InlineData("nested-note")]
    [InlineData("checked-note")]
    public async Task ACompletedJob_AlertsNoOne(string package)
    {
        var job = V31Job.Of(package);
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Empty(job.Alerts);
        Assert.DoesNotContain(context.Calls, c => c.Name == ConsultGenerationActivityNames.AlertOperatorV31);
        // the slow-job timer was made at 80% of the job's 30 minutes, and cancelled with the job's when the run ended
        Assert.Contains(context.Calls, c => c.Kind == "timer" && c.InputJson == JsonSerializer.Serialize(V31Job.Now.AddMinutes(24)));
        Assert.Equal(0, context.PendingTimers);
    }

    /// <summary>Each <c>deliver-v31</c> call: where it is in the calls, and its input.</summary>
    private static List<(int At, DeliverV31Input Input)> Sends(Engine.FakeOrchestrationContext context) =>
        context.Calls
            .Select((call, at) => (call, at))
            .Where(c => c.call.Kind == "activity" && c.call.Name == ConsultGenerationActivityNames.DeliverV31)
            .Select(c => (c.at, JsonSerializer.Deserialize<DeliverV31Input>(c.call.InputJson)!))
            .ToList();

    /// <summary>The custom statuses the orchestration published, in order, from running on (the drain's wait publishes Queued before).</summary>
    private static List<string> Statuses(Engine.FakeOrchestrationContext context) =>
        context.CustomStatuses.Select(s => (string)JsonNode.Parse(s)!["status"]!)
            .SkipWhile(status => status != ConsultGenerationJobStatuses.Running)
            .ToList();

    /// <summary>checked-note, changed by <paramref name="change"/>, as a package of the test's own.</summary>
    private static CompiledGoldenTests.GoldenCase CheckedNote(string name, Func<Dictionary<string, byte[]>, Dictionary<string, byte[]>> change) =>
        new(name, () => change(CompiledGoldenTests.Cases().Single(c => c.Name == "checked-note").Source()), name);

    // ---- #1506 (area 09; D13): the record sealed at the end ---------------------------------------------------------------

    [Theory]
    [InlineData("completed")]
    [InlineData("a failed node")]
    [InlineData("a plan whose hash is not the job's")]
    [InlineData("inputs the retention drop deleted")]
    public async Task EveryEnd_SealsTheRecord_OnceAfterFinalizeJob(string how)
    {
        var job = V31Job.Of("nested-note");
        var stub = job.Agent;
        switch (how)
        {
            case "a failed node":
                job.Agent = call => call.Key == "node.plan" ? new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 5000, "error") : stub(call);
                break;
            case "a plan whose hash is not the job's":
                job.ResolveHash = "sha256:" + new string('0', 64);
                break;
            case "inputs the retention drop deleted":
                job.Inputs.Documents.Clear();
                break;
        }

        var context = job.Context();
        await V31Job.RunAsync(context);

        var seal = Assert.Single(context.Calls, c => c.Name == ConsultGenerationActivityNames.SealRecordV31);
        Assert.Equal(V31Job.JobId, JsonSerializer.Deserialize<SealRecordV31Input>(seal.InputJson)!.JobId);
        Assert.True(context.Calls.IndexOf(seal) > context.Calls.FindLastIndex(c => c.Name == nameof(ConsultGenerationJobEntity.FinalizeJob)));
        Assert.Equal(how == "completed" ? ConsultGenerationJobStatuses.Completed : ConsultGenerationJobStatuses.Failed, job.State.Status);
        Assert.NotNull(job.State.RecordHash);
        Assert.NotNull(await job.Blobs.ReadRecordAsync(V31Job.AccountKind, "user-1", V31Job.JobId, CancellationToken.None));
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
