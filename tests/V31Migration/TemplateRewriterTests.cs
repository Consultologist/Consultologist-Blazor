using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.2, § 1.4; part 05 §§ 2, 3, 5.2, 6.3, 7; part 04 § 2.6; R6): one 30 body
/// rewritten into the 31 dialect, each rule on the smallest body that shows it (over a vendored package's working set,
/// read and placed by the passes before the templates pass), then on the named file of <c>vendored/</c>.
/// </summary>
public class TemplateRewriterTests
{
    /// <summary><paramref name="package"/>'s working set after the nodes pass (renames applied), its 30 files changed by <paramref name="change"/>.</summary>
    internal static Migrated Working(string package, Action<Dictionary<string, string>>? change = null)
    {
        var (json, read) = ByteEquivalenceFixtures.ReadPackage(package);
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        change?.Invoke(files);
        var (given, refusals) = Migrated.Read(json, files, MigrationFixture.Options(package));
        Assert.True(given is not null, string.Join("\n", refusals));
        var (m, renameRefusals) = Migrate.Renames.Apply(given!);
        Assert.True(m is not null, string.Join("\n", renameRefusals));
        Migrate.Layout(m!);
        Migrate.Inputs(m!);
        Migrate.Nodes(m!);
        return m!;
    }

    /// <summary>The 31 file of node <paramref name="id"/>: its rooted key as a path.</summary>
    internal static string FileOf(Migrated m, string id) => "nodes/" + m.Keys[id]["node.".Length..].Replace('.', '/') + ".md";

    private static RewrittenBody Prompt(Migrated m, string node, string body, string? itemLabel = null) =>
        TemplateRewriter.Rewrite(body, new TemplateSite30(TemplateSite.Prompt, FileOf(m, node), node, itemLabel), m);

    private static string Body(Migration31Result result, string path) => MigrateNodesTests.Md(result, path).Body;

