using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1098 (Milestone 31): the Renderers nav group at 25 — the engine's renderer pipes
/// (concept_context, concept_bullets) as read-only entries after Formats, each opening a
/// pane keyed renderer:&lt;name&gt; that names the format it renders, the spelling to insert
/// (one per node of that format, else the generic one), a Copy button with transient
/// feedback, and "Used by" rows from the walk — the template piping a node through the
/// pipe and the node, each a link. Nothing is added, edited or discarded there; a ≤24
/// package shows no group.
/// </summary>
public class TemplatesV25RenderersPaneTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static string SelectedNav(IRenderedComponent<Templates> page) =>
        page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim();

    private static IReadOnlyList<string> Groups(IRenderedComponent<Templates> page) =>
        page.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()).ToList();

    private WorkflowEditorSession Session => Services.GetRequiredService<WorkflowEditorSession>();

    private IRenderedComponent<Templates> OpenRenderer(string pipe, WorkflowPackageContentResponse? fixture = null)
    {
        var page = RenderEditor(fixture);
        Session.SelectedKey = $"renderer:{pipe}";
        page.Render();
        return page;
    }

    /// <summary>The V25 fixture with its assessment prompt piping the concept_list node through concept_context — reshaped here alone.</summary>
    private static WorkflowPackageContentResponse V25WithARenderedRead()
    {
        var package = EditorFixtures.V25();
        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal);
        files["prompts/assessment.md"] = files["prompts/assessment.md"] + "\n{{ node.extract.content | concept_context }}";
        return package with { Files = files };
    }

    /// <summary>The V25 fixture with no node of format concept_list — extract made prose.</summary>
    private static WorkflowPackageContentResponse V25WithoutAConceptListNode()
    {
        var package = EditorFixtures.V25();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        var extract = root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "extract")!.AsObject();
        extract["format"] = "prose";
        // coverage reads node.extract as a concept_list; drop it and the deliverable's check so the walk stays quiet.
        var nodes = root["nodes"]!.AsArray();
        var coverage = nodes.First(n => n!["id"]!.GetValue<string>() == "coverage");
        nodes.Remove(coverage);
        root["deliverables"]!.AsArray()[0]!.AsObject().Remove("check");
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    [Fact]
    public void TheGroup_ListsThePipes_At25_AfterFormats_AndIsAbsentBelow()
    {
        var page = RenderEditor();

        var groups = Groups(page);
        Assert.Equal(groups.ToList().IndexOf("Formats") + 1, groups.ToList().IndexOf("Renderers"));

        var entries = page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Replace("●", string.Empty).Trim()).ToList();
        Assert.Contains("concept_context", entries);
        Assert.Contains("concept_bullets", entries);
        Assert.DoesNotContain(entries, e => e.StartsWith("+ Renderer", StringComparison.Ordinal));

        var below = RenderEditor(EditorFixtures.V12());
        Assert.DoesNotContain("Renderers", Groups(below));
    }

    [Fact]
    public void TheKey_OpensThePane_NamingTheFormatItRenders()
    {
        var page = OpenRenderer("concept_context");

        Assert.Equal("concept_context", SelectedNav(page));
        Assert.Equal("concept_context", page.Find("h3.panel-title").TextContent.Trim());
        var identity = page.Find(".pane-identity").TextContent;
        Assert.Contains("renders concept_list", identity);
        Assert.Contains("the engine's", identity);
        Assert.Empty(page.FindAll("textarea"));
    }

    [Fact]
    public void TheNavEntry_OpensThePane()
    {
        var page = RenderEditor();
        Navigate(page, "concept_bullets");

        Assert.Equal("renderer:concept_bullets", Session.SelectedKey);
        Assert.Equal("concept_bullets", page.Find("h3.panel-title").TextContent.Trim());
    }

    [Fact]
    public void ThePane_NamesTheSpelling_PerConceptListNode_WithALinkToTheNode()
    {
        var page = OpenRenderer("concept_context");

        var spelling = page.Find(".renderer-spelling[data-renderer-node='extract']");
        Assert.Equal("{{ node.extract.content | concept_context }}", spelling.QuerySelector("code")!.TextContent);
        Assert.Empty(page.FindAll(".renderer-spelling__none"));

        page.Find(".renderer-spelling[data-renderer-node='extract'] fluent-button[aria-label='Open node extract']").Click();
        Assert.Equal("node:extract", Session.SelectedKey);
        Assert.Equal("Extract concepts", SelectedNav(page));
    }

    [Fact]
    public void ThePane_FallsBackToTheGenericSpelling_WhenNoNodeProducesTheFormat()
    {
        var page = OpenRenderer("concept_bullets", V25WithoutAConceptListNode());

        var spelling = Assert.Single(page.FindAll(".renderer-spelling"));
        Assert.Equal("{{ node.<id>.content | concept_bullets }}", spelling.QuerySelector("code")!.TextContent);
        Assert.Contains("No node produces concept_list yet", page.Find(".renderer-spelling__none").TextContent);
    }

    [Fact]
    public void UsedBy_ListsTheTemplate_AndTheNode_AsLinks()
    {
        var page = OpenRenderer("concept_context", V25WithARenderedRead());

        var row = Assert.Single(page.FindAll(".renderer-user"));
        Assert.Equal("extract", row.GetAttribute("data-renderer-user-node"));
        Assert.Equal("Prompt 'assessment'", row.QuerySelector(".renderer-user__template")!.TextContent.Trim());
        Assert.Equal("Extract concepts", row.QuerySelector(".renderer-user__node")!.TextContent.Trim());
        Assert.Empty(page.FindAll(".renderer-users__empty"));

        page.Find(".renderer-user .renderer-user__template").Click();
        Assert.Equal("prompts/assessment.md", Session.SelectedKey);

        Session.SelectedKey = "renderer:concept_context";
        page.Render();
        page.Find(".renderer-user .renderer-user__node").Click();
        Assert.Equal("node:extract", Session.SelectedKey);
    }

    [Fact]
    public void UsedBy_IsPerPipe_AndEmptyOtherwise()
    {
        var page = OpenRenderer("concept_bullets", V25WithARenderedRead());

        Assert.Empty(page.FindAll(".renderer-user"));
        Assert.Contains("No template pipes a node through concept_bullets yet", page.Find(".renderer-users__empty").TextContent);

        var plain = OpenRenderer("concept_context");
        Assert.Empty(plain.FindAll(".renderer-user"));
        Assert.NotNull(plain.Find(".renderer-users__empty"));
    }

    [Fact]
    public void TheCopyButton_WritesTheSpelling_AndFlipsItsLabel()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetVoidResult();
        var page = OpenRenderer("concept_context");

        var copy = page.Find("fluent-button[aria-label='Copy the spelling for node extract']");
        Assert.Equal("Copy", copy.TextContent.Trim());
        copy.Click();

        Assert.Equal("{{ node.extract.content | concept_context }}", JSInterop.Invocations["navigator.clipboard.writeText"].Last().Arguments[0]);
        page.WaitForAssertion(() => Assert.Equal("Copied ✓", page.Find("fluent-button[aria-label='Copy the spelling for node extract']").TextContent.Trim()));
    }

    [Fact]
    public void ThePane_HasTheHelpExpander_AtItsFoot()
    {
        var page = OpenRenderer("concept_context");

        var help = page.Find("details.pane-help");
        Assert.Contains("How renderers work", help.TextContent);
        Assert.Contains("The engine's", help.TextContent);
    }

    [Fact]
    public void ThePane_SurvivesLeavingAndReturning()
    {
        var page = OpenRenderer("concept_bullets");
        Navigate(page, "Graph");
        Assert.Equal("Graph", SelectedNav(page));

        Navigate(page, "concept_bullets");
        Assert.Equal("concept_bullets", SelectedNav(page));
        Assert.Equal("concept_bullets", page.Find("h3.panel-title").TextContent.Trim());
    }
}
