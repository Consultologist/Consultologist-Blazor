using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Engine;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;
using Equivalence = Consultologist.PackageFormat.V31.Harness.ByteEquivalence;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10, Task 15; part 10 §§ 3.1, 3.2; D9 to D12, D14; Review Focus 5): each 30 capture (area 01's,
/// read, never rewritten) held against the offline run of the migrated package (<see cref="Equivalence"/>), and
/// <c>migration-report.json</c> (<see cref="MigrationReport"/>). The report of each package with no hand edit, over all its
/// scenarios, is frozen as <c>reports/&lt;package&gt;.json</c>; with <c>GOLDEN_UPDATE_V31=1</c> it is rewritten, then read
/// before it is committed. No test asserts how many texts differ. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class ByteEquivalenceTests
{
    private const string GoldenFolder = "reports";

    /// <summary>The packages Task 12 migrates with no hand edit, whose reports are this task's goldens.</summary>
    public static TheoryData<string> Packages() => MigratedPackagesTests.PackagesWithoutHandEdits();

    // ---- the compared texts ----------------------------------------------------------------------------------

    [Fact]
    public async Task EqualTexts_AreEqual()
    {
        var (capture, run, texts) = await EquivalenceFixture.Compared("example-conditional-sections", "default");

        Assert.NotEmpty(texts);
        // #1510 (item 1): the nodes that strip each loop's items render texts 31 alone has, each credited to loop-item-strip-node;
        // every text 30 also has is equal.
        var stripped = texts.Where(t => t.Site.Contains("_stripped:", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(stripped);
        Assert.All(stripped, t => Assert.Equal((false, true, "loop-item-strip-node"), (t.Equal, t.Expected, Assert.Single(t.Rules))));
        Assert.All(texts.Except(stripped), t => Assert.Equal((true, false, (string?)null), (t.Equal, t.Expected, t.Explanation)));
        foreach (var prompt in capture["prompts"]!.AsArray().OfType<JsonObject>())
        {
            var key = KeyMap.To31((string)prompt["key"]!, EquivalenceFixture.Migrated("example-conditional-sections"), EquivalenceFixture.Plan("example-conditional-sections"))!;
            var text = Assert.Single(texts, t => t.Site == "example-conditional-sections/default#prompts/" + key);
            Assert.Equal((string?)prompt["hash"], text.OldHash);
            Assert.Equal(run.Prompts.Single(p => p.Key == key).Hash, text.NewHash);
        }

        foreach (var row in capture["deliverables"]!.AsArray().OfType<JsonObject>().Where(r => (string?)r["state"] == "composed"))
        {
            var text = Assert.Single(texts, t => t.Site == $"example-conditional-sections/default#deliverables/deliverable.{row["id"]}");
            Assert.Equal((string?)row["hash"], text.OldHash);
            Assert.Equal(run.Deliverables.Single(d => d.Key == "deliverable." + row["id"]).ContentHash, text.NewHash);
        }
    }

    /// <summary>#1508 (item 7): a migrated classifier's prompt ends with its format's instruction alone; 30's answer line is credited to <c>sent-format</c>.</summary>
    [Fact]
    public async Task AClassifiersPrompt_IsExpected()
    {
        var (_, _, texts) = await EquivalenceFixture.Compared("example-classifier-scope", "in-scope");
        var scope = texts.Single(t => t.Site == "example-classifier-scope/in-scope#prompts/node.scope");
        Assert.Equal(["sent-format"], scope.Rules);
        Assert.True(scope.Expected);

        var (_, _, edge) = await EquivalenceFixture.Compared("edge-answer-outside-set", "answer-outside-set");
        var triage = edge.Single(t => t.Site.EndsWith("#prompts/node.triage", StringComparison.Ordinal));
        Assert.Equal(["sent-format"], triage.Rules);
    }

    /// <summary>
    /// #1508 (item 1; PR #1532's unexpected difference 1): 30 ran only what an applicable deliverable needed; the
    /// migration gates the rest, so 31 sends no prompt 30 did not.
    /// </summary>
    [Theory]
    [InlineData("example-classifier-scope")]
    [InlineData("example-conditional-documents")]
    [InlineData("example-structured-intake")]
    public async Task TheGatedPackages_SendOnlyThePrompts30Sent(string package)
    {
        // A scenario 30 refused before any model call (example-structured-intake's all-optional-inputs-unset, the empty-fan
        // refusal) sent no prompt at all: its 31 run is another difference (empty-input-list-runs), not a gate's.
        var compared = 0;
        foreach (var scenario in EquivalenceFixture.Scenarios(package))
        {
            var (capture, _, texts) = await EquivalenceFixture.Compared(package, scenario);
            if ((string?)capture["status"] != "Completed")
                continue;
            compared++;
            var alone = texts.Where(t => t.Site.Contains("#prompts/", StringComparison.Ordinal)
                && t.OldHash == PackageFormat.V31.Harness.ByteEquivalence.EmptyHash && t.NewHash != PackageFormat.V31.Harness.ByteEquivalence.EmptyHash).Select(t => t.Site).ToList();
            Assert.True(alone.Count == 0, "31 alone sent:\n" + string.Join("\n", alone));
        }

        Assert.True(compared > 0);
    }

    [Fact]
    public async Task AMigratedDate_RendersAs30Did()
    {
        // #1508 (item 2): the migration writes 30's "%d %b %Y", so the dated prompt and render are equal.
        var (_, _, billing) = await EquivalenceFixture.Compared("example-conditional-documents", "new-patient-billing");
        var date = billing.Single(t => t.Site.EndsWith("#prompts/node.sections_consult_note_section.consult_note_section:assessment", StringComparison.Ordinal));
        Assert.True(date.Equal);
        Assert.Equal(date.OldHash, date.NewHash);

        var (_, _, v12) = await EquivalenceFixture.Compared("example-v12-constructs", "default");
        var header = v12.Single(t => t.Site.EndsWith("#renders/node.patient_header", StringComparison.Ordinal));
        Assert.True(header.Equal);
    }

    [Fact]
    public async Task ADifferenceNoRuleExplains_IsUnexpected()
    {
        // A prompt 30 sent with a line 31 does not write, which no rule explains (the encounter date's form was one; #1508 item 2
        // made it equal).
        var package = "example-conditional-documents";
        var capture = EquivalenceFixture.Capture(package, "new-patient-no-billing");
        var site = "#prompts/node.sections_consult_note_section.consult_note_section:assessment";
        var prompt = capture["prompts"]!.AsArray().OfType<JsonObject>().Single(p =>
            KeyMap.To31((string)p["key"]!, EquivalenceFixture.Migrated(package), EquivalenceFixture.Plan(package)) == site["#prompts/".Length..]);
        var text = (string)prompt["text"]! + "\nAn extra line only 30 wrote.";
        prompt["text"] = text;
        prompt["hash"] = CanonicalJson.HashText(text);

        var migrated = EquivalenceFixture.Migrated(package);
        var plan = EquivalenceFixture.Plan(package);
        var request = Equivalence.RequestFor(EquivalenceFixture.Scenario(package, "new-patient-no-billing"), capture, migrated, plan);
        var run = await OfflineRun.RunAsync(plan, request);
        var texts = Equivalence.Compare(package, "new-patient-no-billing", capture, run, migrated, plan, request);
        var date = texts.Single(t => t.Site.EndsWith(site, StringComparison.Ordinal));

        Assert.Equal((false, false, (string?)null), (date.Equal, date.Expected, date.Explanation));
        Assert.NotEqual(date.OldHash, date.NewHash);
    }

    [Fact]
    public async Task TheStatus_IsCompared()
    {
        // D11: #status, 30's Completed, Failed or Refused beside 31's, first among the texts.
        var completed = (await EquivalenceFixture.Compared("example-nested-note", "default")).Texts[0];
        Assert.Equal(("example-nested-note/default#status", true), (completed.Site, completed.Equal));
        Assert.Equal(CanonicalJson.HashText("Completed"), completed.OldHash);

        // Refused at both: no deliverable's if holds (R2: still refused at 31), and nothing else is compared.
        var (_, refusedRun, refused) = await EquivalenceFixture.Compared("example-conditional-documents", "encounter-kind-unset");
        var status = Assert.Single(refused);
        Assert.Equal((CanonicalJson.HashText("Refused"), CanonicalJson.HashText("Refused"), true), (status.OldHash, status.NewHash, status.Equal));
        Assert.Equal(OfflineRun.NoApplicableDeliverable, Assert.Single(refusedRun.StartRefusals).Id);

        // Refused at 30, completed at 31: R2's capture, expected.
        var ran = (await EquivalenceFixture.Compared("example-prior-letters", "no-prior-letters")).Texts[0];
        Assert.Equal((CanonicalJson.HashText("Refused"), CanonicalJson.HashText("Completed"), false, true),
            (ran.OldHash, ran.NewHash, ran.Equal, ran.Expected));
        Assert.StartsWith("empty-input-list-runs: ", ran.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATextOneSideLacks_HasTheEmptyTextsHash()
    {
        // D11: a prompt only 31 sent (30 refused the job before any model call: the empty fan, empty-input-list-runs; a node
        // only an unreleased deliverable needs is gated since #1508 item 1), and the deliverable only 31 composed in R2's capture.
        var (_, _, texts) = await EquivalenceFixture.Compared("example-structured-intake", "all-optional-inputs-unset");
        var only31 = texts.Single(t => t.Site.EndsWith("#prompts/node.sections_note_section.note_section:assessment", StringComparison.Ordinal));
        Assert.Equal((Equivalence.EmptyHash, false), (only31.OldHash, only31.Equal));

        var (_, _, ran) = await EquivalenceFixture.Compared("example-prior-letters", "no-prior-letters");
        var note = ran.Single(t => t.Site == "example-prior-letters/no-prior-letters#deliverables/deliverable.note");
        Assert.Equal((Equivalence.EmptyHash, false, true), (note.OldHash, note.Equal, note.Expected));
    }

    [Fact]
    public async Task AKeyThatCannotBeMapped_IsReported()
    {
        // Review Focus 5: a prompt 30 sent under a key the migration does not know keeps its 30 key under #prompts@30/, its
        // 31 side the empty text's hash; a reply pinned under it is not served to anything.
        var package = "example-nested-note";
        var capture = EquivalenceFixture.Capture(package, "default");
        capture["prompts"]!.AsArray().Add(new JsonObject { ["key"] = "ghost:1", ["hash"] = CanonicalJson.HashText("Boo."), ["text"] = "Boo." });
        capture["outputs"]!["ghost:1"] = "Stub reply.";
        capture["renders"]!.AsArray().Add(new JsonObject { ["key"] = "draft_paragraph:9", ["hash"] = CanonicalJson.HashText("x"), ["text"] = "x" });

        var migrated = EquivalenceFixture.Migrated(package);
        var plan = EquivalenceFixture.Plan(package);
        var request = Equivalence.RequestFor(EquivalenceFixture.Scenario(package, "default"), capture, migrated, plan);
        Assert.DoesNotContain(request.Outputs.Keys, k => k.Contains("ghost", StringComparison.Ordinal));
        var run = await OfflineRun.RunAsync(plan, request);
        var texts = Equivalence.Compare(package, "default", capture, run, migrated, plan, request);

        var ghost = Assert.Single(texts, t => t.Site == "example-nested-note/default#prompts@30/ghost:1");
        Assert.Equal((CanonicalJson.HashText("Boo."), Equivalence.EmptyHash, false, false), (ghost.OldHash, ghost.NewHash, ghost.Equal, ghost.Expected));
        // A key whose node is known but whose path is not its loops' (one segment for two loops) cannot be mapped either.
        Assert.Single(texts, t => t.Site == "example-nested-note/default#renders@30/draft_paragraph:9");
    }

    [Fact]
    public async Task MacroChoices_GoThroughTheStart()
    {
        // R7, part 10 § 1.3 (Task 10's rule): a scenario's choice keyed by the macro's id sets the boolean input the macro's
        // if reads (letrozole_macro), and leaves the input of the macro's id alone; a choice the start refuses is left out.
        var plan = V31StartChecksTests.Migrated();
        var migrated = new Migration31Result("{}", [], [], []);
        var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["consult_draft"] = V31StartChecksTests.Draft };
        var scenario = Scenario(inputs, new JsonObject { ["letrozole"] = true, ["no_such_macro"] = true });

        var request = Equivalence.RequestFor(scenario, Capture(), migrated, plan);

        Assert.Equal(true, (bool?)request.Inputs["letrozole_macro"]);
        Assert.False(request.Inputs.ContainsKey("letrozole"));
        Assert.False(request.Inputs.ContainsKey("no_such_macro"));
        // #1508 (R13): the macro's classifier answers yes, so the choice alone decides.
        var yes = new Dictionary<string, JsonNode?>(request.Outputs, StringComparer.Ordinal) { ["node.letrozole_applies"] = JsonNode.Parse("""{ "answer": true, "reason": "The plan starts letrozole." }""") };
        var run = await OfflineRun.RunAsync(plan, request with { Outputs = yes });
        Assert.True(run.StartRefusals.Count == 0, string.Join("\n", run.StartRefusals.Select(r => r.Sentence)));
        var on = run.Prompts.Single(p => p.Key == "node.draft").Text;

        // Chosen off, the macro is not offered: the prompt lacks its instruction.
        var off = Equivalence.RequestFor(Scenario(inputs, new JsonObject { ["letrozole"] = false }), Capture(), migrated, plan);
        Assert.Equal(false, (bool?)off.Inputs["letrozole_macro"]);
        var without = (await OfflineRun.RunAsync(plan, off with { Outputs = yes })).Prompts.Single(p => p.Key == "node.draft").Text;
        Assert.True(on.Length > without.Length && on.StartsWith(without.TrimEnd(), StringComparison.Ordinal), on + "\n---\n" + without);
    }

    [Fact]
    public async Task ScenarioInputs_GoThrough31sStart()
    {
        // The fixture README's scopes: a supplied " " reaches 30 as "", an input not given. At 31 the start judges it (area
        // 06's R5): an optional input supplied " " is not given, so the run is the run of the input left unset, prompt for
        // prompt; a required one supplied " " refuses the start (A06-011).
        var package = "example-two-documents";
        var migrated = EquivalenceFixture.Migrated(package);
        var plan = EquivalenceFixture.Plan(package);
        var capture = EquivalenceFixture.Capture(package, "default");
        Assert.Null(EquivalenceFixture.Scenario(package, "default")["inputs"]!["prior_notes"]);

        var blank = EquivalenceFixture.Scenario(package, "default");
        blank["inputs"]!["prior_notes"] = " ";
        var request = Equivalence.RequestFor(blank, capture, migrated, plan);
        Assert.Equal(" ", (string?)request.Inputs["prior_notes"]);
        var blankRun = await OfflineRun.RunAsync(plan, request);
        var (_, unsetRun, _) = await EquivalenceFixture.Compared(package, "default");
        Assert.Equal(unsetRun.Prompts.Select(p => (p.Key, p.Hash)), blankRun.Prompts.Select(p => (p.Key, p.Hash)));

        var required = EquivalenceFixture.Scenario(package, "default");
        required["inputs"]!["consult_draft"] = " \n";
        var refused = await OfflineRun.RunAsync(plan, Equivalence.RequestFor(required, capture, migrated, plan));
        Assert.Equal("A06-011", Assert.Single(refused.StartRefusals).Id);
    }

    [Fact]
    public void TheRequest_ReplaysTheCapturesOutputs_UnderTheir31Keys()
    {
        // D12: each pinned reply under its 31 row key; § 1.8: a structured reply given every member its 31 format lists, null
        // for one the 30 format did not require; #1504 ruling 3: a concept_list's concepts carry 30's stamped source.
        var nested = EquivalenceFixture.Migrated("example-nested-note");
        var nestedPlan = EquivalenceFixture.Plan("example-nested-note");
        var capture = EquivalenceFixture.Capture("example-nested-note", "verdict-good-on-pass-2");
        var request = Equivalence.RequestFor(EquivalenceFixture.Scenario("example-nested-note", "verdict-good-on-pass-2"), capture, nested, nestedPlan);
        Assert.Equal(
            capture["outputs"]!.AsObject().Select(o => KeyMap.To31(o.Key, nested, nestedPlan)).Order(StringComparer.Ordinal),
            request.Outputs.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(CanonicalJson.Write(capture["outputs"]!["judge:2"]), CanonicalJson.Write(request.Outputs["node.refine.judge#2"]));
        Assert.Equal("draft_paragraph:2/3", request.StubSeed!("node.sections.paragraphs.draft_paragraph:2/3"));
        Assert.Equal(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), request.StartedAtUtc);

        var general = EquivalenceFixture.Migrated("general");
        var generalPlan = EquivalenceFixture.Plan("general");
        var replies = Equivalence.RequestFor(EquivalenceFixture.Scenario("general", "default"), EquivalenceFixture.Capture("general", "default"), general, generalPlan);
        var concepts = replies.Outputs["node.create_patient_trajectory"]!["concepts"]!.AsArray();
        Assert.All(concepts, c => Assert.Equal("patient-trajectory", (string?)c!["source"]));

        var fitted = Equivalence.Fit(
            JsonNode.Parse("""{ "a": "x", "list": [ {} ] }"""),
            JsonNode.Parse("""
                { "type": "object", "properties": {
                    "a": { "type": "string" }, "b": { "type": ["string", "null"] }, "c": { "type": "string" },
                    "list": { "type": "array", "items": { "type": "object", "properties": { "d": { "enum": ["p", null] } } } } } }
                """),
            "plan");
        // c is not nullable: generated from its schema, seeded by the seed and its path (a member 30's format never had).
        var c = WorkflowStubOutputs.FromSchema(JsonNode.Parse("""{ "type": "string" }"""), "plan$.c");
        Assert.Equal(
            $$"""{"a":"x","b":null,"c":{{CanonicalJson.Write(c)}},"list":[{"d":null}]}""",
            CanonicalJson.Write(fitted));
    }

    // ---- the key map -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("example-nested-note", "plan", "node.plan")]
    [InlineData("example-nested-note", "outline:3", "node.sections.outline:3")]
    [InlineData("example-nested-note", "draft_paragraph:2/3", "node.sections.paragraphs.draft_paragraph:2/3")]
    [InlineData("example-nested-note", "judge:2", "node.refine.judge#2")]
    [InlineData("example-nested-note", "deliverable.note", "deliverable.note")]
    [InlineData("example-prior-letters", "read_letter:0", "node.letters.read_letter:1")]
    [InlineData("example-prior-letters", "letter_summary:2", "node.letters.letter_summary:3")]
    [InlineData("example-classifier-scope", "plan_section:assessment", "node.plan_sections.plan_section:assessment")]
    public void AKey_MapsBothWays(string package, string key30, string key31)
    {
        var migrated = EquivalenceFixture.Migrated(package);
        var plan = EquivalenceFixture.Plan(package);

        Assert.Equal(key31, KeyMap.To31(key30, migrated, plan));
        Assert.Equal(key30, KeyMap.To30(key31, migrated, plan));
    }

    [Theory]
    [InlineData("ghost")]
    [InlineData("plan:1")]
    [InlineData("draft_paragraph:2")]
    [InlineData("judge:0")]
    [InlineData("judge:01")]
    [InlineData("deliverable.ghost")]
    public void AKeyThatIsNotThePackages_MapsToNothing(string key30)
    {
        Assert.Null(KeyMap.To31(key30, EquivalenceFixture.Migrated("example-nested-note"), EquivalenceFixture.Plan("example-nested-note")));
    }

    [Theory]
    [InlineData("node.ghost")]
    [InlineData("node.refine.judge:2")]
    [InlineData("node.refine.judge#0")]
    [InlineData("node.sections.paragraphs.draft_paragraph:2")]
    [InlineData("node.sections.paragraphs.draft_paragraph:2/3/4")]
    public void A31KeyThatIsNotThePackages_MapsToNothing(string key31)
    {
        Assert.Null(KeyMap.To30(key31, EquivalenceFixture.Migrated("example-nested-note"), EquivalenceFixture.Plan("example-nested-note")));
    }

    // ---- the report ------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Packages))]
    public async Task TheReport_IsValidAgainstItsSchema(string package)
    {
        var report = await EquivalenceFixture.Report(package);
        var schema = V31ContractsTests.SchemaOf("migration-report");

        Assert.True(schema.Validate(report.ToJson(), null), report.ToJson());
        Assert.Equal(CanonicalJson.Write(JsonNode.Parse(report.ToJson())), report.ToJson());
        Assert.All(report.Texts, t => Assert.Equal(t.Expected, t.Explanation != null));
        Assert.All(report.Texts.Where(t => t.Equal), t => Assert.False(t.Expected));
    }

    [Fact]
    public async Task TheReport_HoldsTheOptions_AndTheSchemaRequiresThem()
    {
        // R18: migration-report.json records the choices the package was migrated with, as its options.json gives them.
        var report = await EquivalenceFixture.Report("example-nested-note");
        var schema = V31ContractsTests.SchemaOf("migration-report");

        Assert.True(JsonNode.DeepEquals(ExampleChoices.Json("example-nested-note"), report.Options));
        var json = JsonNode.Parse(report.ToJson())!.AsObject();
        Assert.Equal("""{"folderFormats":{},"renames":{"parts":"sections"},"whenEmpty":{"note":"fail"}}""", json["options"]!.ToJsonString());
        Assert.True(schema.Validate(json.ToJsonString(), null), json.ToJsonString());

        var without = json.DeepClone().AsObject();
        without.Remove("options");
        Assert.False(schema.Validate(without.ToJsonString(), null), "A report without options is accepted.");
        var wrong = json.DeepClone().AsObject();
        wrong["options"]!["whenEmpty"]!["note"] = "maybe";
        Assert.False(schema.Validate(wrong.ToJsonString(), null), "A whenEmpty that is not release, hold or fail is accepted.");
        var unknown = json.DeepClone().AsObject();
        unknown["options"]!["toolchain"] = new JsonObject();
        Assert.False(schema.Validate(unknown.ToJsonString(), null), "An unknown option is accepted.");
    }

    [Fact]
    public async Task TheReport_ListsEveryNoticeThatNeedsNoEdit()
    {
        // D9: every notice but the hand edits, in the migration's order, then A10-063 for a package with more than one
        // deliverable (R8), then the worst-case note (D14).
        var report = await EquivalenceFixture.Report("example-two-documents");
        var migrated = EquivalenceFixture.Migrated("example-two-documents");

        Assert.Equal((string)JsonNode.Parse(ByteEquivalenceFixtures.ReadPackage("example-two-documents").ManifestJson)!["version"]!, report.OldVersion);
        Assert.Equal(report.OldVersion, report.NewVersion);
        Assert.Empty(report.HandEdits);
        Assert.Equal([.. migrated.Notices, MigrationReport.OneMessageEach], report.Notices.Take(migrated.Notices.Count + 1));
        V31Contract.AssertSentence(new SourceRefusal("A10-063", MigrationReport.OneMessageEach, ""));
        Assert.DoesNotContain(MigrationReport.OneMessageEach, (await EquivalenceFixture.Report("example-nested-note")).Notices);

        var handEdits = MigratedPackagesTests.Migrated("example-macros");
        var edited = MigrationReport.Of("v2026.11.3", handEdits, ExampleChoices.Json("example-macros"), []);
        Assert.Equal(handEdits.HandEdits, edited.HandEdits);
        Assert.Empty(edited.Notices.Intersect(handEdits.HandEdits));
    }

    [Fact]
    public void TheWorstCaseNote_IsSaidPastTheLimit()
    {
        // D14: example-nested-note's worst case is its plan's sections (the items limit) times each section's points (the
        // items limit) paragraphs, plus the rest: far past 30 minutes at 16 calls a minute. A10-014's words.
        var nested = EquivalenceFixture.Migrated("example-nested-note");
        var note = MigrationReport.WorstCaseNote(nested, V31Contract.PublishedTypes());
        Assert.NotNull(note);
        V31Contract.AssertSentence(new SourceRefusal("A10-014", note!, ""));
        Assert.StartsWith("example-nested-note could run for about ", note, StringComparison.Ordinal);
        Assert.EndsWith(" minutes in the worst case; this engine stops a job after 30 minutes.", note, StringComparison.Ordinal);

        // Within the limit, nothing is said: the same package with an hour a round of 16 calls would take, and a package
        // whose worst case is a few calls.
        Assert.Null(MigrationReport.WorstCaseNote(nested, V31Contract.PublishedTypes(), new RunLimits(JobMinutes: 1_000_000)));
        Assert.Null(MigrationReport.WorstCaseNote(EquivalenceFixture.Migrated("example-classifier-scope"), V31Contract.PublishedTypes()));
        Assert.Equal("p could run for about 31 minutes in the worst case; this engine stops a job after 30 minutes.", MigrationReport.WorstCase("p", 31, 30));
    }

    [Fact]
    public async Task AComparedText_NamesTheRulesCredited()
    {
        // #1508 (R19): composer-sources' tally is a let of text escaped in a Markdown deliverable (escaped-plain-text). #1510
        // (#1509 comment 7207709261, item 1): its count is 30's now that the topics it counts are stripped as 30 trimmed them,
        // so the rule explains it all.
        var (_, _, texts) = await EquivalenceFixture.Compared("composer-sources", "awkward-texts");
        var tally = texts.Single(t => t.Site == "composer-sources/awkward-texts#deliverables/deliverable.tally");

        Assert.Equal(["escaped-plain-text"], tally.Rules);
        Assert.Equal((false, true, false), (tally.Equal, tally.Expected, tally.Unexplained));
        Assert.StartsWith("escaped-plain-text: ", tally.Explanation, StringComparison.Ordinal);

        // An explained text names its rules and has nothing left; an equal text names none.
        var (_, _, letters) = await EquivalenceFixture.Compared("example-prior-letters", "default");
        var read = letters.Single(t => t.Site == "example-prior-letters/default#prompts/node.letters.read_letter:1");
        Assert.Equal(["item-ids-from-one", "sent-format"], read.Rules);
        Assert.Equal((true, false), (read.Expected, read.Unexplained));
        Assert.All(texts.Where(t => t.Equal), t => Assert.Equal((0, false), (t.Rules.Count, t.Unexplained)));
    }

    [Fact]
    public async Task EachComposedSource_IsTrimmedAs30Did()
    {
        // #1508 (item 3): 30's composer trimmed each source; the migration writes string.strip on each.
        var (_, _, macros) = await EquivalenceFixture.Compared("composer-macros", "new-visit-defaults");
        var prepended = macros.Single(t => t.Site == "composer-macros/new-visit-defaults#deliverables/deliverable.prepended");
        Assert.True(prepended.Equal || prepended.Expected && prepended.Rules.All(r => r == "escaped-plain-text"), string.Join(",", prepended.Rules));
        // Every deliverable over letter_body but the three whose macros are hand edits (gated, optional, echoes: their text is
        // the edit's to give).
        string[] edited = ["gated", "optional", "echoes"];
        Assert.All(macros.Where(t => t.Site.Contains("#deliverables/deliverable.", StringComparison.Ordinal) && !edited.Any(e => t.Site.EndsWith("deliverable." + e, StringComparison.Ordinal))),
            t => Assert.False(t.Unexplained));

        var (_, _, sources) = await EquivalenceFixture.Compared("composer-sources", "awkward-texts");
        Assert.True(sources.Single(t => t.Site == "composer-sources/awkward-texts#deliverables/deliverable.pair").Equal);

        // The escaped-plain-text credit still finds the value a stripped read holds (tally; example-v12-constructs' consult).
        Assert.Contains("escaped-plain-text", sources.Single(t => t.Site == "composer-sources/awkward-texts#deliverables/deliverable.tally").Rules);
        var (_, _, v12) = await EquivalenceFixture.Compared("example-v12-constructs", "tone-friendly");
        Assert.Contains("escaped-plain-text", v12.Single(t => t.Site == "example-v12-constructs/tone-friendly#deliverables/deliverable.consult").Rules);
    }

    /// <summary>
    /// #1510 (#1509 comment 7207709261, item 1): 30 trimmed each item of a loop it composed; the migration returns each item
    /// of composer-sources' topic loop through a node that strips it, so six of the seven sites the ruling names are 30's
    /// bytes, with no rule, and the seventh, tally, differs only as a plain text escaped in Markdown (escaped-plain-text), its
    /// count now 30's.
    /// </summary>
    [Fact]
    public async Task ALoopsItems_AreTrimmedAs30Did()
    {
        var (_, _, texts) = await EquivalenceFixture.Compared("composer-sources", "awkward-texts");
        string[] sites =
        [
            "prompts/node.opening", "prompts/node.closing", "prompts/node.plan_gate.draft_plan", "prompts/node.tone_case.plain_line",
            "deliverables/deliverable.topics", "deliverables/deliverable.topics_then_opening",
        ];
        foreach (var site in sites)
        {
            var text = texts.Single(t => t.Site == "composer-sources/awkward-texts#" + site);
            Assert.True((text.Equal, text.Rules.Count, text.Unexplained) == (true, 0, false), $"{site}: equal {text.Equal}, rules {string.Join(",", text.Rules)}");
        }

        var tally = texts.Single(t => t.Site == "composer-sources/awkward-texts#deliverables/deliverable.tally");
        Assert.Equal(["escaped-plain-text"], tally.Rules);
        Assert.Equal((false, true, false), (tally.Equal, tally.Expected, tally.Unexplained));
    }

    /// <summary>
    /// #1510 (F-D3): the stripping node's texts are 31's alone; each is credited to <c>loop-item-strip-node</c> exactly when its
    /// node is one the migration wrote and its text is the old <c>ret</c>'s text for that item, trimmed. Never otherwise.
    /// </summary>
    [Fact]
    public async Task AStrippingNodesText_IsCreditedOnlyWhenItIsTheItemTrimmed()
    {
        var (capture, run, texts) = await EquivalenceFixture.Compared("composer-sources", "awkward-texts");
        var stripped = texts.Where(t => t.Site.Contains("#renders/node.topic_loop.topic_gate_stripped", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(stripped);
        Assert.All(stripped, t => Assert.Equal((false, true, false), (t.Equal, t.Expected, t.Unexplained)));
        Assert.All(stripped, t => Assert.Equal(["loop-item-strip-node"], t.Rules));
        Assert.All(texts.Where(t => !t.Site.Contains("_stripped", StringComparison.Ordinal)), t => Assert.DoesNotContain("loop-item-strip-node", t.Rules));

        var migrated = EquivalenceFixture.Migrated("composer-sources");
        var plan = EquivalenceFixture.Plan("composer-sources");
        var render = run.Renders.First(r => r.Key.StartsWith("node.topic_loop.topic_gate_stripped:", StringComparison.Ordinal));
        DifferenceContext Context(string key, string? old, string now) =>
            new("composer-sources", "awkward-texts", "composer-sources/awkward-texts#renders/" + key, ComparedKind.Render, null, key, old, now, capture, run, migrated, plan);

        Assert.StartsWith("loop-item-strip-node: ", ExpectedDifferences.Explain(Context(render.Key, null, render.Text)));
        // Another text at the same node, a text 30 also had, and a 31-only render of a node the migration did not write.
        Assert.Null(ExpectedDifferences.Explain(Context(render.Key, null, render.Text + "\n")));
        Assert.Null(ExpectedDifferences.Explain(Context(render.Key, render.Text, render.Text + " ")));
        Assert.Null(ExpectedDifferences.Explain(Context("node.topic_loop.topic_gate" + render.Key[render.Key.IndexOf(':')..], null, render.Text)));
        Assert.Null(ExpectedDifferences.Explain(Context(render.Key, null, render.Text) with { Migrated = migrated with { StripNodes = new Dictionary<string, string>() } }));
    }

    /// <summary>
    /// #1510 (the owner's third fix-first group, item 4; G-D9): composer-macros' letterhead written as an address, one line
    /// each, which 30 broke at each line ending (its capture is the address as written). The migration ends each line with
    /// <c>\</c>, so every letter that places it differs from 30's only by those, and each is credited to
    /// <c>explicit-hard-break</c> alone; against the same run, a result that says it wrote no break leaves them unexplained.
    /// </summary>
    [Fact]
    public async Task AHardBreakTheMigrationWrote_IsExplainedByItsRule()
    {
        const string Package = "composer-macros";
        const string Scenario = "new-visit-defaults";
        const string Letterhead = "Riverside Clinic (fictional)";
        const string Address = "Riverside Clinic (fictional)\n12 Sample Street\nSampletown";
        var migrated = MigrationFixture.Overlay(
            MigrateNodesTests.Migrated(Package, _ => { }, files => files["macros/letterhead.md"] = files["macros/letterhead.md"].Replace(Letterhead, Address, StringComparison.Ordinal)),
            MigrationFixture.HandEditsOf(Package));
        Assert.Equal(2, migrated.HardBreaks["snippets/letterhead.md"]);
        var (refusals, compiled) = CompiledPackage.Publish(MigratedPackagesTests.Upload(migrated), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed);
        Assert.True(compiled != null, string.Join("\n", refusals.Select(r => r.Sentence)));
        var (plan, refusal) = RunPlan.Build(compiled!.Resolve, compiled.ResolveHash, compiled.LockHash, null);
        Assert.True(plan != null, refusal?.Sentence);

        var capture = EquivalenceFixture.Capture(Package, Scenario);
        var placed = new List<string>();
        foreach (var row in capture["deliverables"]!.AsArray().OfType<JsonObject>())
        {
            if ((string?)row["text"] is not { } text || !text.Contains(Letterhead, StringComparison.Ordinal))
                continue;
            row["text"] = text.Replace(Letterhead, Address, StringComparison.Ordinal);
            row["hash"] = CanonicalJson.HashText((string)row["text"]!);
            placed.Add($"{Package}/{Scenario}#deliverables/deliverable.{(string)row["id"]!}");
        }

        Assert.NotEmpty(placed);
        var request = Equivalence.RequestFor(EquivalenceFixture.Scenario(Package, Scenario), capture, migrated, plan!);
        var run = await OfflineRun.RunAsync(plan!, request);
        var texts = Equivalence.Compare(Package, Scenario, capture, run, migrated, plan!, request);
        foreach (var site in placed)
        {
            var text = texts.Single(t => t.Site == site);
            Assert.True((text.Equal, text.Expected, text.Unexplained) == (false, true, false), $"{site}: equal {text.Equal}, expected {text.Expected}, rules {string.Join(",", text.Rules)}");
            Assert.Equal(["explicit-hard-break"], text.Rules);
        }

        Assert.All(texts.Where(t => !placed.Contains(t.Site)), t => Assert.DoesNotContain("explicit-hard-break", t.Rules));
        var unsaid = Equivalence.Compare(Package, Scenario, capture, run, migrated with { HardBreaks = new Dictionary<string, int>(StringComparer.Ordinal) }, plan!, request);
        Assert.All(placed, site => Assert.True(unsaid.Single(t => t.Site == site).Unexplained, site));
    }

    /// <summary>
    /// #1543 (ruling 1): example-two-documents' SNOMED tool guidance, a Markdown snippet four prompts place, written line per
    /// line, which 30 sent as written. The migration ends its line with <c>\</c>, so each prompt that places it differs from
    /// 30's by that <c>\</c> alone and is expected, credited to <c>explicit-hard-break</c>; with a word of 30's also lost
    /// (a capture whose prompt holds one 31's does not), it is unexplained.
    /// </summary>
    [Fact]
    public async Task ABackslashAPromptReads_IsExplainedByTheHardBreakRule()
    {
        const string Package = "example-two-documents";
        const string Scenario = "default";
        const string Sentence = "never a sentence or passage. If a tool returns";
        const string Lines = "never a sentence or passage.\nIf a tool returns";
        var migrated = MigrationFixture.Overlay(
            MigrateNodesTests.Migrated(Package, _ => { }, files => files["snippets/snomed_tool_guidance.md"] = files["snippets/snomed_tool_guidance.md"].Replace(Sentence, Lines, StringComparison.Ordinal)),
            MigrationFixture.HandEditsOf(Package));
        Assert.Equal(1, migrated.HardBreaks["snippets/snomed_tool_guidance.md"]);
        var (refusals, compiled) = CompiledPackage.Publish(MigratedPackagesTests.Upload(migrated), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed);
        Assert.True(compiled != null, string.Join("\n", refusals.Select(r => r.Sentence)));
        var (plan, refusal) = RunPlan.Build(compiled!.Resolve, compiled.ResolveHash, compiled.LockHash, null);
        Assert.True(plan != null, refusal?.Sentence);

        JsonObject Capture(string lost)
        {
            var capture = EquivalenceFixture.Capture(Package, Scenario);
            foreach (var row in capture["prompts"]!.AsArray().OfType<JsonObject>())
            {
                if ((string?)row["text"] is not { } text || !text.Contains(Sentence, StringComparison.Ordinal))
                    continue;
                row["text"] = text.Replace(Sentence, Lines, StringComparison.Ordinal).Replace("and retry.", "and retry" + lost + ".", StringComparison.Ordinal);
                row["hash"] = CanonicalJson.HashText((string)row["text"]!);
            }

            return capture;
        }

        string[] placed = ["extract_patient_concepts", "identify_problem", "create_typical_trajectory", "create_patient_trajectory"];
        var sites = placed.Select(p => $"{Package}/{Scenario}#prompts/node.{p}").ToList();
        var asWritten = Capture("");
        var request = Equivalence.RequestFor(EquivalenceFixture.Scenario(Package, Scenario), asWritten, migrated, plan!);
        var run = await OfflineRun.RunAsync(plan!, request);
        foreach (var key in placed)
            Assert.Contains("never a sentence or passage.\\\nIf a tool returns", run.Prompts.Single(p => p.Key == "node." + key).Text, StringComparison.Ordinal);

        var texts = Equivalence.Compare(Package, Scenario, asWritten, run, migrated, plan!, request);
        foreach (var site in sites)
        {
            var text = texts.Single(t => t.Site == site);
            Assert.True((text.Equal, text.Expected, text.Unexplained) == (false, true, false), $"{site}: equal {text.Equal}, expected {text.Expected}, rules {string.Join(",", text.Rules)}");
            Assert.Equal(["explicit-hard-break"], text.Rules);
        }

        var lost = Equivalence.Compare(Package, Scenario, Capture(" once"), run, migrated, plan!, request);
        Assert.All(sites, site => Assert.True(lost.Single(t => t.Site == site).Unexplained, site));
    }

    /// <summary>
    /// #1543 (ruling 2, D7, D8): edge-unset-object's intake line reads a field of the object input, a <c>markdown_block</c>
    /// at 31, with text on both sides; the migration moves it onto a line of its own (<c>Referred for: \</c>, the read, then
    /// the text after it). The letter differs from 30's by those line endings alone and is expected, credited to
    /// <c>block-read-own-line</c> alone; against the same run, a result that says it moved no read leaves it unexplained, and
    /// so does a capture whose letter holds a word 31's does not.
    /// </summary>
    [Fact]
    public async Task AReadTheMigrationMoved_IsExplainedByItsRule()
    {
        const string Package = "edge-unset-object";
        const string Scenario = "intake-unset";
        const string Site = $"{Package}/{Scenario}#deliverables/deliverable.note";
        var migrated = EquivalenceFixture.Migrated(Package);
        Assert.Equal(1, migrated.MovedReads["snippets/intake_line.md"]);
        var (capture, run, texts) = await EquivalenceFixture.Compared(Package, Scenario);
        Assert.StartsWith("Referred for: \\\n\n (from the intake form).", run.Deliverables.Single(d => d.Key == "deliverable.note").Text, StringComparison.Ordinal);

        var text = texts.Single(t => t.Site == Site);
        Assert.True((text.Equal, text.Expected, text.Unexplained) == (false, true, false), $"{Site}: equal {text.Equal}, expected {text.Expected}, rules {string.Join(",", text.Rules)}");
        Assert.Equal(["block-read-own-line"], text.Rules);
        Assert.All(texts.Where(t => t.Site != Site), t => Assert.DoesNotContain("block-read-own-line", t.Rules));

        var plan = EquivalenceFixture.Plan(Package);
        var request = Equivalence.RequestFor(EquivalenceFixture.Scenario(Package, Scenario), capture, migrated, plan);
        var unsaid = Equivalence.Compare(Package, Scenario, capture, run, migrated with { MovedReads = new Dictionary<string, int>(StringComparer.Ordinal) }, plan, request);
        Assert.True(unsaid.Single(t => t.Site == Site).Unexplained);

        var lost = capture.DeepClone().AsObject();
        foreach (var row in lost["deliverables"]!.AsArray().OfType<JsonObject>().Where(r => (string?)r["id"] == "note"))
        {
            row["text"] = ((string)row["text"]!).Replace("(from the intake form)", "(from the referral intake form)", StringComparison.Ordinal);
            row["hash"] = CanonicalJson.HashText((string)row["text"]!);
        }

        var partly = Equivalence.Compare(Package, Scenario, lost, run, migrated, plan, request).Single(t => t.Site == Site);
        Assert.True((partly.Expected, partly.Unexplained) == (false, true), $"{Site}: expected {partly.Expected}, rules {string.Join(",", partly.Rules)}");
        Assert.Equal(["block-read-own-line"], partly.Rules);
    }

    /// <summary>
    /// #1510 (#1509 comment 7207709261, item 2): composer-macros' profile in <c>no-signature</c> has no signature block, and
    /// 30 left the signature and the line before it out; through the renderer that tests the trimmed signature, so does 31.
    /// <c>authored</c> differs only as the plain text it holds is escaped in Markdown.
    /// </summary>
    [Fact]
    public async Task ALetterWithNoSignature_EndsAs30Did()
    {
        var (_, _, texts) = await EquivalenceFixture.Compared("composer-macros", "no-signature");
        ComparedText Site(string id) => texts.Single(t => t.Site == "composer-macros/no-signature#deliverables/deliverable." + id);

        foreach (var id in new[] { "signed_letter", "signed_only" })
            Assert.True((Site(id).Equal, Site(id).Rules.Count, Site(id).Unexplained) == (true, 0, false), $"{id}: equal {Site(id).Equal}, rules {string.Join(",", Site(id).Rules)}");
        var authored = Site("authored");
        Assert.Equal((false, true, false), (authored.Equal, authored.Expected, authored.Unexplained));
        Assert.Equal(["escaped-plain-text"], authored.Rules);
    }

    /// <summary>
    /// #1510 (F-D5′, ruled by the controller): with a signature, the letters end with the blank line (the line ending, for
    /// <c>authored</c>'s snippet) and the signature in 30's own bytes, which the renderer writes unescaped; no text is
    /// unexplained, and no rule is credited that the golden report did not already credit for it.
    /// </summary>
    [Fact]
    public async Task ALetterWithASignature_HasThirtysSignatureBytes()
    {
        var letters = new (string Package, string Id, string Before)[]
        {
            ("composer-macros", "signed_letter", "\n\n"), ("composer-macros", "signed_only", "\n\n"), ("composer-macros", "authored", "\n"),
            ("example-classifier-scope", "decline_letter", "\n\n"),
        };
        var compared = 0;
        foreach (var (package, id, before) in letters)
        {
            var golden = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(ByteEquivalenceFixtures.Root, GoldenFolder, package + ".json")))!["texts"]!.AsArray();
            foreach (var scenario in EquivalenceFixture.Scenarios(package))
            {
                if ((string?)EquivalenceFixture.Scenario(package, scenario)["profile"]?["signature"] is not { } signature)
                    continue;
                var (capture, run, texts) = await EquivalenceFixture.Compared(package, scenario);
                var site = $"{package}/{scenario}#deliverables/deliverable.{id}";
                if (texts.SingleOrDefault(t => t.Site == site) is not { } text)
                    continue;
                var old = (string?)capture["deliverables"]!.AsArray().Single(d => (string?)d!["id"] == id)!["text"];
                var now = run.Deliverables.Single(d => d.Key == "deliverable." + id).Text;
                Assert.False(text.Unexplained, site);
                if (old is null)
                    continue; // 30 skipped the letter here (its if, or a source that did not run)
                Assert.EndsWith(before + signature, old, StringComparison.Ordinal);
                Assert.EndsWith(before + signature, now, StringComparison.Ordinal);
                var credited = golden.Single(t => (string?)t!["site"] == site)!["rules"]!.AsArray().Select(r => (string)r!).ToList();
                Assert.All(text.Rules, rule => Assert.Contains(rule, credited));
                compared++;
            }
        }

        Assert.True(compared >= 8, $"{compared} letters with a signature compared");
    }

    [Fact]
    public async Task TheReport_CountsTheTextsExplained_PartlyExplained_AndUnexplained()
    {
        // R19: explained (no remainder), partly explained (a rule credited, a remainder left) and unexplained (no rule),
        // among the texts that differ; an equal text is in none.
        string Hash(char c) => "sha256:" + new string(c, 64);
        var texts = new ComparedText[]
        {
            new("p/s#status", Hash('0'), Hash('0'), true, false, null, [], false),
            new("p/s#prompts/node.a", Hash('0'), Hash('1'), false, true, "sent-format: …", ["sent-format"], false),
            new("p/s#prompts/node.b", Hash('0'), Hash('2'), false, false, null, ["item-ids-from-one", "sent-format"], true),
            new("p/s#prompts/node.c", Hash('0'), Hash('3'), false, false, null, [], true),
            new("p/s#prompts/node.d", Hash('0'), Hash('4'), false, false, null, [], true),
        };
        var report = MigrationReport.Of("v2026.11.3", EquivalenceFixture.Migrated("example-nested-note"), ExampleChoices.Json("example-nested-note"), texts);
        var json = JsonNode.Parse(report.ToJson())!.AsObject();

        Assert.Equal("""{"explained":1,"partlyExplained":1,"unexplained":2}""", json["counts"]!.ToJsonString());
        Assert.Equal("""["item-ids-from-one","sent-format"]""", json["texts"]![2]!["rules"]!.ToJsonString());
        Assert.Equal((false, true), ((bool)json["texts"]![1]!["unexplained"]!, (bool)json["texts"]![2]!["unexplained"]!));
        var schema = V31ContractsTests.SchemaOf("migration-report");
        Assert.True(schema.Validate(json.ToJsonString(), null), json.ToJsonString());
        var without = json.DeepClone().AsObject();
        without.Remove("counts");
        Assert.False(schema.Validate(without.ToJsonString(), null), "A report without counts is accepted.");
        var noRules = json.DeepClone().AsObject();
        noRules["texts"]![1]!.AsObject().Remove("rules");
        Assert.False(schema.Validate(noRules.ToJsonString(), null), "A text without rules is accepted.");
    }

    /// <summary>
    /// The rules no golden report credits, each with the reason it has no hit (R19: every rule either explains a text of a
    /// compared package or says why none can).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RulesWithoutAHit = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["required-blank-refused"] = "no compared capture's 31 start refuses a required text input supplied blank.",
        ["explicit-null-for-optional"] = "no compared prompt or rendered text writes a reply's or an input's object with an optional member 30 left out.",
        ["blank-conversion"] = "no compared capture has a text only 31 has whose own gate is a converted blank(x) that held for empty text.",
        ["macro-trailing-whitespace"] = "the one compared macro whose body ends in more white space than one line ending, edge-unruled-whitespace's problem_close, ends each item of a loop whose .content is read, which the migration strips as 30 trimmed it (#1510); the rule's own tests hold it on hand-made texts.",
        ["explicit-hard-break"] = "no migrated fixture writes a Markdown body line per line (every single line ending inside a paragraph is a prompt's or a part's), so the migration writes no \\ in any (#1510, item 4); AHardBreakTheMigrationWrote_IsExplainedByItsRule holds it on composer-macros with its letterhead written as an address, and ABackslashAPromptReads_IsExplainedByTheHardBreakRule on a prompt that places example-two-documents' guidance written line per line (#1543, ruling 1).",
    };

    [Fact]
    public async Task EveryRule_HasAGoldenHit_OrANamedReason()
    {
        // #1508 (comment 5, item 5; R19): crediting each rule on its own gives sent-format its hits beside the differences no
        // rule explains; a rule with none says why.
        var hits = new HashSet<string>(StringComparer.Ordinal);
        var reports = Path.Combine(ByteEquivalenceFixtures.Root, GoldenFolder);
        var packages = Directory.EnumerateFiles(reports, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(reports, f)[..^".json".Length].Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToList();
        Assert.Contains("composer-macros", packages);
        foreach (var package in packages)
        {
            foreach (var text in (await EquivalenceFixture.Report(package)).Texts)
                hits.UnionWith(text.Rules);
        }

        Assert.All(ExpectedDifferences.Rules, rule => Assert.True(hits.Contains(rule.Id) ^ RulesWithoutAHit.ContainsKey(rule.Id),
            hits.Contains(rule.Id) ? $"{rule.Id} has a golden hit and a reason." : $"{rule.Id} has no golden hit and no reason."));
        Assert.Contains("sent-format", hits);
        Assert.All(RulesWithoutAHit.Keys, id => Assert.Contains(id, ExpectedDifferences.Rules.Select(r => r.Id)));
    }

    [Fact]
    public async Task AMacroThe30EnginePlaced_HasItsMarkerWhereItsInstructionAsks()
    {
        // #1508 (comment 5, item 7, #10): composer-macros' section_intro (before the reply at 30) and section_close (after) are
        // markers at 31, which the captured replies never held: the request writes each where its instruction asks, so
        // the job runs on as 30's did.
        var migrated = EquivalenceFixture.Migrated("composer-macros");
        Assert.Equal([("macro.section_close", false), ("macro.section_intro", true)],
            Equivalence.EnginePlaced(migrated).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)));

        // Read through A10-003's own sentence: a macro placed on both sides is left out (its edit says where), and an edit
        // that names no macro of the package gives nothing.
        var both = migrated with { HandEdits = ["Hand edit needed in macros/section_intro.md, instruction: macro 'section_intro' was placed before and after the reply by the engine; say in its instruction where the model writes its marker."] };
        Assert.Empty(Equivalence.EnginePlaced(both));
        Assert.Empty(Equivalence.EnginePlaced(migrated with { HandEdits = ["Hand edit needed in macros/ghost.md, instruction: macro 'ghost' was placed before the reply by the engine; say in its instruction where the model writes its marker."] }));

        var request = Equivalence.RequestFor(EquivalenceFixture.Scenario("composer-macros", "empty-body"), EquivalenceFixture.Capture("composer-macros", "empty-body"), migrated, EquivalenceFixture.Plan("composer-macros"));
        Assert.Equal(Equivalence.EnginePlaced(migrated), request.EnginePlaced);

        var (_, run, _) = await EquivalenceFixture.Compared("composer-macros", "empty-body");
        Assert.Equal(RunState.Completed, run.State);

        // A package with no such macro gives none.
        Assert.Null(Equivalence.RequestFor(EquivalenceFixture.Scenario("example-nested-note", "default"), EquivalenceFixture.Capture("example-nested-note", "default"),
            EquivalenceFixture.Migrated("example-nested-note"), EquivalenceFixture.Plan("example-nested-note")).EnginePlaced);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public async Task TheReport_IsTheGolden(string package)
    {
        var report = (await EquivalenceFixture.Report(package)).ToJson();
        var file = Path.Combine([ByteEquivalenceFixtures.Root, GoldenFolder, .. package.Split('/')]) + ".json";
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes(report));
        }

        Assert.True(File.Exists(file), $"{GoldenFolder}/{package}.json is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
        Assert.True(report == ByteEquivalenceFixtures.ReadText(file), $"{GoldenFolder}/{package}.json differs");
    }

    private static JsonObject Scenario(Dictionary<string, JsonNode?> inputs, JsonObject choices)
    {
        var given = new JsonObject();
        foreach (var (id, value) in inputs)
            given[id] = value?.DeepClone();
        return new JsonObject
        {
            ["inputs"] = given,
            ["profile"] = new JsonObject { ["name"] = "Dr A. Sample", ["signature"] = null },
            ["macroChoices"] = choices,
            ["outputs"] = new JsonObject(),
        };
    }

    [Fact]
    public void ChoiceClassifiers_AreFound_ThroughTheSnippetsAPromptPlaces()
    {
        // #1508 (R14; Task 32 fix round 1): summary places red_flags and safety_net itself, and house_style through the
        // snippet style; each classifier is found, so each is answered from the capture.
        var found = ChoiceClassifiers.Of(MigrateNodesTests.Migrated("example-macros"));

        Assert.Equal(
            [("node.house_style_applies", "house_style"), ("node.red_flags_applies", "notes.red_flags"), ("node.safety_net_applies", "notes.safety_net")],
            found.Select(c => (c.Key, c.Macro)));
        Assert.All(found, c => Assert.Equal("node.summary", c.Placing));
    }

    [Fact]
    public void AChoiceAnswer_ReadsTheMacros30Name()
    {
        // #1508 (R14; Task 32 fix round 1): 30's model wrote the marker with the macro's 30 id; for a macro the migration
        // renamed, that id is matched, never the 31 one (said here as if letrozole_prescription_text had been 30's letrozole).
        var edited = EquivalenceFixture.Migrated("test/general");
        var renamed = edited with
        {
            MacroIds30 = new Dictionary<string, string>(StringComparer.Ordinal) { ["letrozole_prescription_text"] = "letrozole" },
        };
        var plan = EquivalenceFixture.Plan("test/general");
        JsonObject CaptureWith(string reply)
        {
            var capture = Capture();
            capture["outputs"]!["section_instructions:assessment_plan"] = reply;
            return capture;
        }

        bool? Answer(Migration31Result migrated, string reply) =>
            (bool?)Equivalence.ChoiceAnswers(CaptureWith(reply), migrated, plan)
                .Single(a => a.Key == "node.standards.letrozole_prescription_text_applies:assessment_plan").Answer["answer"];

        Assert.True(Answer(renamed, "Letrozole started.\n{{ macro.letrozole.content }}\n"));
        Assert.False(Answer(renamed, "Letrozole started.\n"));
        Assert.True(Answer(edited, "Letrozole started.\n{{ macro.letrozole_prescription_text.content }}\n"));
        Assert.False(Answer(edited, "Letrozole started.\n{{ macro.letrozole.content }}\n"));

        // A folder's item: the folder and the item are each read back.
        var folders = edited with
        {
            MacroIds30 = new Dictionary<string, string>(StringComparer.Ordinal) { ["notes_macro"] = "notes", ["notes/flags_macro"] = "flags" },
        };
        Assert.Equal("notes.flags", Equivalence.Name30(folders, "notes_macro.flags_macro"));
        Assert.Equal("notes.safety_net", Equivalence.Name30(folders, "notes_macro.safety_net"));
        Assert.Equal("other.flags_macro", Equivalence.Name30(folders, "other.flags_macro"));
    }

    private static JsonObject Capture() => new()
    {
        ["status"] = "Completed",
        ["failure"] = null,
        ["outputs"] = new JsonObject(),
        ["prompts"] = new JsonArray(),
        ["renders"] = new JsonArray(),
        ["deliverables"] = new JsonArray(),
    };
}

/// <summary>
/// #1507 (Task 15): what the comparison's tests share: each package of Task 12 migrated (<see cref="MigratedPackagesTests.Migrated"/>)
/// and compiled to its plan, its scenarios and captures (area 01's, read), each scenario compared once per test run, and the
/// report over all of a package's scenarios.
/// </summary>
internal static class EquivalenceFixture
{
    private static readonly ConcurrentDictionary<string, RunPlan> Plans = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Lazy<Task<(JsonObject, OfflineResult, IReadOnlyList<ComparedText>)>>> Runs = new(StringComparer.Ordinal);

    /// <summary>
    /// The migrated package as it is compared: as migrated when it has no hand edit, else with the operator's edits made
    /// (<see cref="MigrationFixture.Edited"/>, Task 16).
    /// </summary>
    public static Migration31Result Migrated(string package) =>
        MigratedPackagesTests.Migrated(package).HandEdits.Count == 0 ? MigratedPackagesTests.Migrated(package) : MigrationFixture.Edited(package);

    /// <summary>The migrated package's plan; it must publish.</summary>
    public static RunPlan Plan(string package) => Plans.GetOrAdd(package, name =>
    {
        var (refusals, compiled) = CompiledPackage.Publish(MigratedPackagesTests.Upload(Migrated(name)), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed);
        Assert.True(compiled != null, string.Join("\n", refusals.Select(r => r.Sentence)));
        var (plan, refusal) = RunPlan.Build(compiled!.Resolve, compiled.ResolveHash, compiled.LockHash, null);
        Assert.True(plan != null, refusal?.Sentence);
        return plan!;
    });

    /// <summary>The scenarios of <paramref name="package"/>, in ordinal order.</summary>
    public static IReadOnlyList<string> Scenarios(string package) =>
        Directory.EnumerateFiles(Path.Combine([ByteEquivalenceFixtures.Root, "scenarios", .. package.Split('/')]), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!).Order(StringComparer.Ordinal).ToList();

    public static JsonObject Scenario(string package, string scenario) => Read("scenarios", package, scenario);

    public static JsonObject Capture(string package, string scenario) => Read("captures", package, scenario);

    /// <summary>The capture, the 31 run and the compared texts of one scenario (capture → RequestFor → OfflineRun → Compare).</summary>
    public static async Task<(JsonObject Capture, OfflineResult Run, IReadOnlyList<ComparedText> Texts)> Compared(string package, string scenario) =>
        await Runs.GetOrAdd($"{package}#{scenario}", _ => new Lazy<Task<(JsonObject, OfflineResult, IReadOnlyList<ComparedText>)>>(async () =>
        {
            var migrated = Migrated(package);
            var plan = Plan(package);
            var capture = Capture(package, scenario);
            var request = Equivalence.RequestFor(Scenario(package, scenario), capture, migrated, plan);
            var run = await OfflineRun.RunAsync(plan, request);
            return (capture, run, Equivalence.Compare(package, scenario, capture, run, migrated, plan, request));
        })).Value;

    /// <summary><c>migration-report.json</c> of <paramref name="package"/> over all its scenarios, in their order.</summary>
    public static async Task<MigrationReport> Report(string package)
    {
        var texts = new List<ComparedText>();
        foreach (var scenario in Scenarios(package))
            texts.AddRange((await Compared(package, scenario)).Texts);
        var oldVersion = (string)JsonNode.Parse(ByteEquivalenceFixtures.ReadPackage(package).ManifestJson)!["version"]!;
        var migrated = Migrated(package);
        return MigrationReport.Of(oldVersion, migrated, ExampleChoices.Json(package), texts, MigrationReport.WorstCaseNote(migrated, V31Contract.PublishedTypes()));
    }

    private static JsonObject Read(string folder, string package, string scenario) =>
        JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine([ByteEquivalenceFixtures.Root, folder, .. package.Split('/'), scenario + ".json"])))!.AsObject();
}
