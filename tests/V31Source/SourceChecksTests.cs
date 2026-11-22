using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1505 (specVersion 31, area 08): the publish rules of <c>checks/</c> (part 06 §§ 1, 3, 4; D17, D18): what it holds,
/// what a check loop runs over, what a deliverable's <c>check</c> names, and what a gate check's <c>if</c> reads. Each
/// refusal is one change to <see cref="ResolveFixture.Functions"/> (its check loop <c>section_coverage</c> over
/// <c>node.standards.items</c>, its check <c>all_sections</c>, and the deliverable <c>consult</c> it gates), and is the
/// only one said.
/// </summary>
public class SourceChecksTests
{
    private const string AllSections = "checks/all_sections.md";

    private const string Coverage = "checks/section_coverage/index.json";

    private const string Consult = "deliverables/emr/consult.md";

    private const string Plain = "\"plaintext\"";

    private const string TermsSubset =
        "\"expression\": { \"terms_subset\": [{ \"var\": \"node.extract_input_terms.value\" }, "
        + "{ \"var\": \"node.extract_note_terms.value\" }, \"The note does not cover every clinical term found in the draft.\"] }";

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

    /// <summary><see cref="ResolveFixture.Functions"/> with <paramref name="id"/> listed last in <c>checks/</c>.</summary>
    private static Dictionary<string, byte[]> WithCheck(string id) =>
        ResolveFixture.Functions().With("checks/index.json", Index(["section_coverage", "all_sections", id]));

    /// <summary><see cref="ResolveFixture.Functions"/> with the optional boolean input <c>include_billing</c>, whose value can be null.</summary>
    private static Dictionary<string, byte[]> WithOptionalInput() =>
        ResolveFixture.Functions().WithJson("manifest.json", m => m["inputs"]!["include_billing"] = JsonNode.Parse(
            "{ \"label\": \"Include the billing section\", \"description\": null, \"format\": \"boolean\", \"required\": false, \"channel\": null }"));

    /// <summary>The check loop's index set to run over <paramref name="source"/>, a list of text, whose items have no label to name them by.</summary>
    private static void OverList(JsonObject index, string source)
    {
        index["in"] = source;
        index["itemLabel"] = null;
    }

    private const string ReadsBilling = "{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }";

    [Fact]
    public void TheFunctionsPackage_Publishes() => AssertPublishes(ResolveFixture.Functions());

    // ---- What checks/ holds (S06-009, S06-010, S06-011; D17) -------------------------------------

