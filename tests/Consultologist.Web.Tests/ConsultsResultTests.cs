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
        IReadOnlyList<ConsultSkippedDocumentResponse>? skipped = null)
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
            SkippedDocuments: skipped));
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
