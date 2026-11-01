using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1263: the v26 design's § 2.6 mock-up rows the macro pane never built — "(preview)" on the prompt
/// block, "· and the macro's if" under every instance row (macro pane, node pane, Deliverables card),
/// the loop an instance sits in spelled where the 25 forItem select was (the pick was lost on publish
/// at 26), the case sentence for an item.id-only gate, and on Read-by rows "· items:" for a read that
/// reaches one item, "the macro's if:" and never-fires against the read's guard.
/// </summary>
public class TemplatesV26MacroPaneRowsTests : ClientRenderTestContext
{
    private const string ClosingIf = "input.kind == \"new\"";

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

    private static IReadOnlyList<string> Texts(IRenderedComponent<Templates> page, string selector) =>
        page.FindAll(selector).Select(e => e.TextContent.Trim()).ToList();

    /// <summary>V26Gated with its files and the section_notes instance gate rewritten.</summary>
    private static WorkflowPackageContentResponse Gated(Action<Dictionary<string, string>>? files = null, string? toolNoteIf = null)
    {
        var gated = EditorFixtures.V26Gated();
        var root = JsonNode.Parse(gated.Manifest.GetRawText())!.AsObject();
        if (toolNoteIf is not null)
        {
            var sectionNotes = root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "section_notes")!;
            sectionNotes["macros"]![0]!["if"] = toolNoteIf;
        }

