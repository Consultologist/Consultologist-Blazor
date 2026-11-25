using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Publish;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Publish;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 12; part 07 §§ 8.3, 9; D7, D8; Review Focus 3 and 5): a test's expectations against its
/// offline run (<see cref="TestExpectations.First"/>), and the run's outcome hash (<see cref="TestOutcome.Hash"/>). The runs are
/// of the compiled fixtures of areas 05 to 10 (a loop, a <c>while</c>, a <c>case</c>, a check loop, a gated deliverable), with
/// fictional text (Margaret Okafor, Dr A. Sample). A mismatch on a prompt or a deliverable's text holds hashes, never the text.
/// </summary>
public class TestExpectationsTests
{
    private const string TwoSections = """{"sections":[{"title":"History","kind":"narrative"},{"title":"Plan","kind":"table"}]}""";

    private static Dictionary<string, JsonNode?> Stubs(string sections = TwoSections) =>
        new(StringComparer.Ordinal) { ["node.plan"] = JsonNode.Parse(sections) };

    private static async Task<(RunPlan Plan, OfflineResult Result)> RunAsync(
        Dictionary<string, byte[]> files, Dictionary<string, JsonNode?>? stubs = null, bool strict = false)
    {
        var plan = RunFixture.Plan(files);
        var request = SnapshotTest.Fictional(plan) with { Outputs = stubs ?? new Dictionary<string, JsonNode?>(StringComparer.Ordinal), GenerateMissing = !strict };
        return (plan, await OfflineRun.RunAsync(plan, request));
    }

    private static Task<(RunPlan Plan, OfflineResult Result)> Nested() => RunAsync(ResolveFixture.NestedNote(), Stubs());

    private static JsonObject Expect(string json) => JsonNode.Parse(json)!.AsObject();

    private static TestMismatch? First((RunPlan Plan, OfflineResult Result) run, string expect) =>
        TestExpectations.First(run.Plan, Expect(expect), run.Result);

    // ---- the fixture runs are what the tests below say they are ------------------------------------------------

    [Fact]
    public async Task TheNestedNoteRun_Completes_AndAnEmptyExpectIsNeverAMismatch()
    {
        var run = await Nested();

        Assert.Equal(RunState.Completed, run.Result.State);
        Assert.Null(First(run, "{}"));
    }

    // ---- ran ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Ran_Passes_ForInstancesThatCompleted()
    {
        var run = await Nested();

        Assert.Null(First(run, """{ "ran": ["node.plan", "node.sections:2", "node.sections.outline:1", "node.refine.judge#2"] }"""));
    }

    [Fact]
    public async Task Ran_Fails_ForAnInstanceThatDidNotRun_ByKeyInOrdinalOrder()
    {
        var run = await Nested();

        var mismatch = First(run, """{ "ran": ["node.sections:3", "node.refine.judge#9", "node.plan"] }""");

        Assert.Equal(new TestMismatch("ran", "node.refine.judge#9", "ran", "not run"), mismatch);
    }

    // ---- skipped ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Skipped_Passes_ForAnInstanceSkippedByItsCondition_AndFails_ForOneThatRan()
    {
        var run = await RunAsync(ResolveFixture.Conditional());
        var skipped = run.Result.Rows.First(r => r.State == RowState.Skipped && r.Key.StartsWith("node.patient_summaries.narrative_only:", StringComparison.Ordinal)).Key;

        Assert.Null(First(run, $$"""{ "skipped": ["{{skipped}}"] }"""));
        Assert.Equal(new TestMismatch("skipped", "node.plan", "skipped", "ran"), First(run, """{ "skipped": ["node.plan"] }"""));
    }

    [Fact]
    public async Task Skipped_Passes_ForItemsNotRunByMax()
    {
        var files = ResolveFixture.NestedNote()
            .WithJson("nodes/sections/index.json", h => { h["max"] = 1; h["parallel"] = false; });
        var run = await RunAsync(files, Stubs());

        var row = run.Result.Rows.Single(r => r.Key == "node.sections:2");
        Assert.Equal(RowState.NotRun, row.State);
        Assert.Null(First(run, """{ "skipped": ["node.sections:2"] }"""));
        Assert.Equal(new TestMismatch("ran", "node.sections:2", "ran", "not run"), First(run, """{ "ran": ["node.sections:2"] }"""));
    }

