using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.1, § 1.4; D1, D2): the migration's entry point and its gates, and the
/// chain that raises an older package to 30 first. A version below 5, a manifest that does not read and a package
/// already at 31 are refused, writing nothing (the result holds the input's manifest and files). An older package is
/// raised through every rung the script and the Editor run, in order, exactly as running the rungs by hand does; the
/// stage to 24 is the script's, moved into the library unchanged.
/// </summary>
public class MigrationChainTests
{
    private static Migration31Options Options() => MigrationFixture.Options("general");

    private static void AssertRefusedWritingNothing(Migration31Result result, string manifestJson, IReadOnlyDictionary<string, string> files)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(manifestJson, result.ManifestJson);
        Assert.Equal(files.OrderBy(f => f.Key, StringComparer.Ordinal), result.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
        Assert.Empty(result.Notices);
        Assert.Empty(result.HandEdits);
        Assert.Empty(result.Keys);
    }

    [Fact]
    public void APackageBelow5_IsRefused()
    {
        var manifestJson = V24Package.ManifestJson(specVersion: 4);
        var files = V24Package.Files();

        var result = WorkflowPackageMigration30To31.Migrate(manifestJson, files, Options());

        Assert.Equal(["The package is at specVersion 4; migration starts at 5."], result.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("A10-012", result.Refusals[0], ""));
        AssertRefusedWritingNothing(result, manifestJson, files);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "name": "general", "version": "v2026.11.3" }""")]
    [InlineData("""{ "name": "general", "version": "v2026.11.3", "specVersion": "30" }""")]
    [InlineData("""{ "name": "general", "version": "v2026.11.3", "specVersion": 30.5 }""")]
    public void AManifestThatDoesNotRead_IsRefused(string manifestJson)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["prompts/a.md"] = "A prompt." };

        var result = WorkflowPackageMigration30To31.Migrate(manifestJson, files, Options());

        Assert.Equal(["The manifest does not read as a package: it is not a JSON object with a whole-number specVersion."], result.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("A10-016", result.Refusals[0], ""));
        AssertRefusedWritingNothing(result, manifestJson, files);
    }

    [Fact]
    public void A31Package_IsRefused()
    {
        var (thirty, files) = ByteEquivalenceFixture();
        var root = JsonNode.Parse(thirty)!.AsObject();
        root["specVersion"] = 31;
        var manifestJson = root.ToJsonString();

        var result = WorkflowPackageMigration30To31.Migrate(manifestJson, files, Options());

        Assert.Equal(["The package is already at specVersion 31."], result.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("A10-015", result.Refusals[0], ""));
        AssertRefusedWritingNothing(result, manifestJson, files);
    }

    [Fact]
    public void A30Package_Migrates_ToSpecVersion31()
    {
        var result = MigrationFixture.Migrate("general");

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.Equal(31, JsonNode.Parse(result.ManifestJson)!["specVersion"]!.GetValue<int>());
    }

    [Fact]
    public void AnOlderPackage_IsRaisedThroughEveryRung()
    {
        // A 24 package: every rung from 25 on.
        var json24 = V24Package.ManifestJson();
        var files24 = V24Package.Files();
        var chained24 = WorkflowPackageMigrationChain.To30(json24, files24, V24Package.ResolveContract);
        Assert.True(chained24.Succeeded, string.Join("\n", chained24.Refusals));
        AssertSame(ByHandFrom24(json24, files24), chained24);
        Assert.Equal(30, JsonNode.Parse(chained24.ManifestJson)!["specVersion"]!.GetValue<int>());

        // A 27 package: the 28 rewrite and the two raises.
        var (manifest27, files27) = V27Package.Minimal();
        var json27 = JsonSerializer.Serialize(manifest27, WorkflowPackageManifestJson.WriteOptions);
        var chained27 = WorkflowPackageMigrationChain.To30(json27, files27, resolveContract: null);
        Assert.True(chained27.Succeeded, string.Join("\n", chained27.Refusals));
        AssertSame(ByHandFrom27(json27, files27), chained27);
        Assert.Equal(30, JsonNode.Parse(chained27.ManifestJson)!["specVersion"]!.GetValue<int>());

        // And Migrate takes the raised package on: the chain's notices come first.
        var migrated = WorkflowPackageMigration30To31.Migrate(json27, files27, Options());
        Assert.True(migrated.Succeeded, string.Join("\n", migrated.Refusals));
        Assert.Equal(chained27.Notices, migrated.Notices.Take(chained27.Notices.Count));
    }

    [Fact]
    public void A30Package_PassesThroughTheChainUnchanged()
    {
        var (manifestJson, files) = ByteEquivalenceFixture();

        var result = WorkflowPackageMigrationChain.To30(manifestJson, files, resolveContract: null);

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.Equal(manifestJson, result.ManifestJson);
        Assert.Equal(files.OrderBy(f => f.Key, StringComparer.Ordinal), result.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void ARungsRefusal_RefusesTheRun_NamingTheRung()
    {
        var (manifest27, files27) = V27Package.Minimal();
        var json27 = JsonSerializer.Serialize(manifest27, WorkflowPackageManifestJson.WriteOptions);
        files27.Remove("prompts/scope.md");

        var chained = WorkflowPackageMigrationChain.To30(json27, files27, resolveContract: null);
        Assert.Equal(["At specVersion 28: Node 'scope' prompt 'prompts/scope.md' is missing from the package; there is nothing to move."], chained.Refusals);
        V31Contract.AssertSentence(new SourceRefusal("A10-017", chained.Refusals[0], ""));
        Assert.Equal(json27, chained.ManifestJson);

        var migrated = WorkflowPackageMigration30To31.Migrate(json27, files27, Options());
        Assert.Equal(chained.Refusals, migrated.Refusals);
        AssertRefusedWritingNothing(migrated, json27, files27);
    }

    /// <summary>
    /// The Editor's pre-23 package with preludes (<c>EditorFixtures.V7Preludes</c> of the Web tests, copied: the script
    /// has no fixture of its own). The moved stage gives what the script's gave on it (its dry run's notices, and the
    /// staged manifest and files its code writes): <c>guidance</c> a macro before <c>draft-section</c>, <c>unused</c>
    /// dropped, the number 24 with <c>tags</c>.
    /// </summary>
    [Fact]
    public void TheStageTo24_IsTheScripts()
    {
        var root = JsonNode.Parse(V7PreludesManifest)!.AsObject();
        var files = V7PreludesFiles();

        var (notices, refusals) = WorkflowPackageMigrationTo24.StageTo24(root, files, 7);

        Assert.Empty(refusals);
        Assert.Equal(
            [
                "'guidance' → macro 'guidance' before 'draft-section'.",
                "Prelude 'unused' is used by no prompt and was dropped.",
                "Staged specVersion 7 → 24 (the additive bumps: the number, and tags when absent).",
            ],
            notices);
        Assert.Equal(JsonNode.Parse(V7PreludesStaged)!.ToJsonString(), root.ToJsonString());
        Assert.Equal(
            ["data/standards/hpi.md", "data/standards/index.json", "macros/guidance.md", "prompts/draft-section.md"],
            files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Use short SNOMED search terms.", files["macros/guidance.md"]);
    }

    [Fact]
    public void TheStageTo24_RefusesAPreludeThatCannotBeAMacro()
    {
        var root = JsonNode.Parse(V7PreludesManifest.Replace("\"guidance\": \"preludes/guidance.md\"", "\"Guidance\": \"preludes/guidance.md\"", StringComparison.Ordinal))!.AsObject();
        var files = V7PreludesFiles();

        var (_, refusals) = WorkflowPackageMigrationTo24.StageTo24(root, files, 7);

        Assert.Equal(["Prelude 'Guidance' needs a snake_case id to become a macro; rename it, then upgrade."], refusals);
    }

    private static (string ManifestJson, Dictionary<string, string> Files) ByteEquivalenceFixture() =>
        Consultologist.Api.Tests.ByteEquivalence.ByteEquivalenceFixtures.ReadPackage("general");

    private static void AssertSame(Migration26Result expected, Migration26Result actual)
    {
        Assert.Equal(expected.Refusals, actual.Refusals);
        Assert.Equal(expected.ManifestJson, actual.ManifestJson);
        Assert.Equal(expected.Files.OrderBy(f => f.Key, StringComparer.Ordinal), actual.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
        Assert.Equal(expected.Notices, actual.Notices);
    }

    /// <summary>The rungs from 24 run by hand, in the script's and the Editor's order.</summary>
    private static Migration26Result ByHandFrom24(string manifestJson, Dictionary<string, string> files)
    {
        var root = JsonNode.Parse(manifestJson)!.AsObject();
        var staged = new Dictionary<string, string>(files, StringComparer.Ordinal);
        var (stageNotices, stageRefusals) = WorkflowPackageMigrationTo24.StageTo24(root, staged, 24);
        Assert.Empty(stageRefusals);

        var at25 = WorkflowPackageMigration24To25.Migrate(root.ToJsonString(), staged, V24Package.ResolveContract);
        Assert.True(at25.Succeeded, string.Join("\n", at25.Refusals));
        var at26 = WorkflowPackageMigration25To26.Migrate(at25.ManifestJson, at25.Files);
        Assert.True(at26.Succeeded, string.Join("\n", at26.Refusals));
        var at27 = WorkflowPackageVersionRaise.To(at26.ManifestJson, at26.Files, 27);
        var rest = ByHandFrom27(at27.ManifestJson, at27.Files);
        return rest with { Notices = [.. stageNotices, .. at25.Notices, .. at26.Notices, .. at27.Notices, .. rest.Notices] };
    }

    /// <summary>The rungs from 27 run by hand: the 28 rewrite, the 29 and 30 raises.</summary>
    private static Migration26Result ByHandFrom27(string manifestJson, Dictionary<string, string> files)
    {
        var at28 = WorkflowPackageMigration27To28.Migrate(manifestJson, files);
        Assert.True(at28.Succeeded, string.Join("\n", at28.Refusals));
        var at29 = WorkflowPackageVersionRaise.To(at28.ManifestJson, at28.Files, 29);
        var at30 = WorkflowPackageVersionRaise.To(at29.ManifestJson, at29.Files, 30);
        return at30 with { Notices = [.. at28.Notices, .. at29.Notices, .. at30.Notices] };
    }

    private const string V7PreludesManifest = """
        {
          "name": "acct-1234567890ab",
          "version": "v2026.07.1",
          "specVersion": 7,
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "preludes": {
            "guidance": "preludes/guidance.md",
            "unused": "preludes/unused.md"
          },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"], "prelude": "guidance" }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """;

    /// <summary>What the script's stage writes for <see cref="V7PreludesManifest"/>: in member order as it edits the tree.</summary>
    private const string V7PreludesStaged = """
        {
          "name": "acct-1234567890ab",
          "version": "v2026.07.1",
          "specVersion": 24,
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" },
              "macros": ["guidance"] },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ],
          "macros": [ { "id": "guidance", "label": "guidance", "file": "macros/guidance.md" } ],
          "tags": []
        }
        """;

    private static Dictionary<string, string> V7PreludesFiles() => new(StringComparer.Ordinal)
    {
        ["prompts/draft-section.md"] = "Draft {{ section_name }} from {{ consult_draft }}.",
        ["data/standards/index.json"] = """
            { "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "History", "file": "hpi.md" } ] }
            """,
        ["data/standards/hpi.md"] = "Document the presenting illness.",
        ["preludes/guidance.md"] = "Use short SNOMED search terms.",
        ["preludes/unused.md"] = "An unused prelude.",
    };
}
