using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1066: the editor on the manifest shape the Functions worker's default
/// serializer sends — every member of the record, PascalCase, "Results": null
/// beside "Deliverables": [ … ]. Before the reader read a null-valued key as
/// absent, a 25 package showed the single-deliverable form, its aggregator lost
/// the deliverable mark and the remove guard, and a macro removal published
/// "deliverables": [] — the loss acct-…/test/general3@v2026.10.3 shipped with.
/// The camelCase fixture suites cover the registry's shape; this one covers the
/// wire's, and stays after the content endpoint writes camelCase too.
/// </summary>
public class TemplatesV25DeliverablesShapeTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.AsWorkerSerialized(EditorFixtures.V25()));
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    [Fact]
    public void TheWorkerShape_CarriesResultsNullBesideDeliverables()
    {
        // The fixture is the trap, or the suite proves nothing.
        var manifest = EditorFixtures.AsWorkerSerialized(EditorFixtures.V25()).Manifest;

        Assert.Equal(JsonValueKind.Null, manifest.GetProperty("Results").ValueKind);
        Assert.Equal(JsonValueKind.Null, manifest.GetProperty("Result").ValueKind);
        Assert.Equal(1, manifest.GetProperty("Deliverables").GetArrayLength());
    }

    [Fact]
    public void TheDeliverablesPane_ListsTheDeclaredDeliverable_NotTheSingleForm()
    {
        var page = RenderEditor();
        Navigate(page, "Deliverables");

        Assert.Equal("node.all", page.Find("fluent-select[aria-label='Node for deliverable consult']").GetAttribute("current-value"));
        Assert.NotNull(page.Find("[aria-label='Label for deliverable consult']"));
        Assert.DoesNotContain("single-deliverable form", page.Find(".editor-pane").TextContent);
        Assert.DoesNotContain("Convert to a deliverable list", page.Find(".editor-pane").TextContent);
    }

    [Fact]
    public void TheGraph_MarksTheAggregatorAsTheDeliverable_AndOffersNoRemove()
    {
        var page = RenderEditor();
        Navigate(page, "Graph");

        var title = page.FindAll("h3.panel-title").Single(h => h.TextContent.Contains("Everything"));
        Assert.Contains("— deliverable", title.TextContent);

        var header = title.ParentElement!.ParentElement!;
        Assert.DoesNotContain("Remove node", header.TextContent);
        Assert.Contains(page.FindAll("fluent-button"), b => b.TextContent.Contains("Remove node"));
    }

    [Fact]
    public void RemovingAMacro_KeepsTheDeliverables_OnPublish()
    {
        // The wipe path: compose rebuilt resultsToWrite from EffectiveResults
        // whenever a macro was removed — an empty read published "deliverables": [].
        // closing is placed on a node and prepended to the deliverable, and read
        // by no prompt, so its removal is clean; tool_note is read through
        // .prompt and the publish pre-flight refuses to orphan that read.
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "closing");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Remove macro")).Click();
        Publish(page);

        Assert.Empty(page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()));
        Assert.NotNull(sent);

        // Compose keeps the loaded key's casing, so the worker's "Deliverables"
        // is written back as such; the publisher re-serializes camelCase.
        var manifest = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;
        Assert.True(manifest.TryGetProperty("Deliverables", out var list) || manifest.TryGetProperty("deliverables", out list));
        var deliverable = Assert.Single(list.EnumerateArray());
        Assert.Equal("consult", deliverable.GetProperty("id").GetString());
        Assert.Equal("node.all", deliverable.GetProperty("node").GetString());
        Assert.Equal("Consultation note", deliverable.GetProperty("label").GetString());
        Assert.Equal("node.coverage", deliverable.GetProperty("check").GetString());
        Assert.False(deliverable.TryGetProperty("macros", out _));
        Assert.False(manifest.TryGetProperty("Results", out _));
        Assert.False(manifest.TryGetProperty("results", out _));
    }
}