    [Fact]
    public void ACheck_ThatIsAnAgent_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().With(AllSections, Agent("All sections covered", "\"check_result\"",
                "Say whether every section covers the terms of the draft.\n")),
            "S06-010", "Check 'all_sections' has kind agent; a check is computed.", AllSections);

    [Fact]
    public void ACheck_WithAnotherFormat_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(AllSections, h => h["format"] = "boolean"),
            "S06-009", "Check 'all_sections' has format boolean; a check returns check_result.", AllSections);

    [Fact]
    public void AWhile_InChecks_IsRefused() =>
        AssertRefusal(
            WithCheck("again")
                .With("checks/again/index.json", Json(SourceFixture.Node("while", "Checking again", "\"check_result\"", true, true,
                    "\"while\": { \"!\": [{ \"var\": \"check.again.recheck.value.pass\" }] }, \"max\": 3, \"until\": null, "
                    + "\"checkFirst\": false, \"nodes\": [\"recheck\"], \"ret\": \"check.again.recheck\"")))
                .With("checks/again/recheck.md", Md(SourceFixture.Node("computed", "Rechecking", "\"check_result\"", true, true,
                    "\"if\": null, " + TermsSubset), "")),
            "S06-011", "checks/again/index.json in checks/ is while; checks/ holds checks and the loops over them.",
            "checks/again/index.json");

    [Fact]
    public void ACall_InChecks_IsRefused() =>
        AssertRefusal(
            WithCheck("again").With("checks/again.md", Md(SourceFixture.Node("call", "Drafting again", Plain, false, true,
                "\"if\": null, \"function\": \"draft_section\", "
                + "\"with\": { \"standard\": \"snippet.standards.hpi\", \"draft\": \"input.consult_draft.content\" }"), "")),
            "S06-011", "checks/again.md in checks/ is call; checks/ holds checks and the loops over them.", "checks/again.md");

    // ---- What a check loop runs over (S06-014; D17) ----------------------------------------------

    [Fact]
    public void ACheckLoop_OverAnInput_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions()
                .WithJson("manifest.json", m => m["inputs"]!["terms"] = JsonNode.Parse(
                    "{ \"label\": \"Terms\", \"description\": null, \"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }, "
                    + "\"required\": true, \"channel\": null }"))
                .WithJson(Coverage, h => OverList(h, "input.terms.value")),
            "S06-014", "Check loop 'section_coverage' runs over input.terms.value; a check loop runs over a production loop's .items.",
            Coverage);

    [Fact]
    public void ACheckLoop_OverANodesValue_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(Coverage, h => OverList(h, "node.standards.value")),
            "S06-014", "Check loop 'section_coverage' runs over node.standards.value; a check loop runs over a production loop's .items.",
            Coverage);

    [Fact]
    public void ACheckLoop_OverACheckLoopsItems_Publishes() =>
        AssertPublishes(
            WithCheck("recoverage")
                .With("checks/recoverage/index.json", Json(SourceFixture.Node("for", "Coverage, checked again", ArrayOf("check_result"), true, true,
                    "\"in\": \"check.section_coverage.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                    + "\"max\": null, \"itemLabel\": \"label\", \"nodes\": [\"again\"], \"ret\": \"check.recoverage.again\"")))
                .With("checks/recoverage/again.md", Md(SourceFixture.Node("computed", "Terms covered again", "\"check_result\"", true, true,
                    "\"if\": { \"===\": [{ \"var\": \"item.state\" }, \"completed\"] }, " + TermsSubset), "")));

    // ---- A gate check's if (S06-035; D18) --------------------------------------------------------

    [Fact]
    public void AGateChecksIf_ReadingANullableValue_IsRefused() =>
        AssertRefusal(
            WithOptionalInput().WithJson(AllSections, h => h["if"] = JsonNode.Parse(ReadsBilling)),
            "S06-035", "Check 'all_sections' gates deliverable 'consult', and its if reads input.include_billing.value, which can be null; "
            + "a gate check's if reads values that are always set.",
            AllSections);

    [Fact]
    public void ACheckThatGatesNothing_MayReadOne() =>
        AssertPublishes(
            WithOptionalInput()
                .With("checks/index.json", Index(["section_coverage", "all_sections", "billing_terms"]))
                .With("checks/billing_terms.md", Md(SourceFixture.Node("computed", "Billing terms covered", "\"check_result\"", true, true,
                    $"\"if\": {ReadsBilling}, " + TermsSubset), "")));

    // ---- A deliverable's check (A08-001) ---------------------------------------------------------

    [Fact]
    public void ADeliverablesCheck_NamingANode_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(Consult, h => h["check"] = "node.extract_note_terms"),
            "A08-001", "Deliverable 'consult' is checked by node.extract_note_terms; a deliverable's check names a check.*.", Consult);

    [Fact]
    public void ADeliverablesCheck_NamingNoCheck_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(Consult, h => h["check"] = "check.every_section"),
            "A08-001", "Deliverable 'consult' is checked by check.every_section; a deliverable's check names a check.*.", Consult);

    [Fact]
    public void ADeliverablesCheck_NamingACheckInsideALoop_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(Consult, h => h["check"] = "check.coverage"),
            "A08-001", "Deliverable 'consult' is checked by check.coverage; a deliverable's check names a check.*.", Consult);

    [Fact]
    public void ADeliverablesCheck_NamingACheckLoop_IsRefused() =>
        AssertRefusal(
            ResolveFixture.Functions().WithJson(Consult, h => h["check"] = "check.section_coverage"),
            "A08-001", "Deliverable 'consult' is checked by check.section_coverage; a deliverable's check names a check.*.", Consult);
}
