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
        Assert.Contains("Reads", page.Find(".template-reads").TextContent);
        // #1058: the pipe types the read — no "the JSON renders as text" warning under it.
        Assert.Empty(page.FindAll(".node-reads__warning"));
        // The palette resets after an insert.
        Assert.Equal("", page.Find($"fluent-select[aria-label='Reference to insert into {Assessment}']").GetAttribute("current-value") ?? "");

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.NotNull(sent);
        Assert.EndsWith("{{ node.extract.content | concept_context }}", sent!.Files[Assessment]);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void ARenderer_IsOfferedOnlyForAConceptListNodesText()
    {
        // #1058: the validator refuses a renderer pipe on any other format, so the
        // palette offers the renderer select only where it can apply.
        var page = RenderEditor();
        Navigate(page, "assessment");

        page.Find($"fluent-select[aria-label='Reference to insert into {Assessment}']").Change("{{ node.scope.content }}");
        Assert.Empty(page.FindAll("fluent-select[aria-label='Renderer for the reference']"));

        page.Find($"fluent-select[aria-label='Reference to insert into {Assessment}']").Change("{{ node.extract.content }}");
        Assert.Single(page.FindAll("fluent-select[aria-label='Renderer for the reference']"));
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
    public void TheReadsRows_NameTheTemplatesReads_GroupedByKind_AndItsWarning()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");

        // #1086: the comma-joined line is rows — macros, snippets, inputs, nodes — the thing read by its label, the member as the text spells it.
        Assert.Equal("Reads", page.Find(".template-reads").TextContent.Trim());
        var rows = page.Find(".template-read-rows[aria-label='Reads of prompts/assessment.md']").QuerySelectorAll(".template-read-row").ToList();
        Assert.Equal(
            new[] { "Macro 'Tool note'", "Snippet 'standards'", "Input 'Intake form'", "Input 'Referral notes'", "Node 'Extract concepts'", "Node 'Medications'" },
            rows.Select(row => row.GetAttribute("data-read-reader")));
        Assert.Equal(
            new[] { "macro.tool_note.prompt", "snippet.standards.content", "input.intake", "input.notes", "node.extract.value", "node.meds.value" },
            rows.Select(row => row.GetAttribute("data-read-member")));
        Assert.Contains("(inline)", rows[0].TextContent);
        Assert.All(rows, row => Assert.Equal("fluent-button", row.QuerySelector(".template-read-row__reader")!.TagName.ToLowerInvariant()));

        rows[0].QuerySelector(".template-read-row__reader")!.Click();
        Assert.Equal("macros/tool_note.md", EditorSession.SelectedKey);

        Navigate(page, "assessment");
        page.Find(".editor-pane fluent-text-area").Change("{{ node.extract.content }} {{ snippet.standards.content }} {{ macro.tool_note.prompt }}");
        Assert.Contains("the JSON renders as text — read node.extract.value for the fields.", page.Find(".node-reads__warning").TextContent);
        Assert.Contains("node.extract.content", page.Find(".template-read-rows[aria-label='Reads of prompts/assessment.md']").QuerySelectorAll(".template-read-row").Select(row => row.GetAttribute("data-read-member")));
    }

    [Fact]
    public void AFactRead_IsARow_WithoutALink()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        var row = Assert.Single(page.Find(".template-read-rows[aria-label='Reads of macros/closing.md']").QuerySelectorAll(".template-read-row"));
        Assert.Equal("Profile", row.GetAttribute("data-read-reader"));
        Assert.Equal("profile.name", row.GetAttribute("data-read-member"));
        Assert.NotNull(row.QuerySelector("span.template-read-row__reader"));
    }

    [Fact]
    public void ATextReadingNothing_KeepsTheRestingLine()
    {
        var page = RenderEditor();
        Navigate(page, "standards");

        Assert.Equal("Reads: nothing yet", page.Find(".template-reads").TextContent.Trim());
        Assert.Empty(page.FindAll(".template-read-rows[aria-label='Reads of snippets/standards.md']"));
    }

    // ----- #1086: Read by on the prompt pane -----

    private static IReadOnlyList<string?> PromptReaders(IRenderedComponent<Templates> page, string promptId) =>
        page.Find($".template-read-rows[aria-label='Readers of {promptId}']").QuerySelectorAll(".template-read-row").Select(row => row.GetAttribute("data-read-reader")).ToList();

    /// <summary>The fixture with a second node running the assessment prompt.</summary>
    private static WorkflowPackageContentResponse V25WithASecondAssessmentNode()
    {
        var package = EditorFixtures.V25();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "assessment_2", ["label"] = "Assessment again", ["prompt"] = "assessment", ["format"] = "prose" });
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    [Fact]
    public void ThePromptPane_SaysWhichNodeRunsIt_AsARow()
    {
        var page = RenderEditor();
        Navigate(page, "assessment");

        Assert.Contains("Read by", page.Find(".editor-pane").TextContent);
        Assert.Equal(new[] { "Node 'Assessment'" }, PromptReaders(page, "assessment"));
        var row = page.Find(".template-read-rows[aria-label='Readers of assessment'] .template-read-row");
        Assert.Contains("runs this prompt", row.TextContent);
        Assert.DoesNotContain("for each item", row.TextContent);
        Assert.Null(row.QuerySelector(".template-read-row__member"));
        Assert.Empty(page.FindAll(".prompt-readers__empty"));

        row.QuerySelector(".template-read-row__reader")!.Click();
        Assert.Equal("node:assessment", EditorSession.SelectedKey);
    }

    [Fact]
    public void AFannedPrompt_SaysTheFan()
    {
        var page = RenderEditor();
        Navigate(page, "section");

        Assert.Equal(new[] { "Node 'Section notes'" }, PromptReaders(page, "section"));
        Assert.Contains("runs this prompt for each item of snippet.sections", page.Find(".template-read-rows[aria-label='Readers of section']").TextContent);
    }

    [Fact]
    public void APromptRunByTwoNodes_ListsBoth()
    {
        var page = RenderEditor(V25WithASecondAssessmentNode());
        Navigate(page, "assessment");

        Assert.Equal(new[] { "Node 'Assessment'", "Node 'Assessment again'" }, PromptReaders(page, "assessment"));
    }

    [Fact]
    public void AnOrphanPrompt_SaysReadByNothingYet()
    {
        var page = RenderEditor();
        Navigate(page, "+ Prompt");
        page.Find("fluent-text-field[placeholder='summarize-item']").Change("footer_text");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create prompt")).Click();
        Navigate(page, "footer_text");

        Assert.Contains("Read by nothing yet", page.Find(".prompt-readers__empty").TextContent);
        Assert.Empty(page.FindAll(".template-read-rows[aria-label='Readers of footer_text']"));
    }
}
