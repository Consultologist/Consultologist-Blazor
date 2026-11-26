using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance.V31;
using Consultologist.Web.Shared.History31;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 17 (#1509; part 09 § 6.4, part 08 §§ 3, 3.1, 8; R7, D7): History's Inputs and Run facts sections at storage 12.
/// Each input with its origin and hash, a document's kind and extraction versions, an input's elements numbered from one,
/// an earlier run's element linking to its job; each run fact read with its zone, and each run spot under the fact it reads,
/// its output shown while it is retained and its hash after the drop (A12-002); A12-003 beside a body whose two hashes differ.
/// </summary>
public class History31InputsTests : ClientRenderTestContext
{
    private const string JobId = "31aa31aa31aa31aa31aa31aa31aa31aa";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 16, 0, 0, TimeSpan.Zero);

    public History31InputsTests()
    {
        Services.AddSingleton<TimeProvider>(new FixedClock(Now));
    }

    private static JsonArray Inputs(string json) => JsonNode.Parse(json)!.AsArray();

    private static RecordRowRef Ref(string key, string section, string json) =>
        new(key, section, JsonSerializer.SerializeToElement(JsonNode.Parse(json)!));

    private IRenderedComponent<RecordView31> Render31(string? inputs = null, string? runFacts = null, params RecordRowRef[] rows)
    {
        var header = Record31Fixtures.Header() with
        {
            JobId = JobId,
            Inputs = inputs is null ? null : Inputs(inputs),
            RunFacts = runFacts is null ? null : JsonNode.Parse(runFacts)!.AsObject(),
        };
        return Render<RecordView31>(parameters => parameters
            .Add(p => p.Detail, header)
            .Add(p => p.Record, Record31.From(header, rows))
            .Add(p => p.Texts, new RowText31(AIService, JobId)));
    }

    private static string Sha(char c) => "sha256:" + new string(c, 8);

    // ----- Inputs ---------------------------------------------------------------------------------------------------

    [Fact]
    public void EachInput_ShowsItsOrigin_AndHash()
    {
        var page = Render31($$"""
            [ { "id": "consult_draft", "origin": "typed", "hash": "{{Sha('a')}}" },
              { "id": "referral", "origin": "transcript", "hash": "{{Sha('b')}}" } ]
            """);

        var items = page.FindAll("#history31-inputs .history31-input");
        Assert.Equal(2, items.Count);
        Assert.Contains("consult_draft", items[0].TextContent);
        Assert.Contains("typed", items[0].TextContent);
        Assert.Contains(Sha('a'), items[0].TextContent);
        Assert.Contains("referral", items[1].TextContent);
        Assert.Contains("transcript", items[1].TextContent);
        Assert.Contains(Sha('b'), items[1].TextContent);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("ambient-note")]
    public void ADocument_ShowsItsKind_Extractor_Parser_Converter_AndOcr(string kind)
    {
        var page = Render31($$"""
            [ { "id": "scan", "origin": "document", "hash": "{{Sha('c')}}", "kind": "{{kind}}", "extractor": "pdfpig@0.1.9",
                "converter": { "name": "markitdown", "version": "0.0.2" }, "parser": { "name": "PdfPig", "version": "0.1.9" },
                "ocr": { "service": "azure-document-intelligence", "model": "prebuilt-read-2024-11-30" } } ]
            """);

        var text = page.Find("#history31-inputs .history31-input").TextContent;
        Assert.Contains("document", text);
        Assert.Contains(kind, text);
        Assert.Contains("pdfpig@0.1.9", text);
        Assert.Contains("markitdown", text);
        Assert.Contains("0.0.2", text);
        Assert.Contains("PdfPig", text);
        Assert.Contains("azure-document-intelligence", text);
        Assert.Contains("prebuilt-read-2024-11-30", text);
    }

    [Fact]
    public void ANullStep_IsNotListed()
    {
        var page = Render31($$"""
            [ { "id": "scan", "origin": "document", "hash": "{{Sha('c')}}", "kind": "pdf", "extractor": null,
                "converter": null, "parser": { "name": "PdfPig", "version": "0.1.9" }, "ocr": null } ]
            """);

        var text = page.Find("#history31-inputs .history31-input").TextContent;
        Assert.Contains("PdfPig", text);
        Assert.DoesNotContain("Converter", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OCR", text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(page.FindAll(".history31-input__converter"));
        Assert.Empty(page.FindAll(".history31-input__ocr"));
    }

    [Fact]
    public void AnInputOfSeveralElements_ListsThem_NumberedFromOne()
    {
        var page = Render31($$"""
            [ { "id": "prior_letters", "origin": "document", "hash": "{{Sha('d')}}",
                "elements": [ { "origin": "typed" },
                              { "origin": "document", "kind": "pdf", "parser": { "name": "PdfPig", "version": "0.1.9" } } ] } ]
            """);

        var elements = page.FindAll("#history31-inputs .history31-input__element");
        Assert.Equal(2, elements.Count);
        Assert.Contains("1", elements[0].QuerySelector(".history31-input__number")!.TextContent);
        Assert.Contains("typed", elements[0].TextContent);
        Assert.Contains("2", elements[1].QuerySelector(".history31-input__number")!.TextContent);
        Assert.Contains("PdfPig", elements[1].TextContent);
    }

    [Fact]
    public void APreviousRunElement_LinksToItsJob_AndSaysItCameFromAModel()
    {
        var page = Render31($$"""
            [ { "id": "prior_letters", "origin": "document", "hash": "{{Sha('d')}}",
                "elements": [ { "origin": "typed" },
                              { "origin": "previous-run", "jobId": "0256b7140256b7140256b7140256b714", "resultId": "deliverable.patient.letter", "modelDerived": true } ] },
              { "id": "last_note", "origin": "previous-run", "hash": "{{Sha('e')}}",
                "jobId": "99aa99aa99aa99aa99aa99aa99aa99aa", "resultId": "deliverable.emr.note", "modelDerived": true } ]
            """);

        var inputs = page.FindAll("#history31-inputs .history31-input");
        var element = inputs[0].QuerySelectorAll(".history31-input__element")[1];
        Assert.Equal("/history/0256b7140256b7140256b7140256b714", element.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("model", element.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deliverable.patient.letter", element.TextContent);

        // an input an earlier run's deliverable alone filled: the same, beside the input's own origin
        var single = inputs[1];
        Assert.Equal("/history/99aa99aa99aa99aa99aa99aa99aa99aa", single.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("previous-run", single.TextContent);
        Assert.Contains("model", single.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    // ----- Run facts ------------------------------------------------------------------------------------------------

    [Fact]
    public void RunFacts_ShowEachFactRead_WithTheZoneBesideTheDate()
    {
        var page = Render31(runFacts: """
            { "date": { "value": "2026-11-16", "timeZone": "America/Toronto" }, "id": "31aa", "engine": "2026.12.1" }
            """);

        var facts = page.FindAll("#history31-run-facts .history31-run-fact");
        Assert.Equal(3, facts.Count);
        var date = facts.Single(f => f.TextContent.Contains("run.date"));
        Assert.Contains("2026-11-16", date.TextContent);
        Assert.Contains("America/Toronto", date.TextContent);
        Assert.Contains("run.id", facts.Select(f => f.TextContent).Single(t => t.Contains("31aa")));
        Assert.Single(facts, f => f.TextContent.Contains("run.engine") && f.TextContent.Contains("2026.12.1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    public void NoFactsRead_SaysSo(string? runFacts)
    {
        var page = Render31(runFacts: runFacts);

        Assert.Empty(page.FindAll("#history31-run-facts .history31-run-fact"));
        Assert.NotEmpty(page.FindAll("#history31-run-facts .history31-run-facts__none"));
    }

    private const string BillingWithSpot = """
        { "label": "Billing note", "inputHash": "sha256:11111111", "runNeutralHash": "sha256:22222222",
          "runSpots": [ { "id": "node.billing/spot:1", "site": 2, "pass": null, "start": 14, "length": 16, "reads": "run.date",
                          "valueHash": "sha256:v1", "output": "16 November 2026", "outputHash": "sha256:o1" } ] }
        """;

    [Fact]
    public void EachRunSpot_IsListedUnderTheFactItReads_WithItsBodyKey_Site_AndOutput()
    {
        var page = Render31(
            runFacts: """{ "date": { "value": "2026-11-16", "timeZone": "America/Toronto" } }""",
            rows:
            [
                Ref("node.billing", "node", BillingWithSpot),
                Ref("deliverable.emr.note", "deliverable", """
                    { "label": "EMR note", "composedHash": "sha256:33333333", "runNeutralHash": null,
                      "runSpots": [ { "id": "deliverable.emr.note/spot:1", "site": 1, "pass": [2], "start": 0, "length": 10, "reads": "run.date",
                                      "valueHash": "sha256:v1", "output": "2026-11-16", "outputHash": "sha256:o2" } ] }
                    """),
                Ref("node.plan", "node", """
                    { "label": "Plan", "runSpots": [],
                      "macros": [ { "id": "node.plan/macro:follow_up_text", "macro": "macro.follow_up_text", "contentHash": "sha256:c2",
                                    "runNeutralHash": "sha256:c3",
                                    "runSpots": [ { "id": "node.plan/macro:follow_up_text/spot:1", "site": 1, "pass": null, "start": 3, "length": 4,
                                                    "reads": "run.id", "valueHash": "sha256:v9", "output": "31aa", "outputHash": "sha256:o9" } ] } ] }
                    """),
            ]);

        var date = page.FindAll("#history31-run-facts .history31-run-fact").Single(f => f.TextContent.Contains("run.date"));
        var spots = date.QuerySelectorAll(".history31-spot");
        Assert.Equal(2, spots.Length);
        Assert.Contains("node.billing", spots[0].TextContent);
        Assert.Contains("2", spots[0].QuerySelector(".history31-spot__site")!.TextContent);
        Assert.Contains("16 November 2026", spots[0].TextContent);
        Assert.Contains("deliverable.emr.note", spots[1].TextContent);
        Assert.Contains("2026-11-16", spots[1].TextContent);

        // a macro row's own spots are gathered too, under the fact they read (run.id has no fact row here: it still has a home)
        var all = page.FindAll("#history31-run-facts .history31-spot");
        Assert.Contains(all, s => s.TextContent.Contains("node.plan/macro:follow_up_text") && s.TextContent.Contains("31aa"));
    }

    private const string RunDependentComputed = """
        { "label": "Salutation", "inputHash": "sha256:44444444", "runNeutralHash": "sha256:55555555",
          "runSpots": [ { "id": "node.salutation/spot:1", "site": 1, "pass": null, "start": 5, "length": 15, "reads": "run.date",
                          "valueHash": "sha256:v2", "output": null, "outputHash": "sha256:o3" } ] }
        """;

    [Fact]
    public async Task ASpotWhoseOutputIsNotInTheRow_ShowsItWhileRetained_ThenItsHash()
    {
        AIService.GetJobRowAsync(JobId, "node.salutation").Returns(new RecordRowText(
            "node.salutation", "node", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { content = "Dear Margaret Okafor," }), false));
        var page = Render31(
            runFacts: """{ "date": { "value": "2026-11-16", "timeZone": "America/Toronto" } }""",
            rows: [Ref("node.salutation", "node", RunDependentComputed)]);

        // nothing is fetched until the disclosure is opened
        await AIService.DidNotReceive().GetJobRowAsync(Arg.Any<string>(), Arg.Any<string>());
        Assert.DoesNotContain("Margaret", page.Markup);
        Assert.Contains("sha256:o3", page.Markup);

        page.Find(".history31-spot__show").Click();

        page.WaitForAssertion(() => Assert.Contains("Margaret Okafor", page.Find(".history31-spot__output").TextContent));
        await AIService.Received(1).GetJobRowAsync(JobId, "node.salutation");
        Assert.Equal("true", page.Find(".history31-spot__show").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void ASpotWhoseOutputIsNotInTheRow_AfterTheDrop_ShowsItsHash_AndA12002()
    {
        AIService.GetJobRowAsync(JobId, "node.salutation").Returns(new RecordRowText(
            "node.salutation", "node", JsonSerializer.SerializeToElement(new { }), null, true));
        var page = Render31(
            runFacts: """{ "date": { "value": "2026-11-16", "timeZone": "America/Toronto" } }""",
            rows: [Ref("node.salutation", "node", RunDependentComputed)]);

        page.Find(".history31-spot__show").Click();

        page.WaitForAssertion(() => Assert.Contains(HistorySentences31.TextDeleted, page.Find(".history31-spot").TextContent));
        Assert.Contains("sha256:o3", page.Find(".history31-spot").TextContent);
        Assert.Empty(page.FindAll(".history31-spot__output"));
    }

    [Fact]
    public void ABodyWhoseTwoHashesDiffer_SaysA12003()
    {
        var page = Render31(
            runFacts: """{ "date": { "value": "2026-11-16", "timeZone": "America/Toronto" } }""",
            rows:
            [
                Ref("node.billing", "node", BillingWithSpot),
                // equal hashes: nothing to say; none at all: nothing to say
                Ref("node.same", "node", """{ "inputHash": "sha256:66666666", "runNeutralHash": "sha256:66666666", "runSpots": [] }"""),
                Ref("node.plain", "node", """{ "inputHash": "sha256:77777777", "runNeutralHash": null, "runSpots": [] }"""),
            ]);

        var bodies = page.FindAll("#history31-run-facts .history31-run-body");
        var billing = Assert.Single(bodies, b => b.TextContent.Contains("node.billing"));
        Assert.Contains(HistorySentences31.RunFactsDiffer, billing.TextContent);
        Assert.Contains("sha256:11111111", billing.TextContent);
        Assert.Contains("sha256:22222222", billing.TextContent);
        Assert.Equal(1, page.Markup.Split(HistorySentences31.RunFactsDiffer).Length - 1);
    }
}
