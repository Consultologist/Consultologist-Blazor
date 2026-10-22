using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1095 (Milestone 31): the snippet, folder-item, added-item and prompt panes preview
/// what a reader gets at 25 — the text rendered through the engine's renderer over the
/// token scope (run values as tokens, the package's own texts for real, a macro's .prompt
/// as its instruction and the mechanics sentence, the renderer's own sentence when it does
/// not render) in the Markdown preview's place, with a select per top-level enum input
/// the template reads to preview with a chosen value: unchosen by default (a comparison
/// against the token is false, and a note says so), no pending change, surviving a pane
/// switch. A raw prompt keeps its verbatim preview; a ≤24 package keeps Markdown.
/// </summary>
public class TemplatesV25RenderedPreviewTests : ClientRenderTestContext
{
    private const string KindSelect = "fluent-select[aria-label='Preview with input.kind']";
    private const string GatedAssessment = "{{ if input.kind == \"new\" }}NEW{{ else }}FOLLOW{{ end }}\n{{ snippet.standards.content }}";

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null, bool withCatalogue = true)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V25());
        if (withCatalogue)
        {
            WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        }

        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static void CreateFolder(IRenderedComponent<Templates> page, string id)
    {
        Navigate(page, "+ Snippet folder");
        page.Find(".new-item-fields fluent-text-field").Change(id);
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create folder")).Click();
    }

    private static void AddItem(IRenderedComponent<Templates> page, string id, string name, string text)
    {
        var fields = page.FindAll(".new-item-fields fluent-text-field");
        fields[0].Change(id);
        fields[1].Change(name);
        page.Find(".pane-editor fluent-text-area").Change(text);
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Add to")).Click();
    }

    private static WorkflowPackageContentResponse WithFile(string path, string text)
    {
        var package = EditorFixtures.V25();
        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal) { [path] = text };
        return package with { Files = files };
    }

    private static string Rendered(IRenderedComponent<Templates> page) =>
        page.Find(".pane-preview pre.rendered-preview__text").TextContent;

    private static string PreviewHalf(IRenderedComponent<Templates> page) =>
        page.Find(".pane-preview").TextContent;

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private static AngleSharp.Dom.IElement PromptTextSection(IRenderedComponent<Templates> page) =>
        page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text");

    private int DraftSaves() =>
        JSInterop.Invocations.Count(i => i.Identifier == "localStorage.setItem" && i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal));

    [Fact]
    public void TheSnippetPane_ShowsWhatAReaderGets_NotMarkdown()
    {
        var page = RenderEditor();
        Navigate(page, "standards");

        Assert.Equal("Follow the local standards.", Rendered(page));
        Assert.Contains("What a reader gets", PreviewHalf(page));
        Assert.Empty(page.FindAll(".editor-pane .markdown-preview"));
        Assert.Empty(page.FindAll(".pane-preview fluent-select"));
    }

    [Fact]
    public void TheSnippetPane_FollowsAnEdit()
    {
        var page = RenderEditor();
        Navigate(page, "standards");

        page.Find(".editor-pane fluent-text-area").Change("Standards: {{ input.notes }}");

        Assert.Equal("Standards: ⟨input.notes⟩", Rendered(page));
    }

    [Fact]
    public void TheFolderItemPane_RendersItsText()
    {
        var page = RenderEditor();
        Navigate(page, "HPI");

        Assert.Equal("History: ⟨input.notes⟩", Rendered(page));
    }

    [Fact]
    public void AnAddedItem_RendersItsText()
    {
        var page = RenderEditor();
        CreateFolder(page, "extras");
        AddItem(page, "discharge", "Discharge", "Discharge plan: {{ input.notes }}");

        Assert.Equal("Discharge plan: ⟨input.notes⟩", Rendered(page));
        Assert.DoesNotContain("editor-warning", page.Find(".pane-preview pre.rendered-preview__text").ClassName);
    }

    [Fact]
    public void ThePromptPane_RendersTokensAndTheInlineMacro_NotTheReads()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");

        var rendered = Rendered(page);
        Assert.Contains("Follow the local standards.", rendered);
        Assert.Contains("⟨input.notes⟩", rendered);
        Assert.Contains("Allergies: ⟨input.intake.allergies⟩", rendered);
        Assert.Contains(PF.WorkflowMacroMechanics.Prompt("tool_note", "Note the tools used when a procedure is described."), rendered);
        Assert.DoesNotContain("{{ input.notes }}", rendered);
        Assert.DoesNotContain("{{ snippet.standards.content }}", rendered);
    }

    [Fact]
    public void TheEnumSelect_AppearsOnlyForEnumInputsTheTemplateReads()
    {
        var plain = RenderEditor();
        Navigate(plain, "assessment");
        Assert.Empty(plain.FindAll(".pane-preview fluent-select"));

        var gated = RenderEditor(WithFile("prompts/assessment.md", GatedAssessment));
        Navigate(gated, "assessment");
        var select = Assert.Single(gated.FindAll(".pane-preview fluent-select"));
        Assert.Equal("Preview with input.kind", select.GetAttribute("aria-label"));
        Assert.Equal(new[] { "", "new", "follow_up" }, select.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToArray());

        Navigate(gated, "standards");
        Assert.Empty(gated.FindAll(".pane-preview fluent-select"));
    }

    [Fact]
    public void ChoosingAnEnumValue_RendersThatBranch()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", GatedAssessment));
        Navigate(page, "assessment");

        Assert.StartsWith("FOLLOW", Rendered(page));
        Assert.NotEmpty(page.FindAll(".rendered-preview__note"));

        page.Find(KindSelect).Change("new");
        Assert.StartsWith("NEW", Rendered(page));
        Assert.Empty(page.FindAll(".rendered-preview__note"));

        page.Find(KindSelect).Change("follow_up");
        Assert.StartsWith("FOLLOW", Rendered(page));
        Assert.Empty(page.FindAll(".rendered-preview__note"));

        page.Find(KindSelect).Change("");
        Assert.StartsWith("FOLLOW", Rendered(page));
        Assert.NotEmpty(page.FindAll(".rendered-preview__note"));
    }

    [Fact]
    public void APick_IsNotAPendingChange_AndSurvivesAPaneSwitch()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", GatedAssessment));
        Navigate(page, "assessment");
        var savesBefore = DraftSaves();

        page.Find(KindSelect).Change("new");

        Assert.StartsWith("NEW", Rendered(page));
        Assert.DoesNotContain("●", page.Find("button.editor-nav__item--selected").TextContent);
        Assert.Equal(savesBefore, DraftSaves());

        Navigate(page, "standards");
        Navigate(page, "assessment");
        Assert.StartsWith("NEW", Rendered(page));
        var select = page.Find(KindSelect);
        Assert.Equal("new", select.GetAttribute("current-value") ?? select.GetAttribute("value"));
    }

    [Fact]
    public void ATemplateThatDoesNotParse_ShowsTheRenderersSentence()
    {
        var page = RenderEditor();
        Navigate(page, "standards");

        page.Find(".editor-pane fluent-text-area").Change("{{ if }}");

        var preview = page.Find(".pane-preview pre.rendered-preview__text");
        Assert.Contains("editor-warning", preview.ClassName);
        Assert.Contains("Snippet 'standards' does not parse", preview.TextContent);
    }

    [Fact]
    public void ARawPrompt_KeepsTheVerbatimPreview()
    {
        var page = RenderEditor(EditorFixtures.V15Raw());
        Navigate(page, "disclaimer");

        page.Find(".prompt-raw input[type=checkbox]").Change(true);

        Assert.Contains("Not medical advice.", page.Find(".plain-preview").TextContent);
        Assert.Contains("Plain text (verbatim)", PreviewHalf(page));
        Assert.DoesNotContain("What a reader gets", PreviewHalf(page));
        Assert.Empty(page.FindAll(".rendered-preview__text"));
    }

    [Fact]
    public void Below25_ThePreviewStaysMarkdown()
    {
        var page = RenderEditor(EditorFixtures.V24Full());
        Navigate(page, "assessment");

        Assert.NotEmpty(page.FindAll(".editor-pane .markdown-preview"));
        Assert.DoesNotContain("What a reader gets", PreviewHalf(page));
    }

    // ----- #1108: the node pane's inline prompt editor -----

    [Fact]
    public void TheNodePane_PreviewsWhatAReaderGets_ForItsPrompt()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");

        var section = PromptTextSection(page);
        var rendered = section.QuerySelector("pre.rendered-preview__text")!.TextContent;
        Assert.Contains("Follow the local standards.", rendered);
        Assert.Contains("⟨input.notes⟩", rendered);
        Assert.Contains(PF.WorkflowMacroMechanics.Prompt("tool_note", "Note the tools used when a procedure is described."), rendered);
        Assert.Null(section.QuerySelector(".markdown-preview"));
        Assert.Contains("What a reader gets", section.QuerySelector(".pane-preview")!.TextContent);
    }

    [Fact]
    public void APickMadeOnThePromptsPane_ShowsOnTheNodePane()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", GatedAssessment));
        Navigate(page, "assessment");
        page.Find(KindSelect).Change("new");

        ShowNode(page, "assessment");

        var section = PromptTextSection(page);
        Assert.StartsWith("NEW", section.QuerySelector("pre.rendered-preview__text")!.TextContent);
        var select = section.QuerySelector(KindSelect)!;
        Assert.Equal("new", select.GetAttribute("current-value") ?? select.GetAttribute("value"));
    }

    [Fact]
    public void TheNodePane_Below25_KeepsMarkdown()
    {
        var page = RenderEditor(EditorFixtures.V24Full());
        ShowNode(page, "extract-patient-concepts");

        var section = PromptTextSection(page);
        Assert.NotNull(section.QuerySelector(".markdown-preview"));
        Assert.DoesNotContain("What a reader gets", section.TextContent);
    }

    [Fact]
    public void WithoutTheCatalogue_ThePreviewStillRenders_WithoutAControlRow()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", GatedAssessment), withCatalogue: false);
        Navigate(page, "assessment");

        Assert.Empty(page.FindAll(KindSelect));
        Assert.StartsWith("FOLLOW", Rendered(page));
    }
}
