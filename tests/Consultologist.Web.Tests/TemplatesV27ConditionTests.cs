using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v27 (#1236, design § 1.2, § 6): the condition builder and the case subject inside a loop —
/// an if reads the item (a folder's item.id), the inputs and the nodes it may read, never its
/// loop; a case over an array input's element offers the item's enum field and routes its values.
/// </summary>
public class TemplatesV27ConditionTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> ShowNode(string nodeId, WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
        return page;
    }

    [Fact]
    public void AnIfInsideAFolderLoop_OffersTheItemsId_AndTheNodesItMayRead_NeverTheLoop()
    {
        var page = ShowNode("triage", EditorFixtures.V27Flow());

        var operands = page.Find("fluent-select[aria-label='Condition operand for triage']").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Contains("item:id", operands);
        Assert.Contains("node:scope", operands);
        Assert.Contains("kind", operands);
        Assert.DoesNotContain("node:sections", operands);
        Assert.DoesNotContain("node:patient_draft", operands);
    }

    [Fact]
    public void ACaseInsideAnArrayLoop_OffersTheItemsEnumField_AndRoutesItsValues()
    {
        var page = ShowNode("route", EditorFixtures.V27Route());

        var subjects = page.Find("fluent-select.case-subject").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Contains("item.value.kind", subjects);
        Assert.Contains("input.kind", subjects);
        Assert.Equal("item.value.kind", page.Find("fluent-select.case-subject").GetAttribute("current-value"));
        // Both values route to a branch: nothing is unrouted.
        Assert.Empty(page.FindAll(".case-unrouted"));
    }

    [Fact]
    public void ACaseInsideAnArrayLoop_NamesAnUnroutedValue_FromTheItemsField()
    {
        var fixture = EditorFixtures.V27Route();
        var root = System.Text.Json.Nodes.JsonNode.Parse(fixture.Manifest.GetRawText())!.AsObject();
        var route = root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "route")!.AsObject();
        route["branches"]!.AsArray().RemoveAt(1);
        root["nodes"]!.AsArray().Remove(root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "letter_section")!);
        var oneBranch = new WorkflowPackageContentResponse(fixture.Name, fixture.Version, 27, System.Text.Json.JsonDocument.Parse(root.ToJsonString()).RootElement.Clone(), fixture.Files);

        var page = ShowNode("route", oneBranch);

        Assert.Contains("table", page.Find(".case-unrouted").TextContent);
    }
}
