using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.2, 3.4, 9; part 05 § 1.2; part 02 § 3.4; D7): what happens to an agent's
/// reply and to a call the model service refuses. A reply is read as its format's value and validated; an invalid one is
/// asked again once, in the same slot and as the same call, under <c>onInvalid: "retry"</c>, and fails the node with
/// A06-005 otherwise; <c>failIfEmpty</c> fails an empty value with the package's sentence. A throttled call waits on the
/// host's timer and calls again, holding its slot, until its waits pass <c>capacityWaitMinutes</c> (S09-010); a failing
/// one until <c>serviceAttempts</c> (S09-011). Each wait is on the row's <c>retries</c>. A job still running at
/// <c>jobMinutes</c> times out (S09-012), each call in flight recorded cancelled (S08-009), and nothing later is read.
/// Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PlanRunnerAgentsTests
{
    private const string ThreeSections =
        """{"sections":[{"title":"History","kind":"narrative"},{"title":"Examination","kind":"narrative"},{"title":"Plan","kind":"table"}]}""";

    // ---- onInvalid ------------------------------------------------------------------------------

    [Fact]
    public async Task AnInvalidReply_IsRepairedOnce_InTheSameSlot()
    {
        var plan = RunFixture.Plan(Planned(withSummary: true), SummaryReads());
        AssertSummaryRanksBeforePlan(plan);
        var host = new FakeRunHost().Agent("node.plan", Reply("""{"sections":"History"}"""), Reply(ThreeSections));
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(MaxAgentCalls: 1), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            Assert.Equal(["node.plan"], host.Dispatched);
            host.Release("node.billing_text"); // node.summary is now ready, and ranks before node.plan
            host.Release("node.plan");          // invalid: the repair takes the slot node.plan holds

            Assert.Equal(["node.plan", "node.plan"], host.Dispatched);
            host.ReleaseAll();
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.plan", "node.plan", "node.summary"], host.Dispatched);
        Assert.Equal((1, false), (host.Calls[0].Attempt, host.Calls[0].Repair));
        Assert.Equal((2, true), (host.Calls[1].Attempt, host.Calls[1].Repair));
        Assert.Equal(host.Calls[0].Scope.ToJsonString(), host.Calls[1].Scope.ToJsonString());
        Assert.Equal(2, result.AgentCalls); // node.plan's call, its repair included, and node.summary's
        Assert.Equal(1, host.InFlightPeak);

        var row = recorder.Row("node.plan");
        Assert.Equal(RowState.Completed, row.State);
        Assert.True((bool)row.Detail["repaired"]!);
        Assert.Empty(row.Detail["retries"]!.AsArray());
        Assert.Equal(3, result.Values["node.plan"]!["value"]!["sections"]!.AsArray().Count);
        Assert.False((bool)recorder.Row("node.summary").Detail["repaired"]!);
    }

    [Fact]
    public async Task AnInvalidReply_WithOnInvalidFail_FailsTheNode()
    {
        var plan = RunFixture.Plan(Planned(onInvalid: "fail"), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true).Agent("node.plan", Reply("not json"), Reply(ThreeSections));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.plan", result.FailedKey);
        Assert.Equal("Node 'plan' gave a reply that isn't section_plan.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A06-005", result.Sentence!, ""));
        Assert.Single(host.Calls);
        var row = recorder.Row("node.plan");
        Assert.Equal(RowState.Failed, row.State);
        Assert.Equal(result.Sentence, row.Reason);
        Assert.False((bool)row.Detail["repaired"]!);
    }

    [Fact]
    public async Task ASecondInvalidReply_FailsTheNode()
    {
        var plan = RunFixture.Plan(Planned(), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true).Agent("node.plan", Reply("[]"), Reply("""{"sections":[{"title":"History"}]}"""));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("Node 'plan' gave a reply that isn't section_plan.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A06-005", result.Sentence!, ""));
        Assert.Equal(2, host.Calls.Count);
        Assert.True(host.Calls[1].Repair);
        Assert.Equal(1, result.AgentCalls);
        Assert.True((bool)recorder.Row("node.plan").Detail["repaired"]!);
        Assert.DoesNotContain("History", result.Sentence);
    }

    public static TheoryData<string, string?> Replies() => new()
    {
        { "section_plan", "" },
        { "section_plan", " \n\t " },
        { "section_plan", "not json" },
        { "section_plan", new string('[', 600) + new string(']', 600) },
        { "section_plan", "\"" + new string('a', 2 * 1024 * 1024) + "\"" },
        { "section_plan", "null" },
        { "section_plan", null },
        { "section_plan", """[{"title":"History","kind":"narrative"}]""" },
        { "section_plan", """{"sections":[],"sections":[]}""" },
        { "plaintext", "" },
        { "plaintext", " \n\t " },
        { "plaintext", new string('a', 2 * 1024 * 1024) },
        { "plaintext", null },
    };

    [Theory]
    [MemberData(nameof(Replies))]
    public async Task AnyReply_SettlesTheNode(string format, string? reply)
    {
        var plan = RunFixture.Plan(Planned(format: $"\"{format}\""), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true).Agent("node.plan", new AgentOutcome(AgentOutcomeKind.Completed, reply, 0, null));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        var row = recorder.Row("node.plan");
        if (result.State == RunState.Completed)
        {
            Assert.Equal(RowState.Completed, row.State);
        }
        else
        {
            Assert.Equal(RunState.Failed, result.State);
            Assert.Equal(RowState.Failed, row.State);
            Assert.Equal($"Node 'plan' gave a reply that isn't {format}.", result.Sentence);
            V31Contract.AssertSentence(new SourceRefusal("A06-005", result.Sentence!, ""));
            Assert.Equal(2, host.Calls.Count); // repaired once, under onInvalid: "retry"
        }

        if (format == "plaintext")
            Assert.Equal(RunState.Completed, result.State); // a text format's reply is its text
    }

    [Fact]
    public async Task AnEmptyValue_WithFailIfEmpty_FailsWithThePackagesSentence()
    {
        const string sentence = "The plan came back empty for this consult draft.";
        var plan = RunFixture.Plan(Planned(format: "\"plaintext\"", failIfEmpty: $"\"{sentence}\""), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true).Reply("node.plan", " \n ");
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal(sentence, result.Sentence);
        Assert.Single(host.Calls); // an empty value is valid: it is not repaired
        Assert.Equal(sentence, recorder.Row("node.plan").Reason);

        // Without failIfEmpty, the same reply completes.
        var unguarded = RunFixture.Plan(Planned(format: "\"plaintext\""), StubTemplates.Empty());
        var again = await new PlanRunner(unguarded, new RunLimits(), new FakeRunHost(auto: true).Reply("node.plan", " \n "), new ListRecorder())
            .RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        Assert.Equal(RunState.Completed, again.State);
    }

    // ---- retries --------------------------------------------------------------------------------

    [Fact]
    public async Task AThrottledCall_WaitsAndRetries_HoldingItsSlot()
    {
        var plan = RunFixture.Plan(Planned(withSummary: true), SummaryReads());
        AssertSummaryRanksBeforePlan(plan);
        var host = new FakeRunHost(auto: true)
            .Agent("node.plan", Throttled(1000), Throttled(1000), Throttled(1000), Reply(ThreeSections));
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(MaxAgentCalls: 1), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            for (var wait = 1; wait <= 3; wait++)
            {
                // node.summary is ready (node.billing_text rendered at once), but node.plan holds the only slot while it waits.
                Assert.Equal(Enumerable.Repeat("node.plan", wait), host.Dispatched);
                host.Advance(TimeSpan.FromSeconds(1));
            }

            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.plan", "node.plan", "node.plan", "node.plan", "node.summary"], host.Dispatched);
        Assert.Equal([1, 2, 3, 4], host.Calls.Take(4).Select(c => c.Attempt));
        Assert.All(host.Calls, c => Assert.False(c.Repair));
        Assert.Equal(FakeRunHost.Start.AddSeconds(3), host.UtcNow);
        Assert.Equal(2, result.AgentCalls); // a retry is part of its call
        Assert.Equal(1, result.PeakInFlight);

        var retries = recorder.Row("node.plan").Detail["retries"]!.AsArray();
        Assert.Equal(3, retries.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(
                new JsonObject
                {
                    ["atUtc"] = FakeRunHost.Start.AddSeconds(i).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    ["waitMilliseconds"] = 1000,
                    ["for"] = "capacity",
                    ["errorKind"] = null,
                }.ToJsonString(),
                retries[i]!.ToJsonString());
        }

        Assert.False((bool)recorder.Row("node.plan").Detail["repaired"]!);
        Assert.Empty(recorder.Row("node.summary").Detail["retries"]!.AsArray());
    }

    [Fact]
    public async Task ABusyService_FailsTheNodeAfterTenMinutes()
    {
        var plan = RunFixture.Plan(Planned(), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true).Agent("node.plan", Throttled(60_000));
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            for (var wait = 1; wait <= 10; wait++)
                host.Advance(TimeSpan.FromMinutes(1));
            return run;
        });

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.plan", result.FailedKey);
        Assert.Equal("The model service stayed busy for 10 minutes. Run the workflow again later.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S09-010", result.Sentence!, ""));
        Assert.Equal(11, host.Calls.Count); // ten waits taken; the eleventh would pass ten minutes
        var row = recorder.Row("node.plan");
        Assert.Equal(RowState.Failed, row.State);
        Assert.Equal(result.Sentence, row.Reason);
        Assert.Equal(10, row.Detail["retries"]!.AsArray().Count);
        Assert.Equal(1, result.AgentCalls);
    }

    [Theory]
    [InlineData("timeout", "timed out")]
    [InlineData("error", "returned an error")]
    [InlineData("unreachable", "couldn't be reached")]
    public async Task AFailingService_FailsAfterFiveAttempts(string kind, string words)
    {
        var plan = RunFixture.Plan(Planned(), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true).Agent("node.plan", new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 5000, kind));
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            for (var wait = 1; wait <= 4; wait++)
                host.Advance(TimeSpan.FromSeconds(5));
            return run;
        });

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal($"The model service {words} 5 times in a row. Run the workflow again later.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S09-011", result.Sentence!, ""));
        Assert.Equal(5, host.Calls.Count);
        var retries = recorder.Row("node.plan").Detail["retries"]!.AsArray();
        Assert.Equal(4, retries.Count);
        Assert.All(retries, r =>
        {
            Assert.Equal("service", (string?)r!["for"]);
            Assert.Equal(kind, (string?)r["errorKind"]);
            Assert.Equal(5000, (int)r["waitMilliseconds"]!);
        });
    }

    // ---- the job limit --------------------------------------------------------------------------

    [Fact]
    public async Task AJobPastItsLimit_TimesOut_AndCancelsWhatIsRunning()
    {
        var plan = RunFixture.Plan(PlanRunnerCeilingTests.Independent(3), StubTemplates.Empty());
        var host = new FakeRunHost().Fault("node.n3");
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            host.Release("deliverable.note");
            Assert.Equal(["node.n1", "node.n2", "node.n3"], host.Held);
            host.Advance(TimeSpan.FromMinutes(29));
            Assert.False(run.IsCompleted);
            host.Advance(TimeSpan.FromMinutes(1));
            return run;
        });

        Assert.Equal(RunState.TimedOut, result.State);
        Assert.Equal("The job didn't finish within 30 minutes. Run the workflow again later.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S09-012", result.Sentence!, ""));
        Assert.Null(result.FailedKey);

        var cancelled = recorder.Rows.Where(r => r.State == RowState.Cancelled).ToList();
        Assert.Equal(["node.n1", "node.n2", "node.n3"], cancelled.Select(r => r.Key));
        Assert.All(cancelled, r =>
        {
            Assert.Equal("cancelled: the job didn't finish within 30 minutes", r.Reason);
            V31Contract.AssertSentence(new SourceRefusal("S08-009", r.Reason!, ""));
            Assert.Empty(r.Detail["retries"]!.AsArray());
        });

        // Late answers change nothing, and a late fault is never an unobserved task exception.
        var rows = recorder.Rows.Count;
        var unobserved = new List<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> seen = (_, e) =>
        {
            if (e.Exception.InnerExceptions.Any(x => x.Message.Contains("The fake host failed node.n3", StringComparison.Ordinal)))
                unobserved.Add(e.Exception);
        };
        TaskScheduler.UnobservedTaskException += seen;
        try
        {
            host.ReleaseAll();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= seen;
        }

        Assert.Empty(unobserved);
        Assert.Equal(rows, recorder.Rows.Count);
        Assert.Equal(3, host.Calls.Count);
        Assert.Equal("""{"value":null,"content":null,"state":"cancelled"}""", result.Values["node.n1"]!.ToJsonString());
    }

    [Fact]
    public async Task AJobAlreadyPastItsLimit_TimesOut_BeforeItCallsAnything()
    {
        var plan = RunFixture.Plan(Planned(), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder)
            .RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start.AddMinutes(-30));

        Assert.Equal(RunState.TimedOut, result.State);
        V31Contract.AssertSentence(new SourceRefusal("S09-012", result.Sentence!, ""));
        Assert.Empty(host.Calls);
        Assert.Empty(recorder.Rows);
    }

    // ---- fixtures -------------------------------------------------------------------------------

    private static AgentOutcome Reply(string reply) => new(AgentOutcomeKind.Completed, reply, 0, null);

    private static AgentOutcome Throttled(int waitMilliseconds) => new(AgentOutcomeKind.Throttled, null, waitMilliseconds, null);

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> with its agent <c>plan</c> of format <paramref name="format"/> under
    /// <paramref name="onInvalid"/> and <paramref name="failIfEmpty"/> (JSON), and a deliverable that reads nothing. With
    /// <paramref name="withSummary"/>, before it the rendered node <c>billing_text</c> and the agent <c>summary</c>, which
    /// reads it.
    /// </summary>
    private static Dictionary<string, byte[]> Planned(
        string onInvalid = "retry", string format = "\"section_plan\"", string failIfEmpty = "null", bool withSummary = false)
    {
        var files = format == "\"section_plan\"" ? ResolveFixture.WithFormats(Minimal(), "section_plan") : Minimal();
        files.With("nodes/plan.md", Md(
                SourceFixture.Node("agent", "Planning the sections", format, false, true,
                    $"\"if\": null, \"onInvalid\": \"{onInvalid}\", \"failIfEmpty\": {failIfEmpty}, \"parts\": null, \"ordered\": false"),
                "Plan the sections of a consultation note for Margaret Okafor.\n"))
            .With("deliverables/note.md", Md(Deliverable("Consultation note", PlanRunnerLeavesTests.Plain, "null", "hold"), "Dr A. Sample\n"));
        if (withSummary)
        {
            files.With("nodes/index.json", Index(["billing_text", "summary", "plan"]))
                .With("nodes/billing_text.md", Md(
                    SourceFixture.Node("rendered", "Billing text", PlanRunnerLeavesTests.Plain, true, false, "\"if\": null"),
                    "Consultation with Margaret Okafor, 45 minutes.\n"))
                .With("nodes/summary.md", Agent("Summarising the billing", PlanRunnerLeavesTests.Plain,
                    "Summarise the billing text below.\n\n{{ node.billing_text.content }}\n"));
        }

        return files;
    }

    private static StubTemplates SummaryReads() => new(new Dictionary<string, string[]>
    {
        ["node.summary"] = ["node.billing_text.content"],
    });

    /// <summary>The precondition the slot tests rest on: a call that ranks before <c>plan</c> is waiting while it holds the slot.</summary>
    private static void AssertSummaryRanksBeforePlan(RunPlan plan) =>
        Assert.True(plan.Entries["node.summary"].Order < plan.Entries["node.plan"].Order, "node.summary must rank before node.plan.");
}
