using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;
using Equivalence = Consultologist.PackageFormat.V31.Harness.ByteEquivalence;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10, Task 16; part 10 §§ 1.5, 3.1, 3.2; D10, D11): the operator's run over a folder of
/// packages. Today's engine runs offline only through this project's <see cref="CaptureRun"/>, so the run is a test that
/// does nothing without <c>BYTE_EQUIVALENCE_PACKAGES=&lt;dir&gt;</c> (as the conformance exports do). With it, for each
/// package folder under <c>&lt;dir&gt;</c> (a folder holding a <c>manifest.json</c>; <c>_migrated</c> and
/// <c>_hand-edits</c> are the run's own), <see cref="OperatorRun.RunAsync"/> migrates the package, lays over it the
/// operator's edits from <c>&lt;dir&gt;/_hand-edits/&lt;package&gt;/</c> when there are any (the form of the fixtures'
/// <c>hand-edits/</c>), and writes the 31 folder and <c>migration-report.json</c> under <c>&lt;dir&gt;/_migrated/&lt;package&gt;/</c>.
/// A package that publishes is compared on D11's account scenario, <c>migration_snapshot</c>: the 31 snapshot request's
/// inputs and Dr A. Sample's profile run at 30 through <see cref="CaptureRun"/>, that capture replayed at 31
/// (<see cref="Equivalence.RequestFor"/>) and compared; one that does not (hand edits remain) gets a report of its hand
/// edits and notices with no compared text.
/// </summary>
public class OperatorRunTests
{
    [Fact]
    public async Task TheOperatorsRun()
    {
        // D10: the operator's entry. Without the variable it does nothing.
        await OperatorRun.FromEnvironmentAsync(Environment.GetEnvironmentVariable(OperatorRun.Variable));
    }

    [Fact]
    public async Task WithoutTheVariable_TheOperatorsRunDoesNothing()
    {
        Assert.Empty(await OperatorRun.FromEnvironmentAsync(null));
        Assert.Empty(await OperatorRun.FromEnvironmentAsync(""));
    }

    [Fact]
    public async Task TheOperatorsRun_WritesEveryPackage()
    {
        // D10: a folder holding two vendored packages, one that migrates cleanly and one with a hand edit left.
        var dir = Directory.CreateTempSubdirectory("operator-run-").FullName;
        try
        {
            string[] packages = ["example-problem-list", "example-two-documents"];
            foreach (var package in packages)
                Copy(Path.Combine(ByteEquivalenceFixtures.Root, "vendored", package), Path.Combine(dir, package));

            var written = await OperatorRun.FromEnvironmentAsync(dir);

            Assert.Equal(packages, written);
            foreach (var package in packages)
            {
                var folder = Path.Combine(dir, OperatorRun.MigratedFolder, package);
                var migrated = MigratedPackagesTests.Migrated(package);
                Assert.Equal(migrated.ManifestJson, ByteEquivalenceFixtures.ReadText(Path.Combine(folder, "manifest.json")));
                foreach (var (path, text) in migrated.Files)
                    Assert.True(text == ByteEquivalenceFixtures.ReadText(Path.Combine([folder, .. path.Split('/')])), $"{package}/{path} differs");

                var report = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(folder, MigrationReport.FileName)))!;
                Assert.True(V31ContractsTests.SchemaOf("migration-report").Validate(report.ToJsonString(), null), report.ToJsonString());
                Assert.Equal(migrated.HandEdits, report["handEdits"]!.AsArray().Select(h => (string)h!));
            }

            // The clean package is compared on the account scenario; the one with a hand edit left is not.
            var clean = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(dir, OperatorRun.MigratedFolder, "example-two-documents", MigrationReport.FileName)))!;
            var texts = clean["texts"]!.AsArray();
            Assert.NotEmpty(texts);
            Assert.Equal("example-two-documents/migration_snapshot#status", (string?)texts[0]!["site"]);
            Assert.True((bool)texts[0]!["equal"]!);
            var edited = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(dir, OperatorRun.MigratedFolder, "example-problem-list", MigrationReport.FileName)))!;
            Assert.Empty(edited["texts"]!.AsArray());

            // With the operator's edits laid over it, the package publishes and is compared too.
            Copy(MigrationFixture.HandEditsOf("example-problem-list"), Path.Combine(dir, OperatorRun.HandEditsFolder, "example-problem-list"));
            Assert.Equal(packages, await OperatorRun.FromEnvironmentAsync(dir));
            var compared = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(dir, OperatorRun.MigratedFolder, "example-problem-list", MigrationReport.FileName)))!;
            Assert.Equal(MigrationFixture.Edited("example-problem-list").Files["snippets/sections/follow_up_plan.md"],
                ByteEquivalenceFixtures.ReadText(Path.Combine(dir, OperatorRun.MigratedFolder, "example-problem-list", "snippets", "sections", "follow_up_plan.md")));
            Assert.NotEmpty(compared["texts"]!.AsArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}

