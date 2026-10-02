using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #746: the Schemas pane. Output-contract schemas are viewable and an unused
/// one is removable. Removal is refused while a node reads the contract, and
/// drops both the manifest map entry and the file at publish.
/// #760: a body is now editable (free-form JSON) and a new schema can be created
/// by id; the body must still canonically match an engine catalog contract, which
/// the publish desk check pre-checks client-side and the server enforces.
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

    // #760: the publish desk check fetches the catalog to pre-check schema bodies.
    // NSubstitute auto-stubs the unstubbed call to an empty dict (not null), so a
    // schema-publishing test must say which catalog the pre-check sees.
    private void StubRealCatalog() =>
        WorkflowService.GetCatalogSchemasAsync().Returns(
            EditorCatalogSchemas.CatalogSchemas.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

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
        // #760: the body is an editable FluentTextArea now, not a read-only <pre>.
        var body = page.Find("div[data-schema='concept-list'] fluent-text-area.schema-body");
        Assert.Contains("\"concepts\"", body.GetAttribute("value"));
    }

    [Fact]
    public void RemovingAReferencedSchema_IsRefused_AndNamesTheReaders()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        Navigate(page, "Schemas");

        // V12Full's concept-list is the output of extract-input-terms / extract-note-terms.
        Assert.True(page.Find("fluent-button[aria-label='Remove schema concept-list']").HasAttribute("disabled"));
        var card = page.Find("div[data-schema='concept-list']").TextContent;
        Assert.Contains("Used by", card);
        Assert.Contains("extract-input-terms", card);
        // #843: matched to the prelude pane — names the removal precondition.
        Assert.Contains("Change their output contract before it can be removed", card);
    }

    [Fact]
    public void RemovingAnUnusedSchema_DropsTheMapEntryAndTheFile_AndPublishes()
    {
        var page = RenderEditor(EditorFixtures.V10OrphanSchema());
        CapturePublish();
        Navigate(page, "Schemas");

        var remove = page.Find("fluent-button[aria-label='Remove schema report']");
        Assert.False(remove.HasAttribute("disabled"));
        // #843: an unused schema shows the pane hint instead of a reader list.
        Assert.Contains("Not yet used by any node", page.Find("div[data-schema='report']").TextContent);
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

        page.Find("fluent-button[aria-label='Remove schema report']").Click();
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

        page.Find("fluent-button[aria-label='Add output contract concept-list']").Click();
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
        page.Find("fluent-button[aria-label='Add output contract concept-list']").Click();

        Navigate(page, "Graph");
        var options = page.FindAll("fluent-select[aria-label='Node output contract']")
            .SelectMany(select => select.QuerySelectorAll("fluent-option").Select(option => option.GetAttribute("value")));
        Assert.Contains("concept-list", options);
    }

    [Fact]
    public void AnAlreadyDeclaredContract_IsNotOfferedToAdd()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        Navigate(page, "Schemas");

        Assert.Empty(page.FindAll("fluent-button[aria-label='Add output contract concept-list']"));
    }

    [Fact]
    public void RemovingAJustAddedSchema_ReturnsTheAddAffordance()
    {
        var page = RenderEditor(EditorFixtures.V7());
        Navigate(page, "Schemas");

        page.Find("fluent-button[aria-label='Add output contract concept-list']").Click();
        Assert.NotNull(page.Find("div[data-schema='concept-list']"));

        page.Find("fluent-button[aria-label='Remove schema concept-list']").Click();
        Assert.Empty(page.FindAll("div[data-schema='concept-list']"));
        Assert.NotEmpty(page.FindAll("fluent-button[aria-label='Add output contract concept-list']"));
    }

    // ----- free-form create + editable bodies (#760) -----------------------

    [Fact]
    public void CreatingASchemaFreeForm_DeclaresItAndWritesTheAuthoredBody()
    {
        var page = RenderEditor(EditorFixtures.V7());
        CapturePublish();
        StubRealCatalog();
        Navigate(page, "Schemas");

        page.Find("fluent-text-field[placeholder='my_contract']").Change("my_contract");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create schema")).Click();

        // The new schema shows an editable body; author one.
        page.Find("div[data-schema='my_contract'] fluent-text-area.schema-body").Change(EditorCatalogSchemas.ConceptListSchema);
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var schemas = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("schemas");
        Assert.Equal("schemas/my_contract.json", schemas.GetProperty("my_contract").GetString());
        Assert.Equal(EditorCatalogSchemas.ConceptListSchema, sent.Files["schemas/my_contract.json"]);
        // Unreferenced, so the catalog-match rule doesn't apply — the validator accepts it.
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void EditingADeclaredBody_PublishesTheNewText()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        CapturePublish();
        StubRealCatalog();
        Navigate(page, "Schemas");

        // A still-canonical variant (a new title; the match is modulo title/description),
        // so the body round-trips to the package file and the validator still accepts it.
        var edited = System.Text.Json.Nodes.JsonNode.Parse(EditorCatalogSchemas.ConceptListSchema)!;
        edited["title"] = "A clinician-edited title";
        var editedBody = edited.ToJsonString();
        page.Find("div[data-schema='concept-list'] fluent-text-area.schema-body").Change(editedBody);
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal(editedBody, sent!.Files["schemas/concept-list.json"]);
        Assert.Contains("A clinician-edited title", sent.Files["schemas/concept-list.json"]);
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void CreatingASchema_WithABadId_IsRefusedInline()
    {
        var page = RenderEditor(EditorFixtures.V7());
        Navigate(page, "Schemas");

        page.Find("fluent-text-field[placeholder='my_contract']").Change("Bad Id");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create schema")).Click();

        Assert.Contains("must be lowercase", page.Markup);
        Assert.Empty(page.FindAll("div[data-schema='Bad Id']"));
    }

    [Fact]
    public void CreatingASchema_WithADuplicateId_IsRefusedInline()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        Navigate(page, "Schemas");

        page.Find("fluent-text-field[placeholder='my_contract']").Change("concept-list");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create schema")).Click();

        Assert.Contains("already exists", page.Markup);
    }

    [Fact]
    public void AnInvalidJsonBody_ShowsTheInlineHint()
    {
        var page = RenderEditor(EditorFixtures.V7());
        Navigate(page, "Schemas");

        page.Find("fluent-text-field[placeholder='my_contract']").Change("my_contract");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create schema")).Click();
        page.Find("div[data-schema='my_contract'] fluent-text-area.schema-body").Change("{ not valid json");

        Assert.Contains("not valid JSON", page.Find("div[data-schema='my_contract'] .schema-error").TextContent);
    }

    [Fact]
    public void ANonCanonicalBody_OnAReferencedSchema_FiresTheDeskCheck_WhenTheCatalogIsKnown()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        CapturePublish();
        // The desk check pre-checks client-side against the fetched catalog (#852).
        WorkflowService.GetCatalogSchemasAsync().Returns(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["concept-list"] = EditorCatalogSchemas.ConceptListSchema });
        Navigate(page, "Schemas");

        // Valid JSON, but not a catalog contract.
        page.Find("div[data-schema='concept-list'] fluent-text-area.schema-body").Change("{\"type\":\"object\",\"properties\":{}}");
        Publish(page);

        Assert.Null(sent);
        Assert.Contains(Refusals(page), finding => finding.Contains("must canonically match a catalog output contract"));
    }

    [Fact]
    public void ANonCanonicalBody_IsNotFlagged_WhenTheCatalogCannotBeFetched()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        CapturePublish();
        // No catalog (the #852 idiom): the match can't be judged, so the server stays authoritative.
        WorkflowService.GetCatalogSchemasAsync().Returns((IReadOnlyDictionary<string, string>?)null);
        Navigate(page, "Schemas");

        page.Find("div[data-schema='concept-list'] fluent-text-area.schema-body").Change("{\"type\":\"object\",\"properties\":{}}");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
    }
}
