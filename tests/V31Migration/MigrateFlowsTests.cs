using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Migration.MigrateNodesTests;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the flow rows; part 04 §§ 1 to 5): the flow nodes. Each becomes its folder
/// whose <c>index.json</c> is its header, its body inside it; the loop headers of § 2.8, the while of § 3.5 and the branches
/// of § 5.4 are the spec's examples, member for member, but for the flags (Task 12); <c>in</c> is a reference string;
/// <c>labelField</c> is <c>itemLabel</c> on each loop over it; a loop with <c>until</c> is sequential; a <c>case</c> with no
/// <c>else</c> gains an empty branch for the values its branches miss; every <c>ret</c> is the child's full name.
/// </summary>
public class MigrateFlowsTests
{
    private static readonly string[] Flags = ["reproducible", "modelDerived", "runDependent", "profileCarrying"];

    /// <summary>Asserts <paramref name="actual"/> is <paramref name="expected"/> member for member but for the flags, and that its members are in <paramref name="shape"/>'s order.</summary>
    private static void AssertExample(string expected, JsonObject actual, DeclarationShape shape)
    {
        var example = JsonNode.Parse(expected)!.AsObject();
        Assert.Equal(shape.Members.Select(m => m.Name), actual.Select(m => m.Key));
        Assert.Equal(example.Select(m => m.Key).Order(StringComparer.Ordinal), actual.Select(m => m.Key).Order(StringComparer.Ordinal));
        foreach (var (member, value) in example)
        {
            if (Flags.Contains(member))
                continue;
            Assert.True(JsonNode.DeepEquals(value, actual[member]), $"{member}: expected {value?.ToJsonString()}, was {actual[member]?.ToJsonString()}");
        }
    }

    [Fact]
    public void TheNestedNotesLoops_AreTheSpecsExamples()
    {
        // Part 04 § 2.8: the 30 loop parts, renamed sections by the hand choice (§ 1.9, D6).
        var result = Migrated("example-nested-note");

        AssertExample("""
            { "kind": "for", "producer": "computed", "label": "Sections", "description": null,
              "format": { "type": "array", "items": { "type": "array", "items": { "$ref": "plaintext" } } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "node.plan.value.sections",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": "title",
              "nodes": ["outline", "paragraphs"], "ret": "node.sections.paragraphs" }
            """, Json(result, "nodes/sections/index.json"), SourceShapes.FlowFor);
        AssertExample("""
            { "kind": "for", "producer": "computed", "label": "Paragraphs", "description": null,
              "format": { "type": "array", "items": { "$ref": "plaintext" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "node.outline.value.points",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": null,
              "nodes": ["draft_paragraph"], "ret": "node.sections.paragraphs.draft_paragraph" }
            """, Json(result, "nodes/sections/paragraphs/index.json"), SourceShapes.FlowFor);
    }

    [Fact]
    public void TheConditionalSectionsLoop_IsTheSpecsExample()
    {
        AssertExample("""
            { "kind": "for", "producer": "computed", "label": "Sections — drafting each requested section by its kind", "description": null,
              "format": { "type": "array", "items": { "$ref": "plaintext" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "input.sections.value",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": "title",
              "nodes": ["route"], "ret": "node.sections_draft.route" }
            """, Json(Migrated("example-conditional-sections"), "nodes/sections_draft/index.json"), SourceShapes.FlowFor);
    }

    [Fact]
    public void TestGeneralsSnippetLoop_IsTheSpecsExample()
    {
        AssertExample("""
            { "kind": "for", "producer": "computed", "label": "Standards", "description": null,
              "format": { "type": "array", "items": { "$ref": "plaintext" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "snippet.standards",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": "title",
              "nodes": ["section_instructions", "patient_section_draft", "standard_section_draft"],
              "ret": "node.standards.section_instructions" }
            """, Json(Migrated("test/general"), "nodes/standards/index.json"), SourceShapes.FlowFor);
    }

