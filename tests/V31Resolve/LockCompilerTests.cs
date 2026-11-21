using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): <c>lock.json</c> (part 07 § 4.1; D9) compiled from a <c>resolve.json</c>, its hash and
/// a <see cref="Toolchain"/>: every member of the schema, the four libraries exact, the operations the package uses,
/// the built-in machinery and each built-in renderer an entry renders through, and no time. § 4.1's example is held
/// member by member.
/// </summary>
public class LockCompilerTests
{
    /// <summary>The toolchain of part 07 § 4.1's example, built by hand with its values.</summary>
    public static readonly Toolchain Example = new(
        "package-format@v2026.12.1",
        "scriban-dialect@1.0.0",
        new Dictionary<string, string>
        {
            ["Corvus.JsonSchema"] = "4.3.17",
            ["Corvus.Text.Json.JsonLogic"] = "5.8.11",
            ["Markdig"] = "0.41.3",
            ["Scriban"] = "7.2.5",
        },
        "1.0.0",
        "10.0.3",
        "16.0",
        "formats@v2026.12.1",
        "agents@v2026.12.1",
        null);

    /// <summary><see cref="ResolveFixture.NestedNote"/> as § 2.9 names it, compiled.</summary>
    public static ResolveResult NestedNote()
    {
        var files = ResolveFixture.NestedNote().WithJson("manifest.json", m => m["name"] = "example-nested-note");
        var check = SourceCheck.Run(files, ResolveFixture.Published());
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));
        var result = ResolveCompiler.Compile(check, ResolveFixture.Published(), Example.Scriban);
        Assert.NotNull(result.Document);
        return result;
    }

    private static string[] Keys(JsonNode? node) => node!.AsObject().Select(p => p.Key).ToArray();

    [Fact]
    public void HasEveryMember()
    {
        var resolve = NestedNote();
        var schema = V31Contract.Json("schemas", "lock.schema.json");
        var toolchain = Toolchain.Current("package-format@v2026.12.1", "formats@v2026.12.1", "agents@v2026.12.1");

        var lockDocument = LockCompiler.Compile(resolve.Document!, resolve.Hash!, toolchain);

        Assert.Equal(schema["required"]!.AsArray().Select(r => (string)r!), Keys(lockDocument));
        Assert.Equal(
            ["Corvus.JsonSchema", "Corvus.Text.Json.JsonLogic", "Markdig", "Scriban"],
            Keys(lockDocument["libraries"]).Order(StringComparer.Ordinal));
        Assert.Equal("invariant", (string)lockDocument["culture"]!);
        Assert.Equal(resolve.Hash, (string)lockDocument["resolve"]!["hash"]!);
        Assert.Equal(["hash"], Keys(lockDocument["resolve"]));
        Assert.Equal(["name", "version"], Keys(lockDocument["package"]));
        Assert.Equal(["dotnet", "unicode"], Keys(lockDocument["runtime"]));
        Assert.Equal(["agents", "formats", "outputContracts"], Keys(lockDocument["catalogues"]).Order(StringComparer.Ordinal));
        Assert.Equal(["packageFormat", "dialect"], Keys(lockDocument["resolver"]));
        Assert.Equal(1, (int)lockDocument["documentVersion"]!);

        // No time anywhere: nothing that reads as a date, and the same lock compiled again is the same text.
        Assert.DoesNotMatch(new Regex(@"\d{4}-\d{2}-\d{2}"), CanonicalJson.Write(lockDocument));
        Assert.Equal(CanonicalJson.Write(lockDocument), CanonicalJson.Write(LockCompiler.Compile(resolve.Document!, resolve.Hash!, toolchain)));
    }

    [Fact]
    public void IsTheSpecsExample()
    {
        // Part 07 § 4.1's block. Where the fixture differs from § 2.9 (no table involved), the fixture's value is held:
        // its draft_paragraph's if calls blank, so operations hold blank beside size (as requires does, Task 7).
        var resolve = NestedNote();
        var expected = new JsonObject
        {
            ["documentVersion"] = 1,
            ["package"] = new JsonObject { ["name"] = "example-nested-note", ["version"] = "v2026.12.1" },
            ["resolve"] = new JsonObject { ["hash"] = resolve.Hash },
            ["resolver"] = new JsonObject { ["packageFormat"] = "package-format@v2026.12.1", ["dialect"] = "scriban-dialect@1.0.0" },
            ["libraries"] = new JsonObject
            {
                ["Corvus.JsonSchema"] = "4.3.17",
                ["Corvus.Text.Json.JsonLogic"] = "5.8.11",
                ["Markdig"] = "0.41.3",
                ["Scriban"] = "7.2.5",
            },
            ["evaluator"] = "1.0.0",
            ["operations"] = new JsonObject { ["blank"] = "1.0.0", ["size"] = "1.0.0" },
            ["builtins"] = new JsonObject
            {
                ["formats"] = "1.0.0",
                ["markdown_escape"] = "1.0.0",
                ["plain_conversion"] = "1.0.0",
                ["printer"] = "1.0.0",
            },
            ["culture"] = "invariant",
            ["runtime"] = new JsonObject { ["dotnet"] = "10.0.3", ["unicode"] = "16.0" },
            ["catalogues"] = new JsonObject
            {
                ["agents"] = "agents@v2026.12.1",
                ["formats"] = "formats@v2026.12.1",
                ["outputContracts"] = null,
            },
        };

        var lockDocument = LockCompiler.Compile(resolve.Document!, resolve.Hash!, Example);

        Assert.Equal(CanonicalJson.Write(expected), CanonicalJson.Write(lockDocument));
        Assert.Equal(Keys(resolve.Document!["requires"]!["operations"]), Keys(lockDocument["operations"]));
    }

    [Fact]
    public void Builtins_AreConstantsJsons_WithEachBuiltInRendererRenderedThrough()
    {
        // Every entry that renders through a built-in renderer adds it, by either way of writing its id; a package
        // renderer adds nothing.
        var resolve = NestedNote();
        var document = resolve.Document!.DeepClone().AsObject();
        document["nodes"]!["node.plan"]!["renders"] = new JsonArray(
            new JsonObject { ["n"] = 1, ["source"] = "node.plan.value", ["renderer"] = "renderer.concept_bullets" },
            new JsonObject { ["n"] = 2, ["source"] = "node.plan.value", ["renderer"] = "renderer.house_style" });
        document["deliverables"]!["deliverable.note"]!["renders"] = new JsonArray(
            new JsonObject { ["n"] = 1, ["source"] = "node.refine.value", ["renderer"] = "concept_context" });

        var lockDocument = LockCompiler.Compile(document, resolve.Hash!, Example);

        var constants = V31Contract.Json("constants.json");
        Assert.Equal(CanonicalJson.Write(constants["builtins"]), CanonicalJson.Write(lockDocument["builtins"]));
    }

    [Fact]
    public void Operations_AreConstantsJsons()
    {
        var resolve = NestedNote();
        var document = resolve.Document!.DeepClone().AsObject();
        var constants = V31Contract.Json("constants.json")["operations"]!.AsObject();
        var every = new JsonObject();
        foreach (var (name, _) in constants)
            every[name] = "^1.0.0";
        document["requires"]!["operations"] = every;

        var lockDocument = LockCompiler.Compile(document, resolve.Hash!, Example);

        Assert.Equal(CanonicalJson.Write(constants), CanonicalJson.Write(lockDocument["operations"]));
    }

    [Fact]
    public void AnyDocument_GivesALock_NeverThrows()
    {
        foreach (var resolve in new[]
        {
            new JsonObject(),
            new JsonObject { ["package"] = new JsonArray(), ["requires"] = "x", ["nodes"] = new JsonArray() },
            new JsonObject { ["requires"] = new JsonObject { ["operations"] = new JsonArray(1, 2) }, ["nodes"] = new JsonObject { ["node.x"] = 5 } },
            new JsonObject { ["requires"] = new JsonObject { ["operations"] = new JsonObject { ["nonsense"] = "^1.0.0" } } },
        })
        {
            var lockDocument = LockCompiler.Compile(resolve, "sha256:" + new string('0', 64), Example);
            Assert.Equal(
                V31Contract.Json("schemas", "lock.schema.json")["required"]!.AsArray().Select(r => (string)r!),
                Keys(lockDocument));
            Assert.Empty(lockDocument["operations"]!.AsObject());
        }
    }
}
