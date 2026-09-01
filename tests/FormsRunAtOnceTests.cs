using Consultologist.Api.Auth;
using Consultologist.Api.Forms;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #543: the run half of the forms door, tested directly (no HttpRequestData
/// harness exists — the HTTP shell is the named gap every door has). The
/// origin must be APP-shaped: the account's confirmed address and its #518
/// choice, never the respondent; a misfit refuses the whole start by name
/// and the starter is never called.
/// </summary>
public class FormsRunAtOnceTests
{
    private const string Referral =
        "65M, newly diagnosed adenocarcinoma of the lung, stage IIIA, for consideration of chemoradiation. PMHx HTN.";

    private readonly IAccountSettingsStore _settings = Substitute.For<IAccountSettingsStore>();
    private readonly IConsultGenerationJobStarter _starter = Substitute.For<IConsultGenerationJobStarter>();
    private readonly IWorkflowPackagePinResolver _pinResolver = Substitute.For<IWorkflowPackagePinResolver>();
    private readonly IWorkflowPackageStore _packageStore = Substitute.For<IWorkflowPackageStore>();
    private readonly DurableTaskClient _client = Substitute.For<DurableTaskClient>("test");

    private FormsIntake CreateDoor()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(V7Fixtures.Minimal() with
            {
                SpecVersion = 8,
                Inputs = new List<WorkflowInputSpec>
                {
                    new("consult_draft", "Consult draft"),
                    new("urgent", "Urgency", Required: false, Type: "enum", Values: new List<string> { "Routine", "Urgent" })
                }
            }));

        return new FormsIntake(
            Substitute.For<IAccountAuthorizer>(),
            Substitute.For<IAccountStore>(),
            Substitute.For<IFormResponseBlobStore>(),
            Substitute.For<IFormResponseStore>(),
            _settings,
            _starter,
            _pinResolver,
            _packageStore,
            NullLogger<FormsIntake>.Instance);
    }

    private void WithSetting(string key, string? value) =>
        _settings.GetAsync("user-1", key, Arg.Any<CancellationToken>())
            .Returns(value == null ? null : new AccountSetting(key, value, "text/plain", DateTimeOffset.UtcNow));

    [Fact]
    public async Task AStart_IsAppShaped_TheAccountsAddressAndItsChoice_NeverTheRespondent()
    {
        WithSetting(AccountSettingKeys.DeliveryAddress, "doc@clinic.example");
        WithSetting(AccountSettingKeys.EmailPdf, "false");
        _starter.StartAsync(Arg.Any<DurableTaskClient>(), Arg.Any<ConsultGenerationRequest>(), Arg.Any<string>(), Arg.Any<ConsultGenerationJobOrigin>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultGenerationJobStartOutcome("job-1"));

        var result = await CreateDoor().StartRunAsync(
            _client, "user-1", "triage-intake", "17",
            new Dictionary<string, string> { ["consult_draft"] = Referral, ["urgent"] = "Urgent" },
            CancellationToken.None);

        await _starter.Received(1).StartAsync(
            _client,
            Arg.Is<ConsultGenerationRequest>(request =>
                request.WorkflowPackage == null
                && request.Inputs!["consult_draft"].Text == Referral
                && request.Inputs["urgent"].Text == "Urgent"
                && request.InputFormRefs!["consult_draft"] == new ConsultInputFormRef("triage-intake", "17")
                && request.InputFormRefs["urgent"] == new ConsultInputFormRef("triage-intake", "17")),
            "user-1",
            Arg.Is<ConsultGenerationJobOrigin>(origin =>
                origin.Source == ConsultGenerationJobSources.Forms
                && origin.ReplyToAddress == "doc@clinic.example"
                && origin.EmailRequested == false),
            Arg.Any<CancellationToken>());
        Assert.Contains("job-1", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task NoEmailChoice_MeansRequested_AndNoAddressMeansNull()
    {
        // The default-true trap, inverted into an assertion: an account that
        // chose nothing gets the pre-#518 behaviour, not silence.
        WithSetting(AccountSettingKeys.DeliveryAddress, null);
        WithSetting(AccountSettingKeys.EmailPdf, null);
        _starter.StartAsync(Arg.Any<DurableTaskClient>(), Arg.Any<ConsultGenerationRequest>(), Arg.Any<string>(), Arg.Any<ConsultGenerationJobOrigin>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultGenerationJobStartOutcome("job-1"));

        await CreateDoor().StartRunAsync(
            _client, "user-1", "triage-intake", "17",
            new Dictionary<string, string> { ["consult_draft"] = Referral },
            CancellationToken.None);

        await _starter.Received(1).StartAsync(
            _client, Arg.Any<ConsultGenerationRequest>(), "user-1",
            Arg.Is<ConsultGenerationJobOrigin>(origin =>
                origin.ReplyToAddress == null && origin.EmailRequested == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMisfit_RefusesTheWholeStart_AndTheStarterIsNeverCalled()
    {
        // E2's Other answer: named, and nothing runs — the response stays held.
        var result = await CreateDoor().StartRunAsync(
            _client, "user-1", "triage-intake", "17",
            new Dictionary<string, string> { ["consult_draft"] = Referral, ["urgent"] = "As soon as the family arrives" },
            CancellationToken.None);

        var serialized = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("urgent", serialized);
        Assert.Contains("is not one of the declared values", serialized);
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task AnUnreadablePin_HoldsWithoutStarting()
    {
        // The door first (its fixture stubs a good package), then the throw —
        // last stub wins in NSubstitute.
        var door = CreateDoor();
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns<WorkflowPackage>(_ => throw new InvalidOperationException("registry down"));

        var result = await door.StartRunAsync(
            _client, "user-1", "triage-intake", "17",
            new Dictionary<string, string> { ["consult_draft"] = Referral },
            CancellationToken.None);

        Assert.Contains("held for the picker", System.Text.Json.JsonSerializer.Serialize(result));
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task AStarterRefusal_ReachesTheFlow_ByTheAppDoorsDisclosureRule()
    {
        WithSetting(AccountSettingKeys.DeliveryAddress, null);
        WithSetting(AccountSettingKeys.EmailPdf, null);
        _starter.StartAsync(Arg.Any<DurableTaskClient>(), Arg.Any<ConsultGenerationRequest>(), Arg.Any<string>(), Arg.Any<ConsultGenerationJobOrigin>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultGenerationJobStartOutcome(null, ConsultGenerationJobStartError.RateLimited, "Rate limit reached."));

        var result = await CreateDoor().StartRunAsync(
            _client, "user-1", "triage-intake", "17",
            new Dictionary<string, string> { ["consult_draft"] = Referral },
            CancellationToken.None);

        Assert.Contains("Rate limit reached.", System.Text.Json.JsonSerializer.Serialize(result));
    }

    // ----- the pure request builder -----

    private static List<WorkflowInputSpec> Declarations() => new()
    {
        new("consult_draft", "Consult draft"),
        new("urgent", "Urgency", Required: false, Type: "enum", Values: new List<string> { "Routine", "Urgent" })
    };

    [Fact]
    public void TheBuilder_OmitsBlanks_IgnoresUndeclared_AndRefsEveryIncludedId()
    {
        var (request, refusal) = FormsIntake.BuildRunRequest(
            Declarations(),
            new Dictionary<string, string>
            {
                ["consult_draft"] = Referral,
                ["urgent"] = "",
                ["never_declared"] = "ignored entirely"
            },
            "triage-intake", "17");

        Assert.Null(refusal);
        Assert.Equal(new[] { "consult_draft" }, request!.Inputs!.Keys);
        Assert.Equal(new[] { "consult_draft" }, request.InputFormRefs!.Keys);
    }

    [Fact]
    public void AnswersForNoDeclaredInput_AreRefusedByName()
    {
        var (request, refusal) = FormsIntake.BuildRunRequest(
            Declarations(),
            new Dictionary<string, string> { ["never_declared"] = "x" },
            "triage-intake", "17");

        Assert.Null(request);
        Assert.Equal("The response answers none of the package's declared inputs.", refusal);
    }
}
