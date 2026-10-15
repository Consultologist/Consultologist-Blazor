using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C4 (#1010, design §§ 1, 2, 13): the node pane's derived, read-only Reads list in the
/// Inputs section's place — what the node's prompt (or a computed node's parameters)
/// references, from the engine's walk, each row a link; the walker's warnings word for word.
/// Rendered at 25 alone; the ≤24 Inputs section stays. Inert on the live editor until A5.
/// </summary>
public class TemplatesV25ReadsTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null, bool withCatalogue = true)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V25());
        if (withCatalogue)
        {
            WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        }

        return Render<Templates>();
    }

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static string Selected(IRenderedComponent<Templates> page) =>
        page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim();

    private static IReadOnlyList<string> Heads(IRenderedComponent<Templates> page) =>
        page.FindAll(".node-section__head").Select(h => h.TextContent.Trim()).ToList();

    private static IReadOnlyList<string> Reads(IRenderedComponent<Templates> page, string nodeId) =>
        page.Find($"ul.node-reads[aria-label='Reads of {nodeId}']").QuerySelectorAll("li").Select(li => li.TextContent.Trim()).ToList();

    private static WorkflowPackageContentResponse WithFile(string path, string text)
    {
        var package = EditorFixtures.V25();
        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal) { [path] = text };
        return package with { Files = files };
    }

    [Fact]
    public void APromptedNode_ListsItsReads_InTheInputsPlace()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");

        var heads = Heads(page);
        Assert.Contains("Reads", heads);
        Assert.DoesNotContain("Inputs", heads);
        Assert.Empty(page.FindAll(".add-variable"));
        Assert.Empty(page.FindAll(".binding-row"));

        Assert.Equal(
            new[] { "input.intake", "input.notes", "node.extract.value", "node.meds.value", "snippet.standards.text", "macro.tool_note.prompt (slot)" },
            Reads(page, "assessment"));
    }

    [Fact]
    public void AReadRow_OpensTheThingsPane()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");

        page.FindAll("ul.node-reads button").First(b => b.TextContent.Trim() == "node.extract.value").Click();
        Assert.Equal("Extract concepts", Selected(page));

        ShowNode(page, "assessment");
        page.FindAll("ul.node-reads button").First(b => b.TextContent.Trim() == "snippet.standards.text").Click();
        Assert.Equal("standards", Selected(page));

        ShowNode(page, "assessment");
        page.FindAll("ul.node-reads button").First(b => b.TextContent.Trim() == "input.notes").Click();
        Assert.Equal("Inputs", Selected(page));
    }

    [Fact]
    public void AFanNode_NamesTheItemMembersItReads_WithoutALink()
    {
        var page = RenderEditor();
        ShowNode(page, "section_notes");

        var rows = page.Find("ul.node-reads[aria-label='Reads of section_notes']").QuerySelectorAll("li").ToList();
        Assert.Equal(new[] { "item.name — the fan item", "item.text — the fan item" }, rows.Select(li => li.TextContent.Trim()));
        Assert.All(rows, li => Assert.NotNull(li.QuerySelector("span.snippet-readers__row")));
    }

    [Fact]
    public void AComputedNode_ListsItsParameterReads()
    {
        var page = RenderEditor();
        ShowNode(page, "coverage");

        Assert.Contains("Reads", Heads(page));
        Assert.Equal(new[] { "with: node.extract.value" }, Reads(page, "coverage"));
    }

    [Fact]
    public void AnAggregator_KeepsItsOrderedSources_HeadedSources()
    {
        var page = RenderEditor();
        ShowNode(page, "all");

        var heads = Heads(page);
        Assert.Contains("Sources", heads);
        Assert.DoesNotContain("Inputs", heads);
        Assert.DoesNotContain("Reads", heads);
        Assert.NotNull(page.Find("ul.binding-list"));
    }

    [Fact]
    public void ABareTextReadOfAStructuredFormat_ShowsTheWalkersWarning_AndDoesNotBlockPublish()
    {
        WorkflowPackagePublishRequest? sent = null;
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(r => sent = r))
            .Returns(new WorkflowPublishOutcome(new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"), Array.Empty<string>()));
        var page = RenderEditor();
        Navigate(page, "assessment");
        page.Find(".editor-pane fluent-text-area").Change("Assess.\n{{ snippet.standards.text }}\n{{ macro.tool_note.prompt }}\n{{ node.extract.text }}\n{{ for m in node.meds.value.medications }}- {{ m.name }}\n{{ end }}");
        ShowNode(page, "assessment");

        var warning = page.Find(".node-reads__warning").TextContent.Trim();
        Assert.StartsWith("Prompt 'assessment' reads node.extract.text of format 'concept_list'", warning);
        Assert.EndsWith("the JSON renders as text — read node.extract.value for the fields.", warning);
        Assert.Contains("node.extract.text", Reads(page, "assessment"));

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.NotNull(sent);
    }

    [Fact]
    public void AnUnresolvedReference_IsSaidEarly_OnTheReadsList()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", "Assess {{ node.nope.text }} and {{ snippet.standards.text }}"));
        ShowNode(page, "assessment");

        Assert.Equal("1 reference does not resolve — Publish says which.", page.Find(".node-reads__unresolved").TextContent.Trim());
    }

    [Fact]
    public void WithoutTheCatalogue_APromptOnACatalogueFormat_IsNotWalked()
    {
        var page = RenderEditor(withCatalogue: false);
        ShowNode(page, "assessment");

        Assert.Contains("Not walked — the format catalogue is unavailable.", page.Find(".node-reads-section").TextContent);

        // meds is on the package-local format: walked without the catalogue.
        ShowNode(page, "meds");
        Assert.Equal(new[] { "input.notes" }, Reads(page, "meds"));
    }

    // ----- compose at 25: the retired keys are never written -----

    [Fact]
    public void ANewPromptedNode_ComposesAPrompt_WithoutVariablesOrRaw_AndRepointingWritesNoBindings()
    {
        WorkflowPackagePublishRequest? sent = null;
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(r => sent = r))
            .Returns(new WorkflowPublishOutcome(new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"), Array.Empty<string>()));
        var page = RenderEditor();

        // + Node on a prompted format mints its own prompt.
        Navigate(page, "+ Node");
        page.Find("fluent-select.new-node-format").Change("prose");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("plan");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Plan");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create")).Click();
        page.Find("fluent-text-area").Change("Plan from {{ input.notes }}.");
        ShowNode(page, "all");
        page.Find("fluent-select[aria-label='Add aggregate source']").Change("node.plan");

        // Re-point meds at the scope prompt (the section prompt reads the fan item, which meds has not).
        ShowNode(page, "meds");
        page.Find("fluent-select[aria-label='Node prompt']").Change("scope");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

        Assert.NotNull(sent);
        var root = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;
        var plan = root.GetProperty("prompts").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "plan");
        Assert.False(plan.TryGetProperty("variables", out _));
        Assert.False(plan.TryGetProperty("raw", out _));
        var meds = root.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "meds");
        Assert.Equal("scope", meds.GetProperty("prompt").GetString());
        Assert.False(meds.TryGetProperty("bindings", out _));
    }

    [Fact]
    public void AReaderRow_FindsThePrompt_WhenItsIdDiffersFromTheNodes()
    {
        // The walk keys a prompt by the NODE reading it; the pane key must go through the node (C2 gap).
        var package = EditorFixtures.V25();
        var manifest = package.Manifest.GetRawText()
            .Replace("\"id\": \"assessment\", \"file\": \"prompts/assessment.md\"", "\"id\": \"assess\", \"file\": \"prompts/assessment.md\"")
            .Replace("\"prompt\": \"assessment\"", "\"prompt\": \"assess\"");
        var page = RenderEditor(package with { Manifest = JsonDocument.Parse(manifest).RootElement.Clone() });

        Navigate(page, "tool_note");
        var readers = page.Find("ul.macro-readers[aria-label='Readers of tool_note']");
        Assert.Contains("Prompt 'assess'", readers.TextContent);
        readers.QuerySelector("button")!.Click();

        Assert.Equal("assess", Selected(page));
    }
}
