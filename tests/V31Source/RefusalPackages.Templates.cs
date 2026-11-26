using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1504 (specVersion 31, area 07): the <c>refusal-packages</c> cases of the dialect (chapter 5 and
/// <c>scriban-dialect.md</c>): one for every publish sentence of
/// <see cref="Consultologist.PackageFormat.V31.Templates.DialectSentences"/> a package reaches through
/// <see cref="Consultologist.PackageFormat.V31.SourceCheck.Run"/>, now that the dialect is its sixth step (D18), and
/// the sentences of other parts that only a template reaches (S02-019 through a template's read, S04-063 through a
/// placed macro). As the earlier areas' cases are, each side is <see cref="SourceFixture.Minimal"/> plus only what its
/// rule needs (a snippet, a part, a macro, a renderer, a loop over a list input), and each invalid side is its valid side
/// with one text replacement, so it gives exactly one refusal. S05-009's and S05-026's cases are the packages of their
/// worked examples, S05-008 and S05-027.
/// </summary>
internal static partial class RefusalPackages
{
    private const string Plan = "nodes/plan.md";

    private const string Note = "deliverables/note.md";

    private const string DraftRead = "{{ input.consult_draft.content }}";

    private static IEnumerable<RefusalCase> Templates() =>
        Statements().Concat(BodyIfs()).Concat(TemplateReads()).Concat(Forms()).Concat(RunSpots()).Concat(Renderers())
            .Concat(Snippets()).Concat(Parts()).Concat(Macros());

    // ---- Chapter 5 § 5.2.1 to § 5.2.3: statements, built-ins, limits ---------------------------------------

    private static IEnumerable<RefusalCase> Statements()
    {
        yield return new RefusalCase("S05-001",
            "A template presents text: no assignment, capture, func, include and the rest (chapter 5 § 5.2.1). The plan's prompt "
            + "writes the draft. Invalid: it captures the draft first. Valid: it writes it.",
            Minimal(),
            Minimal().Edit(Plan, DraftRead, "{{ capture draft }}" + DraftRead + "{{ end }}"));

        yield return new RefusalCase("S05-002",
            "A template uses only the seven built-ins, each piped (chapter 5 § 5.2.2). The plan's prompt writes the draft. "
            + "Invalid: it pipes the draft into string.replace. Valid: it writes it as it is.",
            Minimal(),
            Minimal().Edit(Plan, DraftRead, "{{ input.consult_draft.content | string.replace \"Dr\" \"Doctor\" }}"));

        yield return new RefusalCase("S05-003",
            "A template never cuts text short (chapter 5 § 5.2.2): string.truncate is refused. The plan's prompt writes the draft. "
            + "Invalid: it pipes the draft into string.truncate 200. Valid: it writes it whole.",
            Minimal(),
            Minimal().Edit(Plan, DraftRead, "{{ input.consult_draft.content | string.truncate 200 }}"));

        yield return new RefusalCase("S05-004",
            "A template does not stand in for a missing value (chapter 5 § 5.2.2): object.default is refused. The plan's prompt "
            + "writes the draft. Invalid: it pipes the draft into object.default. Valid: it writes it as it is.",
            Minimal(),
            Minimal().Edit(Plan, DraftRead, "{{ input.consult_draft.content | object.default \"none\" }}"));

        yield return new RefusalCase("S05-005",
            "date.to_string takes a pattern (chapter 5 § 5.2.2). The note writes the date it was run. Invalid: it pipes run.date "
            + "into date.to_string with no pattern. Valid: with \"%d %B %Y\".",
            Dated(),
            Dated().Edit(Note, " \"%d %B %Y\"", ""));

        yield return new RefusalCase("S05-006",
            "A for runs over a list (chapter 5 § 5.2.1). The plan's prompt lists the input sections' titles. Invalid: its for runs "
            + "over input.consult_draft.content, a text. Valid: over input.sections.value.",
            Sections(),
            Sections().Edit(Plan, "for s in input.sections.value", "for s in input.consult_draft.content"));

        yield return new RefusalCase("S05-013",
            "A template nests if and for at most 15 deep (chapter 5 § 5.2.3). The note lists the sections through nested loops. "
            + "Invalid: 16 loops deep. Valid: 15, the limit.",
            Nested(),
            Nested().Edit(Note, "{{ v1.title }}", "{{ for v16 in input.sections.value }}{{ v1.title }}{{ end }}"));

        yield return new RefusalCase("S05-019",
            "A template keeps its whitespace with ~, never with - (chapter 5 § 5.2.6). The plan's prompt writes the draft. "
            + "Invalid: its tag opens with {{-. Valid: with {{.",
            Minimal(),
            Minimal().Edit(Plan, DraftRead, "{{- input.consult_draft.content }}"));

        yield return new RefusalCase("S05-063",
            "A template reads a list by looping over it, never by position (chapter 5 § 5.2.1). The plan's prompt lists the input "
            + "sections' titles. Invalid: it also reads input.sections.value[0].title. Valid: the loop alone.",
            Sections(),
            Sections().Edit(Plan, "The sections: ", "The sections: {{ input.sections.value[0].title }} "));

        yield return new RefusalCase("A07-004",
            "A for runs over a whole list in order (chapter 5 § 5.2.1; this implementation's sentence). The plan's prompt lists "
            + "the input sections' titles. Invalid: its for takes limit:2. Valid: the whole list.",
            Sections(),
            Sections().Edit(Plan, "for s in input.sections.value", "for s in input.sections.value limit:2"));

        yield return new RefusalCase("A07-005",
            "A template is one Scriban can read: every tag closed (chapter 5 § 5.2; this implementation's sentence). The plan's "
            + "prompt writes the draft. Invalid: its tag is never closed. Valid: it is.",
            Minimal(),
            Minimal().Edit(Plan, DraftRead, "{{ input.consult_draft.content "));
    }

    // ---- Chapter 5 § 5.2.5: a body if tests recorded facts only --------------------------------------------

    private static IEnumerable<RefusalCase> BodyIfs()
    {
        yield return new RefusalCase("S05-007",
            "A body if tests only a loop position, a state or whether a check passed (chapter 5 § 5.2.5). The plan's prompt lists "
            + "the input sections' titles, a separator between each. Invalid: the separator's if tests s.title, a value. Valid: "
            + "it tests !for.last.",
            Sections(),
            Sections().Edit(Plan, "if !for.last", "if s.title"));

        yield return new RefusalCase("S05-009",
            "A body if branching on a state with no else handles every state (chapter 5 § 5.2.5; the worked example S05-008). The "
            + "deliverable consult writes each section's outline by its state. Invalid: it handles completed and skipped, and has "
            + "no else. Valid: it has an else for not_run.",
            Consult(),
            Consult().Edit("deliverables/consult.md", "{{ else }}(not run)\n", ""));

        yield return new RefusalCase("S05-010",
            "A state is compared with completed, skipped or not_run (chapter 5 § 5.2.5). The deliverable consult writes each "
            + "section's outline by its state. Invalid: it compares part.state with done. Valid: with skipped.",
            Consult(),
            Consult().Edit("deliverables/consult.md", "part.state == \"skipped\"", "part.state == \"done\""));
    }

    // ---- Chapter 5 § 5.3.1: what each template reads --------------------------------------------------------

    private static IEnumerable<RefusalCase> TemplateReads()
    {
        // #1508 (ruled by the owner, ruling 12 of 2026-11-25): every step of a read is checked, beyond the first id, so the
        // case is the ruling's own, a member read past a macro folder's item.
        yield return new RefusalCase("S05-011",
            "A template reads what is there, at every step (chapter 5 § 5.3.1). The agent closing places the macro hpi of the "
            + "folder section_notes. Invalid: it reads macro.section_notes.hpi.anything, which the macro does not have. "
            + "Valid: it places macro.section_notes.hpi.",
            SectionNotes(),
            SectionNotes().Edit("nodes/closing.md", "{{~ macro.section_notes.hpi }}", "{{ macro.section_notes.hpi.anything }}"));

        // #1508 (ruling 12; Task 25's fix round 1): a unit is placed on its own, never read in an expression.
        yield return new RefusalCase("A11-012",
            "A part or a macro is placed on its own (chapter 5 § 5.2.6). The agent closing places the macro follow_up. "
            + "Invalid: it pipes macro.follow_up into string.upcase, which is no placement. Valid: it places it.",
            FollowUp(),
            FollowUp().Edit("nodes/closing.md", FollowUpPlaced, "{{ macro.follow_up | string.upcase }}\n"));

        // #1508 (Q5 of 2026-11-26, R17; Task 26): a member read through an object that can be null stands under its guard.
        yield return new RefusalCase("A11-009",
            "A member read through an object that can be null is guarded (chapter 5 § 5.2.5). The optional object input intake is "
            + "unset in some jobs. Invalid: the plan's prompt reads input.intake.value.reason bare, which fails to render when intake "
            + "is unset. Valid: it reads it inside {{ if input.intake.value }}.",
            Intake(IntakeGuarded),
            Intake(IntakeRead));

        yield return new RefusalCase("S05-020",
            "A template reads only its site's roots (chapter 5 § 5.3.1). The plan's prompt writes the draft. Invalid: it reads "
            + "deliverable.note.content, which a prompt never reads. Valid: input.consult_draft.content.",
            Minimal(),
            Minimal().Edit(Plan, DraftRead, "{{ deliverable.note.content }}"));

        yield return new RefusalCase("S05-021",
            "A prompt never reads the profile (chapter 5 § 5.3.1): it is sent to a model. The plan's prompt writes the draft. "
            + "Invalid: it also writes profile.signature. Valid: the draft alone.",
            Minimal(),
            Minimal().Edit(Plan, DraftRead, DraftRead + "\nSigned {{ profile.signature }}"));

        yield return new RefusalCase("S06-028",
            "A template reads only the profile's five fields (chapter 6 § 6.10.1). The note writes the plan. Invalid: it also "
            + "writes profile.nickname. Valid: the plan alone.",
            Minimal(),
            Minimal().Edit(Note, "{{ node.plan.content }}", "{{ node.plan.content }}\n{{ profile.nickname }}"));

        yield return new RefusalCase("S02-019",
            "A plain enum has no titles (chapter 2 § 2.2.1), in a template's read too. The plan's prompt names the input "
            + "note_kind, a plain enum. Invalid: it reads input.note_kind.title. Valid: input.note_kind.value.",
            NoteKind(),
            NoteKind().Edit(Plan, "input.note_kind.value", "input.note_kind.title"));

        yield return new RefusalCase("A07-007",
            "A template writes text, not a whole object or list (chapter 5 § 5.2.2; this implementation's sentence). The plan's "
            + "prompt lists the input sections' titles. Invalid: it also writes input.sections.value, a list. Valid: the loop alone.",
            Sections(),
            Sections().Edit(Plan, "The sections: ", "The sections: {{ input.sections.value }} "));

        yield return new RefusalCase("A07-008",
            "A template reads a member of an input or a node, never it bare (chapter 5 § 5.3.1; this implementation's "
            + "sentence). The plan's prompt writes the draft. Invalid: it reads input.consult_draft. Valid: its .content.",
            Minimal(),
            Minimal().Edit(Plan, DraftRead, "{{ input.consult_draft }}"));
    }

    // ---- Chapter 5 § 5.2.6: placement forms -----------------------------------------------------------------

    private const string TonePlaced = "{{~ snippet.tone.content }}";

    private static IEnumerable<RefusalCase> Forms()
    {
        yield return new RefusalCase("S05-015",
            "A placement trims before it, never after (chapter 5 § 5.2.6). The plan's prompt places the snippet tone. Invalid: its "
            + "tag closes with ~}}. Valid: with }}.",
            Tone(),
            Tone().Edit(Plan, TonePlaced, "{{~ snippet.tone.content ~}}"));

        yield return new RefusalCase("S05-016",
            "A placement keeps its whitespace with ~, never with - (chapter 5 § 5.2.6). The plan's prompt places the snippet tone. "
            + "Invalid: its tag opens with {{-. Valid: with {{~.",
            Tone(),
            Tone().Edit(Plan, TonePlaced, "{{- snippet.tone.content }}"));

        yield return new RefusalCase("S05-017",
            "A block placement stands alone on its line (chapter 5 § 5.2.6). The plan's prompt places the snippet tone. Invalid: "
            + "text before it on its line. Valid: alone.",
            Tone(),
            Tone().Edit(Plan, TonePlaced, "Tone: " + TonePlaced));

        yield return new RefusalCase("S05-018",
            "Nothing follows a block placement on its line (chapter 5 § 5.2.6). The plan's prompt places the snippet tone. Invalid: "
            + "text after it on its line. Valid: alone.",
            Tone(),
            Tone().Edit(Plan, TonePlaced, TonePlaced + " Thank you."));

        yield return new RefusalCase("A07-009",
            "A unit is placed at the top level of its template; its own if decides it (chapter 5 § 5.2.6; this implementation's "
            + "sentence). The note places the snippet tone. Invalid: inside an if on the plan's state. Valid: at the top level.",
            ToneInNote(),
            ToneInNote().Edit(Note, "\n" + TonePlaced + "\n",
                "\n{{ if node.plan.state == \"completed\" }}\n" + TonePlaced + "\n{{ else }}\n{{ end }}\n"));
    }

    // ---- Chapter 5 § 5.4: run spots -------------------------------------------------------------------------

    private static IEnumerable<RefusalCase> RunSpots()
    {
        yield return new RefusalCase("S05-024",
            "An output that reads a run fact reads nothing else (chapter 5 § 5.4). The note writes the run's id. Invalid: it pipes "
            + "the plan into string.upcase with run.id. Valid: run.id alone.",
            RunId(),
            RunId().Edit(Note, "{{ run.id }}", "{{ node.plan.content | string.upcase run.id }}"));

        yield return new RefusalCase("S05-025",
            "An output that reads a run-dependent value reads nothing else (chapter 5 § 5.4). The note writes today, a computed "
            + "node reading run.date. Invalid: it pipes the plan into string.upcase with node.today.value. Valid: "
            + "node.today.value alone.",
            Today(),
            Today().Edit(Note, "{{ node.today.value }}", "{{ node.plan.content | string.upcase node.today.value }}"));
    }

    // ---- Chapter 5 § 5.5: renderers -------------------------------------------------------------------------

    private const string Bullets = "{{ node.extract.value | renderer.concept_bullets }}";

    private const string TermsRenderer = "renderers/terms_list.md";

    private static IEnumerable<RefusalCase> Renderers()
    {
        yield return new RefusalCase("S05-026",
            "A value piped into a renderer is of its input (chapter 5 § 5.5.2; the worked example S05-027). identify_problem "
            + "renders concepts as bullets. Invalid: it pipes node.plan.value, a section_plan, into renderer.concept_bullets. "
            + "Valid: node.extract.value, a concept_list.",
            IdentifyProblem(),
            IdentifyProblem().Edit("nodes/identify_problem.md", "node.extract.value", "node.plan.value"));

        yield return new RefusalCase("S05-028",
            "A renderer returns a text format (chapter 5 § 5.5.1). The renderer terms_list lists concepts. Invalid: its format is "
            + "concept_list. Valid: markdown_block.",
            Terms(),
            Terms().Edit(TermsRenderer, "\"format\": \"markdown_block\"", "\"format\": \"concept_list\""));

        yield return new RefusalCase("S05-029",
            "A renderer reads only its piped value (chapter 5 § 5.5.1). The renderer terms_list lists concepts. Invalid: it also "
            + "reads node.plan.content. Valid: value alone.",
            Terms(),
            Terms().Edit(TermsRenderer, "- {{ c.term }}", "- {{ c.term }} {{ node.plan.content }}"));

        yield return new RefusalCase("S05-030",
            "A renderer is called by a pipe, with no arguments (chapter 5 § 5.5.2). The summary renders the extracted concepts "
            + "as bullets. Invalid: it calls renderer.concept_bullets(1). Valid: it pipes into renderer.concept_bullets.",
            Terms(),
            Terms().Edit("nodes/summary.md", "renderer.concept_bullets", "renderer.concept_bullets(1)"));

        yield return new RefusalCase("S05-031",
            "A template pipes into a renderer that exists (chapter 5 § 5.5.2). The summary renders the extracted concepts as "
            + "bullets. Invalid: it pipes into renderer.concept_lines, which nothing defines. Valid: renderer.concept_bullets.",
            Terms(),
            Terms().Edit("nodes/summary.md", "renderer.concept_bullets", "renderer.concept_lines"));

        yield return new RefusalCase("S05-033",
            "A renderer's body pipes into no renderer (chapter 5 § 5.5.1). The renderer terms_list lists concepts. Invalid: it "
            + "pipes each term into renderer.terms_list. Valid: it writes it.",
            Terms(),
            Terms().Edit(TermsRenderer, "- {{ c.term }}", "- {{ c.term | renderer.terms_list }}"));

        yield return new RefusalCase("S05-034",
            "In a chain each renderer's format is the next one's input (chapter 5 § 5.5.2). The summary renders the extracted "
            + "concepts as bullets. Invalid: it pipes the bullets, a markdown_block, into renderer.concept_bullets again. Valid: once.",
            Terms(),
            Terms().Edit("nodes/summary.md", Bullets, "{{ node.extract.value | renderer.concept_bullets | renderer.concept_bullets }}"));
    }

    // ---- Chapter 5 §§ 5.3.2, 5.6: snippets -------------------------------------------------------------------

    private static IEnumerable<RefusalCase> Snippets()
    {
        yield return new RefusalCase("S05-022",
            "A snippet's body reads what each place that places it gives (chapter 5 § 5.3.2). The snippet title_line reads "
            + "item.value.title; the outline in the loop sections places it. Invalid: the plan's prompt, outside any loop, places "
            + "it too. Valid: the outline alone.",
            TitleLine(),
            TitleLine().Edit(Plan, DraftRead, DraftRead + "\n{{~ snippet.title_line.content }}"));

        yield return new RefusalCase("S05-023",
            "A snippet never reaches itself (chapter 5 § 5.3.2). The plan's prompt places intro, which places outro. Invalid: "
            + "outro places intro. Valid: it places nothing.",
            IntroOutro(),
            IntroOutro().Edit("snippets/outro.md", "Close plainly.\n", "Close plainly.\n{{~ snippet.intro.content }}\n"));

        yield return new RefusalCase("S05-035",
            "A template places a snippet the package defines (chapter 5 § 5.6.3). The plan's prompt places the snippet tone. "
            + "Invalid: it places snippet.voice, which nothing defines. Valid: snippet.tone.",
            Tone(),
            Tone().Edit(Plan, TonePlaced, "{{~ snippet.voice.content }}"));

        yield return new RefusalCase("S05-036",
            "A folder item's header is of its folder's format (chapter 5 § 5.6.2). The snippet folder standards holds hpi, whose "
            + "format is an object with a title. Invalid: hpi's header writes heading. Valid: title.",
            WithSnippetFolder(),
            WithSnippetFolder().Edit("snippets/standards/hpi.md", "\"title\": \"History of Present Illness\"", "\"heading\": \"History of Present Illness\""));

        yield return new RefusalCase("S05-037",
            "A snippet is placed by its .content (chapter 5 § 5.6.3). The plan's prompt places the snippet tone. Invalid: it "
            + "writes snippet.tone bare. Valid: snippet.tone.content.",
            Tone(),
            Tone().Edit(Plan, TonePlaced, "{{~ snippet.tone }}"));

        yield return new RefusalCase("A07-010",
            "A snippet's text is a text format (chapter 5 § 5.6.1; this implementation's sentence). The plan's prompt places the "
            + "snippet tone. Invalid: tone declares the format string. Valid: plaintext.",
            Tone(),
            Tone().Edit("snippets/tone.md", "\"format\": \"plaintext\"", "\"format\": \"string\""));

        yield return new RefusalCase("A07-011",
            "A template places at most 1,000 units, counting what they place (this implementation's sentence and limit). The "
            + "plan's prompt places block, which places line 999 times: 1,000 units. Invalid: block places line once more.",
            Placing(999),
            Placing(999).Edit("snippets/block.md", "End of the lines.\n", "{{~ snippet.line.content }}\nEnd of the lines.\n"));

        yield return new RefusalCase("A07-012",
            "A loop over a snippet folder reads each item's body as item.content, rendered where it is read; one tag stands for "
            + "a different body per item, so that body only presents (chapter 5 § 5.6.2; this implementation's sentence). The loop "
            + "standards' agent section reads item.content. Invalid: hpi's body writes run.date. Valid: it writes only text.",
            StandardsRead(),
            StandardsRead().Edit("snippets/standards/hpi.md", "orientation to the patient.\n", "orientation to the patient, on {{ run.date }}.\n"));

        yield return new RefusalCase("A07-013",
            "A folder item's body is rendered only where a template reads it as item.content in its loop; passed to a function "
            + "(or read by a computed node's expression) it is carried as written, so there it is plain text, no tag and no raw "
            + "block (chapter 5 § 5.6.2; chapter 4 § 4.7.7; this implementation's sentence). The loop standards' call section "
            + "passes item to the function draft_section as param.standard. Invalid: hpi's body writes item.value.title. "
            + "Valid: it writes only text.",
            StandardsCalled(),
            StandardsCalled().Edit("snippets/standards/hpi.md", "Open with a one-sentence", "Open the {{ item.value.title }} with a one-sentence"));
    }

    // ---- Chapter 5 § 5.7: parts -----------------------------------------------------------------------------

    private const string ClosingPlaced = "{{~ part.closing }}\n";

    private const string ClosingText = "Close the plan with a line for Dr A. Sample.";

    private static IEnumerable<RefusalCase> Parts()
    {
        yield return new RefusalCase("S05-038",
            "A prompt places only the parts its node declares (chapter 5 § 5.7.2). The plan declares and places the part closing. "
            + "Invalid: it also places part.signoff, which it does not declare. Valid: closing alone.",
            Closing(),
            Closing().Edit(Plan, ClosingPlaced, ClosingPlaced + "{{~ part.signoff }}\n"));

        yield return new RefusalCase("S05-039",
            "A part a node declares is placed (chapter 5 § 5.7.2). The plan declares the part closing. Invalid: its prompt never "
            + "places it. Valid: it places it.",
            Closing(),
            Closing().Edit(Plan, ClosingPlaced, ""));

        yield return new RefusalCase("S05-040",
            "A part is placed once (chapter 5 § 5.7.2). The plan declares and places the part closing. Invalid: its prompt "
            + "places it twice. Valid: once.",
            Closing(),
            Closing().Edit(Plan, ClosingPlaced, ClosingPlaced + ClosingPlaced));

        yield return new RefusalCase("A07-002",
            "A part's text only presents; its own if decides it (chapter 5 § 5.7.1; this implementation's sentence). The plan "
            + "places the part closing. Invalid: its text holds an if. Valid: none.",
            Closing(),
            Closing().Edit(Plan, ClosingText, "{{ if for.first }}" + ClosingText + "{{ end }}"));

        yield return new RefusalCase("A07-001",
            "A snippet placed in a part's text places no part or macro (chapter 5 § 5.6.3; this implementation's sentence). The "
            + "part closing places the snippet closer. Invalid: closer places the macro follow_up. Valid: it places nothing.",
            Closer(),
            Closer().Edit("snippets/closer.md", "Thank Dr A. Sample.\n", "Thank Dr A. Sample.\n{{~ macro.follow_up }}\n"));
    }

    // ---- Chapter 5 § 5.8: macros ----------------------------------------------------------------------------

    private const string FollowUpFile = "macros/follow_up.md";

    private const string FollowUpBody = "Follow up with Dr A. Sample in six weeks.\n";

    private const string FollowUpLine = "Close the plan with {{ macro.follow_up.content }}";

    private const string FollowUpPlaced = "{{~ macro.follow_up }}\n";

    private static IEnumerable<RefusalCase> Macros()
    {
        // #1508 (comment 6, item 1; R13; Task 32): a judgement node asks its question; the migration leaves it empty.
        yield return new RefusalCase("A11-013",
            "A classifier asks its question (chapter 5 § 5.8.2). The judgement agent applies decides whether a follow-up line "
            + "is offered, from the draft. Invalid: its prompt gives the draft and asks nothing. Valid: it asks whether the "
            + "draft plans a follow-up.",
            Applies("Does the draft plan a follow-up visit?\n"),
            Applies(""));

        yield return new RefusalCase("S05-042",
            "A macro's body places no macro (chapter 5 § 5.8.1). The agent closing places the macro follow_up. Invalid: "
            + "follow_up's body places macro.recall. Valid: it places nothing.",
            FollowUp(),
            FollowUp().Edit(FollowUpFile, FollowUpBody, FollowUpBody + "{{ macro.recall }}\n"));

        yield return new RefusalCase("S05-043",
            "A macro's body places no part (chapter 5 § 5.8.1). The agent closing places the macro follow_up. Invalid: "
            + "follow_up's body places part.closing. Valid: it places nothing.",
            FollowUp(),
            FollowUp().Edit(FollowUpFile, FollowUpBody, FollowUpBody + "{{ part.closing }}\n"));

        yield return new RefusalCase("S05-044",
            "A macro's instruction holds its own marker exactly once (chapter 5 § 5.8.4). The agent closing places the macro "
            + "follow_up. Invalid: its instruction holds no marker. Valid: one.",
            FollowUp(),
            FollowUp().Edit(FollowUpFile, FollowUpLine, "Close the plan with the follow-up line."));

        yield return new RefusalCase("S05-045",
            "A macro's instruction holds no other macro's marker (chapter 5 § 5.8.4). The agent closing places the macro "
            + "follow_up. Invalid: its instruction also holds recall's marker. Valid: its own alone.",
            FollowUp(),
            FollowUp().Edit(FollowUpFile, FollowUpLine, FollowUpLine + " and {{ macro.recall.content }}"));

        yield return new RefusalCase("S05-046",
            "The placing node's format takes the macro's (chapter 5 § 5.8.3). The agent closing, a plaintext, places the macro "
            + "follow_up. Invalid: follow_up is a markdown_block. Valid: a plaintext.",
            FollowUp(),
            FollowUp().Edit(FollowUpFile, "\"format\": \"plaintext\"", "\"format\": \"markdown_block\""));

        yield return new RefusalCase("S05-047",
            "Only an agent's prompt places a macro (chapter 5 § 5.8.3). The agent closing places the macro follow_up. Invalid: "
            + "the note places it too. Valid: closing alone.",
            FollowUp(),
            FollowUp().Edit(Note, "{{ node.plan.content }}", "{{ node.plan.content }}\n{{~ macro.follow_up }}"));

        yield return new RefusalCase("S05-048",
            "A template places a macro once (chapter 5 § 5.8.3). The agent closing places the macro follow_up. Invalid: twice. "
            + "Valid: once.",
            FollowUp(),
            FollowUp().Edit("nodes/closing.md", FollowUpPlaced, FollowUpPlaced + FollowUpPlaced));

        yield return new RefusalCase("S05-049",
            "A node that places a macro returns a text format (chapter 5 § 5.8.3). The agent closing places the macro follow_up. "
            + "Invalid: closing returns a concept_list. Valid: a plaintext.",
            FollowUp(),
            FollowUp().Edit("nodes/closing.md", "\"format\": \"plaintext\"", "\"format\": \"concept_list\""));

        yield return new RefusalCase("S05-050",
            "A plaintext macro's body writes no Markdown (chapter 5 § 5.8.1). The agent closing places the macro follow_up. "
            + "Invalid: its body starts a line with - . Valid: plain text.",
            FollowUp(),
            FollowUp().Edit(FollowUpFile, FollowUpBody, "- " + FollowUpBody));

        yield return new RefusalCase("S05-051",
            "A macro's body holds no marker (chapter 5 § 5.8.4). The agent closing places the macro follow_up. Invalid: its body "
            + "holds its own marker. Valid: none.",
            FollowUp(),
            FollowUp().Edit(FollowUpFile, FollowUpBody, FollowUpBody + "{{ macro.follow_up.content }}\n"));

        yield return new RefusalCase("S05-064",
            "A prompt never reads a node that carries profile text through a macro (chapter 5 § 5.8.6). The agent closing places "
            + "the macro signature, which writes profile.signature, so closing carries profile text; the note reads it. Invalid: "
            + "the plan's prompt reads it too. Valid: the note alone.",
            Signature(),
            Signature().Edit(Plan, DraftRead, DraftRead + "\n{{ node.closing.content }}"));

        yield return new RefusalCase("A07-003",
            "A macro's instruction places no macro or part (chapter 5 § 5.8.3; this implementation's sentence). The agent closing "
            + "places the macro follow_up. Invalid: its instruction places macro.recall. Valid: none.",
            FollowUp(),
            FollowUp().Edit(FollowUpFile, FollowUpLine, FollowUpLine + " {{ macro.recall }}"));

        yield return new RefusalCase("S04-063",
            "A macro placed in a function reads only what the function has (chapter 4 § 4.7.10). The function draft_section's "
            + "agent places the function's own macro follow_up, whose if reads its classifier. Invalid: the if reads node.plan, "
            + "outside the function. Valid: node.classify, in it.",
            FunctionMacro(),
            FunctionMacro().Edit("functions/draft_section/macros/follow_up.md",
                "{ \"===\": [{ \"var\": \"node.classify.value.answer\" }, true] }",
                "{ \"!\": [{ \"blank\": [{ \"var\": \"node.plan.content\" }] }] }"));
    }

    // ---- The packages ---------------------------------------------------------------------------------------

    /// <summary>The note also writes the date it was run, through date.to_string.</summary>
    private static Dictionary<string, byte[]> Dated() =>
        Minimal().Edit(Note, "{{ node.plan.content }}\n", "{{ node.plan.content }}\n\nDated {{ run.date | date.to_string \"%d %B %Y\" }}\n");

    /// <summary>The note also writes the run's id.</summary>
    private static Dictionary<string, byte[]> RunId() =>
        Minimal().Edit(Note, "{{ node.plan.content }}\n", "{{ node.plan.content }}\n\nRun {{ run.id }}\n");

    /// <summary>The computed <c>today</c> (<c>run.date</c>, so run-dependent), which the note writes.</summary>
    private static Dictionary<string, byte[]> Today() =>
        MinimalListing("today")
            .With("nodes/today.md", Md(Node("computed", "Today", "\"date\"", true, false,
                "\"if\": null, \"expression\": { \"var\": \"run.date\" }").Replace("\"runDependent\": false", "\"runDependent\": true", StringComparison.Ordinal), ""))
            .Edit(Note, "{{ node.plan.content }}\n", "{{ node.plan.content }}\n\nDated {{ node.today.value }}\n");

    /// <summary>The input <c>sections</c>, whose titles the plan's prompt lists, a separator between each.</summary>
    private static Dictionary<string, byte[]> Sections() =>
        WithSectionsInput(Minimal())
            .Edit(Plan, DraftRead + "\n", DraftRead + "\n\nThe sections: {{ for s in input.sections.value }}{{ s.title }}{{ if !for.last }}; {{ end }}{{ end }}\n");

    /// <summary>The input <c>sections</c>, whose titles the note lists through 15 nested loops (the limit).</summary>
    private static Dictionary<string, byte[]> Nested()
    {
        var body = new StringBuilder("{{ node.plan.content }}\n\n");
        for (var depth = 1; depth <= 15; depth++)
            body.Append("{{ for v").Append(depth).Append(" in input.sections.value }}");
        body.Append("{{ v1.title }}");
        for (var depth = 1; depth <= 15; depth++)
            body.Append("{{ end }}");
        return WithSectionsInput(Minimal()).With(Note, Md(SourceFixture.Deliverable("Consultation note", Plain, "null", "hold"), body + "\n"));
    }

    /// <summary>The input <c>note_kind</c>, a plain enum, which the plan's prompt names.</summary>
    private static Dictionary<string, byte[]> NoteKind() =>
        WithNoteKind().Edit(Plan, DraftRead + "\n", DraftRead + "\n\nWrite it as {{ input.note_kind.value }}.\n");

    /// <summary>
    /// <see cref="WithLoop"/> with the deliverable <c>consult</c>: over the loop's items as <c>part</c>, each outline by its
    /// state, completed, skipped and else (the spec's example for S05-008).
    /// </summary>
    private static Dictionary<string, byte[]> Consult() =>
        WithLoop()
            .Edit("deliverables/index.json", "\"items\": [\"note\"]", "\"items\": [\"note\", \"consult\"]")
            .With("deliverables/consult.md", Md(SourceFixture.Deliverable("Consult", Plain, "null", "hold"),
                "{{ for part in node.sections.items }}{{ if part.state == \"completed\" }}{{ part.value }}\n"
                + "{{ else if part.state == \"skipped\" }}(skipped)\n{{ else }}(not run)\n{{ end }}{{ end }}"));

    /// <summary>The snippet <c>tone</c>, which the plan's prompt places as a block.</summary>
    private static Dictionary<string, byte[]> Tone() =>
        WithTone(Minimal()).Edit(Plan, DraftRead + "\n", DraftRead + "\n" + TonePlaced + "\n");

    /// <summary>The snippet <c>tone</c>, which the note places as a block.</summary>
    private static Dictionary<string, byte[]> ToneInNote() =>
        WithTone(Minimal()).Edit(Note, "{{ node.plan.content }}\n", "{{ node.plan.content }}\n" + TonePlaced + "\n");

    private static Dictionary<string, byte[]> WithTone(Dictionary<string, byte[]> files) =>
        files.Edit("snippets/index.json", "\"items\": []", "\"items\": [\"tone\"]")
            .With("snippets/tone.md", Md("{ \"label\": \"Tone\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }",
                "Write warmly and plainly.\n"));

    /// <summary><see cref="WithLoop"/> with the snippet <c>title_line</c> (reading <c>item.value.title</c>), which the outline places.</summary>
    private static Dictionary<string, byte[]> TitleLine() =>
        WithLoop()
            .Edit("snippets/index.json", "\"items\": []", "\"items\": [\"title_line\"]")
            .With("snippets/title_line.md", Md("{ \"label\": \"Title line\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }",
                "Section: {{ item.value.title }}\n"))
            .Edit("nodes/sections/outline.md", "short list of points.\n", "short list of points.\n{{~ snippet.title_line.content }}\n");

    /// <summary>The snippet folder <c>standards</c> and the loop <c>standards</c> over it, whose agent <c>section</c> reads each item's body as <c>item.content</c>.</summary>
    private static Dictionary<string, byte[]> StandardsRead() =>
        WithSnippetFolder()
            .Edit("nodes/index.json", "\"items\": [\"plan\"]", "\"items\": [\"plan\", \"standards\"]")
            .With("nodes/standards/index.json", Json(Node("for", "Standards", ArrayOf(Plain), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": 4, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"section\"], \"ret\": \"node.standards.section\"")))
            .With("nodes/standards/section.md", Agent("Applying the section standard", Plain,
                "Write the section '{{ item.value.title }}' of the note for Margaret Okafor, following this standard:\n\n{{ item.content }}\n"));

    /// <summary>
    /// <see cref="WithSnippetFolder"/> with the loop <c>standards</c> over it, whose call <c>section</c> passes <c>item</c>
    /// to the function <c>draft_section</c> as <c>param.standard</c> (part 04 § 7.7), which reads its body as
    /// <c>param.standard.content</c>.
    /// </summary>
    private static Dictionary<string, byte[]> StandardsCalled()
    {
        const string standard = "{ \"type\": \"object\", \"required\": [\"title\"], \"additionalProperties\": false, "
            + "\"properties\": { \"title\": { \"$ref\": \"plaintext\" } } }";
        var files = WithSnippetFolder()
            .Edit("nodes/index.json", "\"items\": [\"plan\"]", "\"items\": [\"plan\", \"standards\"]")
            .With("nodes/standards/index.json", Json(Node("for", "Standards", ArrayOf(Plain), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": 4, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"section\"], \"ret\": \"node.standards.section\"")))
            .With("nodes/standards/section.md", Md(
                Node("call", "Drafting the section", Plain, false, true,
                    "\"if\": null, \"function\": \"draft_section\", \"with\": { \"standard\": \"item\" }"),
                ""))
            .Edit("functions/index.json", "\"items\": []", "\"items\": [\"draft_section\"]")
            .With("functions/draft_section/index.json", Json(
                "{ \"label\": \"Draft a section\", \"description\": null, "
                + "\"inputs\": { \"standard\": { \"label\": \"Standard\", \"description\": null, \"format\": " + standard + " } }, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + "\"nodes\": [\"standard_draft\"], \"ret\": \"node.standard_draft\", \"format\": \"plaintext\" }"))
            .With("functions/draft_section/standard_draft.md", Agent("Drafting from the standard", Plain,
                "Draft the section '{{ param.standard.value.title }}' of the note for Margaret Okafor, following this standard:\n\n"
                + "{{ param.standard.content }}\n"));
        foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
            files.With($"functions/draft_section/{folder}/index.json", SourceFixture.Index([]));
        return files;
    }

    /// <summary>The snippets <c>intro</c> (placing <c>outro</c>) and <c>outro</c>; the plan's prompt places intro.</summary>
    private static Dictionary<string, byte[]> IntroOutro() =>
        Minimal()
            .Edit("snippets/index.json", "\"items\": []", "\"items\": [\"intro\", \"outro\"]")
            .With("snippets/intro.md", Md("{ \"label\": \"Intro\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }",
                "Open warmly.\n{{~ snippet.outro.content }}\n"))
            .With("snippets/outro.md", Md("{ \"label\": \"Outro\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }",
                "Close plainly.\n"))
            .Edit(Plan, DraftRead + "\n", DraftRead + "\n{{~ snippet.intro.content }}\n");

    /// <summary>The snippet <c>line</c> and the snippet <c>block</c> placing it <paramref name="lines"/> times; the plan's prompt places block.</summary>
    private static Dictionary<string, byte[]> Placing(int lines)
    {
        var block = new StringBuilder();
        for (var i = 0; i < lines; i++)
            block.Append("{{~ snippet.line.content }}\n");
        block.Append("End of the lines.\n");
        return Minimal()
            .Edit("snippets/index.json", "\"items\": []", "\"items\": [\"line\", \"block\"]")
            .With("snippets/line.md", Md("{ \"label\": \"Line\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }", "One line.\n"))
            .With("snippets/block.md", Md("{ \"label\": \"Block\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }", block.ToString()))
            .Edit(Plan, DraftRead + "\n", DraftRead + "\n{{~ snippet.block.content }}\n");
    }

    /// <summary>
    /// The agents <c>extract</c> (a concept_list) and <c>summary</c> (rendering it with the built-in
    /// <c>concept_bullets</c>), and the package renderer <c>terms_list</c>.
    /// </summary>
    private static Dictionary<string, byte[]> Terms() =>
        Minimal()
            .Edit("nodes/index.json", "\"items\": [\"plan\"]", "\"items\": [\"plan\", \"extract\", \"summary\"]")
            .With("nodes/extract.md", Agent("Extracting the concepts", "\"concept_list\"", "List the clinical concepts in the draft.\n\n" + DraftRead + "\n"))
            .With("nodes/summary.md", Agent("Summarising the concepts", Plain, "Summarise these concepts for Dr A. Sample:\n" + Bullets + "\n"))
            .Edit("renderers/index.json", "\"items\": []", "\"items\": [\"terms_list\"]")
            .With(TermsRenderer, Md(
                "{ \"label\": \"Terms as a list\", \"kind\": \"rendered\", \"description\": null, \"input\": \"concept_list\", \"format\": \"markdown_block\" }",
                "{{~ for c in value.concepts ~}}\n- {{ c.term }}\n{{~ end ~}}\n"));

    /// <summary>
    /// The spec's example for S05-027: the plan returns a section_plan, <c>extract</c> a concept_list, and
    /// <c>identify_problem</c> renders the concepts with <c>concept_bullets</c>; the note writes identify_problem.
    /// </summary>
    private static Dictionary<string, byte[]> IdentifyProblem() =>
        ResolveFixture.WithFormats(Minimal(), "section_plan")
            .Edit(Plan, "\"format\": \"plaintext\"", "\"format\": \"section_plan\"")
            .Edit("nodes/index.json", "\"items\": [\"plan\"]", "\"items\": [\"plan\", \"extract\", \"identify_problem\"]")
            .With("nodes/extract.md", Agent("Extracting the concepts", "\"concept_list\"", "List the clinical concepts in the draft.\n\n" + DraftRead + "\n"))
            .With("nodes/identify_problem.md", Agent("Identifying the problem", Plain,
                "Name the main problem for Dr A. Sample from these concepts:\n" + Bullets + "\n"))
            .Edit(Note, "{{ node.plan.content }}", "{{ node.identify_problem.content }}");

    /// <summary>The plan declares the part <c>closing</c> and places it as a block.</summary>
    private static Dictionary<string, byte[]> Closing() =>
        Minimal()
            .Edit(Plan, DraftRead + "\n", DraftRead + "\n" + ClosingPlaced)
            .Edit(Plan, "\"parts\": null", $"\"parts\": {{ \"closing\": {{ \"if\": null, \"text\": [\"{ClosingText}\"] }} }}");

    /// <summary><see cref="Closing"/> whose text places the snippet <c>closer</c>, with the macro <c>follow_up</c> unplaced.</summary>
    private static Dictionary<string, byte[]> Closer() =>
        WithFollowUp(Closing().Edit(Plan, $"\"{ClosingText}\"", $"\"{ClosingText}\", \"{{{{~ snippet.closer.content }}}}\""))
            .Edit("snippets/index.json", "\"items\": []", "\"items\": [\"closer\"]")
            .With("snippets/closer.md", Md("{ \"label\": \"Closer\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }",
                "Thank Dr A. Sample.\n"));

    /// <summary>The macro folder <c>section_notes</c> holding <c>hpi</c>, and the agent <c>closing</c>, which places it.</summary>
    private static Dictionary<string, byte[]> SectionNotes() =>
        MinimalListing("closing")
            .Edit("macros/index.json", "\"items\": []", "\"items\": [\"section_notes\"]")
            .With("macros/section_notes/index.json", SourceFixture.Index(["hpi"], extra: ", \"if\": null"))
            .With("macros/section_notes/hpi.md", Md(
                "{ \"label\": \"History\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place the history's closing line alone:\", \"{{~ macro.section_notes.hpi.content }}\"] }",
                "History taken from Margaret Okafor and her daughter.\n"))
            .With("nodes/closing.md", Agent("Writing the closing", Plain, "Write the closing line of the history.\n{{~ macro.section_notes.hpi }}\n"));

    private const string IntakeRead = "Reason for referral: {{ input.intake.value.reason }}";

    private const string IntakeGuarded = "{{ if input.intake.value }}Reason for referral: {{ input.intake.value.reason }}{{ end }}";

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> with the optional object input <c>intake</c> (its <c>reason</c>, text), whose plan's
    /// prompt writes <paramref name="line"/> after the draft.
    /// </summary>
    private static Dictionary<string, byte[]> Intake(string line) =>
        Minimal()
            .Edit("manifest.json", "\"channel\": null }\n  }",
                "\"channel\": null },\n"
                + "    \"intake\": { \"label\": \"Intake form\", \"description\": null, \"format\": { \"type\": \"object\", "
                + "\"required\": [\"reason\"], \"additionalProperties\": false, \"properties\": { \"reason\": { \"type\": \"string\", "
                + "\"title\": \"Reason for referral\" } } }, \"required\": false, \"channel\": null }\n  }")
            .Edit(Plan, DraftRead, DraftRead + "\n" + line);

    /// <summary>The <c>judgement</c> agent <c>applies</c>, its prompt the draft, a blank line and <paramref name="question"/>.</summary>
    private static Dictionary<string, byte[]> Applies(string question) =>
        MinimalListing("applies")
            .With("nodes/applies.md", Agent("Whether a follow-up applies", "\"judgement\"", "{{ input.consult_draft.content }}\n\n" + question));

    /// <summary>The macros <c>follow_up</c> and <c>recall</c> (unplaced), and the agent <c>closing</c>, which places follow_up.</summary>
    private static Dictionary<string, byte[]> FollowUp() =>
        WithFollowUp(MinimalListing("closing"))
            .With("nodes/closing.md", Agent("Writing the closing", Plain, "Write the closing line of the plan.\n" + FollowUpPlaced));

    private static Dictionary<string, byte[]> WithFollowUp(Dictionary<string, byte[]> files) =>
        files.Edit("macros/index.json", "\"items\": []", "\"items\": [\"follow_up\", \"recall\"]")
            .With(FollowUpFile, Md(
                $"{{ \"label\": \"Follow-up\", \"description\": null, \"if\": null, \"format\": \"plaintext\", \"instruction\": [\"{FollowUpLine}\"] }}",
                FollowUpBody))
            .With("macros/recall.md", Md(
                "{ \"label\": \"Recall\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + "\"instruction\": [\"End with {{ macro.recall.content }}\"] }",
                "Recall in a year.\n"));

    /// <summary>
    /// The macro <c>signature</c> (its body writes <c>profile.signature</c>), placed by the agent <c>closing</c>, which
    /// so carries profile text and says so; the note reads closing.
    /// </summary>
    private static Dictionary<string, byte[]> Signature() =>
        MinimalListing("closing")
            .Edit("macros/index.json", "\"items\": []", "\"items\": [\"signature\"]")
            .With("macros/signature.md", Md(
                "{ \"label\": \"Signature\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + "\"instruction\": [\"End with {{ macro.signature.content }}\"] }",
                "Signed, {{ profile.signature }}\n"))
            .With("nodes/closing.md", Agent("Writing the closing", Plain, "Write the closing line of the plan.\n{{~ macro.signature }}\n")
                .Replace("\"profileCarrying\": false", "\"profileCarrying\": true", StringComparison.Ordinal))
            .Edit(Note, "{{ node.plan.content }}", "{{ node.plan.content }}\n{{ node.closing.content }}");

    /// <summary>
    /// <see cref="WithFunction"/> whose function <c>draft_section</c> also holds the classifier <c>classify</c> and its own
    /// macro <c>follow_up</c>, decided on the classifier's answer and placed by <c>patient_draft</c> (part 04 § 7.10: the
    /// function's own macro, looked up first).
    /// </summary>
    private static Dictionary<string, byte[]> FunctionMacro() =>
        WithFunction()
            .Edit("functions/draft_section/index.json", "\"nodes\": [\"patient_draft\"]", "\"nodes\": [\"classify\", \"patient_draft\"]")
            .With("functions/draft_section/classify.md", Agent("Classifying the draft", "\"judgement\"",
                "Does the consult draft below call for a follow-up?\n\n{{ param.draft.content }}\n"))
            .Edit("functions/draft_section/patient_draft.md", "{{ param.draft.content }}\n", "{{ param.draft.content }}\n{{~ macro.follow_up }}\n")
            .With("functions/draft_section/macros/index.json", SourceFixture.Index(["follow_up"]))
            .With("functions/draft_section/macros/follow_up.md", Md(
                "{ \"label\": \"Follow-up\", \"description\": null, "
                + "\"if\": { \"===\": [{ \"var\": \"node.classify.value.answer\" }, true] }, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Close with {{ macro.follow_up.content }}\"] }",
                "Follow up with Dr A. Sample in six weeks.\n"));
}
