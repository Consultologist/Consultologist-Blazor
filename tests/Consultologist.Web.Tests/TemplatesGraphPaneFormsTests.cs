using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1222 — the Graph and Deliverables panes speak the package's own format: the
/// Graph pane's string-`result` select renders only for the single-deliverable
/// form, the ≤24 "not feeding" warning yields to the validator from 25, the
/// Deliverables subtitle names sources at 26 and the Graph intro names format,
/// prompt and reads from 25. Both sides of each line are pinned.
/// </summary>
public class TemplatesGraphPaneFormsTests : ClientRenderTestContext
{
    private const string ResultSelect = ".result-selector fluent-select";
    private const string NotFeeding = "Not feeding the deliverable yet";
    private const string IntroAt25 = "format, prompt and reads";
    private const string IntroBelow = "output contract, prompt bindings and variables";
    private const string SubtitleAt26 = "names its sources";
    private const string SubtitleBelow = "names its own aggregator";

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private IRenderedComponent<Templates> ShowGraph(WorkflowPackageContentResponse fixture)
    {
        var page = RenderEditor(fixture);
        Select(page, "graph");
        return page;
    }

    private static string GraphText(IRenderedComponent<Templates> page) => page.Find(".editor-pane").TextContent;

    private static bool HasAddNode(IRenderedComponent<Templates> page) =>
        page.FindAll(".result-selector fluent-button").Any(button => button.TextContent.Trim() == "+ Node");

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    [Fact]
    public void At26_TheGraphPane_HasNoDeliverableSelect_NoWarning_AndSpeaksFormatPromptReads()
    {
        var page = ShowGraph(EditorFixtures.V26());

        Assert.Empty(page.FindAll(ResultSelect));
        Assert.True(HasAddNode(page));
        var text = GraphText(page);
        Assert.DoesNotContain(NotFeeding, text);
        Assert.Contains(IntroAt25, text);
        Assert.DoesNotContain(IntroBelow, text);
    }

    [Fact]
    public void At26_TheDeliverablesPane_NamesSources()
    {
        var page = RenderEditor(EditorFixtures.V26());
        Select(page, "results");

        var text = GraphText(page);
        Assert.Contains(SubtitleAt26, text);
        Assert.DoesNotContain(SubtitleBelow, text);
    }

    [Fact]
    public void At26_AnUnrelatedEdit_ComposesNoStringResult()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V26());
        Select(page, "results:consult");

        page.Find("input[aria-label='Label for deliverable consult']").Change("Consultation letter");
        Publish(page);

        Assert.NotNull(sent);
        var root = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;
        Assert.False(root.TryGetProperty("result", out _));
        Assert.False(root.TryGetProperty("Result", out _));
        var consult = root.GetProperty("deliverables").EnumerateArray().Single(d => d.GetProperty("id").GetString() == "consult");
        Assert.Equal("Consultation letter", consult.GetProperty("label").GetString());
        Assert.True(consult.TryGetProperty("sources", out _));
    }

    [Fact]
    public void At25_TheGraphPane_HasNoSelectAndNoWarning_AndTheSubtitleStillNamesTheAggregator()
    {
        var page = ShowGraph(EditorFixtures.V25());

        Assert.Empty(page.FindAll(ResultSelect));
        Assert.True(HasAddNode(page));
        var graph = GraphText(page);
        Assert.DoesNotContain(NotFeeding, graph);
        Assert.Contains(IntroAt25, graph);

        Select(page, "results");
        var deliverables = GraphText(page);
        Assert.Contains(SubtitleBelow, deliverables);
        Assert.DoesNotContain(SubtitleAt26, deliverables);
    }

    [Fact]
    public void At24_AListedPackage_HasNoSelect_KeepsTheOlderIntro_AndNoWarningWhenEveryNodeFeeds()
    {
        var page = ShowGraph(EditorFixtures.V24Full());

        Assert.Empty(page.FindAll(ResultSelect));
        Assert.True(HasAddNode(page));
        var text = GraphText(page);
        Assert.Contains(IntroBelow, text);
        Assert.DoesNotContain(IntroAt25, text);
        Assert.DoesNotContain(NotFeeding, text);
    }

    [Fact]
    public void At12_TheWarningStillNamesTheNodeThatFeedsNothing()
    {
        var page = ShowGraph(EditorFixtures.V12Full());

        var text = GraphText(page);
        Assert.Contains(NotFeeding, text);
        Assert.Contains("Is the referral in scope?", text);
    }

    [Fact]
    public void At6_TheSingleDeliverableForm_KeepsItsSelect()
    {
        var page = ShowGraph(EditorFixtures.V6());

        var select = page.Find(ResultSelect);
        Assert.Equal("node:assemble-note", select.GetAttribute("current-value"));
        Assert.True(HasAddNode(page));
        Assert.Contains(IntroBelow, GraphText(page));
    }
}
