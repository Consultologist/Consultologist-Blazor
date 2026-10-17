using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C5 (#1011, design §§ 9, 13): a form slot may be an object with declared fields at 25.
/// The editor's content-channel select offers only the channels the slot's type and version
/// take (the validator's rule, at every version), a retype drops a channel the new type cannot
/// be filled from, and the desk says the validator's channel sentences word for word.
/// </summary>
public class TemplatesV25FormSlotTests : ClientRenderTestContext
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

    private static IReadOnlyList<string?> ChannelOptions(IRenderedComponent<Templates> page, string inputId) =>
        page.Find($"fluent-select[aria-label='Content channel for input {inputId}']").QuerySelectorAll("fluent-option")
            .Select(o => o.GetAttribute("value")).ToList();

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

    private static JsonElement Input(WorkflowPackagePublishRequest request, string id) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("inputs").EnumerateArray()
            .Single(i => i.GetProperty("id").GetString() == id);

    // ----- the rule -----

    private static WorkflowManifestReader.InputView Slot(string type, string? elementType = null, IReadOnlyList<string>? types = null) =>
        new("x", "X", false, type == "text" ? null : type,
            Items: elementType is null ? null : new WorkflowManifestReader.ElementView(elementType),
            Types: types);

    [Theory]
    [InlineData("text", null, 25, "transcript,form,image,ambient-note")]
    [InlineData("text", null, 13, "transcript,form")]
    [InlineData("text", null, 12, "")]
    [InlineData("array", "text", 25, "transcript,form,image,ambient-note")]
    [InlineData("array", null, 25, "transcript,form,image,ambient-note")]
    [InlineData("enum", null, 25, "form")]
    [InlineData("boolean", null, 17, "form")]
    [InlineData("number", null, 25, "form")]
    [InlineData("date", null, 25, "form")]
    [InlineData("object", null, 24, "")]
    [InlineData("object", null, 25, "form")]
    [InlineData("array", "object", 25, "")]
    [InlineData("array", "enum", 25, "")]
    public void ChannelsFor_IsTheValidatorsRule(string type, string? elementType, int specVersion, string expected)
    {
        var offered = WorkflowExpectedContent.ChannelsFor(Slot(type, elementType), specVersion);

        Assert.Equal(expected.Length == 0 ? Array.Empty<string>() : expected.Split(','), offered);
    }

    [Fact]
    public void AUnionSlot_TakesNoChannel()
    {
        Assert.Empty(WorkflowExpectedContent.ChannelsFor(Slot("text", null, new[] { "text", "array" }), 25));
    }

    // ----- the Inputs pane -----

    [Fact]
    public void AnObjectSlot_At25_OffersFormAlone_AndItsFieldsEditor()
    {
        var page = RenderEditor();
        Navigate(page, "Inputs");

        Assert.Equal(new[] { "", "form" }, ChannelOptions(page, "intake"));
        var select = page.Find("fluent-select[aria-label='Content channel for input intake']");
        Assert.Equal("form", select.GetAttribute("current-value"));
        Assert.Contains("From a form", select.TextContent);

        Assert.NotNull(page.Find("[data-fields-for='intake'] [data-field='intake.allergies']"));
        Assert.NotNull(page.Find("[data-fields-for='intake'] [data-field='intake.on_letrozole']"));
    }

    [Fact]
    public void ATextSlot_OffersEveryChannel_AndAnEnumOnlyForm()
    {
        var page = RenderEditor();
        Navigate(page, "Inputs");

        Assert.Equal(new[] { "", "transcript", "form", "image", "ambient-note" }, ChannelOptions(page, "notes"));
        Assert.Equal(new[] { "", "form" }, ChannelOptions(page, "kind"));
    }

    [Fact]
    public void AddingAField_ToTheFormSlot_RoundTrips_AndTheRegistryAccepts()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "Inputs");

        page.Find("input[aria-label='New field id for intake']").Change("weight_kg");
        page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "+ Field").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

        var intake = Input(sent!, "intake");
        Assert.Equal("form", intake.GetProperty("expectedContent").GetString());
        Assert.Equal(new[] { "allergies", "on_letrozole", "weight_kg" },
            intake.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("id").GetString()));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void TheFixtureItself_PublishesAccepted_WithItsFormObject()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "standards");
        page.Find(".editor-pane fluent-text-area").Change("Follow the local standards, edited.");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

        Assert.NotNull(sent);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void RetypingASlot_DropsAChannelTheNewTypeCannotTake_AndKeepsOneItCan()
    {
        var page = RenderEditor(EditorFixtures.V17ChannelAndUnion());
        CapturePublish();
        Navigate(page, "Inputs");

        // encounter_note carries ambient-note; a boolean cannot be filled from it.
        page.Find("fluent-select[aria-label='Type for input encounter_note']").Change("boolean");
        Assert.Equal("", page.Find("fluent-select[aria-label='Content channel for input encounter_note']").GetAttribute("current-value") ?? "");
        Assert.Equal(new[] { "", "form" }, ChannelOptions(page, "encounter_note"));

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        Assert.False(Input(sent!, "encounter_note").TryGetProperty("expectedContent", out _));
    }

    // ----- the desk: the validator's channel sentences, word for word -----

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private static WorkflowPackageContentResponse Reshaped(WorkflowPackageContentResponse package, Action<System.Text.Json.Nodes.JsonObject> reshape)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        reshape(root);
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    private static System.Text.Json.Nodes.JsonObject InputObject(System.Text.Json.Nodes.JsonObject root, string id) =>
        root["inputs"]!.AsArray().First(i => i!["id"]!.GetValue<string>() == id)!.AsObject();

    private IReadOnlyList<string> DeskRefusals(WorkflowPackageContentResponse package, string pendingNode)
    {
        CapturePublish();
        var page = RenderEditor(package, withCatalogue: package.SpecVersion >= 25);
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{pendingNode}";
        page.Render();
        page.Find("input[aria-label='Node label']").Change("Edited");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();
        return Refusals(page);
    }

    public static IEnumerable<object[]> DeskCases()
    {
        yield return new object[] { "form on an object with no fields (25)", 25, new Action<System.Text.Json.Nodes.JsonObject>(root => InputObject(root, "intake").Remove("fields")),
            "Input 'intake' declares expectedContent 'form' on an object with no fields; declare the fields a response fills." };
        yield return new object[] { "form on an array of objects (25)", 25, new Action<System.Text.Json.Nodes.JsonObject>(root => { var i = InputObject(root, "intake"); i["type"] = "array"; i["items"] = "object"; }),
            "Input 'intake' declares expectedContent 'form', which a slot of type 'array' cannot be filled from." };
        yield return new object[] { "transcript on an enum (25)", 25, new Action<System.Text.Json.Nodes.JsonObject>(root => InputObject(root, "kind")["expectedContent"] = "transcript"),
            "Input 'kind' declares expectedContent 'transcript', which is only for a text slot or an array of text." };
        yield return new object[] { "unknown channel (25)", 25, new Action<System.Text.Json.Nodes.JsonObject>(root => InputObject(root, "notes")["expectedContent"] = "fax"),
            "Input 'notes' declares unknown expectedContent 'fax' (accepted: transcript, form, image, ambient-note)." };
        yield return new object[] { "a channel on a union (17)", 17, new Action<System.Text.Json.Nodes.JsonObject>(root => InputObject(root, "notes")["expectedContent"] = "transcript"),
            "Input 'notes' declares expectedContent on a union type; a content channel is only for a single-type slot." };
        yield return new object[] { "form on an object (17)", 17, new Action<System.Text.Json.Nodes.JsonObject>(root => { var i = InputObject(root, "consult_draft"); i["type"] = "object"; i["expectedContent"] = "form"; i["fields"] = System.Text.Json.Nodes.JsonNode.Parse("""[ { "id": "a", "label": "A", "required": false } ]"""); }),
            "Input 'consult_draft' declares expectedContent 'form', which a slot of type 'object' cannot be filled from." };
        yield return new object[] { "image on a boolean (17)", 17, new Action<System.Text.Json.Nodes.JsonObject>(root => { var i = InputObject(root, "consult_draft"); i["type"] = "boolean"; i["expectedContent"] = "image"; }),
            "Input 'consult_draft' declares expectedContent 'image', which is only for a text slot or an array of text." };
    }

    [Theory]
    [MemberData(nameof(DeskCases))]
    public void TheDesk_SaysTheValidatorsChannelSentence(string _, int specVersion, Action<System.Text.Json.Nodes.JsonObject> reshape, string sentence)
    {
        var package = Reshaped(specVersion >= 25 ? EditorFixtures.V25() : EditorFixtures.V17ChannelAndUnion(), reshape);

        var refusals = DeskRefusals(package, specVersion >= 25 ? "assessment" : "draft-section");

        Assert.Contains(sentence, refusals);
        Assert.Null(sent);
    }

    [Fact]
    public void Below25_AnObjectSlot_AndAUnion_OfferNoChannel()
    {
        var page = RenderEditor(EditorFixtures.V17ChannelAndUnion());
        Navigate(page, "Inputs");

        // The union slot takes no channel: no select at all.
        Assert.Empty(page.FindAll("fluent-select[aria-label='Content channel for input notes']"));

        // An object at 17 takes none either.
        page.Find("fluent-select[aria-label='Type for input consult_draft']").Change("object");
        Assert.Empty(page.FindAll("fluent-select[aria-label='Content channel for input consult_draft']"));
    }
}