    [Fact]
    public void TheNestedNotesWhile_IsTheSpecsExample()
    {
        // Part 04 § 3.5; checkFirst, optional at 30, is written with 30's default.
        AssertExample("""
            { "kind": "while", "producer": "computed", "label": "Refining the summary", "description": null,
              "format": "plaintext",
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "while": { "!==": [ { "var": "node.judge.value.verdict" }, "good" ] },
              "max": 3, "until": null, "checkFirst": false,
              "nodes": ["draft_summary", "judge"], "ret": "node.refine.draft_summary" }
            """, Json(Migrated("example-nested-note"), "nodes/refine/index.json"), SourceShapes.FlowWhile);
    }

    [Fact]
    public void TheConditionalSectionsBranches_AreTheSpecsExamples()
    {
        var result = Migrated("example-conditional-sections");

        AssertExample("""
            { "kind": "case", "producer": "computed", "label": "Routing a section by its kind", "description": null,
              "format": "plaintext",
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "case": "item.value.kind",
              "branches": [
                { "when": ["narrative"], "nodes": ["narrative_draft"], "ret": "node.sections_draft.route.narrative_draft" },
                { "when": ["table"],     "nodes": ["table_draft"],     "ret": "node.sections_draft.route.table_draft" },
                { "when": ["omit"],      "nodes": [],                  "ret": null } ],
              "else": null }
            """, Json(result, "nodes/sections_draft/route/index.json"), SourceShapes.FlowCase);
        AssertExample("""
            { "kind": "if", "producer": "computed", "label": "Narrative sections only", "description": null,
              "format": "plaintext",
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "if": { "===": [ { "var": "item.value.kind" }, "narrative" ] },
              "then": { "nodes": ["patient_summary"], "ret": "node.patient_summaries.narrative_only.patient_summary" },
              "else": null }
            """, Json(result, "nodes/patient_summaries/narrative_only/index.json"), SourceShapes.FlowIf);
        AssertExample("""
            { "kind": "if", "producer": "computed", "label": "Holding the billing section back unless asked", "description": null,
              "format": "plaintext",
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "if": { "or": [ { "!==": [ { "var": "item.id" }, "billing" ] },
                              { "===": [ { "var": "input.include_billing.value" }, true ] } ] },
              "then": { "nodes": ["standing_draft"], "ret": "node.standing_sections.applies.standing_draft" },
              "else": null }
            """, Json(result, "nodes/standing_sections/applies/index.json"), SourceShapes.FlowIf);
        Assert.Empty(result.HandEdits);
    }

    [Fact]
    public void ABodyNode_LivesInItsFlowsFolder_AndAFlowsIndexIsItsHeader()
    {
        var result = Migrated("example-conditional-sections");

        Assert.Contains("nodes/sections_draft/route/narrative_draft.md", result.Files.Keys);
        Assert.Contains("nodes/patient_summaries/narrative_only/patient_summary.md", result.Files.Keys);
        Assert.DoesNotContain("nodes/narrative_draft.md", result.Files.Keys);
        Assert.Equal(["sections_draft", "patient_summaries", "standing_sections", "coverage"],
            Json(result, "nodes/index.json")["items"]!.AsArray().Select(i => (string?)i));
        Assert.Equal("case", (string?)Json(result, "nodes/sections_draft/route/index.json")["kind"]);
        Assert.False(Json(result, "nodes/sections_draft/route/index.json").ContainsKey("items"));
    }

    [Theory]
    [InlineData("example-conditional-sections", "nodes/sections_draft/index.json", "input.sections.value")]         // an input fan
    [InlineData("example-problem-list", "nodes/problems/index.json", "node.find_problems.value.problems")]          // a node's list
    [InlineData("example-nested-note", "nodes/sections/paragraphs/index.json", "node.outline.value.points")]           // a body node's list
    [InlineData("example-classifier-scope", "nodes/plan_sections/index.json", "snippet.plan_sections")]             // a snippet folder
    public void In_IsAReferenceString(string package, string path, string @in) =>
        Assert.Equal(@in, (string?)Json(Migrated(package), path)["in"]);

