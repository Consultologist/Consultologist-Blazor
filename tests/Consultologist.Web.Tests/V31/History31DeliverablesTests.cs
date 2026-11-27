using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance.V31;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.History31;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 20 (#1509; part 09 § 6.4 "Deliverables", part 08 § 10; R1): History's Deliverables section at storage 12. Each
/// deliverable row shows its stored state, format and composed hash, its template from the pinned version, its reads by
/// template site, its check line, each destination's stored result, and for a bundle what it left out. A released text has
/// a copy control; a blocked one is shown under "Refused, not delivered" with its check and sentence, and nothing on the
/// page offers it to copy or download. After the drop the heading and the hash remain, with A12-002.
/// </summary>
public class History31DeliverablesTests : ClientRenderTestContext
{
    private const string JobId = "31dd31dd31dd31dd31dd31dd31dd31dd";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 16, 0, 0, TimeSpan.Zero);

    private const string ReleasedText = "Dear Dr A. Sample, Margaret Okafor was seen today at Sample Oncology Clinic.";
    private const string RefusedText = "Refused draft: Margaret Okafor should stop letrozole.";

    public History31DeliverablesTests()
    {
        Services.AddSingleton<TimeProvider>(new FixedClock(Now));
    }

    private IRenderedComponent<RecordView31> Render31(JsonElement? resolve = null, Func<ConsultGenerationJobResponse, ConsultGenerationJobResponse>? header = null,
        params RecordRowRef[] extra)
    {
        var detail = Record31Fixtures.Header() with { JobId = JobId };
        detail = header?.Invoke(detail) ?? detail;
        var rows = Record31Fixtures.Job().Rows.Concat(extra).ToList();
        var content = resolve is { } r ? Content(r) : null;
        return Render<RecordView31>(parameters => parameters
            .Add(p => p.Detail, detail)
            .Add(p => p.Record, Record31.From(detail, rows))
            .Add(p => p.Texts, new RowText31(AIService, JobId))
            .Add(p => p.Content, content));
    }

    private static WorkflowPackageContentResponse Content(JsonElement resolve) =>
        new("test/general", "v2026.12.1", 31, JsonSerializer.SerializeToElement(new { specVersion = 31 }), new Dictionary<string, string>(), Resolve: resolve);

    private static RecordRowRef Deliverable(string key, RowState state, string? reason, string detail) =>
        Record31Fixtures.NodeRow(key, PlanKind.Deliverable, state, reason, detail: detail);

    private static IElement RowOf(IRenderedComponent<RecordView31> page, string key) =>
        page.Find($"#history31-deliverables [id=\"row-{key}\"]");

    private static IElement LineOf(IRenderedComponent<RecordView31> page, string key) =>
        RowOf(page, key).QuerySelector(".history31-deliverable__line")!;

    private static IElement BodyOf(IRenderedComponent<RecordView31> page, string key) =>
        RowOf(page, key).Children.Single(child => child.ClassList.Contains("history31-deliverable__body"));

    private static void Open(IRenderedComponent<RecordView31> page, string key) =>
        LineOf(page, key).QuerySelector(".history31-deliverable__toggle")!.Click();

    private void TextOf(string key, string? content, bool deleted = false) =>
        AIService.GetJobRowAsync(JobId, key).Returns(new RecordRowText(key, "deliverable", JsonSerializer.SerializeToElement(new { }),
            deleted ? null : JsonSerializer.SerializeToElement(new { content }), deleted));

    private int RowFetches() =>
        AIService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IAIEndpointService.GetJobRowAsync));

    private static string Dd(IElement body, string label) =>
        body.QuerySelectorAll("dt").Single(dt => dt.TextContent.Trim() == label).NextElementSibling!.TextContent.Trim();

    // ----- the row ------------------------------------------------------------------------------------------------------

    [Fact]
    public void EachDeliverable_IsARow_CollapsedUntilOpened_AndItsTextIsFetchedOnceThen()
    {
        TextOf("deliverable.emr.note", ReleasedText);
        var page = Render31();

        Assert.Equal(
            ["deliverable.emr.note", "deliverable.failed_note", "deliverable.held_summary", "deliverable.refused", "deliverable.skipped_letter"],
            page.FindAll("#history31-deliverables .history31-deliverables > li").Select(li => li.Id!["row-".Length..]));
        Assert.Empty(RowOf(page, "deliverable.emr.note").QuerySelectorAll(".history31-deliverable__body"));
        Assert.Equal(0, RowFetches());

        Open(page, "deliverable.emr.note");
        Open(page, "deliverable.emr.note");
        Open(page, "deliverable.emr.note");

        Assert.Equal(1, RowFetches());
    }

    [Fact]
    public void ADeliverable_ShowsItsStoredState_Format_AndComposedHash()
    {
        var page = Render31();

        Assert.Equal("released", LineOf(page, "deliverable.emr.note").QuerySelector(".history31-deliverable__state")!.TextContent.Trim());
        Assert.Equal("EMR note", LineOf(page, "deliverable.emr.note").QuerySelector(".history31-deliverable__title")!.TextContent.Trim());
        Assert.Equal("blocked", LineOf(page, "deliverable.refused").QuerySelector(".history31-deliverable__state")!.TextContent.Trim());
        Assert.Equal("held", LineOf(page, "deliverable.held_summary").QuerySelector(".history31-deliverable__state")!.TextContent.Trim());
        Assert.Equal("skipped", LineOf(page, "deliverable.skipped_letter").QuerySelector(".history31-deliverable__state")!.TextContent.Trim());
        Assert.Equal("failed", LineOf(page, "deliverable.failed_note").QuerySelector(".history31-deliverable__state")!.TextContent.Trim());

        Open(page, "deliverable.emr.note");

        var body = BodyOf(page, "deliverable.emr.note");
        Assert.Equal("markdown_block", Dd(body, "format"));
        Assert.Equal("sha256:b1", Dd(body, "composed hash"));
        Assert.Equal("sha256:d1", Dd(body, "template hash"));
    }

    private const string Template = "## Note\n\n{{ node.plan.content }}\n";

    private static JsonElement ResolveWith(string key, string body, string? bundle = null) =>
        JsonSerializer.SerializeToElement(new { deliverables = new Dictionary<string, object> { [key] = new { body, bodyHash = CanonicalJson.HashText(body), bundle } } });

    [Fact]
    public void ItsTemplate_IsShownFromThePinnedVersion_WhenTheHashMatches()
    {
        var page = Render31(ResolveWith("deliverable.pinned", Template), extra:
            Deliverable("deliverable.pinned", RowState.Released, null, $$"""{ "label": "Pinned", "templateHash": "{{CanonicalJson.HashText(Template)}}" }"""));

        Open(page, "deliverable.pinned");

        Assert.Equal(Template, BodyOf(page, "deliverable.pinned").QuerySelector(".history31-deliverable__template")!.TextContent);
        Assert.DoesNotContain(HistorySentences31.TemplateNotRead, BodyOf(page, "deliverable.pinned").TextContent);
    }

    [Fact]
    public void ItsTemplate_IsShownFromThePinnedVersion_AndNotWhenItDoesNot()
    {
        var page = Render31(ResolveWith("deliverable.pinned", Template + "edited"), extra:
            Deliverable("deliverable.pinned", RowState.Released, null, $$"""{ "label": "Pinned", "templateHash": "{{CanonicalJson.HashText(Template)}}" }"""));

        Open(page, "deliverable.pinned");
        // and with no version read at all
        Open(page, "deliverable.emr.note");

        Assert.Null(BodyOf(page, "deliverable.pinned").QuerySelector(".history31-deliverable__template"));
        Assert.Contains(HistorySentences31.TemplateNotRead, BodyOf(page, "deliverable.pinned").TextContent);
        Assert.Contains(HistorySentences31.TemplateNotRead, BodyOf(page, "deliverable.emr.note").TextContent);
    }

    [Theory]
    [InlineData(RowState.Skipped)]
    [InlineData(RowState.Failed)]
    public void ADeliverableWithNoTemplateHash_SaysNothingOfItsTemplate(RowState state)
    {
        // Final review D4: no template ran, so there is none to read: no A12-007.
        var page = Render31(ResolveWith("deliverable.never", Template), extra:
            Deliverable("deliverable.never", state, null, """{ "label": "Never", "composedHash": null }"""));

        Open(page, "deliverable.never");

        Assert.Null(BodyOf(page, "deliverable.never").QuerySelector(".history31-deliverable__template"));
        Assert.DoesNotContain(HistorySentences31.TemplateNotRead, BodyOf(page, "deliverable.never").TextContent);
    }

    // ----- the reads ----------------------------------------------------------------------------------------------------

    private static readonly RecordRowRef Looped = Deliverable("deliverable.looped", RowState.Released, null, """
        { "label": "Looped", "composedHash": "sha256:l0",
          "sites": [ { "site": 1, "at": "1:1", "expr": "node.plan.content", "loop": null },
                     { "site": 2, "at": "3:3", "expr": "item.title", "loop": "node.sections.items" },
                     { "site": 3, "at": "5:1", "expr": "profile.signature", "loop": null } ],
          "substitutions": [ { "site": 1, "pass": null, "reads": "node.plan.content", "hash": "sha256:s1", "start": 0, "size": 4, "renders": null },
                             { "site": 2, "pass": [1], "reads": "item.title", "hash": "sha256:s2", "start": 5, "size": 3, "renders": null },
                             { "site": 2, "pass": [2], "reads": "item.title", "hash": "sha256:s3", "start": 9, "size": 3, "renders": null },
                             { "site": 2, "pass": [3], "reads": "item.title", "hash": "sha256:s4", "start": 13, "size": 3, "renders": null },
                             { "site": 3, "pass": null, "reads": "profile.signature", "hash": "sha256:s5", "start": 17, "size": 12, "renders": null } ],
          "profileReads": [ { "field": "profile.signature", "hash": "sha256:p1" }, { "field": "profile.credentials", "hash": "sha256:p2" } ] }
        """);

    // runes: "Plan" 0..4, "One" 5..8, "Two" 9..12, "Six" 13..16; the signature's 12 runes from 17 run past its end
    private const string LoopedText = "Plan One Two Six Dr A. Sampl";

    [Fact]
    public void Reads_AreGroupedBySite_InSourceOrder()
    {
        var page = Render31(extra: Looped);

        Open(page, "deliverable.looped");

        var sites = BodyOf(page, "deliverable.looped").QuerySelectorAll(".history31-site").ToList();
        Assert.Equal(["1", "2", "3"], sites.Select(site => site.GetAttribute("data-site")));
        Assert.Contains("1:1", sites[0].QuerySelector(".history31-site__at")!.TextContent);
        Assert.Equal("node.plan.content", sites[0].QuerySelector(".history31-site__expr")!.TextContent.Trim());
        Assert.Equal("item.title", sites[1].QuerySelector(".history31-site__expr")!.TextContent.Trim());
    }

    [Fact]
    public void ALoopedSite_IsCollapsedToTimesN_AndExpandsPerPass()
    {
        var page = Render31(extra: Looped);
        Open(page, "deliverable.looped");

        var site = BodyOf(page, "deliverable.looped").QuerySelector("[data-site=\"2\"]")!;
        var toggle = site.QuerySelector(".history31-site__toggle")!;
        Assert.Equal("× 3 passes", toggle.TextContent.Trim());
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Empty(site.QuerySelectorAll(".history31-substitution"));

        toggle.Click();

        site = BodyOf(page, "deliverable.looped").QuerySelector("[data-site=\"2\"]")!;
        Assert.Equal("true", site.QuerySelector(".history31-site__toggle")!.GetAttribute("aria-expanded"));
        var passes = site.QuerySelectorAll(".history31-substitution").ToList();
        Assert.Equal(3, passes.Count);
        Assert.Equal(["pass 1", "pass 2", "pass 3"], passes.Select(p => p.QuerySelector(".history31-substitution__pass")!.TextContent.Trim()));
        Assert.Contains("sha256:s3", passes[1].TextContent);

        // a site in no loop is not collapsed
        Assert.Null(BodyOf(page, "deliverable.looped").QuerySelector("[data-site=\"1\"] .history31-site__toggle"));
        Assert.Single(BodyOf(page, "deliverable.looped").QuerySelectorAll("[data-site=\"1\"] .history31-substitution"));
    }

    [Fact]
    public void EachRead_ShowsItsSizeAndHash_AndItsTextWhileRetained()
    {
        TextOf("deliverable.looped", LoopedText);
        var page = Render31(extra: Looped);
        Open(page, "deliverable.looped");

        page.WaitForAssertion(() =>
        {
            var first = BodyOf(page, "deliverable.looped").QuerySelector("[data-site=\"1\"] .history31-substitution")!;
            Assert.Equal("node.plan.content", first.QuerySelector(".history31-substitution__reads")!.TextContent.Trim());
            Assert.Equal("4 characters", first.QuerySelector(".history31-substitution__size")!.TextContent.Trim());
            Assert.Equal("sha256:s1", first.QuerySelector(".history31-substitution__hash")!.TextContent.Trim());
            Assert.Equal("Plan", first.QuerySelector(".history31-substitution__text")!.TextContent);
        });
        BodyOf(page, "deliverable.looped").QuerySelector("[data-site=\"2\"] .history31-site__toggle")!.Click();
        var passes = BodyOf(page, "deliverable.looped").QuerySelectorAll("[data-site=\"2\"] .history31-substitution").ToList();
        Assert.Equal(["One", "Two", "Six"], passes.Select(p => p.QuerySelector(".history31-substitution__text")!.TextContent));
        // a slice that does not fit the composed text shows its size and hash only
        var signature = BodyOf(page, "deliverable.looped").QuerySelector("[data-site=\"3\"] .history31-substitution")!;
        Assert.Null(signature.QuerySelector(".history31-substitution__text"));
        Assert.Contains("sha256:s5", signature.TextContent);

        // after the drop: sizes and hashes, no text, and A12-002
        TextOf("deliverable.looped", null, deleted: true);
        var after = Render31(extra: Looped);
        Open(after, "deliverable.looped");
        after.WaitForAssertion(() => Assert.Contains(HistorySentences31.TextDeleted, BodyOf(after, "deliverable.looped").TextContent));
        Assert.Empty(BodyOf(after, "deliverable.looped").QuerySelectorAll(".history31-substitution__text"));
        Assert.Contains("sha256:s1", BodyOf(after, "deliverable.looped").TextContent);
        Assert.Contains("4 characters", BodyOf(after, "deliverable.looped").TextContent);
    }

    [Fact]
    public void AProfileRead_ShowsTheFieldAndItsHash()
    {
        var page = Render31(extra: Looped);
        Open(page, "deliverable.looped");

        var reads = BodyOf(page, "deliverable.looped").QuerySelectorAll(".history31-profile-read").ToList();

        Assert.Equal(2, reads.Count);
        Assert.Equal("profile.credentials", reads[1].QuerySelector(".history31-profile-read__field")!.TextContent.Trim());
        Assert.Equal("sha256:p2", reads[1].QuerySelector(".history31-profile-read__hash")!.TextContent.Trim());
    }

    [Fact]
    public void TheSignatureRead_NamesTheBlocksId()
    {
        var page = Render31(header: h => h with { SignatureBlockId = "sig-2", SignatureChosen = "job" }, extra: Looped);
        Open(page, "deliverable.looped");

        var reads = BodyOf(page, "deliverable.looped").QuerySelectorAll(".history31-profile-read").ToList();

        Assert.Equal("profile.signature", reads[0].QuerySelector(".history31-profile-read__field")!.TextContent.Trim());
        Assert.Equal("sha256:p1", reads[0].QuerySelector(".history31-profile-read__hash")!.TextContent.Trim());
        var block = reads[0].QuerySelector(".history31-profile-read__block")!;
        Assert.Equal("sig-2", block.QuerySelector("code")!.TextContent.Trim());
        Assert.Contains("(job)", block.TextContent);
        Assert.Null(reads[1].QuerySelector(".history31-profile-read__block"));
    }

    // ----- the check line, the reason, the destinations, the bundle ------------------------------------------------------

    [Fact]
    public void TheCheckLine_ReadsCheckIdPassedOrFailed_AndLinksToTheChecksRow()
    {
        var page = Render31();
        Open(page, "deliverable.emr.note");
        Open(page, "deliverable.refused");

        var passed = BodyOf(page, "deliverable.emr.note").QuerySelector(".history31-deliverable__check")!;
        Assert.Equal("Check: gate_many passed", Normalise(passed.TextContent));
        Assert.Equal("/history/" + JobId + "#row-check.gate_many", passed.QuerySelector("a")!.GetAttribute("href"));
        var failed = BodyOf(page, "deliverable.refused").QuerySelector(".history31-deliverable__check")!;
        Assert.Equal("Check: gate failed", Normalise(failed.TextContent));

        // following it opens the check's row in Checks
        failed.QuerySelector("a")!.Click();

        page.WaitForAssertion(() => Assert.Equal("true",
            page.Find("#history31-checks [id=\"row-check.gate\"] .history31-check__toggle").GetAttribute("aria-expanded")));
    }

    [Fact]
    public void AnUngatedDeliverable_HasNoCheckLine()
    {
        var page = Render31(extra: Looped);
        Open(page, "deliverable.looped");

        Assert.Null(BodyOf(page, "deliverable.looped").QuerySelector(".history31-deliverable__check"));
    }

    [Fact]
    public void AReleasedDeliverableWhoseCheckDidNotApply_ShowsItsStoredReason()
    {
        var page = Render31(extra: Deliverable("deliverable.unchecked", RowState.Released, "released: check 'seen' didn't apply (its if was false)",
            """{ "label": "Unchecked", "check": "check.seen" }"""));

        Assert.Equal("released: check 'seen' didn't apply (its if was false)",
            LineOf(page, "deliverable.unchecked").QuerySelector(".history31-deliverable__reason")!.TextContent.Trim());
        Open(page, "deliverable.unchecked");
        // the skipped check's stored status, not a verdict it never gave
        Assert.Equal("Check: seen Skipped", Normalise(BodyOf(page, "deliverable.unchecked").QuerySelector(".history31-deliverable__check")!.TextContent));
    }

    [Fact]
    public void EachDestination_ShowsItsStoredResult_ItsReason_AndItsAttempts()
    {
        var page = Render31();
        Open(page, "deliverable.emr.note");

        var destinations = BodyOf(page, "deliverable.emr.note").QuerySelectorAll(".history31-delivery").ToList();

        Assert.Equal(["delivery:deliverable.emr.note/email", "delivery:deliverable.emr.note/emr", "delivery:deliverable.emr.note/pdf"],
            destinations.Select(d => d.Id!["row-".Length..]));
        Assert.Equal(["email", "emr", "pdf"], destinations.Select(d => d.QuerySelector(".history31-delivery__kind")!.TextContent.Trim()));
        Assert.Equal(["sent", "not sent", "failed"], destinations.Select(d => d.QuerySelector(".history31-delivery__result")!.TextContent.Trim()));
        Assert.Equal("not sent: EMR isn't set up for this account", destinations[1].QuerySelector(".history31-delivery__reason")!.TextContent.Trim());
        Assert.Null(destinations[0].QuerySelector(".history31-delivery__reason"));
        var attempts = destinations[2].QuerySelectorAll(".history31-delivery__attempt").ToList();
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, attempt => Assert.Contains("failed: timeout", attempt.TextContent));
        Assert.Contains("2026-11-16T14:00:00", attempts[0].TextContent);
        Assert.Equal("sent", Assert.Single(destinations[0].QuerySelectorAll(".history31-delivery__attempt")).QuerySelector(".history31-delivery__outcome")!.TextContent.Trim());
    }

    [Fact]
    public void AFailedDestination_SaysCouldNotSend()
    {
        var page = Render31();
        Open(page, "deliverable.emr.note");

        var destinations = BodyOf(page, "deliverable.emr.note").QuerySelectorAll(".history31-delivery").ToList();

        Assert.Equal(HistorySentences31.CouldNotSend("PDF"), destinations[2].QuerySelector(".history31-delivery__could-not-send")!.TextContent.Trim());
        Assert.Null(destinations[0].QuerySelector(".history31-delivery__could-not-send"));
        Assert.Null(destinations[1].QuerySelector(".history31-delivery__could-not-send"));
    }

    [Fact]
    public void ABundle_ListsWhatItLeftOut_AndWhy()
    {
        var resolve = JsonSerializer.SerializeToElement(new { deliverables = new Dictionary<string, object>
        {
            ["deliverable.pack.gp_letter"] = new { bundle = "zip" },
            ["deliverable.pack.summary"] = new { bundle = "zip" },
            ["deliverable.pack.addendum"] = new { bundle = "zip" },
            ["deliverable.pack.extra"] = new { bundle = "zip" },
        } });
        var page = Render31(resolve, extra:
        [
            Deliverable("deliverable.pack.gp_letter", RowState.Released, null, """{ "label": "GP letter" }"""),
            Deliverable("deliverable.pack.extra", RowState.Released, null, """{ "label": "Extra" }"""),
            Deliverable("deliverable.pack.summary", RowState.Held, "held: empty", """{ "label": "Summary" }"""),
            Deliverable("deliverable.pack.addendum", RowState.Blocked, "blocked: check 'gate' failed (The plan is not complete.)", """{ "label": "Addendum", "check": "check.gate" }"""),
        ]);

        Open(page, "deliverable.pack.gp_letter");

        var left = BodyOf(page, "deliverable.pack.gp_letter").QuerySelector(".history31-deliverable__left-out")!;
        var items = left.QuerySelectorAll("li").ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("Addendum", items[0].QuerySelector(".history31-left-out__label")!.TextContent.Trim());
        Assert.Equal("blocked", items[0].QuerySelector(".history31-left-out__state")!.TextContent.Trim());
        Assert.Equal("blocked: check 'gate' failed (The plan is not complete.)", items[0].QuerySelector(".history31-left-out__reason")!.TextContent.Trim());
        Assert.Equal("Summary", items[1].QuerySelector(".history31-left-out__label")!.TextContent.Trim());
        Assert.Equal("held: empty", items[1].QuerySelector(".history31-left-out__reason")!.TextContent.Trim());
        // the released one beside it is in the bundle; a deliverable outside the folder is not listed
        Assert.DoesNotContain("Extra", left.TextContent);
        Assert.DoesNotContain("Refused note", left.TextContent);

        // a folder with no bundle lists nothing
        Open(page, "deliverable.emr.note");
        Assert.Null(BodyOf(page, "deliverable.emr.note").QuerySelector(".history31-deliverable__left-out"));
    }

    [Fact]
    public void ALeftOutReason_IsShownAsEveryRowShowsIt()
    {
        // Final review D5: the left-out list words a stateReason as every other row does (HistorySentences31.StateReason).
        const string Stored = "The model service stayed busy for 10 minutes.";
        var resolve = JsonSerializer.SerializeToElement(new { deliverables = new Dictionary<string, object>
        {
            ["deliverable.pack.gp_letter"] = new { bundle = "zip" },
            ["deliverable.pack.summary"] = new { bundle = "zip" },
        } });
        var page = Render31(resolve, extra:
        [
            Deliverable("deliverable.pack.gp_letter", RowState.Released, null, """{ "label": "GP letter" }"""),
            Deliverable("deliverable.pack.summary", RowState.Failed, Stored, """{ "label": "Summary" }"""),
        ]);

        Open(page, "deliverable.pack.gp_letter");

        var reason = BodyOf(page, "deliverable.pack.gp_letter").QuerySelector(".history31-left-out__reason")!.TextContent.Trim();
        Assert.Equal(HistorySentences31.StateReason(Stored), reason);
        Assert.NotEqual(Stored, reason);
    }

    // ----- copy, and the text a check refused (R1) -------------------------------------------------------------------------

    [Fact]
    public void AReleasedDeliverable_HasACopyControl()
    {
        TextOf("deliverable.emr.note", ReleasedText);
        var page = Render31();
        Open(page, "deliverable.emr.note");

        page.WaitForAssertion(() => Assert.Equal(ReleasedText, BodyOf(page, "deliverable.emr.note").QuerySelector(".history31-deliverable__text")!.TextContent));
        var copy = BodyOf(page, "deliverable.emr.note").QuerySelector(".history31-deliverable__copy")!;
        Assert.Equal("Copy note", copy.TextContent.Trim());

        copy.Click();

        var call = Assert.Single(JSInterop.Invocations["navigator.clipboard.writeText"]);
        Assert.Equal(ReleasedText, call.Arguments[0]);
        page.WaitForAssertion(() => Assert.Equal("Copied ✓", BodyOf(page, "deliverable.emr.note").QuerySelector(".history31-deliverable__copy")!.TextContent.Trim()));
    }

    [Fact]
    public void AReleasedDeliverable_WhoseTextWasDropped_HasNoCopyControl_AndSaysA12002()
    {
        TextOf("deliverable.emr.note", null, deleted: true);
        var page = Render31();
        Open(page, "deliverable.emr.note");

        page.WaitForAssertion(() => Assert.Contains(HistorySentences31.TextDeleted, BodyOf(page, "deliverable.emr.note").TextContent));
        Assert.Null(BodyOf(page, "deliverable.emr.note").QuerySelector(".history31-deliverable__copy"));
        Assert.Contains("sha256:b1", BodyOf(page, "deliverable.emr.note").TextContent);
    }

    [Fact]
    public void ABlockedDeliverablesText_IsUnderRefusedNotDelivered_WithItsCheckAndSentence_AndHasNoCopyControl()
    {
        TextOf("deliverable.refused", RefusedText);
        var page = Render31();
        Open(page, "deliverable.refused");

        page.WaitForAssertion(() => Assert.NotNull(BodyOf(page, "deliverable.refused").QuerySelector(".history31-refused__text")));
        var panel = BodyOf(page, "deliverable.refused").QuerySelector(".history31-refused")!;
        var heading = panel.QuerySelector(".history31-refused__heading")!;
        Assert.Equal(HistorySentences31.RefusedHeading, heading.TextContent.Trim());
        Assert.Equal(heading.Id, panel.GetAttribute("aria-labelledby"));
        Assert.Equal(RefusedText, panel.QuerySelector(".history31-refused__text")!.TextContent.Trim());
        // its check, linking to the check's row, with the check's failWith, and the deliverable's sentence
        Assert.Equal("/history/" + JobId + "#row-check.gate", panel.QuerySelector(".history31-refused__check a")!.GetAttribute("href"));
        Assert.Equal("The plan is not complete.", panel.QuerySelector(".history31-refused__fail-with")!.TextContent.Trim());
        Assert.Equal("Refused note was blocked: check 'gate' failed (The plan is not complete.).",
            panel.QuerySelector(".history31-refused__reason")!.TextContent.Trim());
        // the text is only inside the panel; nothing in the row offers to copy it
        Assert.Single(BodyOf(page, "deliverable.refused").QuerySelectorAll("*").Where(e => e.Children.Length == 0 && e.TextContent.Contains("letrozole")));
        Assert.Null(BodyOf(page, "deliverable.refused").QuerySelector(".history31-deliverable__copy"));
        Assert.Null(BodyOf(page, "deliverable.refused").QuerySelector(".history31-deliverable__text"));
        Assert.Empty(panel.QuerySelectorAll("button, fluent-button, a[download]"));
        Assert.DoesNotContain(RowOf(page, "deliverable.refused").QuerySelectorAll("button, fluent-button"),
            button => button.TextContent.Contains("Copy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheRefusedPanel_CannotBeSelected_AndKeepsItsHeadingAtEveryWidth()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Consultologist.Web", "Shared", "History31", "RefusedText31.razor.css"));

        Assert.Matches(@"\.history31-refused\s*\{[^}]*user-select:\s*none", css);
        Assert.DoesNotContain("@media", css);
        Assert.DoesNotContain("display: none", css);
    }

    [Fact]
    public void ABlockedDeliverablesText_IsNotInAnyCopyOrDownloadThePageOffers()
    {
        TextOf("deliverable.emr.note", ReleasedText);
        TextOf("deliverable.refused", RefusedText);
        // the sealed record holds hashes, never a text
        var record = Encoding.UTF8.GetBytes("""{ "schemaVersion": 12, "deliverables": [ { "key": "deliverable.refused", "composedHash": "sha256:b3" } ] }""");
        AIService.DownloadJobRecordAsync(JobId).Returns(new RecordDownload(record, "sha256:cd01", "record-31dd.json"));
        var page = Render31();
        foreach (var key in new[] { "deliverable.emr.note", "deliverable.refused", "deliverable.held_summary", "deliverable.failed_note", "deliverable.skipped_letter" })
        {
            Open(page, key);
        }

        page.WaitForAssertion(() => Assert.Contains(RefusedText, page.Markup));

        // every copy control the page offers, pressed
        var copies = page.FindAll("fluent-button, button").Where(b => b.TextContent.Contains("Copy", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(copies);
        foreach (var copy in copies)
        {
            copy.Click();
        }

        page.Find("fluent-button.history31-download").Click();

        var copied = JSInterop.Invocations["navigator.clipboard.writeText"].Select(call => (string)call.Arguments[0]!).ToList();
        Assert.Equal([ReleasedText], copied);
        var saved = Assert.Single(JSInterop.Invocations["consultologistDownload"]);
        var bytes = Convert.FromBase64String((string)saved.Arguments[2]!);
        Assert.DoesNotContain("letrozole", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain(RefusedText, string.Join("\n", copied));
    }

    [Fact]
    public void AfterTheDrop_TheHeadingAndTheHashRemain_WithA12002()
    {
        TextOf("deliverable.refused", null, deleted: true);
        var page = Render31();
        Open(page, "deliverable.refused");

        page.WaitForAssertion(() => Assert.Contains(HistorySentences31.TextDeleted, BodyOf(page, "deliverable.refused").TextContent));
        var panel = BodyOf(page, "deliverable.refused").QuerySelector(".history31-refused")!;
        Assert.Equal(HistorySentences31.RefusedHeading, panel.QuerySelector(".history31-refused__heading")!.TextContent.Trim());
        Assert.Equal("sha256:b3", panel.QuerySelector(".history31-refused__hash")!.TextContent.Trim());
        Assert.Contains(HistorySentences31.TextDeleted, panel.QuerySelector(".history31-refused__dropped")!.TextContent);
        Assert.Null(panel.QuerySelector(".history31-refused__text"));
    }

    [Fact]
    public void ABlockedDeliverablesReads_ShowSizesAndHashes_ButNotItsText()
    {
        var blocked = Deliverable("deliverable.refused_reads", RowState.Blocked, "blocked: check 'gate' failed (The plan is not complete.)", """
            { "label": "Refused reads", "check": "check.gate", "composedHash": "sha256:r0",
              "sites": [ { "site": 1, "at": "1:1", "expr": "node.plan.content", "loop": null } ],
              "substitutions": [ { "site": 1, "pass": null, "reads": "node.plan.content", "hash": "sha256:r1", "start": 0, "size": 7, "renders": null } ] }
            """);
        TextOf("deliverable.refused_reads", RefusedText);
        var page = Render31(extra: blocked);
        Open(page, "deliverable.refused_reads");

        page.WaitForAssertion(() => Assert.NotNull(BodyOf(page, "deliverable.refused_reads").QuerySelector(".history31-refused__text")));
        var read = BodyOf(page, "deliverable.refused_reads").QuerySelector(".history31-substitution")!;
        Assert.Contains("sha256:r1", read.TextContent);
        Assert.Null(read.QuerySelector(".history31-substitution__text"));
    }

    [Fact]
    public void ThereIsNoReleaseButton()
    {
        TextOf("deliverable.refused", RefusedText);
        var page = Render31();
        Open(page, "deliverable.refused");

        page.WaitForAssertion(() => Assert.NotNull(BodyOf(page, "deliverable.refused").QuerySelector(".history31-refused__text")));
        Assert.DoesNotContain(page.FindAll("button, fluent-button, a"), control => control.TextContent.Contains("Release", StringComparison.OrdinalIgnoreCase));
    }

    // ----- links into the section ---------------------------------------------------------------------------------------

    [Fact]
    public void ABannersLink_ToADelivery_OpensItsDeliverable_AndLandsOnTheDestination()
    {
        var page = Render31();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var before = navigation.Uri;

        page.FindAll(".history31-banner a").First(a => a.GetAttribute("href") == "/history/" + JobId + "#row-delivery:deliverable.emr.note/pdf").Click();

        page.WaitForAssertion(() => Assert.Equal("true", LineOf(page, "deliverable.emr.note").QuerySelector(".history31-deliverable__toggle")!.GetAttribute("aria-expanded")));
        Assert.NotNull(page.Find("#history31-deliverables [id=\"row-delivery:deliverable.emr.note/pdf\"]"));
        Assert.Equal(before, navigation.Uri);
        Assert.Contains(JSInterop.Invocations, call => call.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ABannersLink_ToABlockedDeliverable_OpensItsRow()
    {
        TextOf("deliverable.refused", RefusedText);
        var page = Render31();

        page.FindAll(".history31-banner a").First(a => a.GetAttribute("href") == "/history/" + JobId + "#row-deliverable.refused").Click();

        page.WaitForAssertion(() => Assert.Equal(HistorySentences31.RefusedHeading,
            BodyOf(page, "deliverable.refused").QuerySelector(".history31-refused__heading")!.TextContent.Trim()));
    }

    [Fact]
    public void AChecksLink_ToADeliverable_OpensItsRow()
    {
        var page = Render31();
        page.Find("#history31-checks [id=\"row-check.gate\"] .history31-check__toggle").Click();

        page.Find("#history31-checks [id=\"row-check.gate\"] .history31-check__checked a").Click();

        page.WaitForAssertion(() => Assert.Equal("true", LineOf(page, "deliverable.refused").QuerySelector(".history31-deliverable__toggle")!.GetAttribute("aria-expanded")));
    }

    [Fact]
    public void APageOpenedAtADeliverable_OpensIt()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/history/" + JobId + "#row-deliverable.held_summary");

        var page = Render31();

        Assert.Equal("true", LineOf(page, "deliverable.held_summary").QuerySelector(".history31-deliverable__toggle")!.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void ASkippedDeliverable_ShowsItsDecision()
    {
        var page = Render31(extra: Record31Fixtures.DecisionRow("deliverable.skipped_letter", "self", "input.kind.value = \"letter\"", false));

        Open(page, "deliverable.skipped_letter");

        Assert.Contains("input.kind.value = \"letter\"", BodyOf(page, "deliverable.skipped_letter").QuerySelector(".history31-decisions")!.TextContent);
    }

    private static string Normalise(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
