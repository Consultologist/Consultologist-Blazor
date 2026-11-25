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
        Assert.Equal(Invalid("retry", 2), row.Detail["invalid"]!.ToJsonString());
        Assert.Empty(row.Detail["retries"]!.AsArray());
        Assert.Equal(3, result.Values["node.plan"]!["value"]!["sections"]!.AsArray().Count);
        Assert.Null(recorder.Row("node.summary").Detail["invalid"]);
        Assert.True(recorder.Row("node.summary").Detail.ContainsKey("invalid"));
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
        Assert.Equal(Invalid("fail", 1), row.Detail["invalid"]!.ToJsonString());
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
        Assert.Equal(Invalid("retry", 2), recorder.Row("node.plan").Detail["invalid"]!.ToJsonString());
        Assert.DoesNotContain("History", result.Sentence);
        Assert.DoesNotContain("History", recorder.Row("node.plan").Detail.ToJsonString());
    }

    /// <summary>A reply for a format, the reply its repair gets (null: the same again), and how the node ends.</summary>
    public static TheoryData<string, string?, string?, string> Replies() => new()
    {
        { "section_plan", "", null, "failed" },
        { "section_plan", " \n\t ", ThreeSections, "repaired" },
        { "section_plan", "not json", null, "failed" },
        { "section_plan", new string('[', 600) + new string(']', 600), ThreeSections, "repaired" },
        { "section_plan", "\"" + new string('a', 2 * 1024 * 1024) + "\"", null, "failed" },
        { "section_plan", "null", ThreeSections, "repaired" },
        { "section_plan", null, null, "failed" },
        { "section_plan", """[{"title":"History","kind":"narrative"}]""", ThreeSections, "repaired" },
        { "section_plan", """{"sections":[],"sections":[]}""", null, "failed" },
        { "section_plan", ThreeSections, null, "completed" },
        { "plaintext", "", null, "completed" },
        { "plaintext", " \n\t ", null, "completed" },
        { "plaintext", new string('a', 2 * 1024 * 1024), null, "completed" },
        { "plaintext", null, null, "completed" },
    };

    [Theory]
    [MemberData(nameof(Replies))]
    public async Task AnyReply_SettlesTheNode(string format, string? reply, string? repair, string expected)
    {
        var plan = RunFixture.Plan(Planned(format: $"\"{format}\""), StubTemplates.Empty());
        var first = new AgentOutcome(AgentOutcomeKind.Completed, reply, 0, null);
        var host = new FakeRunHost(auto: true).Agent("node.plan", first, repair == null ? first : Reply(repair));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        var row = recorder.Row("node.plan");
        switch (expected)
        {
            case "completed":
                Assert.Equal(RunState.Completed, result.State);
                Assert.Single(host.Calls);
                Assert.Null(row.Detail["invalid"]);
                break;
            case "repaired":
                Assert.Equal(RunState.Completed, result.State);
                Assert.Equal(2, host.Calls.Count);
                Assert.Equal(Invalid("retry", 2, format), row.Detail["invalid"]!.ToJsonString());
                break;
            default:
                Assert.Equal(RunState.Failed, result.State);
                Assert.Equal(RowState.Failed, row.State);
                Assert.Equal($"Node 'plan' gave a reply that isn't {format}.", result.Sentence);
                V31Contract.AssertSentence(new SourceRefusal("A06-005", result.Sentence!, ""));
                Assert.Equal(2, host.Calls.Count);
                break;
        }
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

    [Theory]
    [InlineData("\"markdown_block\"", "&#32; \n&#9;", RunState.Failed)]
    [InlineData("\"markdown_inline\"", "&#9;", RunState.Failed)]
    [InlineData("\"plaintext\"", "&#32; \n&#9;", RunState.Completed)]
    public async Task TheEscapesReferences_AreBlank_OnlyInMarkdown(string format, string reply, RunState state)
    {
        // Task 15's review: in Markdown, &#32; and &#9; are what the escape writes for a line's leading space or tab, so a
        // value of only those and white space is empty; in plain text they are the characters written.
        const string sentence = "The plan came back empty for this consult draft.";
        var plan = RunFixture.Plan(Planned(format: format, failIfEmpty: $"\"{sentence}\""), StubTemplates.Empty());

        var result = await new PlanRunner(plan, new RunLimits(), new FakeRunHost(auto: true).Reply("node.plan", reply), new ListRecorder())
            .RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.True(result.State == state, result.Sentence);
    }

    [Theory]
    [InlineData("Seen by **Dr A. Sample** in clinic.", null)]
    [InlineData("Seen by **Dr A. Sample**\nin clinic.", "Node 'plan' gave a reply that isn't markdown_inline: markdown_inline text sits inside a line and holds no line ending.")]
    [InlineData("Seen by **Dr A. Sample**\r\nin clinic.", "Node 'plan' gave a reply that isn't markdown_inline: markdown_inline text sits inside a line and holds no line ending.")]
    [InlineData("Seen by **Dr A. Sample**\r", "Node 'plan' gave a reply that isn't markdown_inline: markdown_inline text sits inside a line and holds no line ending.")]
    public async Task AMarkdownInlineValue_IsOneLine(string reply, string? sentence)
    {
        // #1507, 2026-11-22 (2), 3 (R10) and D21: a markdown_inline value holds no line ending (part 02 § 5: it sits inside
        // a line). A reply that holds one does not fit its format: asked again under retry, then the node fails with A10-052.
        var plan = RunFixture.Plan(Planned(format: "\"markdown_inline\""), StubTemplates.Empty());
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), new FakeRunHost(auto: true).Reply("node.plan", reply), recorder)
            .RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        if (sentence is null)
        {
            Assert.Equal(RunState.Completed, result.State);
            Assert.Equal(reply, (string?)result.Values["node.plan"]!["value"]);
            return;
        }

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal(sentence, result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A10-052", result.Sentence!, ""));
        Assert.Equal(new JsonObject { ["reason"] = sentence, ["action"] = "retry", ["attempts"] = 2 }.ToJsonString(),
            recorder.Row("node.plan").Detail["invalid"]!.ToJsonString());
        Assert.DoesNotContain("Sample", recorder.Row("node.plan").Detail.ToJsonString());
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
                    ["waitMs"] = 1000,
                    ["kind"] = "capacity",
                    ["error"] = null,
                }.ToJsonString(),
                retries[i]!.ToJsonString());
        }

        Assert.Null(recorder.Row("node.plan").Detail["invalid"]);
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
    [InlineData("timeout", "timed out", "timeout")]
    [InlineData("error", "returned an error", "5xx")]
    [InlineData("unreachable", "couldn't be reached", "connection")]
    public async Task AFailingService_FailsAfterFiveAttempts(string kind, string words, string recorded)
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
            Assert.Equal("service", (string?)r!["kind"]);
            Assert.Equal(recorded, (string?)r["error"]);
            Assert.Equal(5000, (int)r["waitMs"]!);
        });
    }

    [Theory]
    [InlineData("steps", "S05-062", "Node 'plan' stopped rendering: it took more than 100,000 steps.")]
    [InlineData("compose", "A06-008", "Node 'plan' couldn't be rendered.")]
    [InlineData("engine", "A11-008", "Node 'plan' couldn't be rendered because the engine failed. Tell the operator.")]
    [InlineData("plan", "S07-006", null)]
    public async Task ACallThatCouldNotBeMade_FailsTheNodeAtOnce_WithItsSentence(string kind, string id, string? expected)
    {
        var plan = RunFixture.Plan(Planned(), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true).Agent("node.plan", new AgentOutcome(AgentOutcomeKind.Failed, null, 0, kind));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.plan", result.FailedKey);
        Assert.Equal(expected ?? ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence, result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal(id, result.Sentence!, ""));
        // no retry: the one attempt, the row failed with the sentence
        Assert.Single(host.Calls);
        Assert.Equal(2, host.DelaysMade); // the job's timer and (#1505, D14) the slow-job timer only
        var row = recorder.Row("node.plan");
        Assert.Equal(RowState.Failed, row.State);
        Assert.Equal(result.Sentence, row.Reason);
        Assert.Empty(row.Detail["retries"]!.AsArray());
    }

    [Fact]
    public async Task AThrottledCall_WithNoWait_WaitsTheBackOffsStart_AndCountsIt()
    {
        var plan = RunFixture.Plan(Planned(), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true).Agent("node.plan", Throttled(0), Throttled(-1));
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(CapacityWaitMinutes: 1), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            Assert.Single(host.Calls); // no call again at once
            for (var wait = 1; wait <= 12; wait++)
                host.Advance(TimeSpan.FromSeconds(5));
            return run;
        });

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("The model service stayed busy for 1 minutes. Run the workflow again later.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S09-010", result.Sentence!, ""));
        Assert.Equal(13, host.Calls.Count); // twelve waits of 5 s make the minute; the thirteenth would pass it
        Assert.All(recorder.Row("node.plan").Detail["retries"]!.AsArray(), r => Assert.Equal(5000, (int)r!["waitMs"]!));
    }

    [Fact]
    public async Task AFailingCall_WithNoWait_WaitsTheBackOffsStart()
    {
        var plan = RunFixture.Plan(Planned(), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true)
            .Agent("node.plan", new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 0, "timeout"), Reply(ThreeSections));
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            Assert.Single(host.Calls);
            host.Advance(TimeSpan.FromMilliseconds(4999));
            Assert.Single(host.Calls);
            host.Advance(TimeSpan.FromMilliseconds(1));
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(2, host.Calls.Count);
        Assert.Equal(5000, (int)recorder.Row("node.plan").Detail["retries"]![0]!["waitMs"]!);
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

    [Theory]
    [InlineData("completes")]
    [InlineData("fails")]
    [InlineData("times out")]
    public async Task WhenTheRunEnds_NoTimerIsLeftOutstanding(string ending)
    {
        var plan = RunFixture.Plan(PlanRunnerCeilingTests.Independent(2), StubTemplates.Empty());
        var host = new FakeRunHost(auto: true);
        var limits = new RunLimits(JobMinutes: 1);
        switch (ending)
        {
            case "completes":
                host.Agent("node.n1", Throttled(1000), Reply("A first part for Margaret Okafor."));
                break;
            case "fails":
                // node.n1 is waiting to retry when node.n2 fails the job.
                host.Agent("node.n1", Throttled(60_000));
                host.Agent("node.n2", new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 1000, "error"));
                limits = limits with { ServiceAttempts = 1 };
                break;
            default:
                host.Agent("node.n1", Throttled(120_000));
                break;
        }

        var runner = new PlanRunner(plan, limits, host, new ListRecorder());
        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            if (ending != "fails")
            {
                Assert.Equal(3, host.OutstandingDelays); // the job's timer, (#1505, D14) the slow-job timer and node.n1's wait
                host.Advance(TimeSpan.FromSeconds(ending == "completes" ? 1 : 60));
            }

            return run;
        });

        Assert.Equal(
            ending switch { "completes" => RunState.Completed, "fails" => RunState.Failed, _ => RunState.TimedOut },
            result.State);
        Assert.True(host.DelaysMade >= 2);
        Assert.Equal(0, host.OutstandingDelays);
    }

    // ---- fixtures -------------------------------------------------------------------------------

    /// <summary>Part 08 § 4's <c>invalid</c> for <c>plan</c>'s reply under <paramref name="action"/>, after <paramref name="attempts"/> replies.</summary>
    private static string Invalid(string action, int attempts, string format = "section_plan") => new JsonObject
    {
        ["reason"] = $"Node 'plan' gave a reply that isn't {format}.",
        ["action"] = action,
        ["attempts"] = attempts,
    }.ToJsonString();

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