    private static void AssertHandEdit(string edit, string file, string id, string reason)
    {
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, file));
        Assert.EndsWith(": " + reason, edit, StringComparison.Ordinal);
        V31Contract.AssertSentence(new SourceRefusal(id, reason, ""));
    }

    [Fact]
    public void ABareInputRead_GainsContentOrValue()
    {
        var m = Working("example-structured-intake");

        var rewritten = Prompt(m, "note_section",
            "A {{ input.consult_draft }} B {{ input.length_of_stay }} C {{ input.patient.sex }} D {{ input.prior_notes | array.join \", \" }}\n");

        Assert.Equal(
            "A {{ input.consult_draft.content }} B {{ input.length_of_stay.value }} C {{ input.patient.value.sex }} D {{ input.prior_notes.value | array.join \", \" }}\n",
            rewritten.Text);
        Assert.Empty(rewritten.HandEdits);

        // The vendored file: summary.md's draft, a text input, is read as its content.
        var summary = Body(MigrateNodesTests.Migrated("example-macros"), "nodes/summary.md");
        Assert.EndsWith("Consult draft:\n{{ input.consult_draft.content }}\n", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("{{ input.consult_draft }}", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ABareInputWrittenWhole_IsAHandEdit()
    {
        var m = Working("example-structured-intake");

        var rewritten = Prompt(m, "note_section", "Notes:\n{{ input.prior_notes }}\n");

        Assert.Equal("Notes:\n{{ input.prior_notes.value }}\n", rewritten.Text);
        var edit = Assert.Single(rewritten.HandEdits);
        const string reason = "the template writes input.prior_notes.value at line 2, which is a list at 31; read a member, loop over it or pipe it into a renderer.";
        Assert.Equal($"Hand edit needed in {FileOf(m, "note_section")}, body: {reason}", edit);
        AssertHandEdit(edit, FileOf(m, "note_section"), "A10-037", reason);
    }

    [Fact]
    public void ANodeRead_KeepsItsMember()
    {
        var m = Working("example-nested-note");

        var rewritten = Prompt(m, "draft_summary",
            "{{ node.section_count.value }} {{ node.outline.content }} {{ node.plan.value.sections | array.size }}\n{{ node.section_count }} {{ node.draft_summary }} {{ node.sections }}\n");

        Assert.Equal(
            "{{ node.section_count.value }} {{ node.outline.content }} {{ node.plan.value.sections | array.size }}\n{{ node.section_count.value }} {{ node.draft_summary.content }} {{ node.sections.content }}\n",
            rewritten.Text);
        Assert.Empty(rewritten.HandEdits);

        var draft = Body(MigrateNodesTests.Migrated("example-nested-note"), "nodes/refine/draft_summary.md");
        Assert.Contains("The note that follows has {{ node.section_count.value }} sections.", draft, StringComparison.Ordinal);
    }

    [Fact]
    public void AClassifierValue_ReadsItsValueMember()
    {
        var m = Working("example-classifier-scope");

        var rewritten = Prompt(m, "plan_section", "{{ node.scope.value }} and {{ node.scope }}\n");

        Assert.Equal("{{ node.scope.value.value }} and {{ node.scope.value.value }}\n", rewritten.Text);
        Assert.Empty(rewritten.HandEdits);

        // The vendored files: the decline letter's prompt and the closing macro's body.
        var result = MigrateNodesTests.Migrated("example-classifier-scope");
        Assert.Contains("The referral was classified {{ node.scope.value.value }}:", Body(result, "nodes/letter_decline_letter/decline_letter.md"), StringComparison.Ordinal);
        Migrate.Templates(m);
        Assert.Equal(
            "This determination ({{ node.scope.value.value }}) was recorded on {{ run.date }}. If the clinical picture changes, please send an updated referral.\n",
            m.MacroBodies["macros/closing.md"]);
    }

    [Fact]
    public void AConceptPipe_BecomesARendererPipe()
    {
        var m = Working("example-two-documents");

        var rewritten = Prompt(m, "standard_section_draft",
            "{{ node.identify_problem | concept_bullets }}\n{{ node.create_patient_trajectory.content | concept_context }}\n{{ node.identify_problem.value | renderer.concept_bullets }}\n");

        Assert.Equal(
            "{{ node.identify_problem.value | renderer.concept_bullets }}\n{{ node.create_patient_trajectory.value | renderer.concept_context }}\n{{ node.identify_problem.value | renderer.concept_bullets }}\n",
            rewritten.Text);
        Assert.Empty(rewritten.HandEdits);

        var letter = Body(MigrateNodesTests.Migrated("example-two-documents"), "nodes/draft_patient_letter.md");
        Assert.Contains("Patient trajectory concepts:\n{{ node.create_patient_trajectory.value | renderer.concept_context }}\n", letter, StringComparison.Ordinal);
    }

    [Fact]
    public void ALoopOverANode_ReadsItsItems()
    {
        var m = Working("example-nested-note");

        var rewritten = Prompt(m, "draft_summary", "{{ for s in node.sections }}{{ if !s.skipped }}{{ s.outline.content }}: {{ s.content }}{{ end }}{{ end }}\n");

        // Part 04 § 2.6: the items, a body node through .nodes, and the state for 30's skipped.
        Assert.Equal(
            "{{ for s in node.sections.items }}{{ if s.state != \"skipped\" }}{{ s.nodes.outline.content }}: {{ s.content }}{{ else }}{{ end }}{{ end }}\n",
            rewritten.Text);
        Assert.Empty(rewritten.HandEdits);

        // A state test must handle every state or end with an else (S05-009): one rewritten from skipped gains an empty
        // else, which takes the end tag's left mark, so the text before it is trimmed as at 30.
        var marked = Prompt(m, "draft_summary", "{{ for s in node.sections }}\n{{~ if s.skipped ~}}\nleft out\n{{~ end ~}}\n{{ end }}\n");
        Assert.Equal("{{ for s in node.sections.items }}\n{{~ if s.state == \"skipped\" ~}}\nleft out\n{{~ else }}{{~ end ~}}\n{{ end }}\n", marked.Text);

        Assert.Equal(
            """
            You are closing a consultation note with one line about what it leaves out.

            The sections the author asked for, and whether each was produced:
            {{ for e in node.sections_draft.items }}
            - {{ e.item.value.title }}: {{ if e.state == "skipped" }}left out at the author's request{{ else }}produced{{ end }}
            {{ end }}

            Output rule:
            Return one sentence naming the sections left out at the author's request, or, if none were, one sentence saying every requested section was produced. No heading, no list, no commentary.

            """,
            Body(MigrateNodesTests.Migrated("example-conditional-sections"), "nodes/coverage.md"));
        Assert.Contains(
            "Prior letters, as read:\n{{ for letter in node.letters.items }}{{ if letter.state != \"skipped\" }}{{ letter.content }}\n\n{{ else }}{{ end }}{{ end }}\nConsult draft:",
            Body(MigrateNodesTests.Migrated("example-prior-letters"), "nodes/assessment.md"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AWhitespaceForm31Refuses_IsRewritten()
    {
        var m = Working("example-macros");

        var rewritten = Prompt(m, "summary",
            "A\n{{- input.consult_draft -}}\nB\n{{- snippet.style.content }}\nC {{~ snippet.style.content }} D\n{{ snippet.style.content ~}}\nE\n");

        // Part 05 § 2.6: - becomes ~ on an ordinary output; a placement keeps {{~ only alone on its line, never ~}}.
        Assert.Equal(
            "A\n{{~ input.consult_draft.content ~}}\nB\n{{~ snippet.style.content }}\nC {{ snippet.style.content }} D\n{{ snippet.style.content }}\nE\n",
            rewritten.Text);
        Assert.Empty(rewritten.HandEdits);
        Assert.Empty(TemplateSyntax.Parse(rewritten.Text).Faults);

        // ~ on a statement is kept: an if on a recorded fact stays, with its marks.
        var kept = Prompt(m, "summary",
            "{{ for e in node.requested_sections }}\n{{~ if e.skipped ~}}\nskipped\n{{~ else ~}}\n{{ e.content }}\n{{~ end ~}}\n{{ end }}\n");
        Assert.Equal(
            "{{ for e in node.requested_sections.items }}\n{{~ if e.state == \"skipped\" ~}}\nskipped\n{{~ else ~}}\n{{ e.content }}\n{{~ end ~}}\n{{ end }}\n",
            kept.Text);
        Assert.Empty(kept.HandEdits);

        // patient_line.md's {{~ if … ~}} holds a placement, which no if may hold at 31: it is hoisted, its marks' text kept out.
        Assert.Contains(
            "tell the patient what was decided today.\n\n{{ macro.counselling }}\n\nOutput rule:",
            Body(MigrateNodesTests.Migrated("example-macros"), "nodes/patient_line.md"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASimpleBodyIf_IsLiftedIntoAPart()
    {
        var m = Working("example-conditional-documents");

        var rewritten = Prompt(m, "consult_note_section", "Head\n{{ if input.include_billing == true }}\nBill.\n{{ else }}\nNo bill.\n{{ end }}\nTail\n");

        Assert.Equal("Head\n{{ part.when_1 }}{{ part.when_2 }}\nTail\n", rewritten.Text);
        Assert.Equal(
            """{"when_1":{"if":{"===":[{"var":"input.include_billing.value"},true]},"text":["","Bill.",""]},"when_2":{"if":{"!":[{"===":[{"var":"input.include_billing.value"},true]}]},"text":["","No bill.",""]}}""",
            rewritten.Parts.ToJsonString());
        Assert.Empty(rewritten.HandEdits);

        // A part id the body already names is taken; prose is left as it is.
        var taken = Prompt(m, "consult_note_section", "See part.when_1.\n{{ if input.include_billing == true }}Bill.{{ end }}\n");
        Assert.Equal("See part.when_1.\n{{ part.when_2 }}\n", taken.Text);
        Assert.Equal(["when_2"], taken.Parts.Select(p => p.Key));

        // A whole token only: part.when_10 does not take when_1.
        var longer = Prompt(m, "consult_note_section", "See part.when_10.\n{{ if input.include_billing == true }}Bill.{{ end }}\n");
        Assert.Equal(["when_1"], longer.Parts.Select(p => p.Key));

        // A lifted condition is converted as every condition is: !x on text is blank, and said (A10-008).
        var blank = Prompt(m, "consult_note_section", "{{ if !input.consult_draft }}\nNo referral.\n{{ end }}\n");
        Assert.Equal("""{"blank":[{"var":"input.consult_draft.content"}]}""", blank.Parts["when_1"]!["if"]!.ToJsonString());
        var said = Assert.Single(m.BlankConversions);
        Assert.StartsWith($"{FileOf(m, "consult_note_section")}, parts.when_1.if: '!input.consult_draft' became blank(", said, StringComparison.Ordinal);
        V31Contract.AssertSentence(new SourceRefusal("A10-008", said, ""));

        // The vendored file.
        var result = MigrateNodesTests.Migrated("example-conditional-documents");
        var (header, body) = MigrateNodesTests.Md(result, "nodes/sections_consult_note_section/consult_note_section.md");
        Assert.Contains("Section standard:\n{{ item.content }}\n{{ part.when_1 }}\nReferral:\n{{ input.consult_draft.content }}\n", body, StringComparison.Ordinal);
        Assert.Equal(
            """{"when_1":{"if":{"===":[{"var":"input.include_billing.value"},true]},"text":["","Billing:","End the section with one further sentence stating that this was a new-patient encounter, and repeat the encounter date in that sentence exactly as it is written above.",""]}}""",
            header["parts"]!.ToJsonString());
        Assert.Empty(result.HandEdits);
    }

    [Fact]
    public void ABodyIfOnARecordedFact_StaysInTheBody_AndPassNumberIsLifted()
    {
        // § 2.5: a loop position and an item's state are recorded facts, and stay.
        var m = Working("example-nested-note");
        var facts = Prompt(m, "draft_summary", "{{ for s in node.sections }}{{ if for.first }}First: {{ end }}{{ s.content }}{{ end }}\n");
        Assert.Equal("{{ for s in node.sections.items }}{{ if for.first }}First: {{ end }}{{ s.content }}{{ end }}\n", facts.Text);
        Assert.Empty(facts.Parts);

        // pass.number is not one of § 2.5's facts (a body if testing it is S05-007), so its if is lifted into a part.
        var (header, body) = MigrateNodesTests.Md(MigrateNodesTests.Migrated("example-nested-note"), "nodes/refine/draft_summary.md");
        Assert.Contains("Do not add clinical facts it does not contain.\n{{ part.when_1 }}\nOutput rule:", body, StringComparison.Ordinal);
        Assert.Equal(
            """{"when_1":{"if":{">":[{"var":"pass.number"},1]},"text":["","A reviewer asked for this revision of the previous draft:","{{ pass.previous.node.judge.value.feedback }}","","Previous draft:","{{ pass.previous.node.draft_summary.content }}",""]}}""",
            header["parts"]!.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    [Fact]
    public void AnIfAroundAPlacement_IsHoisted()
    {
        var m = Working("example-macros");

        var rewritten = Prompt(m, "summary", "Style:\n{{ if input.include_counselling == true }}{{ macro.house_style.prompt }}{{ end }}\nEnd.\n");

        Assert.Equal("Style:\n{{ macro.house_style }}\nEnd.\n", rewritten.Text);
        Assert.Empty(rewritten.Parts);
        var placed = Assert.Single(rewritten.Macros);
        Assert.Equal(("house_style", "summary", "nodes/summary.md"), (placed.Macro, placed.Owner, placed.File));
        Assert.Equal("""{"===":[{"var":"input.include_counselling.value"},true]}""", placed.Rule!.Logic.ToJsonString());
        Assert.Equal("input.include_counselling == true", placed.Rule.Text30);

        // Each placement carries its own condition, or none: two in one prompt are two rules, never joined.
        var two = Prompt(m, "summary",
            "{{ if input.include_counselling == true }}{{ macro.house_style.prompt }}{{ end }}\n{{ if input.note_type == \"consult\" }}{{ macro.house_style.prompt }}{{ end }}\n{{ macro.house_style.prompt }}\n");
        Assert.Equal(
            ["""{"===":[{"var":"input.include_counselling.value"},true]}""", """{"===":[{"var":"input.note_type.value"},"consult"]}""", null],
            two.Macros.Select(p => p.Rule?.Logic.ToJsonString()));

        // The vendored file: standing_draft.md; patient_line.md hoists another condition for the same macro, so the one if
        // cannot hold both (A10-036) and the macro has no rule.
        var vendored = Working("example-macros");
        Migrate.Templates(vendored);
        Assert.Contains("Section standard:\n{{ item.content }}\n\n{{ macro.counselling }}\n{{ macro.silent_reminder }}\n", vendored.Sources["nodes/standing_sections/standing_draft.md"].Body, StringComparison.Ordinal);
        Assert.Equal(
            ["""{"===":[{"var":"input.include_counselling.value"},true]}""", """{"===":[{"var":"input.note_type.value"},"follow_up"]}"""],
            vendored.MacroPlacements.Where(p => p.Macro == "counselling").Select(p => p.Rule?.Logic.ToJsonString()));
        Assert.False(vendored.PlacementRules.ContainsKey("counselling"));
        var edit = Assert.Single(vendored.HandEditNotices, h => h.StartsWith("Hand edit needed in macros/counselling.md, if: ", StringComparison.Ordinal));
        const string reason = "macro 'counselling' is placed under different conditions in nodes/standing_sections/standing_draft.md and nodes/patient_line.md; a macro has one if, so place it under one condition, or split it into one macro per condition.";
        AssertHandEdit(edit, "macros/counselling.md", "A10-036", reason);
    }

    [Theory]
    [InlineData("{{ if input.include_counselling == true }}{{ macro.preamble.prompt }}{{ end }}\n{{ macro.preamble.prompt }}\n", "nodes/summary.md")]
    [InlineData("{{ if input.include_counselling == true }}{{ macro.preamble.prompt }}{{ end }}\n{{ if input.note_type == \"consult\" }}{{ macro.preamble.prompt }}{{ end }}\n", "nodes/summary.md")]
    [InlineData("{{ if input.include_counselling == true }}{{ macro.house_style.prompt }}{{ end }}\n", "nodes/summary.md and snippets/style.md")]
    public void PlacementsThatDisagree_InOneBodyOrSeveral_AreAHandEdit(string summary, string files)
    {
        // One hoisted and one plain in one prompt; two different hoists in one prompt; a hoist against a snippet's plain
        // placement (style.md places house_style). Each is appended to summary.md (30 refuses a macro no body places).
        var m = Working("example-macros", f => f["prompts/summary.md"] += summary);

        Migrate.Templates(m);

        var macro = summary.Contains("preamble", StringComparison.Ordinal) ? "preamble" : "house_style";
        Assert.False(m.PlacementRules.ContainsKey(macro));
        var edit = Assert.Single(m.HandEditNotices, h => h.StartsWith($"Hand edit needed in macros/{macro}.md, if: ", StringComparison.Ordinal));
        AssertHandEdit(edit, $"macros/{macro}.md", "A10-036",
            $"macro '{macro}' is placed under different conditions in {files}; a macro has one if, so place it under one condition, or split it into one macro per condition.");
    }

    [Fact]
    public void PlacementsThatAgree_GiveTheMacroItsRule()
    {
        var m = Working("example-macros", f => f["prompts/summary.md"] += "{{ if input.include_counselling == true }}{{ macro.preamble.prompt }}{{ end }}\n");

        Migrate.Templates(m);

        Assert.Equal("""{"===":[{"var":"input.include_counselling.value"},true]}""", m.PlacementRules["preamble"].Logic.ToJsonString());
        Assert.DoesNotContain(m.HandEditNotices, h => h.Contains("macros/preamble.md", StringComparison.Ordinal));
    }

    [Fact]
    public void AForOverAMacroFolder_IsUnrolled()
    {
        var m = Working("example-macros");

        var rewritten = Prompt(m, "summary", "Reminders:\n{{ for m in macro.notes }}- {{ m.label }} ({{ m.id }}): {{ m.prompt }}\n{{ end }}\nEnd.\n");

        Assert.Equal(
            "Reminders:\n- Red flags (red_flags): {{ macro.notes.red_flags }}\n- Safety net (safety_net): {{ macro.notes.safety_net }}\n\nEnd.\n",
            rewritten.Text);
        Assert.Equal(["notes/red_flags", "notes/safety_net"], rewritten.Macros.Select(p => p.Macro));
        Assert.All(rewritten.Macros, p => Assert.Null(p.Rule));
        Assert.Empty(rewritten.HandEdits);

        // One the rewrite cannot write out (a statement in its body) stays, a hand edit (A10-005).
        var kept = Prompt(m, "summary", "{{ for m in macro.notes }}{{ if for.first }}First: {{ end }}{{ m.prompt }}{{ end }}\n");
        var edit = Assert.Single(kept.HandEdits);
        AssertHandEdit(edit, "nodes/summary.md", "A10-005", "the template uses a for over macro.notes at line 1, which the 31 dialect doesn't have; rewrite it.");

        Assert.Contains(
            "Reminders:\n- Red flags: {{ macro.notes.red_flags }}\n- Safety net: {{ macro.notes.safety_net }}\n\n{{ macro.wrap_up }}\n",
            Body(MigrateNodesTests.Migrated("example-macros"), "nodes/summary.md"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void APlacementInsideAForThatStays_IsAHandEdit()
    {
        var m = Working("example-macros");

        // A07-009: a placement stands at the top level. A for over a loop node's items stays, so its placement is an edit.
        var rewritten = Prompt(m, "summary", "{{ for e in node.requested_sections }}\n{{ macro.house_style.prompt }}\n{{ end }}\n");

        Assert.Equal("{{ for e in node.requested_sections.items }}\n{{ macro.house_style }}\n{{ end }}\n", rewritten.Text);
        AssertHandEdit(Assert.Single(rewritten.HandEdits), "nodes/summary.md", "A10-005",
            "the template uses a placement inside an if or a for at line 2, which the 31 dialect doesn't have; rewrite it.");

        // Inside an if already said as a hand edit, the placement is that edit's: one notice.
        var said = Prompt(m, "summary", "{{ for e in node.requested_sections }}{{ if input.include_counselling == true }}{{ macro.house_style.prompt }}{{ end }}{{ end }}\n");
        AssertHandEdit(Assert.Single(said.HandEdits), "nodes/summary.md", "A10-001",
            "the body's if at line 1 does more than choose text to show; write it as parts with their own if.");
    }

    [Fact]
    public void ItemLabel_IsItsLoopsItemLabelField_OrAHandEdit()
    {
        var m = Working("example-macros");

        Assert.Equal("{{ item.value.title }}\n", Prompt(m, "standing_draft", "{{ item.label }}\n", itemLabel: "title").Text);

        var unnamed = Prompt(m, "summary", "Item {{ item.label }}\n");
        Assert.Equal("Item {{ item.label }}\n", unnamed.Text);
        AssertHandEdit(Assert.Single(unnamed.HandEdits), "nodes/summary.md", "A10-005", "the template uses item.label at line 1, which the 31 dialect doesn't have; rewrite it.");

        // The vendored loops name their items: a snippet folder's by title, an input's by its labelField.
        var result = MigrateNodesTests.Migrated("example-macros");
        Assert.Contains("Section:\n{{ item.value.title }}\n", Body(result, "nodes/standing_sections/standing_draft.md"), StringComparison.Ordinal);
        Assert.DoesNotContain("item.label", string.Concat(result.Files.Values), StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileReadInAPrompt_IsAHandEdit()
    {
        var m = Working("example-macros");

        var rewritten = Prompt(m, "summary", "Signed {{ profile.name }}.\n");

        Assert.Equal("Signed {{ profile.name }}.\n", rewritten.Text);
        var edit = Assert.Single(rewritten.HandEdits);
        const string reason = "the prompt reads profile.name, which a 31 prompt doesn't read; move it to a deliverable or a macro body.";
        Assert.Equal($"Hand edit needed in nodes/summary.md, body: {reason}", edit);
        AssertHandEdit(edit, "nodes/summary.md", "S10-004", reason);
        Assert.Contains(edit, m.HandEditNotices);

        // A macro body reads the profile at 31: sign_off.md is kept as it is.
        Migrate.Templates(m);
        Assert.Equal("Dictated by {{ profile.name }}; not yet reviewed.\n", m.MacroBodies["macros/sign_off.md"]);
        Assert.DoesNotContain(m.HandEditNotices, h => h.Contains("sign_off", StringComparison.Ordinal));
    }

    [Fact]
    public void AReadASiteNeverMakes_IsAHandEdit()
    {
        var m = Working("composer-gates");

        var rewritten = Prompt(m, "extract_note_terms", "Terms: {{ deliverable.note.content }}\n");

        Assert.Equal("Terms: {{ deliverable.note.content }}\n", rewritten.Text);
        AssertHandEdit(Assert.Single(rewritten.HandEdits), "nodes/extract_note_terms.md", "A10-039",
            "the template reads deliverable.note.content at line 1, which a 31 prompt doesn't read; rewrite it.");
    }

    [Fact]
    public void ABodyIfTooComplex_IsAHandEdit()
    {
        var m = Working("example-conditional-documents");
        var file = FileOf(m, "consult_note_section");

        var elseIf = Prompt(m, "consult_note_section", "A\n{{ if input.include_billing == true }}\nB\n{{ else if input.encounter_kind == \"new\" }}\nC\n{{ end }}\n");
        Assert.Equal("A\n{{ if input.include_billing.value == true }}\nB\n{{ else if input.encounter_kind.value == \"new\" }}\nC\n{{ end }}\n", elseIf.Text);
        Assert.Empty(elseIf.Parts);
        AssertHandEdit(Assert.Single(elseIf.HandEdits), file, "A10-001", "the body's if at line 2 does more than choose text to show; write it as parts with their own if.");

        var nested = Prompt(m, "consult_note_section", "{{ if input.include_billing == true }}\n{{ if input.encounter_kind == \"new\" }}\nB\n{{ end }}\n{{ end }}\n");
        Assert.Equal(2, nested.HandEdits.Count);
        Assert.All(nested.HandEdits, e => V31Contract.AssertSentence(new SourceRefusal("S10-002", e, file)));
        Assert.EndsWith("at line 1 does more than choose text to show; write it as parts with their own if.", nested.HandEdits[0], StringComparison.Ordinal);
        Assert.EndsWith("at line 2 does more than choose text to show; write it as parts with their own if.", nested.HandEdits[1], StringComparison.Ordinal);

        // An if inside a line of text: senior_review_section.md, its reads rewritten, the if kept.
        var result = MigrateNodesTests.Migrated("example-structured-intake");
        const string path = "nodes/sections_senior_review_section/senior_review_section.md";
        Assert.StartsWith(
            "You are drafting one section of a senior review request for a patient aged {{ input.patient.value.age }}{{ if input.patient.value.sex }} ({{ input.patient.value.sex }}){{ end }}.\n",
            Body(result, path),
            StringComparison.Ordinal);
        AssertHandEdit(Assert.Single(result.HandEdits, h => h.Contains(path, StringComparison.Ordinal)), path, "A10-001",
            "the body's if at line 1 does more than choose text to show; write it as parts with their own if.");

        // A body with no parts (a macro's, a snippet's) keeps its if, a hand edit (A10-035).
        var macros = Working("example-macros-warnings");
        Migrate.Templates(macros);
        Assert.Equal("{{ if input.note_type.value != \"triage\" }}Note type recorded as {{ input.note_type.value }}.{{ end }}\n", macros.MacroBodies["macros/kept_note.md"]);
        AssertHandEdit(
            Assert.Single(macros.HandEditNotices, h => h.StartsWith("Hand edit needed in macros/kept_note.md", StringComparison.Ordinal)),
            "macros/kept_note.md", "A10-035",
            "the body's if at line 1 decides what a macro body shows, which has no parts; write each text as a snippet with its own if, placed where the text was.");
    }

    [Fact]
    public void ABuiltinOutsideTheDialect_IsAHandEdit()
    {
        var m = Working("example-structured-intake");
        var file = FileOf(m, "note_section");

        var rewritten = Prompt(m, "note_section",
            "{{ input.consult_draft | string.truncate 10 }}\n{{ input.consult_draft | string.replace \"a\" \"b\" }}\n{{ input.prior_notes | array.join \", \" }}\n");

        Assert.Equal(
            "{{ input.consult_draft.content | string.truncate 10 }}\n{{ input.consult_draft.content | string.replace \"a\" \"b\" }}\n{{ input.prior_notes.value | array.join \", \" }}\n",
            rewritten.Text);
        Assert.Equal(2, rewritten.HandEdits.Count);
        AssertHandEdit(rewritten.HandEdits[0], file, "A10-005", "the template uses truncate at line 1, which the 31 dialect doesn't have; rewrite it.");
        AssertHandEdit(rewritten.HandEdits[1], file, "A10-005", "the template uses string.replace at line 2, which the 31 dialect doesn't have; rewrite it.");

        // header_line.md's body is a case, which the dialect refuses (S05-001).
        var macros = Working("example-macros");
        Migrate.Templates(macros);
        Assert.Equal(
            "{{ case input.note_type.value }}{{ when \"consult\" }}Consultation note{{ when \"follow_up\" }}Follow-up note{{ else }}Clinical note{{ end }}\n",
            macros.MacroBodies["macros/header_line.md"]);
        AssertHandEdit(
            Assert.Single(macros.HandEditNotices, h => h.StartsWith("Hand edit needed in macros/header_line.md", StringComparison.Ordinal)),
            "macros/header_line.md", "A10-005", "the template uses case at line 1, which the 31 dialect doesn't have; rewrite it.");
    }

    [Fact]
    public void TextOutsideTags_IsNeverRewritten()
    {
        var m = Working("example-nested-note");

        const string prose = "Prose input.consult_draft and node.sections here, {{ \"{{\" }} input.consult_draft.\n"
            + "```\ninput.consult_draft node.plan\n```\n"
            + "{%{ {{ input.consult_draft }} }%}\n"
            + "{{# input.consult_draft }}\n";
        var rewritten = Prompt(m, "draft_summary", prose + "{{ input.consult_draft }}\n");

        Assert.Equal(prose + "{{ input.consult_draft.content }}\n", rewritten.Text);
        Assert.Empty(rewritten.HandEdits);

        // A snippet body that names node.parts in its prose keeps every byte.
        const string snippet = "Read node.parts and input.x as written; {{ \"node.parts\" }}.\n";
        Assert.Equal(snippet, TemplateRewriter.Rewrite(snippet, new TemplateSite30(TemplateSite.SnippetBody, "snippets/x.md", null), m).Text);
    }

    [Theory]
    [InlineData("A\n{{ input.consult_draft\nB\n", 2)]
    [InlineData("{{ if input.consult_draft }}\nA\n", 0)]
    [InlineData("A {{ end }} B\n", 0)]
    [InlineData("{{ for }}\n", 0)]
    public void ABodyThatDoesNotParse_IsAHandEdit_NeverAThrow(string body, int line)
    {
        var m = Working("example-nested-note");

        var rewritten = Prompt(m, "draft_summary", body);

        Assert.Equal(body, rewritten.Text);
        Assert.Empty(rewritten.Parts);
        var edit = Assert.Single(rewritten.HandEdits);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, FileOf(m, "draft_summary")));
        var reason = edit[(edit.IndexOf(": ", StringComparison.Ordinal) + 2)..];
        V31Contract.AssertSentence(new SourceRefusal("A10-038", reason, ""));
        if (line > 0)
            Assert.Equal($"the template can't be read at line {line}; close every tag, if and for.", reason);
    }

    [Fact]
    public void AnEditThatWouldOverlap_IsSetApart_NeverAppliedHalf()
    {
        // No two edits overlap by construction; one that would is set apart (and said as a hand edit), not applied.
        var (kept, dropped) = TemplateRewriter.Order(
        [
            new TemplateRewriter.Edit(10, 20, "x", true),
            new TemplateRewriter.Edit(4, 4, ".value", false),
            new TemplateRewriter.Edit(10, 10, "~", false),
            new TemplateRewriter.Edit(15, 16, "", false),
        ]);

        Assert.Equal([4, 10, 10], kept.Select(e => e.Start));
        Assert.Equal(15, Assert.Single(dropped).Start);
    }
}
