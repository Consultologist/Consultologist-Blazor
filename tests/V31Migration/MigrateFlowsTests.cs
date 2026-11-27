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
/// of § 5.4 are the spec's examples, member for member, the derived flags among them (<c>Migrate.Derived</c>); <c>in</c> is a reference string;
/// <c>labelField</c> is <c>itemLabel</c> on each loop over it; a loop with <c>until</c> is sequential; a <c>case</c> with no
/// <c>else</c> gains an empty branch for the values its branches miss; every <c>ret</c> is the child's full name.
/// </summary>
public class MigrateFlowsTests
{
    /// <summary>Asserts <paramref name="actual"/> is <paramref name="expected"/> member for member, the flags included, and that its members are in <paramref name="shape"/>'s order.</summary>
    private static void AssertExample(string expected, JsonObject actual, DeclarationShape shape)
    {
        var example = JsonNode.Parse(expected)!.AsObject();
        Assert.Equal(shape.Members.Select(m => m.Name), actual.Select(m => m.Key));
        Assert.Equal(example.Select(m => m.Key).Order(StringComparer.Ordinal), actual.Select(m => m.Key).Order(StringComparer.Ordinal));
        foreach (var (member, value) in example)
        {
            Assert.True(JsonNode.DeepEquals(value, actual[member]), $"{member}: expected {value?.ToJsonString()}, was {actual[member]?.ToJsonString()}");
        }
    }

    [Fact]
    public void TheNestedNotesLoops_AreTheSpecsExamples()
    {
        // Part 04 § 2.8: the 30 loop parts, renamed sections by the hand choice (§ 1.9, D6). #1510 (item 1; F-D1): the
        // note reads the loop's .content, so each loop returns its items through a node that strips them, as 30 did; the
        // outer loop's items are then the stripped text of the inner loop's rendering.
        var result = Migrated("example-nested-note");

        AssertExample("""
            { "kind": "for", "producer": "computed", "label": "Sections", "description": null,
              "format": { "type": "array", "items": { "$ref": "markdown_block" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "node.plan.value.sections",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": "title",
              "nodes": ["outline", "paragraphs", "paragraphs_stripped"], "ret": "node.sections.paragraphs_stripped" }
            """, Json(result, "nodes/sections/index.json"), SourceShapes.FlowFor);
        AssertExample("""
            { "kind": "for", "producer": "computed", "label": "Paragraphs", "description": null,
              "format": { "type": "array", "items": { "$ref": "markdown_block" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "node.outline.value.points",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": null,
              "nodes": ["draft_paragraph", "draft_paragraph_stripped"], "ret": "node.sections.paragraphs.draft_paragraph_stripped" }
            """, Json(result, "nodes/sections/paragraphs/index.json"), SourceShapes.FlowFor);
    }

    [Fact]
    public void TheConditionalSectionsLoop_IsTheSpecsExample()
    {
        AssertExample("""
            { "kind": "for", "producer": "computed", "label": "Sections — drafting each requested section by its kind", "description": null,
              "format": { "type": "array", "items": { "$ref": "markdown_block" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "input.sections.value",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": "title",
              "nodes": ["route", "route_stripped"], "ret": "node.sections_draft.route_stripped" }
            """, Json(Migrated("example-conditional-sections"), "nodes/sections_draft/index.json"), SourceShapes.FlowFor);
    }

    [Fact]
    public void TestGeneralsSnippetLoop_IsTheSpecsExample()
    {
        AssertExample("""
            { "kind": "for", "producer": "computed", "label": "Standards", "description": null,
              "format": { "type": "array", "items": { "$ref": "markdown_block" } },
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "in": "snippet.standards",
              "parallel": true, "maxParallel": null, "until": null, "max": null,
              "itemLabel": "title",
              "nodes": ["section_instructions", "patient_section_draft", "standard_section_draft", "section_instructions_stripped"],
              "ret": "node.standards.section_instructions_stripped" }
            """, Json(Migrated("test/general"), "nodes/standards/index.json"), SourceShapes.FlowFor);
    }