    [Fact]
    public void LabelField_IsItemLabel_OnEachLoopOverIt()
    {
        var result = Migrated("composer-sources");

        Assert.Equal("title", (string?)Json(result, "nodes/topic_loop/index.json")["itemLabel"]);                  // the input's
        Assert.Equal("title", (string?)Json(result, "nodes/section_loop/index.json")["itemLabel"]);                // the loop's
        Assert.Equal("heading", (string?)Json(result, "nodes/section_loop/point_loop/index.json")["itemLabel"]);   // the loop's
        Assert.Null(Json(Migrated("example-nested-note"), "nodes/sections/paragraphs/index.json")["itemLabel"]);      // none

        // Two loops over one input both take its labelField.
        var twice = Migrated("example-conditional-sections");
        Assert.Equal("title", (string?)Json(twice, "nodes/sections_draft/index.json")["itemLabel"]);
        Assert.Equal("title", (string?)Json(twice, "nodes/patient_summaries/index.json")["itemLabel"]);
    }

    [Fact]
    public void ALoopWithUntil_IsSequential_EveryOtherParallel_NoneCapped()
    {
        var letters = Json(Migrated("example-prior-letters"), "nodes/letters/index.json");
        Assert.False(letters["parallel"]!.GetValue<bool>());
        Assert.Null(letters["maxParallel"]);
        Assert.Equal(JsonNode.Parse("""{ "===": [ { "var": "node.read_letter.value.answers_question" }, true ] }""")!.ToJsonString(),
            letters["until"]!.ToJsonString());
        Assert.Equal("node.letters.letter_summary", (string?)letters["ret"]);

        var sections = Json(Migrated("composer-sources"), "nodes/section_loop/index.json");
        Assert.True(sections["parallel"]!.GetValue<bool>());
        Assert.Null(sections["maxParallel"]);
        Assert.Equal(3, sections["max"]!.GetValue<int>());
    }

    [Fact]
    public void AWhile_WritesItsCheck_ItsBound_ItsUntil_AndCheckFirst()
    {
        var refine = Json(Migrated("composer-sources"), "nodes/refine/index.json");

        Assert.Equal(JsonNode.Parse("""{ "===": [ { "var": "input.redraft.value" }, true ] }""")!.ToJsonString(), refine["while"]!.ToJsonString());
        Assert.Equal(3, refine["max"]!.GetValue<int>());
        Assert.Equal(JsonNode.Parse("""{ "===": [ { "var": "node.judge.value.verdict" }, "good" ] }""")!.ToJsonString(), refine["until"]!.ToJsonString());
        Assert.True(refine["checkFirst"]!.GetValue<bool>());
        Assert.Equal("node.refine.draft_summary", (string?)refine["ret"]);
    }