        var copy = new Dictionary<string, string>(gated.Files, StringComparer.Ordinal);
        files?.Invoke(copy);
        return new(gated.Name, gated.Version, 26, JsonDocument.Parse(root.ToJsonString()).RootElement.Clone(), copy);
    }

    // ---- row 1 --------------------------------------------------------------------------

    [Fact]
    public void ThePromptBlock_SaysItIsAPreview()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, "macros/closing.md");

        Assert.Contains("What the model is told (preview)", page.Find(".macro-prompt-preview").TextContent);
    }

    // ---- row 2 --------------------------------------------------------------------------

    [Fact]
    public void EveryInstanceOfAGatedMacro_SaysTheMacrosIf_OnTheMacroPane_TheNodePane_AndTheDeliverablesCard()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        var line = $"· and the macro's if: {ClosingIf}";

        Select(page, "macros/closing.md");
        Assert.Equal(new[] { line, line }, Texts(page, "[data-instance-note='macro-if']"));

        Select(page, "node:assessment");
        Assert.Equal(new[] { line }, Texts(page, "[data-instance-note='macro-if']"));

        Select(page, "results:consult");
        Assert.Equal(new[] { line }, Texts(page, "[data-instance-note='macro-if']"));
    }

    [Fact]
    public void AnUngatedMacrosInstances_SayNothingOfAnIf()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, "macros/tool_note.md");

        Assert.Empty(page.FindAll("[data-instance-note='macro-if']"));
        Assert.Empty(page.FindAll("[data-read-note='macro-if']"));
    }

    // ---- row 3 --------------------------------------------------------------------------

    [Fact]
    public void At26_AnInstanceOnALoopsBody_SpellsItsLoop_AndOffersNoForItemSelect()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());

        Select(page, "macros/tool_note.md");
        Assert.Empty(page.FindAll(".node-macro-foritem"));
        Assert.Equal(new[] { "for item in snippet.sections" }, Texts(page, "[data-instance-loop='sections']"));

        Select(page, "node:section_notes");
        Assert.Empty(page.FindAll(".node-macro-foritem"));
        Assert.Equal(new[] { "for item in snippet.sections" }, Texts(page, "[data-instance-loop='sections']"));

        // An instance on a node in no loop spells nothing.
        Select(page, "node:assessment");
        Assert.Empty(page.FindAll("[data-instance-loop]"));
    }

    [Fact]
    public void At25_TheForItemSelectStays_AndNoLoopIsSpelled()
    {
        var page = RenderEditor(EditorFixtures.V25());
        Select(page, "macros/tool_note.md");

        Assert.Single(page.FindAll(".node-macro-foritem"));
        Assert.Empty(page.FindAll("[data-instance-loop]"));
    }

    // ---- row 4 --------------------------------------------------------------------------

    [Fact]
    public void AnInstanceGatedOnSeveralItems_ReadsAsTheCase_OnTheMacroPane_AndTheNodePane()
    {
        var page = RenderEditor(Gated(toolNoteIf: "item.id == \"hpi\" || item.id == \"plan\""));
        const string sentence = "case item.id — when HPI, Plan — else skipped";

        Select(page, "macros/tool_note.md");
        Assert.Equal(new[] { sentence }, Texts(page, "[data-instance-case]"));

        Select(page, "node:section_notes");
        Assert.Equal(new[] { sentence }, Texts(page, "[data-instance-case]"));
    }

    [Theory]
    [InlineData("item.id == \"hpi\"")]
    [InlineData("item.id == \"hpi\" || input.kind == \"new\"")]
    [InlineData("item.id == \"hpi\" && item.id == \"plan\"")]
    public void AnyOtherGateShape_ReadsAsNoCase(string gate)
    {
        var page = RenderEditor(Gated(toolNoteIf: gate));
        Select(page, "macros/tool_note.md");

        Assert.Empty(page.FindAll("[data-instance-case]"));
    }

    // ---- rows 5, 6, 7 -------------------------------------------------------------------

    [Fact]
    public void AReadInsideOneFolderItem_NamesTheItem_AndAReadInTheLoopsPrompt_SaysEveryItem()
    {
        var page = RenderEditor(Gated(files => files["snippets/sections/hpi.md"] += "\n{{ macro.tool_note.prompt }}"));
        Select(page, "macros/tool_note.md");

        var itemRow = page.Find("[data-read-reader=\"Snippet 'sections/hpi'\"]");
        Assert.Equal("· items: HPI", itemRow.QuerySelector("[data-read-items]")!.TextContent.Trim());

        var promptRow = page.Find("[data-read-reader=\"Node 'section_notes' prompt\"]");
        Assert.Equal("· items: every item (2)", promptRow.QuerySelector("[data-read-items]")!.TextContent.Trim());
    }

    [Fact]
    public void AReadOutsideEveryLoop_RunsOnce_AndNamesNoItems()
    {
        // assessment is in no loop; its prompt reads macro.closing.content (V26Gated).
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, "macros/closing.md");

        var row = page.Find("[data-read-reader=\"Node 'assessment' prompt\"]");
        Assert.Null(row.QuerySelector("[data-read-items]"));
    }

    [Fact]
    public void AGatedMacrosReadRows_SayTheMacrosIf()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        Select(page, "macros/closing.md");

        var lines = Texts(page, "[data-read-note='macro-if']");
        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.Equal($"· the macro's if: {ClosingIf}", l));
    }

    [Fact]
    public void AReadWhoseGuardContradictsTheMacrosIf_NeverFires_AndACompatibleGuardSaysNothing()
    {
        var contradicting = RenderEditor(Gated(files => files["prompts/assessment.md"] += "\n{{ if input.kind == \"follow_up\" }}{{ macro.closing.prompt }}{{ end }}"));
        Select(contradicting, "macros/closing.md");
        Assert.Equal(
            new[] { "never fires: the macro's if (input.kind == \"new\") and this read's guard (input.kind == \"follow_up\") cannot both hold" },
            Texts(contradicting, "[data-read-note='never-fires']"));

        var compatible = RenderEditor(Gated(files => files["prompts/assessment.md"] += "\n{{ if input.kind == \"new\" }}{{ macro.closing.prompt }}{{ end }}"));
        Select(compatible, "macros/closing.md");
        Assert.Empty(compatible.FindAll("[data-read-note='never-fires']"));
    }
}
