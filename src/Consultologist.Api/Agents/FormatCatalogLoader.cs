using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Agents;

/// <summary>
/// Loads the v25 formats catalogue (package-format-v25-design.md § 3.3; B2
/// #1001) — the index and one file per format — from the bundled
/// <c>agents/</c> directory (local dev, tests, the attestation baseline) or
/// from the public registry at the pinned version (the Azure runtime path).
/// The shape, the cross-checks and the binding are the library's
/// (<see cref="WorkflowFormatCatalogIndexJson"/>, <see cref="WorkflowFormatCatalog"/>):
/// this class only fetches bytes. Any defect is a startup failure, as it is
/// for the output-contract catalogue: an engine without a coherent catalogue
/// must not accept jobs.
/// </summary>
public static class FormatCatalogLoader
{
    public const string RegistryName = WorkflowFormatRegistry.Name;

    public const string IndexFileName = WorkflowFormatRegistry.IndexFileName;

    /// <summary>The pin setting; its default resolves through the registry's latest pointer.</summary>
    public const string PinSetting = "Formats__Pin";

    public static string DefaultPin => $"{RegistryName}@latest";

    /// <summary>Loads and binds the catalogue from the bundled directory (the attestation service's convention).</summary>
    public static WorkflowFormatCatalog Load(string? directory = null)
    {
        directory ??= OutputContractCatalog.ResolveDirectory();
        var indexPath = Path.Combine(directory, IndexFileName);

        if (!File.Exists(indexPath))
        {
            throw new InvalidOperationException($"Formats catalogue not found at '{indexPath}'.");
        }

        return Build(
            File.ReadAllText(indexPath),
            file =>
            {
                var path = Path.Combine(directory, file);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            },
            $"'{indexPath}'");
    }

    /// <summary>
    /// Loads the catalogue from the public registry at the pinned version. The
    /// pin is a ref (<c>formats@latest</c> or <c>formats@vYYYY.MM.N</c>);
    /// latest resolves through the mutable pointer, and the returned
    /// catalogue's ResolvedRef is always concrete.
    /// </summary>
    public static async Task<WorkflowFormatCatalog> LoadFromRegistryAsync(
        Uri publicBlobServiceUri,
        string pin,
        CancellationToken cancellationToken = default)
    {
        if (!WorkflowPackageRef.TryParse(pin, out var pinRef) || !string.Equals(pinRef!.Name, RegistryName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{PinSetting} '{pin}' is not a valid catalogue ref (expected {RegistryName}@latest or {RegistryName}@vYYYY.MM.N).");
        }

        var container = new BlobServiceClient(publicBlobServiceUri).GetBlobContainerClient(RegistryName);

        var version = pinRef.IsLatest
            ? await ResolveLatestVersionAsync(container, cancellationToken)
            : pinRef.Version;

        var indexJson = await DownloadTextAsync(container, $"{version}/{IndexFileName}", cancellationToken);

        // Pre-fetch the format files the index names; Build stays synchronous.
        var errors = new List<string>();

        if (!WorkflowFormatCatalogIndexJson.TryRead(indexJson, $"Formats catalogue registry blob '{version}/{IndexFileName}'", errors, out var index))
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in index!.Formats.Values)
        {
            if (!files.ContainsKey(entry.File))
            {
                files[entry.File] = await DownloadTextAsync(container, $"{version}/{entry.File}", cancellationToken);
            }
        }

        var catalog = Bind(index, file => files.GetValueOrDefault(file), $"registry {RegistryName}@{version}");

        // The artifact must self-describe as the version it was fetched as —
        // integrity between the blob path and the content.
        if (!string.Equals(catalog.ResolvedRef, $"{RegistryName}@{version}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Formats catalogue registry blob '{version}/{IndexFileName}' declares version '{catalog.ResolvedRef}' — path and content disagree.");
        }

        return catalog;
    }

    /// <summary>Shared core: read the index, bind the files, throw on any sentence.</summary>
    internal static WorkflowFormatCatalog Build(string indexJson, Func<string, string?> readFile, string source)
    {
        var errors = new List<string>();

        if (!WorkflowFormatCatalogIndexJson.TryRead(indexJson, $"Formats catalogue at {source}", errors, out var index))
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        return Bind(index!, readFile, source);
    }

    private static WorkflowFormatCatalog Bind(WorkflowFormatCatalogIndex index, Func<string, string?> readFile, string source)
    {
        var errors = new List<string>();
        var catalog = WorkflowFormatCatalog.Build(index, readFile, errors);

        if (catalog is null)
        {
            throw new InvalidOperationException($"Formats catalogue at {source} does not bind: {string.Join(" ", errors)}");
        }

        return catalog;
    }

    private static async Task<string> ResolveLatestVersionAsync(BlobContainerClient container, CancellationToken cancellationToken)
    {
        var pointerJson = await DownloadTextAsync(container, "latest.json", cancellationToken);
        var pointer = JsonSerializer.Deserialize<LatestPointer>(pointerJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (pointer is null || !CalVerVersion.TryParse(pointer.Version, out _))
        {
            throw new InvalidOperationException($"Latest pointer for the '{RegistryName}' registry is missing or holds an invalid version.");
        }

        return pointer.Version!;
    }

    private static async Task<string> DownloadTextAsync(BlobContainerClient container, string blobPath, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.GetBlobClient(blobPath).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"Formats catalogue registry blob '{blobPath}' was not found in container '{RegistryName}'.", ex);
        }
    }

    private sealed record LatestPointer(string? Version);
}
