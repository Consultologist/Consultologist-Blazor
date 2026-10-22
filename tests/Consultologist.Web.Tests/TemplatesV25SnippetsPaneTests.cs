using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C2 (#1008, design §§ 4, 13): the Snippets group — the Data machinery under the
/// snippets table at 25. "+ Snippet" and "+ Snippet folder" in the two forms' place,
/// files under snippets/, snake_case ids in the registry's words, a node fanning a
/// folder as snippet.&lt;folder&gt;, a folder item's input-only when, and where each
/// snippet is read. Rendered at 25 alone; below 25 the Data group is byte-identical.
/// Inert on the live editor until A5.
/// </summary>
public class TemplatesV25SnippetsPaneTests : ClientRenderTestContext
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

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private static IReadOnlyList<string> Groups(IRenderedComponent<Templates> page) =>
        page.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()).ToList();

    private static IReadOnlyList<string> NavLabels(IRenderedComponent<Templates> page) =>
        page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Replace("●", string.Empty).Trim()).ToList();

    private static void CreateSnippet(IRenderedComponent<Templates> page, string id)
    {
        Navigate(page, "+ Snippet");
        page.Find(".new-item-fields fluent-text-field").Change(id);
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create snippet")).Click();
    }

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

    /// <summary>The fixture with the sections folder's index.json replaced.</summary>
    private static WorkflowPackageContentResponse WithSectionsIndex(string indexJson)
    {
        var package = EditorFixtures.V25();
        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal) { ["snippets/sections/index.json"] = indexJson };
        return package with { Files = files };
    }

    /// <summary>The fixture's manifest with one node replaced by the given JSON object.</summary>
    private static WorkflowPackageContentResponse WithNode(string nodeId, string nodeJson)
    {
        var package = EditorFixtures.V25();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        var nodes = root["nodes"]!.AsArray();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i]!["id"]!.GetValue<string>() == nodeId)
            {
                nodes[i] = System.Text.Json.Nodes.JsonNode.Parse(nodeJson);
            }
        }

        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    private static IReadOnlyList<string?> OptionValues(IRenderedComponent<Templates> page, string selector) =>
        page.Find(selector).QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();

    private static JsonElement SectionsIndex(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Files["snippets/sections/index.json"]).RootElement;

    private static JsonElement Item(JsonElement index, string id) =>
        index.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);

    /// <summary>A pending change opens the publish button: the standards snippet's text.</summary>
    private static void MakePending(IRenderedComponent<Templates> page)
    {
        Navigate(page, "standards");
        page.Find(".editor-pane fluent-text-area").Change("Follow the local standards, edited.");
    }

    private const string HpiAddCondition = "fluent-button[aria-label='Add a condition for sections item hpi']";
    private const string HpiOperand = "fluent-select[aria-label^='Condition operand for sections item hpi']";
    private const string HpiValue = "fluent-select[aria-label^='Condition value for sections item hpi']";

    // ----- the group -----

    [Fact]
    public void TheSnippetsGroup_RendersAt25_InDatasPlace_WithItsTwoButtons()
    {
        var page = RenderEditor();

        var groups = Groups(page);
        Assert.Contains("Snippets", groups);
        Assert.DoesNotContain("Data", groups);

        var labels = NavLabels(page);
        Assert.Contains("+ Snippet", labels);
        Assert.Contains("+ Snippet folder", labels);
        Assert.DoesNotContain("+ Data value", labels);
        Assert.DoesNotContain("+ Data folder", labels);

        // The fixture's snippets table, both shapes: the file and the folder with its items.
        Assert.Contains("standards", labels);
        Assert.Contains("sections (2)", labels.Select(l => l.Replace("›", string.Empty).Trim()).Concat(page.FindAll(".editor-nav__folder-summary").Select(s => s.TextContent.Replace("›", string.Empty).Replace("●", string.Empty).Trim())));
        Assert.Contains("HPI", labels);
        Assert.Contains("Plan", labels);
    }

    [Fact]
    public void Below25_TheDataGroupIsUntouched()
    {
        var page = RenderEditor(EditorFixtures.V6WithValue());

        var groups = Groups(page);
        Assert.Contains("Data", groups);
        Assert.DoesNotContain("Snippets", groups);
        var labels = NavLabels(page);
        Assert.Contains("+ Data value", labels);
        Assert.Contains("+ Data folder", labels);
    }

    [Fact]
    public void TheSnippetPane_ReadsItsFile_AndSaysHowItIsRead()
    {
        var page = RenderEditor();
        Navigate(page, "standards");

        var pane = page.Find(".editor-pane");
        Assert.Contains("snippets/standards.md", pane.TextContent);
        Assert.Contains("{{ snippet.standards.content }}", pane.TextContent);
        Assert.DoesNotContain("data:standards", pane.TextContent);
        Assert.Contains("Follow the local standards.", page.Find(".editor-pane fluent-text-area").GetAttribute("current-value"));
    }

    // ----- + Snippet -----

    [Fact]
    public void ANewSnippet_IsPending_UnderSnippets_AsAMarkdownFile()
    {
        var page = RenderEditor();
        CreateSnippet(page, "house_style");

        var pane = page.Find(".editor-pane");
        Assert.Contains("snippets/house_style.md", pane.TextContent);
        Assert.Contains("new, unpublished", pane.TextContent);
        Assert.Contains("not read by any template yet", page.Markup);
    }

    [Theory]
    [InlineData("House-Style")]
    [InlineData("1st")]
    [InlineData("house-style")]
    public void ASnippetId_MustBeSnakeCase_InTheRegistrysWords(string id)
    {
        var page = RenderEditor();
        CreateSnippet(page, id);

        Assert.Contains(
            $"Snippet id '{id}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).",
            page.Markup);
        Assert.DoesNotContain($"snippets/{id}.md", page.Markup);
    }

    [Fact]
    public void ASnippet_CannotTakeAnExistingSnippetsOrFoldersName()
    {
        var page = RenderEditor();

        CreateSnippet(page, "sections");
        Assert.Contains("A snippet named 'sections' already exists.", page.Markup);

        CreateSnippet(page, "standards");
        Assert.Contains("A snippet named 'standards' already exists.", page.Markup);
    }

    [Fact]
    public void AnEmptyNewSnippet_BlocksPublish_ByName()
    {
        var page = RenderEditor();
        CapturePublish();
        CreateSnippet(page, "house_style");

        Publish(page);

        Assert.Contains("Snippet 'house_style' has no text yet — write it before publishing.", Refusals(page));
        Assert.Null(sent);
    }

    // ----- + Snippet folder and + Add item -----

    [Fact]
    public void ANewFolder_TakesSnakeCase_AndLandsOnItsAddItemPane()
    {
        var page = RenderEditor();

        CreateFolder(page, "care-plans");
        Assert.Contains("Snippet id 'care-plans' must be snake_case", page.Markup);

        CreateFolder(page, "care_plans");
        Assert.Contains("New care_plans item", page.Find(".editor-pane").TextContent);
        Assert.Contains("item.content", page.Find(".editor-pane").TextContent);
    }

    [Fact]
    public void AnEmptyNewFolder_BlocksPublish_ByName()
    {
        var page = RenderEditor();
        CapturePublish();
        CreateFolder(page, "care_plans");

        Publish(page);

        Assert.Contains("Snippet folder 'care_plans' has no items yet — add at least one before publishing.", Refusals(page));
    }

    [Fact]
    public void TheForEachPicker_SpellsAFolder_AsSnippetDot()
    {
        var page = RenderEditor();
        Navigate(page, "Section notes");

        var options = page.FindAll("fluent-select[aria-label='Node forEach collection'] fluent-option")
            .Select(o => o.GetAttribute("value")).ToList();
        Assert.Contains("snippet.sections", options);
        Assert.DoesNotContain("data:sections", options);
    }

    [Fact]
    public void TheForEachPicker_SpellsAnArrayInput_AsInputDot()
    {
        // #1039: the picker wrote input:<id> at 25, which the registry refuses.
        var package = EditorFixtures.V25();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["inputs"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "id": "history", "label": "History", "required": false, "type": "array" }"""));
        package = package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
        var page = RenderEditor(package);
        Navigate(page, "Section notes");

        var options = page.FindAll("fluent-select[aria-label='Node forEach collection'] fluent-option")
            .Select(o => o.GetAttribute("value")).ToList();
        Assert.Contains("input.history", options);
        Assert.DoesNotContain("input:history", options);
    }

    // ----- the publish -----

    [Fact]
    public void APublishAt25_WritesSnippetsNeverData_AndTheRegistryAccepts()
    {
        var page = RenderEditor();
        CapturePublish();

        CreateSnippet(page, "house_style");
        page.Find(".editor-pane fluent-text-area").Change("Write plainly.");

        CreateFolder(page, "care_plans");
        AddItem(page, "discharge", "Discharge", "Discharge plan: {{ input.notes }}");

        Publish(page);

        Assert.NotNull(sent);
        var manifest = Manifest(sent!);
        Assert.False(manifest.TryGetProperty("data", out _));
        var snippets = manifest.GetProperty("snippets");
        Assert.Equal("snippets/house_style.md", snippets.GetProperty("house_style").GetString());
        Assert.Equal("snippets/care_plans/", snippets.GetProperty("care_plans").GetString());
        Assert.Equal("snippets/standards.md", snippets.GetProperty("standards").GetString());

        Assert.Equal("Write plainly.", sent.Files["snippets/house_style.md"]);
        Assert.Equal("Discharge plan: {{ input.notes }}", sent.Files["snippets/care_plans/discharge.md"]);
        var index = JsonDocument.Parse(sent.Files["snippets/care_plans/index.json"]).RootElement;
        Assert.Equal(new[] { "id", "name", "content" }, index.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToArray());
        var item = Assert.Single(index.GetProperty("items").EnumerateArray());
        Assert.Equal("discharge", item.GetProperty("id").GetString());
        Assert.Equal("Discharge", item.GetProperty("name").GetString());
        Assert.Equal("discharge.md", item.GetProperty("file").GetString());
        Assert.False(item.TryGetProperty("when", out _));

        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    // ----- a folder item's when (input-only, the existing builder) -----

    [Fact]
    public void TheItemWhenBuilder_OffersInputsOnly()
    {
        var page = RenderEditor();
        Navigate(page, "HPI");

        // Rests as "Every run" until opened — an item is born unconditional.
        Assert.Contains("Every run", page.Find(".editor-pane").TextContent);
        page.Find(HpiAddCondition).Click();

        var operands = OptionValues(page, HpiOperand);
        Assert.Contains("kind", operands);
        // No node: operand (the fixture's classifier 'scope' would be offered to a
        // node's own when) and no item:id — a folder's size is known at job start.
        Assert.DoesNotContain("node:scope", operands);
        Assert.DoesNotContain("item:id", operands);
    }

    [Fact]
    public void AnItemWhen_WritesIntoIndexJson_AndTheRegistryAccepts()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "HPI");
        page.Find(HpiAddCondition).Click();
        page.Find(HpiOperand).Change("kind");
        page.Find(HpiValue).Change("follow_up");

        // The pending dot on the item, and the kind named by the pending summary.
        Assert.Contains("●", page.Find("button.editor-nav__item--selected").TextContent);

        Publish(page);

        var index = SectionsIndex(sent!);
        // #1037: the builder writes the object spelling at 25; kind is an enum, so the literal is a string.
        Assert.Equal("input.kind == \"follow_up\"", Item(index, "hpi").GetProperty("when").GetString());
        // The loaded sibling keeps its when; the fields list is the loaded one.
        Assert.Equal("input.kind == \"new\"", Item(index, "plan").GetProperty("when").GetString());
        Assert.Equal(new[] { "id", "name", "content" }, index.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToArray());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void ALoadedItemWhen_ShowsInTheBuilder_AndItsEditRoundTrips()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "Plan");

        // A loaded when shows the builder directly, the operand already chosen.
        var operand = page.Find("fluent-select[aria-label^='Condition operand for sections item plan']");
        Assert.Equal("kind", operand.GetAttribute("current-value"));

        page.Find("fluent-select[aria-label^='Condition value for sections item plan']").Change("follow_up");
        Publish(page);

        Assert.Equal("input.kind == \"follow_up\"", Item(SectionsIndex(sent!), "plan").GetProperty("when").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void APendingItem_TakesAWhen_IntoItsFoldersIndex()
    {
        var page = RenderEditor();
        CapturePublish();
        CreateFolder(page, "care_plans");
        AddItem(page, "discharge", "Discharge", "Discharge plan.");

        page.Find("fluent-button[aria-label='Add a condition for care_plans item discharge']").Click();
        page.Find("fluent-select[aria-label^='Condition operand for care_plans item discharge']").Change("kind");
        page.Find("fluent-select[aria-label^='Condition value for care_plans item discharge']").Change("new");
        Publish(page);

        var index = JsonDocument.Parse(sent!.Files["snippets/care_plans/index.json"]).RootElement;
        Assert.Equal("input.kind == \"new\"", Item(index, "discharge").GetProperty("when").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void AnItemWhen_SurvivesAReload_WithItsDraft()
    {
        var page = RenderEditor();
        Navigate(page, "HPI");
        page.Find(HpiAddCondition).Click();
        page.Find(HpiOperand).Change("kind");
        page.Find(HpiValue).Change("new");

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = Render<Templates>();

        Assert.Equal("kind", again.Find(HpiOperand).GetAttribute("current-value"));
        Assert.Contains("1 snippet condition", again.Markup);
    }

    [Fact]
    public void DiscardingTheItemPane_DropsItsWhen()
    {
        var page = RenderEditor();
        Navigate(page, "HPI");
        page.Find(HpiAddCondition).Click();
        page.Find(HpiOperand).Change("kind");
        page.Find(HpiValue).Change("new");

        page.Find(".pane-discard-button").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Confirm discard")).Click();

        Assert.DoesNotContain("●", page.Find("button.editor-nav__item--selected").TextContent);
        Assert.Contains("Every run", page.Find(".editor-pane").TextContent);
    }

    // ----- the desk: the registry's snippet sentences, word for word -----

    [Theory]
    // #1039: at 25 the desk is the registry's validator — its words by construction.
    [InlineData("node.scope.value == \\\"in_scope\\\"",
        "Snippet folder 'sections' item 'plan' condition reads node.scope; a folder item's when reads inputs only, so the folder's size is known at job start (the template's own if gates anything else).")]
    [InlineData("input.severity == \\\"high\\\"",
        "Snippet folder 'sections' item 'plan' condition reads input 'severity', which the package does not declare.")]
    [InlineData("input.kind == \\\"urgent\\\"",
        "Snippet folder 'sections' item 'plan' condition compares input.kind to 'urgent', which it does not declare (values: new, follow_up).")]
    [InlineData("kind == urgent",
        "Snippet folder 'sections' item 'plan' condition 'kind == urgent' is the ≤24 condition grammar; at specVersion 25 a when is a Scriban expression over the objects a template reads — write input.kind == \"urgent\".")]
    public void TheDesk_RefusesAFolderItemsWhen_InTheRegistrysWords(string when, string sentence)
    {
        var page = RenderEditor(WithSectionsIndex($$"""
            { "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md" }, { "id": "plan", "name": "Plan", "file": "plan.md", "when": "{{when}}" } ] }
            """));
        CapturePublish();
        MakePending(page);

        Publish(page);

        Assert.Contains(sentence, Refusals(page));
        Assert.Null(sent);
    }

    [Fact]
    public void TheDesk_NamesAFolderItemsWhen_ThatDoesNotParse()
    {
        var page = RenderEditor(WithSectionsIndex("""
            { "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md" }, { "id": "plan", "name": "Plan", "file": "plan.md", "when": "kind ==" } ] }
            """));
        CapturePublish();
        MakePending(page);

        Publish(page);

        Assert.Contains(Refusals(page), r => r.StartsWith("Snippet folder 'sections' item 'plan' condition ", StringComparison.Ordinal));
        Assert.Null(sent);
    }

    [Theory]
    [InlineData("snippet.missing", "Node 'section_notes' forEach fans snippet folder 'missing', which the package does not declare.")]
    [InlineData("snippet.standards", "Node 'section_notes' forEach fans snippet 'standards', which is a file, not a folder.")]
    [InlineData("snippet:sections", "Node 'section_notes' forEach 'snippet:sections' is the ≤24 spelling; at specVersion 25 forEach fans snippet.<folder> or input.<id>.")]
    [InlineData("data:sections", "Node 'section_notes' forEach 'data:sections' is the ≤24 spelling; at specVersion 25 forEach fans snippet.<folder> or input.<id>.")]
    [InlineData("input:history", "Node 'section_notes' forEach 'input:history' is the ≤24 spelling; at specVersion 25 forEach fans snippet.<folder> or input.<id>.")]
    [InlineData("sections", "Node 'section_notes' forEach 'sections' must be snippet.<folder> or input.<id>.")]
    public void TheDesk_RefusesAForEach_ThatIsNotADeclaredFolder(string forEach, string sentence)
    {
        var page = RenderEditor(WithNode("section_notes", $$"""
            { "id": "section_notes", "label": "Section notes", "prompt": "section", "format": "prose", "forEach": "{{forEach}}" }
            """));
        CapturePublish();
        MakePending(page);

        Publish(page);

        Assert.Contains(sentence, Refusals(page));
        Assert.Null(sent);
    }

    // ----- where each snippet is read (the engine's walk) -----

    [Fact]
    public void TheSnippetPane_ListsItsReaders_AndRefusesRemoveWhileRead()
    {
        var page = RenderEditor();
        Navigate(page, "standards");

        // #1086: the readers as the shared descriptive rows — the reader as a link, the member read.
        var readers = page.Find(".template-read-rows[aria-label='Readers of standards']");
        Assert.Equal(new[] { "Prompt 'assessment'" }, readers.QuerySelectorAll(".template-read-row").Select(row => row.GetAttribute("data-read-reader")));
        Assert.Equal("snippet.standards.content", readers.QuerySelector(".template-read-row__member")!.TextContent.Trim());
        Assert.Empty(page.FindAll("ul.snippet-readers"));
        Assert.Contains("Change the readers below first to delete this snippet.", page.Find(".editor-pane").TextContent);
        Assert.NotNull(page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Remove").GetAttribute("disabled"));
        // The nav says nothing about an unread snippet for one that is read.
        Assert.DoesNotContain("not read by any template yet", page.Markup);
    }

    [Fact]
    public void AReaderRow_OpensTheReadersPane()
    {
        var page = RenderEditor();
        Navigate(page, "standards");

        page.Find(".template-read-rows[aria-label='Readers of standards'] .template-read-row__reader").Click();

        Assert.Equal("assessment", page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim());
    }

    [Fact]
    public void AFoldersItems_ListTheFanningNode()
    {
        var page = RenderEditor();
        Navigate(page, "HPI");

        // #1086: the fanning node by its label, a note-only row ("fans it"), a link to the node.
        var readers = page.Find(".template-read-rows[aria-label='Readers of sections']");
        var row = Assert.Single(readers.QuerySelectorAll(".template-read-row"));
        Assert.Equal("Node 'Section notes'", row.GetAttribute("data-read-reader"));
        Assert.Contains("fans it", row.TextContent);
        Assert.Null(row.QuerySelector(".template-read-row__member"));
        Assert.DoesNotContain("not fanned or iterated by anything yet", page.Markup);

        row.QuerySelector(".template-read-row__reader")!.Click();
        Assert.Equal("node:section_notes", EditorSession.SelectedKey);
    }

    [Fact]
    public void ASnippetReadByAnotherSnippet_NamesIt_AsItIsTyped()
    {
        var page = RenderEditor();
        CreateSnippet(page, "house_style");
        Assert.Contains("Not read by any template yet", page.Find(".editor-pane").TextContent);

        // The new snippet reads standards: standards now lists it; the new one is still unread.
        page.Find(".editor-pane fluent-text-area").Change("{{ snippet.standards.content }} Write plainly.");
        Navigate(page, "standards");

        var readers = page.Find(".template-read-rows[aria-label='Readers of standards']").QuerySelectorAll(".template-read-row").Select(row => row.GetAttribute("data-read-reader")).ToList();
        Assert.Contains("Prompt 'assessment'", readers);
        Assert.Contains("Snippet 'house_style'", readers);
        Assert.Contains("not read by any template yet", page.Markup);
    }

    [Fact]
    public void AnUnreadSnippet_CanBeRemoved_AndTheNavSaysSo()
    {
        var page = RenderEditor();
        CreateSnippet(page, "house_style");

        Assert.Contains("not read by any template yet", page.Markup);
        Assert.Null(page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Remove").GetAttribute("disabled"));
        page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Remove").Click();

        Assert.DoesNotContain("snippets/house_style.md", page.Markup);
    }

    [Fact]
    public void WithoutTheCatalogue_TheReadersListSaysWhatItCannotSee()
    {
        var page = RenderEditor(withCatalogue: false);
        Navigate(page, "standards");

        // assessment is on a catalogue format, so its prompt does not walk without the catalogue.
        Assert.Contains("reads from prompts on catalogue formats are not listed", page.Find(".editor-pane").TextContent);
    }

    [Fact]
    public void TheFixtureItself_PublishesAccepted_WithItsGatedItem()
    {
        var page = RenderEditor();
        CapturePublish();

        // Touch a prompt so there is something to publish.
        Navigate(page, "scope");
        page.Find(".editor-pane fluent-text-area").Change("Is this in scope?\n{{ input.notes }}");
        Publish(page);

        Assert.NotNull(sent);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
        // The loaded index.json travels untouched: its when stays as written.
        Assert.Contains("\"when\": \"input.kind == \\\"new\\\"\"", sent!.Files["snippets/sections/index.json"]);
    }
}
