using System.Net;
using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.Documents;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// The setup form's render (#224). The first test here is the regression for
/// #223: a bind expression Blazor can only reject when the component renders
/// took the whole form down in production, for every spec version.
/// </summary>
public class ConsultsSetupTests : ClientRenderTestContext
{
    private static IReadOnlyList<WorkflowPackageBlockResponse> NineSections(string prefix = "") =>
        new[] { "hpi", "pmh", "medications", "allergies", "social_history", "family_history", "exam", "investigations", "assessment_plan" }
            .Select(id => Block($"{prefix}section-instructions:{id}", id))
            .ToList();

    private static IReadOnlyList<IElement> Fields(IRenderedComponent<Consults> page) =>
        page.FindAll(".input-field");

    [Fact]
    public void LegacyPackage_RendersTheSingleDraftField()
    {
        // A package that declares no inputs is the v5/v6 shape: the page
        // synthesizes the frozen consult_draft slot.
        WithPinnedPackage(blocks: NineSections());

        var page = Render<Consults>();

        var field = Assert.Single(Fields(page));
        Assert.Contains("Consult draft", field.TextContent);
        Assert.DoesNotContain("(optional)", field.TextContent);
    }

    [Fact]
    public void DeclaredInputs_RenderOneFieldEachWithTheOptionalMarker()
    {
        WithPinnedPackage(
            blocks: NineSections("consult_note:"),
            inputs: new[]
            {
                new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
                new WorkflowPackageInputResponse("prior_notes", "Prior notes", false)
            });

        var page = Render<Consults>();

        var fields = Fields(page);
        Assert.Equal(2, fields.Count);
        Assert.Contains("Consult draft", fields[0].TextContent);
        Assert.DoesNotContain("(optional)", fields[0].TextContent);
        Assert.Contains("Prior notes", fields[1].TextContent);
        Assert.Contains("(optional)", fields[1].TextContent);
    }

    [Fact]
    public void ATitledPackage_ShowsItsTitleBeforeThePicker_AndAnUntitledOneTheRefOnly()
    {
        // #432: the title when there is one; the picker beside it shows the ref.
        WithPinnedPackage(blocks: NineSections(), specVersion: 9, title: "Breast oncology consults");
        var titled = Render<Consults>();
        Assert.Equal("Breast oncology consults", titled.Find(".setup-context__title").TextContent.Trim());

        WorkflowService.GetCurrentPackageAsync().Returns(new WorkflowPackageResponse("general", "v2026.07.10", 6, NineSections()));
        var untitled = Render<Consults>();
        Assert.Empty(untitled.FindAll(".setup-context__title"));
        Assert.Contains("9 sections", untitled.Find(".setup-context").TextContent);
    }

    [Fact]
    public void Submit_IsGatedOnEveryRequiredInput()
    {
        WithPinnedPackage(
            blocks: NineSections(),
            inputs: new[]
            {
                new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
                new WorkflowPackageInputResponse("prior_notes", "Prior notes", false)
            });

        var page = Render<Consults>();
        var submit = page.FindAll("fluent-button").Last();
        Assert.True(submit.HasAttribute("disabled"));

        // Filling only the optional input leaves the gate closed.
        page.FindAll("fluent-text-area")[1].Change("Old notes.");
        Assert.True(page.FindAll("fluent-button").Last().HasAttribute("disabled"));

        page.FindAll("fluent-text-area")[0].Change("Chest pain, rule out ACS.");
        Assert.False(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
    }

    [Fact]
    public void MultiDeliverablePackage_GroupsTheSectionRosterByDocument()
    {
        WithPinnedPackage(
            blocks: NineSections("consult_note:").Concat(NineSections("patient_letter:")).ToList(),
            inputs: new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true) },
            results: new[]
            {
                new WorkflowPackageResultResponse("consult_note", "Consultation note"),
                new WorkflowPackageResultResponse("patient_letter", "Patient letter")
            });

        var page = Render<Consults>();

