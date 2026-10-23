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

    // #1086: the node pane's Reads section is the shared descriptive rows — the members read, in order.
    private static IReadOnlyList<string> Reads(IRenderedComponent<Templates> page, string nodeId) =>
        page.Find($".template-read-rows[aria-label='Reads of {nodeId}']").QuerySelectorAll(".template-read-row").Select(row => row.GetAttribute("data-read-member") ?? "").ToList();

    private static void ClickRead(IRenderedComponent<Templates> page, string nodeId, string member) =>
        page.Find($".template-read-rows[aria-label='Reads of {nodeId}'] .template-read-row[data-read-member='{member}'] .template-read-row__reader").Click();

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

        // #1086: grouped by kind — macros, snippets, inputs, nodes — the thing read by its label.
        Assert.Equal(
            new[] { "macro.tool_note.prompt", "snippet.standards.content", "input.intake", "input.notes", "node.extract.value", "node.meds.value" },
            Reads(page, "assessment"));
        Assert.Equal(
            new[] { "Macro 'Tool note'", "Snippet 'standards'", "Input 'Intake form'", "Input 'Referral notes'", "Node 'Extract concepts'", "Node 'Medications'" },
            page.Find(".template-read-rows[aria-label='Reads of assessment']").QuerySelectorAll(".template-read-row").Select(row => row.GetAttribute("data-read-reader")));
        Assert.Contains("(inline)", page.Find(".template-read-rows[aria-label='Reads of assessment'] .template-read-row[data-read-member='macro.tool_note.prompt']").TextContent);
    }

    [Fact]
    public void AReadRow_OpensTheThingsPane()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");

        ClickRead(page, "assessment", "node.extract.value");
        Assert.Equal("Extract concepts", Selected(page));

        ShowNode(page, "assessment");
        ClickRead(page, "assessment", "snippet.standards.content");
        Assert.Equal("standards", Selected(page));

        ShowNode(page, "assessment");
        ClickRead(page, "assessment", "input.notes");
        Assert.Equal("Inputs", Selected(page));
    }

    [Fact]
    public void AFanNode_NamesTheItemMembersItReads_WithoutALink()
    {
        var page = RenderEditor();
        ShowNode(page, "section_notes");

        var rows = page.Find(".template-read-rows[aria-label='Reads of section_notes']").QuerySelectorAll(".template-read-row").ToList();
        Assert.Equal(new[] { "item.content", "item.name" }, rows.Select(row => row.GetAttribute("data-read-member")));
        Assert.Equal(new[] { "Fan item", "Fan item" }, rows.Select(row => row.GetAttribute("data-read-reader")));
        Assert.All(rows, row => Assert.NotNull(row.QuerySelector("span.template-read-row__reader")));
    }

    [Fact]
    public void AComputedNode_ListsItsParameterReads()
    {
        var page = RenderEditor();
        ShowNode(page, "coverage");

        Assert.Contains("Reads", Heads(page));
        Assert.Equal(new[] { "node.extract.value" }, Reads(page, "coverage"));
        Assert.Contains("a parameter", page.Find(".template-read-rows[aria-label='Reads of coverage']").TextContent);
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
        page.Find(".editor-pane fluent-text-area").Change("Assess.\n{{ snippet.standards.content }}\n{{ macro.tool_note.prompt }}\n{{ node.extract.content }}\n{{ for m in node.meds.value.medications }}- {{ m.name }}\n{{ end }}");
        ShowNode(page, "assessment");

        var warning = page.Find(".node-reads__warning").TextContent.Trim();
        Assert.StartsWith("Prompt 'assessment' reads node.extract.content of format 'concept_list'", warning);
        Assert.EndsWith("the JSON renders as text — read node.extract.value for the fields.", warning);
        Assert.Contains("node.extract.content", Reads(page, "assessment"));

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.NotNull(sent);
    }

    [Fact]
    public void AnUnresolvedReference_IsSaidEarly_OnTheReadsList()
    {
        var page = RenderEditor(WithFile("prompts/assessment.md", "Assess {{ node.nope.content }} and {{ snippet.standards.content }}"));
        ShowNode(page, "assessment");

        Assert.Equal("1 problem in this text — Publish says which.", page.Find(".node-reads__unresolved").TextContent.Trim());
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
        var readers = page.Find(".template-read-rows[aria-label='Readers of tool_note']");
        Assert.Contains("Prompt 'assess'", readers.TextContent);
        readers.QuerySelector(".template-read-row__reader")!.Click();

        Assert.Equal("assess", Selected(page));
    }

    /// <summary>#1122: the walker's warning on a literal the enum never answers rides the existing warning surface — nothing editor-side.</summary>
    [Fact]
    public void AnUndeclaredLiteral_ShowsTheWalkersWarning_UnderTheReads_AndOnTheNodePane_AndDoesNotBlockPublish()
    {
        WorkflowPackagePublishRequest? sent = null;
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(r => sent = r))
            .Returns(new WorkflowPublishOutcome(new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"), Array.Empty<string>()));
        var page = RenderEditor();
        Navigate(page, "assessment");
        const string sentence = "Prompt 'assessment' compares input.kind to 'bogus', which it does not declare (values: new, follow_up); that branch never runs.";

        page.Find(".editor-pane fluent-text-area").Change(EditorFixtures.V25().Files["prompts/assessment.md"] + "\n{{ if input.kind == \"bogus\" }}never{{ end }}");

        Assert.Equal(sentence, page.Find(".node-reads__warning").TextContent.Trim());

        ShowNode(page, "assessment");
        Assert.Equal(sentence, page.Find(".node-reads__warning").TextContent.Trim());

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.NotNull(sent);
    }
}
