using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.PackageFormat.V31;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance.V31;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Web.Tests.V31;

/// <summary>A clock a test sets: the page reads its time from the injected <see cref="TimeProvider"/>.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Task 16 (#1509; part 09 §§ 6.2 to 6.4; D9, D10, R6, R7): History opens a storage 12 job through <c>RecordView31</c>:
/// its header with the resolve and lock hashes and the ten limits, the migration line, the banners at the top, the five
/// sections in the order the job runs, the stored status (or "Delivering…", or "Stopped" with A12-001), the record's
/// download and Verify. A 5–30 job renders as it did.
/// </summary>
public class History31PageTests : ClientRenderTestContext
{
    private const string JobId = "31aa31aa31aa31aa31aa31aa31aa31aa";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 16, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private static readonly IReadOnlyDictionary<string, int> TenLimits = new Dictionary<string, int>
    {
        ["maxAgentCalls"] = 4,
        ["capacityWaitMinutes"] = 5,
        ["serviceAttempts"] = 3,
        ["jobMinutes"] = 30,
        ["deliveryMinutes"] = 15,
        ["deliveryAttempts"] = 3,
        ["loopItems"] = 100,
        ["jobAgentCalls"] = 200,
        ["jobInstances"] = 2000,
        ["renderSteps"] = 10000,
    };

    public History31PageTests()
    {
        Services.AddSingleton<TimeProvider>(new FixedClock(Now));
    }

    private static ConsultGenerationJobResponse Header(string status = "Completed") => Record31Fixtures.Header() with
    {
        JobId = JobId,
        Status = status,
        Limits = TenLimits,
        CreatedAtUtc = Now.AddMinutes(-20),
        StartedAtUtc = status == "Waiting for an update to finish" ? null : Now.AddMinutes(-19),
        CompletedAtUtc = status is "Completed" or "Failed" or "Cancelled" ? Now.AddMinutes(-10) : null,
    };

    private static AccountJobSummaryResponse Summary(ConsultGenerationJobResponse header) =>
        new(header.JobId, header.Status, header.CreatedAtUtc ?? Now, header.StartedAtUtc,
            header.CompletedAtUtc, TotalBlockCount: 6, CompletedBlockCount: 6, FailedBlockCount: 0);

