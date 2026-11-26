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
        Assert.All(texts, t => Assert.Equal((true, false, (string?)null), (t.Equal, t.Expected, t.Explanation)));
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

    [Fact]
    public async Task ADifferenceNoRuleExplains_IsUnexpected()
    {
        // 30 wrote the encounter date as Scriban's default "09 Nov 2026"; 31 writes the date ISO (part 05 § 1): no ruling.
        var (_, _, texts) = await EquivalenceFixture.Compared("example-conditional-documents", "new-patient-no-billing");
        var date = texts.Single(t => t.Site.EndsWith("#prompts/node.sections_consult_note_section.consult_note_section:assessment", StringComparison.Ordinal));

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
        // D11: a prompt only 31 sent (30 ran only the nodes an applicable deliverable needed), and the deliverable only 31
        // composed in R2's capture.
        var (_, _, texts) = await EquivalenceFixture.Compared("example-classifier-scope", "in-scope");
        var only31 = texts.Single(t => t.Site.EndsWith("#prompts/node.letter_decline_letter.decline_letter:letter", StringComparison.Ordinal));
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
