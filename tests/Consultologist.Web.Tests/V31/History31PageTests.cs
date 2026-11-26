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

    private void With31(ConsultGenerationJobResponse header, IReadOnlyList<RecordRowRef>? rows = null)
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(header.JobId, header.Status, header.CreatedAtUtc ?? Now, header.StartedAtUtc,
                    header.CompletedAtUtc, TotalBlockCount: 6, CompletedBlockCount: 6, FailedBlockCount: 0),
            },
            null));
        AIService.GetConsultGenerationJobAsync(header.JobId).Returns(header);
        AIService.GetJobRowsAsync(header.JobId).Returns(rows ?? Record31Fixtures.Job().Rows);
    }

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

        var package = page.Find(".history31-package");
        Assert.Contains("test/general@v2026.12.1", package.TextContent);
        Assert.Equal("sha256:9b41", package.QuerySelector(".history31-resolve-hash")!.TextContent.Trim());
        Assert.Equal("sha256:07c2", package.QuerySelector(".history31-lock-hash")!.TextContent.Trim());
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
        Assert.NotEmpty(page.FindAll(".history31-package"));
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
            Assert.Equal($"#row-{expected[i].LinkKey}", link.GetAttribute("href"));
            Assert.Contains($"history31-banner--{expected[i].Kind}", banners[i].ClassName);
        }

        var markup = page.Find(".history31").InnerHtml;
        Assert.True(markup.IndexOf("history31-banner", StringComparison.Ordinal) < markup.IndexOf("history31-inputs", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("history31-package", StringComparison.Ordinal) < markup.IndexOf("history31-banner", StringComparison.Ordinal));
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
        // started two hours before the page's clock, still Running: its orchestration died (D9)
        With31(Header("Running") with { StartedAtUtc = Now.AddHours(-2) });

        var page = Open(TimeSpan.FromMilliseconds(20));

        Assert.Equal("Stopped", page.Find(".job-status-badge").TextContent.Trim());
        Assert.Equal(HistorySentences31.Stopped, page.Find(".job-status-note").TextContent.Trim());
        Assert.Equal("Stopped", page.Find(".history31-status").TextContent.Trim());
        Assert.Equal(HistorySentences31.Stopped, page.Find(".history31-stopped").TextContent.Trim());
        Assert.Empty(page.FindAll(".history31-download"));
        Assert.Empty(page.FindAll(".history31-verify"));
        Thread.Sleep(200);
        Assert.Equal(1, JobFetches());
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

        Assert.NotEmpty(page.FindAll(".history31-package"));
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
