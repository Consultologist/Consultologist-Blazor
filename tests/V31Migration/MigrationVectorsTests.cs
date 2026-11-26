using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 5): the pinned <c>migration-vectors</c> family of the content repository's
/// conformance suite (the <c>external/consultologist-package-format</c> submodule, never a sibling checkout) is what the
/// migration gives. Each pinned case's <c>source/</c>, migrated with its <c>options.json</c>, gives its <c>expected/</c>
/// and <c>notices.json</c> byte for byte; and the family is <see cref="MigrationVectorsExport"/>'s, case for case and
/// file for file, as the index lists it.
/// </summary>
public class MigrationVectorsTests
{
    private static Dictionary<string, byte[]> FilesUnder(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'),
            File.ReadAllBytes,
            StringComparer.Ordinal);

    /// <summary>
    /// The options a pinned case's <c>options.json</c> states: its hand choices, read as a package's <c>options.json</c> is
    /// (<see cref="Migration31Options.FromJson"/>, R18), and its toolchain, which must be the goldens'
    /// (<see cref="CompiledGoldenTests.Fixed"/>); the registry and 30's catalogue are
    /// <see cref="MigrationFixture.Options"/>' (the published ones).
    /// </summary>
    private static Migration31Options OptionsOf(string optionsJson, string where)
    {
        var options = JsonNode.Parse(optionsJson)!.AsObject();
        Assert.Equal(["renames", "whenEmpty", "folderFormats", "toolchain"], options.Select(p => p.Key));
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(CompiledDocumentsExport.ToolchainJson(CompiledGoldenTests.Fixed)), options["toolchain"]),
            $"{where}/options.json: the toolchain is not the goldens'.");
        var choices = (JsonObject)options.DeepClone();
        choices.Remove("toolchain");
        var (read, refusal) = Migration31Options.FromJson(choices, V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, TestFormats.Catalog);
        Assert.True(refusal is null, $"{where}/options.json: {refusal}");
        return read!;
    }

    [Fact]
    public void ThePinnedVectors_AreWhatMigrateGives()
    {
        var family = V31Contract.ConformanceFolder(MigrationVectorsExport.Family);
        var names = MigrationVectorsExport.Cases().Select(c => c.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(names, Directory.GetDirectories(family).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal));

        foreach (var name in names)
        {
            var folder = Path.Combine(family, name);
            var where = $"{MigrationVectorsExport.Family}/{name}";

            // Migrate on the pinned source/ with the pinned options.json gives the pinned expected/ and notices.json.
            var source = Path.Combine(folder, "source");
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(source, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(p => p != "manifest.json")
                .ToDictionary(p => p, p => ByteEquivalenceFixtures.ReadText(Path.Combine([source, .. p.Split('/')])), StringComparer.Ordinal);
            var manifest = ByteEquivalenceFixtures.ReadText(Path.Combine(source, "manifest.json"));
            var options = OptionsOf(ByteEquivalenceFixtures.ReadText(Path.Combine(folder, "options.json")), where);
            var result = WorkflowPackageMigration30To31.Migrate(manifest, files, options);
            Assert.True(result.Succeeded, $"{where}: " + string.Join("\n", result.Refusals));

            var expected = new SortedDictionary<string, string>(result.Files, StringComparer.Ordinal) { ["manifest.json"] = result.ManifestJson };
            var pinnedExpected = FilesUnder(Path.Combine(folder, "expected"));
            Assert.Equal(expected.Keys, pinnedExpected.Keys.Order(StringComparer.Ordinal));
            foreach (var (path, text) in expected)
                Assert.True(text == ByteEquivalenceFixtures.ReadText(Path.Combine([folder, "expected", .. path.Split('/')])), $"{where}/expected/{path} is not what Migrate gives.");
            Assert.Equal(MigratedPackagesTests.NoticesJson(result), ByteEquivalenceFixtures.ReadText(Path.Combine(folder, "notices.json")));

            // And the case is the export's, file for file.
            var exported = MigrationVectorsExport.Files(MigrationVectorsExport.Cases().Single(c => c.Name == name));
            var pinned = FilesUnder(folder);
            Assert.Equal(exported.Keys.Order(StringComparer.Ordinal), pinned.Keys.Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in exported)
                Assert.True(pinned[path].AsSpan().SequenceEqual(bytes), $"{where}/{path}: the pinned file is not what the export writes.");
        }

        var index = V31Contract.Json("conformance", "index.json")["families"]!.AsArray()
            .Single(f => (string?)f!["id"] == MigrationVectorsExport.Family)!;
        Assert.Equal(names, index["cases"]!.AsArray().Select(c => (string)c!));
    }

    [Fact]
    public void TheHandBuiltPackage_ShowsItsThreeConstructs()
    {
        // The case written for the family: the _macro suffix (part 10 § 1.3), an input of two types and a refused
        // condition (§ 1.4), each said once.
        var vector = MigrationVectorsExport.Cases().Single(c => c.Name == MigrationVectorsExport.HandBuiltName);
        var (manifest, files) = MigrationVectorsExport.Source(vector);
        var result = WorkflowPackageMigration30To31.Migrate(manifest, files, MigrationVectorsExport.Options(vector));

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.Equal(["Macro 'leaflet' is now offered by the input 'leaflet_macro'."], result.Report.MacroInputs);
        Assert.Contains(result.HandEdits, e => e.StartsWith("Hand edit needed in manifest.json, inputs.seen_on.format: ", StringComparison.Ordinal));
        Assert.Contains(result.HandEdits, e => e.StartsWith("Hand edit needed in deliverables/urgent_copy.md, if: ", StringComparison.Ordinal));
        Assert.All(result.HandEdits, e => V31Contract.AssertSentence(new Consultologist.PackageFormat.V31.SourceRefusal("S10-002", e, "")));
    }
}
