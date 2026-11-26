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

    /// <summary>
    /// #1507 (Task 15; D16, part 10 § 3.1): 30's engine appended its classifier trailer to the rendered prompt
    /// (<c>ClassificationOutputContract.Trailer</c>, the <c>with</c> values in order); 31 drops <c>with</c> (A10-030) and
    /// appends nothing, so the migration writes the trailer at the end of the body, and the prompt keeps 30's bytes.
    /// </summary>
    [Fact]
    public void AClassifiersPrompt_Ends_WithThe30Trailer()
    {
        var result = Migrated("example-classifier-scope");

        var (_, body) = Md(result, "nodes/scope.md");
        Assert.EndsWith(
            "{{ input.consult_draft.content }}\n"
            + Consultologist.Api.Workflow.ClassificationOutputContract.Trailer(["in_scope", "out_of_scope", "needs_information"]),
            body,
            StringComparison.Ordinal);
        Assert.Contains("Node 'scope' declared with at 30, which 31 has no member for; its values are written at the end of its prompt, as 30's engine sent them.", result.Notices);

        // Only a classifier's: a prose node's prompt is 30's text.
        Assert.DoesNotContain("Answer with exactly one of", Md(result, "nodes/plan_sections/plan_section.md").Body, StringComparison.Ordinal);
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

    /// <summary>
    /// R9 (#1507, 2026-11-22T16:26Z, item 2) and D20 (accepted 2026-11-22T21:38Z): a 30 prose value a deliverable composes
    /// is <c>markdown_block</c>, followed from each source through the flows' <c>ret</c>s to every leaf. In
    /// <c>composer-sources</c>: a scalar source (<c>opening</c>, <c>closing</c>); a leaf inside a <c>for</c> and its
    /// <c>if</c> (<c>topic_loop</c>), a <c>for</c> inside a <c>for</c> (<c>section_loop</c>), a <c>while</c>
    /// (<c>refine</c>, whose leaf the judge's prompt also reads), an <c>if</c> (<c>plan_gate</c>) and a <c>case</c>'s
    /// branches (<c>tone_case</c>). Each flow's format follows its leaves. An agent node with no format would be prose too
    /// (<see cref="Migrate.AgentFormat"/>), but 30 refuses one since specVersion 26, so no package reaches the migration
    /// with it: that refusal is pinned here.
    /// </summary>
    [Fact]
    public void ProseADeliverableComposes_IsMarkdownBlock()
    {
        var result = Migrated("composer-sources");

        foreach (var leaf in new[]
        {
            "nodes/opening.md", "nodes/closing.md", "nodes/topic_loop/topic_gate/draft_topic.md",
            "nodes/section_loop/point_loop/draft_point.md", "nodes/refine/draft_summary.md", "nodes/plan_gate/draft_plan.md",
            "nodes/tone_case/formal_line.md", "nodes/tone_case/plain_line.md",
        })
        {
            Assert.True("markdown_block" == (string?)Md(result, leaf).Header["format"], $"{leaf} is not markdown_block.");
        }

        const string list = """{"type":"array","items":{"$ref":"markdown_block"}}""";
        Assert.Equal(list, Json(result, "nodes/topic_loop/index.json")["format"]!.ToJsonString());
        Assert.Equal("markdown_block", (string?)Json(result, "nodes/topic_loop/topic_gate/index.json")["format"]);
        Assert.Equal(list, Json(result, "nodes/section_loop/point_loop/index.json")["format"]!.ToJsonString());
        Assert.Equal("markdown_block", (string?)Json(result, "nodes/refine/index.json")["format"]);
        Assert.Equal("markdown_block", (string?)Json(result, "nodes/plan_gate/index.json")["format"]);
        Assert.Equal("markdown_block", (string?)Json(result, "nodes/tone_case/index.json")["format"]);
        Assert.Equal("markdown_block", PackageFormat.V31.Migration.Migrate.AgentFormat(null, composed: true));
        // The package's two hand edits are 30 constructs a format does not touch.
        Assert.All(result.HandEdits, e => Assert.Matches("^Hand edit needed in nodes/(topic_tally.md, expression|refine/draft_summary.md, body): ", e));

        var (json, files) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var root = JsonNode.Parse(json)!.AsObject();
        Node(root, "closing").Remove("format");
        var refused = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options("composer-sources"));
        Assert.False(refused.Succeeded);
        Assert.Contains("At specVersion 30: Node 'closing' must declare a format at specVersion 26.", refused.Refusals);
    }

    /// <summary>
    /// <paramref name="package"/>'s options with a 30 catalogue that also has <c>agent_plaintext</c> (prose's entry under that
    /// name): 30's catalogue here has none.
    /// </summary>
    internal static Migration31Options WithAgentPlaintext(string package) => MigrationFixture.Options(package) with
    {
        Formats30 = TestFormats.Catalog with
        {
            Formats = new Dictionary<string, WorkflowFormatCatalogFormat>(TestFormats.Catalog.Formats, StringComparer.Ordinal)
            {
                ["agent_plaintext"] = TestFormats.Catalog.Formats["prose"],
            },
        },
    };

    /// <summary>R9: prose that only prompts read stays <c>plaintext</c> (<c>example-two-documents</c>' section drafts, in the loop's body but not its <c>ret</c>, read by the next prompt).</summary>
    [Fact]
    public void ProseOnlyPromptsRead_StaysPlaintext()
    {
        var result = Migrated("example-two-documents");

        Assert.Equal("plaintext", (string?)Md(result, "nodes/standards/standard_section_draft.md").Header["format"]);
        Assert.Equal("plaintext", (string?)Md(result, "nodes/standards/patient_section_draft.md").Header["format"]);
        Assert.Equal("markdown_block", (string?)Md(result, "nodes/standards/section_instructions.md").Header["format"]); // the loop's ret
        Assert.Equal("markdown_block", (string?)Md(result, "nodes/draft_patient_letter.md").Header["format"]);           // a source
    }

    /// <summary>
    /// D20: only 30's prose (and an agent node with no format) changes. <c>agent_plaintext</c> was declared plain text by its
    /// author, a <c>rendered</c> node and a <c>let</c> of <c>text</c> are 30's text: each stays <c>plaintext</c> though a
    /// deliverable composes it (and is escaped there, part 06 § 6.4). 30's catalogue here has no <c>agent_plaintext</c>, so
    /// the test gives it one, <c>prose</c>'s entry under that name.
    /// </summary>
    [Fact]
    public void AgentPlaintext_ARenderedNode_AndATextLet_StayPlaintext_EvenWhenComposed()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var root = JsonNode.Parse(json)!.AsObject();
        Node(root, "opening")["format"] = "agent_plaintext";
        var sources = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, WithAgentPlaintext("composer-sources"));
        Assert.True(sources.Succeeded, string.Join("\n", sources.Refusals));
        Assert.Equal("plaintext", (string?)Md(sources, "nodes/opening.md").Header["format"]);       // deliverable single
        Assert.Equal("markdown_block", (string?)Md(sources, "nodes/closing.md").Header["format"]);  // its prose neighbour in pair
        Assert.Equal("plaintext", (string?)Md(sources, "nodes/topic_tally.md").Header["format"]);   // deliverable tally, a let of text

        var constructs = Migrated("example-v12-constructs");
        Assert.Equal("plaintext", (string?)Md(constructs, "nodes/patient_header.md").Header["format"]);                    // consult, a source
        Assert.Equal("plaintext", (string?)Md(constructs, "nodes/standards_letter_body/letter_body.md").Header["format"]); // ack_letter, through a for
        Assert.Equal("markdown_block", (string?)Md(constructs, "nodes/standards_section_instructions/section_instructions.md").Header["format"]);
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

    /// <summary>
    /// #1508 (comment 5, item 3): <c>flag</c>, which kept a reply that did not fit, is <c>retry</c> at 31: one more attempt
    /// with the validation error, then the node fails. A10-013 is still said (#1447's notification lists it).
    /// </summary>
    [Fact]
    public void OnInvalidFlag_BecomesRetry()
    {
        var result = Migrated("example-nested-note", root =>
        {
            Node(root, "judge")["onInvalid"] = "flag";
            Node(root, "outline")["onInvalid"] = "retry";
        });

        Assert.Equal("fail", (string?)Md(result, "nodes/plan.md").Header["onInvalid"]);                      // kept
        Assert.Equal("fail", (string?)Md(result, "nodes/sections/paragraphs/draft_paragraph.md").Header["onInvalid"]); // none: 30's default
        Assert.Equal("retry", (string?)Md(result, "nodes/sections/outline.md").Header["onInvalid"]);             // kept
        Assert.Equal("retry", (string?)Md(result, "nodes/refine/judge.md").Header["onInvalid"]);              // flag

        const string notice = "Node 'judge' kept a reply that did not fit its format (onInvalid: flag); 31 makes one more attempt with the validation error, then the node fails.";
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
    public void AClassifiersWith_IsWrittenInItsPrompt_WithANotice()
    {
        // D16: 31 has no with; the values are written at the end of the prompt (Task 15), and A10-064 says so, not A10-030.
        var result = Migrated("example-classifier-scope");

        var (header, _) = Md(result, "nodes/scope.md");
        Assert.False(header.ContainsKey("with"));
        const string notice = "Node 'scope' declared with at 30, which 31 has no member for; its values are written at the end of its prompt, as 30's engine sent them.";
        Assert.Contains(notice, result.Notices);
        Assert.DoesNotContain(result.Notices, n => n.StartsWith("Node 'scope' declared with at 30", StringComparison.Ordinal) && n.EndsWith("it was dropped.", StringComparison.Ordinal));
        V31Contract.AssertSentence(new SourceRefusal("A10-064", notice, ""));
    }

    /// <summary>
    /// #1507 (Task 15 fix round 1; Task 12's invariant): a classifier's values are written into its prompt as plain text,
    /// which the dialect never reads as template, because 30 holds every value to snake_case: a value holding <c>{{</c> or a
    /// line ending never reaches the pass, the package being refused at 30 (A10-017), so nothing is written.
    /// </summary>
    [Theory]
    [InlineData("in_{{ scope }}")]
    [InlineData("in\nscope")]
    public void AClassifiersValues_ThatAreNotText_NeverReachThePrompt(string value)
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-classifier-scope");
        var root = JsonNode.Parse(json)!.AsObject();
        Node(root, "scope")["with"]!["values"] = new JsonArray(value, "in_scope", "out_of_scope", "needs_information");

        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options("example-classifier-scope"));

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Refusals, r => r.Contains("must be snake_case", StringComparison.Ordinal));
        Assert.StartsWith("At specVersion 30: ", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Files.Keys, k => k.StartsWith("nodes/", StringComparison.Ordinal));
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
        // failed) is null at 31, which blank holds, where 30 compared it as not empty: an expected difference (#1508), said (A10-050)
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
        // blank(null) is true: said (A10-050), an expected difference (#1508).
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

    /// <summary>
    /// #1508 (comment 5, item 2): 30's <c>string.contains</c> on a list tested whether the list, written as text, held the
    /// text; 31's <c>in</c> on a list is membership, so the converter does not write it and the migration asks for a hand
    /// edit (S10-002, reason A10-073). On text it is still <c>in</c>, the needle first.
    /// </summary>
    [Fact]
    public void StringContainsOverAList_IsAHandEdit()
    {
        var list = Format("""{"type":"array","items":{"$ref":"markdown_block"}}""");
        var scope = new ConversionScope(
            new Dictionary<string, JsonNode>(StringComparer.Ordinal) { ["problems"] = list, ["reason"] = JsonValue.Create("markdown_block") },
            new Dictionary<string, ConversionRead>(StringComparer.Ordinal), null, false, false, MigrationFixture.Formats());

        var called = WorkflowConditionConverter.FromScriban("string.contains input.problems \"asthma\"", scope);
        var piped = WorkflowConditionConverter.FromScriban("input.problems | string.contains \"asthma\"", scope);
        var text = WorkflowConditionConverter.FromScriban("string.contains input.reason \"asthma\"", scope);

        Assert.Null(called.Logic);
        Assert.Equal(["A10-073"], called.NoticeIds);
        const string reason = "The condition 'string.contains input.problems \"asthma\"' uses string.contains on input.problems.value, a list, which 30 tested as a substring of the list written as text; rewrite it by hand.";
        Assert.Equal([reason], called.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-073", reason, ""));
        Assert.Null(piped.Logic);
        Assert.Equal(["A10-073"], piped.NoticeIds);
        Assert.Equal("""{"in":["asthma",{"var":"input.reason.content"}]}""", CanonicalJson.Write(text.Logic));

        // In a migration: the condition stays 30's text, with one hand edit whose reason is A10-073.
        const string condition = "string.contains input.topics \"asthma\"";
        var result = Migrated("composer-sources", root => Node(root, "plan_gate")["if"] = condition);
        Assert.Equal(JsonValue.Create(condition)!.ToJsonString(), Json(result, "nodes/plan_gate/index.json")["if"]!.ToJsonString());
        var edit = Assert.Single(result.HandEdits, e => e.Contains("plan_gate", StringComparison.Ordinal));
        Assert.Equal(
            $"Hand edit needed in nodes/plan_gate/index.json, if: The condition '{condition}' uses string.contains on input.topics.value, a list, which 30 tested as a substring of the list written as text; rewrite it by hand.",
            edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "nodes/plan_gate/index.json"));

        static JsonNode Format(string json) => JsonNode.Parse(json)!;
    }

    /// <summary>
    /// #1508 (comment 5, item 2): in a migration, a size of a list that can be null is written with its null test, so it
    /// answers as 30 did, and A10-027 is still said; text compared with a literal becomes <c>in</c>, said with A10-074.
    /// </summary>
    [Fact]
    public void ANullableSizeAndATextLiteral_AreWrittenAs30Meant_AndSaid()
    {
        var result = Migrated("composer-sources", root =>
        {
            root["inputs"]!.AsArray().OfType<JsonObject>().Single(i => (string?)i["id"] == "topics")["required"] = false;
            Node(root, "plan_gate")["if"] = "(input.topics | array.size) > 0 && input.consult_draft != \"none\"";
        });

        Assert.Equal(
            """{"and":[{"and":[{"!==":[{"var":"input.topics.value"},null]},{">":[{"size":[{"var":"input.topics.value"}]},0]}]},{"!":[{"in":[{"var":"input.consult_draft.content"},["none"]]}]}]}""",
            CanonicalJson.Write(Json(result, "nodes/plan_gate/index.json")["if"]));
        Assert.DoesNotContain(result.HandEdits, e => e.Contains("plan_gate", StringComparison.Ordinal));
        const string size = "nodes/plan_gate/index.json, if: size(input.topics.value) is null when input.topics.value is null, where 30 counted 0.";
        const string literal = "nodes/plan_gate/index.json, if: 'input.consult_draft.content ≠ \"none\"' became not (input.consult_draft.content in [\"none\"]); text is compared with a literal by in at 31.";
        Assert.Contains(size, result.Notices);
        Assert.Contains(literal, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-027", size, ""));
        V31Contract.AssertSentence(new SourceRefusal("A10-074", literal, ""));
    }

    /// <summary>
    /// #1508 (comment 5, item 2; Task 30 fix round 1): the usual "was it given?" gate on a text input, <c>!= ""</c>, is
    /// <c>x !== null and not (x in [""])</c>, said with A10-075, since 31 reads the input not given or blank as null where 30 read <c>""</c>;
    /// another comparison of an input's text with blank text (<c>lower(x) == ""</c>) is a hand edit (A10-076).
    /// </summary>
    [Fact]
    public void ATextInputComparedWithEmptyText_IsNullTested_AndAnyOtherIsAHandEdit()
    {
        var result = Migrated("composer-sources", root => Node(root, "plan_gate")["if"] = "input.consult_draft != \"\"");

        Assert.Equal(
            """{"and":[{"!==":[{"var":"input.consult_draft.content"},null]},{"!":[{"in":[{"var":"input.consult_draft.content"},[""]]}]}]}""",
            CanonicalJson.Write(Json(result, "nodes/plan_gate/index.json")["if"]));
        const string notice = "nodes/plan_gate/index.json, if: 'input.consult_draft.content ≠ \"\"' became input.consult_draft.content ≠ null and not (input.consult_draft.content in [\"\"]); 31 reads an input not given or blank as null, where 30 read \"\".";
        Assert.Contains(notice, result.Notices);
        Assert.DoesNotContain(result.HandEdits, e => e.Contains("plan_gate", StringComparison.Ordinal));
        V31Contract.AssertSentence(new SourceRefusal("A10-075", notice, ""));

        const string lowered = "(string.downcase input.consult_draft) == \"\"";
        var edited = Migrated("composer-sources", root => Node(root, "plan_gate")["if"] = lowered);
        Assert.Equal(JsonValue.Create(lowered)!.ToJsonString(), Json(edited, "nodes/plan_gate/index.json")["if"]!.ToJsonString());
        var edit = Assert.Single(edited.HandEdits, e => e.Contains("plan_gate", StringComparison.Ordinal));
        Assert.Equal(
            $"Hand edit needed in nodes/plan_gate/index.json, if: The condition '{lowered}' compares lower(input.consult_draft.content), read from an input, with blank text, which 30 and 31 read differently when the text is blank or not given; rewrite it by hand.",
            edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "nodes/plan_gate/index.json"));
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
