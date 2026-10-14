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
/// folder as snippet:&lt;folder&gt;, a folder item's input-only when, and where each
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
            EditorFormatsCatalog.Catalog,
            EditorFormatsCatalog.AcceptedWith25);
    }

    private static JsonElement Manifest(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement;

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
        Assert.Contains("{{ snippet.standards.text }}", pane.TextContent);
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
        Assert.Contains("item.text", page.Find(".editor-pane").TextContent);
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
    public void TheForEachPicker_SpellsAFolder_AsSnippetColon()
    {
        var page = RenderEditor();
        Navigate(page, "Section notes");

        var options = page.FindAll("fluent-select[aria-label='Node forEach collection'] fluent-option")
            .Select(o => o.GetAttribute("value")).ToList();
        Assert.Contains("snippet:sections", options);
        Assert.DoesNotContain("data:sections", options);
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
        Assert.Contains("\"when\": \"kind == new\"", sent!.Files["snippets/sections/index.json"]);
    }
}