    [Fact]
    public async Task Skipped_Passes_ForItemsNotRunByUntil()
    {
        var files = ResolveFixture.NestedNote()
            .WithJson("nodes/sections/index.json", h =>
            {
                h["parallel"] = false;
                h["until"] = JsonNode.Parse("""{ "!": [{ "blank": [{ "var": "node.outline.content" }] }] }""");
            });
        var run = await RunAsync(files, Stubs());

        Assert.Equal("until", (string?)run.Result.Rows.Single(r => r.Key == "node.sections").Detail["stopped"]);
        Assert.Null(First(run, """{ "skipped": ["node.sections:2"] }"""));
        Assert.Equal(new TestMismatch("ran", "node.sections:2", "ran", "not run"), First(run, """{ "ran": ["node.sections:2"] }"""));
    }

    // ---- decisions ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Decisions_ByTheirIds_AreCheckedAsABooleanOrABranch()
    {
        var run = await Nested();
        Assert.Equal(["node.refine/if:pass.1", "node.refine/if:pass.2"], run.Result.Decisions.Select(d => d.Id).Where(id => id.StartsWith("node.refine", StringComparison.Ordinal)));

        Assert.Null(First(run, """{ "decisions": { "node.refine/if:pass.1": true, "node.refine/if:pass.2": false } }"""));
        Assert.Equal(
            new TestMismatch("decisions", "node.refine/if:pass.2", "true", "false"),
            First(run, """{ "decisions": { "node.refine/if:pass.1": true, "node.refine/if:pass.2": true } }"""));
        Assert.Equal(
            new TestMismatch("decisions", "node.refine/if:pass.3", "false", "not run"),
            First(run, """{ "decisions": { "node.refine/if:pass.3": false } }"""));
    }

    [Fact]
    public async Task Decisions_OfACase_AreCheckedByTheBranchTaken()
    {
        var run = await RunAsync(ResolveFixture.Conditional());
        var decision = run.Result.Decisions.First(d => d.Branch != null);
        var other = decision.Branch == "1" ? "2" : "1";

        Assert.Null(First(run, $$"""{ "decisions": { "{{decision.Id}}": "{{decision.Branch}}" } }"""));
        Assert.Equal(
            new TestMismatch("decisions", decision.Id, other, decision.Branch!),
            First(run, $$"""{ "decisions": { "{{decision.Id}}": "{{other}}" } }"""));
        Assert.Equal(
            new TestMismatch("decisions", decision.Id, "true", decision.Branch!),
            First(run, $$"""{ "decisions": { "{{decision.Id}}": true } }"""));
    }

    // ---- loops --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Loops_Count_IsCheckedOnAForLoop()
    {
        var run = await Nested();

        Assert.Null(First(run, """{ "loops": { "node.sections": { "count": 2 } } }"""));
        Assert.Equal(new TestMismatch("loops", "node.sections", "count 3", "count 2"), First(run, """{ "loops": { "node.sections": { "count": 3 } } }"""));
        Assert.Equal(new TestMismatch("loops", "node.nothing", "count 1", "not run"), First(run, """{ "loops": { "node.nothing": { "count": 1 } } }"""));
    }

    [Fact]
    public async Task Loops_PassesAndStopped_AreCheckedOnAWhile()
    {
        var run = await Nested();
        var row = run.Result.Rows.Single(r => r.Key == "node.refine");
        var stopped = (string)row.Detail["stopped"]!;

        Assert.Equal(2, (int)row.Detail["passes"]!);
        Assert.Null(First(run, $$"""{ "loops": { "node.refine": { "passes": 2, "stopped": "{{stopped}}" } } }"""));
        Assert.Equal(
            new TestMismatch("loops", "node.refine", $"passes 3, stopped {stopped}", $"passes 2, stopped {stopped}"),
            First(run, $$"""{ "loops": { "node.refine": { "passes": 3, "stopped": "{{stopped}}" } } }"""));
        Assert.Equal(
            new TestMismatch("loops", "node.refine", "passes 2, stopped bound", $"passes 2, stopped {stopped}"),
            First(run, """{ "loops": { "node.refine": { "passes": 2, "stopped": "bound" } } }"""));
    }

