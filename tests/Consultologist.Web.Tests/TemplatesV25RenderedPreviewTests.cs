using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const string ItemSelect = "fluent-select[aria-label='Preview as item of snippet.sections']";
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

    /// <summary>The V25 fixture with section_notes fanning an array input instead of the folder (its folder-item macro dropped).</summary>
    private static WorkflowPackageContentResponse V25WithAnInputFan()
    {
        var package = EditorFixtures.V25();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["inputs"]!.AsArray().Add(new JsonObject { ["id"] = "priors", ["label"] = "Priors", ["required"] = false, ["type"] = "array", ["items"] = "text" });
        var sectionNotes = root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "section_notes")!.AsObject();
        sectionNotes["forEach"] = "input.priors";
        sectionNotes.Remove("macros");
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    private static IReadOnlyList<string> OptionValues(AngleSharp.Dom.IElement select) =>
        select.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value") ?? "").ToList();

    private static IReadOnlyList<string> OptionTexts(AngleSharp.Dom.IElement select) =>
        select.QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()).ToList();

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

    // ----- #1109: preview as item -----

    [Fact]
    public void TheItemPick_OffersTheFoldersItems_InNavOrder_MarkingGatedOnes()
    {
        var page = RenderEditor();
        Navigate(page, "section");

        var select = page.Find(ItemSelect);
        Assert.Equal(new[] { "", "hpi", "plan" }, OptionValues(select));
        Assert.Equal(new[] { "— unchosen —", "HPI", "Plan — when input.kind == \"new\"" }, OptionTexts(select));
        Assert.Equal("⟨item.name⟩: ⟨item.content⟩", Rendered(page));
    }

    [Fact]
    public void ChoosingAnItem_RendersItForReal()
    {
        var page = RenderEditor();
        Navigate(page, "section");

        page.Find(ItemSelect).Change("hpi");
        Assert.Equal("HPI: History: ⟨input.notes⟩", Rendered(page));

        page.Find(ItemSelect).Change("plan");
        Assert.Equal("Plan: Plan.", Rendered(page));

        page.Find(ItemSelect).Change("");
        Assert.Equal("⟨item.name⟩: ⟨item.content⟩", Rendered(page));
    }

    [Fact]
    public void AnItemPick_SurvivesAPaneSwitch_AndShowsOnTheNodePane()
    {
        var page = RenderEditor();
        Navigate(page, "section");
        var savesBefore = DraftSaves();

        page.Find(ItemSelect).Change("hpi");

        Assert.DoesNotContain("●", page.Find("button.editor-nav__item--selected").TextContent);
        Assert.Equal(savesBefore, DraftSaves());

        ShowNode(page, "section_notes");
        var section = PromptTextSection(page);
        Assert.Equal("HPI: History: ⟨input.notes⟩", section.QuerySelector("pre.rendered-preview__text")!.TextContent);
        var select = section.QuerySelector(ItemSelect)!;
        Assert.Equal("hpi", select.GetAttribute("current-value") ?? select.GetAttribute("value"));
    }

    [Fact]
    public void ARenamedItem_IsOfferedByItsNewName()
    {
        var page = RenderEditor();
        Navigate(page, "HPI");
        page.Find("input[aria-label='Name for snippet hpi']").Change("History");

        Navigate(page, "section");

        Assert.Contains("History", OptionTexts(page.Find(ItemSelect)));
        page.Find(ItemSelect).Change("hpi");
        Assert.Equal("History: History: ⟨input.notes⟩", Rendered(page));
    }

    [Fact]
    public void APendingItem_IsOffered_AfterThePublishedOnes()
    {
        var page = RenderEditor();
        Navigate(page, "+ Add item");
        AddItem(page, "discharge", "Discharge", "Discharge plan: {{ input.notes }}");

        Navigate(page, "section");

        Assert.Equal(new[] { "", "hpi", "plan", "discharge" }, OptionValues(page.Find(ItemSelect)));
        page.Find(ItemSelect).Change("discharge");
        Assert.Equal("Discharge: Discharge plan: ⟨input.notes⟩", Rendered(page));
    }

    [Fact]
    public void AnUnfannedPrompt_OffersNoItemPick()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");

        Assert.Empty(page.FindAll(ItemSelect));
        Assert.Empty(page.FindAll(".rendered-preview__item-pick"));
    }

    [Fact]
    public void AnInputFan_OffersNoItemPick()
    {
        var page = RenderEditor(V25WithAnInputFan());
        Navigate(page, "section");

        Assert.Empty(page.FindAll(".rendered-preview__item-pick"));
        Assert.Equal("⟨item.name⟩: ⟨item.content⟩", Rendered(page));
    }

    [Fact]
    public void WithoutTheCatalogue_ThePreviewStillRenders_WithoutAControlRow()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", GatedAssessment), withCatalogue: false);
        Navigate(page, "assessment");

        Assert.Empty(page.FindAll(KindSelect));
        Assert.StartsWith("FOLLOW", Rendered(page));
    }

    // ----- #1123: picks beyond enums -----

    private const string Urgent = "fluent-select[aria-label='Preview with input.urgent']";
    private const string Visits = "input[aria-label='Preview with input.visits']";
    private const string Scope = "fluent-select[aria-label='Preview with node.scope.value']";
    private const string Notes = "input[aria-label='Preview with input.notes']";
    private const string TypedAssessment =
        "{{ if input.urgent }}URGENT{{ else }}ROUTINE{{ end }}|{{ if input.visits > 2 }}MANY{{ else }}FEW{{ end }}|{{ case node.scope.value }}{{ when \"in_scope\" }}IN{{ else }}OUT{{ end }}|{{ if input.notes == \"none\" }}NONE{{ else }}SOME{{ end }}\n{{ snippet.standards.content }}";

    /// <summary>The V25 package with a top-level boolean and number input, and the assessment prompt testing every typed value.</summary>
    private static WorkflowPackageContentResponse V25WithTypedInputs(string assessment = TypedAssessment)
    {
        var package = WithFile("prompts/assessment.md", assessment);
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        var inputs = root["inputs"]!.AsArray();
        inputs.Add(new JsonObject { ["id"] = "urgent", ["label"] = "Urgent", ["required"] = false, ["type"] = "boolean" });
        inputs.Add(new JsonObject { ["id"] = "visits", ["label"] = "Visits", ["required"] = false, ["type"] = "number" });
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    private static IReadOnlyList<string?> ControlLabels(IRenderedComponent<Templates> page) =>
        page.FindAll(".rendered-preview__controls [aria-label^='Preview with ']").Select(c => c.GetAttribute("aria-label")).ToList();

    [Fact]
    public void TheControls_AppearForBooleans_Numbers_ComparedText_AndNodeValues_InIdOrder()
    {
        var page = RenderEditor(V25WithTypedInputs());
        Navigate(page, "assessment");

        Assert.Equal(
            new[] { "Preview with input.notes", "Preview with input.urgent", "Preview with input.visits", "Preview with node.scope.value" },
            ControlLabels(page));
        Assert.Equal(new[] { "", "true", "false" }, OptionValues(page.Find(Urgent)));
        Assert.Equal(new[] { "", "in_scope", "out_of_scope" }, OptionValues(page.Find(Scope)));
        Assert.Empty(page.FindAll(KindSelect));
        Assert.NotEmpty(page.FindAll(".rendered-preview__note"));
        // Unchosen, the tokens stand: a bare if sees a non-empty text, an ordering compares the text, an equality is false.
        Assert.StartsWith("URGENT|MANY|OUT|SOME", Rendered(page));
    }

    [Fact]
    public void ABooleanPick_RendersThatBranch()
    {
        var page = RenderEditor(V25WithTypedInputs());
        Navigate(page, "assessment");

        page.Find(Urgent).Change("false");
        Assert.StartsWith("ROUTINE|", Rendered(page));

        page.Find(Urgent).Change("true");
        Assert.StartsWith("URGENT|", Rendered(page));

        // Unchosen again: the token, which a bare if reads as true.
        page.Find(Urgent).Change("");
        Assert.StartsWith("URGENT|", Rendered(page));
        Assert.NotEmpty(page.FindAll(".rendered-preview__note"));
    }

    [Fact]
    public void ANumberPick_SatisfiesTheComparison_AndANonNumberAppliesNothing()
    {
        var page = RenderEditor(V25WithTypedInputs());
        Navigate(page, "assessment");

        page.Find(Visits).Change("1");
        Assert.Contains("|FEW|", Rendered(page));

        page.Find(Visits).Change("3");
        Assert.Contains("|MANY|", Rendered(page));

        // A non-number is stale — the token stands (an ordering against the text holds) and nothing errors.
        page.Find(Visits).Change("x");
        Assert.Contains("|MANY|", Rendered(page));
        Assert.DoesNotContain("editor-warning", page.Find("pre.rendered-preview__text").ClassName);

        page.Find(Visits).Change("");
        Assert.Contains("|MANY|", Rendered(page));
    }

    [Fact]
    public void ANodeValuePick_RendersTheCaseBranch()
    {
        var page = RenderEditor(V25WithTypedInputs());
        Navigate(page, "assessment");

        page.Find(Scope).Change("in_scope");
        Assert.Contains("|IN|", Rendered(page));

        page.Find(Scope).Change("out_of_scope");
        Assert.Contains("|OUT|", Rendered(page));

        page.Find(Scope).Change("");
        Assert.Contains("|OUT|", Rendered(page));
    }

    [Fact]
    public void AComparedTextPick_RendersThatBranch_AndAnUncomparedTextInput_GetsNoField()
    {
        var page = RenderEditor(V25WithTypedInputs());
        Navigate(page, "assessment");

        page.Find(Notes).Change("none");
        Assert.EndsWith("|NONE\nFollow the local standards.", Rendered(page).TrimEnd());

        page.Find(Notes).Change("x");
        Assert.Contains("|SOME", Rendered(page));

        // The fixture's assessment reads input.notes but never compares it: no field.
        var plain = RenderEditor();
        Navigate(plain, "assessment");
        Assert.Empty(plain.FindAll(Notes));
    }

    [Fact]
    public void AFieldOfAnObjectInput_StaysItsToken()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", "{{ if input.intake.on_letrozole }}Y{{ else }}N{{ end }}\n{{ snippet.standards.content }}"));
        Navigate(page, "assessment");

        // A token is a non-empty string, which Scriban reads as true: the field stays unpreviewable by design.
        Assert.Empty(page.FindAll("[aria-label='Preview with input.intake.on_letrozole']"));
        Assert.Empty(page.FindAll("[aria-label='Preview with input.intake']"));
        Assert.Empty(page.FindAll(".rendered-preview__controls"));
    }

    [Fact]
    public void TypedPicks_AreNotPending_SurviveAPaneSwitch_AndShowOnTheNodePane()
    {
        var page = RenderEditor(V25WithTypedInputs());
        Navigate(page, "assessment");
        var savesBefore = DraftSaves();

        page.Find(Urgent).Change("true");
        page.Find(Scope).Change("in_scope");
        // visits stays unchosen: its token, which the ordering reads as MANY.

        Assert.StartsWith("URGENT|MANY|IN|", Rendered(page));
        Assert.DoesNotContain("●", page.Find("button.editor-nav__item--selected").TextContent);
        Assert.Equal(savesBefore, DraftSaves());

        Navigate(page, "standards");
        Navigate(page, "assessment");
        Assert.StartsWith("URGENT|MANY|IN|", Rendered(page));

        ShowNode(page, "assessment");
        Assert.StartsWith("URGENT|MANY|IN|", PromptTextSection(page).QuerySelector("pre.rendered-preview__text")!.TextContent);
    }

    // ----- #1124: as Markdown -----

    private const string Bytes = "fluent-button[aria-label='Preview bytes']";
    private const string AsMarkdown = "fluent-button[aria-label='Preview as Markdown']";
    private const string MarkdownView = ".pane-preview .rendered-preview__markdown";
    private const string HeadedStandards = "# Standards\n\nFollow {{ input.notes }} closely.";

    [Fact]
    public void TheDefault_IsBytes_WithTheToggleBesideTheLabel()
    {
        var page = RenderEditor(WithFile("snippets/standards.md", HeadedStandards));
        Navigate(page, "standards");

        Assert.Equal("# Standards\n\nFollow ⟨input.notes⟩ closely.", Rendered(page));
        Assert.Equal("true", page.Find(Bytes).GetAttribute("aria-pressed"));
        Assert.Equal("false", page.Find(AsMarkdown).GetAttribute("aria-pressed"));
        Assert.Equal("Rendered preview of Snippet 'standards' as", page.Find(".rendered-preview__mode").GetAttribute("aria-label"));
        Assert.Empty(page.FindAll(MarkdownView));
    }

    [Fact]
    public void AsMarkdown_RendersTheSameString_AsMarkdown_WithTokensAsCode_AndIsNotPending()
    {
        var page = RenderEditor(WithFile("snippets/standards.md", HeadedStandards));
        Navigate(page, "standards");
        var savesBefore = DraftSaves();

        page.Find(AsMarkdown).Click();

        var view = page.Find(MarkdownView);
        Assert.Equal("Standards", view.QuerySelector("h1")!.TextContent.Trim());
        Assert.Equal("⟨input.notes⟩", view.QuerySelector("code.rendered-preview__token")!.TextContent);
        Assert.Contains("Follow", view.QuerySelector("p")!.TextContent);
        Assert.Empty(page.FindAll(".pane-preview pre.rendered-preview__text"));
        Assert.Equal("true", page.Find(AsMarkdown).GetAttribute("aria-pressed"));
        Assert.Equal("false", page.Find(Bytes).GetAttribute("aria-pressed"));
        // The bytes underneath are untouched: the text, the draft and the dot say so.
        Assert.Equal(HeadedStandards, page.Find(".pane-editor fluent-text-area").GetAttribute("current-value"));
        Assert.Equal(savesBefore, DraftSaves());
        Assert.DoesNotContain("●", page.Find("button.editor-nav__item--selected").TextContent);

        page.Find(Bytes).Click();
        Assert.Equal("# Standards\n\nFollow ⟨input.notes⟩ closely.", Rendered(page));
    }

    [Fact]
    public void TheToggle_IsSharedAcrossPanes_AndTheNodePane()
    {
        var page = RenderEditor(WithFile("snippets/standards.md", HeadedStandards));
        Navigate(page, "standards");
        page.Find(AsMarkdown).Click();

        Navigate(page, "assessment");
        Assert.NotEmpty(page.FindAll(MarkdownView));

        ShowNode(page, "assessment");
        Assert.NotNull(PromptTextSection(page).QuerySelector(".rendered-preview__markdown"));

        PromptTextSection(page).QuerySelector(Bytes)!.Click();
        Navigate(page, "standards");
        Assert.NotEmpty(page.FindAll(".pane-preview pre.rendered-preview__text"));
        Assert.Empty(page.FindAll(MarkdownView));
    }

    [Fact]
    public void AnErrorPreview_StaysBytes_AndHidesTheToggle()
    {
        var page = RenderEditor(WithFile("snippets/standards.md", HeadedStandards));
        Navigate(page, "standards");
        page.Find(AsMarkdown).Click();

        page.Find(".editor-pane fluent-text-area").Change("{{ if }}");

        Assert.Empty(page.FindAll(".rendered-preview__mode"));
        Assert.Empty(page.FindAll(MarkdownView));
        Assert.Contains("editor-warning", page.Find(".pane-preview pre.rendered-preview__text").ClassName);
    }

    [Fact]
    public void ARawPrompt_IsUntouchedByTheToggle()
    {
        var page = RenderEditor(EditorFixtures.V15Raw());
        Navigate(page, "disclaimer");
        page.Find(".prompt-raw input[type=checkbox]").Change(true);

        Assert.Contains("Plain text (verbatim)", PreviewHalf(page));
        Assert.Empty(page.FindAll(".rendered-preview__mode"));
    }
}
