using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1283 (package-format-v28-design.md § 8): the 27 → 28 rewrite on in-repo
/// fixtures — the 27 minimal (whose section_notes prompt is prompts/section.md),
/// a renamed-to-another-word prompt, a shared prompt, hyphenated folder items,
/// a collision with different text and one with the same text.
/// </summary>
public class WorkflowPackageMigration27To28Tests
{
    private static (string Json, Dictionary<string, string> Files) Package27(Action<JsonObject>? reshape = null, Action<Dictionary<string, string>>? files = null)
    {
        var (manifest, bundleFiles) = V27Package.Minimal();
        var root = JsonNode.Parse(JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions))!.AsObject();
        reshape?.Invoke(root);
        var copy = new Dictionary<string, string>(bundleFiles, StringComparer.Ordinal);
        files?.Invoke(copy);
        return (root.ToJsonString(), copy);
    }

    private static JsonObject NodeOf(JsonObject root, string id) =>
        root["nodes"]!.AsArray().Select(n => n!.AsObject()).Single(n => (string?)n["id"] == id);

    private static Migration26Result Migrated(Action<JsonObject>? reshape = null, Action<Dictionary<string, string>>? files = null)
    {
        var (json, bundleFiles) = Package27(reshape, files);
        return WorkflowPackageMigration27To28.Migrate(json, bundleFiles);
    }

    private static WorkflowPackageValidator.ValidationResult ValidateAt28(Migration26Result result) =>
        V28Package.Validate((V28Package.Read(result.ManifestJson), result.Files));

    [Fact]
    public void TheMinimal_Migrates_ToThe28Minimal_AndIsValid()
    {
        var result = Migrated();
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));

        var validated = ValidateAt28(result);
        Assert.Empty(validated.Errors);
        Assert.Empty(validated.Warnings);

        var (expectedJson, expectedFiles) = V28Package.MinimalJson();
        Assert.Equal(JsonNode.Parse(expectedJson)!.ToJsonString(), JsonNode.Parse(result.ManifestJson)!.ToJsonString());
        Assert.Equal(expectedFiles.Keys.Order(), result.Files.Keys.Order());
        foreach (var (path, text) in expectedFiles)
        {
            if (path.EndsWith("index.json", StringComparison.Ordinal))
            {
                Assert.Equal(JsonNode.Parse(text)!.ToJsonString(), JsonNode.Parse(result.Files[path])!.ToJsonString());
            }
            else
            {
                Assert.Equal(text, result.Files[path]);
            }
        }
    }

    [Fact]
    public void TheNotices_NameEachMove_TheIndex_AndTheTotal()
    {
        var result = Migrated();
        Assert.Equal(new[]
        {
            "prompts/section.md → prompts/section_notes.md",
            "snippets/sections/index.json: { \"items\": [\"hpi\", \"plan\"] } (no label; add one in the Editor).",
            "1 file renamed to its id; 4 nodes name no prompt now; macros and snippets are lists of ids.",
        }, result.Notices);
    }

    [Fact]
    public void APromptInOtherWords_AndHyphenatedItems_MoveToTheirIds()
    {
        var result = Migrated(
            root =>
            {
                NodeOf(root, "scope")["prompt"] = "prompts/classify-scope.md";
                root["snippets"]!["sections"] = "snippets/section-list/";
            },
            files =>
            {
                files["prompts/classify-scope.md"] = files["prompts/scope.md"];
                files.Remove("prompts/scope.md");
                foreach (var key in files.Keys.Where(k => k.StartsWith("snippets/sections/", StringComparison.Ordinal)).ToList())
                {
                    files[key.Replace("snippets/sections/", "snippets/section-list/")] = files[key];
                    files.Remove(key);
                }

                files["snippets/section-list/history-of-present-illness.md"] = files["snippets/section-list/hpi.md"];
                files.Remove("snippets/section-list/hpi.md");
                files["snippets/section-list/index.json"] = """[ { "id": "hpi", "file": "history-of-present-illness.md" }, { "id": "plan", "file": "plan.md" } ]""";
            });

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.Contains("prompts/classify-scope.md → prompts/scope.md", result.Notices);
        Assert.Contains("snippets/section-list/history-of-present-illness.md → snippets/sections/hpi.md", result.Notices);
        Assert.Contains("snippets/section-list/plan.md → snippets/sections/plan.md", result.Notices);
        Assert.Contains("snippets/section-list/index.json → snippets/sections/index.json: { \"items\": [\"hpi\", \"plan\"] } (no label; add one in the Editor).", result.Notices);
        Assert.DoesNotContain(result.Files.Keys, k => k.Contains("section-list", StringComparison.Ordinal) || k.Contains("classify", StringComparison.Ordinal));

        var validated = ValidateAt28(result);
        Assert.Empty(validated.Errors);
        Assert.Empty(validated.Warnings);
    }

    [Fact]
    public void ASharedPrompt_IsCopiedToEachNode()
    {
        var result = Migrated(root =>
        {
            NodeOf(root, "extract")["prompt"] = "prompts/shared.md";
            NodeOf(root, "scope")["prompt"] = "prompts/shared.md";
        }, files =>
        {
            files["prompts/shared.md"] = "Shared {{ input.notes }}";
            files.Remove("prompts/extract.md");
            files.Remove("prompts/scope.md");
        });

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.Equal("Shared {{ input.notes }}", result.Files["prompts/extract.md"]);
        Assert.Equal("Shared {{ input.notes }}", result.Files["prompts/scope.md"]);
        Assert.False(result.Files.ContainsKey("prompts/shared.md"));
        Assert.Contains("prompts/shared.md was shared by extract, scope; each node now has its own copy (prompts/extract.md, prompts/scope.md).", result.Notices);
    }

    [Fact]
    public void ASwap_DoesNotCollideWithItself()
    {
        // extract names scope's file and scope names extract's: each lands at its own id.
        var result = Migrated(root =>
        {
            NodeOf(root, "extract")["prompt"] = "prompts/scope.md";
            NodeOf(root, "scope")["prompt"] = "prompts/extract.md";
        });

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        var (_, before) = Package27();
        Assert.Equal(before["prompts/scope.md"], result.Files["prompts/extract.md"]);
        Assert.Equal(before["prompts/extract.md"], result.Files["prompts/scope.md"]);
    }

    [Fact]
    public void ATargetHeldWithDifferentText_Refuses_AndNothingApplies()
    {
        var (json, files) = Package27(files: f => f["prompts/section_notes.md"] = "An unrelated draft.");
        var result = WorkflowPackageMigration27To28.Migrate(json, files);

        Assert.False(result.Succeeded);
        Assert.Equal(new[] { "prompts/section.md would become prompts/section_notes.md, which the package already holds with different text; rename or remove prompts/section_notes.md, then upgrade." }, result.Refusals);
        Assert.Equal(json, result.ManifestJson);
        Assert.Equal(files, result.Files);
    }

    [Fact]
    public void ATargetHeldWithTheSameText_IsTheMovedFile()
    {
        var (json, files) = Package27(files: f => f["prompts/section_notes.md"] = f["prompts/section.md"]);
        var result = WorkflowPackageMigration27To28.Migrate(json, files);

        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        Assert.False(result.Files.ContainsKey("prompts/section.md"));
        Assert.Empty(ValidateAt28(result).Errors);
    }

    [Theory]
    [InlineData(28, "The package is already at specVersion 28.")]
    [InlineData(26, "Migrate to specVersion 27 first (the package is at 26; scripts/migrate-workflow-package.cs --to 27).")]
    public void TheWrongVersion_IsRefused(int version, string refusal)
    {
        var result = Migrated(root => root["specVersion"] = version);
        Assert.Equal(new[] { refusal }, result.Refusals);
    }

    [Fact]
    public void AMissingPrompt_Refuses_NamingIt()
    {
        var result = Migrated(files: f => f.Remove("prompts/scope.md"));
        Assert.Equal(new[] { "Node 'scope' prompt 'prompts/scope.md' is missing from the package; there is nothing to move." }, result.Refusals);
    }
}
