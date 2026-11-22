using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the <c>terms_subset</c> row; part 06 §§ 1.1, 2; part 03 § 7's migrated
/// check): the checks pass. A 30 <c>terms_subset</c> node becomes <c>checks/&lt;id&gt;.md</c>, a <c>computed</c> check of
/// format <c>check_result</c> whose <c>expression</c> is the operation over its operands' values with its
/// <c>failWith</c> as a literal; it has no file under <c>nodes/</c>, and every reader of <c>node.&lt;id&gt;</c> reads
/// <c>check.&lt;id&gt;</c>: a condition, a template and a deliverable's <c>check</c>.
/// </summary>
public class MigrateChecksTests
{
    private static readonly JsonSerializerOptions Unescaped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public void ATermsSubsetNode_BecomesACheck()
    {
        var result = MigrateNodesTests.Migrated("composer-gates");

        // Part 06 § 1.1's members in the Check shape's order; the expression as part 03 § 7's migrated example writes it.
        var (header, body) = MigrateNodesTests.Md(result, "checks/note_coverage.md");
        Assert.Equal(
            """
            {"kind":"computed","producer":"computed","label":"Note coverage check","description":null,"format":"check_result","reproducible":true,"modelDerived":false,"runDependent":false,"profileCarrying":false,"if":null,"expression":{"terms_subset":[{"var":"node.extract_input_terms.value"},{"var":"node.extract_note_terms.value"},"The note does not cover every clinical term in the draft."]}}
            """,
            header.ToJsonString(Unescaped));
        Assert.Equal(string.Empty, body);
        Assert.Equal(SourceShapes.Check.Members.Select(s => s.Name), header.Select(m => m.Key));
        var refusals = new List<SourceRefusal>();
        SourceMembers.Check(header, SourceShapes.Check, SourceShapes.Check.Name, "checks/note_coverage.md", refusals);
        Assert.Empty(refusals);

        // Listed in 30's order; no file under nodes/, and not in its parent's (the top level's) nodes.
        Assert.Equal("""{"description":null,"items":["note_coverage","copy_coverage"]}""", MigrateNodesTests.Json(result, "checks/index.json").ToJsonString());
        Assert.DoesNotContain(result.Files.Keys, k => k.StartsWith("nodes/", StringComparison.Ordinal) && k.Contains("coverage", StringComparison.Ordinal));
        Assert.DoesNotContain(MigrateNodesTests.Json(result, "nodes/index.json")["items"]!.AsArray(), i => ((string?)i)!.EndsWith("coverage", StringComparison.Ordinal));
        Assert.Equal("check.note_coverage", result.Keys["note_coverage"]);

        // D16: 30's onInvalid has no member on a 31 check; it is dropped with a notice.
        var dropped = "Node 'note_coverage' declared onInvalid at 30, which 31 has no member for; it was dropped.";
        Assert.Contains(dropped, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-030", dropped, ""));

        // The check as written is one the 31 dialect accepts beside the nodes it reads.
        Assert.Empty(TemplateFixture.RefusalsOf(TemplateFixture.Package()
            .WithAgent("extract_input_terms", "List the clinical terms in the draft.\n\n{{ input.consult_draft.content }}\n", "\"concept_list\"")
            .WithAgent("extract_note_terms", "List the clinical terms in the plan.\n\n{{ node.plan.content }}\n", "\"concept_list\"")
            .Listed("checks/index.json", "note_coverage")
            .With("checks/note_coverage.md", SourceFixture.Md(SourceFixture.Node("computed", "Note coverage check", "\"check_result\"", true, true,
                "\"if\": null, \"expression\": " + header["expression"]!.ToJsonString()), ""))));
    }

    [Fact]
    public void ACheckOperandThatIsNoConceptList_Is30sToRefuse()
    {
        // An operand 31 could not type as a concept_list would stay 30's read as text with a hand edit on expression
        // (Migrate.Condition); 30 refuses such an operand first, so no package reaches that path. This pins 30's refusal.
        var (json, files) = Consultologist.Api.Tests.ByteEquivalence.ByteEquivalenceFixtures.ReadPackage("composer-gates");
        var root = JsonNode.Parse(json)!.AsObject();
        MigrateNodesTests.Node(root, "note_coverage")["with"]!["in"] = "node.note_body";
        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options("composer-gates"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Refusals, r => r.StartsWith("At specVersion 30: ", StringComparison.Ordinal) && r.Contains("note_coverage", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryReaderOfTheNode_ReadsTheCheck()
    {
        var result = MigrateNodesTests.Migrated("composer-gates",
            root =>
            {
                // A flow node gated on the check, its body a prose node the cover letter composes.
                var nodes = root["nodes"]!.AsArray();
                nodes.Add(new JsonObject
                {
                    ["id"] = "coverage_gate",
                    ["label"] = "When the note covers the draft",
                    ["if"] = "node.note_coverage.value.pass == true",
                    ["then"] = new JsonObject { ["nodes"] = new JsonArray("covered_line"), ["ret"] = "node.covered_line" },
                });
                nodes.Add(new JsonObject { ["id"] = "covered_line", ["label"] = "Writing the covered line", ["format"] = "prose" });
                root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "cover_letter")["sources"]!.AsArray().Add((JsonNode)"node.coverage_gate");
            },
            files =>
            {
                files["prompts/covered_line.md"] = "Write one line saying the note covers the draft.\n{{ node.note_body.content }}\n";
                files["prompts/patient_line.md"] += "Coverage passed: {{ node.note_coverage.value.pass }}\n";
            });

        // A condition: the converter reads the check where the nodes pass registered it.
        Assert.Equal("""{"===":[{"var":"check.note_coverage.value.pass"},true]}""",
            MigrateNodesTests.Json(result, "nodes/coverage_gate/index.json")["if"]!.ToJsonString());

        // A template: the rewriter's read.
        Assert.EndsWith("Coverage passed: {{ check.note_coverage.value.pass }}\n", MigrateNodesTests.Md(result, "nodes/patient_line.md").Body, StringComparison.Ordinal);

        // A deliverable's check names the check.
        Assert.Equal("check.note_coverage", (string?)MigrateNodesTests.Md(result, "deliverables/note.md").Header["check"]);
        Assert.Equal("check.copy_coverage", (string?)MigrateNodesTests.Md(result, "deliverables/record_copy.md").Header["check"]);
        Assert.Null(MigrateNodesTests.Md(result, "deliverables/cover_letter.md").Header["check"]);

        // No file of the migrated package reads the 30 node any more.
        Assert.DoesNotContain(result.Files, f => f.Value.Contains("node.note_coverage", StringComparison.Ordinal) || f.Value.Contains("node.copy_coverage", StringComparison.Ordinal));
    }
}
