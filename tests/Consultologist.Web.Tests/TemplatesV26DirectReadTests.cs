using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v26 (#1207, design § 5.4): the read palette offers a folder item directly — one pick per
/// item under its folder's loop (snippet.&lt;folder&gt;.&lt;item&gt;.content; a macro item with the
/// macro idiom) — the read rows name a direct read, the item panes list their direct readers,
/// and the preview renders the item's content and the 26 label members. Rendered through the
/// #1209 seam; below 26 the palette is pinned unchanged and the spelling refused by the library.
/// </summary>
public class TemplatesV26DirectReadTests : ClientRenderTestContext
{
    private const string Assessment = "prompts/assessment.md";

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26Macros());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private string SelectedKey => Services.GetRequiredService<WorkflowEditorSession>().SelectedKey!;

    private static List<string?> Choices(IRenderedComponent<Templates> page, string file) =>
        page.Find($"fluent-select[aria-label='Reference to insert into {file}']").QuerySelectorAll("fluent-option")
            .Where(o => o.GetAttribute("disabled") == null)
            .Select(o => o.GetAttribute("value")).ToList();

    private static IReadOnlyList<string?> ChoiceLabels(IRenderedComponent<Templates> page, string file) =>
        page.Find($"fluent-select[aria-label='Reference to insert into {file}']").QuerySelectorAll("fluent-option")
            .Where(o => o.GetAttribute("disabled") == null)
            .Select(o => o.TextContent.Trim()).ToList();

    private static void Pick(IRenderedComponent<Templates> page, string file, string value)
    {
        page.Find($"fluent-select[aria-label='Reference to insert into {file}']").Change(value);
        page.Find($"fluent-button[aria-label='Insert reference into {file}']").Click();
    }

    private static AngleSharp.Dom.IElement PromptArea(IRenderedComponent<Templates> page) =>
        page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text").QuerySelector("fluent-text-area")!;

    private static IReadOnlyList<(string Reader, string Member, string Text)> Rows(IRenderedComponent<Templates> page, string aria) =>
        page.Find($".template-read-rows[aria-label='{aria}']").QuerySelectorAll(".template-read-row")
            .Select(r => (r.GetAttribute("data-read-reader")!, r.GetAttribute("data-read-member")!, System.Text.RegularExpressions.Regex.Replace(r.TextContent, @"\s+", " ").Trim()))
            .ToList();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(sent!.Manifest.GetRawText(), PF.WorkflowPackageManifestJson.ReadOptions)!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    // ---- the palette ------------------------------------------------------------------

    [Fact]
    public void ThePalette_OffersEachFoldersItems_UnderItsLoop_At26()
    {
        var page = RenderEditor();
        Select(page, "node:assessment");

        var choices = Choices(page, Assessment);
        var loop = choices.IndexOf("{{ for s in snippet.sections }}\n{{ s.content }}\n{{ end }}");
        Assert.True(loop >= 0);
        Assert.Equal("{{ snippet.sections.hpi.content }}", choices[loop + 1]);
        Assert.Equal("{{ snippet.sections.plan.content }}", choices[loop + 2]);
        Assert.Contains("snippet.sections.hpi.content — HPI", ChoiceLabels(page, Assessment));

        var macroLoop = choices.IndexOf("{{ for m in macro.notes }}\n{{ m.content }}\n{{ end }}");
        Assert.True(macroLoop >= 0);
        Assert.Equal("{{ macro.notes.hpi.content }}", choices[macroLoop + 1]);
        Assert.Equal("{{ macro.notes.hpi.prompt }}", choices[macroLoop + 2]);
        Assert.Equal("{{ macro.notes.plan.content }}", choices[macroLoop + 3]);
        Assert.Contains("macro.notes.hpi.prompt (inline) — HPI notes", ChoiceLabels(page, Assessment));
    }

    [Fact]
    public void TheFanItemGroup_OffersLabel_NotName_At26()
    {
        var page = RenderEditor();
        Select(page, "node:section_notes");

        var choices = Choices(page, "prompts/section.md");
        Assert.Contains("{{ item.label }}", choices);
        Assert.DoesNotContain("{{ item.name }}", choices);
    }

    [Fact]
    public void ThePalette_At25_OffersNoItemDirectly()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Select(page, Assessment);

        var choices = Choices(page, Assessment);
        Assert.Contains("{{ for s in snippet.sections }}\n{{ s.content }}\n{{ end }}", choices);
        Assert.DoesNotContain(choices, c => c!.StartsWith("{{ snippet.sections.", StringComparison.Ordinal));
        Assert.DoesNotContain(choices, c => c!.StartsWith("{{ macro.notes", StringComparison.Ordinal));
        Assert.Contains("{{ item.name }}", Choices(RenderAt25Section(), "prompts/section.md"));

        IRenderedComponent<Templates> RenderAt25Section()
        {
            Select(page, "prompts/section.md");
            return page;
        }
    }

    // ---- the read, its row and the preview -----------------------------------------------

    [Fact]
    public void ASnippetItemPick_AppendsTheRead_RowsSayDirectly_ThePreviewShowsTheItem_AndItValidates()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:assessment");

        var before = PromptArea(page).GetAttribute("current-value");
        Pick(page, Assessment, "{{ snippet.sections.hpi.content }}");
        Assert.Equal(before + "\n{{ snippet.sections.hpi.content }}", PromptArea(page).GetAttribute("current-value"));

        var row = Rows(page, $"Reads of {Assessment}").Single(r => r.Member == "snippet.sections.hpi.content");
        Assert.Equal("Item 'hpi' of folder 'sections'", row.Reader);
        Assert.Contains("directly", row.Text);
        page.Find($".template-read-rows[aria-label='Reads of {Assessment}'] .template-read-row[data-read-member='snippet.sections.hpi.content'] .template-read-row__reader").Click();
        Assert.Equal("snippets/sections/hpi.md", SelectedKey);

        Select(page, "node:assessment");
        Assert.Contains("History: ⟨input.notes⟩", page.Find(".pane-preview pre.rendered-preview__text").TextContent);
        Publish(page);

        Assert.NotNull(sent);
        Assert.EndsWith("{{ snippet.sections.hpi.content }}", sent!.Files[Assessment]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void AMacroItemPromptPick_RowsSayTheItemInline_AndTheFolderIteration_AndItValidates()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:assessment");

        Pick(page, Assessment, "{{ macro.notes.hpi.prompt }}");

        var rows = Rows(page, $"Reads of {Assessment}");
        var item = rows.Single(r => r.Member == "macro.notes.hpi.prompt");
        Assert.Equal("Macro item 'hpi' of folder 'notes'", item.Reader);
        Assert.Contains("inline", item.Text);
        Assert.Contains("directly", item.Text);
        var folder = rows.Single(r => r.Member == "macro.notes");
        Assert.Equal("Macro folder 'notes'", folder.Reader);
        Assert.Contains("iterates its items", folder.Text);
        Publish(page);

        Assert.NotNull(sent);
        Assert.Empty(Validated().Errors);
    }

    // ---- the item panes' readers ---------------------------------------------------------

    [Fact]
    public void TheSnippetItemPane_ListsItsDirectReaders_AndRestsOtherwise()
    {
        var page = RenderEditor();
        Select(page, "snippets/sections/hpi.md");
        Assert.Contains("Not read directly by any template", page.Find(".folder-item-readers__empty").TextContent);

        Select(page, "node:assessment");
        Pick(page, Assessment, "{{ snippet.sections.hpi.content }}");
        Select(page, "snippets/sections/hpi.md");

        var readers = Rows(page, "Readers of sections/hpi");
        var reader = Assert.Single(readers);
        Assert.Equal(("Node 'assessment' prompt", "snippet.sections.hpi.content"), (reader.Reader, reader.Member));
        Assert.Contains("reads it directly", reader.Text);
    }

    [Fact]
    public void TheMacroItemPane_ListsItsReaders_InlineAndThroughTheFolder()
    {
        var page = RenderEditor();
        Select(page, "macros/notes/hpi.md");

        var fromFixture = Rows(page, "Readers of notes/hpi");
        var iterating = Assert.Single(fromFixture);
        Assert.Equal(("Node 'assessment' prompt", "macro.notes"), (iterating.Reader, iterating.Member));
        Assert.Contains("iterates the folder", iterating.Text);

        Select(page, "node:assessment");
        Pick(page, Assessment, "{{ macro.notes.hpi.prompt }}");
        Select(page, "macros/notes/hpi.md");

        var readers = Rows(page, "Readers of notes/hpi");
        Assert.Equal(new[] { "macro.notes.hpi.prompt", "macro.notes" }, readers.Select(r => r.Member));
        Assert.Contains("inline", readers[0].Text);
        // The item pane's own palette finds the item's walk (a macro text keyed notes/hpi) — its reads line renders (none: the body reads nothing).
        Assert.Equal("Reads: nothing yet", page.Find(".template-reads").TextContent.Trim());
    }

    // ---- below 26 the library refuses the spelling -----------------------------------------

    [Fact]
    public void Below26_ADirectRead_IsRefusedByTheLibrary()
    {
        CapturePublish();
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Select(page, Assessment);

        var area = page.Find(".editor-pane fluent-text-area:not(.macro-instruction)");
        area.Change(area.GetAttribute("current-value") + "\n{{ snippet.sections.hpi.content }}");
        Publish(page);

        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("reads snippet.sections.hpi; a folder is iterated: {{ for s in snippet.sections }}.", StringComparison.Ordinal));
    }

    // ---- the preview's label members ------------------------------------------------------

    [Fact]
    public void TheSectionNodesPreview_RendersItemLabel_At26()
    {
        var page = RenderEditor();
        Select(page, "node:section_notes");

        var preview = page.Find(".pane-preview pre.rendered-preview__text");
        Assert.DoesNotContain("editor-warning", preview.ClassName);
        Assert.StartsWith("⟨item.label⟩: ⟨item.content⟩", preview.TextContent);

        // Previewed as an item: the header's label and the item's text.
        page.Find("fluent-select.rendered-preview__item-pick").Change("hpi");
        preview = page.Find(".pane-preview pre.rendered-preview__text");
        Assert.DoesNotContain("editor-warning", preview.ClassName);
        Assert.StartsWith("HPI: History:", preview.TextContent);
    }
}