    [Fact]
    public void AnIf_WritesItsThenAndElseBodies()
    {
        var result = Migrated("composer-sources", root =>
        {
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "no_plan", ["label"] = "Saying there is no plan", ["format"] = "prose" });
            Node(root, "plan_gate")["else"] = new JsonObject { ["nodes"] = new JsonArray("no_plan"), ["ret"] = "node.no_plan" };
        }, files => files["prompts/no_plan.md"] = "Say that no plan was asked for.\n");

        var gate = Json(result, "nodes/plan_gate/index.json");
        Assert.Equal(Compact("""{ "nodes": ["draft_plan"], "ret": "node.plan_gate.draft_plan" }"""), gate["then"]!.ToJsonString());
        Assert.Equal(Compact("""{ "nodes": ["no_plan"], "ret": "node.plan_gate.no_plan" }"""), gate["else"]!.ToJsonString());
        Assert.Contains("nodes/plan_gate/no_plan.md", result.Files.Keys);
        Assert.Equal("Say that no plan was asked for.\n", Md(result, "nodes/plan_gate/no_plan.md").Body);
    }

    [Fact]
    public void ACaseWithNoElse_GainsAnEmptyBranchForEachMissingValue()
    {
        // An enum input: tone's values are formal, plain and none; 30 skipped none.
        var tone = Json(Migrated("composer-sources"), "nodes/tone_case/index.json");
        Assert.Equal("input.tone.value", (string?)tone["case"]);
        Assert.Equal(Compact("""
            [ { "when": ["formal"], "nodes": ["formal_line"], "ret": "node.tone_case.formal_line" },
              { "when": ["plain"], "nodes": ["plain_line"], "ret": "node.tone_case.plain_line" },
              { "when": ["none"], "nodes": [], "ret": null } ]
            """), tone["branches"]!.ToJsonString());
        Assert.Null(tone["else"]);

        // A classifier: its values are the ones its with gave at 30 (a missing value each, in their order).
        var result = Migrated("example-v12-constructs", root =>
        {
            Node(root, "tone")["with"] = new JsonObject { ["values"] = new JsonArray("formal", "friendly", "neutral") };
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "formal_note", ["label"] = "A formal note", ["format"] = "prose" });
            root["deliverables"]!.AsArray().OfType<JsonObject>().First()["sources"]!.AsArray().Add("node.tone_line");
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "tone_line", ["label"] = "A line in the referral's tone", ["case"] = "node.tone.value",
                ["branches"] = new JsonArray(new JsonObject { ["when"] = new JsonArray("formal"), ["nodes"] = new JsonArray("formal_note"), ["ret"] = "node.formal_note" }),
            });
        }, files => files["prompts/formal_note.md"] = "Write one formal line.\n");
        var line = Json(result, "nodes/tone_line/index.json");
        Assert.Equal("node.tone.value.value", (string?)line["case"]);
        Assert.Equal(Compact("""{ "when": ["friendly", "neutral"], "nodes": [], "ret": null }"""), line["branches"]![1]!.ToJsonString());
        // But a classifier's answers are not typed at 31 (classification's value is a plain string; its with is dropped),
        // so the 31 checker refuses the subject (S04-044): a hand edit on the case.
        var edit = Assert.Single(result.HandEdits, e => e.Contains("tone_line", StringComparison.Ordinal));
        const string reason = "the case switches on classifier 'tone', whose answers are not typed at 31; give it a package format whose value is an enum of its answers.";
        Assert.Equal($"Hand edit needed in nodes/tone_line/index.json, case: {reason}", edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "nodes/tone_line/index.json"));
        V31Contract.AssertSentence(new SourceRefusal("A10-032", reason, ""));

        // A case that lists every value gains nothing.
        var complete = Json(Migrated("composer-sources", root =>
            Node(root, "tone_case")["branches"]![1]!["when"] = new JsonArray("plain", "none")), "nodes/tone_case/index.json");
        Assert.Equal(2, complete["branches"]!.AsArray().Count);
    }

    [Fact]
    public void ACaseWhoseSubjectCanBeNull_AndHasNoElse_IsAHandEdit()
    {
        // Part 04 § 5.2: a nullable subject sends null to an else, which 30's case did not have.
        var result = Migrated("composer-sources", root =>
            root["inputs"]!.AsArray().OfType<JsonObject>().Single(i => (string?)i["id"] == "tone")["required"] = false);

        var tone = Json(result, "nodes/tone_case/index.json");
        Assert.Null(tone["else"]);
        var edit = Assert.Single(result.HandEdits, e => e.Contains("tone_case", StringComparison.Ordinal));
        Assert.Equal("Hand edit needed in nodes/tone_case/index.json, else: input.tone.value can be null, which a 31 case sends to its else; add an else body.", edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "nodes/tone_case/index.json"));
        V31Contract.AssertSentence(new SourceRefusal("A10-029", "input.tone.value can be null, which a 31 case sends to its else; add an else body.", ""));
    }

    [Fact]
    public void ACaseThat31CannotList_HasItsSentence()
    {
        // 30 switches only on an enum or a classifier, so no migrated package reaches A10-028; its sentence is held here.
        var reason = PackageFormat.V31.Migration.MigrationSentences.CaseNotEnumerated("input.notes.content");
        Assert.Equal("the case switches on input.notes.content, whose values 31 cannot list; switch on an enumerated string.", reason);
        V31Contract.AssertSentence(new SourceRefusal("A10-028", reason, ""));
    }

    [Fact]
    public void ACaseSubjectThatIsNoSingleRead_IsAHandEdit_WithTheCheckersSentence()
    {
        // 30's subjects are all single reads, so this is held at the step that judges the converter's answer.
        var file = "nodes/route/index.json";
        var expression = new ConversionResult(JsonNode.Parse("""{ "size": [ { "var": "input.topics.value" } ] }"""), "integer", ["input.topics.value"], []);
        var reason = PackageFormat.V31.Migration.Migrate.CaseSubjectRefusal("route", file, expression);
        Assert.Equal("Case 'route' case is an expression; it names one readable thing: compute the value in a computed node and name that.", reason);
        V31Contract.AssertSentence(new SourceRefusal("S04-017", reason!, file));

        var refused = new ConversionResult(null, null, [], ["first.", "second."]);
        Assert.Equal("first. second.", PackageFormat.V31.Migration.Migrate.CaseSubjectRefusal("route", file, refused));

        var read = new ConversionResult(JsonNode.Parse("""{ "var": "item.value.kind" }"""), "string", ["item.value.kind"], []);
        Assert.Null(PackageFormat.V31.Migration.Migrate.CaseSubjectRefusal("route", file, read));
    }

    [Theory]
    [MemberData(nameof(MigrateLayoutTests.Packages), MemberType = typeof(MigrateLayoutTests))]
    public void EveryRet_IsTheFullNameOfAChild(string package)
    {
        var result = Migrated(package);

        foreach (var (path, text) in result.Files.Where(f => f.Key.StartsWith("nodes/", StringComparison.Ordinal) && f.Key.EndsWith("/index.json", StringComparison.Ordinal) && f.Key != "nodes/index.json"))
        {
            var header = JsonNode.Parse(text)!.AsObject();
            var folder = path[..^"/index.json".Length];
            var key = "node." + folder["nodes/".Length..].Replace('/', '.');
            IEnumerable<JsonNode?> bodies = (string?)header["kind"] switch
            {
                "for" or "while" => [header],
                "if" => [header["then"], header["else"]],
                _ => [.. header["branches"]!.AsArray(), header["else"]],
            };
            foreach (var body in bodies.OfType<JsonObject>())
            {
                if (body["ret"] is not JsonValue ret)
                {
                    Assert.Empty(body["nodes"]!.AsArray());
                    continue;
                }

                var child = ((string)ret!)[(key.Length + 1)..];
                Assert.StartsWith(key + ".", (string)ret!, StringComparison.Ordinal);
                Assert.Contains(child, body["nodes"]!.AsArray().Select(n => (string?)n));
                Assert.True(result.Files.ContainsKey($"{folder}/{child}.md") || result.Files.ContainsKey($"{folder}/{child}/index.json"), $"{package}: {ret}");
            }
        }
    }

    [Fact]
    public void AWhenGate_BecomesIf()
    {
        // A node's when, retired at 26, reaches 30 wrapped in an if node (the 25 → 26 rung); at 31 that if is converted.
        var (manifest25, files25) = V25Package.Minimal();
        var root = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(manifest25, WorkflowPackageManifestJson.WriteOptions))!.AsObject();
        root["nodes"]!.AsArray().OfType<JsonObject>().Single(n => (string?)n["id"] == "assessment")["when"] = "input.kind == \"new\"";

        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files25, new Migration31Options(
            V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, V25Package.Catalog()));

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        var gate = Json(result, "nodes/assessment_if/index.json");
        Assert.Equal("if", (string?)gate["kind"]);
        Assert.Equal(Compact("""{ "===": [ { "var": "input.kind.value" }, "new" ] }"""), gate["if"]!.ToJsonString());
        Assert.Equal(Compact("""{ "nodes": ["assessment"], "ret": "node.assessment_if.assessment" }"""), gate["then"]!.ToJsonString());
        Assert.Contains("nodes/assessment_if/assessment.md", result.Files.Keys);
        Assert.Equal("node.assessment_if.assessment", result.Keys["assessment"]);
    }
}
