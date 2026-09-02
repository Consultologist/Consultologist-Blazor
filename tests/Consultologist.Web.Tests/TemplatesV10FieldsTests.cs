using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v10 step (g), PR 1 (#498): the fields editor recurses. A field may be an
/// object or an array at 10 and draws its own editor beneath it, to any
/// depth; below 10 the shapes are not offered and a loaded one is refused by
/// name; the desk's sentences spell the path.
/// </summary>
public class TemplatesV10FieldsTests : ClientRenderTestContext
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

    private static IEnumerable<string?> Options(IRenderedComponent<Templates> page, string aria) =>
        page.Find($"select[aria-label='{aria}']").QuerySelectorAll("option").Select(option => option.GetAttribute("value"));

    [Fact]
    public void ANestedDeclaration_DrawsAnEditorAtEveryLevel()
    {
        var page = RenderEditor(EditorFixtures.V10Nested());
        Navigate(page, "Inputs");

        // The array-of-object's fields, and beneath them the array field's
        // element picker and the object field's own editor.
        Assert.NotNull(page.Find("li.declared-row__fields[data-fields-for='family_history']"));
        Assert.Equal("text", page.Find("select[aria-label='Items for field family_history.conditions']").GetAttribute("value"));
        Assert.NotNull(page.Find("li.declared-row__fields[data-fields-for='family_history.contact']"));
        Assert.NotNull(page.Find("li.declared-field[data-field='family_history.contact.phone']"));
        Assert.Equal(new[] { "phone", "email" },
            page.FindAll("li.declared-field__values[data-values-for='family_history.contact.preferred'] [data-field-enum-value]").Select(chip => chip.GetAttribute("data-field-enum-value")));

        // An array of arrays shows its inner element.
        Assert.Equal("array", page.Find("select[aria-label='Items for input grid']").GetAttribute("value"));
        Assert.Equal("number", page.Find("select[aria-label='Inner items for input grid']").GetAttribute("value"));
    }

    [Fact]
    public void At10_AFieldMayBeStructure_AndBelowItMayNot()
    {
        var at10 = RenderEditor(EditorFixtures.V10Nested());
        Navigate(at10, "Inputs");
        Assert.Contains("object", Options(at10, "Type for field family_history.relative"));
        Assert.Contains("array", Options(at10, "Type for field family_history.relative"));
        Assert.Contains("array", Options(at10, "Items for input grid"));

        var at9 = RenderEditor(EditorFixtures.V9Structured());
        Navigate(at9, "Inputs");
        Assert.DoesNotContain("object", Options(at9, "Type for field patient.age"));
        Assert.DoesNotContain("array", Options(at9, "Type for field patient.age"));
        Assert.DoesNotContain("array", Options(at9, "Items for input labs"));
    }

    [Fact]
    public void AuthoringAField_BelowAField_PublishesThroughTheValidator()
    {
        var page = RenderEditor(EditorFixtures.V10Nested());
        CapturePublish();
        Navigate(page, "Inputs");

        page.Find("input[aria-label='New field id for family_history.contact']").Change("email");
        page.Find("li.declared-row__fields[data-fields-for='family_history.contact'] button.variable-chips__add").Click();
        page.Find("input[aria-label='Label for field family_history.contact.email']").Change("Email address");
        page.Find("input[aria-label='Required for field family_history.contact.email']").Change(false);
        Publish(page);

        Assert.NotNull(sent);
        var contact = Inputs(sent!)[1].GetProperty("fields")[2].GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal(new[] { "phone", "preferred", "email" }, contact.Select(f => f.GetProperty("id").GetString()));
        Assert.Equal("Email address", contact[2].GetProperty("label").GetString());
        Assert.False(contact[2].GetProperty("required").GetBoolean());
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task AFieldRetypedToStructure_StartsEmpty_AndTheDeskSpellsThePath()
    {
        var page = RenderEditor(EditorFixtures.V10Nested());
        CapturePublish();
        Navigate(page, "Inputs");

        // Explicit initialisation at every level: an object field starts
        // with no fields, an array field with no entry type.
        page.Find("select[aria-label='Type for field family_history.contact.phone']").Change(WorkflowInputTypes.Object);
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Input 'family_history' field 'contact.phone' is an object and must declare at least one field.", Refusals(page));

        page.Find("input[aria-label='New field id for family_history.contact.phone']").Change("number");
        page.Find("li.declared-row__fields[data-fields-for='family_history.contact.phone'] button.variable-chips__add").Click();
        page.Find("select[aria-label='Type for field family_history.contact.phone.number']").Change(WorkflowInputTypes.Array);
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Input 'family_history' field 'contact.phone.number' is an array and must declare what its entries are.", Refusals(page));

        page.Find("select[aria-label='Items for field family_history.contact.phone.number']").Change(WorkflowInputTypes.Array);
        Publish(page);
        Assert.Contains("Input 'family_history' field 'contact.phone.number' is an array of arrays and must declare what the inner entries are.", Refusals(page));

        page.Find("select[aria-label='Inner items for field family_history.contact.phone.number']").Change(WorkflowInputTypes.Number);
        Publish(page);

        Assert.NotNull(sent);
        var phone = Inputs(sent!)[1].GetProperty("fields")[2].GetProperty("fields")[0];
        Assert.Equal("object", phone.GetProperty("type").GetString());
        var number = phone.GetProperty("fields")[0];
        Assert.Equal("array", number.GetProperty("type").GetString());
        Assert.Equal("number", number.GetProperty("items").GetProperty("items").GetString());
        var result = Validated();
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task ANestedEnum_TakesItsValuesByPath_AndNeedsTwo()
    {
        var page = RenderEditor(EditorFixtures.V10Nested());
        CapturePublish();
        Navigate(page, "Inputs");

        page.Find("li.declared-field__values[data-values-for='family_history.contact.preferred'] button[title='Remove value']").Click();
        Publish(page);
        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Input 'family_history' field 'contact.preferred' declares 1 enum value; an enum needs at least two.", Refusals(page));

        page.Find("input[aria-label='Add a value to family_history.contact.preferred']").Change("post");
        Publish(page);

        Assert.NotNull(sent);
        var preferred = Inputs(sent!)[1].GetProperty("fields")[2].GetProperty("fields")[1];
        Assert.Equal(new[] { "email", "post" }, preferred.GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        Assert.True(Validated().IsValid);
    }

    [Fact]
    public async Task Below10_ALoadedNestedShape_IsRefusedByName()
    {
        // A draft can carry what the editor at 9 does not offer — a reload of
        // a v10 draft onto a v9 package. Named, with the rung it needs.
        var package = EditorFixtures.V9Structured();
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult("""
                {
                  "Version": 11,
                  "Inputs": [
                    { "Id": "consult_draft", "Label": "Consult draft", "Required": true },
                    { "Id": "patient", "Label": "Patient", "Required": true, "Type": "object",
                      "Fields": [ { "Id": "contact", "Label": "Contact", "Required": false, "Type": "object",
                                    "Fields": [ { "Id": "phone", "Label": "Phone", "Required": true } ] } ] }
                  ]
                }
                """);
        var page = RenderEditor(package);
        CapturePublish();
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("Input 'patient' declares structure deeper than one level, which requires specVersion 10. Use \"Upgrade to specVersion 12\" and publish.", Refusals(page));
    }
}
