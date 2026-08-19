using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// History's provenance panel (#224): a v7 job lists each deliverable's own
/// digest under the job-level hash those digests compose; a v5/v6 job lists
/// none. The deep-link route loads the detail eagerly, which is how these
/// reach the panel without simulating a click.
/// </summary>
public class HistoryDetailTests : ClientRenderTestContext
{
    private const string JobId = "0123456789abcdef0123456789abcdef";

    private void WithJob(
        int outputHashVersion,
        IReadOnlyList<ConsultGenerationResultDocumentResponse>? documents = null,
        IReadOnlyDictionary<string, ConsultInputOrigin>? inputOrigins = null,
        IReadOnlyList<ConsultSkippedDocumentResponse>? skipped = null,
        int? packageSpecVersion = null,
        int? schemaVersion = null)
    {
        // Terminal status only: a non-terminal row would start the page's real
        // 5-second polling loop.
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(
                    JobId, "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    TotalBlockCount: 9, CompletedBlockCount: 9, FailedBlockCount: 0)
            },
            null));

        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId,
            "user-1",
            "Completed",
            TotalBlockCount: 9,
            CompletedBlockCount: 9,
            FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(),
            FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            EffectiveInputHash: "aaaa",
            EffectiveInputHashVersion: outputHashVersion,
            WorkflowOutputHash: "bbbb",
            WorkflowOutputHashVersion: outputHashVersion,
            AssembledDocuments: documents,
            InputOrigins: inputOrigins,
            SkippedDocuments: skipped,
            SchemaVersion: schemaVersion,
            PackageSpecVersion: packageSpecVersion));
    }

    [Fact]
    public void ADeliverableThatDidNotFire_IsRecordedBesideTheDigests()
    {
        // The absence belongs next to the evidence for the documents that do
        // exist: History is where a reader goes to ask what this job actually
        // produced, and a shorter list with no explanation answers wrongly.
        WithJob(3,
            new[] { new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "Note.", "hash-note") },
            skipped: new[]
            {
                new ConsultSkippedDocumentResponse("billing_summary", "Billing summary",
                    "needs billable to be true; it is 'false'")
            });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("Billing summary", page.Markup);
        Assert.Contains("not produced", page.Markup);
        Assert.Contains("needs billable to be true", page.Markup);
    }

    [Fact]
    public void V7Job_ListsEachDeliverablesDigest()
    {
        WithJob(3, new[]
        {
            new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "Note.", "hash-note"),
            new ConsultGenerationResultDocumentResponse("patient_letter", "Patient letter", "Letter.", "hash-letter")
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var nested = page.FindAll(".provenance-list__nested");
        Assert.Equal(
            new[] { "Consultation note", "Patient letter" },
            nested.Select(row => row.TextContent.Trim()).ToArray());

        var provenance = page.Find(".provenance-list").TextContent;
        Assert.Contains("Output hash (v3)", provenance);
        Assert.Contains("hash-note", provenance);
        Assert.Contains("hash-letter", provenance);
    }

    [Fact]
    public void DocumentBackedInput_NamesTheExtractorThatReadIt()
    {
        // #238: beside the input hash, never inside it. This is the fact a
        // reviewer needs when a consult says something the referral did not —
        // whether a machine read it, and with what.
        WithJob(3, inputOrigins: new Dictionary<string, ConsultInputOrigin>
        {
            // The Api's own constant, not a copy of the string: the client
            // record is a hand-written mirror, so the test is where the two
            // are held together.
            ["consult_draft"] = new(Consultologist.Api.Models.ConsultInputOriginKinds.Document, "pdfpig/0.1.15", 3),
            // #240: a reviewer can see that a revision layer existed and was
            // resolved one way to produce this text.
            ["prior_notes"] = new(
                Consultologist.Api.Models.ConsultInputOriginKinds.Document,
                "openxml/3.5.1",
                null,
                TrackedChangesResolved: true)
        });

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var provenance = page.Find(".provenance-list").TextContent;
        Assert.Contains("consult_draft", provenance);
        Assert.Contains("pdfpig/0.1.15", provenance);
        Assert.Contains("3 pages", provenance);
        Assert.Contains("tracked changes resolved to the accepted view", provenance);
    }

    [Fact]
    public void JobWithNoRecordedOrigin_ClaimsNothingAboutItsInputs()
    {
        // Absence is not an assertion that the input was typed: every job
        // recorded before #238, and every email job until #237, has none.
        WithJob(3);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var provenance = page.Find(".provenance-list").TextContent;
        Assert.DoesNotContain("read from a document", provenance);
        Assert.DoesNotContain("typed", provenance);
    }

    [Fact]
    public void LegacyJob_ListsNoPerDeliverableRows()
    {
        WithJob(2);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".provenance-list__nested"));
        Assert.Contains("Output hash (v2)", page.Find(".provenance-list").TextContent);
    }

    private static IReadOnlyList<string> Chips(IRenderedComponent<History> page) =>
        page.FindAll(".provenance-chip").Select(chip => chip.TextContent.Trim()).ToList();

    [Fact]
    public void AJobsPackageFormat_IsOnTheProvenanceRow()
    {
        // #373: the one version on that row an outside reader can act on. It
        // used to show the record's storage version instead, labelled a schema.
        WithJob(3, packageSpecVersion: 8, schemaVersion: 7);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("format v8", Chips(page));
    }

    [Fact]
    public void TheRecordsStorageVersion_IsNotOnTheProvenanceRow()
    {
        // It is a storage discriminator — stamped 6 or 7 and never 8, so a v8
        // job read as v7. The row is for evidence a reader can act on.
        WithJob(3, packageSpecVersion: 8, schemaVersion: 7);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.DoesNotContain(Chips(page), chip => chip.Contains("schema", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Chips(page), chip => chip.Contains("v7", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRecordsStorageVersion_IsStillReadableInTheProvenanceList()
    {
        // Kept, not dropped: it says which fields on the record are the real
        // ones, which has explained real confusion before.
        WithJob(3, packageSpecVersion: 8, schemaVersion: 7);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var list = page.Find(".provenance-list").TextContent;
        Assert.Contains("Record storage version", list, StringComparison.Ordinal);
        Assert.Contains("v7", list, StringComparison.Ordinal);
    }

    [Fact]
    public void AJobThatRecordedNoFormat_ShowsNoFormatChip()
    {
        // Every job from before #373. An absent chip is the record saying it
        // does not know; a dash would read as "no format".
        WithJob(3, packageSpecVersion: null, schemaVersion: 7);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.DoesNotContain(Chips(page), chip => chip.Contains("format", StringComparison.OrdinalIgnoreCase));
    }
}
