using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v26 (#1163, design § 5 and § 12): the label, instruction, optional, default and if of a
/// snippet, folder item or macro are edited where they were — their write target is the
/// file's JSON header, composed ahead of the body; the manifest's macros map and a folder's
/// index list carry id and file alone. Rendered through the #1209 seam; the 25 panes are
/// pinned by the ≤25 suites.
/// </summary>
public class TemplatesV26HeaderPaneTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static AngleSharp.Dom.IElement NavRow(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item").First(button => button.TextContent.Replace("●", string.Empty).Trim() == label);

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

    private static JsonElement Macros(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("macros");

    private static string Header(string label, string? @if = null, string? instruction = null, bool? optional = null, bool? @default = null) =>
        PF.WorkflowTextHeaders.Compose(new PF.WorkflowTextHeader(label, @if, instruction, optional, @default));

    private static string PaneText(IRenderedComponent<Templates> page) =>
        page.Find(".pane-editor fluent-text-area").GetAttribute("current-value") ?? string.Empty;

    private static void Discard(IRenderedComponent<Templates> page)
    {
        page.Find(".pane-discard-button").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Confirm discard")).Click();
    }

    private const string ClosingInstruction = "Add a closing line if the plan changed.";

    /// <summary>Appends a read to the assessment node's prompt (its other reads kept, so the package stays valid).</summary>
    private void AppendToAssessment(IRenderedComponent<Templates> page, string read)
    {
        Select(page, "node:assessment");
        var area = page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text").QuerySelector("fluent-text-area")!;
        area.Change(area.GetAttribute("current-value") + read);
    }

    // ---- the macro pane ------------------------------------------------------------

    [Fact]
    public void TheMacroPane_ReadsTheHeader_AndARelabelWritesIt_NotTheManifest()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "macros/closing.md");

        Assert.Equal("Closing", page.Find("input[aria-label='Label for macro closing']").GetAttribute("value"));
        Assert.Equal(ClosingInstruction, page.Find("fluent-text-area[aria-label='Instruction for macro closing']").GetAttribute("current-value"));
        // The body, not the file: the header never shows in the editor.
        Assert.Equal("Closing: {{ profile.name }}.", PaneText(page));

        page.Find("input[aria-label='Label for macro closing']").Change("Closing line");
        Assert.Contains("●", NavRow(page, "closing").TextContent);
        Assert.Contains("1 header", page.Markup);
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(Header("Closing line", instruction: ClosingInstruction) + "Closing: {{ profile.name }}.", sent!.Files["macros/closing.md"]);
        var macros = Macros(sent);
        Assert.Equal(JsonValueKind.Object, macros.ValueKind);
        Assert.Equal("macros/closing.md", macros.GetProperty("closing").GetString());
        // An untouched headed file passes through byte-identical.
        Assert.Equal(EditorFixtures.V26().Files["macros/tool_note.md"], sent.Files["macros/tool_note.md"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void TheMacroPanesDiscard_RevertsTheHeader()
    {
        var page = RenderEditor();
        Select(page, "macros/closing.md");
        page.Find("input[aria-label='Label for macro closing']").Change("Closing line");
        Assert.Equal("Closing line", page.Find("input[aria-label='Label for macro closing']").GetAttribute("value"));

        Discard(page);

        Assert.Equal("Closing", page.Find("input[aria-label='Label for macro closing']").GetAttribute("value"));
        Assert.DoesNotContain("●", NavRow(page, "closing").TextContent);
    }

    [Fact]
    public void TheInstructionAndTheText_ComposeTheHeaderAndTheBody()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "macros/closing.md");

        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("Close the note.");
        page.Find(".pane-editor fluent-text-area").Change("Closing: {{ profile.name }}. Thank you.");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(Header("Closing", instruction: "Close the note.") + "Closing: {{ profile.name }}. Thank you.", sent!.Files["macros/closing.md"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void TheOptionalPair_IsBackAt26_WritesTheHeader_AndOptionalAloneIsRefused()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "macros/closing.md");

        page.Find("input[aria-label='Optional for macro closing']").Change(true);
        Publish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Macro 'closing' is optional and declares no default"));

        page.Find("fluent-select[aria-label='Default for macro closing']").Change("true");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(Header("Closing", instruction: ClosingInstruction, optional: true, @default: true) + "Closing: {{ profile.name }}.", sent!.Files["macros/closing.md"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void TheMacrosOwnIf_RestsAlways_AndWritesTheHeader()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "macros/closing.md");

        var gate = page.Find(".macro-if");
        Assert.Equal("Always", gate.QuerySelector(".condition-field__resting")!.TextContent.Trim());
        gate.QuerySelector(".condition-field__resting")!.Click();
        page.Find(".macro-if fluent-select[aria-label='Condition operand for macro closing']").Change("kind");
        page.Find(".macro-if fluent-select[aria-label='Condition value for macro closing']").Change("new");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(Header("Closing", @if: "input.kind == \"new\"", instruction: ClosingInstruction) + "Closing: {{ profile.name }}.", sent!.Files["macros/closing.md"]);
        Assert.Empty(Validated().Errors);
    }

    // ---- the standalone snippet pane ---------------------------------------------------

    [Fact]
    public void TheSnippetPane_ReadsTheHeader_AndWritesLabelAndIf()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "snippets/standards.md");

        Assert.Equal("Standards", page.Find("input[aria-label='Label for snippet standards']").GetAttribute("value"));
        Assert.Equal("Follow the local standards.", PaneText(page));

        page.Find("input[aria-label='Label for snippet standards']").Change("House standards");
        page.Find(".condition-field__resting").Click();
        page.Find("fluent-select[aria-label='Condition operand for snippet standards']").Change("kind");
        page.Find("fluent-select[aria-label='Condition value for snippet standards']").Change("new");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(Header("House standards", @if: "input.kind == \"new\"") + "Follow the local standards.", sent!.Files["snippets/standards.md"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void PlusSnippet_BirthsAHeadedFile_ItsLabelFromTheId()
    {
        CapturePublish();
        var page = RenderEditor();
        Navigate(page, "+ Snippet");
        page.Find(".new-item-fields fluent-text-field").Change("note_type");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create snippet")).Click();

        Assert.Equal("Note type", page.Find("input[aria-label='Label for snippet note_type']").GetAttribute("value"));
        page.Find(".pane-editor fluent-text-area").Change("A consult.");
        // Read somewhere, so the package stays valid.
        AppendToAssessment(page, "\n{{ snippet.note_type.content }}");
        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        Assert.Equal(Header("Note type") + "A consult.", sent!.Files["snippets/note_type.md"]);
        Assert.Empty(Validated().Errors);
    }

    // ---- folder items ------------------------------------------------------------------

    [Fact]
    public void AFolderItem_WritesLabelAndIfToItsHeader_NotTheIndex()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "snippets/sections/hpi.md");

        Assert.Equal("label", page.Find(".node-field span").TextContent.Trim());
        page.Find("input[aria-label='Name for snippet hpi']").Change("HPI section");
        Assert.Equal("Always", page.Find(".condition-field__resting").TextContent.Trim());
        page.Find(".condition-field__resting").Click();
        page.Find("fluent-select[aria-label='Condition operand for sections item hpi']").Change("kind");
        page.Find("fluent-select[aria-label='Condition value for sections item hpi']").Change("new");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(Header("HPI section", @if: "input.kind == \"new\"") + "History: {{ input.notes }}", sent!.Files["snippets/sections/hpi.md"]);
        Assert.Equal(EditorFixtures.V26().Files["snippets/sections/index.json"], sent.Files["snippets/sections/index.json"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void Reordering_WritesTheListIndex()
    {
        CapturePublish();
        var page = RenderEditor();
        page.Find("fluent-button[aria-label='Move item Plan up']").Click();
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal("[\n  {\n    \"id\": \"plan\",\n    \"file\": \"plan.md\"\n  },\n  {\n    \"id\": \"hpi\",\n    \"file\": \"hpi.md\"\n  }\n]\n", sent!.Files["snippets/sections/index.json"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void PlusAddItem_BirthsAHeadedFile_AndJoinsTheList()
    {
        CapturePublish();
        var page = RenderEditor();
        Navigate(page, "+ Add item");
        var fields = page.FindAll(".new-item-fields fluent-text-field");
        fields[0].Change("ros");
        fields[1].Change("Review of systems");
        page.Find(".pane-editor fluent-text-area").Change("Systems: {{ input.notes }}");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Add to")).Click();
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(Header("Review of systems") + "Systems: {{ input.notes }}", sent!.Files["snippets/sections/ros.md"]);
        Assert.EndsWith("  {\n    \"id\": \"ros\",\n    \"file\": \"ros.md\"\n  }\n]\n", sent.Files["snippets/sections/index.json"]);
        Assert.DoesNotContain("\"name\"", sent.Files["snippets/sections/index.json"]);
        Assert.Empty(Validated().Errors);
    }

    // ---- macro folders -----------------------------------------------------------------

    [Fact]
    public void AMacroFolder_SitsInTheMacrosNav_WithItemRowsAndArrows_AndItsItemPaneWritesTheHeader()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V26Macros());

        var rows = page.FindAll("[data-folder-item][data-kind='macro']");
        Assert.Equal(new[] { "hpi", "plan" }, rows.Select(r => r.GetAttribute("data-folder-item")));
        Assert.True(page.Find("fluent-button[aria-label='Move item HPI notes up']").HasAttribute("disabled"));
        Assert.Empty(page.FindAll("[data-folder-item][data-kind='snippet']").Where(r => r.GetAttribute("data-folder-item") == "plan" && r.ParentElement!.TextContent.Contains("notes")));

        Navigate(page, "Plan notes");
        Assert.Equal("Note the plan.", page.Find("fluent-text-area[aria-label='Instruction for macro item notes/plan']").GetAttribute("current-value"));
        Assert.Equal("Plan notes.", PaneText(page));
        page.Find("fluent-text-area[aria-label='Instruction for macro item notes/plan']").Change("Note the plan briefly.");
        page.Find("input[aria-label='Name for snippet plan']").Change("Plan note");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(Header("Plan note", @if: "input.kind == \"new\"", instruction: "Note the plan briefly.") + "Plan notes.", sent!.Files["macros/notes/plan.md"]);
        Assert.Equal(EditorFixtures.V26Macros().Files["macros/notes/index.json"], sent.Files["macros/notes/index.json"]);
        Assert.Equal("macros/notes/", Macros(sent).GetProperty("notes").GetString());
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void PlusMacroFolder_WritesTheMap_TheListIndex_AndHeadedItems()
    {
        CapturePublish();
        var page = RenderEditor();
        Navigate(page, "+ Macro folder");
        page.Find(".new-item-fields fluent-text-field").Change("extras");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro folder")).Click();

        // Landed on the folder's "+ Add item" pane.
        Assert.Contains("New extras item", page.Find(".editor-pane").TextContent);
        var fields = page.FindAll(".new-item-fields fluent-text-field");
        fields[0].Change("caveat");
        fields[1].Change("Caveat");
        page.Find(".pane-editor fluent-text-area").Change("A caveat.");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Add to")).Click();
        page.Find("fluent-text-area[aria-label='Instruction for macro item extras/caveat']").Change("Add the caveat.");
        // Read somewhere, so the package stays valid.
        AppendToAssessment(page, "\n{{ for m in macro.extras }}{{ m.content }}{{ end }}");
        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        Assert.Equal("macros/extras/", Macros(sent!).GetProperty("extras").GetString());
        Assert.Equal("[\n  {\n    \"id\": \"caveat\",\n    \"file\": \"caveat.md\"\n  }\n]\n", sent.Files["macros/extras/index.json"]);
        Assert.Equal(Header("Caveat", instruction: "Add the caveat.") + "A caveat.", sent.Files["macros/extras/caveat.md"]);
        Assert.Empty(Validated().Errors);
    }

    // ---- a refused header ---------------------------------------------------------------

    [Fact]
    public void ARefusedHeader_ShowsTheFileWhole_WithTheLibrarysSentence_AndFixingItReturnsTheFields()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V26RefusedHeader());
        Select(page, "snippets/standards.md");

        Assert.Contains(page.FindAll(".header-refused li").Select(li => li.TextContent.Trim()),
            s => s.StartsWith("snippets/standards.md header declares 'name', which is not a header key", StringComparison.Ordinal));
        Assert.Empty(page.FindAll("input[aria-label='Label for snippet standards']"));
        Assert.StartsWith("{\n  \"name\"", PaneText(page));

        page.Find(".pane-editor fluent-text-area").Change(Header("Standards") + "Follow the local standards.");

        Assert.Empty(page.FindAll(".header-refused"));
        Assert.Equal("Standards", page.Find("input[aria-label='Label for snippet standards']").GetAttribute("value"));
        Assert.Equal("Follow the local standards.", PaneText(page));
        page.Find("input[aria-label='Label for snippet standards']").Change("House standards");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(Header("House standards") + "Follow the local standards.", sent!.Files["snippets/standards.md"]);
        Assert.Empty(Validated().Errors);
    }

    // ---- the draft -----------------------------------------------------------------------

    [Fact]
    public void AHeaderEdit_SurvivesAReload_WithItsDraft()
    {
        var page = RenderEditor();
        Select(page, "macros/closing.md");
        page.Find("input[aria-label='Label for macro closing']").Change("Closing line");

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = RenderEditor();
        Select(again, "macros/closing.md");

        Assert.Equal("Closing line", again.Find("input[aria-label='Label for macro closing']").GetAttribute("value"));
        Assert.Contains("1 header", again.Markup);
    }
}
