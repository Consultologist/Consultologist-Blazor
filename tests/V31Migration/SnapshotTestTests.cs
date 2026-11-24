using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;
using Corvus.Text.Json.Validator;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 4; part 07 §§ 8.1 to 8.4; R3; D13; A10-011): the migration's snapshot test,
/// <c>tests/migration_snapshot.json</c>. A migrated package with no hand edit carries one, written by
/// <see cref="WorkflowPackageMigration30To31.Migrate"/> from its offline run (<see cref="SnapshotTest"/>): it follows
/// the test grammar, pins every prompt and deliverable by hash and passes on its own package; a package with hand edits
/// carries none and says A10-011, and <see cref="WorkflowPackageMigration30To31.Snapshot"/> writes it once the edits are
/// made. Content is fictional (generated stubs, Dr A. Sample).
/// </summary>
public class SnapshotTestTests
{
    private const string IndexFile = "tests/index.json";

    /// <summary>The snapshot <paramref name="package"/>'s migration wrote.</summary>
    private static JsonObject SnapshotOf(string package)
    {
        var result = MigratedPackagesTests.Migrated(package);
        Assert.True(result.Files.ContainsKey(SnapshotTest.File), $"{package} has no {SnapshotTest.File}.");
        return JsonNode.Parse(result.Files[SnapshotTest.File])!.AsObject();
    }

    /// <summary><paramref name="result"/> published as the engine publishes it, and its plan.</summary>
    internal static RunPlan PlanOf(Migration31Result result)
    {
        var (refusals, compiled) = CompiledPackage.Publish(MigratedPackagesTests.Upload(result), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed);
        Assert.True(refusals.Count == 0, string.Join("\n", refusals.Select(r => $"{r.Id} {r.Path}: {r.Sentence}")));
        var (plan, refusal) = RunPlan.Build(compiled!.Resolve, compiled.ResolveHash, compiled.LockHash, null);
        Assert.True(plan != null, refusal?.Sentence);
        return plan!;
    }

    /// <summary>The pinned <c>test.schema.json</c>, with the common schema it refers to.</summary>
    private static JsonSchema TestSchema()
    {
        List<AdditionalSchemaFile> files =
        [
            new("urn:consultologist:spec:31:schema:common", V31Contract.Path("schemas", "common.schema.json")),
            new("urn:consultologist:spec:31:schema:test", V31Contract.Path("schemas", "test.schema.json")),
        ];
        var options = new JsonSchema.Options(
            additionalSchemaFiles: files,
            allowFileSystemAndHttpResolution: false,
            defaultDialect: Corvus.Text.Json.RuntimeEvaluator.JsonSchemaDialect.Draft202012,
            alwaysAssertFormat: false,
            additionalDocumentResolver: null);
        return JsonSchema.FromText(
            "{ \"$ref\": \"urn:consultologist:spec:31:schema:test\" }", "urn:consultologist:spec:31:schema:test-file", options, false);
    }

