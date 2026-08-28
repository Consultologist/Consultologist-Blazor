using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using AngleSharp.Dom;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #510: a previous run's deliverable into an input slot. The form shows the
/// text as a preview and sends the reference; a run whose text was deleted is
/// listed and not choosable; the loaded text is shown, never editable.
/// </summary>
public class ConsultsPreviousRunTests : ClientRenderTestContext
{
    private const string Note = "The earlier consultation note: 65M with stage IIIA adenocarcinoma, for consideration of chemoradiation.";

    private const string HeldRun = "0123456789abcdef0123456789abcdef";
    private const string DroppedRun = "fedcba9876543210fedcba9876543210";
    private static readonly DateTimeOffset Completed = new(2026, 8, 27, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Dropped = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private static WorkflowPackageInputResponse[] DraftAndNotes() => new[]
    {
        new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
        new WorkflowPackageInputResponse("prior_notes", "Prior notes", false, WorkflowInputTypes.Array,
            Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Text))
    };

    private void WithRuns()
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") }, inputs: DraftAndNotes());
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(HeldRun, "Completed", Completed, Completed, Completed, TotalBlockCount: 2, CompletedBlockCount: 2, FailedBlockCount: 0),
                new AccountJobSummaryResponse(DroppedRun, "Completed", Completed, Completed, Completed, TotalBlockCount: 2, CompletedBlockCount: 2, FailedBlockCount: 0, TextDroppedAtUtc: Dropped),
                new AccountJobSummaryResponse("aaaa", "Failed", Completed, Completed, Completed, TotalBlockCount: 2, CompletedBlockCount: 0, FailedBlockCount: 2)
            },
            null));
        AIService.GetConsultGenerationJobAsync(HeldRun).Returns(new ConsultGenerationJobResponse(
            HeldRun, "user-1", "Completed", TotalBlockCount: 2, CompletedBlockCount: 2, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(), FailedBlocks: new Dictionary<string, string>(), Success: true,
            CompletedAtUtc: Completed, PackageTitle: "General consult",
            AssembledDocuments: new[]
            {
                new ConsultGenerationResultDocumentResponse("consult", "Consultation note", Note),
                new ConsultGenerationResultDocumentResponse("letter", "Letter", null)
            }));
    }

    private IReadOnlyDictionary<string, ConsultInputValue>? sentInputs;
    private IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>? sentRefs;

    private void CaptureSubmit() =>
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sentInputs = value),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(),
                Arg.Do<IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>?>(value => sentRefs = value))
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

    private static IRenderedComponent<Consults> Rendered(BunitContext ctx)
    {
        var page = ctx.Render<Consults>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".run-picker__trigger")));
        return page;
    }

    private static void OpenPicker(IRenderedComponent<Consults> page, int slot) =>
        page.FindAll(".run-picker__trigger")[slot].Click();

    private static void ChooseNote(IRenderedComponent<Consults> page, int slot)
    {
        OpenPicker(page, slot);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".run-picker__run-toggle")));
        page.FindAll(".run-picker__run-toggle").First(b => !b.HasAttribute("disabled")).Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".run-picker__deliverable")));
        page.FindAll(".run-picker__deliverable").First(b => b.TextContent.Contains("Consultation note")).Click();
    }

    [Fact]
    public void ThePicker_ListsCompletedRuns_AndSaysWhoseTextWasDeleted()
    {
        WithRuns();
        var page = Rendered(this);

        OpenPicker(page, 0);

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".run-picker__run-toggle").Count));
        var deleted = page.Find(".run-picker__run--deleted .run-picker__run-toggle");
        Assert.True(deleted.HasAttribute("disabled"));
        Assert.Contains("text deleted Sep 2, 2026", deleted.TextContent);
        Assert.DoesNotContain("aaaa", page.Markup);
    }

    [Fact]
    public void ExpandingARun_ListsItsDeliverables_AndADeletedOneIsNotChoosable()
    {
        WithRuns();
        var page = Rendered(this);

        OpenPicker(page, 0);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".run-picker__run-toggle")));
        page.FindAll(".run-picker__run-toggle")[0].Click();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".run-picker__deliverable").Count));
        Assert.Contains("General consult", page.Markup);
        var letter = page.FindAll(".run-picker__deliverable").First(b => b.TextContent.Contains("Letter"));
        Assert.True(letter.HasAttribute("disabled"));
        Assert.Contains("text deleted", letter.TextContent);
    }

    [Fact]
    public void ChoosingADeliverable_FillsTheSlotAsAPreview_AndTheSubmitCarriesTheReference()
    {
        WithRuns();
        CaptureSubmit();
        var page = Rendered(this);

        ChooseNote(page, 0);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".input-field__loaded")));
        Assert.Contains("Consultation note", page.Find(".input-field__loaded .input-field__chip-name").TextContent);
        Assert.Contains("run of Aug 27, 2026", page.Find(".input-field__loaded").TextContent);
        Assert.Equal(Note, page.Find(".input-field__loaded .input-field__preview").TextContent.Trim());

        page.FindAll("fluent-button").Last().Click();
        page.WaitForAssertion(() => Assert.NotNull(sentRefs));
        Assert.Equal(new ConsultInputRef(HeldRun, "consult"), Assert.Single(sentRefs!["consult_draft"]));
        Assert.False(sentInputs!.ContainsKey("consult_draft"));
    }

    [Fact]
    public void AnArrayOfText_TakesOneDeliverablePerEntry()
    {
        WithRuns();
        CaptureSubmit();
        var page = Rendered(this);
        page.FindAll("fluent-text-area")[0].Change("Referral text, long enough to be a referral and pass the floor for content.");

        ChooseNote(page, 1);
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".input-field__loaded")));
        // The loaded branch offers the picker again for another entry.
        ChooseNote(page, 1);
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".input-field__loaded").Count));

        page.FindAll("fluent-button").Last().Click();

        page.WaitForAssertion(() => Assert.NotNull(sentRefs));
        Assert.Equal(2, sentRefs!["prior_notes"].Count);
        Assert.False(sentInputs!.ContainsKey("prior_notes"));
        Assert.True(sentInputs.ContainsKey("consult_draft"));
    }

    [Fact]
    public void ALoadedDeliverable_IsShown_NotEditable()
    {
        // The words are the run's, verbatim: no control turns them into text
        // the user can change. Remove and paste is the way to different words.
        WithRuns();
        var page = Rendered(this);
        ChooseNote(page, 0);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".input-field__loaded")));

        Assert.Empty(page.FindAll(".input-field__edit-loaded"));
        Assert.Empty(page.FindAll(".input-field__loaded textarea, .input-field__loaded fluent-text-area"));
        Assert.Contains("exactly as that run produced it", page.Find(".input-field__hint").TextContent);
    }

    [Fact]
    public void Remove_ClearsTheLoadedDeliverable()
    {
        WithRuns();
        var page = Rendered(this);
        ChooseNote(page, 0);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".input-field__loaded")));

        page.FindAll(".input-field__loaded fluent-button").First(b => b.TextContent.Contains("Remove")).Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".input-field__loaded")));
    }
}
