using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Migration;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Publish;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PublishedTypeRegistry = Consultologist.Api.V31.PublishedTypeRegistry;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 36; part 10 § 1.5; R11, R18, R21; ruled on #1508, comments 5 to 8): the operator's
/// migrate-and-publish of one account package (<see cref="MigratePackage"/>) and its route (<c>Admin/MigratePackage</c>),
/// over substitutes for the accounts, the pins, the stored packages, the registry, the settings and the alerts, with the
/// vendored 30 packages as stored: <c>example-two-documents</c> migrates cleanly, <c>example-problem-list</c> has a hand edit
/// (its fixture's edits make it), <c>example-nested-note</c> passes the instances one job may open (A11-001). A package that
/// cannot be published is <c>not migrated: &lt;sentence&gt;</c> and nothing is written: its 30 version and its pin are as
/// they were. Content is fictional (Dr A. Sample's snapshot profile).
/// </summary>
public class MigratePackageTests
{
    private const string OwnerId = "0123456789abcdef0123456789abcdef";
    private const string OperatorId = "fedcba9876543210fedcba9876543210";
    private const string Name = "acct-0123456789ab-letters";
    private const string Kind = SignInKinds.Organisation;

    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 30, 0, TimeSpan.Zero);

    private static readonly Lazy<PublishedTypeRegistry> Types = new(() => new PublishedTypeRegistry(AppContext.BaseDirectory, OutputContractCatalog.ResolveDirectory()));

    /// <summary>The operation over its substitutes: the owner's pin names <see cref="Name"/>, nothing is published yet, every write wins.</summary>
    private sealed class Rig
    {
        public IAccountStore Accounts { get; } = Substitute.For<IAccountStore>();
        public IWorkflowPackagePinResolver Pins { get; } = Substitute.For<IWorkflowPackagePinResolver>();
        public IWorkflowPackageSources Sources { get; } = Substitute.For<IWorkflowPackageSources>();
        public IV31RegistryWriter Registry { get; } = Substitute.For<IV31RegistryWriter>();
        public IWorkflowPackageRegistryWriter Writer { get; } = Substitute.For<IWorkflowPackageRegistryWriter>();
        public IAccountSettingsStore Settings { get; } = Substitute.For<IAccountSettingsStore>();
        public IWorkflowPackageOwnership Ownership { get; } = Substitute.For<IWorkflowPackageOwnership>();
        public IOperatorAlerts Alerts { get; } = Substitute.For<IOperatorAlerts>();
        public CapturingLogger<MigratePackage> Log { get; } = new();
        public CapturingLogger<V31PackagePublisher> PublishLog { get; } = new();

        /// <summary>The engine's configured limits; by default its unset settings, the spec's starting values.</summary>
        public EngineLimitSettings Limits { get; init; } = new(new ConfigurationBuilder().Build());

        /// <summary>Stands in for <see cref="PackagePublish.RunAsync"/> when set.</summary>
        public Func<IReadOnlyDictionary<string, byte[]>, PublishOptions, Task<PublishOutcome>>? Pipeline { get; set; }

        /// <summary>What each <see cref="IV31RegistryWriter.WriteAsync"/> was given, in order.</summary>
        public List<(string Version, IReadOnlyList<PublishFile> Files, PublishedStamp Stamp)> Writes { get; } = [];

        public Rig(string package = "example-two-documents", string version = "v2026.11.3")
        {
            Accounts.GetAccountKindAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(Kind);
            Pins.ResolvePinAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(new WorkflowPackageRef(Name, "latest"));
            Stored(package, version);
            // NSubstitute answers "" for an unstubbed string: the name has no latest pointer
            Writer.ReadLatestVersionAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
            Registry.WriteAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<PublishFile>>(),
                    Arg.Any<PublishedStamp>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Writes.Add(((string)call[2], (IReadOnlyList<PublishFile>)call[3], (PublishedStamp)call[4]));
                    return true;
                });
        }

        /// <summary>The owner's pinned package, as the store reads it: a vendored 30 package at <paramref name="version"/>.</summary>
        public void Stored(string package, string version)
        {
            var (manifestJson, files) = ByteEquivalenceFixtures.ReadPackage(package);
            Stored(new WorkflowPackageSource($"{Name}@{version}", 30, manifestJson, files, TestFormats.Catalog));
        }

        public void Stored(WorkflowPackageSource source) =>
            Sources.ReadSourceAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>()).Returns(source);

        public MigratePackage Operation()
        {
            var publisher = new V31PackagePublisher(
                Registry, Writer, Settings, Ownership, Types.Value, V31Job.Toolchain.Value,
                Limits, Alerts, new FixedTimeProvider(Now), PublishLog,
                new HashSet<string>(StringComparer.Ordinal) { OperatorId })
            {
                Pipeline = (upload, options) => Pipeline?.Invoke(upload, options) ?? PackagePublish.RunAsync(upload, options),
            };
            return new MigratePackage(
                Accounts, Pins, Sources, publisher, Types.Value, V31Job.Toolchain.Value, TestFormats.Catalog, OutputContractCatalog.Load(), Limits, Log);
        }

        public Task<JsonObject> RunAsync(MigratePackageRequest request) => Operation().RunAsync(request, CancellationToken.None);

        /// <summary>No version written, no pointer set, no pin moved: the 30 version and the pin are as they were.</summary>
        public void AssertNothingWritten()
        {
            Assert.Empty(Writes);
            Registry.DidNotReceiveWithAnyArgs().WriteAsync(default, default!, default!, default!, default!, default);
            Writer.DidNotReceiveWithAnyArgs().SetLatestPointerAsync(default, default!, default!, default);
            Writer.DidNotReceiveWithAnyArgs().UploadFileAsync(default, default!, default!, default!, default!, default);
            Settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!, default!, default);
            Ownership.DidNotReceiveWithAnyArgs().RecordAsync(default!, default!, default);
        }
    }

    private static MigratePackageRequest Request(
        JsonObject? options = null, IReadOnlyDictionary<string, string?>? handEdits = null, bool publish = true, string name = Name) =>
        new(OwnerId, name, options, handEdits ?? new Dictionary<string, string?>(), publish);

    /// <summary>The package's <c>options.json</c>, as the operator keeps it beside its hand edits.</summary>
    private static JsonObject OptionsOf(string package) =>
        JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(MigrationFixture.HandEditsOf(package), MigrationOptionsFile.FileName)))!.AsObject();

    /// <summary>The fixture's hand edits as the request carries them: each file's text by its path, a <c>.deleted</c> file as null.</summary>
    private static Dictionary<string, string?> HandEditsOf(string package)
    {
        var folder = MigrationFixture.HandEditsOf(package);
        var edits = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative is "README.md" or "keys.json" or MigrationOptionsFile.FileName)
                continue;
            if (relative.EndsWith(".deleted", StringComparison.Ordinal))
                edits[relative[..^".deleted".Length]] = null;
            else
                edits[relative] = ByteEquivalenceFixtures.ReadText(path);
        }

        return edits;
    }

    private static string[] Strings(JsonNode? node) => [.. node!.AsArray().Select(n => (string)n!)];

    private static string Text(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);

    // ---- publish ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ACleanPackage_IsPublishedAsTheOwnersNextVersion_RecordedAsAMigration_AndThePinMoves()
    {
        var rig = new Rig();

        var answer = await rig.RunAsync(Request());

        Assert.Equal(MigratePackage.Published, (string?)answer["outcome"]);
        Assert.Equal(["owner", "name", "oldVersion", "newVersion", "outcome", "handEditsLeft", "notices", "report", "options", "defaults"], answer.Select(p => p.Key));
        Assert.Equal(OwnerId, (string?)answer["owner"]);
        Assert.Equal(Name, (string?)answer["name"]);
        Assert.Equal("v2026.11.3", (string?)answer["oldVersion"]);
        Assert.Equal("v2026.12.1", (string?)answer["newVersion"]);
        Assert.Empty(Strings(answer["handEditsLeft"]));
        Assert.Contains(MigrationReport.OneMessageEach, Strings(answer["notices"]));

        // the stamp says it was migrated, the report says by whom, the migration report is stored in the version it names
        var (version, files, stamp) = Assert.Single(rig.Writes);
        Assert.Equal("v2026.12.1", version);
        Assert.Equal("v2026.11.3", stamp.MigratedFrom);
        Assert.Equal(V31PackagePublisher.ByOperatorMigration, (string?)answer["report"]!["publishedBy"]);
        Assert.Equal("published", (string?)answer["report"]!["outcome"]);
        var migration = JsonNode.Parse(Text(files.Single(f => f.Path == PackagePublish.MigrationReportFile).Bytes))!;
        Assert.Equal("v2026.11.3", (string?)migration["oldVersion"]);
        Assert.Equal("v2026.12.1", (string?)migration["newVersion"]);
        Assert.Equal(Name, (string?)JsonNode.Parse(Text(files.Single(f => f.Path == "manifest.json").Bytes))!["name"]);

        // the pin moves as any publish moves it, as the owner's package
        await rig.Writer.Received(1).SetLatestPointerAsync(Kind, Name, "v2026.12.1", Arg.Any<CancellationToken>());
        await rig.Settings.Received(1).SaveAsync(OwnerId, WorkflowPackagePinResolver.PackagePinSettingKey, $"{Name}@v2026.12.1", "text/plain", Arg.Any<CancellationToken>());
        await rig.Ownership.Received(1).RecordAsync(OwnerId, Name, Arg.Any<CancellationToken>());

        // no options.json: the defaults, said
        Assert.Equal(MigrationOptionsFile.Defaults, (string?)answer["defaults"]);
        Assert.Equal("""{"renames":{},"whenEmpty":{},"folderFormats":{}}""", answer["options"]!.ToJsonString());
        Assert.True(JsonNode.DeepEquals(answer["options"], migration["options"]));
    }

    [Fact]
    public async Task NoMemberOrLogLine_CarriesInputOrProfileText()
    {
        var rig = new Rig();

        var answer = (await rig.RunAsync(Request())).ToJsonString();

        Assert.Equal(MigratePackage.Published, (string?)JsonNode.Parse(answer)!["outcome"]);
        foreach (var value in SnapshotTest.Profile.Values.Append("Stub reply"))
        {
            Assert.DoesNotContain(value, answer, StringComparison.Ordinal);
            Assert.DoesNotContain(value, rig.Log.Everything, StringComparison.Ordinal);
            Assert.DoesNotContain(value, rig.PublishLog.Everything, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ADryRun_WouldPublish_AndWritesNothing()
    {
        var rig = new Rig();

        var answer = await rig.RunAsync(Request(publish: false));

        Assert.Equal(MigratePackage.WouldPublish, (string?)answer["outcome"]);
        Assert.Equal("v2026.12.1", (string?)answer["newVersion"]);
        Assert.Equal("published", (string?)answer["report"]!["outcome"]);
        rig.AssertNothingWritten();
        Assert.Empty(rig.Alerts.ReceivedCalls());
    }

    [Fact]
    public async Task TheOperatorsHandEdits_AreLaidOverTheMigration_AndThePackagePublishes()
    {
        var rig = new Rig("example-problem-list", "v2026.11.2");
        var edits = HandEditsOf("example-problem-list");
        Assert.Contains(edits, e => e.Value is null);

        var answer = await rig.RunAsync(Request(OptionsOf("example-problem-list"), edits));

        Assert.Equal(MigratePackage.Published, (string?)answer["outcome"]);
        Assert.Null((string?)answer["defaults"]);
        Assert.Empty(Strings(answer["handEditsLeft"]));
        // the snapshot test is written anew over the edited package, so A10-011 is not said
        Assert.DoesNotContain("No snapshot test was written: hand edits remain.", Strings(answer["notices"]));
        var (_, files, _) = Assert.Single(rig.Writes);
        Assert.Equal(edits["snippets/sections/follow_up_plan.md"], Text(files.Single(f => f.Path == "snippets/sections/follow_up_plan.md").Bytes));
        Assert.DoesNotContain(files, f => f.Path == "nodes/sections/section_draft.md");
        Assert.Contains(files, f => f.Path == SnapshotTest.File);
        var migration = JsonNode.Parse(Text(files.Single(f => f.Path == PackagePublish.MigrationReportFile).Bytes))!;
        Assert.NotEmpty(Strings(migration["handEdits"]));
    }

    // ---- not migrated: nothing written --------------------------------------------------------------------------------

    [Fact]
    public async Task AHandEditLeft_IsNotMigrated_AndNothingIsWritten()
    {
        var rig = new Rig("example-problem-list", "v2026.11.2");

        var answer = await rig.RunAsync(Request(OptionsOf("example-problem-list")));

        var left = Strings(answer["handEditsLeft"]);
        Assert.NotEmpty(left);
        Assert.All(left, edit => Assert.StartsWith("Hand edit needed in ", edit, StringComparison.Ordinal));
        Assert.Equal(MigratePackage.NotMigrated + left[0], (string?)answer["outcome"]);
        Assert.Equal("v2026.11.2", (string?)answer["oldVersion"]);
        Assert.Null((string?)answer["newVersion"]);
        Assert.Null(answer["report"]);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task APackagePastTheInstancesLimit_IsNotMigrated_WithA11001_ItsVersionAndPinAsTheyWere()
    {
        // B2 and comment 8: no max is guessed. The operator's edits leave the loops as they are, so publish refuses it.
        var rig = new Rig("example-nested-note", "v2026.11.2");
        var options = OptionsOf("example-nested-note");
        var (manifestJson, files) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        var migrated = WorkflowPackageMigration30To31.Migrate(manifestJson, files, Migration31Options.FromJson(
            options, Types.Value.Registry, V31Job.Toolchain.Value.Current(), TestFormats.Catalog).Options!);
        var unchanged = new Dictionary<string, string?> { ["nodes/sections/index.json"] = migrated.Files["nodes/sections/index.json"] };

        foreach (var publish in new[] { true, false })
        {
            var answer = await rig.RunAsync(Request(options, unchanged, publish));

            var outcome = (string)answer["outcome"]!;
            Assert.StartsWith(MigratePackage.NotMigrated + "Up to ", outcome, StringComparison.Ordinal);
            V31Contract.AssertSentence(new SourceRefusal("A11-001", outcome[MigratePackage.NotMigrated.Length..], ""));
            Assert.Null((string?)answer["newVersion"]);
        }

        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task AHandEditWhoseFileIsNotAmongTheEdits_IsLeft_AndNothingIsWritten()
    {
        // Fix round 1, ruling (1): edge-unruled-whitespace has two hand edits, in nodes/follow_up_applies.md (the classifier's
        // question) and macros/problem_close.md (the instruction). The operator answers the first only: the second is left.
        const string package = "edge-unruled-whitespace";
        var rig = new Rig(package, "v2026.11.1");
        var options = OptionsOf(package);
        var all = HandEditsOf(package);
        var one = new Dictionary<string, string?> { ["nodes/follow_up_applies.md"] = all["nodes/follow_up_applies.md"] };

        var answer = await rig.RunAsync(Request(options, one));

        var left = Strings(answer["handEditsLeft"]);
        var edit = Assert.Single(left);
        Assert.StartsWith("Hand edit needed in macros/problem_close.md, ", edit, StringComparison.Ordinal);
        Assert.Equal(MigratePackage.NotMigrated + edit, (string?)answer["outcome"]);
        Assert.Null(answer["report"]);
        rig.AssertNothingWritten();

        // every file a hand edit names among the edits: it goes on, and publishes
        var published = await rig.RunAsync(Request(options, all));
        Assert.Equal(MigratePackage.Published, (string?)published["outcome"]);
        Assert.Empty(Strings(published["handEditsLeft"]));
        Assert.Single(rig.Writes);
    }

    [Fact]
    public async Task AtTheConfiguredLimits_APackageOverTheDefaultButWithinThem_Publishes()
    {
        // Fix round 1, ruling (2): example-nested-note opens 80,614 instances, past the default 10,000. At a deployment's
        // JobInstances of 100,000 the migration says no hand edit for it, and the package publishes as migrated.
        var rig = new Rig("example-nested-note", "v2026.11.2")
        {
            Limits = new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConsultGeneration:JobInstances"] = "100000" })
                .Build()),
        };

        var answer = await rig.RunAsync(Request(OptionsOf("example-nested-note")));

        Assert.Equal(MigratePackage.Published, (string?)answer["outcome"]);
        Assert.Empty(Strings(answer["handEditsLeft"]));
        var (_, files, _) = Assert.Single(rig.Writes);
        Assert.Contains(files, f => f.Path == SnapshotTest.File);

        // and with an edit given, the snapshot written anew publishes at the same limits
        var (manifestJson, sources) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        var migrated = WorkflowPackageMigration30To31.Migrate(manifestJson, sources, Migration31Options.FromJson(
            OptionsOf("example-nested-note"), Types.Value.Registry, V31Job.Toolchain.Value.Current(), TestFormats.Catalog).Options! with
        {
            Limits = rig.Limits.PublishLimits,
        });
        var edited = await rig.RunAsync(Request(OptionsOf("example-nested-note"),
            new Dictionary<string, string?> { ["nodes/sections/index.json"] = migrated.Files["nodes/sections/index.json"] }));
        Assert.Equal(MigratePackage.Published, (string?)edited["outcome"]);
    }

    [Fact]
    public async Task APublishRefusal_IsNotMigrated_WithItsFirstSentence_AndNothingIsWritten()
    {
        var rig = new Rig
        {
            Pipeline = (_, options) => Task.FromResult(new PublishOutcome(
                new PublishReport(Name, "v2026.12.1", false, null, options.PublishedBy, SourceHash.Of(new Dictionary<string, string>()),
                    null, null, [], [PackagePublishSentences.EngineFailed(nameof(InvalidOperationException))], [], []),
                [], null, null, [])),
        };

        var answer = await rig.RunAsync(Request());

        Assert.Equal(MigratePackage.NotMigrated + PackagePublishSentences.EngineFailed().Sentence, (string?)answer["outcome"]);
        Assert.Equal("refused", (string?)answer["report"]!["outcome"]);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task APackageAt31Already_IsNotMigrated_WithASentenceSayingSo()
    {
        // an idempotent rerun: the pin names the 31 version the last run published
        var rig = new Rig();
        rig.Stored(new WorkflowPackageSource($"{Name}@v2026.12.1", 31, "{\"specVersion\": 31}", new Dictionary<string, string>(), null));

        var answer = await rig.RunAsync(Request());

        Assert.Equal(MigratePackage.NotMigrated + "The package is already at specVersion 31.", (string?)answer["outcome"]);
        Assert.Equal("v2026.12.1", (string?)answer["oldVersion"]);
        rig.AssertNothingWritten();
    }

    [Theory]
    [InlineData(Name, "acct-0123456789ab-other")]
    [InlineData("general", "general")]
    public async Task APackageThePinDoesNotName_OrNoAccountPackage_IsNotMigrated_AndNothingIsRead(string pinned, string name)
    {
        var rig = new Rig();
        rig.Pins.ResolvePinAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(new WorkflowPackageRef(pinned, "latest"));

        var answer = await rig.RunAsync(Request(name: name));

        var outcome = (string)answer["outcome"]!;
        Assert.Equal(MigratePackage.NotMigrated + PackagePublishSentences.NotTheActivePackage(name), outcome);
        V31Contract.AssertSentence(new SourceRefusal("A11-016", outcome[MigratePackage.NotMigrated.Length..], ""));
        Assert.Empty(rig.Sources.ReceivedCalls());
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task OptionsItCannotRead_AreNotMigrated_WithTheirSentence()
    {
        var rig = new Rig();

        var answer = await rig.RunAsync(Request(new JsonObject { ["renames"] = new JsonObject(), ["whenEmpty"] = new JsonObject() }));

        Assert.Equal(MigratePackage.NotMigrated + "options.json must give folderFormats as an object whose values are text; write {} for none.", (string?)answer["outcome"]);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task AStoredPackageThatCannotBeRead_IsNotMigrated_TheFaultLoggedByItsType()
    {
        var rig = new Rig();
        rig.Sources.ReadSourceAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns<WorkflowPackageSource>(_ => throw new InvalidOperationException("a blob for Margaret Okafor"));

        var answer = await rig.RunAsync(Request());

        Assert.Equal(MigratePackage.NotMigrated + PackagePublishSentences.EngineFailed().Sentence, (string?)answer["outcome"]);
        Assert.Contains(nameof(InvalidOperationException), rig.Log.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Margaret Okafor", rig.Log.Everything, StringComparison.Ordinal);
        rig.AssertNothingWritten();
    }

    // ---- the list -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheList_IsEveryAccountsActiveAccountPackageBelow31()
    {
        var rig = new Rig();
        const string At31 = "11111111111111111111111111111111", OnGeneral = "22222222222222222222222222222222", Unread = "33333333333333333333333333333333";
        rig.Accounts.ListAsync(Arg.Any<CancellationToken>()).Returns(
            [new AccountSummary(OwnerId, "Active"), new AccountSummary(At31, "Active"), new AccountSummary(OnGeneral, "Active"), new AccountSummary(Unread, "Active")]);
        rig.Pins.ResolvePinAsync(At31, Arg.Any<CancellationToken>()).Returns(new WorkflowPackageRef("acct-111111111111-letters", "v2026.12.1"));
        rig.Pins.ResolvePinAsync(OnGeneral, Arg.Any<CancellationToken>()).Returns(new WorkflowPackageRef("general", "latest"));
        rig.Pins.ResolvePinAsync(Unread, Arg.Any<CancellationToken>()).Returns<WorkflowPackageRef>(_ => throw new InvalidOperationException());
        var (manifestJson, files) = ByteEquivalenceFixtures.ReadPackage("example-two-documents");
        rig.Sources.ReadSourceAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>()).Returns(call => ((WorkflowPackageRef)call[0]).Name switch
        {
            Name => new WorkflowPackageSource($"{Name}@v2026.11.3", 30, manifestJson, files, TestFormats.Catalog),
            "general" => new WorkflowPackageSource("general@v2026.11.1", 30, manifestJson, files, TestFormats.Catalog),
            _ => new WorkflowPackageSource("acct-111111111111-letters@v2026.12.1", 31, "{\"specVersion\": 31}", new Dictionary<string, string>(), null),
        });

        var (packages, notRead) = await rig.Operation().ListAsync(CancellationToken.None);

        Assert.Equal([new AccountPackageBelow31(OwnerId, Name, "v2026.11.3", 30)], packages);
        Assert.Equal(1, notRead);
    }

    // ---- the route ----------------------------------------------------------------------------------------------------

    private sealed class Route(Rig rig, string? preview = "31")
    {
        public AppAccount? SignedIn { get; set; } = AdminDrainTests.Admin.Account(OperatorId);

        private AdminMigratePackage Functions()
        {
            var authorizer = Substitute.For<IAccountAuthorizer>();
            authorizer.AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>()).Returns(_ => SignedIn);
            return new AdminMigratePackage(
                authorizer, SpecVersionAdmissionTests.With(preview), rig.Operation(), NullLogger<AdminMigratePackage>.Instance,
                new HashSet<string>(StringComparer.Ordinal) { OperatorId });
        }

        public Task<HttpResponseData> PostAsync(string body) =>
            Functions().MigrateAsync(FakeHttp.Request("POST", body, new Uri("https://api.example.test/api/Admin/MigratePackage")));

        public Task<HttpResponseData> GetAsync() =>
            Functions().ListAsync(FakeHttp.Request("GET", null, new Uri("https://api.example.test/api/Admin/MigratePackage")));
    }

    private static string Body(bool publish = false) =>
        new JsonObject { ["owner"] = OwnerId, ["name"] = Name, ["options"] = null, ["handEdits"] = new JsonObject(), ["publish"] = publish }.ToJsonString();

    [Fact]
    public async Task TheRoute_AnswersTheOperatorWithTheOutcome()
    {
        var rig = new Rig();
        rig.Accounts.ListAsync(Arg.Any<CancellationToken>()).Returns([new AccountSummary(OwnerId, "Active")]);
        var route = new Route(rig);

        var posted = await route.PostAsync(Body());
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal(MigratePackage.WouldPublish, (string?)JsonNode.Parse(FakeHttp.TextOf(posted))!["outcome"]);
        Assert.Contains("no-store", posted.Headers.GetValues("Cache-Control"));

        var listed = await route.GetAsync();
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(
            $$"""{"packages":[{"owner":"{{OwnerId}}","name":"{{Name}}","version":"v2026.11.3","specVersion":30}],"notRead":0}""",
            FakeHttp.TextOf(listed));
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task ANonOperator_GetsTodaysForbiddenAnswer_AndNothingIsRead()
    {
        var rig = new Rig();
        var route = new Route(rig);

        foreach (var (account, expected) in new[] { (AdminDrainTests.Admin.Account(OwnerId), HttpStatusCode.Forbidden), (null, HttpStatusCode.Unauthorized) })
        {
            route.SignedIn = account;
            Assert.Equal(expected, (await route.PostAsync(Body(publish: true))).StatusCode);
            Assert.Equal(expected, (await route.GetAsync()).StatusCode);
        }

        Assert.Empty(rig.Pins.ReceivedCalls());
        Assert.Empty(rig.Accounts.ReceivedCalls());
        rig.AssertNothingWritten();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Where31IsNotAdmitted_TheRouteAnswersS09001As400_AndNothingIsRead(string? preview)
    {
        var rig = new Rig();
        var route = new Route(rig, preview);

        foreach (var answer in new[] { await route.PostAsync(Body(publish: true)), await route.GetAsync() })
        {
            Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
            Assert.Equal(SpecVersionAdmissionTests.With(preview).NotSupported(31), (string?)JsonNode.Parse(FakeHttp.TextOf(answer))!["error"]);
        }

        Assert.Empty(rig.Pins.ReceivedCalls());
        Assert.Empty(rig.Accounts.ReceivedCalls());
        rig.AssertNothingWritten();
    }

    [Theory]
    [InlineData("[]", "The body must be a JSON object, no member written twice.")]
    [InlineData("""{"owner":"a","owner":"b","name":"n"}""", "The body must be a JSON object, no member written twice.")]
    [InlineData("""{"name":"n"}""", "owner must be the account's app user id, as text.")]
    [InlineData("""{"owner":"a","name":"n","options":[]}""", "options must be the package's options.json, an object, or null.")]
    [InlineData("""{"owner":"a","name":"n","handEdits":{"manifest.json":null}}""", "handEdits cannot delete manifest.json.")]
    [InlineData("""{"owner":"a","name":"n","handEdits":{"nodes/a.md":3}}""", "handEdits must give 'nodes/a.md' as text, or null to delete it.")]
    [InlineData("""{"owner":"a","name":"n","publish":"yes"}""", "publish must be true or false.")]
    [InlineData("""{"owner":"a","name":"n","force":true}""", "The body has the member 'force'; its members are owner, name, options, handEdits and publish.")]
    public async Task ABodyItCannotRead_Is400_AndNothingIsRead(string body, string error)
    {
        var rig = new Rig();

        var answer = await new Route(rig).PostAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Equal(error, (string?)JsonNode.Parse(FakeHttp.TextOf(answer))!["error"]);
        Assert.Empty(rig.Pins.ReceivedCalls());
    }

    [Fact]
    public void TheRequest_ReadsItsMembers()
    {
        var (request, error) = MigratePackageRequest.Read(
            """{"owner":"a","name":"n","options":{"renames":{},"whenEmpty":{},"folderFormats":{}},"handEdits":{"nodes/a.md":"x","nodes/b.md":null},"publish":true}""");

        Assert.Null(error);
        Assert.Equal("a", request!.Owner);
        Assert.Equal("n", request.Name);
        Assert.NotNull(request.Options);
        Assert.Equal(new Dictionary<string, string?> { ["nodes/a.md"] = "x", ["nodes/b.md"] = null }, request.HandEdits);
        Assert.True(request.Publish);

        var (bare, _) = MigratePackageRequest.Read("""{"owner":"a","name":"n"}""");
        Assert.Null(bare!.Options);
        Assert.Empty(bare.HandEdits);
        Assert.False(bare.Publish);
    }
}