        Assert.Contains("2 documents · 9 sections each", page.Find(".setup-context").TextContent);
        var groups = page.FindAll(".setup-sections__group-label");
        Assert.Equal(
            new[] { "Consultation note", "Patient letter" },
            groups.Select(group => group.TextContent.Trim()).ToArray());
    }

    private static IRenderedComponent<Microsoft.AspNetCore.Components.Forms.InputFile> FileInput(
        IRenderedComponent<Consults> page,
        int index) =>
        page.FindComponents<Microsoft.AspNetCore.Components.Forms.InputFile>()[index];

    private static string FieldText(IRenderedComponent<Consults> page, int index) =>
        page.FindAll("fluent-text-area")[index].GetAttribute("current-value")
            ?? page.FindAll("fluent-text-area")[index].GetAttribute("value")
            ?? string.Empty;

    /// <summary>
    /// The parser reads every attachment (#235), so the client only ever sees
    /// its answer. Stubbing that answer is what these drive.
    /// </summary>
    private void WithExtraction(string text, string extractor = "text/1", int? pageCount = null) =>
        DocumentService.ExtractAsync(Arg.Any<byte[]>(), Arg.Any<string>())
            .Returns(new DocumentExtractionOutcome(text, extractor, pageCount, null));

    private void WithRefusal(string error) =>
        DocumentService.ExtractAsync(Arg.Any<byte[]>(), Arg.Any<string>())
            .Returns(DocumentExtractionOutcome.Refused(error));

    private static IElement Field(IRenderedComponent<Consults> page, int index) =>
        page.FindAll(".input-field")[index];

    [Fact]
    public void AttachingAFile_ReplacesThatSlotsTextAreaOnly()
    {
        // The per-slot behaviour v7 made possible: two declared inputs, and a
        // file aimed at the second leaves the first's text area alone.
        WithPinnedPackage(
            blocks: NineSections(),
            inputs: new[]
            {
                new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
                new WorkflowPackageInputResponse("prior_notes", "Prior notes", false)
            });
        WithExtraction("Old records.");

        var page = Render<Consults>();
        FileInput(page, 1).UploadFiles(InputFileContent.CreateFromText("Old records.", "records.txt"));

        Assert.Single(page.FindAll("fluent-text-area"));
        Assert.Empty(Field(page, 0).QuerySelectorAll(".input-field__chip"));
        Assert.Contains("records.txt", Field(page, 1).QuerySelector(".input-field__chip")!.TextContent);
    }

    [Fact]
    public void AttachingAFile_ShowsWhatTheServerReadFromIt()
    {
        // Not decoration: extraction is lossy on columns and tables, so the
        // read has to be visible while rejecting it is still cheap.
        WithPinnedPackage(blocks: NineSections());
        WithExtraction("Emily Lee is a 54 year old woman.", "pdfpig/0.1.15", pageCount: 3);

        var page = Render<Consults>();
        FileInput(page, 0).UploadFiles(InputFileContent.CreateFromText("%PDF-1.7", "referral.pdf"));

        Assert.Equal("Emily Lee is a 54 year old woman.", page.Find(".input-field__preview").TextContent);
        Assert.Contains("3 pages", page.Find(".input-field__chip").TextContent);
    }

    [Fact]
    public void RemovingTheFile_GivesBackWhatWasTyped()
    {
        WithPinnedPackage(blocks: NineSections());
        WithExtraction("From the file.");

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Typed by hand.");
        FileInput(page, 0).UploadFiles(InputFileContent.CreateFromText("From the file.", "referral.md"));
        Assert.Empty(page.FindAll("fluent-text-area"));

        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Remove")).Click();

        Assert.Equal("Typed by hand.", FieldText(page, 0));
        Assert.Empty(page.FindAll(".input-field__chip"));
    }

    [Fact]
    public void ARefusedFile_ShowsTheServersSentenceAndChangesNothing()
    {
        // The sentence is the server's (DocumentExtractionCopy), rendered
        // verbatim — one copy of the copy, shared with the email door.
        WithPinnedPackage(blocks: NineSections());
        WithRefusal("This PDF has no text layer, so it is a scan or a fax.");

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Typed by hand.");
        FileInput(page, 0).UploadFiles(InputFileContent.CreateFromText("%PDF-1.7", "scan.pdf"));

        Assert.Equal(
            "This PDF has no text layer, so it is a scan or a fax.",
            page.Find(".input-field__file-error").TextContent);
        Assert.Equal("Typed by hand.", FieldText(page, 0));
        Assert.Empty(page.FindAll(".input-field__chip"));
    }

    [Fact]
    public void UploadingAnOversizeFile_IsRefusedBeforeAnyBytesAreSent()
    {
        WithPinnedPackage(blocks: NineSections());

        var page = Render<Consults>();
        FileInput(page, 0).UploadFiles(
            InputFileContent.CreateFromText(new string('x', (10 * 1024 * 1024) + 1), "big.pdf"));

        Assert.Contains("larger than 10 MB", page.Find(".input-field__file-error").TextContent);
        DocumentService.DidNotReceive().ExtractAsync(Arg.Any<byte[]>(), Arg.Any<string>());
    }

    [Fact]
    public void AttachingIntoARequiredSlot_OpensTheSubmitGate()
    {
        // The interaction most likely to regress: every gate used to read
        // field.Value, and a file-backed slot has none.
        WithPinnedPackage(blocks: NineSections());
        WithExtraction("Referral body.");

        var page = Render<Consults>();
        Assert.True(page.FindAll("fluent-button").Last().HasAttribute("disabled"));

        FileInput(page, 0).UploadFiles(InputFileContent.CreateFromText("Referral body.", "referral.txt"));

        Assert.False(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
    }

    [Fact]
    public void Submitting_SendsTheFileItselfAndTypedSlotsAsText()
    {
        // The whole point of the model: the bytes travel, not the preview. The
        // server extracts them again, so a slot's origin is something it
        // observed rather than something this client asserted.
        WithPinnedPackage(
            blocks: NineSections(),
            inputs: new[]
            {
                new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
                new WorkflowPackageInputResponse("prior_notes", "Prior notes", false)
            });
        WithExtraction("Old records, as read.");

        IReadOnlyDictionary<string, ConsultInputValue>? sentInputs = null;
        IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>? sentFiles = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sentInputs = value),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Do<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(value => sentFiles = value))
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Typed referral.");
        FileInput(page, 1).UploadFiles(InputFileContent.CreateFromText("Old records.", "records.txt"));
        page.FindAll("fluent-button").Last().Click();

        Assert.NotNull(sentInputs);
        Assert.Equal(new[] { "consult_draft" }, sentInputs!.Keys.ToArray());
        Assert.Equal("Typed referral.", sentInputs["consult_draft"]);

        Assert.NotNull(sentFiles);
        Assert.Equal(new[] { "prior_notes" }, sentFiles!.Keys.ToArray());
        // #428: a slot lists its documents; this form attaches one, so one.
        var document = Assert.Single(sentFiles["prior_notes"]);
        Assert.Equal("Old records.", System.Text.Encoding.UTF8.GetString(document.Content));
    }

    [Fact]
    public void SingleDeliverablePackage_KeepsTheFlatSectionWording()
    {
        WithPinnedPackage(
            blocks: NineSections("consult:"),
            inputs: new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true) },
            results: new[] { new WorkflowPackageResultResponse("consult", "Consultation note") });

        var page = Render<Consults>();

        Assert.Contains("9 sections", page.Find(".setup-context").TextContent);
        Assert.DoesNotContain("documents", page.Find(".setup-context").TextContent);
        Assert.Empty(page.FindAll(".setup-sections__group-label"));
    }

    // #348: what the form does with a start refusal. The service now carries
    // the server's reason; these hold the page to showing it.

    private const string Refusal =
        "No document applies to these inputs. 'Consultation note' needs billable to be 'true'; it is 'false'.";

    private void WhenSubmitThrows(Exception failure) =>
        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns<Task<ConsultGenerationJobStartResponse>>(_ => throw failure);

    private IRenderedComponent<Consults> SubmitOneDraft()
    {
        WithPinnedPackage(
            blocks: NineSections(),
            inputs: new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true) });

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Chest pain, rule out ACS.");
        page.FindAll("fluent-button").Last().Click();

        return page;
    }

    [Fact]
    public void AStartRefusal_IsShownAsTheServerWroteIt()
    {
        WhenSubmitThrows(new ConsultGenerationRefusedException(HttpStatusCode.UnprocessableEntity, Refusal));

        var page = SubmitOneDraft();

        Assert.Contains(Refusal, page.Markup, StringComparison.Ordinal);

        // The prefix describes a fault that did not occur: nothing was called,
        // the request was declined. It also pushed the actionable half of the
        // sentence past where anyone reads.
        Assert.DoesNotContain("Error calling agent", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusalThatLeftARow_LinksToItInHistory()
    {
        // #434: the sentence is unchanged; what is new is that a record exists,
        // and the user is told so where the sentence is.
        WhenSubmitThrows(new ConsultGenerationRefusedException(
            HttpStatusCode.UnprocessableEntity, Refusal, "bb4df62f2bea4dd193a92ae0e6798370"));

        var page = SubmitOneDraft();

        Assert.Contains(Refusal, page.Markup, StringComparison.Ordinal);
        var link = page.Find("a.refusal-history-link");
        Assert.Equal("/history/bb4df62f2bea4dd193a92ae0e6798370", link.GetAttribute("href"));
        Assert.Equal("View in History", link.TextContent.Trim());
    }

    [Fact]
    public void ARefusalThatLeftNothing_HasNoLink()
    {
        WhenSubmitThrows(new ConsultGenerationRefusedException(HttpStatusCode.UnprocessableEntity, Refusal));

        var page = SubmitOneDraft();

        Assert.Empty(page.FindAll("a.refusal-history-link"));
    }

    [Fact]
    public void ARealFailure_KeepsItsPrefix()
    {
        // The narrow half: a transport fault is still a fault, and saying so
        // is what distinguishes "we could not reach the service" from "the
        // service considered this and said no".
        WhenSubmitThrows(new HttpRequestException("Azure Function call failed: BadGateway"));

        var page = SubmitOneDraft();

        Assert.Contains("Error calling agent", page.Markup, StringComparison.Ordinal);
    }
}

/// <summary>
/// v8 intake (#316): a declared type decides the control, and — for boolean —
/// what the wire carries. Typed inputs are strict, so sending "true" for a
/// boolean slot is a 422; this is the surface that makes such a package
/// submittable at all.
/// </summary>
public class ConsultsTypedIntakeTests : ClientRenderTestContext
{
    private static WorkflowPackageInputResponse[] TypedInputs() => new[]
    {
        new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
        new WorkflowPackageInputResponse("seen_on", "Date seen", true, WorkflowInputTypes.Date),
        new WorkflowPackageInputResponse("encounter_kind", "Encounter kind", true, WorkflowInputTypes.Enum,
            new[] { "new_patient", "follow_up" }),
        new WorkflowPackageInputResponse("billable", "Billable", false, WorkflowInputTypes.Boolean)
    };

    [Fact]
    public void EachDeclaredType_RendersItsOwnControl()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: TypedInputs());

        var page = Render<Consults>();

        // text keeps the textarea; date, enum and boolean each get their own.
        Assert.Single(page.FindAll("fluent-text-area"));
        Assert.Single(page.FindAll("input[type=date]"));
        Assert.Equal(2, page.FindAll("select.node-field__input").Count);
    }

    [Fact]
    public void AnEnumOffersNoSelectionUntilOneIsMade()
    {
        // Explicit initialisation: a plausible default is still a value nobody
        // chose, and a consult is not the place to guess.
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: TypedInputs());

        var page = Render<Consults>();
        var kind = page.FindAll("select.node-field__input")[0];

        Assert.Equal(string.Empty, kind.GetAttribute("value"));
        Assert.Equal("", kind.QuerySelectorAll("option")[0].GetAttribute("value"));
    }

    [Fact]
    public void ABooleanTravelsAsAJsonBoolean_AndADateAsItsCanonicalString()
    {
        // The reason this issue blocked the demo package: the form used to send
        // a string map, and a boolean slot rejects "true" with a 422.
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: TypedInputs());

        IReadOnlyDictionary<string, ConsultInputValue>? sent = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sent = value),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Referral.");
        page.Find("input[type=date]").Change("2026-08-10");
        page.FindAll("select.node-field__input")[0].Change("follow_up");
        page.FindAll("select.node-field__input")[1].Change("true");
        page.FindAll("fluent-button").Last().Click();

        Assert.NotNull(sent);
        Assert.True(sent!["billable"].IsBoolean);
        Assert.Equal("true", sent["billable"].Canonical);
        Assert.False(sent["seen_on"].IsBoolean);
        Assert.Equal("2026-08-10", sent["seen_on"].Canonical);
        Assert.Equal("follow_up", sent["encounter_kind"].Canonical);
    }

    [Fact]
    public void AnUntouchedBoolean_IsOmittedRatherThanSentAsFalse()
    {
        // Absence and falsity are different, all the way down: #315's condition
        // evaluation depends on an unanswered optional not reading as false.
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: TypedInputs());

        IReadOnlyDictionary<string, ConsultInputValue>? sent = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sent = value),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Referral.");
        page.Find("input[type=date]").Change("2026-08-10");
        page.FindAll("select.node-field__input")[0].Change("follow_up");
        page.FindAll("fluent-button").Last().Click();

        Assert.NotNull(sent);
        Assert.False(sent!.ContainsKey("billable"));
    }

    [Fact]
    public void AnUnchosenRequiredEnum_BlocksTheRun()
    {
        // The same gate a blank textarea hits: IsFilled asks whether the slot
        // would supply anything, so "chose nothing" and "typed nothing" are one
        // rule rather than two. The run button is disabled rather than the
        // click being refused — the control says so before it is pressed.
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: TypedInputs());

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Referral.");
        page.Find("input[type=date]").Change("2026-08-10");

        var run = page.FindAll("fluent-button").Last();
        Assert.True(run.HasAttribute("disabled"));

        // Choosing the enum releases it; the optional boolean is not required.
        page.FindAll("select.node-field__input")[0].Change("follow_up");

        Assert.False(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
    }

    // ----- #429: v9 intake controls ----------------------------------------

    private static WorkflowPackageInputResponse[] WithNumber() => new[]
    {
        new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
        new WorkflowPackageInputResponse("length_of_stay", "Length of stay", false, WorkflowInputTypes.Number)
    };

    [Fact]
    public void ANumber_RendersADecimalTextInput()
    {
        // Not type=number: the canonical form is the spelling typed, which a
        // number control would not promise to keep.
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNumber(), specVersion: 9);

        var page = Render<Consults>();

        var control = Assert.Single(page.FindAll("input[inputmode=decimal]"));
        Assert.Equal("Length of stay", control.GetAttribute("aria-label"));
        Assert.Empty(page.FindAll("input[type=number]"));
    }

    [Fact]
    public void ANonNumber_NamesTheFieldAndHoldsTheRun()
    {
        // On an OPTIONAL slot: a value that is present and wrong must not be
        // quietly omitted. Clearing it reopens the gate.
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNumber(), specVersion: 9);

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Referral.");
        Assert.False(page.FindAll("fluent-button").Last().HasAttribute("disabled"));

        page.Find("input[inputmode=decimal]").Change("about a week");

        Assert.Equal(
            "Length of stay must be a plain decimal number, like 12 or 1.50.",
            page.Find(".input-field__error").TextContent.Trim());
        Assert.True(page.FindAll("fluent-button").Last().HasAttribute("disabled"));

        page.Find("input[inputmode=decimal]").Change("");

        Assert.Empty(page.FindAll(".input-field__error"));
        Assert.False(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
    }

    [Fact]
    public void ANumber_TravelsWithTheSpellingTyped()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNumber(), specVersion: 9);
        IReadOnlyDictionary<string, ConsultInputValue>? sent = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sent = value),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Referral.");
        page.Find("input[inputmode=decimal]").Change("1.50");
        page.FindAll("fluent-button").Last().Click();

        Assert.NotNull(sent);
        Assert.True(sent!["length_of_stay"].IsNumber);
        Assert.Equal("1.50", sent["length_of_stay"].Canonical);
    }

    [Fact]
    public void AnUntouchedOptionalNumber_IsOmitted()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNumber(), specVersion: 9);
        IReadOnlyDictionary<string, ConsultInputValue>? sent = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sent = value),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Referral.");
        page.FindAll("fluent-button").Last().Click();

        Assert.Equal(new[] { "consult_draft" }, sent!.Keys);
    }

    private static WorkflowPackageInputResponse[] WithPatient(bool required = true) => new[]
    {
        new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
        new WorkflowPackageInputResponse("patient", "Patient", required, WorkflowInputTypes.Object,
            Fields: new[]
            {
                new WorkflowPackageFieldResponse("age", "Age", true, WorkflowInputTypes.Number),
                new WorkflowPackageFieldResponse("sex", "Sex", false, WorkflowInputTypes.Enum, new[] { "female", "male" }),
                new WorkflowPackageFieldResponse("family_name", "Family name", false)
            })
    };

    [Fact]
    public void AnObject_RendersOneControlPerField()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithPatient(), specVersion: 9);

        var page = Render<Consults>();

        var group = page.Find(".input-field__group");
        Assert.Equal(
            new[] { "Age", "Sex", "Family name" },
            group.QuerySelectorAll("label.input-field__member > span").Select(span => span.TextContent.Replace("(optional)", "").Trim()));
        Assert.Single(group.QuerySelectorAll("input[inputmode=decimal]"));
        Assert.Single(group.QuerySelectorAll("select.node-field__input"));
        Assert.Single(group.QuerySelectorAll("fluent-text-area"));
    }

    [Fact]
    public void ARequiredField_GatesTheRun_AndIsNamedOnceTheObjectIsTouched()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithPatient(), specVersion: 9);

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");

        // Untouched: required, so closed — but nothing is wrong yet.
        Assert.True(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
        Assert.Empty(page.FindAll(".input-field__error"));

        // Touched through an optional field: the required one is now named.
        page.Find(".input-field__group select.node-field__input").Change("female");
        Assert.Equal("Patient: Age is required.", page.Find(".input-field__error").TextContent.Trim());
        Assert.True(page.FindAll("fluent-button").Last().HasAttribute("disabled"));

        page.Find("input[inputmode=decimal]").Change("41");
        Assert.Empty(page.FindAll(".input-field__error"));
        Assert.False(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
    }

    [Fact]
    public void AnObject_TravelsInFieldOrder_WithBlankOptionalFieldsOmitted()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithPatient(), specVersion: 9);
        IReadOnlyDictionary<string, ConsultInputValue>? sent = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sent = value),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        page.FindAll("fluent-text-area")[1].Change("Smith");
        page.Find("input[inputmode=decimal]").Change("41");
        page.FindAll("fluent-button").Last().Click();

        var patient = sent!["patient"];
        Assert.True(patient.IsObject);
        Assert.Equal(new[] { "age", "family_name" }, patient.Fields!.Select(entry => entry.Id));
        Assert.True(patient.Fields![0].Value.IsNumber);
        Assert.Equal("41", patient.Fields[0].Value.Canonical);
    }

    [Fact]
    public void AnUntouchedOptionalObject_IsOmitted()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithPatient(required: false), specVersion: 9);
        IReadOnlyDictionary<string, ConsultInputValue>? sent = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sent = value),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        Assert.False(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
        page.FindAll("fluent-button").Last().Click();

        Assert.Equal(new[] { "consult_draft" }, sent!.Keys);
    }

    [Fact]
    public void AFieldThatDoesNotParse_IsNamedWithItsObject()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithPatient(), specVersion: 9);

        var page = Render<Consults>();
        page.Find("input[inputmode=decimal]").Change("forty");

        Assert.Equal(
            "Patient: Age must be a plain decimal number, like 12 or 1.50.",
            page.Find(".input-field__error").TextContent.Trim());
    }

    private static WorkflowPackageInputResponse[] WithNotes(bool required = false, string items = WorkflowInputTypes.Text) => new[]
    {
        new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
        new WorkflowPackageInputResponse("prior_notes", "Prior notes", required, WorkflowInputTypes.Array,
            Items: new WorkflowPackageElementResponse(items, Values: items == WorkflowInputTypes.Enum ? new[] { "clinic", "ward" } : null))
    };

    private static WorkflowPackageInputResponse[] WithLabs() => new[]
    {
        new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
        new WorkflowPackageInputResponse("labs", "Labs", false, WorkflowInputTypes.Array, Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Object, Fields: new[]
            {
                new WorkflowPackageFieldResponse("name", "Test", true),
                new WorkflowPackageFieldResponse("value", "Value", true, WorkflowInputTypes.Number)
            }))
    };

    private IReadOnlyDictionary<string, ConsultInputValue>? sentInputs;

    private void CaptureSubmit() =>
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sentInputs = value),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

    [Fact]
    public void AnArray_StartsWithNoRowsAndAnAddButton()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(), specVersion: 9);

        var page = Render<Consults>();

        Assert.Empty(page.FindAll(".input-field__row"));
        Assert.Equal("+ Add entry", page.Find(".input-field__add").TextContent.Trim());
    }

    [Fact]
    public void Rows_AreAddedRemovedAndMoved_AndTravelInOrder()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(), specVersion: 9);
        CaptureSubmit();

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        page.Find(".input-field__add").Click();
        page.Find(".input-field__add").Click();
        page.Find(".input-field__add").Click();
        Assert.Equal(3, page.FindAll(".input-field__row").Count);

        var areas = page.FindAll("fluent-text-area");
        areas[1].Change("One.");
        areas[2].Change("Two.");
        areas[3].Change("Three.");

        // The third moves up, the first is removed: Three, Two.
        page.FindAll("button[title='Move up']")[2].Click();
        page.FindAll("button[title='Remove entry']")[0].Click();
        Assert.Equal(2, page.FindAll(".input-field__row").Count);

        page.FindAll("fluent-button").Last().Click();

        var notes = sentInputs!["prior_notes"];
        Assert.True(notes.IsArray);
        Assert.Equal(new[] { "Three.", "Two." }, notes.Elements!.Select(element => element.Canonical));
    }

    [Fact]
    public void ARequiredArrayWithNoRows_KeepsTheGateClosed()
    {
        // No rows is absent, and a required slot cannot be absent — closed
        // without complaint, like a blank required textarea.
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(required: true), specVersion: 9);

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");

        Assert.True(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
        Assert.Empty(page.FindAll(".input-field__error"));
    }

    [Fact]
    public void AnOptionalArrayWithNoRows_IsOmitted()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(required: false), specVersion: 9);
        CaptureSubmit();

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        Assert.False(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
        page.FindAll("fluent-button").Last().Click();

        Assert.Equal(new[] { "consult_draft" }, sentInputs!.Keys);
    }

    [Fact]
    public void AnEmptyRow_NamesItselfAndHoldsTheRun()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(), specVersion: 9);

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        page.Find(".input-field__add").Click();
        page.Find(".input-field__add").Click();
        page.FindAll("fluent-text-area")[1].Change("One.");

        Assert.Equal("Prior notes row 2 is empty; fill it in or remove it.", page.Find(".input-field__error").TextContent.Trim());
        Assert.True(page.FindAll("fluent-button").Last().HasAttribute("disabled"));

        page.FindAll("button[title='Remove entry']")[1].Click();
        Assert.Empty(page.FindAll(".input-field__error"));
        Assert.False(page.FindAll("fluent-button").Last().HasAttribute("disabled"));
    }

    [Fact]
    public void AnArrayOfObjects_RendersAFieldGroupPerRow_AndTravelsAsObjects()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithLabs(), specVersion: 9);
        CaptureSubmit();

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        page.Find(".input-field__add").Click();
        page.Find(".input-field__add").Click();

        Assert.Equal(2, page.FindAll(".input-field__row").Count);
        Assert.Equal(4, page.FindAll(".input-field__row label.input-field__member").Count);

        page.FindAll("fluent-text-area")[1].Change("Sodium");
        page.FindAll("input[inputmode=decimal]")[0].Change("138");
        page.FindAll("fluent-text-area")[2].Change("Potassium");
        Assert.Equal("Labs row 2: Value is required.", page.Find(".input-field__error").TextContent.Trim());
        page.FindAll("input[inputmode=decimal]")[1].Change("4.1");
        Assert.Empty(page.FindAll(".input-field__error"));

        page.FindAll("fluent-button").Last().Click();

        var labs = sentInputs!["labs"];
        Assert.True(labs.IsArray);
        Assert.Equal(2, labs.Elements!.Count);
        Assert.Equal(new[] { "name", "value" }, labs.Elements[0].Fields!.Select(entry => entry.Id));
        Assert.Equal("4.1", labs.Elements[1].Fields![1].Value.Canonical);
    }

    [Fact]
    public void AnArrayOfEnums_OffersTheDeclaredValuesPerRow()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(items: WorkflowInputTypes.Enum), specVersion: 9);

        var page = Render<Consults>();
        page.Find(".input-field__add").Click();

        var options = page.Find(".input-field__row select.node-field__input").QuerySelectorAll("option").Select(option => option.GetAttribute("value"));
        Assert.Equal(new[] { "", "clinic", "ward" }, options);
    }

    // ----- #429: several documents for one slot ----------------------------

    /// <summary>Each file reads as its own text, so order is observable.</summary>
    private void WithExtractionOfEachFile() =>
        DocumentService.ExtractAsync(Arg.Any<byte[]>(), Arg.Any<string>())
            .Returns(call =>
            {
                var text = System.Text.Encoding.UTF8.GetString(call.Arg<byte[]>());
                return text.StartsWith("BAD", StringComparison.Ordinal)
                    ? DocumentExtractionOutcome.Refused("This PDF has no text layer, so it is a scan or a fax.")
                    : new DocumentExtractionOutcome(text, "text/1", text.Length, null);
            });

    private IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>? sentFiles;

    private void CaptureSubmitWithFiles() =>
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sentInputs = value),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Do<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(value => sentFiles = value))
            .Returns(new ConsultGenerationJobStartResponse("job-1", "https://example/status"));

    private static IRenderedComponent<Microsoft.AspNetCore.Components.Forms.InputFile> FileInput(
        IRenderedComponent<Consults> page,
        int index) =>
        page.FindComponents<Microsoft.AspNetCore.Components.Forms.InputFile>()[index];

    private static string FieldText(IRenderedComponent<Consults> page, int index) =>
        page.FindAll("fluent-text-area")[index].GetAttribute("current-value")
            ?? page.FindAll("fluent-text-area")[index].GetAttribute("value")
            ?? string.Empty;

    private static IEnumerable<string> ChipNames(IRenderedComponent<Consults> page) =>
        page.FindAll(".input-field__document .input-field__chip-name").Select(chip => chip.TextContent.Trim());

    [Fact]
    public void TwoFilesSelectedAtOnce_AttachInSelectionOrder()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(), specVersion: 9);
        WithExtractionOfEachFile();

        var page = Render<Consults>();
        FileInput(page, 1).UploadFiles(
            InputFileContent.CreateFromText("First note.", "first.txt"),
            InputFileContent.CreateFromText("Second note.", "second.txt"));

        Assert.Equal(new[] { "first.txt", "second.txt" }, ChipNames(page));
        Assert.Equal(
            new[] { "First note.", "Second note." },
            page.FindAll(".input-field__document .input-field__preview").Select(preview => preview.TextContent.Trim()));
        // Document mode: the rows are hidden, the picker stays to append.
        Assert.Empty(page.FindAll(".input-field__row"));
        Assert.Empty(page.FindAll(".input-field__add"));
    }

    [Fact]
    public void MovingADocumentDown_ReordersWhatIsSent()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(), specVersion: 9);
        WithExtractionOfEachFile();
        CaptureSubmitWithFiles();

        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        FileInput(page, 1).UploadFiles(
            InputFileContent.CreateFromText("First note.", "first.txt"),
            InputFileContent.CreateFromText("Second note.", "second.txt"));
        page.FindAll("button[title='Move down']")[0].Click();
        Assert.Equal(new[] { "second.txt", "first.txt" }, ChipNames(page));

        page.FindAll("fluent-button").Last().Click();

        var documents = sentFiles!["prior_notes"];
        Assert.Equal(
            new[] { "Second note.", "First note." },
            documents.Select(document => System.Text.Encoding.UTF8.GetString(document.Content)));
        Assert.False(sentInputs!.ContainsKey("prior_notes"));
    }

    [Fact]
    public void RemovingOneDocument_KeepsTheOthers_AndRemoveAllGivesBackTheRows()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(), specVersion: 9);
        WithExtractionOfEachFile();

        var page = Render<Consults>();
        page.Find(".input-field__add").Click();
        page.FindAll("fluent-text-area")[1].Change("Typed row.");
        FileInput(page, 1).UploadFiles(
            InputFileContent.CreateFromText("First note.", "first.txt"),
            InputFileContent.CreateFromText("Second note.", "second.txt"));

        page.FindAll("button[title='Remove this file']")[0].Click();
        Assert.Equal(new[] { "second.txt" }, ChipNames(page));

        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Remove all")).Click();
        Assert.Empty(page.FindAll(".input-field__document"));
        Assert.Single(page.FindAll(".input-field__row"));
        Assert.Equal("Typed row.", FieldText(page, 1));
    }

    [Fact]
    public void APerFileRefusal_NamesTheFileAndKeepsTheOthers()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(), specVersion: 9);
        WithExtractionOfEachFile();

        var page = Render<Consults>();
        FileInput(page, 1).UploadFiles(
            InputFileContent.CreateFromText("First note.", "first.txt"),
            InputFileContent.CreateFromText("BAD scan", "scan.pdf"));

        Assert.Equal(new[] { "first.txt" }, ChipNames(page));
        Assert.Equal(
            "scan.pdf: This PDF has no text layer, so it is a scan or a fax.",
            page.Find(".input-field__file-error").TextContent.Trim());
    }

    [Fact]
    public void ALoneRefusedFile_KeepsTheServersSentenceVerbatim()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithNotes(), specVersion: 9);
        WithExtractionOfEachFile();

        var page = Render<Consults>();
        FileInput(page, 1).UploadFiles(InputFileContent.CreateFromText("BAD scan", "scan.pdf"));

        Assert.Equal("This PDF has no text layer, so it is a scan or a fax.", page.Find(".input-field__file-error").TextContent.Trim());
    }

    [Fact]
    public void OnlyAnArrayOfTextTakesSeveral()
    {
        // A text slot's picker is single; an array of text's is multiple; an
        // array of anything else has none — the server's own rule.
        var inputs = new[]
        {
            new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
            new WorkflowPackageInputResponse("prior_notes", "Prior notes", false, WorkflowInputTypes.Array, Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Text)),
            new WorkflowPackageInputResponse("visits", "Visits", false, WorkflowInputTypes.Array, Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Date))
        };
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: inputs, specVersion: 9);

        var page = Render<Consults>();

        var pickers = page.FindAll("input[type=file]");
        Assert.Equal(2, pickers.Count);
        Assert.False(pickers[0].HasAttribute("multiple"));
        Assert.True(pickers[1].HasAttribute("multiple"));
    }

    [Fact]
    public void ReattachingAStructuredRun_RestoresItsRowsAndFields()
    {
        // #429: the memento carries the inputs as typed, so "Edit inputs" on a
        // run gives back the rows and fields that ran, not their text.
        const string JobId = "0123456789abcdef0123456789abcdef";
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithLabs(), specVersion: 9);
        JobSession.Current = new ConsultJobMemento(
            JobId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["consult_draft"] = "Referral.", ["labs"] = "Test: Sodium\nValue: 138" },
            new[] { new ConsultJobBlock("s:hpi", "History") },
            new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = ConsultInputValue.OfText("Referral."),
                ["labs"] = ConsultInputValue.OfArray(new[]
                {
                    ConsultInputValue.OfObject(new[] { new ConsultInputEntry("name", ConsultInputValue.OfText("Sodium")), new ConsultInputEntry("value", ConsultInputValue.OfNumber("138")) }),
                    ConsultInputValue.OfObject(new[] { new ConsultInputEntry("name", ConsultInputValue.OfText("Potassium")), new ConsultInputEntry("value", ConsultInputValue.OfNumber("4.1")) })
                })
            });
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed", TotalBlockCount: 1, CompletedBlockCount: 1, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string> { ["s:hpi"] = "Section prose." },
            FailedBlocks: new Dictionary<string, string>(), Success: true, AssembledDocument: "The note."));

        var page = Render<Consults>();
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Edit inputs")).Click();

        Assert.Equal(2, page.FindAll(".input-field__row").Count);
        Assert.Equal("Sodium", FieldText(page, 1));
        Assert.Equal(new[] { "138", "4.1" }, page.FindAll("input[inputmode=decimal]").Select(input => input.GetAttribute("value")));
    }

    [Fact]
    public async Task SwitchingPackages_CarriesAChosenBoolean()
    {
        // #429: the whole answer carries into a slot the next package still
        // declares with the same shape. A chosen boolean used to be dropped
        // here — Value carried, Flag did not.
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: TypedInputs());
        var page = Render<Consults>();
        page.FindAll("select.node-field__input")[1].Change("true");

        // Only the package stub moves: re-running WithPinnedPackage would
        // re-enter its throwing content stub while configuring it.
        WorkflowService.GetCurrentPackageAsync().Returns(new WorkflowPackageResponse(
            "general", "v2026.07.11", 7, new[] { Block("s:plan", "Plan") }, TypedInputs(), null));
        var picker = page.FindComponent<Consultologist.Web.Shared.WorkflowEditor.WorkflowPackagePicker>();
        await page.InvokeAsync(() => picker.Instance.OnPinned.InvokeAsync());

        Assert.Equal("true", page.FindAll("select.node-field__input")[1].GetAttribute("value"));
    }

    [Fact]
    public void AV7Package_RendersExactlyAsItDid()
    {
        // Every v5-v7 input is a text slot, so nothing about the old form moves.
        WithPinnedPackage(
            blocks: new[] { Block("s:hpi", "History") },
            inputs: new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true) });

        var page = Render<Consults>();

        Assert.Single(page.FindAll("fluent-text-area"));
        Assert.Empty(page.FindAll("input[type=date]"));
        Assert.Empty(page.FindAll("select.node-field__input"));
    }
}

