using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Publish;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using Equivalence = Consultologist.PackageFormat.V31.Harness.ByteEquivalence;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 35; comment 6 item 3 and comment 7, "Edge-case packages", ruled by the owner): six small
/// 30 packages under <c>fixture-packages/edge-*</c>, one per case the review of area 10 found, each with a scenario captured by
/// area 01's capture code, migrated (<c>migrated/edge-*</c>, with <c>hand-edits/edge-*</c>) and compared (<c>reports/edge-*</c>).
/// Each shows two things: the migrated package gives 30's output, published with a test of the capture's replies pinned
/// (<c>edge-tests/&lt;package&gt;.json</c>, added as <c>tests/edge.json</c>) and run offline; and the same 31 package written
/// without the migration's explicit thing is told at publish (a warning or a refusal). Content is fictional (Margaret Okafor,
/// Dr A. Sample).
/// </summary>
public class EdgeCasePackagesTests
{
    /// <summary>Each edge package and the scenario its pinned test replays: the scenario that is the edge case.</summary>
    internal static readonly IReadOnlyDictionary<string, string> EdgeScenario = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["edge-answer-outside-set"] = "answer-outside-set",
        ["edge-flag"] = "reply-does-not-fit",
        ["edge-items-not-run"] = "third-problem-not-run",
        ["edge-missing-marker"] = "reply-leaves-macro-out",
        ["edge-no-branch"] = "plan-left-out",
        ["edge-unruled-whitespace"] = "default",
        ["edge-unset-object"] = "intake-unset",
    };

    /// <summary>The five whose migrated package publishes with its pinned test: the other two pin a reply 31 refuses (D6).</summary>
    private static readonly string[] Publishing = ["edge-items-not-run", "edge-missing-marker", "edge-no-branch", "edge-unruled-whitespace", "edge-unset-object"];

    /// <summary>The folder of the pinned tests, beside <c>scenarios/</c> and <c>captures/</c>.</summary>
    private const string TestsFolder = "edge-tests";

    /// <summary>The pinned test's path in the package.</summary>
    private const string TestFile = "tests/edge.json";

    public static TheoryData<string> Packages() => new(EdgeScenario.Keys);

    public static TheoryData<string> PublishingPackages() => new(Publishing);

    [Fact]
    public void TheEdgePackages_AreFixturePackages_EachWithItsCaptureAndPinnedTest()
    {
        Assert.Equal(EdgeScenario.Keys, ByteEquivalenceFixtures.FixturePackages.Where(p => p.StartsWith("edge-", StringComparison.Ordinal)));
        Assert.All(EdgeScenario, edge =>
        {
            Assert.Contains(edge.Value, EquivalenceFixture.Scenarios(edge.Key));
            Assert.True(File.Exists(Path.Combine(ByteEquivalenceFixtures.Root, "captures", edge.Key, edge.Value + ".json")), edge.Key);
            Assert.True(File.Exists(TestPath(edge.Key)), edge.Key);
        });
        Assert.Equal(EdgeScenario.Keys.Select(p => p + ".json"),
            Directory.EnumerateFiles(Path.Combine(ByteEquivalenceFixtures.Root, TestsFolder)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void ThePinnedTest_HoldsTheCapturesReplies_UnderTheir31Keys(string package)
    {
        // Each stub is what the comparison replays (ByteEquivalence.RequestFor): the capture's reply under its 31 key, a choice
        // classifier answered from the capture (R14). edge-answer-outside-set's job stopped at triage, so its problem stubs are
        // those of its other scenario's capture, the same inputs.
        var test = Test(package);
        var replayed = Replayed(package, EdgeScenario[package]);
        if (package == "edge-answer-outside-set")
        {
            var inSet = Replayed(package, "answer-in-set");
            replayed = new Dictionary<string, JsonNode?>(inSet.Where(o => o.Key != "node.triage"), StringComparer.Ordinal) { ["node.triage"] = replayed["node.triage"] };
        }

        // edge-unruled-whitespace's problem_close was placed after each reply by 30's engine: the comparison writes its marker
        // into the captured reply (OfflineStubs.WithEngineMarkers); a test's reply is used as written, so it holds the marker.
        if (package == "edge-unruled-whitespace")
        {
            foreach (var key in replayed.Keys.Where(k => k.StartsWith("node.problem_loop.problem_draft:", StringComparison.Ordinal)).ToList())
                replayed[key] = (string)replayed[key]! + "\n{{~ macro.problem_close.content }}";
        }

        Assert.Equal(replayed.Keys.Order(StringComparer.Ordinal), test["outputs"]!.AsObject().Select(o => o.Key));
        Assert.All(replayed, o => Assert.True(JsonNode.DeepEquals(o.Value, test["outputs"]![o.Key]), o.Key));
        var inputs = EquivalenceFixture.Scenario(package, EdgeScenario[package])["inputs"]!.AsObject();
        Assert.True(JsonNode.DeepEquals(inputs, test["inputs"]), package);
        Assert.Equal((0, 0), (test["profile"]!.AsObject().Count, test["run"]!.AsObject().Count));
    }

    [Theory]
    [MemberData(nameof(PublishingPackages))]
    public async Task TheMigratedPackage_PublishesWithItsPinnedTest_AndGives30sOutput(string package)
    {
        // Steps 0 to 3 at the engine's limits: the check, the compiler, and the package's tests run offline on their stubs,
        // none generated. Each pinned test's expect holds 31's deliverable text: 30's, read from the capture, but where the
        // comparison found the texts unequal (F-D17: credited to a rule or not), where it is the comparison run's and differs
        // from 30's.
        var outcome = await PackagePublish.CheckAsync(Upload(Edged(package)), Options());

        Assert.True(outcome.Report.Refusals.Count == 0, Said(outcome.Report.Refusals));
        Assert.True(outcome.Report.Published);
        Assert.All(outcome.Report.Tests, t => Assert.True(t.Pass, t.Id));
        Assert.Contains(outcome.Report.Tests, t => t.Id == "edge");
        Assert.DoesNotContain(outcome.Report.Warnings, w => w.Id is "A11-010" or "A11-011" or "S07-014");

        var (capture, run, texts) = await EquivalenceFixture.Compared(package, EdgeScenario[package]);
        var expected = Test(package)["expect"]!["deliverables"]!.AsObject();
        foreach (var row in capture["deliverables"]!.AsArray().OfType<JsonObject>())
        {
            var key = "deliverable." + (string)row["id"]!;
            var wanted = expected[key]!;
            if ((string?)row["state"] != "composed")
                Assert.Equal(("skipped", "skipped"), ((string)row["state"]!, (string)wanted["state"]!));
            else if (!texts.Single(t => t.Site == $"{package}/{EdgeScenario[package]}#deliverables/{key}").Equal)
            {
                Assert.Equal(("released", run.Deliverables.Single(d => d.Key == key).Text), ((string)wanted["state"]!, (string?)wanted["text"]));
                Assert.NotEqual((string?)row["text"], (string?)wanted["text"]);
            }
            else
                Assert.Equal(("released", (string?)row["text"]), ((string)wanted["state"]!, (string?)wanted["text"]));
        }
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public async Task TheReport_HasNoUnexplainedText(string package)
    {
        // R19: every text that differs from 30's is credited to a rule with nothing left; since #1508, 2026-11-26, items 4 and
        // 5, the owner made the last sites no ruling covered expected (loop-item-not-run, chosen-macro-final-line-ending,
        // empty-read-trailing-space), so none is partly explained or unexplained.
        var report = await EquivalenceFixture.Report(package);
        var counts = JsonNode.Parse(report.ToJson())!["counts"]!;

        Assert.All(report.Texts, t => Assert.True(t.Equal || t.Expected, t.Site));
        Assert.Equal(0, (int)counts["partlyExplained"]! + (int)counts["unexplained"]!);
    }

    [Theory]
    [InlineData("edge-items-not-run")]
    [InlineData("edge-no-branch")]
    [InlineData("edge-unset-object")]
    public async Task TheMigratedPackage_IsByteForByte30s(string package)
    {
        // The three Q3 to Q5 cases: the migration's if, the loop's .content and the object guard give 30's bytes, every text
        // equal but edge-items-not-run's template loop, Q4's difference, expected as the owner ruled (#1508, 2026-11-26,
        // item 4: loop-item-not-run), and the texts of the node that strips the problem loop's items, 31's alone (#1510,
        // loop-item-strip-node). #1543 (ruling 2, D7): edge-unset-object's intake line stands alone on its line, after
        // "Referred for: \", with the text after it on the next line, credited to block-read-own-line alone.
        Assert.All((await EquivalenceFixture.Report(package)).Texts, t => Assert.True(
            t.Equal || (t.Expected && (t.Rules.Contains("loop-item-not-run") || t.Rules.SequenceEqual(["loop-item-strip-node"]) || t.Rules.SequenceEqual(["block-read-own-line"]))), t.Site));
    }

    // ---- edge-missing-marker -------------------------------------------------------------------------------------

    [Fact]
    public async Task AMissingMarker_IsNoFault_WhereTheClassifierSaysNo_AndFailsTheNodeInANewPackage()
    {
        // R13, R14 (comment 6 items 1 and 2): the migration gates follow_up with the judgement follow_up_applies, whose
        // question is the hand edit; the capture's reply placed no follow_up, so it answers no, the macro is not offered and
        // the reply as written needs no marker. Its other scenario holds the marker: the classifier answers yes there.
        var migrated = EquivalenceFixture.Migrated("edge-missing-marker");
        Assert.Equal(("node.follow_up_applies", "follow_up", "node.letter"), ChoiceClassifiers.Of(migrated).Select(c => (c.Key, c.Macro, c.Placing)).Single());
        Assert.False((bool)Replayed("edge-missing-marker", "reply-leaves-macro-out")["node.follow_up_applies"]!["answer"]!);
        Assert.True((bool)Replayed("edge-missing-marker", "reply-holds-macro")["node.follow_up_applies"]!["answer"]!);
        var (_, holds, _) = await EquivalenceFixture.Compared("edge-missing-marker", "reply-holds-macro");
        Assert.Equal(
            (string?)EquivalenceFixture.Capture("edge-missing-marker", "reply-holds-macro")["deliverables"]![0]!["text"],
            holds.Deliverables.Single().Text);

        // The new package: the same 31 package with the macro offered as 31 offers it, no classifier. The pinned reply is used
        // exactly as written, so the marker is missing (S05-053), the letter's node fails under its onInvalid (fail), and the
        // test does not hold.
        var files = Upload(Edged("edge-missing-marker"));
        files.Remove("nodes/follow_up_applies.md");
        Edit(files, "nodes/index.json", "\"follow_up_applies\",\n    ", "");
        Edit(files, "macros/follow_up.md", "\"if\":{\"===\":[{\"var\":\"node.follow_up_applies.value.answer\"},true]}", "\"if\":null");
        EditJson(files, TestFile, test =>
        {
            test["outputs"]!.AsObject().Remove("node.follow_up_applies");
            test["expect"]!["ran"] = new JsonArray("node.letter");
            test["expect"]!.AsObject().Remove("decisions");
        });

        var outcome = await PackagePublish.CheckAsync(files, Options());
        var refused = Assert.Single(outcome.Report.Refusals);
        Assert.Equal(("S07-007", TestFile), (refused.Id, refused.Path));
        Assert.Equal(new TestMismatch("run", "", "completed", "failed"), Assert.Single(outcome.Report.Tests, t => t.Id == "edge").FirstMismatch);

        var run = await StrictRun(files);
        var letter = run.Rows.Single(r => r.Key == "node.letter");
        Assert.Equal(RowState.Failed, letter.State);
        Assert.Equal("fail", (string?)letter.Detail["invalid"]!["action"]);
        Assert.Contains("follow_up", (string?)letter.Detail["invalid"]!["reason"], StringComparison.Ordinal);
        Assert.Contains(ReplySentences.NoMarker("follow_up"), letter.Reason, StringComparison.Ordinal);
        V31Contract.AssertSentence(new SourceRefusal("S05-053", ReplySentences.NoMarker("follow_up"), ""));
    }

    // ---- edge-no-branch ------------------------------------------------------------------------------------------

    [Fact]
    public async Task AFlowThatTookNoBranch_SkipsTheDeliverable_AndANewPackageWithoutItsIfIsWarned()
    {
        // R15 (Q3): the migration writes plan an if on its source flow having run (A10-079), so it is skipped as at 30.
        var migrated = EquivalenceFixture.Migrated("edge-no-branch");
        Assert.Contains("deliverables/plan.md, if: written to skip the deliverable unless node.plan_gate ran, as 30 skipped a deliverable none of whose sources ran.", migrated.Notices);

        // The new package, written without it: published, with A11-010 on the deliverable.
        var files = Upload(Edged("edge-no-branch"));
        Edit(files, "deliverables/plan.md", "\"if\":{\"===\":[{\"var\":\"node.plan_gate.state\"},\"completed\"]}", "\"if\":null");
        var outcome = await PackagePublish.CheckAsync(files, Options());

        var warning = Assert.Single(outcome.Report.Warnings, w => w.Id == "A11-010");
        Assert.Equal(("deliverables/plan.md", ResolveSentences.MayBeReleasedEmpty("plan", "deliverables/plan.md").Sentence), (warning.Path, warning.Sentence));
        V31Contract.AssertSentence(warning);
        // Without the if, the flow that took no branch releases the plan empty: the pinned test, which expects the plan's if
        // decided and the plan skipped, fails, and so does the migration's snapshot.
        Assert.Equal(["S07-007", "S07-007"], outcome.Report.Refusals.Select(r => r.Id));
        Assert.Equal(new TestMismatch("decisions", "deliverable.plan/if:self", "false", "not run"), outcome.Report.Tests.Single(t => t.Id == "edge").FirstMismatch);
        var plan = (await StrictRun(files)).Deliverables.Single(d => d.Key == "deliverable.plan");
        Assert.Equal((DeliverableState.Released, ""), (plan.State, plan.Text));
    }

    // ---- edge-items-not-run --------------------------------------------------------------------------------------

    [Fact]
    public async Task ALoopsItemsNotRun_WriteNothing_AndANewPackageWritingFixedTextForEachIsWarned()
    {
        // R16 (Q4): the third item is not run (beyond max 2); the migrated note composes the loop's .content, which holds the
        // items that ran, as 30's composer did. A new package whose deliverable loops over .items and writes a heading for
        // each, with no guard on its state, writes one for the item that did not run too: A11-011.
        var run = await StrictRun(Upload(Edged("edge-items-not-run")));
        Assert.Equal((RowState.NotRun, "not run: beyond max 2"), run.Rows.Where(r => r.Key == "node.problem_loop:3").Select(r => (r.State, r.Reason)).Single());

        // problem_lines, a 30 template loop that names each item's problem, keeps .items and is guarded on each item's state
        // (R16), so 31 writes no line for the item not run, where 30's template wrote "- <problem>: " for it: Q4's difference,
        // expected as the owner ruled (#1508, 2026-11-26, item 4: loop-item-not-run).
        Assert.Contains("{{ for e in node.problem_loop.items }}{{ if e.state == \"completed\" }}- {{ e.item.value }}: {{ e.content }}\n{{ else }}{{ end }}{{ end }}",
            EquivalenceFixture.Migrated("edge-items-not-run").Files["nodes/problem_lines.md"], StringComparison.Ordinal);
        var (capture, compared, _) = await EquivalenceFixture.Compared("edge-items-not-run", "third-problem-not-run");
        var lines30 = (string)capture["renders"]!.AsArray().Single(r => (string?)r!["key"] == "problem_lines")!["text"]!;
        Assert.Equal(lines30.Replace("- Blood pressure of 148/92 (fictional): \n", "", StringComparison.Ordinal), compared.Renders.Single(r => r.Key == "node.problem_lines").Text);
        Assert.Contains("Blood pressure", lines30, StringComparison.Ordinal);

        var files = Upload(Edged("edge-items-not-run"));
        Edit(files, "deliverables/note.md", "{{ node.problem_loop.content | string.strip }}", "{{ for e in node.problem_loop.items }}## {{ e.label }}\n\n{{ e.content }}\n\n{{ end }}");
        var outcome = await PackagePublish.CheckAsync(files, Options());

        var warning = Assert.Single(outcome.Report.Warnings, w => w.Id == "A11-011");
        Assert.Equal(("deliverables/note.md", ResolveSentences.FixedTextForEveryItem("note", "node.problem_loop", 1, "deliverables/note.md").Sentence), (warning.Path, warning.Sentence));
        V31Contract.AssertSentence(warning);
        var note = (await StrictRun(files)).Deliverables.Single(d => d.Key == "deliverable.note");
        Assert.Contains("## Problem list 3", note.Text, StringComparison.Ordinal);

        // Guarded on state, as the migration guards a template loop that needs .items, nothing is warned.
        Edit(files, "deliverables/note.md", "## {{ e.label }}\n\n{{ e.content }}\n\n", "{{ if e.state == \"completed\" }}## {{ e.label }}\n\n{{ e.content }}\n\n{{ else }}{{ end }}");
        Assert.DoesNotContain((await PackagePublish.CheckAsync(files, Options())).Report.Warnings, w => w.Id == "A11-011");
    }

    // ---- edge-unruled-whitespace ---------------------------------------------------------------------------------

    [Fact]
    public async Task TwoWhiteSpaceDifferences_AreExpected_AndAnAfterMacrosWhiteSpace_IsR5s()
    {
        // #1508 (Task 35 fix round 1; 2026-11-26, item 5, ruled by the owner: expected, no migration change). (a) follow_up, a
        // macro the model chose, ends its body with a line ending: 30 wrote the body whole where the reply's marker stood, 31
        // drops one final line ending, so the letter has one line ending fewer (chosen-macro-final-line-ending). (b)
        // clinic_line, a deliverable's macro at 30 and a snippet at 31, ends its line with a read of the unset clinic: 30
        // trimmed the rendered macro, 31's snippet keeps "Seen at: " (empty-read-trailing-space).
        var (capture, run, texts) = await EquivalenceFixture.Compared("edge-unruled-whitespace", "default");
        string At30(string id) => (string)capture["deliverables"]!.AsArray().Single(d => (string?)d!["id"] == id)!["text"]!;
        string At31(string key) => run.Deliverables.Single(d => d.Key == key).Text!;

        Assert.Equal(At30("letter").Replace("three months.\n\n", "three months.\n", StringComparison.Ordinal), At31("deliverable.letter"));
        Assert.StartsWith("Seen at:\n\n", At30("problems"), StringComparison.Ordinal);
        // #1543 (ruling 2, D7): the read stands alone on its line at 31, after "Seen at: \".
        Assert.StartsWith("Seen at: \\\n\n\n", At31("deliverable.problems"), StringComparison.Ordinal);
        Assert.Equal(["chosen-macro-final-line-ending"], texts.Single(t => t.Site.EndsWith("#deliverables/deliverable.letter", StringComparison.Ordinal)).Rules);

        // problem_close, placed after each paragraph by 30's engine, ends its body in more white space than one line ending:
        // 30 trimmed it all, 31 keeps all but one line ending (R5). #1510 (item 1): it ends each item of the problem loop,
        // whose .content is read, so the node that strips each item takes that white space off as 30 trimmed it, and
        // macro-trailing-whitespace is no longer needed: the letter's prompt and the problems deliverable are explained
        // without it.
        var letter = texts.Single(t => t.Site.EndsWith("#prompts/node.letter", StringComparison.Ordinal));
        Assert.Equal((false, true, false), (letter.Equal, letter.Expected, letter.Unexplained));
        Assert.Equal(["before-after-macro-marker", "macro-choice-classifier"], letter.Rules);
        var problems = texts.Single(t => t.Site.EndsWith("#deliverables/deliverable.problems", StringComparison.Ordinal));
        Assert.Equal(["block-read-own-line", "before-after-macro-marker", "empty-read-trailing-space"], problems.Rules);
        Assert.True(problems.Expected);
    }

    // ---- edge-unset-object ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AnUnsetObjectsMember_IsReadInsideTheMigrationsGuard_AndANewPackageWithoutItIsRefused()
    {
        // R17 (Q5): the problem prompt and the intake line (the 30 macro, a snippet at 31) read input.intake's reason inside
        // {{ if input.intake.value }}; unguarded, the checker refuses each read (A11-009), so a render never fails at run time.
        var files = Upload(Edged("edge-unset-object"));
        const string Guarded = "{{ if input.intake.value }}{{ input.intake.value.reason }}{{ end }}";
        Assert.Contains(Guarded, Text(files, "nodes/problem_loop/problem_draft.md"), StringComparison.Ordinal);
        Assert.Contains(Guarded, Text(files, "snippets/intake_line.md"), StringComparison.Ordinal);

        Edit(files, "nodes/problem_loop/problem_draft.md", Guarded, "{{ input.intake.value.reason }}");
        Edit(files, "snippets/intake_line.md", Guarded, "{{ input.intake.value.reason }}");
        var refusals = (await PackagePublish.CheckAsync(files, Options())).Report.Refusals;

        Assert.Equal([("A11-009", "nodes/problem_loop/problem_draft.md"), ("A11-009", "snippets/intake_line.md")],
            refusals.Select(r => (r.Id, r.Path)).OrderBy(r => r.Path, StringComparer.Ordinal));
        Assert.All(refusals, V31Contract.AssertSentence);
        Assert.All(refusals, r => Assert.Contains("through input.intake.value, which can be null.", r.Sentence, StringComparison.Ordinal));
    }

    // ---- edge-answer-outside-set ---------------------------------------------------------------------------------

    [Fact]
    public async Task AnAnswerOutsideTheSet_IsRefusedAtBoth()
    {
        // R12 (comment 5 item 1): triage's format is the package format triage_answer, an enum of its 30 values. 30 refused the
        // reply soon (onInvalid: fail) and failed the job; 31 refuses it alike, and sends no problem prompt beside it: only
        // note, whose condition reads triage, needs problem_draft, so the migration gives it note's if (#1508 item 1), where
        // before the problem prompts were the dispatch order's (failed-job-dispatch-order, #9). The pinned stub is refused at publish (S07-009), naming the
        // format, the path and the keyword, never the value; a new package's enum refuses it the same way.
        var capture = EquivalenceFixture.Capture("edge-answer-outside-set", "answer-outside-set");
        Assert.Equal("Failed", (string?)capture["status"]);
        Assert.Contains("Node 'triage' reply does not conform to format 'classification' (onInvalid: fail)", (string?)capture["failure"], StringComparison.Ordinal);

        var (_, run, texts) = await EquivalenceFixture.Compared("edge-answer-outside-set", "answer-outside-set");
        Assert.Equal(RunState.Failed, run.State);
        var triage = run.Rows.Single(r => r.Key == "node.triage");
        Assert.Equal((RowState.Failed, "fail"), (triage.State, (string?)triage.Detail["invalid"]!["action"]));
        Assert.DoesNotContain(texts, t => !t.Equal && t.Site.Contains("problem_draft", StringComparison.Ordinal));
        Assert.True(texts.Single(t => t.Site.EndsWith("#status", StringComparison.Ordinal)).Equal);

        var refused = Assert.Single((await PackagePublish.CheckAsync(Upload(Edged("edge-answer-outside-set")), Options())).Report.Refusals);
        Assert.Equal(("S07-009", TestFile), (refused.Id, refused.Path));
        Assert.Equal(PackagePublishSentences.StubInvalid("edge", "node.triage", "triage_answer", "/value enum", TestFile).Sentence, refused.Sentence);
        Assert.DoesNotContain("soon", refused.Sentence, StringComparison.Ordinal);
        V31Contract.AssertSentence(refused);
        // D6: the stub is refused before any expectation is read, so the pinned test's expect states nothing 31 does not
        // produce; the node's failed state is the comparison run's, above.
        Assert.Empty(Test("edge-answer-outside-set")["expect"]!.AsObject());
    }

    // ---- edge-flag -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task AFlagNode_IsRetried_AndANewPackageWithFlagIsRefused()
    {
        // Comment 5 item 3: onInvalid: flag is retry at 31 (A10-013). 30 kept summary's reply and composed the note; 31 asks
        // once more with the validation error, then fails, which the report lists under flag-becomes-retry, text by text.
        var migrated = EquivalenceFixture.Migrated("edge-flag");
        Assert.Contains("\"onInvalid\":\"retry\"", migrated.Files["nodes/summary.md"], StringComparison.Ordinal);
        Assert.Contains(migrated.Notices, n => n.StartsWith("Node 'summary' kept a reply that did not fit its format (onInvalid: flag)", StringComparison.Ordinal));

        var (capture, run, texts) = await EquivalenceFixture.Compared("edge-flag", "reply-does-not-fit");
        Assert.Equal(("Completed", RunState.Failed), ((string?)capture["status"], run.State));
        var summary = run.Rows.Single(r => r.Key == "node.summary");
        Assert.Equal((RowState.Failed, "retry", 2), (summary.State, (string?)summary.Detail["invalid"]!["action"], (int)summary.Detail["invalid"]!["attempts"]!));
        // Each text that differs is credited to it; the retried prompt's description, which 31 sends, is sent-format's (R19).
        Assert.All(texts.Where(t => !t.Equal), t => Assert.Equal("flag-becomes-retry", t.Rules[0]));
        Assert.Equal(["flag-becomes-retry", "sent-format"], texts.Single(t => t.Site.EndsWith("#prompts/node.summary", StringComparison.Ordinal)).Rules);
        Assert.Contains(texts, t => t.Site.EndsWith("#prompts/node.summary", StringComparison.Ordinal) && !t.Equal);

        // As a test's stub the reply is refused at publish (S07-009, D6) before any expectation is read, so the pinned test's
        // expect is {}: the node's retried and failed state above is the comparison run's.
        var refused = Assert.Single((await PackagePublish.CheckAsync(Upload(Edged("edge-flag")), Options())).Report.Refusals);
        Assert.Equal(PackagePublishSentences.StubInvalid("edge", "node.summary", "summary", "/line type", TestFile).Sentence, refused.Sentence);
        Assert.Empty(Test("edge-flag")["expect"]!.AsObject());

        // The new package that writes 30's flag: refused at the reader (A11-014), as 31 has only fail and retry, before any test runs.
        var files = Upload(Edged("edge-flag"));
        Edit(files, "nodes/summary.md", "\"onInvalid\":\"retry\"", "\"onInvalid\":\"flag\"");
        var flag = Assert.Single((await PackagePublish.CheckAsync(files, Options())).Report.Refusals);
        Assert.Equal(("A11-014", "nodes/summary.md", "Node 'summary' has onInvalid flag; write retry or fail."), (flag.Id, flag.Path, flag.Sentence));
        V31Contract.AssertSentence(flag);
    }

    // ---- what the tests share ------------------------------------------------------------------------------------

    /// <summary>The engine's publish options: the published registry, the goldens' toolchain, the engine's default limits, a stopped clock.</summary>
    private static PublishOptions Options() =>
        new(V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, WorkflowPackageMigration30To31.EngineLimits, "ci", new CompiledGoldenTests.StoppedClock());

    private static string TestPath(string package) => Path.Combine(ByteEquivalenceFixtures.Root, TestsFolder, package + ".json");

    /// <summary>The pinned test of <paramref name="package"/>, <c>edge-tests/&lt;package&gt;.json</c>.</summary>
    private static JsonObject Test(string package) => JsonNode.Parse(ByteEquivalenceFixtures.ReadText(TestPath(package)))!.AsObject();

    /// <summary>The stubs the comparison replays for a scenario: the capture's replies under their 31 keys.</summary>
    private static Dictionary<string, JsonNode?> Replayed(string package, string scenario) =>
        new(Equivalence.RequestFor(EquivalenceFixture.Scenario(package, scenario), EquivalenceFixture.Capture(package, scenario),
            EquivalenceFixture.Migrated(package), EquivalenceFixture.Plan(package)).Outputs, StringComparer.Ordinal);

    /// <summary>
    /// The migrated package as compared (its hand edits made), with its pinned test as <c>tests/edge.json</c>, listed in
    /// <c>tests/index.json</c> after any snapshot test the migration wrote.
    /// </summary>
    private static Migration31Result Edged(string package)
    {
        var migrated = EquivalenceFixture.Migrated(package);
        var files = new Dictionary<string, string>(migrated.Files, StringComparer.Ordinal)
        {
            [TestFile] = ByteEquivalenceFixtures.ReadText(TestPath(package)),
        };
        var index = JsonNode.Parse(files["tests/index.json"])!;
        index["items"]!.AsArray().Add("edge");
        files["tests/index.json"] = index.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n";
        return migrated with { Files = files };
    }

    private static Dictionary<string, byte[]> Upload(Migration31Result result) => MigratedPackagesTests.Upload(result);

    private static string Text(Dictionary<string, byte[]> files, string path) => Encoding.UTF8.GetString(files[path]);

    /// <summary>Replaces <paramref name="old"/>, which must be there, by <paramref name="now"/> in the file at <paramref name="path"/>.</summary>
    private static void Edit(Dictionary<string, byte[]> files, string path, string old, string now)
    {
        var text = Text(files, path);
        Assert.Contains(old, text, StringComparison.Ordinal);
        files[path] = Encoding.UTF8.GetBytes(text.Replace(old, now, StringComparison.Ordinal));
    }

    private static void EditJson(Dictionary<string, byte[]> files, string path, Action<JsonObject> edit)
    {
        var json = JsonNode.Parse(Text(files, path))!.AsObject();
        edit(json);
        files[path] = Encoding.UTF8.GetBytes(json.ToJsonString());
    }

    /// <summary>The pinned test of <paramref name="files"/> run offline as publish runs it (no stub generated), on the files' own plan.</summary>
    private static async Task<OfflineResult> StrictRun(Dictionary<string, byte[]> files)
    {
        var (refusals, compiled) = CompiledPackage.Publish(files, V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed);
        Assert.True(compiled != null, Said(refusals));
        var (plan, _) = RunPlan.Build(compiled!.Resolve, compiled.ResolveHash, compiled.LockHash, null);
        var request = SnapshotTest.Request(JsonNode.Parse(Text(files, TestFile))!.AsObject()) with { GenerateMissing = false };
        return await OfflineRun.RunAsync(plan!, request);
    }

    private static string Said(IEnumerable<SourceRefusal> refusals) => string.Join("\n", refusals.Select(r => $"{r.Id} {r.Path}: {r.Sentence}"));
}
