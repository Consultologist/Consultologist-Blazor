using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Auth;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1509 (specVersion 31, area 12, Task 2; D1; part 09 § 5.3): <c>WorkflowPackages/Current/StartChecks</c> answers a start's
/// account checks before a start: the signature block's refusal, then the profile's and the zone's sentences, in a start's
/// order, and <c>needs.profile</c>. The ref is resolved and loaded as <c>Current</c> does. Content is fictional.
/// </summary>
public class V31StartChecksRouteTests
{
    private const string AppUserId = "0123456789abcdef0123456789abcdef";

    private const string Zone = "America/Toronto";

    private const string ClinicBlock = "Dr A. Sample\nSample Oncology Clinic";

    private static readonly Lazy<EngineCapabilities> ThisEngine = new(() => V31Job.Factory().Current());

    private static string Blocks(string? chosen = "sig-1") => JsonSerializer.Serialize(new SignatureBlocks.SignatureBlockSet(
        [
            new("sig-1", "Clinic", ClinicBlock, DateTimeOffset.UnixEpoch),
            new("sig-2", "Ward", "Dr A. Sample\nWard 4", DateTimeOffset.UnixEpoch),
        ],
        chosen));

    [Fact]
    public async Task AnAccountWithEverythingSet_HasNoRefusals()
    {
        var rig = new Rig(zone: Zone, signatures: Blocks(), credentials: "MD, FRCPC");

        var body = await rig.AskAsync(rig.Put("signature", "credentials"));

        Assert.Empty(body.Refusals);
        Assert.Equal(["credentials", "signature"], body.NeedsProfile);
    }

    [Fact]
    public async Task NoTimeZone_SaysS06008()
    {
        var rig = new Rig();

        var body = await rig.AskAsync(rig.Put());

        Assert.Equal(["Set your time zone in your account settings before running a workflow."], body.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("S06-008", body.Refusals[0], ""));
    }

    [Fact]
    public async Task AMissingSignature_SaysS06031_BeforeTheZone()
    {
        var rig = new Rig();

        var body = await rig.AskAsync(rig.Put("signature"));

        Assert.Equal(2, body.Refusals.Count);
        var expected = V31StartChecks.Account(rig.Plan("signature"), new Dictionary<string, string?>(), null);
        Assert.Equal(["S06-031", "S06-008"], expected.Select(r => r.Id));
        Assert.Equal(expected.Select(r => r.Sentence), body.Refusals);
    }

    [Fact]
    public async Task AMissingCredentialsLine_SaysS06032_WithItsLabel()
    {
        var rig = new Rig(zone: Zone);

        var body = await rig.AskAsync(rig.Put("credentials"));

        Assert.Equal(["This workflow needs your credentials. Set it in Profile, under Letter details."], body.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("S06-032", body.Refusals[0], ""));
    }

    [Fact]
    public async Task ASignaturePickNotInTheProfile_SaysA08004()
    {
        var rig = new Rig(zone: Zone, signatures: Blocks());

        var body = await rig.AskAsync(rig.Put("signature"), "sig-9");

        Assert.Equal(["The signature chosen for this job isn't in your profile. Choose one in Profile."], body.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("A08-004", body.Refusals[0], ""));
    }

    [Fact]
    public async Task ASignaturePick_SatisfiesAPackageThatReadsTheSignature_WhenNoBlockIsChosen()
    {
        var rig = new Rig(zone: Zone, signatures: Blocks(chosen: null));
        var packageRef = rig.Put("signature");

        Assert.Single((await rig.AskAsync(packageRef)).Refusals);
        Assert.Empty((await rig.AskAsync(packageRef, "sig-2")).Refusals);
    }

    [Fact]
    public async Task NeedsProfile_IsThePlans()
    {
        var rig = new Rig(zone: Zone);

        Assert.Equal(["closing", "organisation"], (await rig.AskAsync(rig.Put("organisation", "closing"))).NeedsProfile);
        Assert.Empty((await new Rig(zone: Zone).AskPlainAsync()).NeedsProfile);
    }

