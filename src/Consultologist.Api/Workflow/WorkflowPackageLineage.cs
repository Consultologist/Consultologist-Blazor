using System.Collections.Concurrent;
using System.Text.Json;
using Azure;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Workflow;

/// <summary>
/// Walks a package's derivedFrom chain to the root (#89). Reads manifests only —
/// never full package resolution — so lineage displays even for chain members the
/// engine wouldn't execute, and each hop is one small blob read. Published
/// versions are immutable, so resolved derivedFrom values cache forever.
/// </summary>
public sealed class WorkflowPackageLineageResolver
{
    /// <summary>
    /// Permissive on purpose, unlike the load and publish paths (#416). Lineage
    /// walks backwards through history, and history contains manifests this
    /// engine would refuse to run — general@v2026.07.4 still carries the retired
    /// sectionSteps vocabulary. Refusing an ancestor would break the chain
    /// display for every descendant, over a package nobody is trying to execute.
    ///
    /// Tighten where a manifest is about to be used; stay permissive where one
    /// is only being traversed.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkflowPackageBlobContainerFactory _containers;
    private readonly ConcurrentDictionary<string, string?> _derivedFromCache = new(StringComparer.Ordinal);

    public WorkflowPackageLineageResolver(WorkflowPackageBlobContainerFactory containers)
    {
        _containers = containers;
    }

    /// <summary>
    /// The ordered chain, start → root, as concrete refs. The start ref must be
    /// concrete (job records and the content endpoint always are).
    /// </summary>
    public Task<IReadOnlyList<string>> GetLineageAsync(WorkflowPackageRef start, CancellationToken cancellationToken)
        => WalkAsync(start, reference => ReadDerivedFromAsync(reference, cancellationToken));

    /// <summary>The pure chain walk over a derivedFrom reader — the unit-tested core.</summary>
    internal static async Task<IReadOnlyList<string>> WalkAsync(
        WorkflowPackageRef start,
        Func<WorkflowPackageRef, Task<string?>> readDerivedFrom)
    {
        if (start.IsLatest)
        {
            throw new ArgumentException($"Lineage requires a concrete ref; '{start}' is a latest pointer.", nameof(start));
        }

        var chain = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = start;

        while (true)
        {
            var currentRef = current.ToString();

            if (!visited.Add(currentRef))
            {
                throw new InvalidOperationException($"Workflow package lineage of '{start}' contains a cycle at '{currentRef}'.");
            }

            // No depth cap (#463): every republish derives from its predecessor,
            // so a chain grows by one per version and a cap of ten fired on the
            // operator's own package. The visited set is what terminates the
            // walk; a chain is as long as a package's history, and that is the
            // point of showing it.
            chain.Add(currentRef);

            var derivedFrom = await readDerivedFrom(current);

            if (derivedFrom is null)
            {
                return chain;
            }

            if (!WorkflowPackageRef.TryParse(derivedFrom, out var parent) || parent!.IsLatest)
            {
                throw new InvalidOperationException(
                    $"Workflow package '{currentRef}' declares an invalid derivedFrom '{derivedFrom}' (a concrete ref is required).");
            }

            current = parent;
        }
    }

    private async Task<string?> ReadDerivedFromAsync(WorkflowPackageRef reference, CancellationToken cancellationToken)
    {
        var cacheKey = reference.ToString();

        if (_derivedFromCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        string manifestJson;
        try
        {
            var blob = _containers.GetContainerFor(reference.Name)
                .GetBlobClient($"{reference.Name}/{reference.Version}/manifest.json");
            var response = await blob.DownloadContentAsync(cancellationToken);
            manifestJson = response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException($"Workflow package '{cacheKey}' was not found in the registry.", ex);
        }

        var manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidOperationException($"Workflow package manifest for '{cacheKey}' is empty or malformed.");

        _derivedFromCache.TryAdd(cacheKey, manifest.DerivedFrom);
        return manifest.DerivedFrom;
    }
}

public sealed record WorkflowPackageLineageResponse(IReadOnlyList<string> Chain);
