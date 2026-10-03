using System.Collections.Concurrent;
using Consultologist.Api.Agents;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Workflow;

/// <summary>
/// #923 phase 2: loads a specific, concrete output-contract catalog version on
/// demand and caches it forever. A stamped package resolves — its contracts and
/// the agents behind them — against the catalog version its stamp names, not the
/// engine's global pin, so a global pin bump can never strand a published package
/// and a package reproducibly runs the agents it was published against.
///
/// Published catalog versions are immutable (each lives at its own {version}/
/// prefix; only latest.json is mutable), so a concrete ref caches forever —
/// mirroring WorkflowPackageStore._packageCache, not the TTL latest cache. A
/// failed load is evicted so a transient error does not poison the key. The
/// global catalog singleton stays the engine default (publish, attestation,
/// unstamped legacy packages); this is a separate lazy cache for stamped refs.
/// </summary>
public sealed class CatalogResolver
{
    private readonly IConfiguration _configuration;
    private readonly Func<string, CancellationToken, Task<OutputContractCatalog>> _loader;
    private readonly ConcurrentDictionary<string, OutputContractCatalog> _cache = new(StringComparer.Ordinal);

    public CatalogResolver(IConfiguration configuration)
    {
        _configuration = configuration;
        // The production loader resolves the public registry URI itself, so a
        // test-supplied loader never touches configuration or blob storage.
        _loader = (catalogRef, cancellationToken) =>
            OutputContractCatalog.LoadFromRegistryAsync(ResolvePublicUri(), catalogRef, cancellationToken);
    }

    // The loader is a seam so tests can count loads and return a built catalog
    // without reaching blob storage.
    internal CatalogResolver(
        IConfiguration configuration,
        Func<string, CancellationToken, Task<OutputContractCatalog>> loader)
    {
        _configuration = configuration;
        _loader = loader;
    }

    /// <summary>
    /// The catalog at <paramref name="catalogRef"/> (a concrete
    /// output-contracts@vYYYY.MM.N). Loaded once per ref and cached on success.
    /// </summary>
    public async Task<OutputContractCatalog> GetAsync(string catalogRef, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(catalogRef, out var cached))
        {
            return cached;
        }

        // Load outside the dictionary and cache only on success — a faulted load
        // must never be cached, or a transient error would strand the ref forever.
        // Two concurrent first-callers for the same ref may both load; that is
        // benign (versions are immutable) and mirrors WorkflowPackageStore's
        // resolve-then-TryAdd.
        var catalog = await _loader(catalogRef, cancellationToken);
        _cache.TryAdd(catalogRef, catalog);
        return catalog;
    }

    private Uri ResolvePublicUri()
    {
        // The same resolution OperatorCatalogStrands uses (explicit override,
        // else derived from the storage region — #596).
        var publicUri = _configuration["WorkflowPackages:PublicBlobServiceUri"] is { Length: > 0 } explicitUri
            ? explicitUri
            : StorageAccounts.DerivedUri(_configuration, StorageAccounts.PublicRole, "blob");

        if (string.IsNullOrWhiteSpace(publicUri))
        {
            throw new InvalidOperationException(
                "The public registry is not configured, so a package's pinned catalog cannot be loaded.");
        }

        return new Uri(publicUri);
    }
}