    [Fact]
    public void TheNestedNotesWhile_IsTheSpecsExample()
    {
        // Part 04 § 3.5; checkFirst, optional at 30, is written with 30's default.
        AssertExample("""
            { "kind": "while", "producer": "computed", "label": "Refining the summary", "description": null,
              "format": "markdown_block",
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
              "format": "markdown_block",
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
              "format": "markdown_block",
              "reproducible": false, "modelDerived": true, "runDependent": false, "profileCarrying": false,
              "if": { "===": [ { "var": "item.value.kind" }, "narrative" ] },
              "then": { "nodes": ["patient_summary"], "ret": "node.patient_summaries.narrative_only.patient_summary" },
              "else": null }
            """, Json(result, "nodes/patient_summaries/narrative_only/index.json"), SourceShapes.FlowIf);
        AssertExample("""
            { "kind": "if", "producer": "computed", "label": "Holding the billing section back unless asked", "description": null,
              "format": "markdown_block",
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
        Assert.Equal("node.letters.letter_summary_stripped", (string?)letters["ret"]); // #1510: the note reads its .content

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
    public void ACheckFirstWhileWhoseCheckReadsItsOwnBody_IsAHandEditOnCheckFirst()
    {
        // Final review A2 (A06-002, the owner's ruling 4 on #1507): a check made before the first pass that reads a node of
        // the loop's own body has nothing to read. checkFirst stays as 30 had it, so the checker refuses it.
        var result = Migrated("example-nested-note", root => Node(root, "refine")["checkFirst"] = true);

        var refine = Json(result, "nodes/refine/index.json");
        Assert.True(refine["checkFirst"]!.GetValue<bool>());
        var edit = Assert.Single(result.HandEdits);
        const string reason = "the check reads the loop's own nodes (judge), which have no value before the first pass; check after the pass, or read a value from outside the loop.";
        Assert.Equal($"Hand edit needed in nodes/refine/index.json, checkFirst: {reason}", edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "nodes/refine/index.json"));
        V31Contract.AssertSentence(new SourceRefusal("A10-066", reason, ""));
        Assert.Contains(MigratedPackagesTests.Publish(result), r => r.Id == "A06-002");

        // The near pair: a checkFirst while whose check reads an input (composer-sources' refine) is no hand edit.
        Assert.DoesNotContain(Migrated("composer-sources").HandEdits, e => e.Contains("checkFirst", StringComparison.Ordinal));
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

        // A case that lists every value gains nothing.
        var complete = Json(Migrated("composer-sources", root =>
            Node(root, "tone_case")["branches"]![1]!["when"] = new JsonArray("plain", "none")), "nodes/tone_case/index.json");
        Assert.Equal(2, complete["branches"]!.AsArray().Count);
    }

    /// <summary>
    /// #1508 (R12, comment 5, item 1; A10-032 retired): a <c>case</c> on a 30 classifier switches on its answer format's
    /// enum, so it is written with no hand edit: three values, two branches and no <c>else</c>, the missing value gets the
    /// empty branch part 10 § 1.2 writes, and the package publishes.
    /// </summary>
    [Fact]
    public void ACaseOnAClassifier_NeedsNoHandEdit()
    {
        var result = Migrated("example-classifier-scope", root =>
        {
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "in_scope_note", ["label"] = "An in-scope note", ["format"] = "prose" });
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "out_of_scope_note", ["label"] = "An out-of-scope note", ["format"] = "prose" });
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "scope_line", ["label"] = "A line on the referral's scope", ["case"] = "node.scope.value",
                ["branches"] = new JsonArray(
                    new JsonObject { ["when"] = new JsonArray("in_scope"), ["nodes"] = new JsonArray("in_scope_note"), ["ret"] = "node.in_scope_note" },
                    new JsonObject { ["when"] = new JsonArray("out_of_scope"), ["nodes"] = new JsonArray("out_of_scope_note"), ["ret"] = "node.out_of_scope_note" }),
            });
            root["deliverables"]!.AsArray().OfType<JsonObject>().First()["sources"]!.AsArray().Add("node.scope_line");
        }, files =>
        {
            files["prompts/in_scope_note.md"] = "Write one line saying the clinic will see Margaret Okafor.\n";
            files["prompts/out_of_scope_note.md"] = "Write one line saying the clinic does not see this request.\n";
        });

        var line = Json(result, "nodes/scope_line/index.json");
        Assert.Equal("node.scope.value.value", (string?)line["case"]);
        Assert.Equal(3, line["branches"]!.AsArray().Count);
        Assert.Equal(Compact("""{ "when": ["needs_information"], "nodes": [], "ret": null }"""), line["branches"]![2]!.ToJsonString());
        Assert.Null(line["else"]);
        Assert.Empty(result.HandEdits);
        Assert.DoesNotContain(result.Notices, n => n.StartsWith("Hand edit needed", StringComparison.Ordinal));
        var (refusals, compiled) = CompiledPackage.Publish(MigratedPackagesTests.Upload(result), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed);
        Assert.True(compiled != null, string.Join("\n", refusals.Select(r => r.Sentence)));
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

    /// <summary>
    /// #1510 (#1509 comment 7207709261, item 1; F-D1, F-D2): 30 trimmed each item of a loop it composed; at 31 a loop's
    /// <c>.content</c> is the engine's rendering of its items. So a <c>for</c> whose <c>.content</c> is read returns each item
    /// through a new <c>rendered</c> node, the last of its body, that strips the old <c>ret</c>'s text, runs only when the old
    /// <c>ret</c> completed, and writes Markdown exactly when the old <c>ret</c> did; the reads of <c>.content</c> stay as written.
    /// </summary>
    [Fact]
    public void AForWhoseContentIsRead_ReturnsItsItemsThroughAStrippingNode()
    {
        var result = Migrated("composer-sources");

        var loop = Json(result, "nodes/topic_loop/index.json");
        Assert.Equal("node.topic_loop.topic_gate_stripped", (string?)loop["ret"]);
        Assert.Equal(["topic_gate", "topic_gate_stripped"], loop["nodes"]!.AsArray().Select(n => (string?)n));
        Assert.Equal(Compact("""{ "type": "array", "items": { "$ref": "markdown_block" } }"""), loop["format"]!.ToJsonString());

        var (header, body) = Md(result, "nodes/topic_loop/topic_gate_stripped.md");
        Assert.Equal(SourceShapes.NodeRendered.Members.Select(m => m.Name), header.Select(m => m.Key));
        Assert.Equal(("rendered", "computed", "Drafted topics only", "markdown_block"),
            ((string?)header["kind"], (string?)header["producer"], (string?)header["label"], (string?)header["format"]));
        Assert.Null(header["description"]);
        Assert.Equal(Compact("""{ "===": [ { "var": "node.topic_gate.state" }, "completed" ] }"""), header["if"]!.ToJsonString());
        Assert.Equal("{{ node.topic_gate.content | string.strip }}", body);
        Assert.Equal("node.topic_loop.topic_gate", result.StripNodes["node.topic_loop.topic_gate_stripped"]);

        const string notice = "Loop 'topic_loop' returns each item through the new node 'topic_gate_stripped', which strips the item's text as specVersion 30 did.";
        Assert.Single(result.Notices, n => n == notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-088", notice, ""));

        // The reads of the loop's .content are as written; the new node has no 30 id.
        Assert.Contains("{{ node.topic_loop.content }}", Md(result, "nodes/opening.md").Body, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Keys.Values, k => k.EndsWith("_stripped", StringComparison.Ordinal));
    }

    /// <summary>
    /// #1510 (F-D1, F-D2): a loop over a loop strips the inner loop's rendering, which is Markdown whatever its items' format,
    /// so the outer loop's items are lists no more but the new node's text; the inner loop, whose <c>.content</c> the new node
    /// reads, strips its own items too, as 30 trimmed both.
    /// </summary>
    [Fact]
    public void ALoopOverALoop_StripsAtEachLevel()
    {
        var result = Migrated("composer-sources");

        var outer = Json(result, "nodes/section_loop/index.json");
        Assert.Equal("node.section_loop.point_loop_stripped", (string?)outer["ret"]);
        Assert.Equal(Compact("""{ "type": "array", "items": { "$ref": "markdown_block" } }"""), outer["format"]!.ToJsonString());
        var (header, body) = Md(result, "nodes/section_loop/point_loop_stripped.md");
        Assert.Equal(("Points", "markdown_block"), ((string?)header["label"], (string?)header["format"]));
        Assert.Equal("{{ node.point_loop.content | string.strip }}", body);

        var inner = Json(result, "nodes/section_loop/point_loop/index.json");
        Assert.Equal("node.section_loop.point_loop.draft_point_stripped", (string?)inner["ret"]);
        Assert.Equal("{{ node.draft_point.content | string.strip }}", Md(result, "nodes/section_loop/point_loop/draft_point_stripped.md").Body);
    }

    /// <summary>#1510 (F-D1): a loop whose <c>.content</c> nothing reads (read only through <c>.items</c>) keeps its <c>ret</c>.</summary>
    [Fact]
    public void AForWhoseContentNobodyReads_IsLeftAsItWas()
    {
        var result = Migrated("example-prior-letters", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single()["sources"] = new JsonArray("node.assessment"));

        Assert.Contains("node.letters.items", Md(result, "nodes/assessment.md").Body, StringComparison.Ordinal);
        var loop = Json(result, "nodes/letters/index.json");
        Assert.Equal("node.letters.letter_summary", (string?)loop["ret"]);
        Assert.DoesNotContain(result.Files.Keys, f => f.EndsWith("_stripped.md", StringComparison.Ordinal));
        Assert.Empty(result.StripNodes);
        Assert.DoesNotContain(result.Notices, n => n.StartsWith("Loop 'letters' returns", StringComparison.Ordinal));
    }

    /// <summary>#1510 (F-D2): the new node's id is fitted and numbered as a classifier's is, when a node already has it.</summary>
    [Fact]
    public void AStrippingNodesId_AvoidsATakenId()
    {
        var result = Migrated("composer-sources", root =>
        {
            // A body node of that id, which the topic's draft reads.
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "topic_gate_stripped", ["label"] = "A taken id", ["format"] = "prose" });
            Node(root, "topic_loop")["nodes"]!.AsArray().Insert(0, "topic_gate_stripped");
        }, files =>
        {
            files["prompts/topic_gate_stripped.md"] = "Write one line on the topic \"{{ item.value.title }}\".\n";
            files["prompts/draft_topic.md"] += "\n{{ node.topic_gate_stripped.content }}\n";
        });

        var loop = Json(result, "nodes/topic_loop/index.json");
        Assert.Equal("node.topic_loop.topic_gate_stripped_2", (string?)loop["ret"]);
        Assert.Equal(["topic_gate_stripped", "topic_gate", "topic_gate_stripped_2"], loop["nodes"]!.AsArray().Select(n => (string?)n));
        Assert.Equal("{{ node.topic_gate.content | string.strip }}", Md(result, "nodes/topic_loop/topic_gate_stripped_2.md").Body);
        Assert.Contains("Renamed 'topic_gate_stripped' to 'topic_gate_stripped_2'; 'topic_gate_stripped' was already taken.", result.Notices);
        Assert.Contains("Loop 'topic_loop' returns each item through the new node 'topic_gate_stripped_2', which strips the item's text as specVersion 30 did.", result.Notices);
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
