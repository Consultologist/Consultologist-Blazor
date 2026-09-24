using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v10 (#498), #748: array elements recurse in the editor the way object fields
/// do. An array element that is itself an array, an object, or an enum draws its
/// own editor beneath it, to any depth, so array-of-array-of-object, N-level
/// arrays and inner enums are authored rather than hand-edited. The desk spells
/// a deep shape gap; below 10 the depth is refused by name.
/// </summary>
public class TemplatesV10ArraysTests : ClientRenderTestContext
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

    private Consultologist.PackageFormat.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
            manifest, sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static JsonElement Inputs(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("inputs");

    [Fact]
    public void DeepArrays_DrawAnElementEditorAtEveryLevel_AndTheFixturePublishes()
    {
        var page = RenderEditor(EditorFixtures.V10DeepArrays());
        CapturePublish();
        Navigate(page, "Inputs");

        // matrix: array -> array -> object, its fields drawn beneath the element.
        Assert.Equal("array", page.Find("select[aria-label='Items for input matrix']").GetAttribute("value"));
        Assert.Equal("object", page.Find("select[aria-label='Inner items for input matrix']").GetAttribute("value"));
        Assert.Contains("matrix.[].[]", page.FindAll("li.declared-row__fields").Select(row => row.GetAttribute("data-fields-for")));
        Assert.Contains("matrix.[].[].name", page.FindAll("li.declared-field").Select(row => row.GetAttribute("data-field")));
        Assert.Equal("number", page.Find("select[aria-label='Items for field matrix.[].[].scores']").GetAttribute("value"));

        // cube: array -> array -> array -> number, the third level an element editor.
        Assert.Equal("array", page.Find("select[aria-label='Items for input cube']").GetAttribute("value"));
        Assert.Equal("array", page.Find("select[aria-label='Inner items for input cube']").GetAttribute("value"));
        Assert.Equal("number", page.Find("select[aria-label='Inner items for element cube.[].[].[]']").GetAttribute("value"));

        // labels: array -> array -> enum, its values drawn beneath the element.
        Assert.Equal("enum", page.Find("select[aria-label='Inner items for input labels']").GetAttribute("value"));
        Assert.Equal(new[] { "low", "high" },
            page.FindAll("li.declared-field__values[data-values-for='labels.[].[]'] [data-field-enum-value]").Select(chip => chip.GetAttribute("data-field-enum-value")));

        // After an unrelated relabel, the loaded shapes publish through the
        // server validator unchanged — the depth round-trips.
        page.Find("input[aria-label='Label for input matrix']").Change("Score matrix");
        Publish(page);
        Assert.NotNull(sent);
        var cube = Inputs(sent!)[2].GetProperty("items");
        Assert.Equal("array", cube.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("number", cube.GetProperty("items").GetProperty("items").GetString());
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AnInnerArrayOfObjects_IsAuthored_AndPublishesThroughTheValidator()
    {
        var page = RenderEditor(EditorFixtures.V10Nested());
        CapturePublish();
        Navigate(page, "Inputs");

        // grid is an array of arrays of number; make its inner entries an object
        // and give that object a field.
        page.Find("select[aria-label='Inner items for input grid']").Change(WorkflowInputTypes.Object);
        page.Find("input[aria-label='New field id for grid.[].[]']").Change("score");
        page.Find("li.declared-row__fields[data-fields-for='grid.[].[]'] button.variable-chips__add").Click();
        page.Find("input[aria-label='Label for field grid.[].[].score']").Change("Score");
        page.Find("select[aria-label='Type for field grid.[].[].score']").Change(WorkflowInputTypes.Number);
        Publish(page);

        Assert.NotNull(sent);
        var grid = Inputs(sent!)[2].GetProperty("items");
        Assert.Equal("array", grid.GetProperty("type").GetString());
        var inner = grid.GetProperty("items");
        Assert.Equal("object", inner.GetProperty("type").GetString());
        Assert.Equal("score", inner.GetProperty("fields")[0].GetProperty("id").GetString());
        Assert.Equal("number", inner.GetProperty("fields")[0].GetProperty("type").GetString());
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AThreeLevelArrayOfScalars_IsAuthored_AndPublishesThroughTheValidator()
    {
        var page = RenderEditor(EditorFixtures.V10Nested());
        CapturePublish();
        Navigate(page, "Inputs");

        // grid (array of arrays of number) gains a third array level.
        page.Find("select[aria-label='Inner items for input grid']").Change(WorkflowInputTypes.Array);
        page.Find("select[aria-label='Inner items for element grid.[].[].[]']").Change(WorkflowInputTypes.Number);
        Publish(page);

        Assert.NotNull(sent);
        var grid = Inputs(sent!)[2].GetProperty("items");
        Assert.Equal("array", grid.GetProperty("type").GetString());
        Assert.Equal("array", grid.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("number", grid.GetProperty("items").GetProperty("items").GetString());
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task AnInnerEnum_TakesItsValues_AndNeedsTwo()
    {
        var page = RenderEditor(EditorFixtures.V10Nested());
        CapturePublish();
        Navigate(page, "Inputs");

        page.Find("select[aria-label='Inner items for input grid']").Change(WorkflowInputTypes.Enum);
        page.Find("input[aria-label='Add a value to grid.[].[]']").Change("low");
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Input 'grid' is an array of enums that declares 1 value; an enum needs at least two.", Refusals(page));

        page.Find("input[aria-label='Add a value to grid.[].[]']").Change("high");
        Publish(page);

        Assert.NotNull(sent);
        var grid = Inputs(sent!)[2].GetProperty("items");
        Assert.Equal("enum", grid.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal(new[] { "low", "high" },
            grid.GetProperty("items").GetProperty("values").EnumerateArray().Select(value => value.GetString()));
        Assert.True(Validated().IsValid);
    }

    [Fact]
    public async Task ADeepArrayWithNoInnerEntries_IsRefusedAtTheDesk()
    {
        var page = RenderEditor(EditorFixtures.V10Nested());
        CapturePublish();
        Navigate(page, "Inputs");

        // grid (array of arrays of number) gains a third array level with no
        // entry type — a gap the one-level desk check used to miss.
        page.Find("select[aria-label='Inner items for input grid']").Change(WorkflowInputTypes.Array);
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Input 'grid' is an array of arrays and must declare what the inner entries are.", Refusals(page));
    }

    [Fact]
    public async Task Below10_ALoadedDeepArray_IsRefusedByName()
    {
        // A draft can carry a nested array element the editor at 9 does not
        // offer. Refused by name, with the rung it needs.
        var package = EditorFixtures.V9Structured();
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult("""
                {
                  "Version": 11,
                  "Inputs": [
                    { "Id": "consult_draft", "Label": "Consult draft", "Required": true },
                    { "Id": "grid", "Label": "Grid", "Required": false, "Type": "array",
                      "Element": { "Type": "array", "Items": { "Type": "text" } } }
                  ]
                }
                """);
        var page = RenderEditor(package);
        CapturePublish();
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Input 'grid' declares structure deeper than one level, which requires specVersion 10. Use \"Upgrade to specVersion 19\" and publish.", Refusals(page));
    }
}