    [Fact]
    public async Task TheAnswer_CarriesNoProfileValue()
    {
        var rig = new Rig(zone: Zone, signatures: Blocks(), credentials: "MD, FRCPC");

        var response = await rig.GetAsync(rig.Put("signature", "credentials"));

        var text = FakeHttp.TextOf(response);
        Assert.DoesNotContain("Sample Oncology Clinic", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FRCPC", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APackageBelow31_AnswersEmptyLists()
    {
        var rig = new Rig();
        var packageRef = new WorkflowPackageRef("general", "v2026.08.1");
        var manifest = V7Fixtures.Minimal();
        rig.Store.ResolveAsync(packageRef, Arg.Any<CancellationToken>()).Returns(new WorkflowPackage(manifest, Nodes: manifest.Nodes));

        var body = await rig.AskAsync(packageRef);

        Assert.Empty(body.Refusals);
        Assert.Empty(body.NeedsProfile);
    }

    [Fact]
    public async Task A31RefWithThePreviewOff_IsA400_WithS09001()
    {
        var rig = new Rig(preview: null);
        var packageRef = rig.Put();
        rig.Store.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Throws(new WorkflowPackageSpecVersionException($"{packageRef.Name}@{packageRef.Version}", 31, WorkflowPackageStore.SupportedSpecVersions.ToList()));

        var response = await rig.GetAsync(packageRef);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (string)JsonNode.Parse(FakeHttp.TextOf(response))!["error"]!;
        Assert.Equal(SpecVersionAdmissionTests.With(null).NotSupported(31), error);
        V31Contract.AssertSentence(new SourceRefusal("S09-001", error, ""));
    }

    [Fact]
    public async Task ALoadRefusal_IsTheSame400AsCurrent()
    {
        var rig = new Rig();
        var packageRef = rig.Registry.PutChanged(
            CompiledGoldenTests.Cases().Single(c => c.Name == "nested-note"), resolve: r => r["requires"]!["kinds"]!.AsArray().Add("teleport"));
        var load = await rig.Compiled.TryLoadAsync(packageRef, CancellationToken.None);
        Assert.Equal("S07-004", load!.RefusalId);

        var response = await rig.GetAsync(packageRef);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(load.Refusal, (string)JsonNode.Parse(FakeHttp.TextOf(response))!["error"]!);
    }

    [Fact]
    public async Task AnotherAccountsPackage_IsRefusedAsCurrentRefusesIt()
    {
        var rig = new Rig();
        rig.Ownership.OwnsAsync(AppUserId, "acct-fedcba9876543210-note", Arg.Any<CancellationToken>()).Returns(false);

        var response = await rig.GetAsync(new WorkflowPackageRef("acct-fedcba9876543210-note", "v2026.11.1"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Workflow package is not accessible from this account.", (string)JsonNode.Parse(FakeHttp.TextOf(response))!["error"]!);
        Assert.Empty(rig.Registry.Reads);
    }

    [Fact]
    public async Task ABlankSignatureBlockId_IsNoPick()
    {
        var rig = new Rig(zone: Zone, signatures: Blocks());
        var packageRef = rig.Put("signature");

        var blank = await rig.Route.GetStartChecksAsync(FakeHttp.Request("GET", null,
            new Uri($"https://api.example.test/api/WorkflowPackages/Current/StartChecks?ref={packageRef.Name}@{packageRef.Version}&signatureBlockId=%20")));

        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        Assert.Empty(JsonSerializer.Deserialize<WorkflowPackageStartChecksResponse>(FakeHttp.TextOf(blank), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Refusals);
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_Gets401()
    {
        var rig = new Rig();
        rig.Authorizer.AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>()).Returns((AppAccount?)null);

        var response = await rig.GetAsync(rig.Put());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAccountThatCannotUseTheApp_Gets403()
    {
        var rig = new Rig();
        rig.Authorizer.AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>())
            .Returns(AdminDrainTests.Admin.Account(AppUserId) with { Status = AccountStatuses.Disabled });

        var response = await rig.GetAsync(rig.Put());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheRoute_AnswersOptionsWithCors()
    {
        var rig = new Rig();
        var request = FakeHttp.Request("OPTIONS");
        request.Headers.Add("Origin", "https://app.consultologist.ai");

        var response = await rig.Route.GetStartChecksAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://app.consultologist.ai", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        await rig.Authorizer.DidNotReceive().AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAnswer_CarriesCors()
    {
        var rig = new Rig(zone: Zone);
        var request = FakeHttp.Request("GET", null, rig.Url(rig.Put(), null));
        request.Headers.Add("Origin", "https://app.consultologist.ai");

        var response = await rig.Route.GetStartChecksAsync(request);

        Assert.Equal("https://app.consultologist.ai", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    /// <summary>The route over the in-memory registry, the real compiled store and substituted account stores.</summary>
    private sealed class Rig
    {
        public Rig(string? preview = "31", string? zone = null, string? signatures = null, string? credentials = null)
        {
            var admission = SpecVersionAdmissionTests.With(preview);
            Compiled = new CompiledPackageStore(Registry, Registry, ThisEngine, admission, new EngineLimitSettings(new ConfigurationBuilder().Build()));
            Authorizer.AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>()).Returns(AdminDrainTests.Admin.Account(AppUserId));
            Set(AccountSettingKeys.TimeZone, zone);
            Set(AccountSettingKeys.ProfileSignatures, signatures);
            Set(AccountSettingKeys.ProfileCredentials, credentials);
            Route = new V31StartChecksRoute(Authorizer, Pins, Ownership, Store, Substitute.For<IAccountStore>(), Settings,
                NullLogger<V31StartChecksRoute>.Instance, Compiled, admission);
        }

        public FakeRegistry Registry { get; } = new();

        public IAccountAuthorizer Authorizer { get; } = Substitute.For<IAccountAuthorizer>();

        public IWorkflowPackageOwnership Ownership { get; } = Substitute.For<IWorkflowPackageOwnership>();

        public IWorkflowPackagePinResolver Pins { get; } = Substitute.For<IWorkflowPackagePinResolver>();

        public IWorkflowPackageStore Store { get; } = Substitute.For<IWorkflowPackageStore>();

        public IAccountSettingsStore Settings { get; } = Substitute.For<IAccountSettingsStore>();

        public CompiledPackageStore Compiled { get; }

        public V31StartChecksRoute Route { get; }

        private void Set(string key, string? value)
        {
            if (value != null)
                Settings.GetAsync(Arg.Any<string>(), key, Arg.Any<CancellationToken>()).Returns(new AccountSetting(key, value, "text/plain", DateTimeOffset.UnixEpoch));
        }

        /// <summary>
        /// Publishes the minimal package with its note, a <c>markdown_block</c> (the signature and the closing are, #1543), reading
        /// each <paramref name="profile"/> field on its own line.
        /// </summary>
        public WorkflowPackageRef Put(params string[] profile)
        {
            var body = "{{ node.plan.content }}\n\n" + string.Concat(profile.Select(field => "{{ profile." + field + " }}\n"));
            var files = SourceFixture.Minimal().With("deliverables/note.md", SourceFixture.Md(
                SourceFixture.Deliverable("Consultation note", "\"markdown_block\"", "null", "hold"), body));
            var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
            Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Sentence}")));
            return Registry.Put(compiled!.Resolve, compiled.Lock);
        }

        public async Task<WorkflowPackageStartChecksResponse> AskPlainAsync() => await AskAsync(Put());

        public Uri Url(WorkflowPackageRef packageRef, string? signatureBlockId) =>
            new($"https://api.example.test/api/WorkflowPackages/Current/StartChecks?ref={packageRef.Name}@{packageRef.Version}"
                + (signatureBlockId != null ? $"&signatureBlockId={signatureBlockId}" : ""));

        public async Task<HttpResponseData> GetAsync(WorkflowPackageRef packageRef, string? signatureBlockId = null) =>
            await Route.GetStartChecksAsync(FakeHttp.Request("GET", null, Url(packageRef, signatureBlockId)));

        public async Task<WorkflowPackageStartChecksResponse> AskAsync(WorkflowPackageRef packageRef, string? signatureBlockId = null)
        {
            var response = await GetAsync(packageRef, signatureBlockId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonSerializer.Deserialize<WorkflowPackageStartChecksResponse>(FakeHttp.TextOf(response), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }

        /// <summary>The run plan of the package <see cref="Put"/> publishes for <paramref name="profile"/> (the start's own reading).</summary>
        public RunPlan Plan(params string[] profile) => Compiled.TryLoadAsync(Put(profile), CancellationToken.None).GetAwaiter().GetResult()!.Plan!;
    }
}
