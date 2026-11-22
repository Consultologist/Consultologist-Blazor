using Consultologist.Api.Tests.V31Resolve;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1505 (specVersion 31, area 08): the <c>refusal-packages</c> cases of <c>checks/</c> and of what gates a deliverable
/// (chapter 6 §§ 6.1, 6.3, 6.4), and of a deliverable's header, its reads and its delivery folder (§§ 6.6.1, 6.6.3, 6.6.4, 6.8): one for
/// every publish sentence of <see cref="Consultologist.PackageFormat.V31.CheckSentences"/> and
/// <see cref="Consultologist.PackageFormat.V31.DeliverableSentences"/>. Each side is <see cref="ResolveFixture.Functions"/>
/// (its production loop <c>standards</c>, the check loop <c>section_coverage</c> over its <c>.items</c>, the check
/// <c>all_sections</c> and the deliverable <c>consult</c> it gates) plus only what its rule needs. Each invalid side is
/// its valid side with one change: one text replacement, or, where a check's kind changes and the kind's members with
/// it, one file written again.
/// </summary>
internal static partial class RefusalPackages
{
    private const string AllSectionsFile = "checks/all_sections.md";

    private const string CoverageIndex = "checks/section_coverage/index.json";

    private const string ConsultFile = "deliverables/emr/consult.md";

    private const string EmrIndex = "deliverables/emr/index.json";

    private const string TermsCovered =
        "\"expression\": { \"terms_subset\": [{ \"var\": \"node.extract_input_terms.value\" }, "
        + "{ \"var\": \"node.extract_note_terms.value\" }, \"The note does not cover every clinical term found in the draft.\"] }";

