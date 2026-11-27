using System.Text.Json;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance.V31;
using Consultologist.Web.Shared.History31;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 19 (#1509; part 09 § 6.4 "Checks", part 08 § 6; R8): History's Checks section at storage 12. Each check row has
/// four lines (checks, reads, result, effect); a check loop expands per item beside the production item it is aligned with;
/// an aggregate shows its failWith; a skipped check its reason; the rows come in the order recorded. Words are shown as stored.
/// </summary>
public class History31ChecksTests : ClientRenderTestContext
{
    private const string JobId = "31aa31aa31aa31aa31aa31aa31aa31aa";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 16, 0, 0, TimeSpan.Zero);

    public History31ChecksTests()
    {
        Services.AddSingleton<TimeProvider>(new FixedClock(Now));
    }

    private IRenderedComponent<RecordView31> Render31(params RecordRowRef[] extra)
    {
        var header = Record31Fixtures.Header() with { JobId = JobId };
        var rows = Record31Fixtures.Job().Rows.Concat(extra).ToList();
        return Render<RecordView31>(parameters => parameters
            .Add(p => p.Detail, header)
            .Add(p => p.Record, Record31.From(header, rows))
            .Add(p => p.Texts, new RowText31(AIService, JobId)));
    }

    private static RecordRowRef Check(string key, string detail, RowState state = RowState.Completed, string? reason = null, PlanKind kind = PlanKind.Computed) =>
        Record31Fixtures.NodeRow(key, kind, state, reason, detail: detail);

    private static IElement RowOf(IRenderedComponent<RecordView31> page, string key) =>
        page.Find($"#history31-checks [id=\"row-{key}\"]");

    private static bool HasRow(IRenderedComponent<RecordView31> page, string key) =>
        page.FindAll($"#history31-checks [id=\"row-{key}\"]").Count > 0;

    private static IElement LineOf(IRenderedComponent<RecordView31> page, string key) =>
        RowOf(page, key).QuerySelector(".history31-check__line")!;

    private static IElement BodyOf(IRenderedComponent<RecordView31> page, string key) =>
        RowOf(page, key).Children.Single(child => child.ClassList.Contains("history31-check__body"));

    private static void Open(IRenderedComponent<RecordView31> page, string key) =>
        LineOf(page, key).QuerySelector(".history31-check__toggle")!.Click();

    private static string Dd(IElement body, string label)
    {
        var dt = body.QuerySelectorAll("dt").Single(d => d.TextContent.Trim() == label);
        return dt.NextElementSibling!.TextContent;
    }

    private void TextOf(string key, JsonObject? text, bool deleted = false) =>
        AIService.GetJobRowAsync(JobId, key).Returns(new RecordRowText(key, "check", JsonSerializer.SerializeToElement(new { }),
            deleted ? null : JsonSerializer.SerializeToElement(text ?? new JsonObject()), deleted));

    private int RowFetches() =>
        AIService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IAIEndpointService.GetJobRowAsync));

    private static readonly RecordRowRef Full = Check("check.full", """
        { "label": "Full", "checked": { "ref": "deliverable.refused", "hash": "sha256:b3" },
          "reads": [ { "path": "node.plan", "hash": "sha256:r1" }, { "path": "check.gate", "hash": "sha256:r2" }, { "path": "input.parts", "hash": "sha256:r3" } ],
          "pass": false, "failWith": "Sample Oncology Clinic needs every part covered.", "passed": 7, "total": 9, "effect": "released" }
        """);

    [Fact]
    public void ARow_IsCollapsedUntilOpened_AndItsTextIsFetchedOnceThen()
    {
        TextOf("check.full", new JsonObject());
        var page = Render31(Full);

        Assert.Equal("false", LineOf(page, "check.full").QuerySelector(".history31-check__toggle")!.GetAttribute("aria-expanded"));
        Assert.Empty(RowOf(page, "check.full").QuerySelectorAll(".history31-check__body"));
        Assert.Equal(0, RowFetches());

        Open(page, "check.full");
        Open(page, "check.full");
        Open(page, "check.full");

        Assert.Equal(1, RowFetches());
    }

    [Fact]
    public void ACheckRow_HasFourLines_Checks_Reads_Result_Effect()
    {
        var page = Render31(Full);

        Open(page, "check.full");

        var labels = BodyOf(page, "check.full").QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()).ToList();
        Assert.Equal(["checks", "reads", "result", "effect"], labels);
        Assert.Equal("Full", LineOf(page, "check.full").QuerySelector(".history31-check__title")!.TextContent.Trim());
        Assert.Equal("Completed", LineOf(page, "check.full").QuerySelector(".history31-check__status")!.TextContent.Trim());
    }

    [Fact]
    public void Checks_NamesWhatItRanOn_WithThatOutputsHash_AndLinksToIt()
    {
        var page = Render31(Full);

        Open(page, "check.full");

        var checkedLine = BodyOf(page, "check.full").QuerySelector(".history31-check__checked")!;
        Assert.Equal("/history/" + JobId + "#row-deliverable.refused", checkedLine.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("deliverable.refused", checkedLine.QuerySelector("a")!.TextContent.Trim());
        Assert.Contains("sha256:b3", checkedLine.TextContent);
    }

    [Fact]
    public void Reads_LinkEachToItsRow()
    {
        var page = Render31(Full);

        Open(page, "check.full");

        var reads = BodyOf(page, "check.full").QuerySelectorAll(".history31-check__read").ToList();
        Assert.Equal(3, reads.Count);
        Assert.Equal("/history/" + JobId + "#row-node.plan", reads[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("/history/" + JobId + "#row-check.gate", reads[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("sha256:r1", reads[0].TextContent);
        // an input is not a row: shown, not linked
        Assert.Null(reads[2].QuerySelector("a"));
        Assert.Contains("input.parts", reads[2].TextContent);
    }

    [Fact]
    public void ALinkFromAReadToANodeRow_RevealsItInNodes()
    {
        var page = Render31(
            Check("check.deep", """{ "label": "Deep", "reads": [ { "path": "node.sections.draft:1", "hash": "sha256:r1" } ] }"""));
        Open(page, "check.deep");

        BodyOf(page, "check.deep").QuerySelector(".history31-check__read a")!.Click();

        Assert.Equal("true", page.Find("#history31-nodes [id=\"row-node.sections\"] .history31-node__toggle").GetAttribute("aria-expanded"));
        Assert.Equal("true", page.Find("#history31-nodes [id=\"row-node.sections:1\"] .history31-node__toggle").GetAttribute("aria-expanded"));
        Assert.NotEmpty(page.FindAll("#history31-nodes [id=\"row-node.sections.draft:1\"]"));
    }

    [Fact]
    public void Result_ShowsPassOrFail_TheCounts_AndFailWith()
    {
        var page = Render31(Full,
            Check("check.ok", """{ "label": "Ok", "pass": true, "failWith": null, "passed": 3, "total": 3 }"""));

        Open(page, "check.full");
        Open(page, "check.ok");

        var failed = BodyOf(page, "check.full").QuerySelector(".history31-check__result")!;
        Assert.Equal("fail", failed.QuerySelector(".history31-check__verdict")!.TextContent.Trim());
        Assert.Equal("7 of 9", failed.QuerySelector(".history31-check__counts")!.TextContent.Trim());
        Assert.Equal("Sample Oncology Clinic needs every part covered.", failed.QuerySelector(".history31-check__fail-with")!.TextContent.Trim());

        var passed = BodyOf(page, "check.ok").QuerySelector(".history31-check__result")!;
        Assert.Equal("pass", passed.QuerySelector(".history31-check__verdict")!.TextContent.Trim());
        Assert.Equal("3 of 3", passed.QuerySelector(".history31-check__counts")!.TextContent.Trim());
        Assert.Null(passed.QuerySelector(".history31-check__fail-with"));
    }

    private static readonly RecordRowRef Lists = Check("check.terms", """
        { "label": "Terms", "pass": false, "passed": 1, "total": 3, "failWith": "Some terms are missing.",
          "uncoveredHash": "sha256:u1", "uncoveredSize": 2, "untestedHash": "sha256:t1", "untestedSize": 1 }
        """);

    [Fact]
    public void UncoveredAndUntested_AreShownWhileRetained_ThenTheirHashAndSize()
    {
        TextOf("check.terms", new JsonObject
        {
            ["uncovered"] = new JsonArray("letrozole", "anastrozole"),
            ["untested"] = new JsonArray("tamoxifen"),
        });
        var page = Render31(Lists);

        Open(page, "check.terms");

        page.WaitForAssertion(() =>
        {
            var uncovered = BodyOf(page, "check.terms").QuerySelector("[data-list=uncovered]")!;
            Assert.Equal(["letrozole", "anastrozole"], uncovered.QuerySelectorAll("li").Select(li => li.TextContent.Trim()));
            Assert.DoesNotContain("sha256:u1", uncovered.TextContent);
            Assert.Equal(["tamoxifen"], BodyOf(page, "check.terms").QuerySelector("[data-list=untested]")!.QuerySelectorAll("li").Select(li => li.TextContent.Trim()));
            Assert.DoesNotContain(HistorySentences31.TextDeleted, BodyOf(page, "check.terms").TextContent);
        });

        // after the drop
        TextOf("check.terms", null, deleted: true);
        var after = Render31(Lists);
        Open(after, "check.terms");

        after.WaitForAssertion(() =>
        {
            var body = BodyOf(after, "check.terms");
            Assert.Contains("sha256:u1", body.QuerySelector("[data-list=uncovered]")!.TextContent);
            Assert.Contains("2", body.QuerySelector("[data-list=uncovered] .history31-check__list-size")!.TextContent);
            Assert.Contains("sha256:t1", body.QuerySelector("[data-list=untested]")!.TextContent);
            Assert.Empty(body.QuerySelectorAll("[data-list] li"));
            Assert.Contains(HistorySentences31.TextDeleted, body.TextContent);
        });
    }

    [Fact]
    public void AFailedFetch_SaysTheRecordIsNotAvailable_AndKeepsTheHashes()
    {
        AIService.GetJobRowAsync(JobId, "check.terms").Returns(Task.FromException<RecordRowText?>(new HttpRequestException()));
        var page = Render31(Lists);

        Open(page, "check.terms");

        page.WaitForAssertion(() =>
        {
            Assert.Contains(HistorySentences31.RecordNotAvailable, BodyOf(page, "check.terms").TextContent);
            Assert.Contains("sha256:u1", BodyOf(page, "check.terms").TextContent);
        });
    }

    [Fact]
    public void Effect_IsTheStoredWord()
    {
        var page = Render31(
            Check("check.one", """{ "label": "One", "effect": "released" }"""),
            Check("check.two", """{ "label": "Two", "effect": "blocked deliverable.note; released deliverable.summary" }"""),
            Check("check.three", """{ "label": "Three", "effect": "read by node.x" }"""),
            Check("check.none", """{ "label": "None", "pass": true, "effect": null }"""));

        foreach (var key in new[] { "check.one", "check.two", "check.three", "check.none" })
        {
            Open(page, key);
        }

        Assert.Equal("released", Dd(BodyOf(page, "check.one"), "effect").Trim());
        Assert.Equal("blocked deliverable.note; released deliverable.summary", Dd(BodyOf(page, "check.two"), "effect").Trim());
        Assert.Equal("read by node.x", Dd(BodyOf(page, "check.three"), "effect").Trim());
        Assert.DoesNotContain(BodyOf(page, "check.none").QuerySelectorAll("dt"), dt => dt.TextContent.Trim() == "effect");
    }

    [Fact]
    public void ACheckLoop_ExpandsPerItem_BesideItsProductionItem_WithTheSameName()
    {
        var page = Render31();

        Open(page, "check.part_coverage");

        Assert.True(HasRow(page, "check.part_coverage:1"));
        Assert.True(HasRow(page, "check.part_coverage:2"));
        var link = LineOf(page, "check.part_coverage:2").QuerySelector(".history31-check__aligned a")!;
        Assert.Equal("/history/" + JobId + "#row-node.sections:2", link.GetAttribute("href"));
        Assert.Equal("Sections 2", link.TextContent.Trim());
        Assert.Empty(LineOf(page, "check.part_coverage").QuerySelectorAll(".history31-check__aligned"));

        // the check inside the item, and its own alignment
        Open(page, "check.part_coverage:2");
        Assert.True(HasRow(page, "check.part_coverage.coverage:2"));
        Assert.Contains(BodyOf(page, "check.part_coverage:2").QuerySelectorAll("[id]"), e => e.Id == "row-check.part_coverage.coverage:2");
        Assert.Equal("/history/" + JobId + "#row-node.sections:2",
            LineOf(page, "check.part_coverage.coverage:2").QuerySelector(".history31-check__aligned a")!.GetAttribute("href"));
    }

    [Fact]
    public void AnItemNamedByItsLoop_ShowsTheNameTheProductionItemShows()
    {
        var page = Render31(
            Record31Fixtures.NodeRow("node.parts", PlanKind.For, RowState.Completed, detail: """{ "label": "Parts", "count": 1 }"""),
            Record31Fixtures.NodeRow("node.parts:1", PlanKind.For, RowState.Completed, detail: """{ "label": "Parts", "itemName": "Intake summary" }"""),
            Check("check.parts", """{ "label": "Part checks", "count": 1 }""", kind: PlanKind.For),
            Check("check.parts:1", """{ "label": "Part checks", "itemName": "Intake summary", "alignedWith": "node.parts:1" }""", kind: PlanKind.For));

        Open(page, "check.parts");

        Assert.Equal("Intake summary", LineOf(page, "check.parts:1").QuerySelector(".history31-check__title")!.TextContent.Trim());
        Assert.Equal("Intake summary", LineOf(page, "check.parts:1").QuerySelector(".history31-check__aligned a")!.TextContent.Trim());
    }

    [Fact]
    public void AnAggregate_ShowsItsFailWithSentence()
    {
        var page = Render31();

        Open(page, "check.gate");

        var result = BodyOf(page, "check.gate").QuerySelector(".history31-check__result")!;
        Assert.Equal("fail", result.QuerySelector(".history31-check__verdict")!.TextContent.Trim());
        Assert.Equal("The plan is not complete.", result.QuerySelector(".history31-check__fail-with")!.TextContent.Trim());
        Assert.Equal("blocked", Dd(BodyOf(page, "check.gate"), "effect").Trim());
    }

    [Fact]
    public void ASkippedCheck_ShowsItsReason()
    {
        var page = Render31();

        var reason = LineOf(page, "check.seen").QuerySelector(".history31-check__reason")!.TextContent;

        Assert.Contains("skipped: input.kind.value = \"letter\" is false", reason);
        Assert.Equal("Skipped", LineOf(page, "check.seen").QuerySelector(".history31-check__status")!.TextContent.Trim());
    }

    [Fact]
    public void AGatesRow_ComesAfterItsDeliverables_InTheOrderRecorded()
    {
        // the gate settles last, so its row starts after the others', though its key sorts first
        var page = Render31(
            Check("check.aaa_gate", """{ "label": "Gate", "startedAtUtc": "2026-11-16T14:09:00Z", "effect": "released" }"""),
            Check("check.zzz_first", """{ "label": "First", "startedAtUtc": "2026-11-16T14:01:00Z" }"""),
            Check("check.mmm_second", """{ "label": "Second", "startedAtUtc": "2026-11-16T14:02:00Z" }"""));

        var keys = page.FindAll("#history31-checks > ul > li").Select(li => (li.GetAttribute("id") ?? "")["row-".Length..]).ToList();

        Assert.Equal(new[] { "check.zzz_first", "check.mmm_second", "check.aaa_gate" }, keys.Take(3));
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void ALinkToACheckRow_OpensItAndTheRowsItSitsIn()
    {
        var page = Render31();
        var navigation = Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo("#row-check.part_coverage.coverage:2");

        page.WaitForAssertion(() => Assert.True(HasRow(page, "check.part_coverage.coverage:2")));
        foreach (var key in new[] { "check.part_coverage", "check.part_coverage:2", "check.part_coverage.coverage:2" })
        {
            Assert.Equal("true", LineOf(page, key).QuerySelector(".history31-check__toggle")!.GetAttribute("aria-expanded"));
        }
    }

    [Fact]
    public void ALinkClickedInTheSection_ToACheckRow_OpensIt()
    {
        var page = Render31(Check("check.pointer", """{ "label": "Pointer", "reads": [ { "path": "check.part_coverage.coverage:2", "hash": "sha256:r1" } ] }"""));
        Open(page, "check.pointer");

        BodyOf(page, "check.pointer").QuerySelector(".history31-check__read a")!.Click();

        page.WaitForAssertion(() => Assert.True(HasRow(page, "check.part_coverage.coverage:2")));
        Assert.Equal("true", LineOf(page, "check.part_coverage.coverage:2").QuerySelector(".history31-check__toggle")!.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void ARowLink_HasTheFullPathAsItsHref_SoANewTabDoesNotGoHome()
    {
        var page = Render31();

        var hrefs = page.FindAll(".history31-banner a").Select(a => a.GetAttribute("href")!).ToList();

        Assert.NotEmpty(hrefs);
        Assert.All(hrefs, href => Assert.StartsWith("/history/" + JobId + "#row-", href, StringComparison.Ordinal));
    }
}