    private void With31(ConsultGenerationJobResponse header, IReadOnlyList<RecordRowRef>? rows = null, params ConsultGenerationJobResponse[] others)
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[] { Summary(header) }.Concat(others.Select(Summary)).ToList(),
            null));
        AIService.GetConsultGenerationJobAsync(header.JobId).Returns(header);
        AIService.GetJobRowsAsync(header.JobId).Returns(rows ?? Record31Fixtures.Job().Rows);
        foreach (var other in others)
        {
            AIService.GetConsultGenerationJobAsync(other.JobId).Returns(other);
        }
    }

    private int FetchesOf(string jobId) =>
        AIService.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(IAIEndpointService.GetConsultGenerationJobAsync) && Equals(call.GetArguments()[0], jobId));

    private int RowListings() =>
        AIService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IAIEndpointService.GetJobRowsAsync));

    private IRenderedComponent<History> Open(TimeSpan? fastPoll = null) =>
        Render<History>(parameters =>
        {
            parameters.Add(p => p.JobId, JobId);
            if (fastPoll is { } interval)
            {
                parameters.Add(p => p.FastPollInterval, interval);
            }
        });

    private int JobFetches() =>
        AIService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IAIEndpointService.GetConsultGenerationJobAsync));

    // ----- the header (§ 6.2) -----

    [Fact]
    public void A31Job_ShowsResolveHashAndLockHash_BesideThePackage()
    {
        With31(Header());

        var page = Open();

        // the package's own chip, then its resolve and lock hashes beside it; the package is said once
        var chips = page.FindAll(".provenance-chips > span");
        Assert.Contains("test/general@v2026.12.1", chips[0].TextContent);
        Assert.Contains("history31-resolve-hash", chips[1].ClassName);
        Assert.Equal("sha256:9b41", chips[1].QuerySelector("code")!.TextContent.Trim());
        Assert.Contains("history31-lock-hash", chips[2].ClassName);
        Assert.Equal("sha256:07c2", chips[2].QuerySelector("code")!.TextContent.Trim());
        var panel = page.Find(".job-detail-panel").TextContent;
        Assert.Equal(1, panel.Split("test/general@v2026.12.1").Length - 1);
    }

    /// <summary>A 31 job, with every line the two storage families share given a value.</summary>
    private void WithSharedLines()
    {
        With31(Header() with
        {
            TextDroppedAtUtc = Now.AddDays(-1),
            InputsDroppedAtUtc = Now.AddDays(-1),
            PackageTags = ["oncology", "Breast"],
            ProvenanceRef = "provenance@v2026.11.1",
            EngineRelease = "v2026.11.9",
            EffectiveInputHash = "sha256:e1",
            EffectiveInputHashVersion = 7,
            Nodes = [new ConsultGenerationNodeDescriptor("plan", "Plan")],
            AgentVersions = new Dictionary<string, string> { ["text"] = "47" },
        });
        WorkflowService.GetLineageAsync("test/general@v2026.12.1").Returns(new[] { "test/general@v2026.12.1", "general@v2026.11.4" });
    }

    [Theory]
    [InlineData(".provenance-text-deleted", "the produced text was deleted under the retention policy")]
    [InlineData(".provenance-inputs-deleted", "the held inputs were deleted under the retention policy")]
    [InlineData(".provenance-tags", "oncology · Breast")]
    [InlineData(".provenance-list dd.hash-text", "general@v2026.11.4")]
    [InlineData(".provenance-agents", "text")]
    [InlineData(".provenance-contract a", "provenance@v2026.11.1")]
    [InlineData(".provenance-engine a", "v2026.11.9")]
    [InlineData(".provenance-host .muted-text", "not recorded")]
    public void A31Job_ShowsTheLinesBothStorageFamiliesShare(string selector, string expected)
    {
        WithSharedLines();

        var page = Open();

        Assert.Contains(page.FindAll(selector), element => element.TextContent.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A31Job_LinksItsStorageVersion_AndItsInputHashDefinition()
    {
        WithSharedLines();

        var page = Open();

        var refs = page.FindAll(".provenance-list a.provenance-ref").Select(a => (a.TextContent.Trim(), a.GetAttribute("href"))).ToList();
        Assert.Contains(("v12", "https://github.com/Consultologist/consultologist-provenance/releases/tag/v2026.11.1"), refs);
        Assert.Contains(("v7", "https://github.com/Consultologist/consultologist-provenance/releases/tag/v2026.11.1"), refs);
        Assert.Contains("sha256:e1", page.Find(".provenance-list").TextContent);
    }

    [Fact]
    public void A31Job_HasNoFiveToThirtyOnlyLines()
    {
        With31(Header() with
        {
            WorkflowOutputHash = "bbbb",
            InputOrigins = new Dictionary<string, IReadOnlyList<ConsultInputOrigin>> { ["referral"] = [new ConsultInputOrigin("document")] },
        });

        var page = Open();

        Assert.Empty(page.FindAll(".provenance-verify"));
        Assert.Empty(page.FindAll(".provenance-list__nested"));
        Assert.DoesNotContain("Output hash", page.Find(".provenance-list").TextContent);
        Assert.Empty(page.FindAll(".node-grid"));
    }

    [Fact]
    public void A31Job_ShowsEveryLimit_WithItsName()
    {
        With31(Header());

        var page = Open();

        var limits = page.FindAll(".history31-limit")
            .Select(limit => (limit.QuerySelector(".history31-limit__name")!.TextContent.Trim(), limit.QuerySelector(".history31-limit__value")!.TextContent.Trim()))
            .ToList();
        Assert.Equal(
            new[]
            {
                ("maxAgentCalls", "4"), ("capacityWaitMinutes", "5"), ("serviceAttempts", "3"), ("jobMinutes", "30"), ("deliveryMinutes", "15"),
                ("deliveryAttempts", "3"), ("loopItems", "100"), ("jobAgentCalls", "200"), ("jobInstances", "2000"), ("renderSteps", "10000"),
            },
            limits);
    }

    [Fact]
    public void AMigratedVersion_ShowsS09004_LinkingToTheEditor()
    {
        With31(Header());
        WorkflowService.GetCurrentPackageContentAsync("test/general@v2026.12.1").Returns(new WorkflowPackageContentResponse(
            "test/general", "v2026.12.1", 31, JsonSerializer.SerializeToElement(new { }), new Dictionary<string, string>(),
            MigratedFrom: "v2026.11.4"));

        var page = Open();

        var link = page.Find("a.history31-migrated");
        Assert.Equal(HistorySentences31.MigratedFrom("v2026.11.4"), link.TextContent.Trim());
        Assert.Equal("/editor", link.GetAttribute("href"));
    }

    [Fact]
    public void AVersionNotMigrated_HasNoMigrationLine()
    {
        With31(Header());
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>())
            .ThrowsAsync(new HttpRequestException("The workflow package could not be read."));

        var page = Open();

        Assert.Empty(page.FindAll(".history31-migrated"));
        Assert.NotEmpty(page.FindAll(".history31-header"));
    }

    // ----- banners and sections (§§ 6.3, 6.4) -----

    [Fact]
    public void Banners_AreAtTheTop_EachLinkingToItsRow()
    {
        With31(Header());
        var expected = Banners31.Of(Record31.From(Header(), Record31Fixtures.Job().Rows));

        var page = Open();

        var banners = page.FindAll(".history31-banner");
        Assert.Equal(expected.Count, banners.Count);
        Assert.NotEmpty(expected);
        for (var i = 0; i < expected.Count; i++)
        {
            var link = banners[i].QuerySelector("a")!;
            Assert.Equal(expected[i].Text, link.TextContent.Trim());
            Assert.Equal($"/history/{JobId}#row-{expected[i].LinkKey}", link.GetAttribute("href"));
            Assert.Contains($"history31-banner--{expected[i].Kind}", banners[i].ClassName);
        }

        var markup = page.Find(".history31").InnerHtml;
        Assert.True(markup.IndexOf("history31-banner", StringComparison.Ordinal) < markup.IndexOf("history31-inputs", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("history31-header", StringComparison.Ordinal) < markup.IndexOf("history31-banner", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFiveSections_AreInTheOrderTheJobRuns()
    {
        With31(Header());

        var page = Open();

        var sections = page.FindAll(".history31 section.history31-section");
        Assert.Equal(
            new[] { "history31-inputs", "history31-run-facts", "history31-nodes", "history31-checks", "history31-deliverables" },
            sections.Select(s => s.Id));
        Assert.Equal(
            new[] { "Inputs", "Run facts", "Nodes", "Checks", "Deliverables" },
            sections.Select(s => s.QuerySelector("h3")!.TextContent.Trim()));
    }

    // ----- the status (one vocabulary; R7; D9) -----

    [Fact]
    public void Delivering_IsShownAsDelivering_AndPolledFast()
    {
        With31(Header("delivering"));

        var page = Open(TimeSpan.FromMilliseconds(20));

        Assert.Equal(HistorySentences31.Delivering, page.Find(".job-status-badge").TextContent.Trim());
        Assert.Equal(HistorySentences31.Delivering, page.Find(".history31-status").TextContent.Trim());
        page.WaitForAssertion(() => Assert.True(JobFetches() >= 3), Settle);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    [InlineData("Waiting for an update to finish")]
    public void AStatusIsShownAsStored(string status)
    {
        With31(Header(status));

        var page = Open();

        Assert.Equal(status, page.Find(".job-status-badge").TextContent.Trim());
        Assert.Equal(status, page.Find(".history31-status").TextContent.Trim());
        Assert.DoesNotContain(HistorySentences31.Stopped, page.Markup);
    }

    [Fact]
    public void AStoppedJob_SaysA12001_IsNotPolledFast_AndOffersNoDownload()
    {
        // started two hours before the page's clock, still Running: its orchestration died (D9). Beside it, a 31 job
        // running within the cap keeps the poll at the fast rate; the stopped one is not fetched again at that rate.
        const string runningId = "77bb77bb77bb77bb77bb77bb77bb77bb";
        With31(Header("Running") with { StartedAtUtc = Now.AddHours(-2) }, null,
            Header("Running") with { JobId = runningId, StartedAtUtc = Now.AddMinutes(-5) });

        var page = Open(TimeSpan.FromMilliseconds(20));

        var stoppedRow = page.FindAll("tr").Single(row => row.TextContent.Contains("31aa31aa", StringComparison.Ordinal));
        Assert.Equal("Stopped", stoppedRow.QuerySelector(".job-status-badge")!.TextContent.Trim());
        Assert.Equal(HistorySentences31.Stopped, stoppedRow.QuerySelector(".job-status-note")!.TextContent.Trim());
        Assert.Equal("Stopped", page.Find(".history31-status").TextContent.Trim());
        Assert.Equal(HistorySentences31.Stopped, page.Find(".history31-stopped").TextContent.Trim());
        Assert.Empty(page.FindAll(".history31-download"));
        Assert.Empty(page.FindAll(".history31-verify"));
        page.WaitForAssertion(() => Assert.True(FetchesOf(runningId) >= 4), Settle);
        Assert.Equal(1, FetchesOf(JobId));
    }

    [Fact]
    public void ARunningJobWithinTheCap_IsNotStopped_AndIsPolledFast()
    {
        With31(Header("Running") with { StartedAtUtc = Now.AddMinutes(-59) });

        var page = Open(TimeSpan.FromMilliseconds(20));

        Assert.Equal("Running", page.Find(".job-status-badge").TextContent.Trim());
        Assert.Empty(page.FindAll(".job-status-note"));
        page.WaitForAssertion(() => Assert.True(JobFetches() >= 3), Settle);
    }

    // ----- the record's download -----

    [Fact]
    public void DownloadRecord_SavesTheRoutesBytes_UnderItsFileName()
    {
        With31(Header());
        var bytes = Encoding.UTF8.GetBytes("{\"jobId\":\"31aa\"}");
        AIService.DownloadJobRecordAsync(JobId).Returns(new RecordDownload(bytes, "sha256:cd01", "record-31aa.json"));

        var page = Open();
        page.Find("fluent-button.history31-download").Click();

        var saved = Assert.Single(JSInterop.Invocations["consultologistDownload"]);
        Assert.Equal("record-31aa.json", saved.Arguments[0]);
        Assert.Equal("application/json", saved.Arguments[1]);
        Assert.Equal(Convert.ToBase64String(bytes), saved.Arguments[2]);
    }

    [Fact]
    public void DownloadRecord_IsOfferedOnlyForATerminalJob()
    {
        With31(Header("delivering"));

        var page = Open();

        Assert.Empty(page.FindAll(".history31-download"));
        Assert.Empty(page.FindAll(".history31-verify"));
    }

    [Fact]
    public void ADownloadThatFails_ShowsTheRoutesSentence()
    {
        With31(Header());
        AIService.DownloadJobRecordAsync(JobId)
            .ThrowsAsync(new ConsultGenerationRefusedException(HttpStatusCode.InternalServerError, "The record isn't available yet. Try again."));

        var page = Open();
        page.Find("fluent-button.history31-download").Click();

        Assert.Equal("The record isn't available yet. Try again.", page.Find(".history31-download-error").TextContent.Trim());
        Assert.Empty(JSInterop.Invocations["consultologistDownload"]);
    }

    // ----- Verify -----

    private const string LetterKey = "deliverable.patient.patient_letter";
    private const string LetterText = "Dear Margaret Okafor, your results are normal.";

    private static string SealedDocument()
    {
        const string name = "Margaret Okafor";
        return CanonicalJson.Write(new JsonObject
        {
            ["jobId"] = JobId,
            ["status"] = "Completed",
            ["deliverables"] = new JsonArray(new JsonObject
            {
                ["key"] = LetterKey,
                ["label"] = "Patient letter",
                ["state"] = "released",
                ["composedHash"] = CanonicalJson.HashText(LetterText),
                ["substitutions"] = new JsonArray(new JsonObject
                {
                    ["start"] = 5,
                    ["size"] = name.Length,
                    ["hash"] = CanonicalJson.HashText(name),
                }),
            }),
        });
    }

    private static readonly JsonElement LetterRow =
        JsonSerializer.SerializeToElement(new { key = LetterKey, label = "Patient letter", state = "released", composedHash = CanonicalJson.HashText(LetterText) });

    private void WithSealedJob(string? recordHash = null)
    {
        var document = SealedDocument();
        With31(Header() with { RecordHash = recordHash ?? CanonicalJson.HashText(document) },
            [new RecordRowRef(LetterKey, "deliverable", LetterRow)]);
        AIService.DownloadJobRecordAsync(JobId).Returns(new RecordDownload(Encoding.UTF8.GetBytes(document), null, $"record-{JobId}.json"));
        AIService.GetJobRowAsync(JobId, LetterKey).Returns(new RecordRowText(LetterKey, "deliverable", LetterRow,
            JsonSerializer.SerializeToElement(new { content = LetterText }), false));
    }

    private static IReadOnlyList<(string What, string Mark)> Lines(IRenderedComponent<History> page) =>
        page.FindAll(".history31-check-line")
            .Select(line => (line.QuerySelector(".history31-check-line__what")!.TextContent.Trim(), line.GetAttribute("data-ok")!))
            .ToList();

    [Fact]
    public void Verify_ChecksTheRecordHash_AndReplaysEachDeliverable()
    {
        WithSealedJob();

        var page = Open();
        page.Find("fluent-button.history31-verify").Click();

        page.WaitForAssertion(() => Assert.Equal(
            new[] { ("recordHash", "true"), ($"deliverables[{LetterKey}].composedHash", "true"), ($"deliverables[{LetterKey}].replay", "true") },
            Lines(page)));
        Assert.Contains("history31-verified--ok", page.Find(".history31-verified").ClassName);
        AIService.Received(1).DownloadJobRecordAsync(JobId);
    }

    [Fact]
    public void Verify_AChangedRecord_FailsItsHash()
    {
        WithSealedJob(recordHash: "sha256:" + new string('0', 64));

        var page = Open();
        page.Find("fluent-button.history31-verify").Click();

        page.WaitForAssertion(() => Assert.Contains(("recordHash", "false"), Lines(page)));
        Assert.Contains("history31-verified--failed", page.Find(".history31-verified").ClassName);
    }

    [Fact]
    public void Verify_ADroppedText_IsAttested_NotFailed()
    {
        WithSealedJob();
        AIService.GetJobRowAsync(JobId, LetterKey).Returns(new RecordRowText(LetterKey, "deliverable", LetterRow, null, true));

        var page = Open();
        page.Find("fluent-button.history31-verify").Click();

        page.WaitForAssertion(() => Assert.Equal(
            new[] { ("recordHash", "true"), ($"deliverables[{LetterKey}].composedHash", "attested") },
            Lines(page)));
    }

    [Fact]
    public void Verify_WhenTheRecordCannotBeFetched_SaysA12005()
    {
        WithSealedJob();
        AIService.DownloadJobRecordAsync(JobId)
            .ThrowsAsync(new ConsultGenerationRefusedException(HttpStatusCode.InternalServerError, "The record isn't available yet. Try again."));

        var page = Open();
        page.Find("fluent-button.history31-verify").Click();

        page.WaitForAssertion(() => Assert.Equal(
            HistorySentences31.RecordNotChecked("the record file"), page.Find(".history31-verify-error").TextContent.Trim()));
        Assert.Empty(page.FindAll(".history31-check-line"));
    }

    [Fact]
    public void Verify_WhenARowCannotBeFetched_SaysA12005_NamingItsDeliverable()
    {
        WithSealedJob();
        AIService.GetJobRowAsync(JobId, LetterKey).ThrowsAsync(new HttpRequestException("Azure Function call failed: InternalServerError"));

        var page = Open();
        page.Find("fluent-button.history31-verify").Click();

        page.WaitForAssertion(() => Assert.Equal(
            HistorySentences31.RecordNotChecked("the text of 'Patient letter'"), page.Find(".history31-verify-error").TextContent.Trim()));
        Assert.Empty(page.FindAll(".history31-check-line"));
    }

    [Fact]
    public void Verify_WhenTheRowsCannotBeListed_StillChecksEachDeliverableOfTheRecord()
    {
        WithSealedJob();
        AIService.GetJobRowsAsync(JobId)
            .ThrowsAsync(new ConsultGenerationRefusedException(HttpStatusCode.InternalServerError, "The record isn't available yet. Try again."));

        var page = Open();
        page.Find("fluent-button.history31-verify").Click();

        page.WaitForAssertion(() => Assert.Equal(
            new[] { ("recordHash", "true"), ($"deliverables[{LetterKey}].composedHash", "true"), ($"deliverables[{LetterKey}].replay", "true") },
            Lines(page)));
        AIService.Received(1).GetJobRowAsync(JobId, LetterKey);
    }

    [Fact]
    public void Verify_WhenADeliverablesRowCannotBeFetched_SaysA12005_AndIsNotAPass()
    {
        // the row route answers 404 for a deliverable the record holds
        WithSealedJob();
        AIService.GetJobRowAsync(JobId, LetterKey).Returns((RecordRowText?)null);

        var page = Open();
        page.Find("fluent-button.history31-verify").Click();

        page.WaitForAssertion(() => Assert.Equal(
            HistorySentences31.RecordNotChecked("the text of 'Patient letter'"), page.Find(".history31-verify-error").TextContent.Trim()));
        Assert.Empty(page.FindAll(".history31-verified--ok"));
    }

    [Fact]
    public void Verify_SaysTextDeleted_OnlyWhenTheRowSaysSo()
    {
        // the row is there and not dropped, but offers no text: that is a text not read, never a text deleted
        WithSealedJob();
        AIService.GetJobRowAsync(JobId, LetterKey).Returns(new RecordRowText(LetterKey, "deliverable", LetterRow, null, false));

        var page = Open();
        page.Find("fluent-button.history31-verify").Click();

        page.WaitForAssertion(() => Assert.Equal(
            HistorySentences31.RecordNotChecked("the text of 'Patient letter'"), page.Find(".history31-verify-error").TextContent.Trim()));
        Assert.DoesNotContain(RecordCheck31.TextDeleted, page.Markup);
        Assert.Empty(page.FindAll(".history31-verified--ok"));
    }

    // ----- rerun, the rows' listing, and the 5–30 page -----

    [Fact]
    public void Rerun_IsOffered_WhenTheInputsAreHeld()
    {
        With31(Header() with { HeldInputs = new Dictionary<string, string> { ["referral"] = "Referral for Margaret Okafor." } });

        var page = Open();

        var rerun = page.Find("fluent-button.rerun-button");
        Assert.False(rerun.HasAttribute("disabled"));
        Assert.Empty(page.FindAll(".rerun-row__reason"));
    }

    [Fact]
    public void Rerun_IsGreyed_WhenTheInputsAreNotHeld()
    {
        With31(Header());

        var page = Open();

        Assert.True(page.Find("fluent-button.rerun-button").HasAttribute("disabled"));
        Assert.Equal("inputs were not held for this run", page.Find(".rerun-row__reason").TextContent.Trim());
    }

    [Fact]
    public void ARerun_LinksToItsSource_Only()
    {
        const string source = "50cc50cc50cc50cc50cc50cc50cc50cc";
        With31(Header() with { RerunOf = source });

        var page = Open();

        Assert.Equal($"/history/{source}", page.Find("a.history31-rerun-of").GetAttribute("href"));
        Assert.Empty(page.FindAll(".rerun-comparison"));
        AIService.DidNotReceive().GetConsultGenerationJobAsync(source);
    }

    [Fact]
    public void TheRowsAreListedOnce_PerLoad()
    {
        With31(Header());

        var page = Open();
        page.Render();
        page.FindAll("tr").First(row => row.TextContent.Contains("31aa31aa", StringComparison.Ordinal)).Click();

        AIService.Received(1).GetJobRowsAsync(JobId);
    }

    [Fact]
    public void ARunningJob_IsListedAgain_OnlyWhenItsRowCountMoves()
    {
        // the load and three polls at 5 rows, then 7 rows from the fifth answer on: one listing, then one more
        var at5 = Header("Running") with { StartedAtUtc = Now.AddMinutes(-5), Rows = 5 };
        var at7 = at5 with { Rows = 7 };
        With31(at5);
        AIService.GetConsultGenerationJobAsync(JobId).Returns(at5, at5, at5, at5, at7);

        var page = Open(TimeSpan.FromMilliseconds(20));

        page.WaitForAssertion(() => Assert.True(FetchesOf(JobId) >= 8), Settle);
        Assert.Equal(2, RowListings());
    }

    [Fact]
    public void A30Job_RendersExactlyAsBefore()
    {
        With31(Header() with { SchemaVersion = 11, PackageSpecVersion = 30, ResolveHash = null, LockHash = null, Limits = null, WorkflowOutputHash = "bbbb" });

        var page = Open();

        Assert.DoesNotContain("history31", page.Markup);
        Assert.NotEmpty(page.FindAll(".provenance-list"));
        Assert.NotEmpty(page.FindAll(".node-grid"));
        AIService.DidNotReceive().GetJobRowsAsync(Arg.Any<string>());
        WorkflowService.DidNotReceive().GetCurrentPackageContentAsync(Arg.Any<string?>());
    }

    [Fact]
    public void AJobWhoseRowsCannotBeListed_ShowsTheHeader_AndTheRoutesSentence()
    {
        With31(Header());
        AIService.GetJobRowsAsync(JobId)
            .ThrowsAsync(new ConsultGenerationRefusedException(HttpStatusCode.InternalServerError, "The record isn't available yet. Try again."));

        var page = Open();

        Assert.NotEmpty(page.FindAll(".history31-header"));
        Assert.Equal("The record isn't available yet. Try again.", page.Find(".history31-rows-error").TextContent.Trim());
        Assert.Empty(page.FindAll(".history31-section"));
        Assert.Empty(page.FindAll(".history31-banner"));
    }

    [Fact]
    public void ARowsListingFailureWithoutTheRoutesWords_SaysTheRoutesSentence()
    {
        With31(Header());
        AIService.GetJobRowsAsync(JobId).ThrowsAsync(new HttpRequestException("Azure Function call failed: BadGateway"));

        var page = Open();

        Assert.Equal("The record isn't available yet. Try again.", page.Find(".history31-rows-error").TextContent.Trim());
    }
}