    private static IEnumerable<RefusalCase> Checks()
    {
        yield return new RefusalCase("S06-009",
            "A check returns check_result (chapter 6 § 6.1.1). The check all_sections gates the consult note. Invalid: its "
            + "format is boolean. Valid: check_result.",
            ResolveFixture.Functions(),
            ResolveFixture.Functions().Edit(AllSectionsFile, "\"format\": \"check_result\"", "\"format\": \"boolean\""));

        yield return new RefusalCase("S06-010",
            "A check is computed (chapter 6 § 6.1). The package has a second check, notes_covered. Invalid: it is an agent "
            + "asked to judge the note. Valid: a computed check of the same terms.",
            WithSecondCheck("notes_covered"),
            WithSecondCheck("notes_covered").With("checks/notes_covered.md", Agent("Notes covered", "\"check_result\"",
                "Say whether the note covers every clinical term of the draft.\n")));

        yield return new RefusalCase("S06-011",
            "checks/ holds checks and the loops over them: a for, an if or a case (chapter 6 § 6.3.1). The package has a "
            + "second check loop, recheck, over the production loop's .items. Invalid: it is a while. Valid: a for.",
            WithRecheckLoop(),
            WithRecheckLoop().With("checks/recheck/index.json", Json(Node("while", "Checking again", ArrayOf("check_result"), true, true,
                "\"while\": { \"!\": [{ \"var\": \"check.recheck.again.value.pass\" }] }, \"max\": 3, \"until\": null, "
                + "\"checkFirst\": false, \"nodes\": [\"again\"], \"ret\": \"check.recheck.again\""))));

        yield return new RefusalCase("S06-014",
            "A check loop runs over a production loop's .items (chapter 6 § 6.3.1). Invalid: the check loop section_coverage "
            + "runs over node.standards.value, the sections' text (and names its items by nothing, as a list of text has no labels). "
            + "Valid: node.standards.items, each named by its label.",
            ResolveFixture.Functions(),
            ResolveFixture.Functions().Edit(CoverageIndex,
                "\"in\": \"node.standards.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, \"itemLabel\": \"label\"",
                "\"in\": \"node.standards.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, \"itemLabel\": null"));

        yield return new RefusalCase("S06-035",
            "A gate check's if reads values that are always set (chapter 6 § 6.1.1). The check all_sections gates the consult "
            + "note, and runs when a boolean input is true. Invalid: it reads include_billing, an input that is not required. "
            + "Valid: final_copy, one that is.",
            WithGateIf(),
            WithGateIf().Edit(AllSectionsFile, "input.final_copy.value", "input.include_billing.value"));

        yield return new RefusalCase("A08-001",
            "A deliverable's check names a check.* (chapter 6 § 6.1.2; this implementation's sentence). The consult note is gated. "
            + "Invalid: its check names the production node extract_note_terms. Valid: the check all_sections.",
            ResolveFixture.Functions(),
            ResolveFixture.Functions().Edit(ConsultFile, "\"check\": \"check.all_sections\"", "\"check\": \"node.extract_note_terms\""));

        yield return new RefusalCase("A08-003",
            "A check the deliverable waits for reads its .content, not its .state (chapter 6 §§ 6.1.2, 6.7; this implementation's "
            + "sentence). The consult note is gated by the check proofread, which reads the note and the check consult_words. "
            + "Invalid: consult_words reads the note's state, which proofread decides. Valid: it counts the note's words.",
            ResolveFixture.Proofread(),
            ResolveFixture.Proofread().Edit("checks/consult_words.md",
                "{ \">\": [{ \"words\": [{ \"var\": \"deliverable.emr.consult.content\" }] }, 0] }",
                "{ \"===\": [{ \"var\": \"deliverable.emr.consult.state\" }, \"released\"] }"));

        yield return new RefusalCase("S06-018",
            "A deliverable can't read its own check (chapter 6 § 6.6.3). The consult note is gated by the check all_sections, and "
            + "the package has a second check, notes_covered, gating nothing. Invalid: the note writes all_sections' message. "
            + "Valid: notes_covered's.",
            WithCheckRead(),
            WithCheckRead().Edit(ConsultFile, "check.notes_covered.value.message", "check.all_sections.value.message"));

        yield return new RefusalCase("S06-020",
            "A deliverable is markdown_block, plaintext or an object format (chapter 6 § 6.6.1). Invalid: the consult note is "
            + "markdown_inline. Valid: markdown_block.",
            ResolveFixture.Functions(),
            ResolveFixture.Functions().Edit(ConsultFile, "\"format\": \"markdown_block\"", "\"format\": \"markdown_inline\""));

        yield return new RefusalCase("S06-021",
            "A deliverable's reads fit its format: a Markdown one writes no object (chapter 6 § 6.6.4). The consult note is "
            + "markdown_block. Invalid: it writes the draft's terms, a concept_list. Valid: the sections' text.",
            ResolveFixture.Functions(),
            ResolveFixture.Functions().Edit(ConsultFile, "{{ node.standards.content }}", "{{ node.extract_input_terms.value }}"));

        yield return new RefusalCase("S06-022",
            "A plain-text deliverable writes no Markdown syntax (chapter 6 § 6.6.4). The consult note is plaintext. Invalid: "
            + "its first line is the heading '## Sections'. Valid: the line 'Sections'.",
            PlainConsult(),
            PlainConsult().Edit(ConsultFile, "Sections\n\n", "## Sections\n\n"));

        yield return new RefusalCase("S06-023",
            "An object deliverable's template is one node's value of its format and nothing else (chapter 6 § 6.6.4). The "
            + "consult note is a concept_list. Invalid: it writes 'The terms: ' before the draft's terms. Valid: the terms alone.",
            TermsConsult(),
            TermsConsult().Edit(ConsultFile, "{{ node.extract_input_terms.value }}", "The terms: {{ node.extract_input_terms.value }}"));

        yield return new RefusalCase("S06-025",
            "A deliverable's whenEmpty is release, hold or fail (chapter 6 § 6.6.1). Invalid: the consult note's is skip. "
            + "Valid: hold.",
            ResolveFixture.Functions(),
            ResolveFixture.Functions().Edit(ConsultFile, "\"whenEmpty\": \"hold\"", "\"whenEmpty\": \"skip\""));

        yield return new RefusalCase("S06-026",
            "A delivery folder's kinds are email, pdf, emr and zip (chapter 6 § 6.8). Invalid: the folder emr is delivered by "
            + "emr and fax. Valid: emr and email.",
            ResolveFixture.Functions(),
            ResolveFixture.Functions().Edit(EmrIndex, "\"delivery\": [\"emr\", \"email\"]", "\"delivery\": [\"emr\", \"fax\"]"));

        yield return new RefusalCase("A08-002",
            "A delivery folder's bundle is \"zip\" or null (chapter 6 § 6.8; this implementation's sentence). Invalid: the folder "
            + "emr's bundle is tar. Valid: zip.",
            ResolveFixture.Functions(),
            ResolveFixture.Functions().Edit(EmrIndex, "\"bundle\": \"zip\"", "\"bundle\": \"tar\""));
    }

