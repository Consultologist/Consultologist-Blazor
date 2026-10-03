using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Tests;

/// <summary>
/// #923 phase 2: the per-catalogRef cache. Concrete catalog versions are
/// immutable, so a ref loads once and caches forever; a failed load is evicted
/// so a transient error does not strand the ref.
/// </summary>
public class CatalogResolverTests
{
    private static OutputContractCatalog LoadCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return OutputContractCatalog.Load(Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents"));
    }

    [Fact]
    public async Task GetAsync_LoadsOncePerDistinctRef_AndReturnsTheCachedInstance()
    {
        var catalog = LoadCatalog();
        var loaded = new List<string>();
        var resolver = new CatalogResolver(
            new ConfigurationBuilder().Build(),
            (catalogRef, _) =>
            {
                loaded.Add(catalogRef);
                return Task.FromResult(catalog);
            });

        var a1 = await resolver.GetAsync("output-contracts@v2026.08.1", CancellationToken.None);
        var a2 = await resolver.GetAsync("output-contracts@v2026.08.1", CancellationToken.None);
        var b1 = await resolver.GetAsync("output-contracts@v2026.09.1", CancellationToken.None);

        Assert.Same(a1, a2);
        Assert.Same(catalog, a1);
        Assert.Same(catalog, b1);
        // Two distinct refs → two loads; the repeat of the first is served from cache.
        Assert.Equal(new[] { "output-contracts@v2026.08.1", "output-contracts@v2026.09.1" }, loaded);
    }

    [Fact]
    public async Task GetAsync_DoesNotCacheAFailedLoad_SoTheNextCallReattempts()
    {
        var catalog = LoadCatalog();
        var attempts = 0;
        var resolver = new CatalogResolver(
            new ConfigurationBuilder().Build(),
            (_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException<OutputContractCatalog>(new InvalidOperationException("transient"))
                    : Task.FromResult(catalog);
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.GetAsync("output-contracts@v2026.08.1", CancellationToken.None));

        // The faulted load was evicted, so the retry re-attempts and succeeds.
        var ok = await resolver.GetAsync("output-contracts@v2026.08.1", CancellationToken.None);
        Assert.Same(catalog, ok);
        Assert.Equal(2, attempts);
    }
}
