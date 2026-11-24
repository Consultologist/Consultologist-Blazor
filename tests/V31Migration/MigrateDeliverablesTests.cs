using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2 the deliverable rows, §§ 1.4, 1.10; part 06 §§ 6.1, 6.3 to 6.6, 7, 8, 10;
/// D3, D4, D7, D8): the deliverables pass. Each 30 deliverable becomes <c>deliverables/&lt;id&gt;.md</c> with part 06
/// § 6.1's header and the composition template D8 names, one test per composer rule of the fixture README's
/// <c>composer-*</c> sections (whether a template gives 30's bytes is Task 15's to measure). A deliverable macro with no
/// condition is a snippet read; one with a condition is a hand edit written in a form the 31 checker refuses, as is a
/// placed macro's read of another deliverable. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class MigrateDeliverablesTests
{
    private static readonly JsonSerializerOptions Unescaped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Header(Migration31Result result, string path) => MigrateNodesTests.Md(result, path).Header.ToJsonString(Unescaped);

    private static string Template(Migration31Result result, string id) => MigrateNodesTests.Md(result, $"deliverables/{id}.md").Body;

    private static void AssertHandEdit(Migration31Result result, string file, string member, string id, string reason)
    {
        var edit = $"Hand edit needed in {file}, {member}: {reason}";
        Assert.Single(result.HandEdits, e => e == edit);
        Assert.Contains(edit, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, file));
        V31Contract.AssertSentence(new SourceRefusal(id, reason, ""));
    }

    private static string Conditioned(string macro) =>
        $"macro '{macro}' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.";

    /// <summary>The refusals of a 31 package whose deliverable <c>note</c> has <paramref name="body"/>, the macro <c>review_note</c> and the snippet <c>echo</c> (<paramref name="snippet"/>) beside it.</summary>
    private static IReadOnlyList<SourceRefusal> RefusalsOfDeliverable(string body, string snippet = "Seen with Margaret Okafor.\n") =>
        TemplateFixture.RefusalsOf(TemplateFixture.Package()
            .WithMacro("review_note", "Seen again for review.\n")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.review_note }}\n")
            .WithSnippet("echo", snippet, "markdown_block")
            .WithDeliverable(body));

    [Fact]
    public void ADeliverable_BecomesItsFile()
    {
        var gates = MigrateNodesTests.Migrated("composer-gates");

        // Part 06 § 6.1's members, in the Deliverable shape's order: no if, the gate as check.<id>, whenEmpty's default.
        Assert.Equal(
            """{"label":"Clinic note","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":"check.note_coverage","whenEmpty":"release"}""",
            Header(gates, "deliverables/note.md"));

        // The if converted (part 10 § 2), ungated: check null.
        Assert.Equal(
            """{"label":"Patient copy","kind":"rendered","description":null,"format":"markdown_block","if":{"===":[{"var":"input.audience.value"},"patient"]},"check":null,"whenEmpty":"release"}""",
            Header(gates, "deliverables/patient_copy.md"));

        // The root delivery folder lists them in 30's order (part 06 § 8; no destination folder, as area 08's fixtures).
        Assert.Equal("""{"description":null,"items":["note","record_copy","patient_copy","cover_letter"],"delivery":[],"bundle":null}""",
            MigrateNodesTests.Json(gates, "deliverables/index.json").ToJsonString());

        // Keys: each deliverable by its 30 key; a node of the same id keeps its own entry (composer-sources' plan).
        Assert.Equal("deliverable.note", gates.Keys["deliverable.note"]);
        var sources = MigrateNodesTests.Migrated("composer-sources");
        Assert.Equal("deliverable.plan", sources.Keys["deliverable.plan"]);
        Assert.Equal("node.plan", sources.Keys["plan"]);

        // A renamed deliverable (§ 1.9) keeps its 30 key in Keys, its value the 31 key.
        var renamed = MigrateNodesTests.Migrated("composer-gates", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "cover_letter")["id"] = "value");
        Assert.Equal("deliverable.value_deliverable", renamed.Keys["deliverable.value"]);
        Assert.True(renamed.Files.ContainsKey("deliverables/value_deliverable.md"));

        // § 1.10, D7: whenEmpty from the options; the hand-chosen examples.
        Assert.Equal("hold", (string?)MigrateNodesTests.Md(MigrateNodesTests.Migrated("test/general"), "deliverables/consult.md").Header["whenEmpty"]);
        Assert.Equal("fail", (string?)MigrateNodesTests.Md(MigrateNodesTests.Migrated("example-nested-note"), "deliverables/note.md").Header["whenEmpty"]);

        // An if the converter refuses (string.upcase has no JSON Logic form; string.strip compared with "" is blank since
        // fix round 1 of Task 12, part 05 § 7) stays 30's text with one hand edit, through the one condition helper.
        var refused = MigrateNodesTests.Migrated("composer-gates", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "patient_copy")["if"] = "(input.consult_draft | string.upcase) == \"\"");
        Assert.Equal("(input.consult_draft | string.upcase) == \"\"", (string?)MigrateNodesTests.Md(refused, "deliverables/patient_copy.md").Header["if"]);
        var edit = Assert.Single(refused.HandEdits, e => e.StartsWith("Hand edit needed in deliverables/patient_copy.md, if: ", StringComparison.Ordinal));
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "deliverables/patient_copy.md"));

        // Written so, the 31 checker refuses the if until the edit is made: a JSON string is no condition.
        var refusal = Assert.Single(TemplateFixture.RefusalsOf(TemplateFixture.Package().With("deliverables/note.md", SourceFixture.Md(
            "{ \"label\": \"Consultation note\", \"kind\": \"rendered\", \"description\": null, \"format\": \"markdown_block\", \"if\": "
            + MigrateNodesTests.Md(refused, "deliverables/patient_copy.md").Header["if"]!.ToJsonString() + ", \"check\": null, \"whenEmpty\": \"release\" }",
            "{{ input.consult_draft.content }}"))));
        Assert.Equal(("S03-025", "deliverables/note.md"), (refusal.Id, refusal.Path));
        V31Contract.AssertSentence(refusal);
    }

    [Theory]
    [InlineData("single", "{{ node.opening.content }}")]                                     // one scalar source
    [InlineData("pair", "{{ node.opening.content }}\n\n{{ node.closing.content }}")]          // two, one blank line between
    [InlineData("topics", "{{ node.topic_loop.content }}")]                                   // a for: its .content, 30's blocks
    [InlineData("sections", "{{ node.section_loop.content }}")]                               // a for with a for inside
    [InlineData("summary", "{{ node.refine.content }}")]                                      // a while
    [InlineData("plan", "{{ node.plan_gate.content }}")]                                      // an if
    [InlineData("tone_line", "{{ node.tone_case.content }}")]                                 // a case
    [InlineData("tally", "{{ node.topic_tally.content }}")]                                   // a let of type text
    [InlineData("summary_then_opening", "{{ node.refine.content }}\n\n{{ node.opening.content }}")]
    [InlineData("topics_then_opening", "{{ node.topic_loop.content }}\n\n{{ node.opening.content }}")]
    public void EachSource_IsReadAsItsContent_InSourcesOrder(string deliverable, string template)
    {
        var result = MigrateNodesTests.Migrated("composer-sources");

        Assert.Equal(template, Template(result, deliverable));
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(result, $"deliverables/{deliverable}.md").Header["format"]);
    }

    [Fact]
    public void ASignature_IsTheProfilesSignatureAfterABlankLine()
    {
        var result = MigrateNodesTests.Migrated("composer-macros");

        Assert.Equal("{{ node.letter_body.content }}\n\n{{ profile.signature }}", Template(result, "signed_only"));
        // After an appended macro: the signature is last.
        Assert.Equal("{{ node.letter_body.content }}\n\n{{ snippet.cc_line.content }}\n\n{{ profile.signature }}", Template(result, "signed_letter"));
        Assert.DoesNotContain(result.HandEdits, e => e.Contains("deliverables/signed", StringComparison.Ordinal));
    }

    [Fact]
    public void UnconditionalMacros_AreSnippetReads_InPlacementOrder()
    {
        var result = MigrateNodesTests.Migrated("composer-macros");

        // D4: a prepend before the sources, an append after them, each where 30 placed it.
        Assert.Equal("{{ snippet.letterhead.content }}\n\n{{ node.letter_body.content }}", Template(result, "prepended"));
        Assert.Equal("{{ node.letter_body.content }}\n\n{{ snippet.closing_line.content }}", Template(result, "appended"));
        Assert.Equal(
            "{{ snippet.letterhead.content }}\n\n{{ snippet.reference_line.content }}\n\n{{ node.letter_body.content }}\n\n"
            + "{{ snippet.closing_line.content }}\n\n{{ snippet.second_closing.content }}",
            Template(result, "both"));
        // A deliverable's own if does not make its macros conditional (review_summary's letterhead).
        Assert.Equal("{{ snippet.letterhead.content }}\n\n{{ node.letter_body.content }}", Template(result, "review_summary"));
        Assert.Equal("{{ node.section_loop.content }}", Template(result, "sections_note"));
        Assert.Equal("Riverside Clinic (fictional)\n", MigrateNodesTests.Md(result, "snippets/letterhead.md").Body);
        Assert.DoesNotContain(result.HandEdits, e => e.Contains("deliverables/both.md", StringComparison.Ordinal) || e.Contains("deliverables/prepended.md", StringComparison.Ordinal));

        // A snippet read in a deliverable is one 31 accepts.
        Assert.Empty(RefusalsOfDeliverable("{{ snippet.echo.content }}\n\n{{ input.consult_draft.content }}"));

        // A deliverable macro that stays a macro (a prompt also places it) is read as a snippet written from its body, its
        // macro file kept (D4): example-macros' kept_note.
        var placed = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["prompts/summary.md"] += "{{ macro.kept_note.prompt }}\n");
        Assert.Contains("\n\n{{ snippet.kept_note.content }}\n\n", Template(placed, "consult_note"), StringComparison.Ordinal);
        Assert.True(placed.Files.ContainsKey("macros/kept_note.md"));
        Assert.Equal("""{"label":"Kept note","description":null,"if":null,"format":"markdown_block"}""", Header(placed, "snippets/kept_note.md"));
        Assert.Equal(MigrateNodesTests.Md(placed, "macros/kept_note.md").Body, MigrateNodesTests.Md(placed, "snippets/kept_note.md").Body);
        Assert.Single(MigrateNodesTests.Json(placed, "snippets/index.json")["items"]!.AsArray(), i => (string?)i == "kept_note");
    }

    [Fact]
    public void ADeliverableMacroWithACondition_IsAHandEdit()
    {
        var result = MigrateNodesTests.Migrated("composer-macros");

        // A macro if (new_visit_note), a placement if (review_note); review_leaflet and closing_line have none.
        Assert.Equal(
            "{{ node.letter_body.content }}\n\n{{ macro.new_visit_note.fixed_text }}\n\n{{ macro.review_note.fixed_text }}\n\n"
            + "{{ snippet.review_leaflet.content }}\n\n{{ snippet.closing_line.content }}",
            Template(result, "gated"));
        AssertHandEdit(result, "deliverables/gated.md", "body", "A10-002", Conditioned("new_visit_note"));
        AssertHandEdit(result, "deliverables/gated.md", "body", "A10-002", Conditioned("review_note"));

        // Optional macros, default on and default off.
        Assert.Equal("{{ node.letter_body.content }}\n\n{{ macro.followup_offer.fixed_text }}\n\n{{ macro.leaflet_offer.fixed_text }}", Template(result, "optional"));
        AssertHandEdit(result, "deliverables/optional.md", "body", "A10-002", Conditioned("followup_offer"));
        AssertHandEdit(result, "deliverables/optional.md", "body", "A10-002", Conditioned("leaflet_offer"));

        // The macro file stays for the operator (its if, its choice input).
        Assert.True(result.Files.ContainsKey("macros/review_note.md"));
        Assert.True(result.Files.ContainsKey("macros/followup_offer.md"));

        // example-macros: a placement if (header_line, prepended), a macro if and a placement if (followup_only), a
        // macro if (brief_plan), an optional macro (follow_up_reminder); closing is fixed text.
        var macros = MigrateNodesTests.Migrated("example-macros");
        var consult = Template(macros, "consult_note");
        Assert.StartsWith("{{ macro.header_line.fixed_text }}\n\n{{ node.requested_sections.content }}\n\n", consult, StringComparison.Ordinal);
        Assert.EndsWith("{{ macro.followup_only.fixed_text }}\n\n{{ macro.brief_plan.fixed_text }}\n\n{{ macro.follow_up_reminder.fixed_text }}", consult, StringComparison.Ordinal);
        foreach (var macro in new[] { "header_line", "followup_only", "brief_plan", "follow_up_reminder" })
            AssertHandEdit(macros, "deliverables/consult_note.md", "body", "A10-002", Conditioned(macro));
        Assert.Contains("\n\n{{ snippet.closing.content }}\n\n", consult, StringComparison.Ordinal);

        // Written so, the 31 checker refuses the template until the edit is made (D3: a hand edit has a refusal behind it).
        // S05-047: a deliverable places no macro.
        var refusal = Assert.Single(RefusalsOfDeliverable("{{ input.consult_draft.content }}\n\n{{ macro.review_note.fixed_text }}"));
        Assert.Equal(("S05-047", "deliverables/note.md"), (refusal.Id, refusal.Path));
        V31Contract.AssertSentence(refusal);
        Assert.Empty(RefusalsOfDeliverable("{{ input.consult_draft.content }}"));
    }

    [Fact]
    public void TheTestMacro_OfTestGeneral_BecomesASnippetReadAtTheEnd()
    {
        var result = MigrateNodesTests.Migrated("test/general");

        // D6: the macro test, fixed text appended, is the snippet test_snippet, read last.
        Assert.Equal("{{ node.standards.content }}\n\n{{ snippet.test_snippet.content }}", Template(result, "consult"));
        Assert.True(result.Files.ContainsKey("snippets/test_snippet.md"));
        Assert.False(result.Files.ContainsKey("macros/test.md"));
        Assert.Equal(
            """{"label":"Consultation note","kind":"rendered","description":null,"format":"markdown_block","if":null,"check":null,"whenEmpty":"hold"}""",
            Header(result, "deliverables/consult.md"));
    }

    [Fact]
    public void ADeliverableThatReadsAnotherDeliverable_IsAHandEdit()
    {
        // composer-macros' quoted_review (optional) reads deliverable.review_summary.content: the placement is A10-002, and
        // the macro body's read, which part 06 § 6.3 refuses, is A10-039 on the macro file.
        var result = MigrateNodesTests.Migrated("composer-macros");
        Assert.EndsWith("{{ macro.quoted_review.fixed_text }}", Template(result, "echoes"), StringComparison.Ordinal);
        AssertHandEdit(result, "deliverables/echoes.md", "body", "A10-002", Conditioned("quoted_review"));
        AssertHandEdit(result, "macros/quoted_review.md", "body", "A10-039",
            "the template reads deliverable.review_summary.content at line 1, which a 31 macro body doesn't read; rewrite it.");
        Assert.Contains("[{{ deliverable.review_summary.content }}]", MigrateNodesTests.Md(result, "macros/quoted_review.md").Body, StringComparison.Ordinal);

        // Not optional, it is fixed text: a snippet the deliverable reads, the read left where it is, said on the snippet.
        var fixedText = MigrateNodesTests.Migrated("composer-macros", _ => { }, files =>
            files["macros/quoted_review.md"] = files["macros/quoted_review.md"].Replace("  \"optional\": true,\n  \"default\": false\n", "  \"optional\": false\n", StringComparison.Ordinal));
        Assert.EndsWith("{{ snippet.quoted_review.content }}", Template(fixedText, "echoes"), StringComparison.Ordinal);
        AssertHandEdit(fixedText, "snippets/quoted_review.md", "body", "A10-039",
            "the template reads deliverable.review_summary.content at line 1, which a 31 deliverable doesn't read; rewrite it.");
        var line = MigrateNodesTests.Md(fixedText, "snippets/quoted_review.md").Body;
        Assert.Contains("[{{ deliverable.review_summary.content }}]", line, StringComparison.Ordinal);

        // Both are refused by the 31 checker: the snippet placed in a deliverable (S05-022), and the macro body (S05-020).
        var inSnippet = Assert.Single(RefusalsOfDeliverable("{{ input.consult_draft.content }}\n\n{{ snippet.echo.content }}", line));
        Assert.Equal(("S05-022", "deliverables/note.md"), (inSnippet.Id, inSnippet.Path));
        var inMacro = Assert.Single(TemplateFixture.RefusalsOf(TemplateFixture.Package()
            .WithMacro("review_note", MigrateNodesTests.Md(result, "macros/quoted_review.md").Body.Replace("review_summary", "note", StringComparison.Ordinal))
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.review_note }}\n")));
        Assert.Equal(("S05-020", "macros/review_note.md"), (inMacro.Id, inMacro.Path));
    }

    [Fact]
    public void ADeliverableMacroThatIsNoSingleMacro_Is30sToRefuse()
    {
        // A placement naming no single macro (a folder's item, a folder) would be A10-047 at 31; 30 refuses it first, so no
        // package reaches that path. This pins 30's refusal.
        foreach (var id in new[] { "notes/red_flags", "notes" })
        {
            var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-macros");
            var root = JsonNode.Parse(json)!.AsObject();
            root["deliverables"]![0]!["macros"]!.AsArray().Add(new JsonObject { ["id"] = id, ["at"] = "append" });
            var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options("example-macros"));
            Assert.False(result.Succeeded);
            Assert.Contains(result.Refusals, r => r.StartsWith("At specVersion 30: Deliverable 'consult_note' macro '" + id + "'", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ASnippetCopy_TakesAFreeId_AndCarriesTheMacroBodysHandEdits()
    {
        // example-macros-warnings' kept_note has a body if (A10-035); placed by a prompt too, it stays a macro, and its
        // deliverable reads a copy of its body, which holds the same text: the edit is said on each file.
        var warned = MigrateNodesTests.Migrated("example-macros-warnings", _ => { }, files => files["prompts/summary.md"] += "{{ macro.kept_note.prompt }}\n");
        var onMacro = Assert.Single(warned.HandEdits, e => e.StartsWith("Hand edit needed in macros/kept_note.md, body: ", StringComparison.Ordinal));
        Assert.Single(warned.HandEdits, e => e == onMacro.Replace("macros/kept_note.md", "snippets/kept_note.md", StringComparison.Ordinal));
        Assert.Equal(MigrateNodesTests.Md(warned, "macros/kept_note.md").Body, MigrateNodesTests.Md(warned, "snippets/kept_note.md").Body);

        // A snippet that already has the macro's id keeps it; the copy takes <id>_snippet, said with A10-048.
        var taken = MigrateNodesTests.Migrated("example-macros",
            root => root["snippets"]!.AsArray().Add((JsonNode)"kept_note"),
            files =>
            {
                files["prompts/summary.md"] += "{{ macro.kept_note.prompt }}\n{{ snippet.kept_note.content }}\n";
                files["snippets/kept_note.md"] = "{\n  \"label\": \"Kept snippet\"\n}\nA snippet of Dr A. Sample's own.\n";
            });
        Assert.Contains("\n\n{{ snippet.kept_note_snippet.content }}\n\n", Template(taken, "consult_note"), StringComparison.Ordinal);
        Assert.Equal("A snippet of Dr A. Sample's own.\n", MigrateNodesTests.Md(taken, "snippets/kept_note.md").Body);
        Assert.Equal(MigrateNodesTests.Md(taken, "macros/kept_note.md").Body, MigrateNodesTests.Md(taken, "snippets/kept_note_snippet.md").Body);
        var notice = "Macro 'kept_note' is read on deliverables as the snippet 'kept_note_snippet': a snippet 'kept_note' already exists.";
        Assert.Contains(notice, taken.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-048", notice, ""));
    }

    [Theory]
    [MemberData(nameof(MigrateLayoutTests.Packages), MemberType = typeof(MigrateLayoutTests))]
    public void EveryDeliverableFile_PassesItsShape(string package)
    {
        var result = MigrateNodesTests.Migrated(package);
        var (json30, _) = ByteEquivalenceFixtures.ReadPackage(package);
        var ids = JsonNode.Parse(json30)!["deliverables"]!.AsArray().Select(d => (string)d!["id"]!).ToList();
        var refusals = new List<SourceRefusal>();

        var index = MigrateNodesTests.Json(result, "deliverables/index.json");
        SourceMembers.Check(index, SourceShapes.DeliverablesIndex, SourceShapes.DeliverablesIndex.Name, "deliverables/index.json", refusals);
        Assert.Equal(SourceShapes.DeliverablesIndex.Members.Select(s => s.Name), index.Select(m => m.Key));
        Assert.Equal(ids, index["items"]!.AsArray().Select(i => (string)i!));

        foreach (var id in ids)
        {
            var path = $"deliverables/{id}.md";
            var (header, body) = MigrateNodesTests.Md(result, path);
            SourceMembers.Check(header, SourceShapes.Deliverable, SourceShapes.Deliverable.Name, path, refusals);
            Assert.Equal(SourceShapes.Deliverable.Members.Select(s => s.Name), header.Select(m => m.Key));
            Assert.Equal("rendered", (string?)header["kind"]);
            Assert.Equal("markdown_block", (string?)header["format"]);
            Assert.Contains((string?)header["whenEmpty"], new[] { "release", "hold", "fail" });
            Assert.True(header["check"] is null || ((string?)header["check"])!.StartsWith("check.", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(body), $"{path}: the template is empty.");
            Assert.Equal($"deliverable.{id}", result.Keys[$"deliverable.{id}"]);
        }

        Assert.Equal(ids.Count, result.Files.Keys.Count(k => k.StartsWith("deliverables/", StringComparison.Ordinal) && k.EndsWith(".md", StringComparison.Ordinal)));
        Assert.Empty(refusals);
    }

    /// <summary>
    /// Part 05 § 8.3 (the macro's format follows its placers'), with R9: the macros <c>example-macros</c>' <c>summary</c>
    /// places (in its prompt, or through the snippet <c>style</c>) take its format, <c>markdown_block</c> while
    /// <c>consult_note</c> composes it, <c>plaintext</c> when only a prompt reads it.
    /// </summary>
    [Fact]
    public void AMacroPlacedOnAComposedNode_TakesItsFormat()
    {
        string[] placedBySummary = ["preamble", "sign_off", "wrap_up", "house_style"];
        var composed = MigrateNodesTests.Migrated("example-macros");
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(composed, "nodes/summary.md").Header["format"]);
        foreach (var macro in placedBySummary)
            Assert.True("markdown_block" == (string?)MigrateNodesTests.Md(composed, $"macros/{macro}.md").Header["format"], $"{macro} is not markdown_block.");

        // 30 refuses a node that feeds no deliverable, so the patient line's prompt reads the summary instead.
        var uncomposed = MigrateNodesTests.Migrated("example-macros", root =>
            root["deliverables"]![0]!["sources"]!.AsArray().Remove(root["deliverables"]![0]!["sources"]!.AsArray().Single(s => (string?)s == "node.summary")),
            files => files["prompts/patient_line.md"] += "\n{{ node.summary.content }}\n");
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(uncomposed, "nodes/summary.md").Header["format"]);
        foreach (var macro in placedBySummary)
            Assert.True("plaintext" == (string?)MigrateNodesTests.Md(uncomposed, $"macros/{macro}.md").Header["format"], $"{macro} is not plaintext.");

        // A macro placed by a composed agent and an uncomposed one is plaintext (escaped in the Markdown agent): counselling,
        // placed by standing_draft (composed by consult_note) and patient_line, once patient_letter no longer composes it.
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(composed, "macros/counselling.md").Header["format"]);
        var mixed = MigrateNodesTests.Migrated("example-macros", root =>
            root["deliverables"]!.AsArray().Remove(root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "patient_letter")),
            files => files["prompts/summary.md"] += "\n{{ node.patient_line.content }}\n");
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(mixed, "nodes/standing_sections/standing_draft.md").Header["format"]);
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(mixed, "nodes/patient_line.md").Header["format"]);
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(mixed, "macros/counselling.md").Header["format"]);
    }

    /// <summary>
    /// D20 gives no union of composed and uncomposed prose: 30 composes only a top-level node (a source inside a flow is
    /// refused, "name node.&lt;owner&gt;"), so a flow is composed with every leaf its <c>ret</c>s reach, or none is.
    /// <c>composer-sources</c>' <c>tone_case</c> is one <c>markdown_block</c> while <c>tone_line</c> composes it, one
    /// <c>plaintext</c> when only a prompt reads it, never an <c>anyOf</c>; both publish with no refusal but the package's two hand edits.
    /// </summary>
    [Fact]
    public void AFlowOfComposedAndUncomposedProse_IsNotAUnion()
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var inside = JsonNode.Parse(json)!.AsObject();
        inside["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "single")["sources"] = new JsonArray("node.formal_line");
        var refused = WorkflowPackageMigration30To31.Migrate(inside.ToJsonString(), files, MigrationFixture.Options("composer-sources"));
        Assert.False(refused.Succeeded);
        Assert.Contains(refused.Refusals, r => r.StartsWith("At specVersion 30: ", StringComparison.Ordinal) && r.Contains("source node.formal_line runs inside", StringComparison.Ordinal));

        var composed = MigrateNodesTests.Migrated("composer-sources");
        // 30 refuses a node that feeds no deliverable, so the opening's prompt reads the case instead.
        var uncomposed = MigrateNodesTests.Migrated("composer-sources", root =>
            root["deliverables"]!.AsArray().Remove(root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "tone_line")),
            files => files["prompts/opening.md"] += "\n{{ node.tone_case.content }}\n");
        foreach (var (result, format) in new[] { (composed, "markdown_block"), (uncomposed, "plaintext") })
        {
            Assert.Equal(JsonValue.Create(format).ToJsonString(), MigrateNodesTests.Json(result, "nodes/tone_case/index.json")["format"]!.ToJsonString());
            Assert.Equal(format, (string?)MigrateNodesTests.Md(result, "nodes/tone_case/formal_line.md").Header["format"]);
            Assert.Equal(format, (string?)MigrateNodesTests.Md(result, "nodes/tone_case/plain_line.md").Header["format"]);
            // The package's two hand edits (topic_tally's expression, draft_summary's body if) are refused at publish where
            // they sit; nothing else is, the case and its leaves least of all.
            Assert.All(result.HandEdits, e => Assert.Matches("^Hand edit needed in nodes/(topic_tally.md, expression|refine/draft_summary.md, body): ", e));
            Assert.All(MigratedPackagesTests.Publish(result), r => Assert.True(r.Path is "nodes/topic_tally.md" or "nodes/refine/draft_summary.md", $"{r.Id} {r.Path}: {r.Sentence}"));
        }
    }

    /// <summary>
    /// D20 with an <c>if</c> or <c>case</c> whose leaves mix 30 prose with a <c>let</c> of <c>text</c>, a <c>rendered</c>
    /// node or <c>agent_plaintext</c> (each stays <c>plaintext</c>): composer-sources' <c>tone_case</c> with
    /// <c>plain_line</c> made one. 30 refuses a deliverable that names such a flow as its source (its branches disagree; "a
    /// deliverable composes prose"). Read only by a prompt, the flow composes nothing, its prose leaf stays <c>plaintext</c>
    /// and its format is the one <c>plaintext</c>; publish refuses nothing but the package's two hand edits. A loop or a
    /// while over such a flow is <see cref="AComposedLoopOrWhileOverMixedBranches_IsAHandEdit"/>.
    /// </summary>
    [Theory]
    [InlineData("let")]
    [InlineData("rendered")]
    [InlineData("agent_plaintext")]
    public void AFlowMixingProseWithALetOrARenderedLeaf_IsNeverAUnion(string kind)
    {
        var (json, read) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var options = MigrateNodesTests.WithAgentPlaintext("composer-sources");

        var composedRoot = JsonNode.Parse(json)!.AsObject();
        var composedFiles = new Dictionary<string, string>(read, StringComparer.Ordinal);
        MakePlainLeaf(composedRoot, composedFiles, "plain_line", kind);
        var refused = WorkflowPackageMigration30To31.Migrate(composedRoot.ToJsonString(), composedFiles, options);
        Assert.False(refused.Succeeded);
        Assert.Contains(refused.Refusals, r => r.StartsWith("At specVersion 30: Deliverable 'tone_line' source node.tone_case returns ", StringComparison.Ordinal)
            && r.EndsWith("; a deliverable composes prose.", StringComparison.Ordinal));

        var root = JsonNode.Parse(json)!.AsObject();
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        MakePlainLeaf(root, files, "plain_line", kind);
        root["deliverables"]!.AsArray().Remove(root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "tone_line"));
        files["prompts/opening.md"] += "\n{{ node.tone_case.content }}\n";
        var promptOnly = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, options);
        Assert.True(promptOnly.Succeeded, string.Join("\n", promptOnly.Refusals));

        Assert.Equal("\"plaintext\"", MigrateNodesTests.Json(promptOnly, "nodes/tone_case/index.json")["format"]!.ToJsonString());
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(promptOnly, "nodes/tone_case/formal_line.md").Header["format"]);
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(promptOnly, "nodes/tone_case/plain_line.md").Header["format"]);
        Assert.Equal(KindOf(kind), (string?)MigrateNodesTests.Md(promptOnly, "nodes/tone_case/plain_line.md").Header["kind"]);
        Assert.All(promptOnly.HandEdits, e => Assert.Matches("^Hand edit needed in nodes/(topic_tally.md, expression|refine/draft_summary.md, body): ", e));
        Assert.All(MigratedPackagesTests.Publish(promptOnly), r => Assert.True(r.Path is "nodes/topic_tally.md" or "nodes/refine/draft_summary.md", $"{r.Id} {r.Path}: {r.Sentence}"));
    }

    /// <summary>
    /// R9, D20 (fix round 2 of Task 21): 30 does accept a deliverable composing a <c>for</c> or a <c>while</c> whose
    /// <c>ret</c> is an <c>if</c> mixing 30 prose with a <c>let</c> of text, a <c>rendered</c> node or <c>agent_plaintext</c>
    /// (only a flow named as a source is held to one format). The <c>if</c> would be <c>markdown_block | plaintext</c>, a
    /// union the <c>markdown_block</c> deliverable escapes as one string, the prose's Markdown with it, so it is a hand edit
    /// (A10-053): the <c>if</c> is written <c>markdown_block</c>, which publish refuses against the union its branches give
    /// (S02-015), and the loop or while keeps the format derived from the branches. composer-sources without its two hand
    /// edits (<c>refine</c>, <c>topic_tally</c>), so the edit is the package's only one.
    /// </summary>
    [Theory]
    [InlineData("for", "let")]
    [InlineData("for", "rendered")]
    [InlineData("for", "agent_plaintext")]
    [InlineData("while", "let")]
    [InlineData("while", "rendered")]
    [InlineData("while", "agent_plaintext")]
    public void AComposedLoopOrWhileOverMixedBranches_IsAHandEdit(string shape, string kind)
    {
        var (json, read) = ByteEquivalenceFixtures.ReadPackage("composer-sources");
        var root = JsonNode.Parse(json)!.AsObject();
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        foreach (var id in new[] { "refine", "draft_summary", "judge", "topic_tally" })
            root["nodes"]!.AsArray().Remove(MigrateNodesTests.Node(root, id));
        foreach (var deliverable in root["deliverables"]!.AsArray().OfType<JsonObject>().Where(d => (string?)d["id"] is "summary" or "tally" or "summary_then_opening").ToList())
            root["deliverables"]!.AsArray().Remove(deliverable);

        string gate, flow, prose;
        root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "left_out", ["label"] = "A left-out line" });
        MakePlainLeaf(root, files, "left_out", kind);
        if (shape == "for")
        {
            // topic_loop's if gains an else returning the plain-text leaf.
            MigrateNodesTests.Node(root, "topic_gate")["else"] = new JsonObject { ["nodes"] = new JsonArray("left_out"), ["ret"] = "node.left_out" };
            (gate, flow, prose) = ("nodes/topic_loop/topic_gate/index.json", "nodes/topic_loop/index.json", "nodes/topic_loop/topic_gate/draft_topic.md");
        }
        else
        {
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "redo", ["label"] = "Redoing the line", ["while"] = "input.redraft == true", ["checkFirst"] = true, ["max"] = 2,
                ["nodes"] = new JsonArray("redo_gate"), ["ret"] = "node.redo_gate",
            });
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "redo_gate", ["label"] = "Drafted or left out", ["if"] = "input.include_plan == true",
                ["then"] = new JsonObject { ["nodes"] = new JsonArray("redo_line"), ["ret"] = "node.redo_line" },
                ["else"] = new JsonObject { ["nodes"] = new JsonArray("left_out"), ["ret"] = "node.left_out" },
            });
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "redo_line", ["label"] = "Redrafting the line", ["format"] = "prose" });
            files["prompts/redo_line.md"] = "Write one line from this draft.\n{{ input.consult_draft }}\nTopics so far: {{ node.topic_loop.content }}\n";
            root["deliverables"]!.AsArray().Add(new JsonObject { ["id"] = "redo", ["label"] = "A while over an if", ["sources"] = new JsonArray("node.redo") });
            (gate, flow, prose) = ("nodes/redo/redo_gate/index.json", "nodes/redo/index.json", "nodes/redo/redo_gate/redo_line.md");
        }

        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrateNodesTests.WithAgentPlaintext("composer-sources"));
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));

        const string union = """{"anyOf":[{"$ref":"markdown_block"},{"$ref":"plaintext"}]}""";
        Assert.Equal("markdown_block", (string?)MigrateNodesTests.Md(result, prose).Header["format"]);
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(result, gate.Replace("index.json", "left_out.md", StringComparison.Ordinal)).Header["format"]);
        Assert.Equal("\"markdown_block\"", MigrateNodesTests.Json(result, gate)["format"]!.ToJsonString());
        Assert.Equal(shape == "for" ? $$"""{"type":"array","items":{{union}}}""" : union, MigrateNodesTests.Json(result, flow)["format"]!.ToJsonString());

        var reason = $"the if '{(shape == "for" ? "topic_gate" : "redo_gate")}' that a deliverable composes returns Markdown from one branch and plain text from another, "
            + "which the deliverable would escape as one string; give its branches one text format, then write that format here and on each flow that returns this one.";
        AssertHandEdit(result, gate, "format", "A10-053", reason);
        Assert.Single(result.HandEdits);
        Assert.Contains(MigratedPackagesTests.Publish(result), r => r.Id == "S02-015" && r.Path == gate);
    }

    /// <summary>The 30 node <paramref name="id"/> of <paramref name="root"/> made a plain-text leaf: a <c>let</c> of text, a <c>rendered</c> node or an <c>agent_plaintext</c> agent.</summary>
    private static void MakePlainLeaf(JsonObject root, Dictionary<string, string> files, string id, string kind)
    {
        var leaf = MigrateNodesTests.Node(root, id);
        leaf.Remove("format");
        switch (kind)
        {
            case "let":
                leaf["let"] = "\"A plain line.\"";
                leaf["type"] = "text";
                leaf["onInvalid"] = "fail";
                files.Remove($"prompts/{id}.md");
                break;
            case "rendered":
                leaf["format"] = "rendered";
                files[$"prompts/{id}.md"] = "A plain line for {{ input.consult_draft }}.\n";
                break;
            default:
                leaf["format"] = "agent_plaintext";
                files[$"prompts/{id}.md"] = "Write one plain line from this draft.\n{{ input.consult_draft }}\n";
                break;
        }
    }

    private static string KindOf(string kind) => kind switch { "let" => "computed", "rendered" => "rendered", _ => "agent" };
}
