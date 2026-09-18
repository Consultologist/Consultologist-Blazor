using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #746: the Schemas pane. Output-contract schemas are viewable and an unused
/// one is removable; a body must match an engine catalog contract, so it is
/// shown as published, not edited. Removal is refused while a node reads the
/// contract, and drops both the manifest map entry and the file at publish.
/// </summary>
public class TemplatesSchemasTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
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

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    // Schema-bearing publishes must validate against the real catalog, not an empty dict.
    private Consultologist.PackageFormat.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            EditorCatalogSchemas.CatalogSchemas.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    [Fact]
    public void ThePane_ListsADeclaredSchema_AndShowsItsBody()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        Navigate(page, "Schemas");

        var row = page.Find("div[data-schema='concept-list']");
        Assert.Contains("schemas/concept-list.json", row.TextContent);
        // #760: a declared body is shown in an editable text area.
        Assert.Contains("\"concepts\"", page.Find("textarea[aria-label='Schema body for concept-list']").GetAttribute("value"));
    }

    [Fact]
    public void RemovingAReferencedSchema_IsRefused_AndNamesTheReaders()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        Navigate(page, "Schemas");

        // V12Full's concept-list is the output of extract-input-terms / extract-note-terms.
        Assert.True(page.Find("button[aria-label='Remove schema concept-list']").HasAttribute("disabled"));
        Assert.Contains("Used by", page.Find("div[data-schema='concept-list']").TextContent);
        Assert.Contains("extract-input-terms", page.Find("div[data-schema='concept-list']").TextContent);
    }

    [Fact]
    public void RemovingAnUnusedSchema_DropsTheMapEntryAndTheFile_AndPublishes()
    {
        var page = RenderEditor(EditorFixtures.V10OrphanSchema());
        CapturePublish();
        Navigate(page, "Schemas");

        var remove = page.Find("button[aria-label='Remove schema report']");
        Assert.False(remove.HasAttribute("disabled"));
        remove.Click();
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var manifest = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;
        var stillDeclared = manifest.TryGetProperty("schemas", out var schemas)
            && schemas.ValueKind == JsonValueKind.Object
            && schemas.EnumerateObject().Any();
        Assert.False(stillDeclared);
        Assert.DoesNotContain("schemas/report.json", sent.Files.Keys);
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ARemovedSchema_CanBeRestored()
    {
        var page = RenderEditor(EditorFixtures.V10OrphanSchema());
        Navigate(page, "Schemas");

        page.Find("button[aria-label='Remove schema report']").Click();
        Assert.NotNull(page.Find("div[data-schema-removed='report']"));

        page.Find("button[aria-label='Restore schema report']").Click();
        Assert.NotNull(page.Find("div[data-schema='report']"));
        Assert.Empty(page.FindAll("div[data-schema-removed='report']"));
    }

    // ----- catalog-backed add (#759) ---------------------------------------

    [Fact]
    public void AddingConceptList_DeclaresItAndWritesTheCanonicalBody()
    {
        var page = RenderEditor(EditorFixtures.V7());
        CapturePublish();
        Navigate(page, "Schemas");

        page.Find("button[aria-label='Add output contract concept-list']").Click();
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var schemas = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("schemas");
        Assert.Equal("schemas/concept-list.json", schemas.GetProperty("concept-list").GetString());
        Assert.Contains("\"concepts\"", sent.Files["schemas/concept-list.json"]);
        // Unreferenced but a canonical catalog match, so the validator accepts it.
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AfterAdding_ANodeOutputPickerOffersTheContract()
    {
        var page = RenderEditor(EditorFixtures.V7());
        Navigate(page, "Schemas");
        page.Find("button[aria-label='Add output contract concept-list']").Click();

        Navigate(page, "Graph");
        var options = page.FindAll("select[aria-label='Node output contract']")
            .SelectMany(select => select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        Assert.Contains("concept-list", options);
    }

    [Fact]
    public void AnAlreadyDeclaredContract_IsNotOfferedToAdd()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        Navigate(page, "Schemas");

        Assert.Empty(page.FindAll("button[aria-label='Add output contract concept-list']"));
    }

    [Fact]
    public void RemovingAJustAddedSchema_ReturnsTheAddAffordance()
    {
        var page = RenderEditor(EditorFixtures.V7());
        Navigate(page, "Schemas");

        page.Find("button[aria-label='Add output contract concept-list']").Click();
        Assert.NotNull(page.Find("div[data-schema='concept-list']"));

        page.Find("button[aria-label='Remove schema concept-list']").Click();
        Assert.Empty(page.FindAll("div[data-schema='concept-list']"));
        Assert.NotEmpty(page.FindAll("button[aria-label='Add output contract concept-list']"));
    }
}
