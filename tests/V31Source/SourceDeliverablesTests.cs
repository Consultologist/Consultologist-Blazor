using Consultologist.Api.Tests.V31Resolve;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1505 (specVersion 31, area 08): the publish rules of a deliverable's header and of its delivery folder (part 06
/// §§ 6.1, 6.3, 8): its <c>format</c> (S06-020), its <c>whenEmpty</c> (S06-025), a folder's <c>delivery</c> kinds
/// (S06-026) and <c>bundle</c> (A08-002), and a deliverable reading its own check (S06-018). Each refusal is one change to
/// <see cref="ResolveFixture.Functions"/> (its deliverable <c>consult</c> in the delivery folder <c>emr</c>, gated by the
/// check <c>all_sections</c>), and is the only one said.
/// </summary>
public class SourceDeliverablesTests
{
    private const string Consult = "deliverables/emr/consult.md";

    private const string Emr = "deliverables/emr/index.json";

    private const string Patient = "deliverables/patient/index.json";

    private static void AssertRefusal(Dictionary<string, byte[]> files, string id, string sentence, string path)
    {
        var result = ResolveFixture.Run(files);
        Assert.True(result.Refusals.Count == 1, ResolveFixture.Said(result));
        var refusal = result.Refusals[0];
        V31Contract.AssertSentence(refusal);
        Assert.Equal((id, sentence, path), (refusal.Id, refusal.Sentence, refusal.Path));
    }

    private static void AssertPublishes(Dictionary<string, byte[]> files)
    {
        var result = ResolveFixture.Run(files);
        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
    }

    /// <summary><see cref="ResolveFixture.Functions"/> with the consult note's header <c>format</c> and body replaced.</summary>
    private static Dictionary<string, byte[]> WithConsult(string format, string body) =>
        ResolveFixture.Functions().With(Consult, Md(Deliverable("Consult note", format, "\"check.all_sections\"", "hold"), body));

    /// <summary>
    /// <see cref="ResolveFixture.Functions"/> with a second delivery folder, <c>patient</c>, delivered as
    /// <paramref name="delivery"/>, holding the plain-text letter <c>patient_letter</c>.
    /// </summary>
    private static Dictionary<string, byte[]> WithPatientFolder(string delivery) =>
        ResolveFixture.Functions()
            .With("deliverables/index.json", Index(["emr", "patient"], extra: ", \"delivery\": [], \"bundle\": null"))
            .With(Patient, Index(["patient_letter"], extra: $", \"delivery\": {delivery}, \"bundle\": null"))
            .With("deliverables/patient/patient_letter.md", Md(Deliverable("Letter to the patient", "\"plaintext\"", "null", "hold"),
                "Dear Margaret Okafor,\n\nYour consult note is ready.\n"));

    /// <summary><see cref="ResolveFixture.Functions"/> with a second check, <c>notes_covered</c>, of the same terms, gating nothing.</summary>
    private static Dictionary<string, byte[]> WithSecondCheck() =>
        ResolveFixture.Functions()
            .With("checks/index.json", Index(["section_coverage", "all_sections", "notes_covered"]))
            .With("checks/notes_covered.md", Md(SourceFixture.Node("computed", "Notes covered", "\"check_result\"", true, true,
                "\"if\": null, \"expression\": { \"terms_subset\": [{ \"var\": \"node.extract_input_terms.value\" }, "
                + "{ \"var\": \"node.extract_note_terms.value\" }, \"The note does not cover every clinical term found in the draft.\"] }"),
                ""));

    // ---- Its format (S06-020) ---------------------------------------------------------------------

    [Fact]
    public void ADeliverable_OfMarkdownBlock_Publishes() => AssertPublishes(ResolveFixture.Functions());

    [Fact]
    public void ADeliverable_OfPlaintext_Publishes() =>
        AssertPublishes(WithConsult("\"plaintext\"", "Consult note for Margaret Okafor.\n"));

    // Its body is fixed text here: until area 08's D8 says what an object deliverable's body is, writing the object is
    // A07-007's.
    [Theory]
    [InlineData("concept_list")]
    [InlineData("check_result")]
    [InlineData("standard_section")]
    public void ADeliverable_OfANamedObjectFormat_Publishes(string format) =>
        AssertPublishes(WithConsult($"\"{format}\"", "The terms of the draft.\n"));