    /// <summary>
    /// <see cref="WithSecondCheck"/>'s <c>notes_covered</c>, whose message the consult note (gated by <c>all_sections</c>)
    /// writes after its sections.
    /// </summary>
    private static Dictionary<string, byte[]> WithCheckRead() =>
        WithSecondCheck("notes_covered").Edit(ConsultFile, "{{ node.standards.content }}\n",
            "{{ node.standards.content }}\n\n{{ check.notes_covered.value.message }}\n");

    /// <summary><see cref="ResolveFixture.Functions"/> with the consult note <c>plaintext</c>, its heading the plain line <c>Sections</c>.</summary>
    private static Dictionary<string, byte[]> PlainConsult() =>
        ResolveFixture.Functions()
            .Edit(ConsultFile, "\"format\": \"markdown_block\"", "\"format\": \"plaintext\"")
            .Edit(ConsultFile, "## Sections\n", "Sections\n");

    /// <summary><see cref="ResolveFixture.Functions"/> with the consult note a <c>concept_list</c>, its body the draft's terms (D8).</summary>
    private static Dictionary<string, byte[]> TermsConsult() =>
        ResolveFixture.Functions()
            .Edit(ConsultFile, "\"format\": \"markdown_block\"", "\"format\": \"concept_list\"")
            .Edit(ConsultFile, "## Sections\n\n{{ node.standards.content }}\n", "{{ node.extract_input_terms.value }}\n");

    /// <summary><see cref="ResolveFixture.Functions"/> with a second check, <paramref name="id"/>, of the same terms, gating nothing.</summary>
    private static Dictionary<string, byte[]> WithSecondCheck(string id) =>
        ResolveFixture.Functions()
            .Edit("checks/index.json", "\"all_sections\"]", $"\"all_sections\", \"{id}\"]")
            .With($"checks/{id}.md", Md(Node("computed", "Notes covered", "\"check_result\"", true, true, "\"if\": null, " + TermsCovered), ""));

    /// <summary><see cref="ResolveFixture.Functions"/> with a second check loop, <c>recheck</c>, over <c>node.standards.items</c>, gating nothing.</summary>
    private static Dictionary<string, byte[]> WithRecheckLoop() =>
        ResolveFixture.Functions()
            .Edit("checks/index.json", "\"all_sections\"]", "\"all_sections\", \"recheck\"]")
            .With("checks/recheck/index.json", Json(Node("for", "Sections checked again", ArrayOf("check_result"), true, true,
                "\"in\": \"node.standards.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                + "\"max\": null, \"itemLabel\": \"label\", \"nodes\": [\"again\"], \"ret\": \"check.recheck.again\"")))
            .With("checks/recheck/again.md", Md(Node("computed", "Terms covered again", "\"check_result\"", true, true,
                "\"if\": { \"===\": [{ \"var\": \"item.state\" }, \"completed\"] }, " + TermsCovered), ""));

    /// <summary>
    /// <see cref="ResolveFixture.Functions"/> with the boolean inputs <c>final_copy</c> (required) and
    /// <c>include_billing</c> (not required), and the gate check <c>all_sections</c> running when <c>final_copy</c> is true.
    /// </summary>
    private static Dictionary<string, byte[]> WithGateIf() =>
        ResolveFixture.Functions()
            .Edit("manifest.json", "\"channel\": null }\n  }",
                "\"channel\": null },\n"
                + "    \"final_copy\": { \"label\": \"Final copy\", \"description\": null, \"format\": \"boolean\", \"required\": true, \"channel\": null },\n"
                + "    \"include_billing\": { \"label\": \"Include the billing section\", \"description\": null, \"format\": \"boolean\", "
                + "\"required\": false, \"channel\": null }\n  }")
            .Edit(AllSectionsFile, "\"if\": null", "\"if\": { \"===\": [{ \"var\": \"input.final_copy.value\" }, true] }");
}
