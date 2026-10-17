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
        // prose nodes take output macros; the classifier (enum), the concept_list and the
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

    // ----- the macro pane: Read at -----

    [Fact]
    public void TheMacroPane_ListsWhereItIsReadInline_FromTheWalk()
    {
        var page = RenderEditor();
        Navigate(page, "tool_note");

        var readers = page.Find("ul.macro-readers[aria-label='Readers of tool_note']");
        Assert.Equal(new[] { "Prompt 'assessment' — macro.tool_note.prompt (inline)" }, readers.QuerySelectorAll("li").Select(li => li.TextContent.Trim()));

        page.Find("ul.macro-readers button.snippet-readers__row").Click();
        Assert.Equal("assessment", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void AMacroReadNowhere_SaysSo_AndATextReadIsNamedAsText()
    {
        var page = RenderEditor();
        Navigate(page, "closing");
        Assert.Contains("Not read by any template", page.Find(".editor-pane").TextContent);

        // Read as text from the standards snippet.
        Navigate(page, "standards");
        page.Find(".editor-pane fluent-text-area").Change("Follow the local standards. {{ macro.closing.content }}");
        Navigate(page, "closing");

        var readers = page.Find("ul.macro-readers[aria-label='Readers of closing']");
        Assert.Equal(new[] { "Snippet 'standards' — macro.closing.content" }, readers.QuerySelectorAll("li").Select(li => li.TextContent.Trim()));
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
        Assert.Equal("input.kind == \"new\"", macros[0].GetProperty("when").GetString());
        Assert.Equal("tool_note", macros[1].GetProperty("id").GetString());
        Assert.Equal("append", macros[1].GetProperty("at").GetString());
        Assert.Equal("input.kind == \"follow_up\"", macros[1].GetProperty("when").GetString());
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
        var page = RenderEditor();
        Navigate(page, "closing");

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
}
