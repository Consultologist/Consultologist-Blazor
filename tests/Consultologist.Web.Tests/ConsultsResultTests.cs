using System.Linq;
using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using Xunit;

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
        ConsultTokenUsage? tokens = null,
        IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes = null,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus>? nodeOutputs = null)
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
            Tokens: tokens,
            Nodes: nodes,
            NodeOutputs: nodeOutputs));
    }

    [Fact]
    public void AnEmptyHeldInputsMap_StillCountsAsInputsAvailable_TheRerunStaysOffered()
    {
        // #1051: a catch-up event carries an empty map in place of the held
        // input text (the text never rides the events table); the flag the
        // Rerun control reads is "a map is present", not "the map has text".
        WithCompletedJob(documents: OneNote, heldInputs: new Dictionary<string, string>());

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var rerun = page.Find("fluent-button.rerun-button");
        Assert.Null(rerun.GetAttribute("disabled"));
        Assert.Empty(page.FindAll(".rerun-blocked-line"));
    }

    [Fact]
    public void NoHeldInputs_GreysTheRerun_AndSaysWhy()
    {
        WithCompletedJob(documents: OneNote, heldInputs: null);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.NotNull(page.Find("fluent-button.rerun-button").GetAttribute("disabled"));
        Assert.Contains("Rerun unavailable", page.Find(".rerun-blocked-line").TextContent);
    }

    // ----- #933: the custom tier's unattested marker (#760) on the result -----

    private static readonly ConsultGenerationNodeDescriptor[] CustomNodes =
    {
        new("section-instructions", "Applying section instructions", OutputContract: "custom", ForEach: "data:standards")
    };

    private static readonly Dictionary<string, ConsultGenerationNodeStatus> CustomOutputs = new(StringComparer.Ordinal)
    {
        ["section-instructions"] = new("section-instructions", "Applying section instructions", "Completed", null, null, DateTimeOffset.UtcNow, null,
            Unattested: true, CustomSchemaHash: "0123456789abcdef", CustomAgent: "custom-0123@1")
    };

    [Fact]
    public void ARunWithACustomNode_SaysSoAboveTheResult()
    {
        WithCompletedJob(documents: OneNote, nodes: CustomNodes, nodeOutputs: CustomOutputs);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var bar = page.Find(".result-unattested");
        Assert.Contains("output from a user-defined schema (1 unattested block)", bar.TextContent);
        Assert.Contains("not an attested clinical contract, no terminology grounding", bar.TextContent);
        Assert.Contains("Applying section instructions: schema 0123456789ab · agent custom-0123@1", bar.TextContent);
    }

    [Fact]
    public void ARunWithA25CustomShape_SaysWhatItIs_AboveTheResult()
    {
        // B7 #1006: a 25 custom producer carries no unattested stamp; the
        // descriptor says format, producer and the attested generic agent, and
        // the bar says the engine validated the shape.
        var shape = new ConsultGenerationNodeDescriptor(
            "section-instructions", "Applying section instructions", "p", ForEach: "snippet.standards",
            Format: "mine", Producer: "custom", FormatHash: "0123456789abcdef", AgentName: "custom-json", AgentVersion: "1");
        WithCompletedJob(documents: OneNote, nodes: new[] { shape });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var bar = page.Find(".result-unattested");
        Assert.Contains("output from a package-defined shape (1 custom-shape block)", bar.TextContent);
        Assert.Contains("checked by the engine against its declared schema", bar.TextContent);
        Assert.Contains("Applying section instructions: format mine · hash 0123456789ab · served by custom-json@1, attested", bar.TextContent);
        Assert.DoesNotContain("user-defined schema", bar.TextContent);
    }

    [Fact]
    public void ACatalogOnlyRun_ShowsNoUnattestedBar()
    {
        WithCompletedJob(documents: OneNote,
            nodes: new[] { new ConsultGenerationNodeDescriptor("section-instructions", "Applying section instructions", OutputContract: "concept-list") });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".result-unattested"));
    }

    [Fact]
    public void ThePerSectionView_MarksTheBlocks_ACustomNodeProduced()
    {
        // No assembled document: the pre-assembly per-section view renders the
        // generated block, whose id's node prefix is the custom node.
        WithCompletedJob(nodes: CustomNodes, nodeOutputs: CustomOutputs);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var badge = Assert.Single(page.FindAll(".note-section__unattested"));
        Assert.Equal("unattested", badge.TextContent.Trim());
        Assert.Contains("no terminology grounding", badge.GetAttribute("title"));
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

    // ----- #803: New consult beside Rerun on the completed panel -----

    [Fact]
    public void ACompletedRun_OffersNewConsult_BesideRerun()
    {
        WithCompletedJob(documents: OneNote, heldInputs: new Dictionary<string, string> { ["consult_draft"] = "The referral." });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.NotEmpty(page.FindAll(".result-header__actions .new-consult-button"));
    }

    [Fact]
    public void ADroppedRun_StillOffersNewConsult_ThoughRerunIsGreyed()
    {
        // The New consult button does not share Rerun's held-inputs guard.
        WithCompletedJob(documents: OneNote, inputsDroppedAtUtc: new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero));

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.True(page.Find(".rerun-button").HasAttribute("disabled"));
        Assert.NotEmpty(page.FindAll(".result-header__actions .new-consult-button"));
    }

    [Fact]
    public async Task NewConsult_FromTheResultHeader_Resets()
    {
        WithCompletedJob(documents: OneNote, heldInputs: new Dictionary<string, string> { ["consult_draft"] = "The referral." });

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));
        await page.Find(".result-header__actions .new-consult-button").ClickAsync(new());

        Assert.Null(JobSession.Current);
        var navigation = (Microsoft.AspNetCore.Components.NavigationManager)Services
            .GetService(typeof(Microsoft.AspNetCore.Components.NavigationManager))!;
        Assert.EndsWith("/create", navigation.Uri);
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
        Assert.EndsWith("/create/new-job-1", navigation.Uri);
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

    // ----- #768: the Copy control's scope on multi-document runs -----

    private static readonly ConsultGenerationResultDocumentResponse[] TwoNotes =
    {
        new("consult_note", "Consultation note", "The assembled note."),
        new("patient_letter", "Patient letter", "Dear patient,")
    };

    private string? LastCopiedText() =>
        JSInterop.Invocations["navigator.clipboard.writeText"].Last().Arguments[0] as string;

    private static IElement CopyButton(IRenderedComponent<Consults> page, string label) =>
        page.FindAll("fluent-button").Single(button => button.TextContent.Trim() == label);

    [Fact]
    public void SeveralDocuments_ScopeTheCopyButton_AndOfferCopyAll()
    {
        WithCompletedJob(documents: TwoNotes);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var labels = page.FindAll("fluent-button").Select(button => button.TextContent.Trim()).ToArray();
        Assert.Contains("Copy this document", labels);
        Assert.Contains("Copy all", labels);
        Assert.DoesNotContain("Copy note", labels);
    }

    [Fact]
    public void CopyThisDocument_CopiesOnlyTheDocumentInView()
    {
        WithCompletedJob(documents: TwoNotes);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));
        CopyButton(page, "Copy this document").Click();

        // The active tab defaults to the first deliverable.
        Assert.Equal("The assembled note.", LastCopiedText());
    }

    [Fact]
    public void CopyAll_CopiesEveryDocumentUnderItsLabel()
    {
        WithCompletedJob(documents: TwoNotes);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));
        CopyButton(page, "Copy all").Click();

        var copied = LastCopiedText();
        Assert.NotNull(copied);
        Assert.Contains("Consultation note", copied);
        Assert.Contains("The assembled note.", copied);
        Assert.Contains("Patient letter", copied);
        Assert.Contains("Dear patient,", copied);
    }

    [Fact]
    public void ASingleDocument_KeepsCopyNote_AndNoCopyAll()
    {
        WithCompletedJob(documents: OneNote);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var labels = page.FindAll("fluent-button").Select(button => button.TextContent.Trim()).ToArray();
        Assert.Contains("Copy note", labels);
        Assert.DoesNotContain("Copy all", labels);

        CopyButton(page, "Copy note").Click();
        Assert.Equal("The assembled note.", LastCopiedText());
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

    [Fact]
    public void ASkippedBlock_IsANamedLine_InTheSectionsPlace_AndCountsAsDone()
    {
        // v27 (#1236, record storage 9; decided 2026-10-29): an if/case inside a loop skipped the item —
        // the section is a named state with the condition's text, not an absence.
        const string reason = "needs `item.id == \"hpi\"`; it does not hold";
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness"), Block("section-instructions:plan", "Plan") });
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed",
            TotalBlockCount: 2, CompletedBlockCount: 2, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string> { ["section-instructions:hpi"] = "Section prose." },
            FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            AssembledDocument: "Section prose.",
            SkippedBlocks: new Dictionary<string, string> { ["section-instructions:plan"] = reason }));

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        var skipped = page.Find("[data-skipped-block='section-instructions:plan']");
        Assert.Equal($"Plan was skipped: it {reason}.", skipped.TextContent.Trim().Replace("\n", " "));
        Assert.Contains("Section prose.", page.Find(".generated-note").TextContent);
        Assert.Contains("2/2 sections", page.Markup);
    }
}
