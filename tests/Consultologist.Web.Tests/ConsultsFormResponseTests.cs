using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Forms;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #540: the setup form filled from a held form response — coerced by each
/// declaration, editable after the fill, misfits named rather than filled,
/// and the submit carrying the reference only while the value still equals
/// the fill (an edited value is typed text and carries none).
/// </summary>
public class ConsultsFormResponseTests : ClientRenderTestContext
{
    private const string Referral =
        "65M, newly diagnosed adenocarcinoma of the lung, stage IIIA, for consideration of chemoradiation. PMHx HTN.";

    private static readonly DateTimeOffset Submitted = new(2026, 9, 1, 14, 2, 11, TimeSpan.Zero);
    private static readonly DateTimeOffset Deleted = new(2026, 9, 8, 3, 0, 0, TimeSpan.Zero);

    private void WithResponses(string urgentAnswer = "Urgent")
    {
        WithPinnedPackage(
            blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") },
            inputs: new[]
            {
                new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
                new WorkflowPackageInputResponse("urgent", "Urgency", false, WorkflowInputTypes.Enum,
                    Values: new[] { "Routine", "Urgent" })
            });
        FormsService.ListResponsesAsync().Returns(new[]
        {
            new FormResponseListRow("triage-intake", "17", Submitted, new[] { "consult_draft", "urgent" }, null),
            new FormResponseListRow("triage-intake", "9", Submitted.AddDays(-3), new[] { "consult_draft" }, Deleted)
        });
        FormsService.GetResponseAsync("triage-intake", "17").Returns(new FormResponseValues(
            "triage-intake", "17", Submitted,
            new Dictionary<string, string> { ["consult_draft"] = Referral, ["urgent"] = urgentAnswer }));
    }

    private IReadOnlyDictionary<string, ConsultInputValue>? sentInputs;
    private IReadOnlyDictionary<string, ConsultInputFormRef>? sentFormRefs;
    private bool submitted;

    private void CaptureSubmit() =>
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => { sentInputs = value; submitted = true; }),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>?>(),
                Arg.Do<IReadOnlyDictionary<string, ConsultInputFormRef>?>(value => sentFormRefs = value))
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

    private IRenderedComponent<Consults> Rendered()
    {
        var page = Render<Consults>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".form-picker__trigger")));
        return page;
    }

    private static void Choose(IRenderedComponent<Consults> page)
    {
        page.Find(".form-picker__trigger").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".form-picker__choose")));
        page.FindAll(".form-picker__choose").First(b => !b.HasAttribute("disabled")).Click();
    }

    [Fact]
    public void ThePicker_ListsResponsesNewestFirst_AndAdeletedOneIsNotChoosable()
    {
        WithResponses();
        var page = Rendered();

        page.Find(".form-picker__trigger").Click();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".form-picker__choose").Count));
        // Newest first — the held response, then the older discarded one.
        Assert.Contains("17", page.FindAll(".form-picker__choose")[0].TextContent);
        var deleted = page.Find(".form-picker__response--deleted .form-picker__choose");
        Assert.True(deleted.HasAttribute("disabled"));
        Assert.Contains("values deleted Sep 8, 2026", deleted.TextContent);
    }

    [Fact]
    public void ChoosingAResponse_FillsTheDeclaredInputs_Editable()
    {
        WithResponses();
        var page = Rendered();

        Choose(page);

        // The fill is editable form state — the referral in the textarea, the
        // enum selected — not a read-only chip (#510's preview is the other
        // picker's shape).
        page.WaitForAssertion(() => Assert.Contains("filled 2 from triage-intake · 17", page.Markup));
        Assert.Contains(Referral, page.Markup);
        Assert.Empty(page.FindAll(".input-field__loaded"));
    }

    [Fact]
    public void AMisfitAnswer_IsNamed_NotFilled()
    {
        // E2: an *Other* choice arrives as free text indistinguishable from a
        // declared option.
        WithResponses(urgentAnswer: "As soon as the family arrives");
        var page = Rendered();

        Choose(page);

        page.WaitForAssertion(() => Assert.Contains("Not filled:", page.Markup));
        Assert.Contains("Urgency is not one of the declared values", page.Markup);
        Assert.DoesNotContain("As soon as the family arrives", page.FindAll("select").Select(s => s.GetAttribute("value") ?? "").ToList());
        Assert.Contains("filled 1 from", page.Markup);
    }

    [Fact]
    public void TheSubmit_CarriesTheValueAndItsReference()
    {
        WithResponses();
        CaptureSubmit();
        var page = Rendered();
        Choose(page);
        page.WaitForAssertion(() => Assert.Contains(Referral, page.Markup));

        page.FindAll("fluent-button").Last().Click();

        page.WaitForAssertion(() => Assert.True(submitted));
        // Unlike InputRefs: the VALUE travels in Inputs, the reference beside it.
        Assert.Equal(Referral, sentInputs!["consult_draft"].Text);
        Assert.Equal(new ConsultInputFormRef("triage-intake", "17"), sentFormRefs!["consult_draft"]);
        Assert.Equal(new ConsultInputFormRef("triage-intake", "17"), sentFormRefs!["urgent"]);
    }

    [Fact]
    public void AnEditedInput_CarriesNoReference()
    {
        WithResponses();
        CaptureSubmit();
        var page = Rendered();
        Choose(page);
        page.WaitForAssertion(() => Assert.Contains(Referral, page.Markup));

        // The clinician reviews and edits — the value is now typed text.
        page.FindAll("fluent-text-area")[0].Change(Referral + " Now with the clinician's own addition.");
        page.FindAll("fluent-button").Last().Click();

        page.WaitForAssertion(() => Assert.True(submitted));
        Assert.False(sentFormRefs?.ContainsKey("consult_draft") ?? false);
        // The untouched enum keeps its reference.
        Assert.Equal(new ConsultInputFormRef("triage-intake", "17"), sentFormRefs!["urgent"]);
    }
}
