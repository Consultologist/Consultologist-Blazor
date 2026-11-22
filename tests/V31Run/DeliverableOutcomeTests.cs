using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 §§ 6.6, 6.7, 7; R1 to R3, D3 to D6): <see cref="PlanRunner"/> decides each
/// deliverable's outcome, in part 06 § 7's order. Skipped by its <c>if</c> (it reads <c>null</c>, R1); failed when its
/// render failed or it came out empty under <c>whenEmpty: fail</c> (the job fails, A08-005); held when empty under
/// <c>hold</c>; then, once its gate settles, blocked (the gate did not pass, or could not run: D4) or released (it passed,
/// was skipped by its own <c>if</c>, or there is none). A blocked deliverable is withheld (R2): every reader but its gate
/// set reads <c>null</c>. Its one row is recorded when the outcome is final. Content is fictional (Margaret Okafor,
/// Dr A. Sample).
/// </summary>
public class DeliverableOutcomeTests
{
    private const string Plain = "\"plaintext\"";

    private const string Composed = "Margaret Okafor's plan: rest, fluids and a repeat TSH in six weeks.";

    // ---- ungated, skipped, empty --------------------------------------------------------------

    [Fact]
    public async Task AnUngatedDeliverable_IsReleased_WhenComposed()
    {
        var plan = RunFixture.Plan(Minimal());
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true).Render("deliverable.note", Rendered(Composed)));

        Assert.Equal(RunState.Completed, result.State);
        var row = recorder.Row("deliverable.note");
        Assert.Equal(RowState.Released, row.State);
        Assert.Null(row.Reason);
        Assert.Equal(CanonicalJson.HashText(Composed), row.ValueHash);
        Assert.Null(row.Detail["check"]);
        Assert.Equal(CanonicalJson.HashText(Composed), (string?)row.Detail["composedHash"]);

        var read = result.Values["deliverable.note"]!;
        Assert.Equal("released", (string?)read["state"]);
        Assert.Equal(Composed, (string?)read["content"]);
        Assert.Equal(Composed, (string?)read["value"]);

        var outcome = Assert.Single(result.Deliverables);
        Assert.Equal(new DeliverableOutcome("deliverable.note", "Consultation note", "plaintext", "", DeliverableState.Released, null, null,
            Composed, CanonicalJson.HashText(Composed), outcome.Delivery, null), outcome);
        Assert.Empty(outcome.Delivery);
    }

    [Fact]
    public async Task ASkippedDeliverable_ReadsNull_AndTheJobGoesOn()
    {
        // R1: the check seen, outside the note's gate set (it has none), reads the skipped note's .content as null and its
        // .state as skipped; reading it does not fail the job.
        var files = Billing(Minimal(), required: false)
            .WithJson("deliverables/note.md", h => h["if"] = JsonNode.Parse("{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }"))
            .With("checks/index.json", Index(["seen"]))
            .With("checks/seen.md", Check("The note was skipped", Result(
                "{ \"and\": [{ \"===\": [{ \"var\": \"deliverable.note.content\" }, null] }, "
                + "{ \"===\": [{ \"var\": \"deliverable.note.state\" }, \"skipped\"] }] }"), modelDerived: true));
        var plan = RunFixture.Plan(files);
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true), PlanRunnerLeavesTests.Inputs(includeBilling: false));

        Assert.Equal(RunState.Completed, result.State);
        var row = recorder.Row("deliverable.note");
        Assert.Equal(RowState.Skipped, row.State);
        Assert.Equal("skipped: input.include_billing.value = true is false", row.Reason);
        Assert.Null(row.ValueHash);
        Assert.True((bool)result.Values["check.seen"]!["value"]!["pass"]!);

        var outcome = Assert.Single(result.Deliverables);
        Assert.Equal(DeliverableState.Skipped, outcome.State);
        Assert.Equal(row.Reason, outcome.Reason);
        Assert.Null(outcome.Text);
        Assert.Null(outcome.ContentHash);
    }

    public static TheoryData<string, string, string, DeliverableState> Empties() => new()
    {
        { "plaintext", "hold", " \n\n", DeliverableState.Held },
        { "plaintext", "release", " \n\n", DeliverableState.Released },
        { "plaintext", "fail", " \n\n", DeliverableState.Failed },
        { "markdown_block", "hold", " \n\n", DeliverableState.Held },

        // Review Focus 2: a fixed heading is content; an object deliverable is never empty
        { "markdown_block", "hold", "## Plan\n", DeliverableState.Released },
        { "section_plan", "fail", "{\"sections\":[]}", DeliverableState.Released },
    };

    [Theory]
    [MemberData(nameof(Empties))]
    public async Task AnEmptyDeliverable_Holds_Releases_OrFailsTheJob(string format, string whenEmpty, string text, DeliverableState state)
    {
        var plan = RunFixture.Plan(Note(format, whenEmpty));
        var host = new FakeRunHost(auto: true).Reply("node.plan", "{\"sections\":[]}").Render("deliverable.note", Rendered(text));

        var (result, recorder) = await RunAsync(plan, host);

        var row = recorder.Row("deliverable.note");
        var outcome = Assert.Single(result.Deliverables);
        Assert.Equal(state, outcome.State);
        Assert.Equal(text, outcome.Text);
        Assert.Equal(CanonicalJson.HashText(text), outcome.ContentHash);
        Assert.Equal(CanonicalJson.HashText(text), row.ValueHash);
        switch (state)
        {
            case DeliverableState.Held:
                Assert.Equal(RunState.Completed, result.State);
                Assert.Equal(RowState.Held, row.State);
                Assert.Equal("held: empty", row.Reason);
                V31Contract.AssertSentence(new SourceRefusal("S08-003", row.Reason!, ""));
                Assert.Equal("held", (string?)result.Values["deliverable.note"]!["state"]);
                break;
            case DeliverableState.Released:
                Assert.Equal(RunState.Completed, result.State);
                Assert.Equal(RowState.Released, row.State);
                Assert.Null(row.Reason);
                break;
            default:
                Assert.Equal(RunState.Failed, result.State);
                Assert.Equal("deliverable.note", result.FailedKey);
                Assert.Equal("Deliverable 'note' was empty, and the workflow fails an empty Consultation note.", result.Sentence);
                V31Contract.AssertSentence(new SourceRefusal("A08-005", result.Sentence!, ""));
                Assert.Equal(RowState.Failed, row.State);
                Assert.Equal(result.Sentence, row.Reason);
                Assert.Equal(result.Sentence, outcome.Reason);
                break;
        }
    }

    [Fact]
    public async Task AnEscapedBlankValue_IsEmpty_InAMarkdownDeliverable()
    {
        // #1505 (Task 15's ruling): the escape writes a line's leading space or tab as &#32; / &#9;, so the composed text of
        // white space read into a markdown_block deliverable is not white space to string.IsNullOrWhiteSpace; it is still
        // empty (MarkdownEscape.IsBlank), and held.
        var plan = RunFixture.Plan(Note("markdown_block", "hold"));
        var host = new ComposingRunHost(plan, (_, _) => "  \n\t");
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var outcome = Assert.Single(result.Deliverables);
        Assert.Contains("&#", outcome.Text, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Text));
        Assert.Equal(DeliverableState.Held, outcome.State);
        Assert.Equal("held: empty", recorder.Row("deliverable.note").Reason);
    }

    [Fact]
    public async Task AnObjectDeliverable_ReadsItsValue_AsTheObject()
    {
        // Task 7 → 8: an object deliverable settles the parsed value: .value the object, .content its canonical JSON, and
        // the hash that of that text.
        var plan = RunFixture.Plan(Note("section_plan", "hold"));
        const string text = "{\"sections\":[{\"kind\":\"narrative\",\"title\":\"History\"}]}";
        var host = new FakeRunHost(auto: true).Reply("node.plan", text).Render("deliverable.note", Rendered(text));

        var (result, recorder) = await RunAsync(plan, host);

        Assert.Equal(RunState.Completed, result.State);
        var read = result.Values["deliverable.note"]!;
        Assert.IsType<JsonObject>(read["value"]);
        Assert.Equal("History", (string?)read["value"]!["sections"]![0]!["title"]);
        Assert.Equal(text, (string?)read["content"]);
        Assert.Equal(CanonicalJson.HashText(text), recorder.Row("deliverable.note").ValueHash);
        Assert.Equal("section_plan", Assert.Single(result.Deliverables).Format);
    }

    // ---- gated --------------------------------------------------------------------------------

    [Fact]
    public async Task AFailingGate_BlocksIt_AndTheJobCompletes()
    {
        var plan = RunFixture.Plan(Gated(Check("Every section covers its planned terms", Result("{ \"var\": \"input.include_billing.value\" }", "1", "2")), "fail"));
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true).Render("deliverable.note", Rendered(Composed)),
            PlanRunnerLeavesTests.Inputs(includeBilling: false));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Null(result.Sentence);
        var row = recorder.Row("deliverable.note");
        Assert.Equal(RowState.Blocked, row.State);
        Assert.Equal("blocked: check 'all_sections' failed (1 of 2 passed)", row.Reason);
        V31Contract.AssertSentence(new SourceRefusal("S08-007", row.Reason!, ""));
        Assert.Equal(CanonicalJson.HashText(Composed), row.ValueHash);
        Assert.Equal("check.all_sections", (string?)row.Detail["check"]);

        // R2: withheld, not erased: it reads null, and its outcome keeps the composed text and hash for the record
        var read = result.Values["deliverable.note"]!;
        Assert.Null(read["value"]);
        Assert.Null(read["content"]);
        Assert.Equal("blocked", (string?)read["state"]);
        var outcome = Assert.Single(result.Deliverables);
        Assert.Equal(DeliverableState.Blocked, outcome.State);
        Assert.Equal(row.Reason, outcome.Reason);
        Assert.Equal("check.all_sections", outcome.Check);
        Assert.Equal(Composed, outcome.Text);
        Assert.Equal(CanonicalJson.HashText(Composed), outcome.ContentHash);
    }

    [Fact]
    public async Task ATermsGate_SaysHowManyTermsAreMissing()
    {
        var files = Minimal().WithCheck()
            .With("deliverables/note.md", Md(Deliverable("Consultation note", Plain, "\"check.coverage\"", "hold"), "{{ node.plan.content }}\n"));
        var plan = RunFixture.Plan(files);
        var host = new FakeRunHost(auto: true)
            .Reply("node.extract_input_terms", Concepts(("Fatigue", "84229001"), ("Raised TSH", "166340003")))
            .Reply("node.extract_note_terms", Concepts())
            .Render("deliverable.note", Rendered(Composed));

        var (result, recorder) = await RunAsync(plan, host);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal("blocked: check 'coverage' failed (2 terms missing)", recorder.Row("deliverable.note").Reason);

        // one term: the singular
        host = new FakeRunHost(auto: true)
            .Reply("node.extract_input_terms", Concepts(("Fatigue", "84229001"), ("Raised TSH", "166340003")))
            .Reply("node.extract_note_terms", Concepts(("Fatigue", "84229001")))
            .Render("deliverable.note", Rendered(Composed));
        (_, recorder) = await RunAsync(plan, host);
        Assert.Equal("blocked: check 'coverage' failed (1 term missing)", recorder.Row("deliverable.note").Reason);
    }

    [Fact]
    public async Task ASkippedGate_ReleasesIt_WithItsReason()
    {
        var plan = RunFixture.Plan(Gated(Check("Every section covers its planned terms", Result("false", "1", "2"),
            condition: "{ \"var\": \"input.include_billing.value\" }")));
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true).Render("deliverable.note", Rendered(Composed)),
            PlanRunnerLeavesTests.Inputs(includeBilling: false));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(RowState.Skipped, recorder.Row("check.all_sections").State);
        var row = recorder.Row("deliverable.note");
        Assert.Equal(RowState.Released, row.State);
        Assert.Equal("released: check 'all_sections' didn't apply (its if was false)", row.Reason);
        V31Contract.AssertSentence(new SourceRefusal("S06-006", row.Reason!, ""));
        Assert.Equal("released", (string?)result.Values["deliverable.note"]!["state"]);
        Assert.Equal(Composed, (string?)result.Values["deliverable.note"]!["content"]);
    }

    [Fact]
    public async Task AGateThatCannotRun_BlocksIt()
    {
        // D4: the gate's if yields text at run time (an input that escaped validation): the gate's row is failed with its
        // sentence, the job is not, and the note is blocked with S08-008, its <error> that sentence without its full stop.
        var plan = RunFixture.Plan(Gated(Check("Every section covers its planned terms", Result("true"),
            condition: "{ \"var\": \"input.include_billing.value\" }")));
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true).Render("deliverable.note", Rendered(Composed)), Escaped());

        Assert.Equal(RunState.Completed, result.State);
        Assert.Null(result.FailedKey);
        var gate = recorder.Row("check.all_sections");
        Assert.Equal(RowState.Failed, gate.State);
        Assert.Equal("Check 'all_sections' if yields text; a condition yields a boolean.", gate.Reason);
        Assert.Equal("failed", (string?)result.Values["check.all_sections"]!["state"]);
        Assert.Null(result.Values["check.all_sections"]!["value"]);

        var row = recorder.Row("deliverable.note");
        Assert.Equal(RowState.Blocked, row.State);
        Assert.Equal("blocked: check 'all_sections' could not run (Check 'all_sections' if yields text; a condition yields a boolean)", row.Reason);
        V31Contract.AssertSentence(new SourceRefusal("S08-008", row.Reason!, ""));
    }

    [Fact]
    public async Task AnotherCheckThatCannotRun_FailsTheJob()
    {
        // D4: a check no deliverable names as its check fails the job, as any node does.
        var files = Billing(Minimal())
            .With("checks/index.json", Index(["other"]))
            .With("checks/other.md", Check("Billing reviewed", Result("true"), condition: "{ \"var\": \"input.include_billing.value\" }"));
        var plan = RunFixture.Plan(files);
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true), Escaped());

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("check.other", result.FailedKey);
        Assert.Equal("Check 'other' if yields text; a condition yields a boolean.", result.Sentence);
        Assert.Equal(RowState.Failed, recorder.Row("check.other").State);
    }

    [Fact]
    public async Task ABlockedDeliverable_ReadsNullToTheRestOfTheJob()
    {
        // R2, Review Focus 1: the letter's gate reads the note, which its own gate blocks; the letter's gate is outside the
        // note's gate set, so it waits for the note's outcome and reads .content null and .state blocked; it still returns
        // a check_result, which passes only on those, and the letter is released on it.
        var files = Gated(Check("Every section covers its planned terms", Result("false", "1", "2")))
            .With("checks/index.json", Index(["all_sections", "after_note"]))
            .With("checks/after_note.md", Check("The note was withheld", Result(
                "{ \"and\": [{ \"===\": [{ \"var\": \"deliverable.note.content\" }, null] }, "
                + "{ \"===\": [{ \"var\": \"deliverable.note.state\" }, \"blocked\"] }] }"), modelDerived: true))
            .With("deliverables/index.json", Index(["note", "letter"], extra: ", \"delivery\": [], \"bundle\": null"))
            .With("deliverables/letter.md", Md(Deliverable("Letter to the GP", Plain, "\"check.after_note\"", "hold"),
                "Dear Dr A. Sample,\n\n{{ node.plan.content }}\n"));
        var plan = RunFixture.Plan(files);
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true), PlanRunnerLeavesTests.Inputs(includeBilling: true));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(RowState.Blocked, recorder.Row("deliverable.note").State);
        Assert.True((bool)result.Values["check.after_note"]!["value"]!["pass"]!);
        Assert.Equal(RowState.Released, recorder.Row("deliverable.letter").State);
        Assert.Equal(["deliverable.note", "deliverable.letter"], result.Deliverables.Select(d => d.Key));

        // the letter's gate is recorded after the note's outcome, which it read
        var keys = recorder.Rows.Select(r => r.Key).ToList();
        Assert.True(keys.IndexOf("deliverable.note") < keys.IndexOf("check.after_note"));
    }

    [Fact]
    public async Task EveryDeliverableBlocked_IsACompletedJob()
    {
        // R3
        var plan = RunFixture.Plan(Gated(Check("Every section covers its planned terms", Result("false", "0", "2"))));
        var (result, _) = await RunAsync(plan, new FakeRunHost(auto: true));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Null(result.Sentence);
        Assert.All(result.Deliverables, d => Assert.Equal(DeliverableState.Blocked, d.State));
        Assert.NotEmpty(result.Deliverables);
    }

    [Fact]
    public async Task ARow_IsRecordedOnce_WhenTheOutcomeIsFinal()
    {
        // The gate proofread reads the note composed (D3: it is the note's gate set) and passes on its .content; the note's
        // one row is recorded once that outcome is final, after the gate's row.
        var files = Gated(Check("Proofreading the note", Result(ResolveFixture.NotBlank("deliverable.note.content")), modelDerived: true));
        var plan = RunFixture.Plan(files);
        Assert.Contains("deliverable.note", plan.Entries["check.all_sections"].ReadsComposed);
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true).Render("deliverable.note", Rendered(Composed)));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Single(recorder.Rows, r => r.Key == "deliverable.note");
        Assert.Equal(["node.plan", "check.all_sections", "deliverable.note"], recorder.Rows.Select(r => r.Key));
        Assert.True((bool)result.Values["check.all_sections"]!["value"]!["pass"]!);
        Assert.Equal(RowState.Released, recorder.Row("deliverable.note").State);
        Assert.Equal("released", (string?)result.Values["deliverable.note"]!["state"]);
    }

    // ---- fixtures -----------------------------------------------------------------------------

    private static async Task<(RunResult Result, ListRecorder Recorder)> RunAsync(RunPlan plan, FakeRunHost host, RunInputs? inputs = null)
    {
        var recorder = new ListRecorder();
        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(inputs ?? PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        return (result, recorder);
    }

    private static RenderOutcome Rendered(string text) => new(text, false, null);

    /// <summary><see cref="SourceFixture.Minimal"/> with the boolean input <c>include_billing</c> (required unless not <paramref name="required"/>).</summary>
    private static Dictionary<string, byte[]> Billing(Dictionary<string, byte[]> files, bool required = true) =>
        files.WithJson("manifest.json", m => m["inputs"]!["include_billing"] = JsonNode.Parse(
            "{ \"label\": \"Include the billing section\", \"description\": null, \"format\": \"boolean\", "
            + $"\"required\": {(required ? "true" : "false")}, \"channel\": null }}"));

    /// <summary>The inputs with the boolean <c>include_billing</c> supplied as text (a value that escaped validation).</summary>
    private static RunInputs Escaped()
    {
        var inputs = PlanRunnerLeavesTests.Inputs();
        return inputs with
        {
            Inputs = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal)
            {
                ["include_billing"] = new JsonObject { ["value"] = "yes", ["content"] = null, ["title"] = null },
            },
        };
    }

    /// <summary>A check file: a <c>computed</c> check of <paramref name="expression"/>, with the <c>if</c> <paramref name="condition"/>.</summary>
    private static string Check(string label, string expression, string condition = "null", bool modelDerived = false) =>
        Md(SourceFixture.Node("computed", label, "\"check_result\"", true, modelDerived, $"\"if\": {condition}, \"expression\": {expression}"), "");

    /// <summary>A <c>check_result</c> built by <c>object</c>: <paramref name="pass"/> (JSON Logic) and the counts given.</summary>
    private static string Result(string pass, string passed = "null", string total = "null") =>
        $"{{ \"object\": {{ \"pass\": {pass}, \"message\": null, \"uncovered\": null, \"untested\": null, \"passed\": {passed}, \"total\": {total} }} }}";

    /// <summary>Minimal (with <c>include_billing</c>) whose note is gated by the check <c>all_sections</c> of <paramref name="check"/>.</summary>
    private static Dictionary<string, byte[]> Gated(string check, string whenEmpty = "hold") =>
        Billing(Minimal())
            .With("checks/index.json", Index(["all_sections"]))
            .With("checks/all_sections.md", check)
            .With("deliverables/note.md", Md(Deliverable("Consultation note", Plain, "\"check.all_sections\"", whenEmpty), "{{ node.plan.content }}\n"));

    /// <summary>
    /// Minimal whose ungated note is of <paramref name="format"/> under <paramref name="whenEmpty"/>: a text format reads the
    /// plan's text; an object format (<c>section_plan</c>) reads its value, the plan returning one.
    /// </summary>
    private static Dictionary<string, byte[]> Note(string format, string whenEmpty)
    {
        if (format is "plaintext" or "markdown_block")
        {
            return Minimal().With("deliverables/note.md",
                Md(Deliverable("Consultation note", $"\"{format}\"", "null", whenEmpty), "{{ node.plan.content }}\n"));
        }

        return ResolveFixture.WithFormats(Minimal(), format)
            .With("nodes/plan.md", Agent("Planning the sections", $"\"{format}\"", "Plan the sections.\n\n{{ input.consult_draft.content }}\n"))
            .With("deliverables/note.md", Md(Deliverable("Consultation note", $"\"{format}\"", "null", whenEmpty), "{{ node.plan.value }}\n"));
    }

    /// <summary>A <c>concept_list</c> reply of codable concepts.</summary>
    private static string Concepts(params (string Term, string Id)[] concepts) =>
        new JsonObject
        {
            ["concepts"] = new JsonArray(concepts.Select(c => (JsonNode?)new JsonObject
            {
                ["term"] = c.Term, ["type"] = "finding", ["id"] = c.Id, ["isSnomedConcept"] = true, ["isActive"] = true,
                ["source"] = "input", ["support"] = null,
            }).ToArray()),
        }.ToJsonString();
}
