using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C3 (#1009, design §§ 5, 7, 13): macros at 25 — the macro pane's instruction,
/// Instances (node and deliverable rows) and Read inline at; the node pane's one Macros
/// section with its derived inline slots; the Deliverables pane's prepend/append; the
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
            EditorFormatsCatalog.Catalog,
            EditorFormatsCatalog.AcceptedWith25);
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
    public void TheMacroPane_ListsItsInstances_OnNodesAndDeliverables_WithTheirControls()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        Assert.Empty(page.FindAll(".macro-attachments"));
        var instances = page.Find(".macro-instances");
        Assert.Contains("Instances", instances.TextContent);

        var onNode = instances.QuerySelector("[data-instance-node='assessment']")!;
        Assert.Equal("after", onNode.QuerySelector("fluent-select[aria-label='Placement for macro closing on assessment']")!.GetAttribute("current-value"));
        Assert.Equal(new[] { "before", "after" }, OptionValues(page, "fluent-select[aria-label='Placement for macro closing on assessment']"));
        Assert.Contains("after this node's output", onNode.TextContent);
        Assert.NotNull(onNode.QuerySelector("fluent-button[aria-label='Remove macro closing from assessment']"));
        Assert.NotNull(onNode.QuerySelector("fluent-button[aria-label='Add a condition for assessment macro closing']"));

        var onDeliverable = instances.QuerySelector("[data-instance-deliverable='consult']")!;
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
    public void AttachingToANode_OffersOnlyFormatsTakingOutputMacros_AndWritesABeforeInstance()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "tool_note");

        var nodes = OptionValues(page, "fluent-select[aria-label='Attach macro tool_note to a node']");
        // prose nodes take output macros; the classifier (enum), the concept-list and the
        // computed check do not; the aggregator never; section_notes already carries it (forItem) but has a free identity.
        Assert.Contains("assessment", nodes);
        Assert.Contains("section_notes", nodes);
        Assert.DoesNotContain("scope", nodes);
        Assert.DoesNotContain("extract", nodes);
        Assert.DoesNotContain("coverage", nodes);
        Assert.DoesNotContain("all", nodes);

        page.Find("fluent-select[aria-label='Attach macro tool_note to a node']").Change("assessment");
        Assert.NotNull(page.Find(".macro-instances [data-instance-node='assessment']"));
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
    public void RemovingANodeInstance_FromTheMacroPane_LeavesTheNodesList()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "closing");

        page.Find("fluent-button[aria-label='Remove macro closing from assessment']").Click();
        Assert.Empty(page.FindAll("[data-instance-node='assessment']"));
        Publish(page);

        Assert.False(Node(Manifest(sent!), "assessment").TryGetProperty("macros", out _));
    }

    [Fact]
    public void TheRemoveMacroButton_NamesEveryInstanceItTakes()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        var remove = page.FindAll("fluent-button").First(b => b.TextContent.Contains("Remove macro"));
        Assert.Contains("node 'assessment'", remove.TextContent);
        Assert.Contains("deliverable 'consult'", remove.TextContent);
    }

    // ----- the macro pane: Read inline at -----

    [Fact]
    public void TheMacroPane_ListsWhereItIsReadInline_FromTheWalk()
    {
        var page = RenderEditor();
        Navigate(page, "tool_note");

        var readers = page.Find("ul.macro-readers[aria-label='Readers of tool_note']");
        Assert.Equal(new[] { "Prompt 'assessment' — macro.tool_note.prompt (slot)" }, readers.QuerySelectorAll("li").Select(li => li.TextContent.Trim()));

        page.Find("ul.macro-readers button.snippet-readers__row").Click();
        Assert.Equal("assessment", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void AMacroReadNowhere_SaysSo_AndATextReadIsNamedAsText()
    {
        var page = RenderEditor();
        Navigate(page, "closing");
        Assert.Contains("Not read inline by any template", page.Find(".editor-pane").TextContent);

        // Read as text from the standards snippet.
        Navigate(page, "standards");
        page.Find(".editor-pane fluent-text-area").Change("Follow the local standards. {{ macro.closing.text }}");
        Navigate(page, "closing");

        var readers = page.Find("ul.macro-readers[aria-label='Readers of closing']");
        Assert.Equal(new[] { "Snippet 'standards' — macro.closing.text" }, readers.QuerySelectorAll("li").Select(li => li.TextContent.Trim()));
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
    [InlineData("extract", "concept-list")]
    public void ANodeWhoseFormatTakesNoOutputMacros_CannotAttach_AndSaysWhy(string nodeId, string format)
    {
        var page = RenderEditor();
        ShowNode(page, nodeId);

        Assert.Empty(page.FindAll($"fluent-select[aria-label='Attach a macro to {nodeId}']"));
        Assert.Contains($"Format '{format}' takes no output macros; only a text format with outputMacros may.", page.Find(".node-macros-refused").TextContent);
    }

    [Fact]
    public void InlineSlots_AreDerivedFromTheWalk_AndOpenTheMacro()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");

        var slots = page.Find("ul.node-inline-slots[aria-label='Inline slots of assessment']");
        Assert.Equal(new[] { "macro.tool_note.prompt — Tool note" }, slots.QuerySelectorAll("li").Select(li => li.TextContent.Trim()));

        page.Find("ul.node-inline-slots button").Click();
        Assert.Equal("tool_note", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void AFanNode_NamesTheItemsLackingTheSlotRead_InTheValidatorsWords()
    {
        var page = RenderEditor();
        ShowNode(page, "section_notes");
        Assert.Contains("No inline slot", page.Find(".editor-pane").TextContent);

        // The hpi item's text reads the slot; plan's does not.
        Navigate(page, "HPI");
        page.Find(".editor-pane fluent-text-area").Change("History: {{ input.notes }} {{ macro.tool_note.prompt }}");
        ShowNode(page, "section_notes");

        Assert.Contains("macro.tool_note.prompt", page.Find("ul.node-inline-slots[aria-label='Inline slots of section_notes']").TextContent);
        Assert.Equal(
            "Node 'section_notes' fans snippet folder 'sections'; items plan carry no macro.tool_note.prompt read, so no marker is emitted for them.",
            page.Find(".node-inline-slots__warning").TextContent.Trim());
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
        Assert.DoesNotContain("closing", offered);
        Assert.Contains("tool_note", offered);
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
        Assert.Equal("kind == new", macros[0].GetProperty("when").GetString());
        Assert.Equal("tool_note", macros[1].GetProperty("id").GetString());
        Assert.Equal("append", macros[1].GetProperty("at").GetString());
        Assert.Equal("kind == follow_up", macros[1].GetProperty("when").GetString());
        Assert.False(macros[1].TryGetProperty("before", out _));
        Assert.False(macros[1].TryGetProperty("slot", out _));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
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
}
