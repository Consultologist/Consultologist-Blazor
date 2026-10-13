using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Tests;

/// <summary>The bundled formats catalogue, as the engine loads it (B2 #1001) — from the re-pinned agents submodule.</summary>
public static class TestFormats
{
    public static readonly WorkflowFormatCatalog Catalog = FormatCatalogLoader.Load(AgentsDirectory());

    public static string AgentsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents");
    }
}

public sealed class FormatCatalogLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "formats-load-" + Guid.NewGuid().ToString("N"));

    public FormatCatalogLoaderTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "formats"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteFixture()
    {
        File.WriteAllText(Path.Combine(_dir, "formats.json"), WorkflowFormatCatalogIndexTests.Example);
        File.WriteAllText(Path.Combine(_dir, "formats", "prose.json"), FormatFixtures.Prose);
        File.WriteAllText(Path.Combine(_dir, "formats", "classification.json"), FormatFixtures.Classification);
        File.WriteAllText(Path.Combine(_dir, "formats", "concept-list.json"), FormatFixtures.ConceptList);
        File.WriteAllText(Path.Combine(_dir, "formats", "rendered.json"), FormatFixtures.Rendered);
        File.WriteAllText(Path.Combine(_dir, "formats", "terms-subset.json"), FormatFixtures.TermsSubset);
    }

    [Fact]
    public void TheBundledCatalogue_Loads_AndBindsTheFivePublishedFormats()
    {
        var catalog = TestFormats.Catalog;

        Assert.Equal("formats@v2026.10.1", catalog.ResolvedRef);
        Assert.Equal(new[] { "classification", "concept-list", "prose", "rendered", "terms-subset" }, catalog.Formats.Keys.Order());
        Assert.Equal("test-json@48", catalog.Formats["prose"].Entry.AgentDisplay);
        Assert.Equal("terms-subset", catalog.Formats["terms-subset"].Entry.Op);
        Assert.Equal("custom-json@1", catalog.CustomProducer.AgentDisplay);
        Assert.True(catalog.Formats["prose"].Spec.AllowsOutputMacros);
    }

    [Fact]
    public void TheBundledCatalogue_BindsWhatTheRegistryPublished_ByCanonicalBytes()
    {
        // The published file and the bundled file are the same bytes, so the
        // engine's formatHash of a bundled file equals the registry's.
        var bundled = File.ReadAllText(Path.Combine(TestFormats.AgentsDirectory(), "formats", "concept-list.json"));

        Assert.Equal(WorkflowFormatSpecJson.CanonicalJson(bundled), TestFormats.Catalog.Formats["concept-list"].CanonicalJson);
    }

    [Fact]
    public void AFixtureDirectory_Loads()
    {
        WriteFixture();

        var catalog = FormatCatalogLoader.Load(_dir);

        Assert.Equal("formats@v2026.11.1", catalog.ResolvedRef);
        Assert.Equal(5, catalog.Formats.Count);
    }

    [Fact]
    public void NoIndex_FailsLoud()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FormatCatalogLoader.Load(_dir));

        Assert.StartsWith("Formats catalogue not found at '", ex.Message);
    }

    [Theory]
    [InlineData("{ nope", "is not valid JSON")]
    [InlineData("""{ "version": "2026.11.1", "formats": { "prose": { "file": "formats/prose.json", "agentName": "a", "agentVersion": 1 } }, "producers": { "custom": { "agentName": "c", "agentVersion": 1, "trailer": "t" } } }""", "must declare a CalVer version")]
    [InlineData("""{ "version": "v2026.11.1", "formats": { "prose": { "file": "formats/missing.json", "agentName": "a", "agentVersion": 1 } }, "producers": { "custom": { "agentName": "c", "agentVersion": 1, "trailer": "t" } } }""", "file 'formats/missing.json' is missing")]
    [InlineData("""{ "version": "v2026.11.1", "formats": { "prose": { "file": "formats/prose.json" } }, "producers": { "custom": { "agentName": "c", "agentVersion": 1, "trailer": "t" } } }""", "is attested and its index entry must name the agent")]
    public void ADefectiveCatalogue_FailsLoud_WithTheReadersSentence(string indexJson, string part)
    {
        WriteFixture();
        File.WriteAllText(Path.Combine(_dir, "formats.json"), indexJson);

        var ex = Assert.Throws<InvalidOperationException>(() => FormatCatalogLoader.Load(_dir));

        Assert.Contains(part, ex.Message);
        Assert.StartsWith("Formats catalogue at '", ex.Message);
    }

    [Theory]
    [InlineData("output-contracts@latest")]
    [InlineData("formats")]
    [InlineData("formats@2026.10.1")]
    public async Task ABadPin_IsRefused_BeforeAnyNetworkCall(string pin)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FormatCatalogLoader.LoadFromRegistryAsync(new Uri("https://example.invalid"), pin));

        Assert.Equal($"Formats__Pin '{pin}' is not a valid catalogue ref (expected formats@latest or formats@vYYYY.MM.N).", ex.Message);
    }

    [Fact]
    public void TheDefaultPin_IsLatest()
    {
        Assert.Equal("formats@latest", FormatCatalogLoader.DefaultPin);
        Assert.Equal("Formats__Pin", FormatCatalogLoader.PinSetting);
    }
}

