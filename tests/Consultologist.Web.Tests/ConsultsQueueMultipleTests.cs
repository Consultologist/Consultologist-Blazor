using System.Linq;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #793: the "Queue multiple" toggle — Create starts the job now but stays on the
/// setup form with emptied fields (the job runs server-side and appears in
/// History), instead of opening the live run view. Disabled while Run overnight
/// is on, since overnight already clears and stays.
/// </summary>
public class ConsultsQueueMultipleTests : ClientRenderTestContext
{
    private static readonly IReadOnlyList<WorkflowPackageBlockResponse> Sections =
        new[] { Block("section-instructions:hpi", "hpi") };

    private void CaptureSubmit() =>
        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("0123456789abcdef0123456789abcdef", "Scheduled"));

    // The FluentSwitch toggles are awkward to drive in bUnit; set the bound field
    // and re-render (the handler reads the field at click time regardless).
    private static void Set(IRenderedComponent<Consults> page, string field, object? value)
    {
        typeof(Consults).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page.Instance, value);
        page.Render();
    }

    private static IElement Submit(IRenderedComponent<Consults> page) => page.FindAll("fluent-button").Last();

    [Fact]
    public async Task QueueMultiple_StartsTheJob_ButStaysOnTheSetupForm()
    {
        WithPinnedPackage(blocks: Sections);
        CaptureSubmit();
        var page = Render<Consults>();

        page.FindAll("fluent-text-area")[0].Change("Referral.");
        Set(page, "queueMultiple", true);
        await Submit(page).ClickAsync(new());

        await AIService.Received(1).StartConsultGenerationJobAsync(
            Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(),
            Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>());
        // Stayed on the setup form — not the run view.
        Assert.NotEmpty(page.FindAll(".consult-setup"));
        Assert.Empty(page.FindAll(".consult-run"));
        // And a confirmation tells the clinician the queue took.
        Assert.Contains("running now", page.Markup);
    }

    [Fact]
    public async Task Overnight_ShowsItsConfirmation_AfterClearing()
    {
        // #793: the scheduled-run confirmation is set after ClearInputs (which
        // nulls it), so it survives and renders instead of being wiped.
        WithPinnedPackage(blocks: Sections);
        CaptureSubmit();
        var page = Render<Consults>();

        page.FindAll("fluent-text-area")[0].Change("Referral.");
        Set(page, "runOvernight", true);
        Set(page, "scheduledAtLocal", DateTime.Now.AddDays(1).ToString("yyyy-MM-ddTHH:mm"));
        await Submit(page).ClickAsync(new());

        var confirmation = (string?)typeof(Consults)
            .GetField("scheduleConfirmation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(page.Instance);
        Assert.False(string.IsNullOrEmpty(confirmation));
        Assert.Contains(confirmation!, page.Markup);
    }

    [Fact]
    public void RunOvernight_DisablesQueueMultiple()
    {
        WithPinnedPackage(blocks: Sections);
        var page = Render<Consults>();

        // Two switches in the action row: [0] Run overnight, [1] Queue multiple.
        Assert.False(page.FindAll("fluent-switch")[1].HasAttribute("disabled"));

        Set(page, "runOvernight", true);
        Assert.True(page.FindAll("fluent-switch")[1].HasAttribute("disabled"));
    }
}
