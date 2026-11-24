using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the node rows; part 02 §§ 1, 11; part 05 § 1.2): the nodes pass's leaves.
/// A node's kind follows the member it has; its format is the plain format of the same shape; an agent node's prompt
/// members are written with 30's values or 30's defaults; a <c>terms_subset</c> node gets no file and is read at
/// <c>check.&lt;id&gt;</c>; every condition and <c>let</c> is converted, the ones the converter refuses kept as 30 wrote
/// them with one hand edit; each <c>blank</c> conversion and each <c>size</c> of a list that can be null is said; every
/// node file has its shape.
/// </summary>
public class MigrateNodesTests
{
    internal static Migration31Result Migrated(string package)
    {
        var result = MigrationFixture.Migrate(package);
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        return result;
    }

    /// <summary><paramref name="package"/> with its manifest reshaped and its files changed, migrated with its options.</summary>
    internal static Migration31Result Migrated(string package, Action<JsonObject> reshape, Action<Dictionary<string, string>>? change = null)
    {
        var (json, read) = ByteEquivalenceFixtures.ReadPackage(package);
        var root = JsonNode.Parse(json)!.AsObject();
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        reshape(root);
        change?.Invoke(files);
        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options(package));
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        return result;
    }

    /// <summary>The 30 node <paramref name="id"/> of <paramref name="root"/>.</summary>
    internal static JsonObject Node(JsonObject root, string id) =>
        root["nodes"]!.AsArray().OfType<JsonObject>().Single(n => (string?)n["id"] == id);

    internal static (JsonObject Header, string Body) Md(Migration31Result result, string path)
    {
        Assert.True(result.Files.ContainsKey(path), $"{path} was not written.");
        var split = FrontMatter.Split(result.Files[path]);
        Assert.NotNull(split);
        return (JsonNode.Parse(split!.Header)!.AsObject(), split.Body);
    }

    internal static JsonObject Json(Migration31Result result, string path)
    {
        Assert.True(result.Files.ContainsKey(path), $"{path} was not written.");
        return JsonNode.Parse(result.Files[path])!.AsObject();
    }

    internal static string Compact(string json) => JsonNode.Parse(json)!.ToJsonString();

    [Theory]
    [InlineData("example-two-documents", "nodes/extract_patient_concepts.md", "concept_list")] // attested
    [InlineData("example-nested-note", "nodes/plan.md", "section_plan")]                       // custom
    [InlineData("example-classifier-scope", "nodes/scope.md", "classification")]               // attested
    public void AFormatOfAnAttestedOrCustomProducer_IsAnAgentNode(string package, string path, string format)
    {
        var (header, _) = Md(Migrated(package), path);

        Assert.Equal("agent", (string?)header["kind"]);
        Assert.Equal("generated", (string?)header["producer"]);
        Assert.Equal(format, (string?)header["format"]);
        Assert.Equal(
            ["kind", "producer", "label", "description", "format", "reproducible", "modelDerived", "runDependent", "profileCarrying",
             "if", "onInvalid", "failIfEmpty", "parts", "ordered"],
            header.Select(m => m.Key));
    }

    [Fact]
    public void TheRenderedFormat_IsARenderedNode_OfATextFormat_ItsTemplateKept()
    {
        var (_, files) = ByteEquivalenceFixtures.ReadPackage("example-v12-constructs");
        var (header, body) = Md(Migrated("example-v12-constructs"), "nodes/patient_header.md");

        Assert.Equal(Compact("""
            { "kind": "rendered", "producer": "computed", "label": "Patient header", "description": null, "format": "plaintext",
              "reproducible": true, "modelDerived": false, "runDependent": false, "profileCarrying": false, "if": null }
            """), header.ToJsonString());
        // The templates pass rewrites its reads (a date input is read as its value); the rest is 30's template.
        Assert.Equal(files["prompts/patient_header.md"].Replace("{{ input.seen_on }}", "{{ input.seen_on.value }}", StringComparison.Ordinal), body);
    }

    [Fact]
    public void ALet_IsAComputedNode_ItsExpressionConverted_ItsBodyEmpty()
    {
        var (header, body) = Md(Migrated("example-nested-note"), "nodes/section_count.md");

        Assert.Equal(Compact("""
            { "kind": "computed", "producer": "computed", "label": "Counting the sections", "description": null, "format": "number",
              "reproducible": true, "modelDerived": true, "runDependent": false, "profileCarrying": false, "if": null,
              "expression": { "size": [ { "var": "node.plan.value.sections" } ] } }
            """), header.ToJsonString());
        Assert.Equal("", body);
    }

    [Theory]
    [InlineData("text", "\"Planned sections\"", "plaintext")]
    [InlineData("number", "(node.plan.value.sections | array.size) + 1", "number")]
    [InlineData("boolean", "(node.plan.value.sections | array.size) > 1", "boolean")]
    [InlineData("verdict", "{ verdict: \"good\", feedback: \"none\" }", "verdict")]
    public void ALetsFormat_ComesFromItsType(string type, string let, string format)
    {
        var result = Migrated("example-nested-note", root =>
        {
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "tally", ["label"] = "A tally", ["let"] = let, ["type"] = type, ["onInvalid"] = "fail",
            });
        }, files => files["prompts/draft_summary.md"] += type == "text" ? "\n{{ node.tally.content }}\n" : "\n{{ node.tally.value }}\n");

        var (header, _) = Md(result, "nodes/tally.md");
        Assert.Equal("computed", (string?)header["kind"]);
        Assert.Equal(format, (string?)header["format"]);
        // Converted, not left as 30's text (D3's hand-edit form, a JSON string holding the let as written); a text let's
        // logic is a JSON string too, without 30's quotes.
        Assert.NotEqual(JsonValue.Create(let).ToJsonString(), header["expression"]!.ToJsonString());
        Assert.Empty(result.HandEdits);
    }

    [Theory]
    [InlineData("prose", "plaintext")]
    [InlineData("agent_plaintext", "plaintext")]
    [InlineData("agent_markdown_block", "markdown_block")]
    [InlineData("agent_markdown_inline", "markdown_inline")]
    [InlineData("classification", "classification")]
    [InlineData("concept_list", "concept_list")]
    [InlineData("section_plan", "section_plan")]
    public void AnAgentsFormat_IsThePlainFormatOfTheSameShape(string at30, string at31) =>
        Assert.Equal(at31, PackageFormat.V31.Migration.Migrate.AgentFormat(at30));

    [Fact]
    public void Prose_IsPlaintext_InAMigratedNode()
    {
        var (header, _) = Md(Migrated("example-nested-note"), "nodes/sections/paragraphs/draft_paragraph.md");

        Assert.Equal("plaintext", (string?)header["format"]);
    }

    [Fact]
    public void FailIfEmpty_Keeps30sSentence_OrIsNull()
    {
        var result = Migrated("example-two-documents");

        Assert.Equal("No valid disease or problem concept was identified.", (string?)Md(result, "nodes/identify_problem.md").Header["failIfEmpty"]);
        var (none, _) = Md(result, "nodes/draft_patient_letter.md");
        Assert.True(none.ContainsKey("failIfEmpty"));
        Assert.Null(none["failIfEmpty"]);
    }

    [Fact]
    public void OnInvalid_IsKept_DefaultsToFail_AndFlagBecomesFailWithANotice()
    {
        var result = Migrated("example-nested-note", root =>
        {
            Node(root, "judge")["onInvalid"] = "flag";
            Node(root, "outline")["onInvalid"] = "retry";
        });

        Assert.Equal("fail", (string?)Md(result, "nodes/plan.md").Header["onInvalid"]);                      // kept
        Assert.Equal("fail", (string?)Md(result, "nodes/sections/paragraphs/draft_paragraph.md").Header["onInvalid"]); // none: 30's default
        Assert.Equal("retry", (string?)Md(result, "nodes/sections/outline.md").Header["onInvalid"]);             // kept
        Assert.Equal("fail", (string?)Md(result, "nodes/refine/judge.md").Header["onInvalid"]);               // flag

        const string notice = "Node 'judge' kept a reply that did not fit its format (onInvalid: flag); 31 fails it instead.";
        Assert.Equal([notice], result.Notices.Where(n => n.StartsWith("Node 'judge'", StringComparison.Ordinal)));
        Assert.Empty(result.HandEdits);
        V31Contract.AssertSentence(new SourceRefusal("A10-013", notice, ""));
    }

    [Fact]
    public void AnAgentsPartsAndOrdered_AreWritten_ForTheTemplatesPass()
    {
        var (header, _) = Md(Migrated("example-macros"), "nodes/summary.md");

        Assert.True(header.ContainsKey("parts"));
        Assert.Null(header["parts"]);
        Assert.False(header["ordered"]!.GetValue<bool>());
    }

    [Fact]
    public void AClassifiersWith_IsDroppedWithANotice()
    {
        var result = Migrated("example-classifier-scope");

        var (header, _) = Md(result, "nodes/scope.md");
        Assert.False(header.ContainsKey("with"));
        const string notice = "Node 'scope' declared with at 30, which 31 has no member for; it was dropped.";
        Assert.Contains(notice, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-030", notice, ""));
    }

    [Fact]
    public void AMemberANonAgentNodeHasNoPlaceFor_IsDroppedWithANotice()
    {
        // D16: a let's onInvalid (a computed node has none) and a rendered node's failIfEmpty are dropped, each said.
        var let = Migrated("example-nested-note");
        Assert.False(Md(let, "nodes/section_count.md").Header.ContainsKey("onInvalid"));
        const string notice = "Node 'section_count' declared onInvalid at 30, which 31 has no member for; it was dropped.";
        Assert.Contains(notice, let.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-030", notice, ""));

        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-v12-constructs");
        var root = JsonNode.Parse(json)!.AsObject();
        Node(root, "patient_header")["failIfEmpty"] = "The header came back empty.";
        var rendered = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options("example-v12-constructs"));
        // 30 refuses failIfEmpty on a rendered node (no reply to judge), and on a let, so no failIfEmpty reaches 31 to drop;
        // the pass still drops one and says so (A10-030) should a package carry it.
        Assert.False(rendered.Succeeded);
        Assert.Contains(rendered.Refusals, r => r.Contains("patient_header", StringComparison.Ordinal) && r.Contains("failIfEmpty", StringComparison.Ordinal));

        // An agent's onInvalid and failIfEmpty are kept, and not said.
        Assert.DoesNotContain(Migrated("example-two-documents").Notices, n => n.Contains("declared onInvalid", StringComparison.Ordinal) || n.Contains("declared failIfEmpty", StringComparison.Ordinal));
    }

    [Fact]
    public void ATermsSubsetNode_GetsNoFile_AndIsReadAtCheck()
    {
        var result = Migrated("example-v12-constructs");

        Assert.DoesNotContain(result.Files.Keys, k => k.StartsWith("nodes/", StringComparison.Ordinal) && k.Contains("coverage", StringComparison.Ordinal));
        Assert.DoesNotContain("note_coverage", Json(result, "nodes/index.json")["items"]!.AsArray().Select(i => (string?)i));
        Assert.Equal("check.note_coverage", result.Keys["note_coverage"]);
        Assert.Equal("check.letter_coverage", result.Keys["letter_coverage"]);
    }

    [Fact]
    public void EveryNode_IsKeyedByItsRootedKey()
    {
        var keys = Migrated("example-conditional-sections").Keys;

        Assert.Equal("node.coverage", keys["coverage"]);
        Assert.Equal("node.sections_draft", keys["sections_draft"]);
        Assert.Equal("node.sections_draft.route", keys["route"]);
        Assert.Equal("node.sections_draft.route.narrative_draft", keys["narrative_draft"]);
        Assert.Equal("node.patient_summaries.narrative_only.patient_summary", keys["patient_summary"]);
    }

    [Fact]
    public void TheScope_KnowsEveryNodesRead_AndEachLoopsItem()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-v12-constructs");
        var m = Read(json, files, "example-v12-constructs");

        Assert.Equal("node.tone", m.NodeReads["tone"].Path);
        Assert.Equal("\"classification\"", m.NodeReads["tone"].Format.ToJsonString());
        Assert.True(m.NodeReads["tone"].AlwaysValued);
        Assert.Equal("check.note_coverage", m.NodeReads["note_coverage"].Path);
        Assert.Equal("\"check_result\"", m.NodeReads["note_coverage"].Format.ToJsonString());

        // A body node reads the folder's item; a top-level node no item.
        var inLoop = m.Scope("letter_body");
        Assert.True(inLoop.ItemIsFolderItem);
        Assert.Equal("\"standards_item\"", inLoop.Item!.ToJsonString());
        Assert.Null(m.Scope("tone").Item);

        // An array input's element, and a node list's: the loop's item.value.
        var (sectionsJson, sectionsFiles) = ByteEquivalenceFixtures.ReadPackage("example-conditional-sections");
        var sections = Read(sectionsJson, sectionsFiles, "example-conditional-sections");
        Assert.Equal("object", (string?)sections.Scope("route").Item!["type"]);
        Assert.Contains("kind", sections.Scope("route").Item!["properties"]!.AsObject().Select(p => p.Key));
        Assert.False(sections.NodeReads["route"].AlwaysValued);   // it gained an empty branch
        Assert.True(sections.NodeReads["sections_draft"].AlwaysValued);
        Assert.False(sections.NodeReads["narrative_only"].AlwaysValued);

        var (nestedJson, nestedFiles) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        var nested = Read(nestedJson, nestedFiles, "example-nested-note");
        Assert.Equal("node.outline", nested.NodeReads["outline"].Path);
        Assert.Contains("point", nested.Scope("draft_paragraph").Item!["properties"]!.AsObject().Select(p => p.Key));
        Assert.True(nested.Scope("refine").InWhile);
        Assert.True(nested.Scope("judge").InWhile);
        Assert.False(nested.Scope("plan").InWhile);
    }

    private static Migrated Read(string json, Dictionary<string, string> files, string package)
    {
        var (m, refusals) = PackageFormat.V31.Migration.Migrated.Read(json, files, MigrationFixture.Options(package));
        Assert.Empty(refusals);
        PackageFormat.V31.Migration.Migrate.Layout(m!);
        PackageFormat.V31.Migration.Migrate.Inputs(m!);
        PackageFormat.V31.Migration.Migrate.Nodes(m!);
        return m!;
    }

    [Fact]
    public void AConditionTheConverterRefuses_IsAHandEdit()
    {
        var result = Migrated("composer-sources");

        var (header, _) = Md(result, "nodes/topic_tally.md");
        const string text = "\"Topic section length: \" | string.append (node.topic_loop.content | string.size)";
        Assert.Equal(JsonValue.Create(text)!.ToJsonString(), header["expression"]!.ToJsonString());
        var edit = Assert.Single(result.HandEdits, e => e.Contains("nodes/topic_tally.md", StringComparison.Ordinal));
        Assert.Equal(
            $"Hand edit needed in nodes/topic_tally.md, expression: The condition '{text}' uses string.append, which has no JSON Logic form at 31: rewrite it by hand. The condition '{text}' uses string.size, which has no JSON Logic form at 31: rewrite it by hand.",
            edit);
        Assert.Contains(edit, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "nodes/topic_tally.md"));
    }

    [Fact]
    public void AFlowConditionTheConverterRefuses_StaysAString_WithOneHandEdit()
    {
        var result = Migrated("composer-sources", root => Node(root, "plan_gate")["if"] = "input.include_plan == true && profile.name == \"x\"");

        var header = Json(result, "nodes/plan_gate/index.json");
        Assert.Equal(JsonValue.Create("input.include_plan == true && profile.name == \"x\"")!.ToJsonString(), header["if"]!.ToJsonString());
        var edit = Assert.Single(result.HandEdits, e => e.Contains("plan_gate", StringComparison.Ordinal));
        Assert.StartsWith("Hand edit needed in nodes/plan_gate/index.json, if: ", edit, StringComparison.Ordinal);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "nodes/plan_gate/index.json"));
    }

    [Fact]
    public void ANotOnText_IsListed()
    {
        // 30 refuses ! on a text read, but not on a pipe it cannot type; at 31 the pipe is trim, a text.
        var result = Migrated("composer-sources", root => Node(root, "plan_gate")["if"] = "!(input.consult_draft | string.strip)");

        Assert.Equal(Compact("""{ "blank": [ { "trim": [ { "var": "input.consult_draft.content" } ] } ] }"""),
            Json(result, "nodes/plan_gate/index.json")["if"]!.ToJsonString());
        const string notice = "nodes/plan_gate/index.json, if: '!(input.consult_draft | string.strip)' became blank(trim(input.consult_draft.content)); it now also holds for empty and white-space-only text.";
        Assert.Equal([notice], result.Report.BlankConversions);
        Assert.Contains(notice, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-008", notice, ""));
    }

    [Theory]
    [InlineData("(node.plan_gate.content | string.strip) == \"\"", """{"blank":[{"var":"node.plan_gate.content"}]}""",
        "deliverables/single.md, if: 'trim(node.plan_gate.content) = \"\"' became blank(node.plan_gate.content); it now also holds when node.plan_gate.content is null.")]
    [InlineData("(node.plan_gate.content | string.strip) != \"\"", """{"!":[{"blank":[{"var":"node.plan_gate.content"}]}]}""",
        "deliverables/single.md, if: 'trim(node.plan_gate.content) ≠ \"\"' became not blank(node.plan_gate.content); it now no longer holds when node.plan_gate.content is null.")]
    [InlineData("(input.consult_draft | string.strip) == \"\"", """{"blank":[{"var":"input.consult_draft.content"}]}""", null)]
    public void ATrimComparedWithEmptyText_IsBlank_AndListedWhenItCanBeNull(string condition, string logic, string? notice)
    {
        // Part 05 § 7: a trimmed text compared with "" is blank. A node that did not run (plan_gate, an if whose test
        // failed) is null at 31, which blank holds, where 30 compared it as not empty: an unruled difference, said (A10-050)
        // and listed beside A10-008. A text input not given read "" at 30, so its form answers as before, unsaid.
        var result = Migrated("composer-sources", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "single")["if"] = condition);

        Assert.Equal(logic, Md(result, "deliverables/single.md").Header["if"]!.ToJsonString());
        Assert.DoesNotContain(result.HandEdits, e => e.Contains("deliverables/single.md", StringComparison.Ordinal));
        if (notice is null)
        {
            Assert.Empty(result.Report.BlankConversions);
            return;
        }

        Assert.Equal(notice, Assert.Single(result.Report.BlankConversions));
        Assert.Contains(notice, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-050", notice, ""));
    }

    [Fact]
    public void ATrimmedFieldOfAnInput_ComparedWithEmptyText_IsListed()
    {
        // Only a text input read whole read "" at 30 when not given. A field of an object input is null at 30 when it or
        // its object is absent (an unsupplied object input is an empty object), where 30's comparison is false and
        // blank(null) is true: said (A10-050), an unruled difference for the owner.
        var result = Migrated("example-structured-intake", root =>
        {
            var patient = root["inputs"]!.AsArray().OfType<JsonObject>().Single(i => (string?)i["id"] == "patient");
            patient["fields"]!.AsArray().Add(new JsonObject { ["id"] = "name", ["label"] = "Name", ["type"] = "text" });
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "note")["if"] = "(input.patient.name | string.strip) == \"\"";
        });

        Assert.Equal("""{"blank":[{"var":"input.patient.value.name"}]}""", Md(result, "deliverables/note.md").Header["if"]!.ToJsonString());
        const string notice = "deliverables/note.md, if: 'trim(input.patient.value.name) = \"\"' became blank(input.patient.value.name); it now also holds when input.patient.value.name is null.";
        Assert.Equal(notice, Assert.Single(result.Report.BlankConversions));
        Assert.Contains(notice, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-050", notice, ""));
    }

    [Fact]
    public void ASizeOfAListThatCanBeNull_IsSaid()
    {
        // The Task 4 ruling: an optional list input absent at 30 counted 0; 31's size(null) is null.
        var result = Migrated("composer-sources", root =>
        {
            root["inputs"]!.AsArray().OfType<JsonObject>().Single(i => (string?)i["id"] == "topics")["required"] = false;
            Node(root, "plan_gate")["if"] = "(input.topics | array.size) > 0";
        });

        const string notice = "nodes/plan_gate/index.json, if: size(input.topics.value) is null when input.topics.value is null, where 30 counted 0.";
        Assert.Contains(notice, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-027", notice, ""));

        // A list that always has a value is not said: section_count's plan always completes, and its sections are required.
        Assert.DoesNotContain(Migrated("example-nested-note").Notices, n => n.Contains("size(", StringComparison.Ordinal));
    }

    [Fact]
    public void ASingleSnippetsIf_IsConverted_OrAHandEdit()
    {
        static Migration31Result WithIf(string condition) => Migrated("example-macros", _ => { }, files =>
            files["snippets/style.md"] = files["snippets/style.md"].Replace(
                "  \"label\": \"House style\"\n", $"  \"label\": \"House style\",\n  \"if\": {JsonSerializer.Serialize(condition)}\n"));

        var converted = WithIf("input.include_counselling == true");
        Assert.Equal(Compact("""{ "===": [ { "var": "input.include_counselling.value" }, true ] }"""),
            Md(converted, "snippets/style.md").Header["if"]!.ToJsonString());
        Assert.DoesNotContain(converted.HandEdits, e => e.Contains("snippets/style.md", StringComparison.Ordinal));

        var refused = WithIf("input.include_counselling == true && snippet.style.content != \"\"");
        Assert.Equal(JsonValue.Create("input.include_counselling == true && snippet.style.content != \"\"")!.ToJsonString(),
            Md(refused, "snippets/style.md").Header["if"]!.ToJsonString());
        var edit = Assert.Single(refused.HandEdits, e => e.Contains("snippets/style.md", StringComparison.Ordinal));
        Assert.StartsWith("Hand edit needed in snippets/style.md, if: The condition ", edit, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MigrateLayoutTests.Packages), MemberType = typeof(MigrateLayoutTests))]
    public void EveryNodeFile_HasItsShape(string package)
    {
        var result = Migrated(package);
        var refusals = new List<SourceRefusal>();
        var checkedFiles = 0;

        foreach (var (path, text) in result.Files.Where(f => f.Key.StartsWith("nodes/", StringComparison.Ordinal) && f.Key != "nodes/index.json"))
        {
            var md = path.EndsWith(".md", StringComparison.Ordinal);
            var declaration = md ? JsonNode.Parse(FrontMatter.Split(text)!.Header)!.AsObject() : JsonNode.Parse(text)!.AsObject();
            var shape = ((string?)declaration["kind"]) switch
            {
                "agent" => SourceShapes.NodeAgent,
                "rendered" => SourceShapes.NodeRendered,
                "computed" => SourceShapes.NodeComputed,
                "for" => SourceShapes.FlowFor,
                "while" => SourceShapes.FlowWhile,
                "if" => SourceShapes.FlowIf,
                "case" => SourceShapes.FlowCase,
                var other => throw new Xunit.Sdk.XunitException($"{path}: kind {other}"),
            };
            Assert.Equal(md, shape != SourceShapes.FlowFor && shape != SourceShapes.FlowWhile && shape != SourceShapes.FlowIf && shape != SourceShapes.FlowCase);
            SourceMembers.Check(declaration, shape, shape.Name, path, refusals);
            Assert.Equal(shape.Members.Select(s => s.Name), declaration.Select(d => d.Key));
            checkedFiles++;
        }

        Assert.True(checkedFiles > 0);
        Assert.Empty(refusals);
    }

    [Theory]
    [MemberData(nameof(MigrateLayoutTests.Packages), MemberType = typeof(MigrateLayoutTests))]
    public void EveryNodeOf30_IsWrittenOnce_OrIsACheck(string package)
    {
        var (json, _) = ByteEquivalenceFixtures.ReadPackage(package);
        var result = Migrated(package);

        foreach (var node in JsonNode.Parse(json)!["nodes"]!.AsArray().OfType<JsonObject>())
        {
            var id = (string)node["id"]!;
            var key = result.Keys[id];
            if (key.StartsWith("check.", StringComparison.Ordinal))
                continue;
            var path = "nodes/" + string.Join('/', key.Split('.').Skip(1));
            Assert.True(result.Files.ContainsKey(path + ".md") ^ result.Files.ContainsKey(path + "/index.json"), $"{package}: {id}");
        }
    }
}