    // ---- values -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Values_AreComparedAsCanonicalJson_OneEqualsOnePointZero_AndMemberOrderIsIgnored()
    {
        var run = await Nested();

        Assert.Null(First(run, """{ "values": { "node.section_count": 2 } }"""));
        Assert.Null(First(run, """{ "values": { "node.section_count": 2.0 } }"""));
        Assert.Null(First(run, """
            { "values": { "node.plan": { "sections": [ { "kind": "narrative", "title": "History" }, { "title": "Plan", "kind": "table" } ] } } }
            """));
    }

    [Fact]
    public async Task Values_MismatchGivesTheCanonicalValuesOnBothSides()
    {
        var run = await Nested();

        Assert.Equal(new TestMismatch("values", "node.section_count", "3", "2"), First(run, """{ "values": { "node.section_count": 3 } }"""));
        Assert.Equal(
            new TestMismatch("values", "node.plan", """{"sections":[]}""", CanonicalJson.Write(JsonNode.Parse(TwoSections))),
            First(run, """{ "values": { "node.plan": { "sections": [] } } }"""));
        Assert.Equal(new TestMismatch("values", "node.nothing", "1", "not run"), First(run, """{ "values": { "node.nothing": 1 } }"""));
    }

    // ---- prompts ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Prompts_AreCheckedByTheirTextOrTheirHash()
    {
        var run = await Nested();
        var prompt = run.Result.Prompts.First(p => p.Key == "node.plan");

        Assert.Null(First(run, $$"""{ "prompts": { "node.plan": { "text": {{CanonicalJson.Write(JsonValue.Create(prompt.Text))}} } } }"""));
        Assert.Null(First(run, $$"""{ "prompts": { "node.plan": { "hash": "{{prompt.Hash}}" } } }"""));
    }

    [Fact]
    public async Task Prompts_Mismatch_ReportsHashesBothWays_AndNoPromptText()
    {
        var run = await Nested();
        var prompt = run.Result.Prompts.First(p => p.Key == "node.plan");
        var wrong = "Plan the note for Margaret Okafor, who is fictional.";

        var byText = First(run, $$"""{ "prompts": { "node.plan": { "text": "{{wrong}}" } } }""");

        Assert.Equal(new TestMismatch("prompts", "node.plan", CanonicalJson.HashText(wrong), prompt.Hash), byText);
        var other = "sha256:" + new string('0', 64);
        Assert.Equal(new TestMismatch("prompts", "node.plan", other, prompt.Hash), First(run, $$"""{ "prompts": { "node.plan": { "hash": "{{other}}" } } }"""));
        Assert.Equal(new TestMismatch("prompts", "node.nothing", other, "not run"), First(run, $$"""{ "prompts": { "node.nothing": { "hash": "{{other}}" } } }"""));
        AssertNoTextIn(byText!, wrong, prompt.Text);
    }

    // ---- checks -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Checks_PassIsNamedAsTheCheckResultsPass()
    {
        var run = await RunAsync(ResolveFixture.CheckedNote());
        var verdict = (bool)run.Result.Rows.Single(r => r.Key == "check.proofread").Detail["pass"]!;
        var said = verdict ? "true" : "false";
        var other = verdict ? "false" : "true";

        Assert.Null(First(run, $$"""{ "checks": { "check.proofread": { "pass": {{said}} } } }"""));
        Assert.Equal(
            new TestMismatch("checks", "check.proofread", other, said),
            First(run, $$"""{ "checks": { "check.proofread": { "pass": {{other}} } } }"""));
        Assert.Equal(
            new TestMismatch("checks", "check.nothing", "true", "not run"),
            First(run, """{ "checks": { "check.nothing": { "pass": true } } }"""));
    }

