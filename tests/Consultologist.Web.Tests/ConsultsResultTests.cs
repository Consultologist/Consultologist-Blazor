using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// The result panel's render (#224): the deliverable set drives one tab per
/// document, and a single document keeps the pre-v7 shape with no tab strip.
/// Reached by re-attaching to a completed job, which is the only way into the
/// run phase without executing one.
/// </summary>
public class ConsultsResultTests : ClientRenderTestContext
{
    private const string JobId = "0123456789abcdef0123456789abcdef";

    private void WithCompletedJob(
        string? assembledDocument = null,
        IReadOnlyList<ConsultGenerationResultDocumentResponse>? documents = null,
        IReadOnlyList<ConsultSkippedDocumentResponse>? skipped = null,
        IReadOnlyList<ConsultFailedDocumentResponse>? failed = null,
        IReadOnlyDictionary<string, string>? heldInputs = null,
        DateTimeOffset? inputsDroppedAtUtc = null,
        ConsultTokenUsage? tokens = null)
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") });

        // The route job id is what sends the page down the re-attach path;
        // a terminal snapshot stops it before any streaming.
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId,
            "user-1",
            "Completed",
            TotalBlockCount: 1,
            CompletedBlockCount: 1,
            FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string> { ["section-instructions:hpi"] = "Section prose." },
            FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            AssembledDocument: assembledDocument,
            AssembledDocuments: documents,
            SkippedDocuments: skipped,
            FailedDocuments: failed,
            HeldInputs: heldInputs,
            InputsDroppedAtUtc: inputsDroppedAtUtc,
            Tokens: tokens));
    }

    // ----- #551: the run's total on the completed view -----

    [Fact]
    public void ACompletedRun_SaysWhatItSpent()
    {
        WithCompletedJob(documents: OneNote, tokens: new ConsultTokenUsage(2100, 650));

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Equal("2,100 input · 650 output tokens", page.Find(".tokens-line").TextContent.Trim());
    }

    [Fact]
    public void ARecordFromBefore_SaysNothing_NeverZero()
    {
        WithCompletedJob(documents: OneNote);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".tokens-line"));
    }

    private static readonly ConsultGenerationResultDocumentResponse[] OneNote =
    {
        new("consult", "Consultation note", "The assembled note.")
    };

    // ----- #549: the Rerun action on the result panel -----

    [Fact]
    public void AHeldRun_OffersRerun_WithNoBlockedLine()
    {
        WithCompletedJob(documents: OneNote, heldInputs: new Dictionary<string, string> { ["consult_draft"] = "The referral." });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.False(page.Find(".rerun-button").HasAttribute("disabled"));
        Assert.Empty(page.FindAll(".rerun-blocked-line"));
    }

    [Fact]
    public void ARunNeverHeld_GreysRerun_AndSaysSo()
    {
        WithCompletedJob(documents: OneNote);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.True(page.Find(".rerun-button").HasAttribute("disabled"));
        Assert.Contains("inputs were not held for this run", page.Find(".rerun-blocked-line").TextContent);
    }

    [Fact]
    public void ADroppedRun_GreysRerun_AndSaysTheDate()
    {
        WithCompletedJob(documents: OneNote, inputsDroppedAtUtc: new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero));

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.True(page.Find(".rerun-button").HasAttribute("disabled"));
        Assert.Contains("inputs deleted", page.Find(".rerun-blocked-line").TextContent);
    }

    [Fact]
    public async Task Rerun_StartsTheReplay_FromTheShownRun()
    {
        WithCompletedJob(documents: OneNote, heldInputs: new Dictionary<string, string> { ["consult_draft"] = "The referral." });
        AIService.RerunConsultGenerationJobAsync(JobId).Returns("new-job-1");

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));
        await page.Find(".rerun-button").ClickAsync(new());

        await AIService.Received(1).RerunConsultGenerationJobAsync(JobId);
        var navigation = (Microsoft.AspNetCore.Components.NavigationManager)Services
            .GetService(typeof(Microsoft.AspNetCore.Components.NavigationManager))!;
        Assert.EndsWith("/consults/new-job-1", navigation.Uri);
    }

    [Fact]
    public void SingleDocument_RendersWithoutATabStrip()
    {
        WithCompletedJob(documents: new[]
        {
            new ConsultGenerationResultDocumentResponse("consult", "Consultation note", "The assembled note.")
        });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("The assembled note.", page.Find(".note-preview").TextContent);
        Assert.Contains("Consultation note", page.Find(".result-header").TextContent);
        // The strip only exists for several documents — Fluent's element is the
        // only hook here, since the component owns that markup.
        Assert.Empty(page.FindAll("fluent-tab"));
    }

    [Fact]
    public void ADeliverableThatDidNotFire_IsNamedBesideTheDocuments()
    {
        // #315's failure mode: a job producing fewer documents than the package
        // declares, saying nothing about which was skipped, is
        // indistinguishable from one that silently failed. The person who just
        // ran the consult is the one most likely to wonder.
        WithCompletedJob(
            documents: new[]
            {
                new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "The assembled note.")
            },
            skipped: new[]
            {
                new ConsultSkippedDocumentResponse("billing_summary", "Billing summary",
                    "needs billable to be true; it is 'false'")
            });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var note = page.Find("[data-skipped-result='billing_summary']").TextContent;
        Assert.Contains("Billing summary", note);
        Assert.Contains("was not produced", note);
        Assert.Contains("needs billable to be true", note);
    }

    [Fact]
    public void WithNothingSkipped_NoNoteAppears()
    {
        WithCompletedJob(documents: new[]
        {
            new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "The assembled note.")
        });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll("[data-skipped-result]"));
        Assert.Empty(page.FindAll("[data-failed-result]"));
    }

    [Fact]
    public void ADocumentItsOwnCheckRefused_IsNamedBesideTheDocuments()
    {
        // v12 #624: the third state. The package's failWith sentence is
        // authored content — shown verbatim beside what did produce.
        WithCompletedJob(
            documents: new[]
            {
                new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "The assembled note.")
            },
            failed: new[]
            {
                new ConsultFailedDocumentResponse("patient_letter", "Patient letter",
                    "The letter does not cover every clinical term found in the referral.")
            });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var note = page.Find("[data-failed-result='patient_letter']").TextContent;
        Assert.Contains("Patient letter", note);
        Assert.Contains("was not produced: its check failed", note);
        Assert.Contains("does not cover every clinical term", note);
    }

    [Fact]
    public void SeveralDocuments_RenderOneTabEachInResultSetOrder()
    {
        WithCompletedJob(documents: new[]
        {
            new ConsultGenerationResultDocumentResponse("consult_note", "Consultation note", "The assembled note."),
            new ConsultGenerationResultDocumentResponse("patient_letter", "Patient letter", "Dear patient,")
        });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var tabs = page.FindAll("fluent-tab");
        Assert.Equal(2, tabs.Count);
        Assert.Equal(
            new[] { "consult_note", "patient_letter" },
            tabs.Select(tab => tab.GetAttribute("id")).ToArray());
        Assert.Contains("Consultation note", tabs[0].TextContent);
        Assert.Contains("Patient letter", tabs[1].TextContent);
    }

    [Fact]
    public void LegacySingleDocumentField_StillRenders()
    {
        // A v6 job carries the one string rather than the set; the page
        // synthesizes a one-entry set so both eras share the render path.
        WithCompletedJob(assembledDocument: "The v6 assembled note.");

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("The v6 assembled note.", page.Find(".note-preview").TextContent);
        Assert.Empty(page.FindAll("fluent-tab"));
    }
}

/// <summary>
/// #642: the run rail offers the run diagram once any snapshot-shaped
/// payload arrived, and the × closes it — the re-attach path stands in for
/// a live run (the same ApplyConsultGenerationJobResponse seam).
/// </summary>
public class ConsultsRunDagTests : ClientRenderTestContext
{
    private const string JobId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void TheRail_OffersTheDiagram_AndTheCross_ClosesIt()
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") });
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed",
            TotalBlockCount: 1, CompletedBlockCount: 1, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string> { ["section-instructions:hpi"] = "Prose." },
            FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            AssembledDocuments: new[] { new ConsultGenerationResultDocumentResponse("consult", "Consultation note", "The note.") },
            Nodes: new[] { new ConsultGenerationNodeDescriptor("draft", "Drafting", "p") },
            NodeOutputs: new Dictionary<string, ConsultGenerationNodeStatus>
            {
                ["draft"] = new("draft", "Drafting", "Completed", "i", "o", null, null)
            }));

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        page.Find(".run-dag-button").Click();
        Assert.NotEmpty(page.FindAll(".run-dag-panel"));

        page.Find(".run-dag-close").Click();
        Assert.Empty(page.FindAll(".run-dag-panel"));
    }
}
