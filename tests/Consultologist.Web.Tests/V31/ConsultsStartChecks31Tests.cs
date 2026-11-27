using System.Net;
using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using static Consultologist.Web.Tests.V31.Package31Fixtures;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 11 (#1509): before a 31 start the Create page asks the start's account checks (D1) and says each refusal above
/// Start, which stays disabled until they clear (part 09 § 5.3); it offers a per-job signature choice when the account has
/// more than one block and the package reads the signature (part 09 § 5.2, D6). The checks are asked again on window focus
/// and before submission (Review Focus 5). A 5–30 package asks nothing.
/// </summary>
public class ConsultsStartChecks31Tests : ClientRenderTestContext
{
    private const string Draft = "Margaret Okafor, 58, referred for fatigue and a raised TSH (fictional).";
    private const string NoTimeZone = "Set your time zone in your account settings before running a workflow.";
    private const string NoSignature = "This workflow needs your signature. Choose one in Profile.";
    private const string NoCredentials = "This workflow needs your credentials. Set it in Profile, under Letter details.";
    private const string Requires =
        "This workflow (example-nested-note@v2026.12.1) needs Scriban ^7.2; this engine runs Scriban 8.0.1. Publish a new version of the workflow to run it on this engine.";
    private const string A12004 = "The workflow's checks couldn't be run. Reload the page to try again.";
    private const string Ref = "example-nested-note@v2026.12.1";

    private bool submitted;
    private string? sentSignatureBlockId;

    private void Pin(int specVersion = 31)
    {
        var package = Package(Input("referral", "Referral", true, Plaintext)) with { SpecVersion = specVersion };
        WorkflowService.GetCurrentPackageAsync().Returns(package);
        WorkflowService.GetCurrentPackageContentAsync()
            .Returns<Task<WorkflowPackageContentResponse>>(_ => throw new InvalidOperationException("content unavailable"));

        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>?>(),
                Arg.Any<IReadOnlyDictionary<string, ConsultInputFormRef>?>(),
                Arg.Any<IReadOnlyDictionary<string, bool>?>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>(),
                Arg.Do<string?>(value => { sentSignatureBlockId = value; submitted = true; }))
            .Returns(new ConsultGenerationJobStartResponse("0123456789abcdef0123456789abcdef", "Scheduled"));
    }

    private void Checks(params WorkflowPackageStartChecksResponse[] answers) =>
        WorkflowService.GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(answers[0], answers.Skip(1).ToArray());

    private static WorkflowPackageStartChecksResponse Answer(string[]? refusals = null, string[]? needs = null) =>
        new(refusals ?? [], needs ?? []);

    private static string Blocks(string? chosenId, params string[] ids) =>
        SignatureBlocks.Serialize(new SignatureBlocks.SignatureBlockSet(
            ids.Select(id => new SignatureBlocks.SignatureBlock(id, "Dr A. Sample (" + id + ")", "Dr A. Sample\nSample Oncology Clinic", DateTimeOffset.UnixEpoch)).ToList(),
            chosenId));

    private void Signatures(params string?[] values)
    {
        AccountSettingResponse? Setting(string? value) =>
            value is null ? null : new AccountSettingResponse(SignatureBlocks.SettingKey, value, SignatureBlocks.ContentType, DateTimeOffset.UnixEpoch);

        AccountService.GetSettingAsync(SignatureBlocks.SettingKey)
            .Returns(Setting(values[0]), values.Skip(1).Select(Setting).ToArray());
    }

    private static void Set(IRenderedComponent<Consults> page, string field, object? value)
    {
        typeof(Consults).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(page.Instance, value);
        page.Render();
    }

    private static IElement Start(IRenderedComponent<Consults> page) => page.FindAll("fluent-button").Last();

    private static void Fill(IRenderedComponent<Consults> page) =>
        page.Find("[data-input-id='referral'] fluent-text-area").Change(Draft);

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Consults> page) =>
        page.FindAll(".start-checks31__refusal").Select(r => r.TextContent.Trim()).ToList();

    private static IElement? SignatureSelect(IRenderedComponent<Consults> page) =>
        page.FindAll(".start-checks31__signature fluent-select").SingleOrDefault();

    [Fact]
    public void EachRefusal_IsShownAboveStart_WordForWord_AndStartIsDisabled()
    {
        Pin();
        Checks(Answer([NoSignature, NoCredentials, NoTimeZone], ["credentials", "signature"]));

        var page = Render<Consults>();
        Fill(page);

        Assert.Equal([NoSignature, NoCredentials, NoTimeZone], Refusals(page));
        Assert.True(Start(page).HasAttribute("disabled"));
        // Directly above Start: the action row holding it is the checks' next element.
        Assert.True(page.Find(".start-checks31").NextElementSibling!.ClassList.Contains("action-row"));
        WorkflowService.Received().GetStartChecksAsync(Ref, null);
    }

    [Fact]
    public void NoRefusals_LeaveStartEnabled()
    {
        Pin();
        Checks(Answer());

        var page = Render<Consults>();
        Fill(page);

        Assert.Empty(Refusals(page));
        Assert.False(Start(page).HasAttribute("disabled"));
        Start(page).Click();
        Assert.True(submitted);
    }

    [Fact]
    public void TheProfileSentences_LinkToProfile()
    {
        Pin();
        Checks(Answer([NoSignature, NoTimeZone], ["signature"]));

        var page = Render<Consults>();

        var links = page.FindAll(".start-checks31 a");
        var link = Assert.Single(links);
        Assert.Equal("/profile", link.GetAttribute("href"));
        Assert.Equal("Open Profile", link.TextContent.Trim());
        Assert.Equal([NoSignature, NoTimeZone], Refusals(page));
    }

    [Fact]
    public void ARequiresRefusalFromCurrent_IsShownAboveStart_AndNoInputsAreDrawn()
    {
        WorkflowService.GetCurrentPackageAsync()
            .Returns<Task<WorkflowPackageResponse>>(_ => throw new HttpRequestException(Requires, null, HttpStatusCode.BadRequest));
        WorkflowService.GetCurrentPackageContentAsync()
            .Returns<Task<WorkflowPackageContentResponse>>(_ => throw new InvalidOperationException("content unavailable"));

        var page = Render<Consults>();

        Assert.Equal([Requires], Refusals(page));
        Assert.Empty(page.FindAll("[data-input-id]"));
        Assert.Empty(page.FindAll(".start-checks31 a"));
        Assert.DoesNotContain("Unable to load section standards", page.Markup);
        Assert.True(Start(page).HasAttribute("disabled"));
        // The way out stays: the picker is offered to switch package.
        Assert.NotEmpty(page.FindComponents<Consultologist.Web.Shared.WorkflowEditor.WorkflowPackagePicker>());
    }

    [Theory]
    [InlineData(2, true, true)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(1, false, false)]
    public void TheSignatureSelect_IsShown_OnlyWithMoreThanOneBlock_AndAPackageThatReadsTheSignature(int blocks, bool readsSignature, bool shown)
    {
        Pin();
        Signatures(Blocks("sig-1", new[] { "sig-1", "sig-2" }.Take(blocks).ToArray()));
        Checks(Answer(needs: readsSignature ? ["name", "signature"] : ["name"]));

        var page = Render<Consults>();

        Assert.Equal(shown, SignatureSelect(page) is not null);
    }

    [Fact]
    public void ItStartsOnTheChosenBlock()
    {
        Pin();
        Signatures(Blocks("sig-2", "sig-1", "sig-2", "sig-3"));
        Checks(Answer(needs: ["signature"]));

        var page = Render<Consults>();

        var select = SignatureSelect(page)!;
        Assert.Equal("sig-2", select.GetAttribute("current-value"));
        Assert.Equal(["Dr A. Sample (sig-1)", "Dr A. Sample (sig-2)", "Dr A. Sample (sig-3)"],
            select.QuerySelectorAll("fluent-option").Select(o => o.TextContent.Trim()));
        Assert.NotNull(page.Find(".start-checks31__signature label"));
    }

    [Fact]
    public void WithNoChosenBlock_ItStartsOnChoose()
    {
        Pin();
        Signatures(Blocks(null, "sig-1", "sig-2"));
        Checks(Answer([NoSignature], ["signature"]), Answer(needs: ["signature"]));

        var page = Render<Consults>();
        Fill(page);

        var select = SignatureSelect(page)!;
        Assert.Equal("choose…", select.QuerySelectorAll("fluent-option").First().TextContent.Trim());
        Assert.Equal([NoSignature], Refusals(page));
        Assert.True(Start(page).HasAttribute("disabled"));

        select.Change("sig-1");

        Assert.Empty(Refusals(page));
        Start(page).Click();
        Assert.Equal("sig-1", sentSignatureBlockId);
    }

    [Fact]
    public void PickingAnother_SendsItsId_AndAsksTheChecksAgainWithIt()
    {
        Pin();
        Signatures(Blocks("sig-1", "sig-1", "sig-2"));
        Checks(Answer(needs: ["signature"]));

        var page = Render<Consults>();
        Fill(page);
        SignatureSelect(page)!.Change("sig-2");

        WorkflowService.Received().GetStartChecksAsync(Ref, "sig-2");
        Start(page).Click();
        Assert.True(submitted);
        Assert.Equal("sig-2", sentSignatureBlockId);
    }

    [Fact]
    public void PickingTheChosenBlock_SendsNoSignatureBlockId()
    {
        Pin();
        Signatures(Blocks("sig-1", "sig-1", "sig-2"));
        Checks(Answer(needs: ["signature"]));

        var page = Render<Consults>();
        Fill(page);
        SignatureSelect(page)!.Change("sig-2");
        SignatureSelect(page)!.Change("sig-1");
        Start(page).Click();

        Assert.True(submitted);
        Assert.Null(sentSignatureBlockId);
    }

    [Fact]
    public void EveryStart_CarriesThePick_Overnight_QueueMultiple_AndLive()
    {
        Pin();
        Signatures(Blocks("sig-1", "sig-1", "sig-2"));
        Checks(Answer(needs: ["signature"]));
        var picks = new List<string?>();
        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>?>(),
                Arg.Any<IReadOnlyDictionary<string, ConsultInputFormRef>?>(), Arg.Any<IReadOnlyDictionary<string, bool>?>(),
                Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>(),
                Arg.Do<string?>(picks.Add))
            .Returns(new ConsultGenerationJobStartResponse("0123456789abcdef0123456789abcdef", "Scheduled"));

        var page = Render<Consults>();

        // Overnight.
        Fill(page);
        SignatureSelect(page)!.Change("sig-2");
        Set(page, "runOvernight", true);
        Start(page).Click();

        // Queue multiple.
        Fill(page);
        SignatureSelect(page)!.Change("sig-2");
        Set(page, "queueMultiple", true);
        Start(page).Click();

        // Live.
        Set(page, "queueMultiple", false);
        Fill(page);
        SignatureSelect(page)!.Change("sig-2");
        Start(page).Click();

        Assert.Equal(["sig-2", "sig-2", "sig-2"], picks);
    }

    [Fact]
    public void ThePick_NeverChangesTheAccountsSetting()
    {
        Pin();
        Signatures(Blocks("sig-1", "sig-1", "sig-2"));
        Checks(Answer(needs: ["signature"]));

        var page = Render<Consults>();
        Fill(page);
        SignatureSelect(page)!.Change("sig-2");
        Start(page).Click();

        AccountService.DidNotReceive().SaveSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        AccountService.DidNotReceive().DeleteSettingAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task OnWindowFocus_TheChecksAreAskedAgain()
    {
        Pin();
        Checks(Answer([NoTimeZone]), Answer());

        var page = Render<Consults>();
        Fill(page);
        Assert.Equal([NoTimeZone], Refusals(page));
        Assert.True(Start(page).HasAttribute("disabled"));
        Assert.Single(JSInterop.Invocations["consultologistWindowFocus.listen"]);

        await page.InvokeAsync(() => page.Instance.OnWindowFocusAsync());

        Assert.Empty(Refusals(page));
        Assert.False(Start(page).HasAttribute("disabled"));
        await WorkflowService.Received(2).GetStartChecksAsync(Ref, null);
    }

    [Fact]
    public async Task DisposingThePage_StopsTheFocusListener()
    {
        Pin();
        Checks(Answer());
        JSInterop.Setup<int>("consultologistWindowFocus.listen", _ => true).SetResult(7);

        Render<Consults>();
        await DisposeComponentsAsync();

        var stop = Assert.Single(JSInterop.Invocations["consultologistWindowFocus.stop"]);
        Assert.Equal(7, stop.Arguments[0]);
    }

    [Fact]
    public void BeforeSubmission_TheChecksAreAskedAgain_AndARefusalStopsTheStart()
    {
        Pin();
        Checks(Answer(), Answer([NoTimeZone]));

        var page = Render<Consults>();
        Fill(page);
        Assert.False(Start(page).HasAttribute("disabled"));

        Start(page).Click();

        Assert.False(submitted);
        Assert.Equal([NoTimeZone], Refusals(page));
        Assert.True(Start(page).HasAttribute("disabled"));
    }

    [Fact]
    public async Task APickWhoseBlockWasRemoved_FallsBackToTheChosenBlock()
    {
        Pin();
        Signatures(Blocks("sig-1", "sig-1", "sig-2", "sig-3"), Blocks("sig-1", "sig-1", "sig-2", "sig-3"), Blocks("sig-1", "sig-1", "sig-3"));
        Checks(Answer(needs: ["signature"]));

        var page = Render<Consults>();
        Fill(page);
        SignatureSelect(page)!.Change("sig-2");

        // sig-2 is removed in another tab.
        await page.InvokeAsync(() => page.Instance.OnWindowFocusAsync());

        Assert.Equal("sig-1", SignatureSelect(page)!.GetAttribute("current-value"));
        Start(page).Click();
        Assert.True(submitted);
        Assert.Null(sentSignatureBlockId);
    }

    [Fact]
    public async Task OneBlockLeft_HidesTheSelect_AndSendsNoPick()
    {
        Pin();
        Signatures(Blocks("sig-1", "sig-1", "sig-2"), Blocks("sig-1", "sig-1", "sig-2"), Blocks("sig-2", "sig-2"));
        Checks(Answer(needs: ["signature"]));

        var page = Render<Consults>();
        Fill(page);
        SignatureSelect(page)!.Change("sig-2");

        await page.InvokeAsync(() => page.Instance.OnWindowFocusAsync());

        Assert.Null(SignatureSelect(page));
        Start(page).Click();
        Assert.True(submitted);
        Assert.Null(sentSignatureBlockId);
    }

    [Fact]
    public void WhenTheChecksCannotBeReached_ItSaysA12004_AndStartIsDisabled()
    {
        Pin();
        WorkflowService.GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns<Task<WorkflowPackageStartChecksResponse>>(_ => throw new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable));

        var page = Render<Consults>();
        Fill(page);

        Assert.Equal([A12004], Refusals(page));
        Assert.Empty(page.FindAll(".start-checks31 a"));
        Assert.True(Start(page).HasAttribute("disabled"));
    }

    [Fact]
    public void ALoadRefusalFromTheChecks_IsShownWordForWord()
    {
        Pin();
        WorkflowService.GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns<Task<WorkflowPackageStartChecksResponse>>(_ => throw new HttpRequestException(Requires, null, HttpStatusCode.BadRequest));

        var page = Render<Consults>();
        Fill(page);

        Assert.Equal([Requires], Refusals(page));
        Assert.True(Start(page).HasAttribute("disabled"));
    }

    [Fact]
    public void AStartTheEngineStillRefuses_ShowsTheEnginesSentence()
    {
        Pin();
        Checks(Answer());
        AIService.StartConsultGenerationJobAsync(
                Arg.Any<IReadOnlyDictionary<string, ConsultInputValue>>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>?>(),
                Arg.Any<IReadOnlyDictionary<string, ConsultInputFormRef>?>(), Arg.Any<IReadOnlyDictionary<string, bool>?>(),
                Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<IReadOnlyDictionary<string, InputFilePayload>?>(),
                Arg.Do<string?>(_ => submitted = true))
            .Returns<Task<ConsultGenerationJobStartResponse>>(_ => throw new ConsultGenerationRefusedException(HttpStatusCode.UnprocessableEntity, NoTimeZone));

        var page = Render<Consults>();
        Fill(page);
        Start(page).Click();

        // The engine repeats the checks at the start (part 09 § 5.3): its sentence is shown as the server wrote it.
        Assert.True(submitted);
        Assert.Contains(NoTimeZone, page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Error calling agent", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFocusDuringTheBeforeStartAsk_DoesNotLetARefusalPass()
    {
        // Fix round 1: a focus arriving while the click's own ask is in flight must not replace it with an older clean answer.
        Pin();
        Signatures(Blocks("sig-1", "sig-1", "sig-2"));
        var beforeStart = new TaskCompletionSource<WorkflowPackageStartChecksResponse>();
        var asks = 0;
        WorkflowService.GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => ++asks == 2 ? beforeStart.Task : Task.FromResult(Answer(needs: ["signature"])));

        var page = Render<Consults>();
        Fill(page);

        var clicking = page.InvokeAsync(() => Start(page).ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        Assert.True(SignatureSelect(page)!.HasAttribute("disabled"));
        await page.InvokeAsync(() => page.Instance.OnWindowFocusAsync());
        beforeStart.SetResult(Answer([NoTimeZone], ["signature"]));
        await clicking;

        Assert.False(submitted);
        Assert.Equal([NoTimeZone], Refusals(page));
        Assert.True(Start(page).HasAttribute("disabled"));
    }

    [Fact]
    public void ASignInNeededDuringTheBeforeStartAsk_StopsTheStart()
    {
        Pin();
        var asks = 0;
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        WorkflowService.GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => ++asks == 1
                ? Task.FromResult(Answer())
                : throw new Microsoft.AspNetCore.Components.WebAssembly.Authentication.AccessTokenNotAvailableException(
                    navigation,
                    new Microsoft.AspNetCore.Components.WebAssembly.Authentication.AccessTokenResult(
                        Microsoft.AspNetCore.Components.WebAssembly.Authentication.AccessTokenResultStatus.RequiresRedirect, null!, "authentication/login",
                        new Microsoft.AspNetCore.Components.WebAssembly.Authentication.InteractiveRequestOptions
                        {
                            Interaction = Microsoft.AspNetCore.Components.WebAssembly.Authentication.InteractionType.GetToken,
                            ReturnUrl = "/create",
                        }),
                    null));

        var page = Render<Consults>();
        Fill(page);
        Start(page).Click();

        Assert.False(submitted);
    }

    [Fact]
    public void AFailureDuringTheBeforeStartAsk_StopsTheStart()
    {
        Pin();
        var asks = 0;
        WorkflowService.GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => ++asks == 1 ? Task.FromResult(Answer()) : throw new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable));

        var page = Render<Consults>();
        Fill(page);
        Start(page).Click();

        Assert.False(submitted);
        Assert.Equal([A12004], Refusals(page));
    }

    [Fact]
    public async Task AnUnreachableAnswer_KeepsTheSignatureChoiceAndThePick()
    {
        Pin();
        Signatures(Blocks("sig-1", "sig-1", "sig-2"));
        var asks = 0;
        WorkflowService.GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => ++asks <= 2 ? Task.FromResult(Answer(needs: ["signature"])) : throw new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable));

        var page = Render<Consults>();
        Fill(page);
        SignatureSelect(page)!.Change("sig-2");

        await page.InvokeAsync(() => page.Instance.OnWindowFocusAsync());

        Assert.Equal([A12004], Refusals(page));
        Assert.Equal("sig-2", SignatureSelect(page)!.GetAttribute("current-value"));
        Assert.True(Start(page).HasAttribute("disabled"));
    }

    [Fact]
    public async Task AfterAnUnreachableAnswer_AFocusThatSucceeds_EnablesStart()
    {
        Pin();
        var asks = 0;
        WorkflowService.GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => ++asks == 1 ? throw new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable) : Task.FromResult(Answer()));

        var page = Render<Consults>();
        Fill(page);
        Assert.Equal([A12004], Refusals(page));
        Assert.True(Start(page).HasAttribute("disabled"));

        await page.InvokeAsync(() => page.Instance.OnWindowFocusAsync());

        Assert.Empty(Refusals(page));
        Assert.False(Start(page).HasAttribute("disabled"));
        Start(page).Click();
        Assert.True(submitted);
    }

    [Fact]
    public async Task DisposingWhileTheListenerRegisters_StopsItOnceRegistered()
    {
        Pin();
        Checks(Answer());
        var listen = JSInterop.Setup<int>("consultologistWindowFocus.listen", _ => true);

        Render<Consults>();
        await DisposeComponentsAsync();
        Assert.Empty(JSInterop.Invocations["consultologistWindowFocus.stop"]);

        listen.SetResult(9);
        for (var wait = 0; wait < 100 && JSInterop.Invocations["consultologistWindowFocus.stop"].Count == 0; wait++)
        {
            await Task.Delay(10);
        }

        var stop = Assert.Single(JSInterop.Invocations["consultologistWindowFocus.stop"]);
        Assert.Equal(9, stop.Arguments[0]);
    }

    [Fact]
    public async Task AnOlderAsksSettingsRead_LandingAfterAPick_DoesNotResetThePick()
    {
        // Final review C1: a focus's ask reads the blocks; the clinician picks while that read is out; the older ask's answer
        // must not put the pick back on the chosen block.
        Pin();
        var held = new TaskCompletionSource<AccountSettingResponse?>();
        var reads = 0;
        var setting = new AccountSettingResponse(SignatureBlocks.SettingKey, Blocks("sig-1", "sig-1", "sig-2"), SignatureBlocks.ContentType, DateTimeOffset.UnixEpoch);
        AccountService.GetSettingAsync(SignatureBlocks.SettingKey).Returns(_ => ++reads == 2 ? held.Task : Task.FromResult<AccountSettingResponse?>(setting));
        Checks(Answer(needs: ["signature"]));

        var page = Render<Consults>();
        Fill(page);

        var focusing = page.InvokeAsync(() => page.Instance.OnWindowFocusAsync());
        SignatureSelect(page)!.Change("sig-2");
        held.SetResult(setting);
        await focusing;

        Assert.Equal("sig-2", SignatureSelect(page)!.GetAttribute("current-value"));
        Start(page).Click();
        Assert.True(submitted);
        Assert.Equal("sig-2", sentSignatureBlockId);
    }

    [Fact]
    public async Task APackageSwitch_DuringTheBeforeStartAsk_StartsNothing()
    {
        // Final review C2: Create pressed on a 31 package; the clinician pins a 30 package while the ask is out. The old ask's
        // answer is not the new package's: nothing starts.
        Pin();
        var beforeStart = new TaskCompletionSource<WorkflowPackageStartChecksResponse>();
        var asks = 0;
        WorkflowService.GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => ++asks == 2 ? beforeStart.Task : Task.FromResult(Answer()));

        var page = Render<Consults>();
        Fill(page);

        var clicking = page.InvokeAsync(() => Start(page).ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        WorkflowService.GetCurrentPackageAsync().Returns(Package(Input("referral", "Referral", true, Plaintext)) with { SpecVersion = 30 });
        await page.InvokeAsync(() => (Task)typeof(Consults)
            .GetMethod("OnPackagePinnedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(page.Instance, null)!);
        beforeStart.SetResult(Answer());
        await clicking;

        Assert.False(submitted);
    }

    [Fact]
    public void A30Package_AsksNoChecks()
    {
        Pin(specVersion: 30);

        var page = Render<Consults>();

        WorkflowService.DidNotReceive().GetStartChecksAsync(Arg.Any<string?>(), Arg.Any<string?>());
        AccountService.DidNotReceive().GetSettingAsync(SignatureBlocks.SettingKey);
        Assert.Empty(page.FindAll(".start-checks31"));
        Assert.Empty(JSInterop.Invocations["consultologistWindowFocus.listen"]);
    }
}
