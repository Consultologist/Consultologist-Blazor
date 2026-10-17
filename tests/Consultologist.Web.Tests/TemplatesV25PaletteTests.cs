using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C4 (#1010, design §§ 1, 13): the insert-reference palette on the prompt, snippet and
/// macro panes — the objects a template may read, a renderer pipe for a node's text, appended
/// to the text (cursor insertion is the code editor rung's); the Variables form, the plain-text
/// toggle and the slot hint are ≤24. Inert on the live editor until A5.
/// </summary>
public class TemplatesV25PaletteTests : ClientRenderTestContext
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

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static IReadOnlyList<string?> Choices(IRenderedComponent<Templates> page, string file) =>
        page.Find($"fluent-select[aria-label='Reference to insert into {file}']").QuerySelectorAll("fluent-option")
            .Where(o => o.GetAttribute("disabled") == null)
            .Select(o => o.GetAttribute("value")).ToList();

    private static string EditorText(IRenderedComponent<Templates> page) =>
        page.Find(".editor-pane fluent-text-area:not(.macro-instruction)").GetAttribute("current-value") ?? string.Empty;

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    private const string Assessment = "prompts/assessment.md";

    [Fact]
    public void ThePromptPane_At25_HasThePalette_AndNoneOfThe24Controls()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");

        Assert.NotNull(page.Find("details.insert-reference"));
        Assert.Empty(page.FindAll(".prompt-variables"));
        Assert.Empty(page.FindAll(".prompt-raw-row"));
        Assert.Empty(page.FindAll("input[aria-label='New variable name']"));
        Assert.DoesNotContain("[[slot:", page.Find(".editor-pane").TextContent);
    }

    [Fact]
    public void Below25_ThePromptPane_KeepsItsVariablesForm_AndHasNoPalette()
    {
        var page = RenderEditor(EditorFixtures.V12());
        Navigate(page, "draft-section");

        Assert.Empty(page.FindAll("details.insert-reference"));
        Assert.NotNull(page.Find(".prompt-variables"));
    }

    [Fact]
    public void ThePalette_OffersEveryObject_InOrder_ButNeverTheReadingNodeItself()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");

        var choices = Choices(page, Assessment);
        Assert.Contains("{{ input.notes }}", choices);
        Assert.Contains("{{ input.kind }}", choices);
        Assert.Contains("{{ node.extract.content }}", choices);
        Assert.Contains("{{ node.extract.value }}", choices);
        Assert.Contains("{{ for c in node.extract.value.concepts }}\n- {{ c }}\n{{ end }}", choices);
        // The package-local format's schema: medications is an array field.
        Assert.Contains("{{ for c in node.meds.value.medications }}\n- {{ c }}\n{{ end }}", choices);
        // The classifier's enum value.
        Assert.Contains("{{ node.scope.value }}", choices);
        Assert.Contains("{{ snippet.standards.content }}", choices);
        Assert.Contains("{{ for s in snippet.sections }}\n{{ s.content }}\n{{ end }}", choices);
        Assert.Contains("{{ macro.closing.content }}", choices);
        Assert.Contains("{{ macro.closing.prompt }}", choices);
        Assert.Contains("{{ run.date }}", choices);
        Assert.Contains("{{ profile.name }}", choices);
        // Not itself, and no item on a node that does not fan.
        Assert.DoesNotContain("{{ node.assessment.content }}", choices);
        Assert.DoesNotContain(choices, c => c!.StartsWith("{{ item.", StringComparison.Ordinal));

        // In the objects' order: inputs, nodes, snippets, macros, run, profile.
        var order = new[] { "{{ input.notes }}", "{{ node.extract.content }}", "{{ snippet.standards.content }}", "{{ macro.closing.content }}", "{{ run.date }}", "{{ profile.name }}" }
            .Select(v => choices.ToList().IndexOf(v)).ToList();
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void AFanningNodesPrompt_OffersTheItemMembers()
    {
        var page = RenderEditor();
        Navigate(page, "section");

        var choices = Choices(page, "prompts/section.md");
        Assert.Contains("{{ item.content }}", choices);
        Assert.Contains("{{ item.name }}", choices);
    }

    [Fact]
    public void InsertingANodeText_WithARenderer_AppendsThePipedRead_AndTheRegistryAccepts()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "assessment");
        var before = EditorText(page);

        page.Find($"fluent-select[aria-label='Reference to insert into {Assessment}']").Change("{{ node.extract.content }}");
        page.Find("fluent-select[aria-label='Renderer for the reference']").Change("concept_context");
        page.Find($"fluent-button[aria-label='Insert reference into {Assessment}']").Click();

        Assert.Equal(before + "\n{{ node.extract.content | concept_context }}", EditorText(page));
        Assert.Contains("Reads:", page.Find(".template-reads").TextContent);
        // The palette resets after an insert.
        Assert.Equal("", page.Find($"fluent-select[aria-label='Reference to insert into {Assessment}']").GetAttribute("current-value") ?? "");

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.NotNull(sent);
        Assert.EndsWith("{{ node.extract.content | concept_context }}", sent!.Files[Assessment]);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void InsertingALoop_AppendsTheForBlock()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");

        page.Find($"fluent-select[aria-label='Reference to insert into {Assessment}']").Change("{{ for s in snippet.sections }}\n{{ s.content }}\n{{ end }}");
        Assert.Empty(page.FindAll("fluent-select[aria-label='Renderer for the reference']"));
        page.Find($"fluent-button[aria-label='Insert reference into {Assessment}']").Click();

        Assert.EndsWith("\n{{ for s in snippet.sections }}\n{{ s.content }}\n{{ end }}", EditorText(page));
    }

    [Fact]
    public void TheMacroPane_HasThePalette_ButNeverAnInlineRead()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        var choices = Choices(page, "macros/closing.md");
        Assert.Contains("{{ macro.tool_note.content }}", choices);
        Assert.DoesNotContain("{{ macro.tool_note.prompt }}", choices);
        Assert.DoesNotContain("{{ macro.closing.content }}", choices);
        Assert.Contains("{{ profile.name }}", choices);
    }

    [Fact]
    public void TheSnippetAndItemPanes_HaveThePalette()
    {
        var page = RenderEditor();

        Navigate(page, "standards");
        var snippetChoices = Choices(page, "snippets/standards.md");
        Assert.Contains("{{ input.notes }}", snippetChoices);
        Assert.DoesNotContain("{{ snippet.standards.content }}", snippetChoices);

        Navigate(page, "HPI");
        Assert.Contains("{{ macro.tool_note.prompt }}", Choices(page, "snippets/sections/hpi.md"));
    }

    [Fact]
    public void TheReadsLine_NamesTheTemplatesReads_AndItsWarning()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");

        Assert.Equal(
            "Reads: input.intake, input.notes, node.extract.value, node.meds.value, snippet.standards.content, macro.tool_note.prompt (inline)",
            page.Find(".template-reads").TextContent.Trim());

        page.Find(".editor-pane fluent-text-area").Change("{{ node.extract.content }} {{ snippet.standards.content }} {{ macro.tool_note.prompt }}");
        Assert.Contains("the JSON renders as text — read node.extract.value for the fields.", page.Find(".node-reads__warning").TextContent);
    }
}
