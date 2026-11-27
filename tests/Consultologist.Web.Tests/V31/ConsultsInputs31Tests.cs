using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using static Consultologist.Web.Tests.V31.Package31Fixtures;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 9 (#1509): the Create page draws a specVersion 31 package's inputs from their formats (part 09 § 5.1, part 02
/// §§ 8.1, 9, 10; D2, D3, D4, R4). A text input keeps today's slot (documents, earlier runs, snippets, chips); the data
/// widgets are <c>Input31</c>.
/// </summary>
public class ConsultsInputs31Tests : ClientRenderTestContext
{
    private const string Draft = "Margaret Okafor, 58, referred for fatigue and a raised TSH (fictional).";

    private IReadOnlyDictionary<string, ConsultInputValue>? sent;
    private IReadOnlyDictionary<string, bool>? sentMacroChoices;
    private IReadOnlyDictionary<string, InputFilePayload>? sentFormFiles;
    private IReadOnlyDictionary<string, ConsultInputFormRef>? sentFormRefs;
    private bool submitted;

    private void Pin(params WorkflowPackageInputResponse[] inputs) => Pin(Package(inputs));

    private void Pin(WorkflowPackageResponse package, WorkflowPackageContentResponse? content = null)
    {
        WorkflowService.GetCurrentPackageAsync().Returns(package);
        if (content is null)
        {
            WorkflowService.GetCurrentPackageContentAsync()
                .Returns<Task<WorkflowPackageContentResponse>>(_ => throw new InvalidOperationException("content unavailable"));
        }
        else
        {
            WorkflowService.GetCurrentPackageContentAsync().Returns(content);
        }

        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => { sent = value; submitted = true; }),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>?>(),
                Arg.Do<IReadOnlyDictionary<string, ConsultInputFormRef>?>(value => sentFormRefs = value),
                Arg.Do<IReadOnlyDictionary<string, bool>?>(value => sentMacroChoices = value),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Do<IReadOnlyDictionary<string, InputFilePayload>?>(value => sentFormFiles = value),
                Arg.Any<string?>())
            .Returns(new ConsultGenerationJobStartResponse("0123456789abcdef0123456789abcdef", "Scheduled"));
    }

    private static IElement Start(IRenderedComponent<Consults> page) => page.FindAll("fluent-button").Last();

    private static IElement Slot(IRenderedComponent<Consults> page, string id) => page.Find($"[data-input-id='{id}']");

    private static string Problem(IRenderedComponent<Consults> page, string id) =>
        string.Join(" ", Slot(page, id).QuerySelectorAll(".input31__problem").Select(p => p.TextContent.Trim()));

    private static IElement Field(IRenderedComponent<Consults> page, string id, string fieldId) =>
        Slot(page, id).QuerySelector($"[data-field-id='{fieldId}']")!;

    private static string Group(params (string Id, string Schema)[] properties) =>
        "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
        + string.Join(",", properties.Select(p => "\"" + p.Id + "\":" + p.Schema))
        + "},\"required\":[" + string.Join(",", properties.Select(p => "\"" + p.Id + "\"")) + "]";

    private static string Closed(string group) => group + "}";

    private static string WithDefs(string group, params string[] builtins) =>
        group + ",\"$defs\":{" + string.Join(",", builtins.Select(name => "\"" + name + "\":" + Def(name))) + "}}";

    [Fact]
    public void A31Package_DrawsItsInputsInWrittenOrder_WithLabelAndHelpText()
    {
        Pin(Package(
                Input("consult_draft", "Consult draft", true, MarkdownBlock),
                Input("visit_type", "Visit type", true, """{"type":"string","oneOf":[{"const":"new_patient","title":"New patient"},{"const":"follow_up","title":"Follow-up"}]}"""),
                Input("include_billing", "Include the billing section", false, """{"type":"boolean"}""")),
            Content(
                ("consult_draft", ["Paste the referral as written.", "Leave out the patient's address."]),
                ("visit_type", null),
                ("include_billing", ["Adds a billing section to the note."])));

        var page = Render<Consults>();

        Assert.Equal(["consult_draft", "visit_type", "include_billing"],
            page.FindAll("[data-input-id]").Select(slot => slot.GetAttribute("data-input-id")));
        Assert.Contains("Consult draft", Slot(page, "consult_draft").TextContent);
        Assert.Contains("Visit type", Slot(page, "visit_type").TextContent);
        Assert.Contains("Include the billing section", Slot(page, "include_billing").TextContent);

        Assert.Equal("Paste the referral as written.\nLeave out the patient's address.",
            Slot(page, "consult_draft").QuerySelector(".input-field__help")!.TextContent);
        Assert.Null(Slot(page, "visit_type").QuerySelector(".input-field__help"));
        Assert.Equal("Adds a billing section to the note.",
            Slot(page, "include_billing").QuerySelector(".input-field__help")!.TextContent);
    }

    [Fact]
    public void WithoutTheContent_NoHelpTextIsShown()
    {
        Pin(Input("consult_draft", "Consult draft", true, MarkdownBlock));

        var page = Render<Consults>();

        Assert.Empty(page.FindAll(".input-field__help"));
        Assert.Contains("Consult draft", Slot(page, "consult_draft").TextContent);
    }

    [Fact]
    public void AString_IsASingleLineField()
    {
        Pin(Input("referrer", "Referring clinician", true, """{"type":"string"}"""));

        var page = Render<Consults>();

        var slot = Slot(page, "referrer");
        Assert.Empty(slot.QuerySelectorAll("fluent-text-area"));
        slot.QuerySelector("input[type=text]")!.Change("Dr A. Sample");
        Start(page).Click();

        Assert.Equal("Dr A. Sample", sent!["referrer"].Text);
    }

    [Fact]
    public void Plaintext_IsATextArea()
    {
        Pin(Input("referral", "Referral", true, Plaintext));

        var page = Render<Consults>();

        Slot(page, "referral").QuerySelector("fluent-text-area")!.Change(Draft);
        Start(page).Click();

        Assert.Equal(Draft, sent!["referral"].Text);
    }

    [Fact]
    public void AnEnum_ShowsBareValues()
    {
        Pin(Input("side", "Side", true, """{"type":"string","enum":["left","right"]}"""));

        var page = Render<Consults>();

        var select = Slot(page, "side").QuerySelector("fluent-select")!;
        Assert.Equal(["choose…", "left", "right"], select.QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()));
        select.Change("right");
        Start(page).Click();

        Assert.Equal("right", sent!["side"].Text);
    }

    [Fact]
    public void ALabelledChoice_ShowsTitles_AndSendsTheConst()
    {
        Pin(Input("visit_type", "Visit type", true,
            """{"type":"string","oneOf":[{"const":"new_patient","title":"New patient"},{"const":"follow_up","title":"Follow-up"}]}"""));

        var page = Render<Consults>();

        var select = Slot(page, "visit_type").QuerySelector("fluent-select")!;
        Assert.Equal(["choose…", "New patient", "Follow-up"], select.QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()));
        select.Change("follow_up");
        Start(page).Click();

        Assert.Equal("follow_up", sent!["visit_type"].Text);
    }

    [Fact]
    public void ANullableLabelledChoice_OffersNoNullOption()
    {
        Pin(Input("visit_type", "Visit type", false,
            """{"type":["string","null"],"oneOf":[{"const":"new_patient","title":"New patient"},{"const":"follow_up","title":"Follow-up"},{"const":null}]}"""));

        var page = Render<Consults>();

        var select = Slot(page, "visit_type").QuerySelector("fluent-select")!;
        Assert.Equal(["choose…", "New patient", "Follow-up"], select.QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()));
    }

    [Fact]
    public void ADate_IsADatePicker()
    {
        Pin(Input("seen_on", "Date seen", true, Date));

        var page = Render<Consults>();

        Slot(page, "seen_on").QuerySelector("input[type=date]")!.Change("2026-11-16");
        Start(page).Click();

        Assert.Equal("2026-11-16", sent!["seen_on"].Text);
    }

    [Fact]
    public void ABoolean_StartsUnset_AndIsSentOnlyWhenYesOrNo()
    {
        Pin(
            Input("consult_draft", "Consult draft", true, MarkdownBlock),
            Input("include_billing", "Include the billing section", false, """{"type":"boolean"}"""));

        var page = Render<Consults>();
        var select = Slot(page, "include_billing").QuerySelector("fluent-select")!;
        Assert.Equal(["choose…", "Yes", "No"], select.QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()));
        Slot(page, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);
        Start(page).Click();

        Assert.True(submitted);
        Assert.False(sent!.ContainsKey("include_billing"));

        JobSession.Current = null;
        var again = Render<Consults>();
        Slot(again, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);
        Slot(again, "include_billing").QuerySelector("fluent-select")!.Change("true");
        Start(again).Click();

        Assert.True(sent!["include_billing"].IsBoolean);
        Assert.True(sent["include_billing"].Flag);
    }

    [Fact]
    public void ARequiredBooleanLeftUnset_KeepsStartDisabled()
    {
        Pin(Input("on_letrozole", "On letrozole", true, """{"type":"boolean"}"""));

        var page = Render<Consults>();

        Assert.True(Start(page).HasAttribute("disabled"));
        Assert.Equal("Required.", Problem(page, "on_letrozole"));

        Slot(page, "on_letrozole").QuerySelector("fluent-select")!.Change("false");
        Assert.False(Start(page).HasAttribute("disabled"));
        Start(page).Click();

        Assert.False(sent!["on_letrozole"].Flag);
    }

    [Fact]
    public void ANumberAndAnInteger_AreNumberFields_AndSendNumbers()
    {
        Pin(
            Input("dose", "Dose (mg)", true, """{"type":"number"}"""),
            Input("cycles", "Cycles", true, """{"type":"integer"}"""));

        var page = Render<Consults>();

        var dose = Slot(page, "dose").QuerySelector("input")!;
        var cycles = Slot(page, "cycles").QuerySelector("input")!;
        Assert.Equal("decimal", dose.GetAttribute("inputmode"));
        Assert.Equal("numeric", cycles.GetAttribute("inputmode"));

        dose.Change("1.50");
        Slot(page, "cycles").QuerySelector("input")!.Change("1.5");
        Assert.Equal("Must be a whole number, like 12.", Problem(page, "cycles"));
        Assert.True(Start(page).HasAttribute("disabled"));

        Slot(page, "cycles").QuerySelector("input")!.Change("12");
        Start(page).Click();

        Assert.True(sent!["dose"].IsNumber);
        Assert.Equal("1.50", sent["dose"].Number);
        Assert.True(sent["cycles"].IsNumber);
        Assert.Equal("12", sent["cycles"].Number);
    }

    [Fact]
    public void AnObject_IsAFieldGroup_LabelledByTitles_ToAnyDepth()
    {
        var address = Closed(Group(
            ("city", """{"type":"string","title":"City"}"""),
            ("seen", """{"$ref":"urn:consultologist:spec:31:format:date","title":"Seen on"}""")))
            .Replace("{\"type\":\"object\"", "{\"title\":\"Address\",\"type\":\"object\"");
        Pin(Input("patient", "Patient", true, WithDefs(Group(
            ("name", """{"type":"string","title":"Name"}"""),
            ("address", address)), "date")));

        var page = Render<Consults>();

        Assert.Equal(["Name", "Address", "City", "Seen on"],
            Slot(page, "patient").QuerySelectorAll(".input31__field-label").Select(label => label.TextContent.Replace("(optional)", "").Trim()));
        Assert.NotNull(Field(page, "patient", "address").QuerySelector(".input31__group"));

        Field(page, "patient", "name").QuerySelector("input[type=text]")!.Change("Margaret Okafor");
        Field(page, "patient", "city").QuerySelector("input[type=text]")!.Change("Sampletown");
        Field(page, "patient", "seen").QuerySelector("input[type=date]")!.Change("2026-11-16");
        Start(page).Click();

        var patient = sent!["patient"];
        Assert.Equal(["name", "address"], patient.Fields!.Select(f => f.Id));
        Assert.Equal("Margaret Okafor", patient.Fields[0].Value.Text);
        Assert.Equal(["city", "seen"], patient.Fields[1].Value.Fields!.Select(f => f.Id));
        Assert.Equal("2026-11-16", patient.Fields[1].Value.Fields![1].Value.Text);
    }

    private static string Medicine() => WithDefs(Group(
        ("name", """{"type":"string","title":"Name"}"""),
        ("notes", NullableRef("markdown_block", "Notes")),
        ("dose", """{"type":["number","null"],"title":"Dose"}""")), "markdown_block");

    [Fact]
    public void ABlankNullableField_IsSentAsNull()
    {
        Pin(Input("medicine", "Medicine", true, Medicine()));

        var page = Render<Consults>();

        Assert.NotNull(Field(page, "medicine", "notes").QuerySelector("fluent-text-area"));
        Field(page, "medicine", "name").QuerySelector("input[type=text]")!.Change("Letrozole");
        Start(page).Click();

        var medicine = sent!["medicine"];
        Assert.Equal(["name", "notes", "dose"], medicine.Fields!.Select(f => f.Id));
        Assert.Equal("Letrozole", medicine.Fields[0].Value.Text);
        Assert.True(medicine.Fields[1].Value.IsNull);
        Assert.True(medicine.Fields[2].Value.IsNull);
    }

    [Fact]
    public void ABlankFieldThatIsNotNullable_KeepsStartDisabled()
    {
        Pin(Input("medicine", "Medicine", true, Medicine()));

        var page = Render<Consults>();
        Field(page, "medicine", "dose").QuerySelector("input")!.Change("2.5");

        Assert.True(Start(page).HasAttribute("disabled"));
        Assert.Equal("Required.", Field(page, "medicine", "name").QuerySelector(".input31__problem")!.TextContent.Trim());
        Assert.Null(Field(page, "medicine", "notes").QuerySelector(".input31__problem"));

        Field(page, "medicine", "name").QuerySelector("input[type=text]")!.Change("Letrozole");
        Assert.False(Start(page).HasAttribute("disabled"));
    }

    [Fact]
    public void AnUntouchedOptionalObject_IsLeftOut()
    {
        Pin(
            Input("consult_draft", "Consult draft", true, MarkdownBlock),
            Input("medicine", "Medicine", false, Medicine()));

        var page = Render<Consults>();
        Slot(page, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);

        Assert.False(Start(page).HasAttribute("disabled"));
        Start(page).Click();
        Assert.False(sent!.ContainsKey("medicine"));
    }

    [Fact]
    public void AnArrayOfObjects_IsRepeatingRows()
    {
        Pin(Input("medicines", "Medicines", true, "{\"type\":\"array\",\"items\":" + Closed(Group(
            ("drug", """{"type":"string","title":"Drug"}"""),
            ("dose", """{"type":["number","null"],"title":"Dose"}"""))) + "}"));

        var page = Render<Consults>();
        Assert.Empty(Slot(page, "medicines").QuerySelectorAll(".input31__row"));
        Assert.True(Start(page).HasAttribute("disabled"));

        // A Remove or an Add whose button the re-render drops must not reach a <label> (AGENTS.md).
        Assert.True(Slot(page, "medicines").QuerySelector(".input31__add-wrap")!.HasAttribute("blazor:onclick:preventdefault"));
        Slot(page, "medicines").QuerySelector("fluent-button.input31__add")!.Click();
        Slot(page, "medicines").QuerySelector("fluent-button.input31__add")!.Click();
        Assert.Equal(["1.", "2."], Slot(page, "medicines").QuerySelectorAll(".input31__row-number").Select(n => n.TextContent.Trim()));
        Assert.All(Slot(page, "medicines").QuerySelectorAll(".input31__row-buttons"),
            buttons => Assert.True(buttons.HasAttribute("blazor:onclick:preventdefault")));

        // An empty row holds the run.
        Assert.True(Start(page).HasAttribute("disabled"));
        Slot(page, "medicines").QuerySelectorAll(".input31__row")[1].QuerySelector("fluent-button[aria-label='Remove entry 2']")!.Click();
        Assert.Single(Slot(page, "medicines").QuerySelectorAll(".input31__row"));

        Slot(page, "medicines").QuerySelector("[data-field-id='drug'] input[type=text]")!.Change("Letrozole");
        Start(page).Click();

        var row = Assert.Single(sent!["medicines"].Elements!);
        Assert.Equal(["drug", "dose"], row.Fields!.Select(f => f.Id));
        Assert.Equal("Letrozole", row.Fields[0].Value.Text);
        Assert.True(row.Fields[1].Value.IsNull);
    }

    [Fact]
    public void ABlankOptionalText_IsLeftOut_NeverSentAsEmptyText()
    {
        Pin(
            Input("consult_draft", "Consult draft", true, MarkdownBlock),
            Input("notes", "Notes", false, Plaintext),
            Input("referrer", "Referring clinician", false, """{"type":"string"}"""));

        var page = Render<Consults>();
        Slot(page, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);
        Slot(page, "notes").QuerySelector("fluent-text-area")!.Change("   ");
        Start(page).Click();

        Assert.Equal(["consult_draft"], sent!.Keys);
    }

    [Fact]
    public void ARequiredTextLeftBlank_KeepsStartDisabled_AndSaysRequired()
    {
        Pin(Input("consult_draft", "Consult draft", true, MarkdownBlock));

        var page = Render<Consults>();

        Assert.True(Start(page).HasAttribute("disabled"));
        Assert.Equal("Required.", Slot(page, "consult_draft").QuerySelector(".input-field__required-hint")!.TextContent.Trim());
        Slot(page, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);
        Assert.False(Start(page).HasAttribute("disabled"));
    }

    [Fact]
    public void A31Package_HasNoMacroPanel()
    {
        Pin(Package(Input("consult_draft", "Consult draft", true, MarkdownBlock))
            with { Macros = [new WorkflowPackageMacroResponse("safety_netting", "Safety-netting advice", true)] });

        var page = Render<Consults>();

        Assert.Empty(page.FindAll(".macro-choices"));
    }

    [Fact]
    public void AnOptionalBooleanFromAMigratedMacro_TravelsInInputs_AndMacroChoicesIsNull()
    {
        Pin(
            Input("consult_draft", "Consult draft", true, MarkdownBlock),
            Input("include_safety_netting", "Safety-netting advice", false, """{"type":"boolean"}"""));

        var page = Render<Consults>();
        Assert.Empty(page.FindAll(".macro-choices"));
        Slot(page, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);
        Slot(page, "include_safety_netting").QuerySelector("fluent-select")!.Change("true");
        Start(page).Click();

        Assert.True(submitted);
        Assert.True(sent!["include_safety_netting"].Flag);
        Assert.Null(sentMacroChoices);
    }

    [Theory]
    [InlineData("transcript", "Transcript")]
    [InlineData("image", "Image")]
    [InlineData("ambient-note", "Ambient note")]
    public void AChannel_ShowsItsChip_AndDoesWhatExpectedContentDid(string channel, string chip)
    {
        Pin(Input("encounter", "Encounter", true, Plaintext, channel));

        var page = Render<Consults>();

        var slot = Slot(page, "encounter");
        Assert.Equal(chip, slot.QuerySelector(".input-field__channel")!.TextContent.Trim());
        Assert.NotNull(slot.QuerySelector("input[type=file]"));
        Assert.NotNull(slot.QuerySelector("fluent-text-area"));
    }

    // The Api fills the 30 members beside Format (part 09 § 4.3); the questionnaire file is read against them.
    private static WorkflowPackageInputResponse Intake() =>
        Input("intake", "Intake questionnaire", false, Closed(Group(
                ("on_letrozole", """{"type":"boolean","title":"On letrozole"}"""),
                ("allergies", """{"type":["string","null"],"title":"Allergies"}"""))), WorkflowExpectedContent.Form)
            with
            {
                Type = WorkflowInputTypes.Object,
                Fields =
                [
                    new WorkflowPackageFieldResponse("on_letrozole", "On letrozole", true, WorkflowInputTypes.Boolean),
                    new WorkflowPackageFieldResponse("allergies", "Allergies", false),
                ],
            };

    private static void ImportQuestionnaire(IRenderedComponent<Consults> page, string json) =>
        page.FindComponents<Microsoft.FluentUI.AspNetCore.Components.FluentInputFile>()
            .Single(component => component.Instance.Accept == ".json,application/json")
            .FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(json, "intake.json", contentType: "application/json"));

    [Fact]
    public void AFormChannel_ShowsItsChip_AndOffersTheQuestionnaireFile()
    {
        Pin(Input("consult_draft", "Consult draft", true, MarkdownBlock), Intake());

        var page = Render<Consults>();

        var slot = Slot(page, "intake");
        Assert.Equal("From a form", slot.QuerySelector(".input-field__channel")!.TextContent.Trim());
        Assert.NotNull(slot.QuerySelector(".input-field__questionnaire-trigger"));
        Assert.Equal(["On letrozole", "Allergies"],
            slot.QuerySelectorAll(".input31__field-label").Select(label => label.TextContent.Replace("(optional)", "").Trim()));

        ImportQuestionnaire(page, """{ "on_letrozole": true }""");
        Assert.Empty(page.FindAll(".input-field__file-error"));
        Assert.Contains("Questionnaire file", Slot(page, "intake").QuerySelector(".input-field__questionnaire")!.TextContent);
        Assert.DoesNotContain("edited", Slot(page, "intake").QuerySelector(".input-field__questionnaire")!.TextContent);
        Slot(page, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);
        Start(page).Click();

        // The 31 start fills a left-out input from its file ("if left out, the form fills it"); a value sent beside
        // the file would have to equal the file's own object, which { on_letrozole } beside { on_letrozole, allergies: null } is not.
        Assert.True(submitted);
        Assert.False(sent!.ContainsKey("intake"));
        Assert.Equal("application/json", sentFormFiles!["intake"].ContentType);
    }

    [Fact]
    public void AQuestionnaireFillEditedAfterTheImport_IsSentTyped_WithoutTheFile()
    {
        Pin(Input("consult_draft", "Consult draft", true, MarkdownBlock), Intake());

        var page = Render<Consults>();
        ImportQuestionnaire(page, """{ "on_letrozole": true }""");
        Field(page, "intake", "allergies").QuerySelector("input[type=text]")!.Change("latex");
        Slot(page, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);
        Start(page).Click();

        var intake = sent!["intake"];
        Assert.True(intake.Fields![0].Value.Flag);
        Assert.Equal("latex", intake.Fields[1].Value.Text);
        Assert.True(sentFormFiles is null || !sentFormFiles.ContainsKey("intake"));
    }

    private static readonly DateTimeOffset Submitted = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

    private IRenderedComponent<Consults> FilledFromAFormResponse()
    {
        // The Api fills the 30 members beside Format; a held response is read against them.
        Pin(
            Input("consult_draft", "Consult draft", true, MarkdownBlock),
            Input("visit_type", "Visit type", false,
                    """{"type":"string","oneOf":[{"const":"new_patient","title":"New patient"},{"const":"follow_up","title":"Follow-up"}]}""")
                with { Type = WorkflowInputTypes.Enum, Values = ["new_patient", "follow_up"] });
        FormsService.ListResponsesAsync().Returns(new[]
        {
            new Consultologist.Web.Services.Forms.FormResponseListRow("triage-intake", "17", Submitted, new[] { "visit_type" }, null),
        });
        FormsService.GetResponseAsync("triage-intake", "17").Returns(new Consultologist.Web.Services.Forms.FormResponseValues(
            "triage-intake", "17", Submitted, new Dictionary<string, string> { ["visit_type"] = "follow_up" }));

        var page = Render<Consults>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".form-picker__trigger")));
        page.Find(".form-picker__trigger").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".form-picker__choose")));
        page.FindAll(".form-picker__choose").First(b => !b.HasAttribute("disabled")).Click();
        Slot(page, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);
        return page;
    }

    [Fact]
    public void AnInputFilledFromAFormResponse_SendsTheReference_AndNoValueBesideIt()
    {
        var page = FilledFromAFormResponse();
        Start(page).Click();

        Assert.True(submitted);
        Assert.False(sent!.ContainsKey("visit_type"));
        Assert.Equal(new ConsultInputFormRef("triage-intake", "17"), sentFormRefs!["visit_type"]);
    }

    [Fact]
    public void AFormResponseFillEditedAfterwards_IsSentTyped_WithoutTheReference()
    {
        var page = FilledFromAFormResponse();
        Slot(page, "visit_type").QuerySelector("fluent-select")!.Change("new_patient");
        Start(page).Click();

        Assert.Equal("new_patient", sent!["visit_type"].Text);
        Assert.True(sentFormRefs is null || !sentFormRefs.ContainsKey("visit_type"));
    }

    [Fact]
    public void ADataInputWithAChannel_IsStillInput31_AndShowsTheChip()
    {
        // Publish refuses this (part 02 § 8.3); the page still draws and sends what it edits.
        Pin(Input("seen_on", "Date seen", true, Date, "transcript"));

        var page = Render<Consults>();

        var slot = Slot(page, "seen_on");
        Assert.Equal("Transcript", slot.QuerySelector(".input-field__channel")!.TextContent.Trim());
        Assert.NotNull(slot.QuerySelector(".input31"));
        slot.QuerySelector("input[type=date]")!.Change("2026-11-16");
        Start(page).Click();

        Assert.Equal("2026-11-16", sent!["seen_on"].Text);
    }

    [Fact]
    public void ABlankRow_SaysItIsEmpty_WhenNothingElseWould()
    {
        Pin(Input("notes", "Notes", false, "{\"type\":\"array\",\"items\":" + Closed(Group(
            ("note", """{"type":["string","null"],"title":"Note"}"""))) + "}"));

        var page = Render<Consults>();
        Slot(page, "notes").QuerySelector("fluent-button.input31__add")!.Click();

        Assert.True(Start(page).HasAttribute("disabled"));
        Assert.Equal("Row 1 is empty; fill it in or remove it.", Problem(page, "notes"));
    }

    [Fact]
    public void ABlankSubGroupWhoseFieldsAreAllNullable_SaysRequired()
    {
        var sub = Closed(Group(("x", """{"type":["string","null"],"title":"Detail"}""")))
            .Replace("{\"type\":\"object\"", "{\"title\":\"Details\",\"type\":\"object\"");
        Pin(Input("visit", "Visit", false, Closed(Group(
            ("where", """{"type":["string","null"],"title":"Where"}"""),
            ("details", sub)))));

        var page = Render<Consults>();
        Field(page, "visit", "where").QuerySelector("input[type=text]")!.Change("Sample Oncology Clinic");

        Assert.True(Start(page).HasAttribute("disabled"));
        Assert.Equal("Required.", Field(page, "visit", "details").QuerySelector(".input31__problem")!.TextContent.Trim());
    }

    [Fact]
    public void ATextMemento_IsRestoredIntoADataInput_WhenItReadsAsItsValue()
    {
        const string JobId = "0123456789abcdef0123456789abcdef";
        Pin(
            Input("consult_draft", "Consult draft", true, MarkdownBlock),
            Input("seen_on", "Date seen", false, Date),
            Input("on_letrozole", "On letrozole", false, """{"type":"boolean"}"""),
            Input("medicines", "Medicines", false, "{\"type\":\"array\",\"items\":" + Closed(Group(
                ("drug", """{"type":"string","title":"Drug"}"""))) + "}"),
            Input("visit", "Visit", false, Closed(Group(("where", """{"type":"string","title":"Where"}""")))));
        JobSession.Current = new ConsultJobMemento(
            JobId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["consult_draft"] = Draft,
                ["seen_on"] = "2026-11-16",
                ["on_letrozole"] = "true",
                ["medicines"] = """[{"drug":"Letrozole"},{"drug":"Anastrozole"}]""",
                ["visit"] = "Where: Sample Oncology Clinic",
            },
            [new ConsultJobBlock("node.plan", "Planning the sections")]);
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed", TotalBlockCount: 1, CompletedBlockCount: 1, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string> { ["node.plan"] = "Section prose." },
            FailedBlocks: new Dictionary<string, string>(), Success: true, AssembledDocument: "The note."));

        var page = Render<Consults>();
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Edit inputs")).Click();
        Start(page).Click();

        Assert.Equal("2026-11-16", sent!["seen_on"].Text);
        Assert.True(sent["on_letrozole"].Flag);
        Assert.Equal(["Letrozole", "Anastrozole"], sent["medicines"].Elements!.Select(row => row.Fields![0].Value.Text));
        // Text that is not the group's value leaves it blank, never half-filled.
        Assert.False(sent.ContainsKey("visit"));
    }

    [Fact]
    public void ATextInput_StillTakesADocument_AnEarlierRun_AndASnippet()
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active",
            new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            Array.Empty<AccountIdentity>()));
        AccountService.GetSettingAsync(Snippets.SettingKey).Returns(
            new AccountSettingResponse(Snippets.SettingKey, SnippetsModelTests.Wire, Snippets.ContentType, DateTimeOffset.UtcNow));
        Pin(
            Input("consult_draft", "Consult draft", true, MarkdownBlock),
            Input("prior_letters", "Earlier letters", false, "{\"type\":\"array\",\"items\":" + Ref("plaintext")
                + ",\"$defs\":{\"plaintext\":" + Def("plaintext") + "}}"));

        var page = Render<Consults>();

        var draft = Slot(page, "consult_draft");
        Assert.NotNull(draft.QuerySelector("fluent-text-area"));
        Assert.NotNull(draft.QuerySelector("input[type=file]"));
        Assert.NotNull(draft.QuerySelector(".run-picker"));
        Assert.NotNull(draft.QuerySelector(".snippet-picker"));

        var letters = Slot(page, "prior_letters");
        Assert.NotNull(letters.QuerySelector(".input-field__chooser input[type=file]"));
        Assert.NotNull(letters.QuerySelector(".run-picker"));
        letters.QuerySelector("fluent-button.input-field__choose-text")!.Click();
        Assert.NotNull(Slot(page, "prior_letters").QuerySelector(".input-field__row fluent-text-area"));
        Assert.NotNull(Slot(page, "prior_letters").QuerySelector(".input-field__row .snippet-picker"));
    }

    [Fact]
    public void AnUnsupportedFormat_SaysTheWorkflowCannotBeFilledInHere_AndDisablesStart()
    {
        Pin(
            Input("consult_draft", "Consult draft", true, MarkdownBlock),
            Input("body_map", "Body map", false, """{"type":"object","properties":{"region":{"$ref":"urn:nowhere:format:region"}},"required":["region"]}"""));

        var page = Render<Consults>();
        Slot(page, "consult_draft").QuerySelector("fluent-text-area")!.Change(Draft);

        Assert.Equal(
            "This workflow can't be filled in here: this page has no field for 'Body map'. Tell the operator.",
            Slot(page, "body_map").QuerySelector(".input-field__error")!.TextContent.Trim());
        Assert.True(Start(page).HasAttribute("disabled"));
    }

    [Fact]
    public void A30Package_IsDrawnExactlyAsBefore()
    {
        WithPinnedPackage(blocks: [Block("s:hpi", "History")], specVersion: 30, inputs:
        [
            new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
            new WorkflowPackageInputResponse("seen_on", "Date seen", true, WorkflowInputTypes.Date),
            new WorkflowPackageInputResponse("encounter_kind", "Encounter kind", true, WorkflowInputTypes.Enum, ["new_patient", "follow_up"]),
            new WorkflowPackageInputResponse("billable", "Billable", false, WorkflowInputTypes.Boolean),
        ]);

        var page = Render<Consults>();

        Assert.Empty(page.FindAll("[data-input-id]"));
        Assert.Empty(page.FindAll(".input31"));
        Assert.Empty(page.FindAll(".input-field__help"));
        Assert.Single(page.FindAll("fluent-text-area"));
        Assert.Single(page.FindAll("input[type=date]"));
        Assert.Equal(2, page.FindAll("fluent-select.node-field__input").Count);
        // A required input left blank does not disable Start below 31; the click names it.
        Assert.False(Start(page).HasAttribute("disabled"));
        Assert.Empty(page.FindAll(".input-field__required-hint"));
    }
}