    [Fact]
    public async Task Checks_OfACheckLoop_AreCheckedByInstance()
    {
        var run = await RunAsync(ResolveFixture.CheckedNote(), Stubs());
        var first = run.Result.Rows.Single(r => r.Key == "check.part_coverage.coverage:1");
        var said = (bool)first.Detail["pass"]! ? "true" : "false";

        Assert.Null(First(run, $$"""{ "checks": { "check.part_coverage.coverage:1": { "pass": {{said}} } } }"""));
    }

    // ---- deliverables -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Deliverables_AreCheckedByStateAndTextOrHash()
    {
        var run = await Nested();
        var note = run.Result.Deliverables.Single();

        Assert.Equal(DeliverableState.Released, note.State);
        Assert.Null(First(run, $$"""{ "deliverables": { "deliverable.note": { "state": "released", "text": {{CanonicalJson.Write(JsonValue.Create(note.Text))}} } } }"""));
        Assert.Null(First(run, $$"""{ "deliverables": { "deliverable.note": { "state": "released", "hash": "{{note.ContentHash}}" } } }"""));
    }

    [Fact]
    public async Task Deliverables_ATextExpectedOfTheWrongState_IsAStateMismatch_BeforeAnyText()
    {
        var run = await Nested();
        var wrongHash = "sha256:" + new string('0', 64);

        Assert.Equal(
            new TestMismatch("deliverables", "deliverable.note", "held", "released"),
            First(run, $$"""{ "deliverables": { "deliverable.note": { "state": "held", "hash": "{{wrongHash}}" } } }"""));
    }

    [Fact]
    public async Task Deliverables_TextMismatch_GivesHashesOnly_NeverTheText()
    {
        var run = await Nested();
        var note = run.Result.Deliverables.Single();
        var wrong = "Margaret Okafor was seen for fatigue (fictional).";

        var byText = First(run, $$"""{ "deliverables": { "deliverable.note": { "state": "released", "text": "{{wrong}}" } } }""");

        Assert.Equal(new TestMismatch("deliverables", "deliverable.note", CanonicalJson.HashText(wrong), note.ContentHash!), byText);
        AssertNoTextIn(byText!, wrong, note.Text!);
    }

    [Fact]
    public async Task Deliverables_ASkippedOne_IsExpectedByItsStateAlone()
    {
        var files = ResolveFixture.CheckedNote()
            .WithJson("deliverables/patient/patient_letter.md", h =>
                h["if"] = JsonNode.Parse("""{ "blank": [{ "var": "input.consult_draft.content" }] }"""));
        var run = await RunAsync(files);

        Assert.Equal(DeliverableState.Skipped, run.Result.Deliverables.Single(d => d.Key == "deliverable.patient.patient_letter").State);
        Assert.Null(First(run, """{ "deliverables": { "deliverable.patient.patient_letter": { "state": "skipped" } } }"""));
        Assert.Equal(
            new TestMismatch("deliverables", "deliverable.patient.patient_letter", "released", "skipped"),
            First(run, """{ "deliverables": { "deliverable.patient.patient_letter": { "state": "released" } } }"""));
    }

    [Fact]
    public async Task Deliverables_ABlockedOne_IsExpectedAsBlocked()
    {
        var run = await RunAsync(ResolveFixture.CheckedNote());

        Assert.Equal(DeliverableState.Blocked, run.Result.Deliverables.Single(d => d.Key == "deliverable.note").State);
        Assert.Null(First(run, """{ "deliverables": { "deliverable.note": { "state": "blocked" } } }"""));
        Assert.Equal(
            new TestMismatch("deliverables", "deliverable.note", "released", "blocked"),
            First(run, """{ "deliverables": { "deliverable.note": { "state": "released" } } }"""));
    }

