using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): the published formats' two hashes and type strings, frozen in
/// <c>Golden/format-hashes.json</c>, so a change to either equality or to a published file shows.
/// With <c>UPDATE_V31_GOLDENS=1</c> the test writes the golden into the source tree instead.
/// </summary>
public class FormatHashGoldenTests
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public void ThePublishedFormats_HashAsFrozen()
    {
        var registry = TypeFixture.Published();
        var formats = FormatSet.ForPackage(registry, []);
        var hashes = new JsonObject();
        foreach (var (id, schema) in registry.BuiltInFormats.Concat(registry.CatalogueFormats).OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            hashes[id] = new JsonObject
            {
                ["typeHash"] = FormatTypes.TypeHash(JsonValue.Create(id), formats),
                ["formatHash"] = FormatTypes.FormatHash(JsonValue.Create(id), formats),
                ["typeString"] = TypeStrings.Print(schema, formats),
            };
        }

        var text = hashes.ToJsonString(Indented) + "\n";
        var root = Path.GetDirectoryName(TypeFixture.RepoFile("Consultologist.sln"))!;
        var golden = Path.Combine(root, "tests", "V31Types", "Golden", "format-hashes.json");
        if (Environment.GetEnvironmentVariable("UPDATE_V31_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, text);
        }

        Assert.True(File.Exists(golden), $"{golden} is missing; run the test with UPDATE_V31_GOLDENS=1 to write it.");
        Assert.Equal(11, hashes.Count);
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), text);
    }
}
