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
        page.FindAll("label.input-field");

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
        page.FindAll("label.input-field")[index];

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
        IReadOnlyDictionary<string, InputFilePayload>? sentFiles = null;
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sentInputs = value),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Do<IReadOnlyDictionary<string, InputFilePayload>?>(value => sentFiles = value))
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
        Assert.Equal("Old records.", System.Text.Encoding.UTF8.GetString(sentFiles["prior_notes"].Content));
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
                Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>())
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
                Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>())
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
                Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>())
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
                Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>())
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
                Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>())
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
                Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>())
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
                Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>())
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
                Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>())
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
