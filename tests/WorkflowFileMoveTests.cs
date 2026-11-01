using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>#1267: a file renamed to match its id — every kind's site updated, the refusals, and the mismatches listed.</summary>
public class WorkflowFileMoveTests
{
    private static (JsonObject Manifest, Dictionary<string, string> Files) Package() =>
        (JsonNode.Parse("""
            {
              "nodes": [
                { "id": "draft", "prompt": "prompts/draft-note.md", "format": "prose" },
                { "id": "plan", "prompt": "prompts/plan.md", "format": "prose" },
                { "id": "a", "prompt": "prompts/shared.md", "format": "prose" },
                { "id": "b", "prompt": "prompts/shared.md", "format": "prose" }
              ],
              "macros": { "closing": "macros/closing-line.md", "notes": "macros/notes/" },
              "snippets": { "style": "snippets/style.md", "sections": "snippets/sections/" }
            }
            """)!.AsObject(),
         new Dictionary<string, string>(StringComparer.Ordinal)
         {
             ["prompts/draft-note.md"] = "Draft.",
             ["prompts/plan.md"] = "Plan.",
             ["prompts/shared.md"] = "Shared.",
             ["macros/closing-line.md"] = "{\n  \"label\": \"Closing\"\n}\nBye.",
             ["macros/notes/index.json"] = "[ { \"id\": \"red_flags\", \"file\": \"red_flags.md\" } ]",
             ["macros/notes/red_flags.md"] = "{\n  \"label\": \"Red flags\"\n}\nNone.",
             ["snippets/style.md"] = "{\n  \"label\": \"Style\"\n}\nPlain.",
             ["snippets/sections/index.json"] = "[ { \"id\": \"safety_netting\", \"file\": \"safety-netting.md\" }, { \"id\": \"billing\", \"file\": \"billing.md\" } ]",
             ["snippets/sections/safety-netting.md"] = "{\n  \"label\": \"Safety\"\n}\nSafe.",
             ["snippets/sections/billing.md"] = "{\n  \"label\": \"Billing\"\n}\nBill.",
         });

    [Fact]
    public void Mismatches_ListEveryKind_AndLeaveASharedPromptOut()
    {
        var (manifest, files) = Package();

        var found = WorkflowFileMove.Mismatches(manifest, files);

        Assert.Equal(
            new[] { "prompts/draft-note.md → prompts/draft.md", "macros/closing-line.md → macros/closing.md", "snippets/sections/safety-netting.md → snippets/sections/safety_netting.md" },
            found.Select(m => $"{m.Path} → {m.Implied}"));
    }

    [Fact]
    public void ANodePrompt_Moves_AndTheNodeNamesIt()
    {
        var (manifest, files) = Package();

        Assert.True(WorkflowFileMove.Apply(manifest, files, "prompts/draft-note.md", "prompts/draft.md"));

        Assert.Equal("prompts/draft.md", manifest["nodes"]![0]!["prompt"]!.GetValue<string>());
        Assert.Equal("Draft.", files["prompts/draft.md"]);
        Assert.False(files.ContainsKey("prompts/draft-note.md"));
    }

    [Fact]
    public void AMacro_Moves_AndTheMapNamesIt()
    {
        var (manifest, files) = Package();

        Assert.True(WorkflowFileMove.Apply(manifest, files, "macros/closing-line.md", "macros/closing.md"));

        Assert.Equal("macros/closing.md", manifest["macros"]!["closing"]!.GetValue<string>());
        Assert.True(files.ContainsKey("macros/closing.md"));
    }

    [Fact]
    public void AFolderItem_Moves_AndItsIndexKeepsTheOrder()
    {
        var (manifest, files) = Package();

        Assert.True(WorkflowFileMove.Apply(manifest, files, "snippets/sections/safety-netting.md", "snippets/sections/safety_netting.md"));

        var index = JsonNode.Parse(files["snippets/sections/index.json"])!.AsArray();
        Assert.Equal(new[] { "safety_netting.md", "billing.md" }, index.Select(i => i!["file"]!.GetValue<string>()));
        Assert.Equal(new[] { "safety_netting", "billing" }, index.Select(i => i!["id"]!.GetValue<string>()));
        Assert.True(files.ContainsKey("snippets/sections/safety_netting.md"));
    }

    [Fact]
    public void AMove_IsRefused_OntoAnExistingFile_OrForASharedPrompt()
    {
        var (manifest, files) = Package();

        Assert.Equal("A file 'prompts/plan.md' already exists.", WorkflowFileMove.Refusal(manifest, files, "prompts/draft-note.md", "prompts/plan.md"));
        Assert.Equal("The prompt is shared by a, b — it keeps its name.", WorkflowFileMove.Refusal(manifest, files, "prompts/shared.md", "prompts/a.md"));
        Assert.Null(WorkflowFileMove.Refusal(manifest, files, "prompts/draft-note.md", "prompts/draft.md"));
    }

    [Fact]
    public void AMovedPackage_StillValidates()
    {
        var (manifest, files) = V27Package.Minimal();
        var root = JsonNode.Parse(JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions))!.AsObject();
        var moved = new Dictionary<string, string>(files, StringComparer.Ordinal);
        var target = root["nodes"]!.AsArray().OfType<JsonObject>().First(n => n["prompt"] is not null);
        var from = target["prompt"]!.GetValue<string>();
        var to = "prompts/moved_by_the_test.md";

        Assert.True(WorkflowFileMove.Apply(root, moved, from, to));

        var read = WorkflowPackageManifestJson.Read(root.ToJsonString(), "moved", WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.Empty(WorkflowPackageValidator.Validate(read, moved, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog()).Errors);
    }
}
