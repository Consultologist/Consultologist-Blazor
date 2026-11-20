using System.Diagnostics;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>#1500 (specVersion 31, area 03): the two pins, the bounds and the registry the area reads.</summary>
public class TypePinsTests
{
    private static string Props() => File.ReadAllText(TypeFixture.RepoFile("Directory.Packages.props"));

    [Fact]
    public void Markdig_IsTheVersionTheContractNames()
    {
        var constants = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(TypeFixture.RepoFile("external", "consultologist-package-format", "v31", "constants.json")))!;
        var named = (string)constants["markdig"]!["version"]!;
        var loaded = FileVersionInfo.GetVersionInfo(typeof(Markdig.Markdown).Assembly.Location).ProductVersion!;

        Assert.Equal("0.41.3", named);
        Assert.StartsWith(named, loaded);
        Assert.Contains($"<PackageVersion Include=\"Markdig\" Version=\"{named}\" />", Props());
    }

    [Fact]
    public void Corvus_IsPinnedExactly()
    {
        Assert.Contains("<PackageVersion Include=\"Corvus.Text.Json.Validator\" Version=\"5.8.12\" />", Props());
        Assert.DoesNotMatch(@"Corvus\.Text\.Json\.Validator""[^>]*Version=""[^""]*[*\[\(,]", Props());
    }

    [Fact]
    public void FormatBounds_AreTheContracts()
    {
        var bounds = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(TypeFixture.RepoFile("external", "consultologist-package-format", "v31", "bounds.json")))!["formatBounds"]!;

        int[] code = [FormatBounds.Passes, FormatBounds.Depth, FormatBounds.Parallel];
        Assert.Equal([(int)bounds["passes"]!, (int)bounds["depth"]!, (int)bounds["parallel"]!], code);
        Assert.Equal([15, 15, 15], code);
    }

    [Fact]
    public void ThePublishedRegistry_HoldsTheNineBuiltIns_AndTheCatalogue()
    {
        var registry = TypeFixture.Published();
        var index = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(TypeFixture.RepoFile("external", "consultologist-package-format", "v31", "formats", "index.json")))!;
        var items = index["items"]!.AsArray().Select(i => (string)i!).ToArray();

        Assert.Equal(9, items.Length);
        Assert.Equal(items.Order(StringComparer.Ordinal), registry.BuiltInFormats.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["classification", "concept_list"], registry.CatalogueFormats.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["concept_bullets", "concept_context"], registry.BuiltInRenderers.Order(StringComparer.Ordinal));
        Assert.True(registry.Bindings.ContainsKey("formats"));
        Assert.True(registry.Agents.ContainsKey("agents"));
        Assert.Equal(registry.BuiltInFormats.Keys.ToHashSet(), registry.Names.BuiltInFormats);
        Assert.Equal(registry.CatalogueFormats.Keys.ToHashSet(), registry.Names.CatalogueFormats);
        Assert.Equal(registry.BuiltInRenderers, registry.Names.BuiltInRenderers);
    }

    [Fact]
    public void ARegistryFileThatIsMissingOrNotAnObject_IsAFormatException()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "index.json"), "{\"items\":[\"x\"]}");
            Assert.Throws<FormatException>(() => TypeRegistry.Read(folder, folder, Path.Combine(folder, "index.json"), Path.Combine(folder, "none.json"), Path.Combine(folder, "none.json")));
            File.WriteAllText(Path.Combine(folder, "x.json"), "[]");
            Assert.Throws<FormatException>(() => TypeRegistry.Read(folder, folder, Path.Combine(folder, "index.json"), Path.Combine(folder, "none.json"), Path.Combine(folder, "none.json")));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Subject_NamesTheRoot_OrThePointer()
    {
        Assert.Equal("Format 'x' at the root", FormatSentences.Subject("Format 'x'", ""));
        Assert.Equal("Format 'x' at /properties/kind", FormatSentences.Subject("Format 'x'", "/properties/kind"));
    }
}