    [Fact]
    public async Task Deliverables_AFailedRun_PassesOnlyWhenExpectSaysItFailedThroughThatDeliverable()
    {
        var files = ResolveFixture.NestedNote()
            .With("deliverables/note.md", Md(Deliverable("Nested consultation note", "\"markdown_block\"", "null", "fail"), "{{ \"\" }}"));
        var run = await RunAsync(files, Stubs());

        Assert.Equal(RunState.Failed, run.Result.State);
        Assert.Equal(DeliverableState.Failed, run.Result.Deliverables.Single().State);
        Assert.Null(First(run, """{ "deliverables": { "deliverable.note": { "state": "failed" } } }"""));
        Assert.Equal(new TestMismatch("run", "", "completed", "failed"), First(run, "{}"));
        Assert.Equal(new TestMismatch("run", "", "completed", "failed"), First(run, """{ "ran": ["node.plan"] }"""));
    }

    [Fact]
    public async Task Deliverables_ExpectedFailedButReleased_IsADeliverableMismatch()
    {
        var run = await Nested();

        var mismatch = First(run, """{ "deliverables": { "deliverable.note": { "state": "failed" } } }""");

        Assert.Equal(new TestMismatch("deliverables", "deliverable.note", "failed", "released"), mismatch);
    }

    // ---- the run itself -----------------------------------------------------------------------------------------

    [Fact]
    public async Task ARunThatFailed_IsTheFirstMismatch_BeforeAnyExpectation()
    {
        var run = await RunAsync(ResolveFixture.NestedNote(), new Dictionary<string, JsonNode?>(StringComparer.Ordinal), strict: true);

        Assert.Equal(RunState.Failed, run.Result.State);
        Assert.Equal(
            new TestMismatch("run", "", "completed", "failed"),
            First(run, """{ "values": { "node.section_count": 9 }, "ran": ["node.nothing"] }"""));
    }

    [Fact]
    public async Task AStartThatRefused_IsTheRunMismatch()
    {
        var plan = RunFixture.Plan(ResolveFixture.NestedNote());
        var request = SnapshotTest.Fictional(plan) with { Inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) };
        var result = await OfflineRun.RunAsync(plan, request);

