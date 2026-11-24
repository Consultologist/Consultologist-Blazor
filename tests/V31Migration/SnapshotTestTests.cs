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

        // Snapshot on the migrated source without its test writes the same bytes.
        var files = new Dictionary<string, string>(result.Files, StringComparer.Ordinal);
        files.Remove(SnapshotTest.File);
        files[IndexFile] = "{\n  \"description\": null,\n  \"items\": []\n}\n";
        var again = WorkflowPackageMigration30To31.Snapshot(result.ManifestJson, files, MigrationFixture.Options(package));
        Assert.True(again.Succeeded, string.Join("\n", again.Refusals));
        Assert.Equal(result.Files[SnapshotTest.File], again.Files[SnapshotTest.File]);
        Assert.Equal(result.Files[IndexFile], again.Files[IndexFile]);
        Assert.Equal(result.Files.Keys.Order(StringComparer.Ordinal), again.Files.Keys.Order(StringComparer.Ordinal));
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