    private static List<string> Keys(JsonNode? node) => node!.AsObject().Select(p => p.Key).ToList();

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.PackagesWithoutHandEdits), MemberType = typeof(MigratedPackagesTests))]
    public async Task TheSnapshot_FollowsTheGrammar(string package)
    {
        var result = MigratedPackagesTests.Migrated(package);
        var text = result.Files[SnapshotTest.File];
        var snapshot = JsonNode.Parse(text)!.AsObject();
        var plan = PlanOf(result);
        var run = await OfflineRun.RunAsync(plan, SnapshotTest.Request(snapshot));

        Assert.True(TestSchema().Validate(text, null), $"{package}'s snapshot does not fit test.schema.json.");
        Assert.Equal(["description", "inputs", "profile", "run", "outputs", "expect"], Keys(snapshot));
        Assert.Equal(plan.InputOrder, Keys(snapshot["inputs"]));
        Assert.Equal(plan.NeedsProfile.Distinct(StringComparer.Ordinal), Keys(snapshot["profile"]));
        foreach (var (field, value) in snapshot["profile"]!.AsObject())
            Assert.Equal(SnapshotTest.Profile[field], (string?)value);
        Assert.Equal(plan.RunFacts.Select(f => f["run.".Length..]), Keys(snapshot["run"]));
        if (snapshot["run"]!["date"] is { } date)
            Assert.Equal("""{"value":"2026-11-16","timeZone":"America/Toronto"}""", date.ToJsonString());

        // One stub per agent row that ran, by its row key.
        var ran = run.Rows.Where(r => r.Kind == PlanKind.Agent && r.State == RowState.Completed).Select(r => r.Key).Order(StringComparer.Ordinal);
        Assert.Equal(ran, Keys(snapshot["outputs"]));
        Assert.NotEmpty(Keys(snapshot["outputs"]));

        // Every agent row's prompt by { hash }; every deliverable by { state, hash }, or { state: "skipped" } alone (R3).
        var expect = snapshot["expect"]!.AsObject();
        Assert.Equal(["prompts", "deliverables"], Keys(expect));
        Assert.Equal(run.Prompts.Select(p => p.Key), Keys(expect["prompts"]));
        Assert.All(expect["prompts"]!.AsObject(), p => Assert.Equal(["hash"], Keys(p.Value)));
        Assert.Equal(plan.Document["deliverables"]!.AsObject().Select(d => d.Key).Order(StringComparer.Ordinal), Keys(expect["deliverables"]).Order(StringComparer.Ordinal));
        foreach (var (key, value) in expect["deliverables"]!.AsObject())
        {
            var state = (string?)value!["state"];
            Assert.Equal(state == "skipped" ? ["state"] : ["state", "hash"], Keys(value));
        }
    }

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.PackagesWithoutHandEdits), MemberType = typeof(MigratedPackagesTests))]
    public async Task TheSnapshot_PassesOnItsOwnPackage(string package)
    {
        // Part 07 § 8.3: the package run again on the file's inputs, profile, run and outputs gives every hash in expect.
        var snapshot = SnapshotOf(package);
        var plan = PlanOf(MigratedPackagesTests.Migrated(package));

        var run = await OfflineRun.RunAsync(plan, SnapshotTest.Request(snapshot));

        Assert.Equal(RunState.Completed, run.State);
        Assert.Empty(run.UnusedOutputs);
        var expect = snapshot["expect"]!;
        Assert.Equal(
            expect["prompts"]!.AsObject().Select(p => (p.Key, (string?)p.Value!["hash"])),
            run.Prompts.Select(p => (p.Key, (string?)p.Hash)));
        foreach (var deliverable in run.Deliverables)
        {
            var expected = expect["deliverables"]![deliverable.Key]!;
            Assert.Equal((string?)expected["state"], deliverable.State.ToString().ToLowerInvariant());
            Assert.Equal((string?)expected["hash"], deliverable.State == DeliverableState.Skipped ? null : deliverable.ContentHash);
        }

        // The replies served are the file's, markers included (D12): none is appended a second time.
        foreach (var (key, value) in snapshot["outputs"]!.AsObject())
            Assert.True(JsonNode.DeepEquals(value, run.Outputs[key]), key);
    }

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.PackagesWithoutHandEdits), MemberType = typeof(MigratedPackagesTests))]
    public void ItIsWrittenByMigrate(string package)
    {
        var result = MigratedPackagesTests.Migrated(package);

        Assert.Contains(SnapshotTest.File, result.Files.Keys);
        Assert.Equal(["migration_snapshot"], JsonNode.Parse(result.Files[IndexFile])!["items"]!.AsArray().Select(i => (string?)i));
        Assert.DoesNotContain(result.Notices, n => n.StartsWith("No snapshot test was written", StringComparison.Ordinal));

        // Snapshot on the migrated source writes the same bytes, whether it carries its snapshot (written anew) or not.
        var carried = WorkflowPackageMigration30To31.Snapshot(result.ManifestJson, result.Files, MigrationFixture.Options(package));
        Assert.True(carried.Succeeded, string.Join("\n", carried.Refusals));
        Assert.Equal(result.Files.Keys.Order(StringComparer.Ordinal), carried.Files.Keys.Order(StringComparer.Ordinal));
        Assert.All(result.Files, f => Assert.True(f.Value == carried.Files[f.Key], f.Key));

        var files = new Dictionary<string, string>(result.Files, StringComparer.Ordinal);
        files.Remove(SnapshotTest.File);
        files[IndexFile] = "{\n  \"description\": null,\n  \"items\": []\n}\n";
        var again = WorkflowPackageMigration30To31.Snapshot(result.ManifestJson, files, MigrationFixture.Options(package));
        Assert.True(again.Succeeded, string.Join("\n", again.Refusals));
        Assert.Equal(result.Files[SnapshotTest.File], again.Files[SnapshotTest.File]);
        Assert.Equal(result.Files[IndexFile], again.Files[IndexFile]);
        Assert.Equal(result.Files.Keys.Order(StringComparer.Ordinal), again.Files.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Snapshot_WritesItsTestAnew_AndLeavesAnAuthorsOwnTestsAlone()
    {
        // An operator's new snapshot of an edited package: the old snapshot and its listing are replaced, an author's own
        // test (listed before it, with its index's description) is kept, bytes and order.
        const string package = "example-nested-note";
        var result = MigratedPackagesTests.Migrated(package);
        var files = new Dictionary<string, string>(result.Files, StringComparer.Ordinal)
        {
            ["tests/own_case.json"] = "{\n  \"description\": null,\n  \"inputs\": {},\n  \"profile\": {},\n  \"run\": {},\n  \"outputs\": {},\n  \"expect\": {}\n}\n",
            [IndexFile] = "{\n  \"description\": [\n    \"An author's tests.\"\n  ],\n  \"items\": [\n    \"own_case\",\n    \"migration_snapshot\"\n  ]\n}\n",
            [SnapshotTest.File] = "{ \"description\": null, \"inputs\": {}, \"profile\": {}, \"run\": {}, \"outputs\": {}, \"expect\": {} }\n",
        };

        var snapshot = WorkflowPackageMigration30To31.Snapshot(result.ManifestJson, files, MigrationFixture.Options(package));

        Assert.True(snapshot.Succeeded, string.Join("\n", snapshot.Refusals));
        Assert.Equal(result.Files[SnapshotTest.File], snapshot.Files[SnapshotTest.File]);
        Assert.Equal(files["tests/own_case.json"], snapshot.Files["tests/own_case.json"]);
        Assert.Equal(files[IndexFile], snapshot.Files[IndexFile]);
    }

    [Fact]
    public void WithTheSnapshotOff_NothingIsPublishedOrSaid()
    {
        // Migration31Options.WriteSnapshot false (the weekly summary): no snapshot, no A10-011/060/061, items [].
        foreach (var package in new[] { "example-nested-note", "example-macros" })
        {
            var (manifestJson, source) = Consultologist.Api.Tests.ByteEquivalence.ByteEquivalenceFixtures.ReadPackage(package);
            var off = WorkflowPackageMigration30To31.Migrate(manifestJson, source, MigrationFixture.Options(package) with { WriteSnapshot = false });
            var on = MigratedPackagesTests.Migrated(package);

            Assert.DoesNotContain(SnapshotTest.File, off.Files.Keys);
            Assert.Empty(JsonNode.Parse(off.Files[IndexFile])!["items"]!.AsArray());
            Assert.DoesNotContain(off.Notices, n => n.StartsWith("No snapshot test was written", StringComparison.Ordinal));
            Assert.Equal(on.Notices.Where(n => !n.StartsWith("No snapshot test was written", StringComparison.Ordinal)), off.Notices);
            Assert.Equal(on.HandEdits, off.HandEdits);
            Assert.All(off.Files, f => Assert.True(f.Key == IndexFile || f.Value == on.Files[f.Key], f.Key));
        }
    }

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.PackagesWithHandEdits), MemberType = typeof(MigratedPackagesTests))]
    public void WithHandEdits_NoSnapshot_AndANotice(string package)
    {
        var result = MigratedPackagesTests.Migrated(package);

        Assert.DoesNotContain(SnapshotTest.File, result.Files.Keys);
        Assert.Empty(JsonNode.Parse(result.Files[IndexFile])!["items"]!.AsArray());
        var notice = Assert.Single(result.Notices, n => n.StartsWith("No snapshot test was written", StringComparison.Ordinal));
        Assert.Equal("No snapshot test was written: hand edits remain.", notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-011", notice, ""));
        Assert.Equal(result.Notices[^1], notice);
        Assert.DoesNotContain(notice, result.HandEdits);

        // Snapshot refuses a source that does not publish, with the checker's sentences.
        var snapshot = WorkflowPackageMigration30To31.Snapshot(result.ManifestJson, result.Files, MigrationFixture.Options(package));
        Assert.False(snapshot.Succeeded);
        var refusals = MigratedPackagesTests.Publish(result);
        Assert.Equal(refusals.Select(r => r.Sentence), snapshot.Refusals);
        Assert.Equal(result.Files, snapshot.Files);
    }

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.PackagesWithoutHandEdits), MemberType = typeof(MigratedPackagesTests))]
    public void TheSnapshot_HoldsNoLongText(string package)
    {
        // Part 10 § 4: expect holds hashes, never a whole note or prompt.
        var expect = SnapshotOf(package)["expect"]!;

        Assert.DoesNotContain("\"text\"", expect.ToJsonString(), StringComparison.Ordinal);
        foreach (var (_, group) in expect.AsObject())
            Assert.All(group!.AsObject(), entry => Assert.False(entry.Value!.AsObject().ContainsKey("text"), entry.Key));
    }

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.PackagesWithoutHandEdits), MemberType = typeof(MigratedPackagesTests))]
    public void ItCountsAgainstSourceBounds(string package)
    {
        // Part 07 § 8.4: a test file counts against SourceBounds; the package with its snapshot still passes intake.
        var upload = MigratedPackagesTests.Upload(MigratedPackagesTests.Migrated(package));

        Assert.Empty(SourceIntake.Read(upload).Refusals);
        Assert.True(upload[SnapshotTest.File].Length <= SourceBounds.FileBytes);
    }

    [Fact]
    public void ASnapshotOverFileBytes_IsNotWritten_AndSaysSo()
    {
        // A snapshot that would pass the bound is a notice (A10-060), never a hand edit: Task 12's invariant holds.
        var (manifestJson, files) = Consultologist.Api.Tests.ByteEquivalence.ByteEquivalenceFixtures.ReadPackage("example-two-documents");
        var result = WorkflowPackageMigration30To31.Migrate(manifestJson, files, MigrationFixture.Options("example-two-documents"), snapshotFileBytes: 64);

        Assert.True(result.Succeeded);
        Assert.Empty(result.HandEdits);
        Assert.DoesNotContain(SnapshotTest.File, result.Files.Keys);
        Assert.Empty(JsonNode.Parse(result.Files[IndexFile])!["items"]!.AsArray());
        var notice = Assert.Single(result.Notices, n => n.StartsWith("No snapshot test was written", StringComparison.Ordinal));
        Assert.Equal("No snapshot test was written: tests/migration_snapshot.json would take the package past SourceBounds.fileBytes.", notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-060", notice, ""));
        Assert.Empty(MigratedPackagesTests.Publish(result));

        // Snapshot, asked for it, refuses with the same sentence.
        var snapshot = WorkflowPackageMigration30To31.Snapshot(result.ManifestJson, result.Files, MigrationFixture.Options("example-two-documents"), snapshotFileBytes: 64);
        Assert.Equal([notice], snapshot.Refusals);
    }

    [Fact]
    public void ASourceThatDoesNotPublish_SaysA10061_WithTheCheckersSentences()
    {
        // Task 12's invariant broken (no hand edit, yet Publish refuses): Migrate's notice carries the checker's sentences.
        var result = MigratedPackagesTests.Migrated("example-macros");
        var sentences = MigratedPackagesTests.Publish(result).Select(r => r.Sentence).ToList();
        Assert.NotEmpty(sentences);

        var (files, why, unpublished) = WorkflowPackageMigration30To31.WithSnapshot(
            result.ManifestJson, result.Files, MigrationFixture.Options("example-macros"), SourceBounds.FileBytes);

        Assert.Null(files);
        Assert.Equal(sentences, unpublished);
        Assert.Equal("No snapshot test was written: the migrated package does not publish: " + string.Join(" ", sentences) + ".", why);
        V31Contract.AssertSentence(new SourceRefusal("A10-061", why!, ""));
    }

    [Fact]
    public void AFaultOnTheWay_SaysA10061_CannotBeRunOffline_NeverThrows()
    {
        var result = MigratedPackagesTests.Migrated("example-nested-note");
        var broken = MigrationFixture.Options("example-nested-note") with { Toolchain = null! };

        var (files, why, unpublished) = WorkflowPackageMigration30To31.WithSnapshot(result.ManifestJson, result.Files, broken, SourceBounds.FileBytes);

        Assert.Null(files);
        Assert.Null(unpublished);
        Assert.Equal("No snapshot test was written: the migrated package cannot be run offline.", why);
        V31Contract.AssertSentence(new SourceRefusal("A10-061", why!, ""));
    }

    [Fact]
    public async Task ARunThatDoesNotComplete_SaysA10061_ByHowItEnded()
    {
        var none = new Dictionary<string, string>(StringComparer.Ordinal);
        var noUpload = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // A start that refuses the request (a required input not given).
        var refusedPlan = V31Engine.V31StartChecksTests.Plan();
        var refusedRequest = SnapshotTest.Fictional(refusedPlan) with { Inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) };
        var refused = await OfflineRun.RunAsync(refusedPlan, refusedRequest);
        Assert.Null(refused.State);
        var (_, startWhy) = WorkflowPackageMigration30To31.Written(none, noUpload, refusedPlan, refusedRequest, refused, SourceBounds.FileBytes);
        Assert.Equal("No snapshot test was written: the migrated package is refused at its run's start.", startWhy);

        // A run that fails: a markdown_inline reply holding a line ending fails its node (D21).
        var failingPlan = Consultologist.Api.Tests.V31Run.RunFixture.Plan(OfflineRunTests.Formats());
        var failingRequest = SnapshotTest.Fictional(failingPlan) with
        {
            Outputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["node.inline"] = "Two lines\nof stub text." },
        };
        var failed = await OfflineRun.RunAsync(failingPlan, failingRequest);
        Assert.Equal(RunState.Failed, failed.State);
        var (_, failedWhy) = WorkflowPackageMigration30To31.Written(none, noUpload, failingPlan, failingRequest, failed, SourceBounds.FileBytes);
        Assert.Equal("No snapshot test was written: the migrated package fails its offline run.", failedWhy);

        // TimedOut cannot be reached offline (the clock never moves, Task 13 decision 1): the form is held on a result that says it.
        var (_, timedOutWhy) = WorkflowPackageMigration30To31.Written(none, noUpload, failingPlan, failingRequest, failed with { State = RunState.TimedOut }, SourceBounds.FileBytes);
        Assert.Equal("No snapshot test was written: the migrated package times out in its offline run.", timedOutWhy);

        foreach (var why in new[] { startWhy, failedWhy, timedOutWhy })
            V31Contract.AssertSentence(new SourceRefusal("A10-061", why!, ""));
    }

    [Fact]
    public void Fictional_LeavesAMacroChoiceUnset_AndGivesEveryOtherInput()
    {
        // D13: every input given, a migrated macro choice (the one boolean input a macro's if reads) left null.
        var plan = V31Engine.V31StartChecksTests.Migrated();

        var request = SnapshotTest.Fictional(plan);

        Assert.Equal(plan.InputOrder, request.Inputs.Keys);
        Assert.Null(request.Inputs["letrozole_macro"]);
        Assert.All(plan.InputOrder.Where(id => id != "letrozole_macro"), id => Assert.NotNull(request.Inputs[id]));
        Assert.Empty(request.Outputs);
        Assert.Equal(SnapshotTest.StartedAtUtc, request.StartedAtUtc);
    }

    [Fact]
    public async Task AnOfferedMacrosMarker_IsInTheOutputs_AndTheSnapshotPassesAgain()
    {
        // D12: the offline run appends an offered macro's marker to a reply; outputs hold the reply as served, so a run
        // from the file serves it unchanged (the marker is not appended twice) and gives the same hashes.
        var plan = V31Engine.V31StartChecksTests.Migrated(("reviewed", "true"));
        var request = SnapshotTest.Fictional(plan);
        var first = await OfflineRun.RunAsync(plan, request);
        var snapshot = SnapshotTest.Write(plan, request, first);

        var draft = (string?)snapshot["outputs"]!["node.draft"];
        Assert.Contains("{{ macro.reviewed", draft, StringComparison.Ordinal);
        Assert.DoesNotContain("macro.letrozole", draft, StringComparison.Ordinal); // its choice is left unset

        var again = await OfflineRun.RunAsync(plan, SnapshotTest.Request(snapshot));
        Assert.Equal(RunState.Completed, again.State);
        Assert.Equal(snapshot.ToJsonString(), SnapshotTest.Write(plan, SnapshotTest.Request(snapshot), again).ToJsonString());
    }

    [Fact]
    public void Fictional_GivesExactlyTheProfileAndRunFactsThePlanReads()
    {
        var plan = V31Engine.V31StartChecksTests.Plan(needs: ["signature", "name"], facts: ["run.date", "run.package", "run.id", "run.engine"]);

        var request = SnapshotTest.Fictional(plan);

        Assert.Equal(new Dictionary<string, string?> { ["signature"] = "Dr A. Sample\nInternal Medicine", ["name"] = "Dr A. Sample" }, request.Profile);
        Assert.Equal(
            $$"""{"date":"2026-11-16","package":"{{plan.Package}}@{{plan.Version}}","id":"migration_snapshot","engine":"offline"}""",
            request.RunFacts.ToJsonString());
        Assert.Empty(SnapshotTest.Fictional(V31Engine.V31StartChecksTests.Plan()).Profile);
        Assert.Empty(SnapshotTest.Fictional(V31Engine.V31StartChecksTests.Plan()).RunFacts);
    }
}