/// <summary>#1507 (Task 16, D10): the operator's run over a folder of 30 packages (see <see cref="OperatorRunTests"/>).</summary>
internal static class OperatorRun
{
    /// <summary>The variable naming the folder of packages.</summary>
    public const string Variable = "BYTE_EQUIVALENCE_PACKAGES";

    /// <summary>Where the run writes each migrated package and its report.</summary>
    public const string MigratedFolder = "_migrated";

    /// <summary>Where the operator puts a package's hand edits, laid over its migration as the fixtures' are.</summary>
    public const string HandEditsFolder = "_hand-edits";

    /// <summary>D11: the scenario of an account package's compared texts.</summary>
    public const string Scenario = SnapshotTest.Id;

    /// <summary>The packages run under <paramref name="dir"/>, in ordinal order; none, and nothing done, when it is unset.</summary>
    public static async Task<IReadOnlyList<string>> FromEnvironmentAsync(string? dir)
    {
        if (string.IsNullOrEmpty(dir))
            return [];

        var packages = Directory.EnumerateDirectories(dir)
            .Select(Path.GetFileName)
            .Where(name => name is not (MigratedFolder or HandEditsFolder) && File.Exists(Path.Combine(dir, name!, "manifest.json")))
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (var package in packages)
            await RunAsync(dir, package);
        return packages;
    }

    /// <summary>One package: migrate, lay the operator's edits over it, compare when it publishes, write the folder and report.</summary>
    public static async Task RunAsync(string dir, string package)
    {
        var (manifestJson, files) = Read(Path.Combine(dir, package));
        var migrated = WorkflowPackageMigration30To31.Migrate(manifestJson, files, MigrationFixture.Options(package));
        Assert.True(migrated.Succeeded, $"{package}: " + string.Join("\n", migrated.Refusals));
        migrated = MigrationFixture.Overlay(migrated, Path.Combine(dir, HandEditsFolder, package));

        var texts = new List<ComparedText>();
        var (refusals, compiled) = CompiledPackage.Publish(MigratedPackagesTests.Upload(migrated), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed);
        if (refusals.Count == 0 && RunPlan.Build(compiled!.Resolve, compiled.ResolveHash, compiled.LockHash, null).Plan is { } plan)
        {
            var scenario = ScenarioOf(manifestJson, SnapshotTest.Fictional(plan));
            var run30 = await CaptureRun.StartAsync(ByteEquivalenceFixtures.Load(package, manifestJson, files), CaptureScenario.Read(scenario.ToJsonString()));
            var capture = JsonNode.Parse(PackageCapture.Write(package, null, Scenario, run30))!.AsObject();
            var request = Equivalence.RequestFor(scenario, capture, migrated, plan);
            var run31 = await OfflineRun.RunAsync(plan, request);
            texts.AddRange(Equivalence.Compare(package, Scenario, capture, run31, migrated, plan, request));
        }

        var oldVersion = (string?)JsonNode.Parse(manifestJson)!["version"] ?? "";
        var report = MigrationReport.Of(oldVersion, migrated, texts, MigrationReport.WorstCaseNote(migrated, V31Contract.PublishedTypes()));

        var folder = Path.Combine(dir, MigratedFolder, package);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        var written = new SortedDictionary<string, string>(migrated.Files, StringComparer.Ordinal) { ["manifest.json"] = migrated.ManifestJson };
        foreach (var (path, text) in written)
            Write(Path.Combine([folder, .. path.Split('/')]), text);
        Write(Path.Combine(folder, MigrationReport.FileName), report.ToJson());
    }

    /// <summary>
    /// The 30 scenario of the account run: each 30 input given the 31 snapshot request's value of the same id (an input
    /// renamed at 31, or a macro's choice, left unset, so 30 takes its default), Dr A. Sample's name and signature, no
    /// choice and no pinned reply.
    /// </summary>
    private static JsonObject ScenarioOf(string manifestJson, OfflineRequest fictional)
    {
        var inputs = new JsonObject();
        foreach (var input in JsonNode.Parse(manifestJson)!["inputs"] as JsonArray ?? [])
        {
            var id = (string)input!["id"]!;
            inputs[id] = fictional.Inputs.GetValueOrDefault(id)?.DeepClone();
        }

        return new JsonObject
        {
            ["description"] = null,
            ["inputs"] = inputs,
            ["profile"] = new JsonObject { ["name"] = SnapshotTest.Profile["name"], ["signature"] = SnapshotTest.Profile["signature"] },
            ["macroChoices"] = new JsonObject(),
            ["outputs"] = new JsonObject(),
        };
    }

    /// <summary>A package folder as <see cref="ByteEquivalenceFixtures.ReadPackage"/> reads one: the manifest, and the files but it and <c>dag.mmd</c>.</summary>
    private static (string ManifestJson, Dictionary<string, string> Files) Read(string directory)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative is not ("manifest.json" or "dag.mmd"))
                files[relative] = ByteEquivalenceFixtures.ReadText(path);
        }

        return (ByteEquivalenceFixtures.ReadText(Path.Combine(directory, "manifest.json")), files);
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
    }
}