        Assert.NotEmpty(result.StartRefusals);
        Assert.Equal(new TestMismatch("run", "", "completed", "refused"), TestExpectations.First(plan, new JsonObject(), result));
    }

    // ---- order (D7) ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TwoFailingExpectations_ReportTheEarlierMember_ThenTheLowerKey()
    {
        var run = await Nested();
        var wrongHash = "sha256:" + new string('1', 64);

        // every member fails; members are written in the reverse of the table's order
        var all = $$"""
            { "deliverables": { "deliverable.note": { "state": "held" } },
              "checks": { "check.nothing": { "pass": true } },
              "prompts": { "node.plan": { "hash": "{{wrongHash}}" } },
              "values": { "node.section_count": 7 },
              "loops": { "node.sections": { "count": 9 } },
              "decisions": { "node.refine/if:pass.1": false },
              "skipped": ["node.plan"],
              "ran": ["node.nothing"] }
            """;
        var expect = Expect(all);
        var order = new List<string>();
        while (true)
        {
            var mismatch = TestExpectations.First(run.Plan, expect, run.Result);
            if (mismatch == null)
                break;
            order.Add(mismatch.Expectation);
            expect.Remove(mismatch.Expectation);
        }

        Assert.Equal(["ran", "skipped", "decisions", "loops", "values", "prompts", "checks", "deliverables"], order);
        Assert.Equal(
            "node.a",
            First(run, """{ "ran": ["node.b", "node.a", "node.plan"] }""")!.Key);
        Assert.Equal(
            "node.refine/if:pass.1",
            First(run, """{ "decisions": { "node.refine/if:pass.2": true, "node.refine/if:pass.1": false } }""")!.Key);
    }

    // ---- hostile shapes (Review Focus 3) -------------------------------------------------------------------------

    [Theory]
    [InlineData("""{ "ran": "node.plan" }""")]
    [InlineData("""{ "ran": [1, null, {}] }""")]
    [InlineData("""{ "skipped": 5 }""")]
    [InlineData("""{ "decisions": [true] }""")]
    [InlineData("""{ "decisions": { "node.refine/if:pass.1": 5, "x": null } }""")]
    [InlineData("""{ "loops": { "node.sections": 3, "node.refine": { "count": "many" }, "node.plan": { "passes": "x", "stopped": 1 } } }""")]
    [InlineData("""{ "values": [1, 2] }""")]
    [InlineData("""{ "values": "x" }""")]
    [InlineData("""{ "prompts": { "node.plan": 1, "node.x": {} , "node.y": { "text": 5 } } }""")]
    [InlineData("""{ "checks": { "check.x": { "pass": "yes" }, "check.y": null } }""")]
    [InlineData("""{ "deliverables": { "deliverable.note": "released", "deliverable.x": { "state": 3 }, "deliverable.y": { "hash": 4 } } }""")]
    [InlineData("""{ "unknown": 1 }""")]
    [InlineData("""{ "ran": null, "skipped": null, "decisions": null, "loops": null, "values": null, "prompts": null, "checks": null, "deliverables": null }""")]
    public async Task AMemberOfAnotherShape_IsNotChecked_AndNeverThrows(string expect)
    {
        var run = await Nested();

        Assert.Null(First(run, expect));
    }

    // ---- the outcome hash (D8) ----------------------------------------------------------------------------------

    [Fact]
    public async Task OutcomeHash_IsTheSame_ForTwoRunsOfOneTest()
    {
        var first = await Nested();
        var second = await Nested();

        Assert.Matches("^sha256:[0-9a-f]{64}$", TestOutcome.Hash(first.Result));
        Assert.Equal(TestOutcome.Hash(first.Result), TestOutcome.Hash(second.Result));
    }

    [Fact]
    public async Task OutcomeHash_Differs_WhenAStubChangesAPrompt()
    {
        var first = await Nested();
        var second = await RunAsync(ResolveFixture.NestedNote(),
            Stubs("""{"sections":[{"title":"Examination","kind":"narrative"},{"title":"Plan","kind":"table"}]}"""));

        Assert.NotEqual(
            first.Result.Prompts.Single(p => p.Key == "node.sections.outline:1").Hash,
            second.Result.Prompts.Single(p => p.Key == "node.sections.outline:1").Hash);
        Assert.NotEqual(TestOutcome.Hash(first.Result), TestOutcome.Hash(second.Result));
    }

    [Fact]
    public async Task OutcomeHash_Differs_ForARunThatFailed_AndHoldsNoPromptText()
    {
        var completed = await Nested();
        var failed = await RunAsync(ResolveFixture.NestedNote(), new Dictionary<string, JsonNode?>(StringComparer.Ordinal), strict: true);

        Assert.NotEqual(TestOutcome.Hash(completed.Result), TestOutcome.Hash(failed.Result));
    }

    [Fact]
    public async Task OutcomeHash_DoesNotDependOnTheOrderTheRowsWereRecordedIn()
    {
        var run = await Nested();
        var shuffled = run.Result with { Rows = [.. run.Result.Rows.Reverse()], Decisions = [.. run.Result.Decisions.Reverse()] };

        Assert.Equal(TestOutcome.Hash(run.Result), TestOutcome.Hash(shuffled));
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static void AssertNoTextIn(TestMismatch mismatch, params string[] texts)
    {
        foreach (var text in texts)
        {
            Assert.DoesNotContain(text, mismatch.Expected, StringComparison.Ordinal);
            Assert.DoesNotContain(text, mismatch.Actual, StringComparison.Ordinal);
            Assert.DoesNotContain(text, mismatch.Key, StringComparison.Ordinal);
        }

        Assert.Matches("^sha256:[0-9a-f]{64}$", mismatch.Expected);
        Assert.Matches("^sha256:[0-9a-f]{64}$", mismatch.Actual);
    }
}
