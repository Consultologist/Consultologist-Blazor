using System.Collections.Concurrent;
using Consultologist.Api.Agents;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Workflow;

/// <summary>
/// B2 (#1001): loads a specific, concrete formats catalogue version on demand
/// and caches it forever — the <see cref="CatalogResolver"/> of the output
/// contracts, for the v25 formats. A stamped package resolves its formats and
/// the agents behind them against the catalogue version its stamp names, not
/// the engine's global pin, so a pin bump can never strand a published package
/// and a package reproducibly runs the agents it was published against.
/// Published versions are immutable (each at its own {version}/ prefix; only
/// latest.json moves), so a concrete ref caches forever; a failed load is not
/// cached. The global singleton stays the engine default (publish,
/// attestation); this is the lazy cache for stamped refs.
/// </summary>
public sealed class FormatCatalogResolver
{
    private readonly IConfiguration _configuration;
    private readonly Func<string, CancellationToken, Task<WorkflowFormatCatalog>> _loader;
    private readonly ConcurrentDictionary<string, WorkflowFormatCatalog> _cache = new(StringComparer.Ordinal);

    public FormatCatalogResolver(IConfiguration configuration)
    {
        _configuration = configuration;
        _loader = (formatsRef, cancellationToken) =>
            FormatCatalogLoader.LoadFromRegistryAsync(ResolvePublicUri(), formatsRef, cancellationToken);
    }

    // The loader is a seam so tests can count loads and return a built catalogue
    // without reaching blob storage.
    internal FormatCatalogResolver(
        IConfiguration configuration,
        Func<string, CancellationToken, Task<WorkflowFormatCatalog>> loader)
    {
        _configuration = configuration;
        _loader = loader;
    }

    /// <summary>The catalogue at <paramref name="formatsRef"/> (a concrete formats@vYYYY.MM.N). Loaded once per ref and cached on success.</summary>
    public async Task<WorkflowFormatCatalog> GetAsync(string formatsRef, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(formatsRef, out var cached))
        {
            return cached;
        }

        var catalog = await _loader(formatsRef, cancellationToken);
        _cache.TryAdd(formatsRef, catalog);
        return catalog;
    }

    private Uri ResolvePublicUri()
    {
        var publicUri = _configuration["WorkflowPackages:PublicBlobServiceUri"] is { Length: > 0 } explicitUri
            ? explicitUri
            : StorageAccounts.DerivedUri(_configuration, StorageAccounts.PublicRole, "blob");

        if (string.IsNullOrWhiteSpace(publicUri))
        {
            throw new InvalidOperationException(
                "The public registry is not configured, so a package's pinned formats catalogue cannot be loaded.");
        }

        return new Uri(publicUri);
    }
}
