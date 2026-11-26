using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using PublishedTypeRegistry = Consultologist.Api.V31.PublishedTypeRegistry;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1507 (specVersion 31, area 10; R11, D22): the registry the Api reads 31's types from, bundled into its build output by
/// <c>Consultologist.Api.csproj</c> (the package format's <c>v31/formats/</c> and <c>v31/renderers/</c>, the agents
/// catalogue's <c>v31/formats/</c>, beside the <c>bindings.json</c> and <c>agents.json</c> it already carried). It is the
/// published registry the tests read from the submodules, name for name and byte for byte, so a folder dropped from the
/// csproj fails here.
/// </summary>
public class PublishedTypeRegistryTests
{
    [Fact]
    public void TheBundledRegistry_IsThePublishedOne()
    {
        // the build output's copy: the test project's output receives the Api's bundled files, under the same paths
        var bundled = new PublishedTypeRegistry(AppContext.BaseDirectory, OutputContractCatalog.ResolveDirectory()).Registry;
        var published = V31Contract.PublishedTypes();

        AssertSame(published.BuiltInFormats, bundled.BuiltInFormats);
        AssertSame(published.CatalogueFormats, bundled.CatalogueFormats);
        Assert.True(JsonNode.DeepEquals(published.Bindings, bundled.Bindings), "bindings.json differs.");
        Assert.True(JsonNode.DeepEquals(published.Agents, bundled.Agents), "agents.json differs.");
        Assert.Equal(published.BuiltInRenderers.Keys.Order(StringComparer.Ordinal), bundled.BuiltInRenderers.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, renderer) in published.BuiltInRenderers)
        {
            var copy = bundled.BuiltInRenderers[id];
            Assert.Equal(renderer.Version, copy.Version);
            Assert.True(JsonNode.DeepEquals(renderer.Header, copy.Header), $"renderer '{id}''s header differs.");
            Assert.Equal(renderer.Body, copy.Body);
        }

        Assert.NotEmpty(bundled.BuiltInFormats);
        Assert.NotEmpty(bundled.CatalogueFormats);
        Assert.NotEmpty(bundled.BuiltInRenderers);
    }

    [Fact]
    public void TheRegistry_IsReadOnFirstUse_AndAFailureIsNotKept()
    {
        var missing = Path.Combine(Path.GetTempPath(), "consultologist-no-registry-" + Guid.NewGuid().ToString("N"));
        var registry = new PublishedTypeRegistry(missing, missing);

        // nothing is read by construction; a read that fails throws at the use, every time
        Assert.ThrowsAny<Exception>(() => registry.Registry);
        Assert.ThrowsAny<Exception>(() => registry.Registry);
    }

    private static void AssertSame(IReadOnlyDictionary<string, JsonObject> expected, IReadOnlyDictionary<string, JsonObject> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, format) in expected)
        {
            Assert.True(JsonNode.DeepEquals(format, actual[name]), $"format '{name}' differs.");
        }
    }
}
