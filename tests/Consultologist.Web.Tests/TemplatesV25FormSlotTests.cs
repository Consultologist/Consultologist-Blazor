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
            EditorFormatsCatalog.Catalog,
            EditorFormatsCatalog.AcceptedWith25);
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