    [Fact]
    public void ADeliverable_OfMarkdownInline_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(Consult, h => h["format"] = "markdown_inline"),
            "S06-020", "Deliverable 'consult' has format markdown_inline; a deliverable is markdown_block, plaintext or an object format.",
            Consult);

    [Fact]
    public void ADeliverable_OfAList_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions()
                .With("formats/index.json", Index(["standard_section", "section_titles"]))
                .With("formats/section_titles.json", Json(
                    "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }"))
                .WithJson(Consult, h => h["format"] = "section_titles"),
            "S06-020", "Deliverable 'consult' has format section_titles; a deliverable is markdown_block, plaintext or an object format.",
            Consult);

    [Fact]
    public void ADeliverable_OfAString_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(Consult, h => h["format"] = "string"),
            "S06-020", "Deliverable 'consult' has format string; a deliverable is markdown_block, plaintext or an object format.",
            Consult);

    // ---- Its whenEmpty (S06-025) ------------------------------------------------------------------

    [Theory]
    [InlineData("release")]
    [InlineData("hold")]
    [InlineData("fail")]
    public void WhenEmpty_OfTheThree_Publishes(string value) =>
        AssertPublishes(ResolveFixture.Functions().WithJson(Consult, h => h["whenEmpty"] = value));

    [Fact]
    public void WhenEmpty_Otherwise_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(Consult, h => h["whenEmpty"] = "skip"),
            "S06-025", "Deliverable 'consult' has whenEmpty skip; write release, hold or fail.", Consult);

    // ---- Its folder's delivery and bundle (S06-026, A08-002) --------------------------------------

    [Fact]
    public void EveryDeliveryKind_Publishes() =>
        AssertPublishes(WithPatientFolder("[\"email\", \"pdf\", \"emr\", \"zip\"]"));

    [Fact]
    public void NoDeliveryKind_Publishes() => AssertPublishes(WithPatientFolder("[]"));

    [Fact]
    public void ADeliveryKind_Unknown_IsRefused() =>
        AssertRefusal(
            WithPatientFolder("[\"email\", \"fax\"]"),
            "S06-026", "Deliverables folder 'deliverables/patient' has delivery kind fax; the kinds are email, pdf, emr and zip.", Patient);

    [Fact]
    public void ADeliveryKind_UnknownAtTheTop_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson("deliverables/index.json", h => h["delivery"] = new System.Text.Json.Nodes.JsonArray("fax")),
            "S06-026", "Deliverables folder 'deliverables' has delivery kind fax; the kinds are email, pdf, emr and zip.",
            "deliverables/index.json");

    [Fact]
    public void ABundle_Otherwise_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(Emr, h => h["bundle"] = "tar"),
            "A08-002", "Deliverables folder 'deliverables/emr' has bundle tar; write \"zip\" or null.", Emr);

    // ---- Its own check (S06-018) ------------------------------------------------------------------

    [Fact]
    public void ADeliverable_ReadingItsOwnCheck_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().With(Consult, Md(
                Deliverable("Consult note", "\"markdown_block\"", "\"check.all_sections\"", "hold"),
                "## Sections\n\n{{ node.standards.content }}\n\n{{ check.all_sections.value.message }}\n")),
            "S06-018", "Deliverable 'consult' reads check.all_sections, which checks consult: a deliverable can't read its own check.", Consult);

    [Fact]
    public void ADeliverable_ReadingItsOwnCheck_ThroughASnippet_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions()
                .With("snippets/index.json", Index(["standards", "coverage_note"]))
                .With("snippets/coverage_note.md", Md(
                    "{ \"label\": \"Coverage note\", \"description\": null, \"if\": null, \"format\": \"markdown_block\" }",
                    "{{ check.all_sections.value.message }}\n"))
                .With(Consult, Md(Deliverable("Consult note", "\"markdown_block\"", "\"check.all_sections\"", "hold"),
                    "## Sections\n\n{{ node.standards.content }}\n\n{{~ snippet.coverage_note.content }}\n")),
            "S06-018", "Deliverable 'consult' reads check.all_sections, which checks consult: a deliverable can't read its own check.", Consult);

    [Fact]
    public void ADeliverable_ReadingAnotherCheck_Publishes() =>
        AssertPublishes(
            WithSecondCheck().With(Consult, Md(Deliverable("Consult note", "\"markdown_block\"", "\"check.all_sections\"", "hold"),
                "## Sections\n\n{{ node.standards.content }}\n\n{{ check.notes_covered.value.message }}\n")));
}
