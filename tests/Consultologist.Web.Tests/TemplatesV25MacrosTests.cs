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
/// v25 C3 (#1009, design §§ 5, 7, 13): macros at 25 — the macro pane's instruction,
/// Instances (node and deliverable rows) and Read at; the node pane's one Macros
/// section with its derived inline macros; the Deliverables pane's prepend/append; the
/// desk in the registry's words. Rendered at 25 alone; the four ≤24 macro suites stay
/// as the parity proof. Inert on the live editor until A5.
/// </summary>
public class TemplatesV25MacrosTests : ClientRenderTestContext
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

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private static IReadOnlyList<string?> OptionValues(IRenderedComponent<Templates> page, string selector) =>
        page.Find(selector).QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();

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

    private static JsonElement Manifest(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement;

    private static JsonElement Node(JsonElement manifest, string id) =>
        manifest.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == id);

    private static JsonElement Macro(JsonElement manifest, string id) =>
        manifest.GetProperty("macros").EnumerateArray().Single(m => m.GetProperty("id").GetString() == id);

    private static JsonElement Deliverable(JsonElement manifest) =>
        manifest.GetProperty("deliverables").EnumerateArray().Single();

    // ----- the macro pane: instruction -----

    [Fact]
    public void TheMacroPane_ShowsTheInstruction_AndItsEditRoundTrips()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "closing");

        var field = page.Find("fluent-text-area[aria-label='Instruction for macro closing']");
        Assert.Equal("Add a closing line if the plan changed.", field.GetAttribute("current-value"));

        field.Change("Add a closing line whenever the plan changed.");
        Assert.Contains("1 macro instruction", page.Markup);
        Publish(page);

        Assert.Equal("Add a closing line whenever the plan changed.", Macro(Manifest(sent!), "closing").GetProperty("instruction").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void ANewMacro_IsBornWithABlankInstruction_WrittenAsTyped()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "+ Macro");
        Assert.Contains("carries an instruction", page.Find(".editor-pane").TextContent);
        page.Find("fluent-text-field[placeholder='closing_paragraph']").Change("signature_line");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro")).Click();

        var field = page.Find("fluent-text-area[aria-label='Instruction for macro signature_line']");
        Assert.Equal("", field.GetAttribute("current-value") ?? "");
        field.Change("Sign off when the note is complete.");
        page.Find(".editor-pane fluent-text-area:not(.macro-instruction)").Change("Signed, {{ profile.name }}.");

        // Place it, or the registry refuses an unused macro: attach to the deliverable later; here a node.
        page.Find("fluent-select[aria-label='Attach macro signature_line to a node']").Change("assessment");
        Publish(page);

        var declared = Macro(Manifest(sent!), "signature_line");
        Assert.Equal("Sign off when the note is complete.", declared.GetProperty("instruction").GetString());
        Assert.Equal("macros/signature_line.md", declared.GetProperty("file").GetString());
    }

    [Fact]
    public void TheInstruction_HasThePalette_ThatAppendsIntoIt_AndNeverOffersTheMacrosOwnMembers()
    {
        // #1044: the instruction is a template — the palette inserts into it; the
        // macro's own .content and .prompt are not offered (its text is what the inline
        // read fills, its inline read is itself), another macro's are.
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "closing");

        var options = OptionValues(page, "fluent-select[aria-label='Reference to insert into instruction of macro closing']");
        Assert.Contains("{{ input.kind }}", options);
        Assert.Contains("{{ macro.tool_note.prompt }}", options);
        Assert.DoesNotContain("{{ macro.closing.prompt }}", options);
        Assert.DoesNotContain("{{ macro.closing.content }}", options);

        page.Find("fluent-select[aria-label='Reference to insert into instruction of macro closing']").Change("{{ input.kind }}");
        page.Find("fluent-button[aria-label='Insert reference into instruction of macro closing']").Click();

        Assert.Equal("Add a closing line if the plan changed.\n{{ input.kind }}", page.Find("fluent-text-area[aria-label='Instruction for macro closing']").GetAttribute("value"));
        Publish(page);
        Assert.NotNull(sent);
        Assert.Equal("Add a closing line if the plan changed.\n{{ input.kind }}", Macro(Manifest(sent!), "closing").GetProperty("instruction").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void TheInstruction_SurvivesAReload_WithItsDraft()
    {
        var page = RenderEditor();
        Navigate(page, "closing");
        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("Edited instruction.");

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = Render<Templates>();

        Assert.Equal("Edited instruction.", again.Find("fluent-text-area[aria-label='Instruction for macro closing']").GetAttribute("current-value"));
    }

    [Fact]
    public void DiscardingTheMacroPane_DropsTheInstructionEdit()
    {
        var page = RenderEditor();
        Navigate(page, "closing");
        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("Edited instruction.");

        page.Find(".pane-discard-button").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Confirm discard")).Click();

        Assert.Equal("Add a closing line if the plan changed.", page.Find("fluent-text-area[aria-label='Instruction for macro closing']").GetAttribute("current-value"));
    }

    // ----- the macro pane: Instances -----

    [Fact]
    public void TheMacroPane_ListsItsInstances_AsRows_PickersFirst()
    {
        // #1077 (reversing #1072's chips): Placed on is the attach pickers first,
        // then one editable row per instance — the owner's row (placement, fan item,
        // remove, the condition builder) led by a link to the owner; no chips.
        var page = RenderEditor();
        Navigate(page, "closing");

        Assert.Empty(page.FindAll(".macro-attachments"));
        Assert.Empty(page.FindAll(".macro-instances"));
        var where = page.Find(".macro-where");
        Assert.Contains("Placed on", where.TextContent);
        Assert.Empty(where.QuerySelectorAll("ul.macro-placed"));
        Assert.DoesNotContain("Not placed anywhere", where.TextContent);
        Assert.Empty(where.QuerySelectorAll(".macro-where__empty"));

        // The pickers precede the rows.
        var markup = where.OuterHtml;
        Assert.True(markup.IndexOf("Attach macro closing to a node", StringComparison.Ordinal) < markup.IndexOf("macro-instance", StringComparison.Ordinal));

        var onNode = where.QuerySelector("[data-instance-node='assessment']")!;
        Assert.Equal("Assessment", onNode.QuerySelector(".macro-instance__owner")!.TextContent.Trim());
        Assert.Contains("node 'assessment'", onNode.QuerySelector(".macro-instance__owner")!.GetAttribute("title"));
        Assert.Equal("after", onNode.QuerySelector("fluent-select[aria-label='Placement for macro closing on assessment']")!.GetAttribute("current-value"));
        Assert.Equal(new[] { "before", "after" }, OptionValues(page, "fluent-select[aria-label='Placement for macro closing on assessment']"));
        Assert.Contains("after the node's output", onNode.TextContent);
        Assert.DoesNotContain("this node's output", onNode.TextContent);
        Assert.NotNull(onNode.QuerySelector("fluent-button[aria-label='Remove macro closing from assessment']"));
        Assert.NotNull(onNode.QuerySelector("fluent-button[aria-label='Add a condition for assessment macro closing']"));
        // No reorder on the macro pane — the order belongs to the node's list.
        Assert.Empty(onNode.QuerySelectorAll(".node-macro-up"));

        var onDeliverable = where.QuerySelector("[data-instance-deliverable='consult']")!;
        Assert.Equal("Consultation note", onDeliverable.QuerySelector(".macro-instance__owner")!.TextContent.Trim());
        var at = onDeliverable.QuerySelector("fluent-select[aria-label='Placement for macro closing in deliverable consult']")!;
        Assert.Equal("prepend", at.GetAttribute("current-value"));
        Assert.Equal(new[] { "prepend", "append" }, OptionValues(page, "fluent-select[aria-label='Placement for macro closing in deliverable consult']"));
        Assert.NotNull(onDeliverable.QuerySelector("fluent-button[aria-label='Detach macro closing from deliverable consult']"));
        // The loaded gate shows its builder.
        Assert.Equal("kind", onDeliverable.QuerySelector("fluent-select[aria-label^='Condition operand for consult macro closing']")!.GetAttribute("current-value"));

        // No ≤24 controls: no slot, no sources, no optional/default, no placeholder tokens.
        Assert.Empty(page.FindAll(".result-macro-foritem"));
        Assert.Empty(page.FindAll(".macro-optional"));
        Assert.Empty(page.FindAll(".macro-placeholders"));
        Assert.DoesNotContain("slot (the model places it)", page.Markup);
    }

    [Fact]
    public void AFanItemInstance_ShowsItsItem_OnTheMacroPane()
    {
        var page = RenderEditor();
        Navigate(page, "tool_note");

        var onNode = page.Find(".macro-where [data-instance-node='section_notes']");
        Assert.Equal("before", onNode.QuerySelector("fluent-select[aria-label='Placement for macro tool_note on section_notes']")!.GetAttribute("current-value"));
        Assert.Equal("hpi", onNode.QuerySelector("fluent-select[aria-label='Fan item for macro tool_note on section_notes']")!.GetAttribute("current-value"));
        // assessment reads tool_note inline but carries no instance of it: no row.
        Assert.Empty(page.FindAll(".macro-where [data-instance-node='assessment']"));
    }

    [Fact]
    public void ANodeRowsLink_OpensTheNodePane()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        page.Find(".macro-where [data-instance-node='assessment'] .macro-instance__owner").Click();

        Assert.Equal("Assessment", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void ADeliverableRowsLink_OpensTheDeliverablesPane()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        page.Find(".macro-where [data-instance-deliverable='consult'] .macro-instance__owner").Click();

        Assert.Equal("Deliverables", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void EditingAnInstance_OnTheMacroPane_WritesTheManifest()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "closing");

        page.Find(".macro-where fluent-select[aria-label='Placement for macro closing on assessment']").Change("before");
        page.Find(".macro-where fluent-select[aria-label='Placement for macro closing in deliverable consult']").Change("append");
        Publish(page);

        var manifest = Manifest(sent!);
        // Bare = before: the format's own default, written as the string form.
        var onNode = Node(manifest, "assessment").GetProperty("macros")[0];
        Assert.Equal(JsonValueKind.String, onNode.ValueKind);
        Assert.Equal("closing", onNode.GetString());
        var consult = manifest.GetProperty("deliverables").EnumerateArray().First(d => d.GetProperty("id").GetString() == "consult");
        Assert.Equal("append", consult.GetProperty("macros")[0].GetProperty("at").GetString());
    }

    [Fact]
    public void RemovingANodeInstance_FromTheMacroPane_LeavesTheNodesList()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "closing");

        page.Find(".macro-where fluent-button[aria-label='Remove macro closing from assessment']").Click();

        Assert.Empty(page.FindAll(".macro-where [data-instance-node='assessment']"));
        Assert.NotNull(page.Find(".macro-where [data-instance-deliverable='consult']"));
        Publish(page);

        Assert.False(Node(Manifest(sent!), "assessment").TryGetProperty("macros", out _));
    }

    [Fact]
    public void AttachingToANode_OffersOnlyFormatsTakingOutputMacros_AndWritesABeforeInstance()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "tool_note");

        var nodes = OptionValues(page, "fluent-select[aria-label='Attach macro tool_note to a node']");
        // prose nodes take output macros; the classifier (enum), the concept_list and the
        // computed check do not; the aggregator never; section_notes already carries it (forItem) but has a free identity.
        Assert.Contains("assessment", nodes);
        Assert.Contains("section_notes", nodes);
        Assert.DoesNotContain("scope", nodes);
        Assert.DoesNotContain("extract", nodes);
        Assert.DoesNotContain("coverage", nodes);
        Assert.DoesNotContain("all", nodes);

        page.Find("fluent-select[aria-label='Attach macro tool_note to a node']").Change("assessment");
        // The row appears at once, before (the format's default).
        var row = page.Find(".macro-where [data-instance-node='assessment']");
        Assert.Equal("before", row.QuerySelector("fluent-select[aria-label='Placement for macro tool_note on assessment']")!.GetAttribute("current-value"));
        Assert.Contains("before the node's output", row.TextContent);
        Publish(page);

        var macros = Node(Manifest(sent!), "assessment").GetProperty("macros").EnumerateArray().ToList();
        Assert.Equal(2, macros.Count);
        // Bare = before: the format's own default, written as the string form.
        Assert.Equal(JsonValueKind.String, macros[1].ValueKind);
        Assert.Equal("tool_note", macros[1].GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void ANodeHoldingBothPlacements_IsNotOfferedAgain()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        // assessment carries closing after; attach it before too — now both identities are taken.
        page.Find("fluent-select[aria-label='Attach macro closing to a node']").Change("assessment");

        Assert.DoesNotContain("assessment", OptionValues(page, "fluent-select[aria-label='Attach macro closing to a node']"));
    }

    [Fact]
    public void RemovingANodeInstance_OnTheNodePane_DropsItsRow()
    {
        var page = RenderEditor();
        CapturePublish();
        ShowNode(page, "assessment");
        page.Find("fluent-button[aria-label='Remove macro closing from assessment']").Click();

        Navigate(page, "closing");
        Assert.Empty(page.FindAll(".macro-where [data-instance-node='assessment']"));
        Publish(page);

        Assert.False(Node(Manifest(sent!), "assessment").TryGetProperty("macros", out _));
    }

    [Fact]
    public void TheRemoveMacroButton_NamesEveryInstanceItTakes()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        // #1073: the button sits in the header; its reach is the tooltip. #1077: the
        // rows under Placed on are the reach — the sentence that named it is gone.
        var remove = page.Find(".pane-header__actions").QuerySelectorAll("fluent-button").First(b => b.TextContent.Contains("Remove macro"));
        Assert.Contains("node 'assessment'", remove.GetAttribute("title"));
        Assert.Contains("deliverable 'consult'", remove.GetAttribute("title"));
        Assert.Empty(page.FindAll(".macro-where__reach"));
    }

    // ----- #1073: the pane's layout -----

    [Fact]
    public void TheMacroPane_IsOrdered_FieldsThenWhereThenText_WithRemoveInTheHeader()
    {
        var page = RenderEditor();
        Navigate(page, "closing");
        var markup = page.Markup;

        int At(string marker) { var i = markup.IndexOf(marker, StringComparison.Ordinal); Assert.True(i >= 0, marker); return i; }
        var instruction = At("aria-label=\"Instruction for macro closing\"");
        var instructionPalette = At("Reference to insert into instruction of macro closing");
        var preview = At("class=\"macro-prompt-preview\"");
        var where = At("class=\"macro-where\"");
        var textPalette = At("Reference to insert into macros/closing.md");
        var editor = At("<span>Macro text</span>");
        var help = At("class=\"pane-help\"");
        Assert.True(instruction < instructionPalette && instructionPalette < preview && preview < where && where < textPalette && textPalette < editor && editor < help,
            $"order: instruction {instruction}, its palette {instructionPalette}, preview {preview}, where {where}, text palette {textPalette}, editor {editor}, help {help}");

        // Remove macro is a header action and nowhere else.
        Assert.Contains(page.Find(".pane-header__actions").QuerySelectorAll("fluent-button"), b => b.TextContent.Contains("Remove macro"));
        Assert.DoesNotContain(page.Find(".editor-pane").QuerySelectorAll(".pane-header__actions ~ * fluent-button"), b => b.TextContent.Contains("Remove macro"));
        Assert.Empty(page.FindAll(".macro-objects"));
    }

    [Fact]
    public void TheInstruction_IsAFullWidthField()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        var field = page.Find("fluent-text-area[aria-label='Instruction for macro closing']").ParentElement!;
        Assert.Equal("label", field.TagName.ToLowerInvariant());
        Assert.Contains("input-field", field.ClassName);
    }

    [Fact]
    public void ThePalettes_NameTheirTargets()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        Assert.Equal(new[] { "+ reference into the instruction", "+ reference into the macro text" },
            page.FindAll("details.insert-reference > summary").Select(s => s.TextContent.Trim()));
    }

    [Fact]
    public void TheHelp_IsOneSentencePerField_AndTheRestInTheExpander()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        Assert.Single(page.FindAll(".macro-text-help"));
        Assert.DoesNotContain("placed text instructs no model", page.Markup);
        var help = page.Find("details.pane-help");
        Assert.Contains("How macros work", help.QuerySelector("summary")!.TextContent);
        Assert.Contains("Read inline", help.TextContent);
        Assert.Contains("mechanics sentence", help.TextContent);
        // The footer renders after the editor split; the snippet pane has none.
        Assert.True(page.Markup.IndexOf("class=\"pane-split\"", StringComparison.Ordinal) < page.Markup.IndexOf("class=\"pane-footer\"", StringComparison.Ordinal));
        Navigate(page, "standards");
        Assert.Empty(page.FindAll(".pane-footer"));
    }

    // ----- the macro pane: Read by (#1085) -----

    private const string ReadRows = ".template-read-rows[aria-label='Readers of tool_note']";

    private static IReadOnlyList<string> Readers(IRenderedComponent<Templates> page, string rows = ReadRows) =>
        page.Find(rows).QuerySelectorAll(".template-read-row").Select(row => row.GetAttribute("data-read-reader") ?? "").ToList();

    private string SelectedKey => Services.GetRequiredService<WorkflowEditorSession>().SelectedKey ?? "";

    /// <summary>The fixture with a second node running the assessment prompt — a prompt walked twice.</summary>
    private static WorkflowPackageContentResponse V25WithASecondAssessmentNode()
    {
        var package = EditorFixtures.V25();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "assessment_2", ["label"] = "Assessment again", ["prompt"] = "assessment", ["format"] = "prose" });
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    [Fact]
    public void TheMacroPane_ListsItsReaders_AsRows_WithTheMember()
    {
        var page = RenderEditor();
        Navigate(page, "tool_note");

        var where = page.Find(".macro-where");
        Assert.Contains("Read by", where.TextContent);
        Assert.DoesNotContain("Read inline by", where.TextContent);
        Assert.DoesNotContain("Read as text by", where.TextContent);
        Assert.Empty(where.QuerySelectorAll("ul.snippet-readers"));

        var row = Assert.Single(page.Find(ReadRows).QuerySelectorAll(".template-read-row"));
        Assert.Equal("Prompt 'assessment'", row.GetAttribute("data-read-reader"));
        Assert.Equal("Prompt 'assessment'", row.QuerySelector(".template-read-row__reader")!.TextContent.Trim());
        Assert.Equal("macro.tool_note.prompt", row.QuerySelector(".template-read-row__member")!.TextContent.Trim());
        Assert.Contains("reads", row.TextContent);
        Assert.Contains("(inline)", row.TextContent);
        Assert.DoesNotContain("the instruction", row.TextContent);

        page.Find($"{ReadRows} .template-read-row__reader").Click();
        Assert.Equal("prompts/assessment.md", SelectedKey);
        Assert.Equal("assessment", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void APlacedOnlyMacro_ShowsNoReadRows_AndNoWarning()
    {
        // closing is placed and read by nothing: "placed only" is a normal state.
        var page = RenderEditor();
        Navigate(page, "closing");

        var where = page.Find(".macro-where");
        Assert.Empty(where.QuerySelectorAll(".template-read-rows"));
        Assert.DoesNotContain("Read by", where.TextContent);
        Assert.Empty(where.QuerySelectorAll(".macro-where__empty"));
        Assert.DoesNotContain("Not read by any template", where.TextContent);
    }

    [Fact]
    public void ATextRead_IsARow_AfterTheInlineOnes()
    {
        var page = RenderEditor();
        Navigate(page, "standards");
        page.Find(".editor-pane fluent-text-area").Change("Follow the local standards. {{ macro.tool_note.content }}");
        Navigate(page, "tool_note");

        Assert.Equal(new[] { "Prompt 'assessment'", "Snippet 'standards'" }, Readers(page));

        var textRow = page.Find($"{ReadRows} .template-read-row[data-read-reader=\"Snippet 'standards'\"]");
        Assert.Equal("macro.tool_note.content", textRow.QuerySelector(".template-read-row__member")!.TextContent.Trim());
        Assert.DoesNotContain("(inline)", textRow.TextContent);

        textRow.QuerySelector(".template-read-row__reader")!.Click();
        Assert.Equal("snippets/standards.md", SelectedKey);
    }

    [Fact]
    public void AnInstructionReader_IsLabelledAsTheMacro_WithTheNote()
    {
        var page = RenderEditor();
        Navigate(page, "closing");
        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("Write {{ macro.closing.content }} last.");

        var rows = ".template-read-rows[aria-label='Readers of closing']";
        Assert.Equal(new[] { "Macro 'closing'" }, Readers(page, rows));

        var row = page.Find($"{rows} .template-read-row");
        Assert.Equal("macro.closing.content", row.QuerySelector(".template-read-row__member")!.TextContent.Trim());
        Assert.Contains("the instruction", row.TextContent);
        Assert.Equal("fluent-button", row.QuerySelector(".template-read-row__reader")!.TagName.ToLowerInvariant());

        row.QuerySelector(".template-read-row__reader")!.Click();
        Assert.Equal("macros/closing.md", SelectedKey);
    }

    [Fact]
    public void AFoldersItems_AreOneRowEach_InNavOrder_EachOpeningItsItem()
    {
        var page = RenderEditor();
        Navigate(page, "HPI");
        page.Find(".editor-pane fluent-text-area").Change("History: {{ input.notes }} {{ macro.tool_note.prompt }}");
        Navigate(page, "Plan");
        page.Find(".editor-pane fluent-text-area").Change("Plan. {{ macro.tool_note.prompt }}");
        Navigate(page, "tool_note");

        Assert.Equal(new[] { "Prompt 'assessment'", "Snippet 'sections/hpi'", "Snippet 'sections/plan'" }, Readers(page));
        Assert.All(page.Find(ReadRows).QuerySelectorAll(".template-read-row"), row => Assert.Contains("(inline)", row.TextContent));

        page.Find($"{ReadRows} .template-read-row[data-read-reader=\"Snippet 'sections/hpi'\"] .template-read-row__reader").Click();
        Assert.Equal("snippets/sections/hpi.md", SelectedKey);
        Assert.Equal("HPI", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void APromptRunByTwoNodes_IsOneRow()
    {
        var page = RenderEditor(V25WithASecondAssessmentNode());
        Navigate(page, "tool_note");

        Assert.Equal(new[] { "Prompt 'assessment'" }, Readers(page));
    }

    [Fact]
    public void AMacroPlacedNowhereAndReadNowhere_SaysNotUsedYet()
    {
        var page = RenderEditor();
        Navigate(page, "+ Macro");
        page.Find("fluent-text-field[placeholder='closing_paragraph']").Change("signature_line");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro")).Click();
        Navigate(page, "signature_line");

        Assert.Contains("Not used yet", page.Find(".macro-where .macro-where__empty").TextContent);
    }

    // ----- the node pane: one Macros section -----

    private static IReadOnlyList<string> SectionHeads(IRenderedComponent<Templates> page) =>
        page.FindAll(".node-section__head").Select(h => h.TextContent.Trim()).ToList();

    [Fact]
    public void TheNodePane_HasOneMacrosSection_PlacedAroundTheOutput_AndNoOutputMacrosView()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");

        var heads = SectionHeads(page);
        Assert.Single(heads, h => h == "Macros");
        Assert.DoesNotContain("Output macros", heads);
        Assert.Empty(page.FindAll(".node-output-macro"));

        var row = page.Find(".node-macro-row[data-at='after']");
        Assert.Contains("closing", row.TextContent);
        var placement = row.QuerySelector("fluent-select[aria-label='Placement for macro closing on assessment']")!;
        Assert.Equal("after", placement.GetAttribute("current-value"));
        Assert.Contains("after this node's output", placement.TextContent);
        Assert.DoesNotContain("after the prompt", placement.TextContent);
        Assert.NotNull(row.QuerySelector(".node-macro-up"));
        Assert.NotNull(row.QuerySelector(".node-macro-down"));
    }

    [Fact]
    public void ReorderingWithinAPlacement_RoundTrips_AndTheRegistryAccepts()
    {
        var page = RenderEditor();
        CapturePublish();
        ShowNode(page, "assessment");

        // A second after-block: attached before by default, then moved after, then above closing.
        page.Find("fluent-select[aria-label='Attach a macro to assessment']").Change("tool_note");
        page.Find("fluent-select[aria-label='Placement for macro tool_note on assessment']").Change("after");
        Assert.Equal(new[] { "closing", "tool_note" },
            page.FindAll(".node-macro-row[data-at='after'] .binding-row__source").Select(b => b.TextContent.Trim()));

        page.Find("fluent-button[aria-label='Move macro tool_note up on assessment']").Click();
        Assert.Equal(new[] { "tool_note", "closing" },
            page.FindAll(".node-macro-row[data-at='after'] .binding-row__source").Select(b => b.TextContent.Trim()));
        Publish(page);

        var macros = Node(Manifest(sent!), "assessment").GetProperty("macros").EnumerateArray()
            .Select(m => m.ValueKind == JsonValueKind.String ? m.GetString() : m.GetProperty("id").GetString()).ToList();
        Assert.Equal(new[] { "tool_note", "closing" }, macros);
        // No optional key rides a 25 instance.
        Assert.All(Node(Manifest(sent!), "assessment").GetProperty("macros").EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object),
            m => Assert.False(m.TryGetProperty("optional", out _)));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void TheAttachPicker_SkipsAMacroHoldingBothPlacements()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");

        Assert.Contains("closing", OptionValues(page, "fluent-select[aria-label='Attach a macro to assessment']"));
        page.Find("fluent-select[aria-label='Attach a macro to assessment']").Change("closing");

        Assert.DoesNotContain("closing", OptionValues(page, "fluent-select[aria-label='Attach a macro to assessment']"));
        Assert.Contains("tool_note", OptionValues(page, "fluent-select[aria-label='Attach a macro to assessment']"));
    }

    [Theory]
    [InlineData("scope", "classification")]
    [InlineData("extract", "concept_list")]
    public void ANodeWhoseFormatTakesNoOutputMacros_CannotAttach_AndSaysWhy(string nodeId, string format)
    {
        var page = RenderEditor();
        ShowNode(page, nodeId);

        Assert.Empty(page.FindAll($"fluent-select[aria-label='Attach a macro to {nodeId}']"));
        Assert.Contains($"Format '{format}' takes no output macros; only a text format with outputMacros may.", page.Find(".node-macros-refused").TextContent);
    }

    [Fact]
    public void InlineMacros_AreDerivedFromTheWalk_AndOpenTheMacro()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");

        var slots = page.Find("ul.node-inline-macros[aria-label='Inline macros of assessment']");
        Assert.Equal(new[] { "macro.tool_note.prompt — Tool note" }, slots.QuerySelectorAll("li").Select(li => li.TextContent.Trim()));

        page.Find("ul.node-inline-macros button").Click();
        Assert.Equal("tool_note", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void AFanNode_NamesTheItemsLackingTheInlineRead_InTheValidatorsWords()
    {
        var page = RenderEditor();
        ShowNode(page, "section_notes");
        Assert.Contains("No inline macro", page.Find(".editor-pane").TextContent);

        // The hpi item's text reads the slot; plan's does not.
        Navigate(page, "HPI");
        page.Find(".editor-pane fluent-text-area").Change("History: {{ input.notes }} {{ macro.tool_note.prompt }}");
        ShowNode(page, "section_notes");

        Assert.Contains("macro.tool_note.prompt", page.Find("ul.node-inline-macros[aria-label='Inline macros of section_notes']").TextContent);
        Assert.Equal(
            "Node 'section_notes' fans snippet folder 'sections'; items plan carry no macro.tool_note.prompt read, so nothing tells the model to write macro.tool_note.content for them.",
            page.Find(".node-inline-macros__warning").TextContent.Trim());
    }

    // ----- the Deliverables pane: prepend / append, document scope -----

    private const string DeliverablesPane = "Deliverables";

    [Fact]
    public void TheDeliverablesPane_OffersPrependAndAppendOnly_AndNotesTheNodePlacedInstances()
    {
        var page = RenderEditor();
        Navigate(page, DeliverablesPane);

        var section = page.Find("section.deliverable-section[data-v11-for='consult']");
        Assert.Contains("Macros", section.QuerySelector(".deliverable-section__head")!.TextContent);
        Assert.DoesNotContain("Appended content", section.TextContent);

        var placement = section.QuerySelector("fluent-select[aria-label='Placement for macro closing in deliverable consult']")!;
        Assert.Equal("prepend", placement.GetAttribute("current-value"));
        Assert.Equal(new[] { "prepend", "append" }, placement.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")));
        Assert.Empty(section.QuerySelectorAll(".result-macro-foritem"));
        Assert.DoesNotContain("slot (the model places it)", section.TextContent);

        // #983: the node-placed instances the document composes, each a link to its node.
        var note = section.QuerySelector(".deliverable-node-macros")!;
        Assert.Contains("closing after 'Assessment'", note.TextContent);
        Assert.Contains("tool_note before 'Section notes'", note.TextContent);
        note.QuerySelectorAll("button").First().Click();
        Assert.Equal("Assessment", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void AppendingAMacro_BirthsItUnplaced_ThenAtRoundTrips_UnderDeliverables()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, DeliverablesPane);

        var picker = page.Find("fluent-select[aria-label='Append macro to deliverable consult']");
        var offered = picker.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        // #1080: closing is prepended, so its append identity is still free — offered by name.
        Assert.Contains("closing", offered);
        Assert.Equal("closing — append", picker.QuerySelectorAll("fluent-option").First(o => o.GetAttribute("value") == "closing").TextContent.Trim());
        Assert.Contains("tool_note", offered);
        Assert.Equal("tool_note", picker.QuerySelectorAll("fluent-option").First(o => o.GetAttribute("value") == "tool_note").TextContent.Trim());
        picker.Change("tool_note");

        var placement = page.Find("fluent-select[aria-label='Placement for macro tool_note in deliverable consult']");
        Assert.Equal("", placement.GetAttribute("current-value") ?? "");
        Assert.Contains("choose a placement…", placement.TextContent);
        placement.Change("append");

        page.Find("fluent-button[aria-label='Add a condition for consult macro tool_note']").Click();
        page.Find("fluent-select[aria-label^='Condition operand for consult macro tool_note']").Change("kind");
        page.Find("fluent-select[aria-label^='Condition value for consult macro tool_note']").Change("follow_up");
        Publish(page);

        var manifest = Manifest(sent!);
        Assert.False(manifest.TryGetProperty("results", out _));
        var macros = Deliverable(manifest).GetProperty("macros").EnumerateArray().ToList();
        Assert.Equal(2, macros.Count);
        Assert.Equal("prepend", macros[0].GetProperty("at").GetString());
        Assert.Equal("input.kind == \"new\"", macros[0].GetProperty("when").GetString());
        Assert.Equal("tool_note", macros[1].GetProperty("id").GetString());
        Assert.Equal("append", macros[1].GetProperty("at").GetString());
        Assert.Equal("input.kind == \"follow_up\"", macros[1].GetProperty("when").GetString());
        Assert.False(macros[1].TryGetProperty("before", out _));
        Assert.False(macros[1].TryGetProperty("slot", out _));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    // ----- #1080: the identity at 25 is (id, at) -----

    private static WorkflowPackageContentResponse V25WithClosingPrependedAndAppended() =>
        Reshaped(root => DeliverableObject(root)["macros"] = System.Text.Json.Nodes.JsonNode.Parse(
            """[ { "id": "closing", "at": "prepend", "when": "input.kind == \"new\"" }, { "id": "closing", "at": "append" } ]"""));

    [Fact]
    public void BothIdentities_Load_AsTwoRows_EditedApart()
    {
        var page = RenderEditor(V25WithClosingPrependedAndAppended());
        CapturePublish();
        Navigate(page, DeliverablesPane);

        var rows = page.FindAll("[data-v11-for='consult'] .result-macro");
        Assert.Equal(2, rows.Count);
        Assert.Equal("prepend", page.Find("fluent-select[aria-label='Placement for macro closing in deliverable consult (prepend)']").GetAttribute("current-value"));
        Assert.Equal("append", page.Find("fluent-select[aria-label='Placement for macro closing in deliverable consult (append)']").GetAttribute("current-value"));
        // The prepend row carries the loaded gate; the append row's gate rests.
        Assert.Equal("kind", page.Find("fluent-select[aria-label^='Condition operand for consult macro closing prepend']").GetAttribute("current-value"));
        page.Find("fluent-button[aria-label='Add a condition for consult macro closing append']").Click();
        page.Find("fluent-select[aria-label^='Condition operand for consult macro closing append']").Change("kind");
        page.Find("fluent-select[aria-label^='Condition value for consult macro closing append']").Change("follow_up");
        Publish(page);

        var macros = Deliverable(Manifest(sent!)).GetProperty("macros").EnumerateArray().ToList();
        Assert.Equal(2, macros.Count);
        Assert.Equal("input.kind == \"new\"", macros[0].GetProperty("when").GetString());
        Assert.Equal("append", macros[1].GetProperty("at").GetString());
        Assert.Equal("input.kind == \"follow_up\"", macros[1].GetProperty("when").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void RemovingOneIdentity_LeavesTheOther()
    {
        var page = RenderEditor(V25WithClosingPrependedAndAppended());
        CapturePublish();
        Navigate(page, DeliverablesPane);

        page.Find("fluent-button[aria-label='Remove macro 2']").Click();

        Assert.Single(page.FindAll("[data-v11-for='consult'] .result-macro"));
        Assert.Equal("prepend", page.Find("fluent-select[aria-label='Placement for macro closing in deliverable consult']").GetAttribute("current-value"));
        Publish(page);
        var macros = Deliverable(Manifest(sent!)).GetProperty("macros").EnumerateArray().ToList();
        Assert.Single(macros);
        Assert.Equal("prepend", macros[0].GetProperty("at").GetString());
    }

    [Fact]
    public void AppendingTheSecondIdentity_IsBornAtTheFreePlacement()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, DeliverablesPane);

        page.Find("fluent-select[aria-label='Append macro to deliverable consult']").Change("closing");

        var born = page.Find("fluent-select[aria-label='Placement for macro closing in deliverable consult (append)']");
        Assert.Equal("append", born.GetAttribute("current-value"));
        Assert.DoesNotContain("choose a placement…", born.TextContent);
        // Both identities taken: closing leaves the picker; tool_note stays.
        var offered = OptionValues(page, "fluent-select[aria-label='Append macro to deliverable consult']");
        Assert.DoesNotContain("closing", offered);
        Assert.Contains("tool_note", offered);
        Publish(page);

        var macros = Deliverable(Manifest(sent!)).GetProperty("macros").EnumerateArray().ToList();
        Assert.Equal(2, macros.Count);
        Assert.Equal("closing", macros[1].GetProperty("id").GetString());
        Assert.Equal("append", macros[1].GetProperty("at").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void AnUnplacedEntry_BlocksASecondOfTheSameId()
    {
        var page = RenderEditor();
        Navigate(page, DeliverablesPane);

        page.Find("fluent-select[aria-label='Append macro to deliverable consult']").Change("tool_note");

        // tool_note waits unplaced; a second would be "placed twice ()" — not offered until placed.
        Assert.DoesNotContain("tool_note", OptionValues(page, "fluent-select[aria-label='Append macro to deliverable consult']"));
        page.Find("fluent-select[aria-label='Placement for macro tool_note in deliverable consult']").Change("prepend");
        Assert.Contains("tool_note", OptionValues(page, "fluent-select[aria-label='Append macro to deliverable consult']"));
    }

    [Fact]
    public void TheMacroPane_ShowsOneDeliverableRowPerIdentity()
    {
        var page = RenderEditor(V25WithClosingPrependedAndAppended());
        Navigate(page, "closing");

        var rows = page.FindAll(".macro-where [data-instance-deliverable='consult']");
        Assert.Equal(new[] { "prepend", "append" }, rows.Select(r => r.GetAttribute("data-instance-at")));
        // Both identities taken: the attach picker offers no deliverable.
        Assert.Empty(page.FindAll("fluent-select[aria-label='Attach macro closing to a deliverable']"));

        page.Find("fluent-button[aria-label='Detach macro closing from deliverable consult (append)']").Click();

        var left = page.FindAll(".macro-where [data-instance-deliverable='consult']");
        Assert.Single(left);
        Assert.Equal("prepend", left[0].GetAttribute("data-instance-at"));
        // And the deliverable is offered again, for the placement left free.
        var attach = page.Find("fluent-select[aria-label='Attach macro closing to a deliverable']");
        Assert.Equal("Consultation note — append", attach.QuerySelectorAll("fluent-option").Last().TextContent.Trim());
    }

    [Fact]
    public void ADeliverableInstancesAt_SurvivesAReload_WithItsDraft()
    {
        var page = RenderEditor();
        Navigate(page, DeliverablesPane);
        page.Find("fluent-select[aria-label='Placement for macro closing in deliverable consult']").Change("append");

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = Render<Templates>();

        Assert.Equal("append", again.Find("fluent-select[aria-label='Placement for macro closing in deliverable consult']").GetAttribute("current-value"));
    }

    [Fact]
    public void RemovingALibraryMacro_StripsItsNodeAndDeliverableInstances_At25()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "closing");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Remove macro")).Click();
        Publish(page);

        var manifest = Manifest(sent!);
        Assert.DoesNotContain(manifest.GetProperty("macros").EnumerateArray(), m => m.GetProperty("id").GetString() == "closing");
        Assert.False(Node(manifest, "assessment").TryGetProperty("macros", out _));
        Assert.False(Deliverable(manifest).TryGetProperty("macros", out _));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    // ----- the desk: the registry's macro sentences, word for word -----

    /// <summary>The fixture with its manifest reshaped (and optionally a file replaced).</summary>
    private static WorkflowPackageContentResponse Reshaped(Action<System.Text.Json.Nodes.JsonObject> reshape, params (string Path, string Text)[] files)
    {
        var package = EditorFixtures.V25();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        reshape(root);
        var replaced = new Dictionary<string, string>(package.Files, StringComparer.Ordinal);
        foreach (var (path, text) in files)
        {
            replaced[path] = text;
        }

        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone(), Files = replaced };
    }

    private static System.Text.Json.Nodes.JsonObject NodeObject(System.Text.Json.Nodes.JsonObject root, string id) =>
        root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == id)!.AsObject();

    private static System.Text.Json.Nodes.JsonObject DeliverableObject(System.Text.Json.Nodes.JsonObject root) =>
        root["deliverables"]!.AsArray().First()!.AsObject();

    private static void MakePending(IRenderedComponent<Templates> page)
    {
        Navigate(page, "standards");
        page.Find(".editor-pane fluent-text-area").Change("Follow the local standards, edited.");
    }

    private IReadOnlyList<string> DeskRefusals(WorkflowPackageContentResponse fixture)
    {
        CapturePublish();
        var page = RenderEditor(fixture);
        MakePending(page);
        Publish(page);
        return Refusals(page);
    }

    public static IEnumerable<object[]> DeskCases()
    {
        yield return new object[] { "blank instruction", new Action<System.Text.Json.Nodes.JsonObject>(root => root["macros"]!.AsArray()[0]!["instruction"] = "  "),
            "Macro 'closing' must declare an instruction at specVersion 25; placed text tells the model when to write its text read." };
        yield return new object[] { "aggregator places", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "all")["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ "closing" ]""")),
            "Node 'all' aggregates and must not place macros; place them on the nodes it aggregates or on the deliverable." };
        yield return new object[] { "undeclared on node", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "assessment")["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ "nope" ]""")),
            "Node 'assessment' macro 'nope' is not a declared macro." };
        yield return new object[] { "node at vocabulary", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "assessment")["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "closing", "at": "inside" } ]""")),
            "Node 'assessment' macro 'closing' declares at 'inside'; a node instance is placed 'before' or 'after' the node's output." };
        yield return new object[] { "node placed twice", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "assessment")["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "closing", "at": "after" }, { "id": "closing", "at": "after", "when": "kind == new" } ]""")),
            "Node 'assessment' macro 'closing' is placed twice (after); one identity (id, at, forItem) once — two gates to one text is one instance with when: a or b." };
        yield return new object[] { "format takes none", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "scope")["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ "closing" ]""")),
            "Node 'scope' macro 'closing' is placed before the output, but format 'classification' takes no output macros; only a text format with outputMacros may." };
        yield return new object[] { "forItem without a fan", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "assessment")["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "closing", "at": "after", "forItem": "hpi" } ]""")),
            "Node 'assessment' macro 'closing' anchors to fan item 'hpi', but 'assessment' does not fan a snippet folder." };
        yield return new object[] { "forItem not in the folder", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "section_notes")["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "tool_note", "at": "before", "forItem": "ros" } ]""")),
            "Node 'section_notes' macro 'tool_note' anchors to fan item 'ros', which the folder fanned by 'section_notes' does not contain (items: hpi, plan)." };
        yield return new object[] { "node when against an enum node", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "assessment")["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "closing", "at": "after", "when": "node.scope.value == \"maybe\"" } ]""")),
            "Node 'assessment' macro 'closing' condition compares node.scope.value to 'maybe', which it does not declare (values: in_scope, out_of_scope)." };
        yield return new object[] { "node when in the ≤24 grammar", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "assessment")["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "closing", "at": "after", "when": "node:scope == in_scope" } ]""")),
            "Node 'assessment' macro 'closing' condition 'node:scope == in_scope' is the ≤24 condition grammar; at specVersion 25 a when is a Scriban expression over the objects a template reads — write node.scope.value == \"in_scope\"." };
        
        yield return new object[] { "deliverable at missing", new Action<System.Text.Json.Nodes.JsonObject>(root => DeliverableObject(root)["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ "closing" ]""")),
            "Deliverable 'consult' macro 'closing' must declare at (prepend | append)." };
        yield return new object[] { "deliverable at vocabulary", new Action<System.Text.Json.Nodes.JsonObject>(root => DeliverableObject(root)["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "closing", "at": "slot" } ]""")),
            "Deliverable 'consult' macro 'closing' declares at 'slot'; a deliverable instance is placed 'prepend' or 'append'." };
        yield return new object[] { "deliverable placed twice", new Action<System.Text.Json.Nodes.JsonObject>(root => DeliverableObject(root)["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "closing", "at": "prepend" }, { "id": "closing", "at": "prepend", "when": "kind == new" } ]""")),
            "Deliverable 'consult' macro 'closing' is placed twice (prepend); one identity (id, at) once." };
        yield return new object[] { "deliverable undeclared", new Action<System.Text.Json.Nodes.JsonObject>(root => DeliverableObject(root)["macros"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "nope", "at": "append" } ]""")),
            "Deliverable 'consult' macro 'nope' is not a declared macro." };
        yield return new object[] { "unused macro", new Action<System.Text.Json.Nodes.JsonObject>(root => root["macros"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "id": "spare", "label": "Spare", "file": "macros/closing.md", "instruction": "Never." }"""))),
            "Macro 'spare' is declared but never placed or read; place it on a node or a deliverable, or read macro.spare.content / macro.spare.prompt." };
        // #1067: the instruction's own read is not a use — the refusal reaches the desk.
        yield return new object[] { "macro read only by its own instruction", new Action<System.Text.Json.Nodes.JsonObject>(root => root["macros"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "id": "spare", "label": "Spare", "file": "macros/closing.md", "instruction": "Write {{ macro.spare.content }} once." }"""))),
            "Macro 'spare' is declared but never placed or read; place it on a node or a deliverable, or read macro.spare.content / macro.spare.prompt." };
    }

    [Theory]
    [MemberData(nameof(DeskCases))]
    public void TheDesk_SaysTheRegistrysMacroSentence(string _, Action<System.Text.Json.Nodes.JsonObject> reshape, string sentence)
    {
        var refusals = DeskRefusals(Reshaped(reshape));

        Assert.Contains(sentence, refusals);
        Assert.Null(sent);
    }

    [Fact]
    public void TheDesk_RefusesADeliverableMacro_ReadingAFanningNode()
    {
        var refusals = DeskRefusals(Reshaped(_ => { }, ("macros/closing.md", "Closing: {{ node.section_notes.content }}")));

        Assert.Contains("Deliverable 'consult' macro 'closing' reads node 'section_notes', which fans; a deliverable macro reads scalar nodes and aggregators — place the macro on the node to read its items.", refusals);
    }

    [Fact]
    public void TheDesk_RefusesAnInlineMacro_OnAFormatTakingNoOutputMacros()
    {
        var refusals = DeskRefusals(Reshaped(_ => { }, ("prompts/scope.md", "Is this in scope? {{ input.notes }} {{ macro.tool_note.prompt }}")));

        Assert.Contains("Node 'scope' reads macro.tool_note.prompt (an inline macro), but format 'classification' takes no output macros; only a text format with outputMacros may.", refusals);
    }

    [Fact]
    public void AnUnplacedDeliverableRow_AndABlankNewInstruction_BlockPublish_ByName()
    {
        var page = RenderEditor();
        CapturePublish();

        Navigate(page, DeliverablesPane);
        page.Find("fluent-select[aria-label='Append macro to deliverable consult']").Change("tool_note");

        Navigate(page, "+ Macro");
        page.Find("fluent-text-field[placeholder='closing_paragraph']").Change("spare");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro")).Click();
        page.Find(".editor-pane fluent-text-area:not(.macro-instruction)").Change("Spare text.");
        Publish(page);

        var refusals = Refusals(page);
        Assert.Contains("Deliverable 'consult' macro 'tool_note' must declare at (prepend | append).", refusals);
        Assert.Contains("Macro 'spare' must declare an instruction at specVersion 25; placed text tells the model when to write its text read.", refusals);
        Assert.Contains("Macro 'spare' is declared but never placed or read; place it on a node or a deliverable, or read macro.spare.content / macro.spare.prompt.", refusals);
        Assert.Null(sent);
    }

    [Fact]
    public void TheMacroWhenBuilder_OffersTheEnumFormatNode_WithItsValues()
    {
        // #1072: an instance is edited on its owner's pane — the node pane carries the builder.
        var page = RenderEditor();
        ShowNode(page, "assessment");

        page.Find("fluent-button[aria-label='Add a condition for assessment macro closing']").Click();
        var operands = OptionValues(page, "fluent-select[aria-label^='Condition operand for assessment macro closing']");
        Assert.Contains("node:scope", operands);
        // #1039: the option's text is the 25 spelling; its value stays the tree's key.
        Assert.Equal("when node.scope.value", page.Find("fluent-select[aria-label^='Condition operand for assessment macro closing']")
            .QuerySelectorAll("fluent-option").First(o => o.GetAttribute("value") == "node:scope").TextContent.Trim());
        page.Find("fluent-select[aria-label^='Condition operand for assessment macro closing']").Change("node:scope");
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, OptionValues(page, "fluent-select[aria-label^='Condition value for assessment macro closing']").Where(v => !string.IsNullOrEmpty(v)));
    }

    [Fact]
    public void TheFixtureItself_PublishesAccepted()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "standards");
        page.Find(".editor-pane fluent-text-area").Change("Follow the local standards, edited.");
        Publish(page);

        Assert.NotNull(sent);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    // ----- #1071: what the model is told -----

    private static string PromptPreview(IRenderedComponent<Templates> page, string macroId) =>
        page.Find($"[aria-label='What the model is told for macro {macroId}'] .macro-prompt-preview__text").TextContent;

    [Fact]
    public void TheMacroPane_ShowsWhatTheModelIsTold_ForAnInlineMacro()
    {
        // tool_note is read inline by prompts/assessment.md: the preview is the
        // instruction rendered, then the mechanics sentence — the engine's bytes.
        var page = RenderEditor();
        Navigate(page, "tool_note");

        Assert.Equal(PF.WorkflowMacroMechanics.Prompt("tool_note", "Note the tools used when a procedure is described."), PromptPreview(page, "tool_note"));
        Assert.Empty(page.FindAll(".macro-prompt-preview__note"));
    }

    [Fact]
    public void TheMacroPane_ShowsWhatTheModelIsTold_ForAPlacedOnlyMacro_WithTheNote()
    {
        // closing is placed (after assessment, prepended to consult) and read inline by nothing.
        var page = RenderEditor();
        Navigate(page, "closing");

        Assert.Equal(PF.WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed."), PromptPreview(page, "closing"));
        Assert.Contains("Read inline by nothing yet — placed instances do not use the instruction.", page.Find(".macro-prompt-preview__note").TextContent);
    }

    [Fact]
    public void EditingTheInstruction_UpdatesWhatTheModelIsTold_WithTokensForTheRunsValues()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("Close for {{ profile.name }} after {{ node.assessment.content }}.");

        Assert.StartsWith("Close for ⟨profile.name⟩ after ⟨node.assessment.content⟩.", PromptPreview(page, "closing"));
    }

    [Fact]
    public void ASelfRead_ShowsTheMacrosTextInThePreview_AndTheWarning()
    {
        // The #1067 trap made visible: the text lands in the prompt, and the validator's sentence sits beside it.
        var page = RenderEditor();
        Navigate(page, "closing");

        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("Write {{ macro.closing.content }} last.");

        Assert.StartsWith("Write Closing: ⟨profile.name⟩. last.", PromptPreview(page, "closing"));
        Assert.Contains(
            "Macro 'closing' instruction reads macro.closing.content; an instruction renders where macro.closing.prompt is read, so that read puts the macro's text in the prompt — say where the content goes, the mechanics sentence names the read.",
            page.Find(".macro-prompt-preview .node-reads__warning").TextContent);
    }

    [Fact]
    public void AnInstructionThatDoesNotRender_ShowsTheRenderersSentence()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("{{ if }}");

        var preview = page.Find("[aria-label='What the model is told for macro closing'] .macro-prompt-preview__text");
        Assert.Contains("editor-warning", preview.ClassName);
        Assert.Contains("does not parse", preview.TextContent);
    }

    [Fact]
    public void ABlankInstruction_SaysSo()
    {
        var page = RenderEditor();
        Navigate(page, "+ Macro");
        page.Find("fluent-text-field[placeholder='closing_paragraph']").Change("signature_line");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro")).Click();
        Navigate(page, "signature_line");

        Assert.Equal("No instruction yet.", PromptPreview(page, "signature_line").Trim());
    }

}
