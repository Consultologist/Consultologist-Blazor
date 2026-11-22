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

        // An if the converter refuses stays 30's text with one hand edit, through the one condition helper.
        var refused = MigrateNodesTests.Migrated("composer-gates", root =>
            root["deliverables"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["id"] == "patient_copy")["if"] = "(input.consult_draft | string.strip) == \"\"");
        Assert.Equal("(input.consult_draft | string.strip) == \"\"", (string?)MigrateNodesTests.Md(refused, "deliverables/patient_copy.md").Header["if"]);
        var edit = Assert.Single(refused.HandEdits, e => e.StartsWith("Hand edit needed in deliverables/patient_copy.md, if: ", StringComparison.Ordinal));
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "deliverables/patient_copy.md"));
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
}
