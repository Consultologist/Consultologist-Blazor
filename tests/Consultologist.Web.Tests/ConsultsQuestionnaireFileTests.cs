using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #991: the questionnaire-file door on a form-channel object input — a bare
/// JSON object keyed by the declared field ids fills the fields for review
/// (editable), misfits are named on the field and fill nothing, and the submit
/// carries the bytes beside the value only while the value still equals the
/// import (an edited input is typed and carries none).
/// </summary>
public class ConsultsQuestionnaireFileTests : ClientRenderTestContext
{
    private const string Referral =
        "65M, newly diagnosed adenocarcinoma of the lung, stage IIIA, for consideration of chemoradiation. PMHx HTN.";

    private const string Filled = """{"allergies":["latex"],"on_letrozole":true}""";

    private static WorkflowPackageInputResponse Intake(bool formChannel = true, bool asText = false) => asText
        ? new WorkflowPackageInputResponse("intake", "Intake", false, ExpectedContent: WorkflowExpectedContent.Form)
        : new WorkflowPackageInputResponse("intake", "Intake", false, WorkflowInputTypes.Object,
            Fields: new[]
            {
                new WorkflowPackageFieldResponse("allergies", "Allergies", false, WorkflowInputTypes.Array, Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Text)),
                new WorkflowPackageFieldResponse("on_letrozole", "On letrozole", true, WorkflowInputTypes.Boolean),
            },
            ExpectedContent: formChannel ? WorkflowExpectedContent.Form : null);

    private void WithIntake(WorkflowPackageInputResponse? intake = null) =>
        WithPinnedPackage(
            blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") },
            inputs: new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true), intake ?? Intake() },
            specVersion: 25);

    private IReadOnlyDictionary<string, ConsultInputValue>? sentInputs;
    private IReadOnlyDictionary<string, InputFilePayload>? sentFormFiles;
    private bool submitted;

    private void CaptureSubmit() =>
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => { sentInputs = value; submitted = true; }),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>?>(),
                Arg.Any<IReadOnlyDictionary<string, ConsultInputFormRef>?>(),
                Arg.Any<IReadOnlyDictionary<string, bool>?>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Do<IReadOnlyDictionary<string, InputFilePayload>?>(value => sentFormFiles = value))
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

    private static IRenderedComponent<InputFile> QuestionnaireInput(IRenderedComponent<Consults> page) =>
        page.FindComponents<FluentInputFile>()
            .Single(component => component.Instance.Accept == ".json,application/json")
            .FindComponent<InputFile>();

    private static void Import(IRenderedComponent<Consults> page, string json) =>
        QuestionnaireInput(page).UploadFiles(InputFileContent.CreateFromText(json, "intake.json", contentType: "application/json"));

    private static void TypeTheDraft(IRenderedComponent<Consults> page) =>
        page.FindAll("fluent-text-area")[0].Change(Referral);

    private static void Submit(IRenderedComponent<Consults> page) =>
        page.FindAll("fluent-button").Last().Click();

    private static string Chip(IRenderedComponent<Consults> page) =>
        page.Find(".input-field__questionnaire").TextContent.Replace("Remove", string.Empty).Trim();

    [Theory]
    [InlineData("form object", true)]
    [InlineData("plain object", false)]
    [InlineData("form text", false)]
    public void TheDoor_RendersOnlyOnAFormChannelObjectInput(string shape, bool offered)
    {
        WithIntake(shape switch
        {
            "form object" => Intake(),
            "plain object" => Intake(formChannel: false),
            _ => Intake(asText: true),
        });

        var triggers = Render<Consults>().FindAll(".input-field__questionnaire-trigger");

        Assert.Equal(offered ? 1 : 0, triggers.Count);
    }

    [Fact]
    public void ImportingAFile_FillsTheFields_AndTheChipCountsThem()
    {
        WithIntake();
        CaptureSubmit();
        var page = Render<Consults>();
        TypeTheDraft(page);

        Import(page, """{ "on_letrozole": true, "allergies": ["latex"] }""");

        Assert.Contains("Questionnaire file", Chip(page));
        Assert.Contains("2 fields", Chip(page));
        Assert.DoesNotContain("edited", Chip(page));
        Assert.Empty(page.FindAll(".input-field__file-error"));
        Assert.Empty(page.FindAll(".input-field__questionnaire-trigger"));

        Submit(page);
        Assert.True(submitted);
        Assert.Equal(Filled, System.Text.Json.JsonSerializer.Serialize(sentInputs!["intake"]));
    }

    [Theory]
    [InlineData("""{ "allergies": ["latex"] }""", "That questionnaire file is missing required field 'on_letrozole'.")]
    [InlineData("""{ "allergies": ["latex"], "on_letrozole": true, "smoker": false }""", "That questionnaire file has a field 'smoker' it does not declare (fields: allergies, on_letrozole).")]
    [InlineData("{ not json", "That questionnaire file is not JSON.")]
    [InlineData("[1]", "That questionnaire file must be a JSON object; got a JSON array.")]
    [InlineData("{}", "That questionnaire file is missing required field 'on_letrozole'.")]
    public void ARefusedFile_IsNamedOnTheField_AndFillsNothing(string json, string sentence)
    {
        WithIntake();
        CaptureSubmit();
        var page = Render<Consults>();
        TypeTheDraft(page);

        Import(page, json);

        Assert.Equal(sentence, page.Find(".input-field__file-error").TextContent.Trim());
        Assert.Empty(page.FindAll(".input-field__questionnaire"));
        Submit(page);
        Assert.False(sentInputs!.ContainsKey("intake"));
        Assert.Null(sentFormFiles);
    }

    [Fact]
    public void AnOversizedFile_IsRefusedBeforeReading()
    {
        WithIntake();
        var page = Render<Consults>();

        QuestionnaireInput(page).UploadFiles(InputFileContent.CreateFromBinary(new byte[256 * 1024 + 1], "big.json", contentType: "application/json"));

        Assert.Equal("That file is larger than 256 KB.", page.Find(".input-field__file-error").TextContent.Trim());
        Assert.Empty(page.FindAll(".input-field__questionnaire"));
    }

    [Fact]
    public void TheSubmit_CarriesTheBytesBesideTheValue()
    {
        WithIntake();
        CaptureSubmit();
        var page = Render<Consults>();
        TypeTheDraft(page);
        const string json = """{ "on_letrozole": true, "allergies": ["latex"] }""";

        Import(page, json);
        Submit(page);

        var file = Assert.Contains("intake", sentFormFiles!);
        Assert.Equal("application/json", file.ContentType);
        Assert.Equal(json, System.Text.Encoding.UTF8.GetString(file.Content));
        Assert.Equal(Filled, System.Text.Json.JsonSerializer.Serialize(sentInputs!["intake"]));
    }

    [Fact]
    public void AnEditedInput_CarriesNoBytes_AndTheChipSaysSo()
    {
        WithIntake();
        CaptureSubmit();
        var page = Render<Consults>();
        TypeTheDraft(page);
        Import(page, """{ "on_letrozole": true, "allergies": ["latex"] }""");

        // The boolean member is the group's one select; flip it.
        page.Find(".input-field__group fluent-select.node-field__input").Change("false");

        Assert.Contains("edited", Chip(page));
        Submit(page);
        Assert.Null(sentFormFiles);
        Assert.Equal("""{"allergies":["latex"],"on_letrozole":false}""", System.Text.Json.JsonSerializer.Serialize(sentInputs!["intake"]));
    }

    [Fact]
    public void Remove_ClearsTheFillAndTheFields()
    {
        WithIntake();
        CaptureSubmit();
        var page = Render<Consults>();
        TypeTheDraft(page);
        Import(page, """{ "on_letrozole": true, "allergies": ["latex"] }""");

        page.Find(".input-field__questionnaire fluent-button").Click();

        Assert.Empty(page.FindAll(".input-field__questionnaire"));
        Assert.Single(page.FindAll(".input-field__questionnaire-trigger"));
        Submit(page);
        Assert.False(sentInputs!.ContainsKey("intake"));
        Assert.Null(sentFormFiles);
    }

    [Fact]
    public void AnUnusedDoor_SendsNoFormFiles()
    {
        WithIntake();
        CaptureSubmit();
        var page = Render<Consults>();
        TypeTheDraft(page);

        Submit(page);

        Assert.True(submitted);
        Assert.Null(sentFormFiles);
    }
}