/// <summary>
/// #360: which package version a submit runs against. The page holds one
/// resolved ref — the current pin's, the same package its input fields were
/// built from — and re-attaching to an earlier job must not move it.
///
/// In production it did. Publishing from the editor repins server-side without
/// navigating, so the next visit to Consults built the new version's fields and
/// then submitted them against the previous run's version: "Unknown input(s)
/// 'include_billing_rename' (declared: consult_draft, encounter_kind,
/// include_billing, seen_on)". When the two versions happen to declare the same
/// ids there is no error at all and the run silently uses the older package —
/// the case that needs a test, because nothing else would ever report it.
///
/// These are also the first tests to execute the session-matched re-attach
/// branch; ConsultsResultTests reaches re-attach by route id only, which takes
/// the other one.
/// </summary>
public class ConsultsPackageRefTests : ClientRenderTestContext
{
    private const string EarlierJobId = "0123456789abcdef0123456789abcdef";
    private const string CurrentRef = "general@v2026.08.02";

    private void WithTheCurrentPin() =>
        WithPinnedPackage(
            blocks: new[] { Block("consult:hpi", "History") },
            inputs: new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true) },
            version: "v2026.08.02");

    /// <summary>
    /// The tab state publishing leaves behind: a memento for a job this tab ran
    /// under the previous version, terminal on arrival — which is where the
    /// person stands when they edit the draft and run it again.
    /// </summary>
    private void WithARunFromTheEarlierPackage()
    {
        JobSession.Current = new ConsultJobMemento(
            EarlierJobId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["consult_draft"] = "Chest pain, rule out ACS." },
            new[] { new ConsultJobBlock("consult:hpi", "History") });

        AIService.GetConsultGenerationJobAsync(EarlierJobId).Returns(new ConsultGenerationJobResponse(
            EarlierJobId,
            "user-1",
            "Completed",
            TotalBlockCount: 1,
            CompletedBlockCount: 1,
            FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string> { ["consult:hpi"] = "Section prose." },
            FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            AssembledDocument: "The note.",
            // The job's own version — the server's record of it, and the only
            // place this page should ever learn it from.
            WorkflowPackage: "general@v2026.07.10"));
    }

    private string? CaptureSentRef()
    {
        var sent = new string?[1];

        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(),
                Arg.Do<string?>(value => sent[0] = value),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-2", "https://example/status"));

        return sent[0];
    }

    [Fact]
    public void ASubmit_SendsTheResolvedRefOfThePackageItsFieldsCameFrom()
    {
        // The control: no memento, nothing to confuse it. Holds the deliberate
        // half of the behaviour — the ref is sent rather than left null for the
        // server to re-resolve, so a floating @latest pin cannot move between
        // the form's render and its submit.
        WithTheCurrentPin();

        string? sentRef = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(),
                Arg.Do<string?>(value => sentRef = value),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-2", "https://example/status"));

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("Chest pain, rule out ACS.");
        page.FindAll("fluent-button").Last().Click();

        Assert.Equal(CurrentRef, sentRef);
    }

    [Fact]
    public void AReattachedRunFromAnEarlierPackage_DoesNotMoveTheRefTheNextSubmitSends()
    {
        WithTheCurrentPin();
        WithARunFromTheEarlierPackage();

        string? sentRef = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(),
                Arg.Do<string?>(value => sentRef = value),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-2", "https://example/status"));

        var page = Render<Consults>();

        // Re-attach lands on the finished run; editing is the way back to the
        // form. One declared input, so the control reads "Edit draft".
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Edit draft")).Click();

        page.Find("fluent-text-area").Change("Chest pain, rule out ACS.");
        page.FindAll("fluent-button").Last().Click();

        Assert.Equal(CurrentRef, sentRef);
    }

    [Fact]
    public void AnOvernightSubmit_SendsTheCurrentPinToo()
    {
        // The scheduled path writes no memento but read the same field, and it
        // is the worse place to be wrong: the run happens hours later with
        // nobody watching, so a version mismatch surfaces as an email.
        WithTheCurrentPin();
        WithARunFromTheEarlierPackage();

        string? sentRef = null;
        DateTimeOffset? sentSchedule = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(),
                Arg.Do<string?>(value => sentRef = value),
                Arg.Do<DateTimeOffset?>(value => sentSchedule = value),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-2", "https://example/status"));

        var page = Render<Consults>();
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Edit draft")).Click();
        page.Find("fluent-text-area").Change("Chest pain, rule out ACS.");
        // FluentSwitch raises onswitchcheckedchange carrying Fluent's own
        // CheckboxChangeEventArgs — .Change() and a plain ChangeEventArgs both
        // throw rather than silently doing nothing, which is why this is
        // spelled out.
        page.Find("fluent-switch").TriggerEvent(
            "onswitchcheckedchange",
            new Microsoft.FluentUI.AspNetCore.Components.CheckboxChangeEventArgs { Checked = true });

        // The switch is the entire difference between the two paths. Assert it
        // took, or this silently re-runs the immediate one.
        Assert.Contains("Schedule consult", page.FindAll("fluent-button").Last().TextContent);

        page.FindAll("fluent-button").Last().Click();

        Assert.Equal(CurrentRef, sentRef);
        Assert.NotNull(sentSchedule);
    }

    [Fact]
    public void TurningOnOvernight_SeedsTheTimeFromTheAccountDefault()
    {
        // #390: the common case stays one click — the control appears already
        // holding the account's preferred time, so Schedule is the next action.
        AccountService.GetSettingAsync(ScheduleDefault.SettingKey)
            .Returns(new AccountSettingResponse(
                ScheduleDefault.SettingKey, "06:30", "text/plain", DateTimeOffset.UtcNow));
        WithTheCurrentPin();
        WithARunFromTheEarlierPackage();

        var page = Render<Consults>();
        // The fixture opens in the run phase; the switch lives in setup.
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Edit draft")).Click();
        page.Find("fluent-switch").TriggerEvent(
            "onswitchcheckedchange",
            new Microsoft.FluentUI.AspNetCore.Components.CheckboxChangeEventArgs { Checked = true });

        var seeded = page.Find("input[type=datetime-local]").GetAttribute("value");

        Assert.NotNull(seeded);
        Assert.EndsWith("T06:30", seeded, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoAccountDefault_TheSeedIsStillTwoAm()
    {
        // What #157 shipped, unchanged for anyone who never sets a preference.
        AccountService.GetSettingAsync(ScheduleDefault.SettingKey).Returns((AccountSettingResponse?)null);
        WithTheCurrentPin();
        WithARunFromTheEarlierPackage();

        var page = Render<Consults>();
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Edit draft")).Click();
        page.Find("fluent-switch").TriggerEvent(
            "onswitchcheckedchange",
            new Microsoft.FluentUI.AspNetCore.Components.CheckboxChangeEventArgs { Checked = true });

        Assert.EndsWith("T02:00", page.Find("input[type=datetime-local]").GetAttribute("value")!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTimeControlAppearsOnlyWhenScheduling()
    {
        WithTheCurrentPin();
        WithARunFromTheEarlierPackage();

        var page = Render<Consults>();
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Edit draft")).Click();

        // In setup with the switch OFF — asserting Empty from the run phase
        // would pass without proving anything.
        Assert.NotEmpty(page.FindAll("fluent-switch"));
        Assert.Empty(page.FindAll("input[type=datetime-local]"));
    }

    [Fact]
    public void AnOvernightSubmit_SendsTheChosenTimeRatherThanThePreset()
    {
        // The whole issue in one assertion: what the user typed is what is
        // scheduled. Before #390 this was always the next 2 AM.
        AccountService.GetSettingAsync(ScheduleDefault.SettingKey).Returns((AccountSettingResponse?)null);
        WithTheCurrentPin();
        WithARunFromTheEarlierPackage();

        DateTimeOffset? sentSchedule = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(),
                Arg.Any<string?>(),
                Arg.Do<DateTimeOffset?>(value => sentSchedule = value),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("job-2", "https://example/status"));

        var page = Render<Consults>();
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Edit draft")).Click();
        page.Find("fluent-text-area").Change("Chest pain, rule out ACS.");
        page.Find("fluent-switch").TriggerEvent(
            "onswitchcheckedchange",
            new Microsoft.FluentUI.AspNetCore.Components.CheckboxChangeEventArgs { Checked = true });

        var chosen = DateTime.Now.Date.AddDays(3).AddHours(9).AddMinutes(15);
        page.Find("input[type=datetime-local]").Change(chosen.ToString("yyyy-MM-ddTHH:mm"));
        page.FindAll("fluent-button").Last().Click();

        Assert.NotNull(sentSchedule);
        Assert.Equal(chosen, sentSchedule!.Value.ToLocalTime().DateTime);
    }
}

/// <summary>
/// v12 § 3 (#621): the setup form offers one checkbox per optional macro,
/// pre-checked to the package's declared default, and the request names only
/// the deviations — an untouched form sends nothing at all.
/// </summary>
public class ConsultsMacroChoiceTests : ClientRenderTestContext
{
    private static readonly WorkflowPackageMacroResponse[] TwoChoices =
    {
        new("disclaimer", "Standing disclaimer", true),
        new("counseling", "Counseling paragraph", false)
    };

    private void WithTwelvePackage(IReadOnlyList<WorkflowPackageMacroResponse>? macros = null, int specVersion = 12) =>
        WithPinnedPackage(
            blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") },
            inputs: new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true) },
            specVersion: specVersion,
            macros: macros);

    [Fact]
    public void OneCheckboxPerOptionalMacro_PreCheckedToTheDefault()
    {
        WithTwelvePackage(TwoChoices);

        var page = Render<Consults>();

        var boxes = page.FindAll(".macro-choice input[type=checkbox]");
        Assert.Equal(2, boxes.Count);
        Assert.True(boxes[0].HasAttribute("checked"));
        Assert.False(boxes[1].HasAttribute("checked"));
        Assert.Contains("Standing disclaimer", page.Find(".macro-choices").TextContent);
    }

    [Fact]
    public void NoOptionalMacros_OffersNothing()
    {
        WithTwelvePackage();
        Assert.Empty(Render<Consults>().FindAll(".macro-choices"));
    }

    [Fact]
    public void BelowTwelve_OffersNothing_EvenIfMacrosArrive()
    {
        WithTwelvePackage(TwoChoices, specVersion: 11);
        Assert.Empty(Render<Consults>().FindAll(".macro-choices"));
    }

    [Fact]
    public async Task AnUntouchedForm_SendsNoChoices_AndADeviationSendsOnlyItself()
    {
        WithTwelvePackage(TwoChoices);
        IReadOnlyDictionary<string, bool>? sentChoices = new Dictionary<string, bool> { ["sentinel"] = true };
        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>?>(),
                Arg.Any<IReadOnlyDictionary<string, ConsultInputFormRef>?>(),
                Arg.Do<IReadOnlyDictionary<string, bool>?>(choices => sentChoices = choices))
            .Returns(new ConsultGenerationJobStartResponse("job-1", "Queued"));

        var page = Render<Consults>();
        page.Find("fluent-text-area").Change("65M, adenocarcinoma of the lung.");
        await page.FindAll("fluent-button").Last().ClickAsync(new());
        Assert.Null(sentChoices);

        // One deviation on a fresh page (the first submit left the setup
        // phase): the default-true macro unchecked → exactly that id.
        var second = Render<Consults>();
        second.Find("fluent-text-area").Change("65M, adenocarcinoma of the lung.");
        second.FindAll(".macro-choice input[type=checkbox]")[0].Change(false);
        await second.FindAll("fluent-button").Last().ClickAsync(new());
        Assert.NotNull(sentChoices);
        var choice = Assert.Single(sentChoices!);
        Assert.Equal(("disclaimer", false), (choice.Key, choice.Value));
    }
}