public class FormatCatalogResolverTests
{
    private static WorkflowFormatCatalog Catalog(string version)
    {
        var index = WorkflowFormatCatalogIndexTests.Read(WorkflowFormatCatalogIndexTests.Example.Replace("v2026.11.1", version));
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["formats/prose.json"] = FormatFixtures.Prose,
            ["formats/classification.json"] = FormatFixtures.Classification,
            ["formats/concept-list.json"] = FormatFixtures.ConceptList,
            ["formats/rendered.json"] = FormatFixtures.Rendered,
            ["formats/terms-subset.json"] = FormatFixtures.TermsSubset,
        };
        var errors = new List<string>();
        return WorkflowFormatCatalog.Build(index, path => files.GetValueOrDefault(path), errors)!;
    }

    [Fact]
    public async Task LoadsOncePerRef_AndCachesForever()
    {
        var loaded = new List<string>();
        var resolver = new FormatCatalogResolver(new ConfigurationBuilder().Build(), (formatsRef, _) =>
        {
            loaded.Add(formatsRef);
            return Task.FromResult(Catalog(formatsRef[(formatsRef.IndexOf('@') + 1)..]));
        });

        var a1 = await resolver.GetAsync("formats@v2026.10.1", CancellationToken.None);
        var a2 = await resolver.GetAsync("formats@v2026.10.1", CancellationToken.None);
        var b = await resolver.GetAsync("formats@v2026.11.1", CancellationToken.None);

        Assert.Same(a1, a2);
        Assert.Equal("formats@v2026.10.1", a1.ResolvedRef);
        Assert.Equal("formats@v2026.11.1", b.ResolvedRef);
        Assert.Equal(new[] { "formats@v2026.10.1", "formats@v2026.11.1" }, loaded);
    }

    [Fact]
    public async Task AFailedLoad_IsNotCached()
    {
        var attempts = 0;
        var resolver = new FormatCatalogResolver(new ConfigurationBuilder().Build(), (_, _) =>
            ++attempts == 1
                ? Task.FromException<WorkflowFormatCatalog>(new InvalidOperationException("transient"))
                : Task.FromResult(Catalog("v2026.10.1")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.GetAsync("formats@v2026.10.1", CancellationToken.None));
        var catalog = await resolver.GetAsync("formats@v2026.10.1", CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal("formats@v2026.10.1", catalog.ResolvedRef);
    }

    [Fact]
    public async Task WithoutAPublicRegistry_TheProductionLoader_SaysSo()
    {
        var resolver = new FormatCatalogResolver(new ConfigurationBuilder().Build());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.GetAsync("formats@v2026.10.1", CancellationToken.None));

        Assert.Equal("The public registry is not configured, so a package's pinned formats catalogue cannot be loaded.", ex.Message);
    }
}
