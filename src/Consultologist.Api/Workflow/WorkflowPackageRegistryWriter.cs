using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Workflow;

/// <summary>
/// Thrown when a version's manifest already exists: published versions are
/// immutable, and the manifest's conditional create is the atomic guard. The
/// publish endpoint reacts by re-reading latest, bumping, and retrying.
/// </summary>
public sealed class WorkflowPackageVersionConflictException : Exception
{
    public WorkflowPackageVersionConflictException(string packageRef, Exception inner)
        : base($"Workflow package version {packageRef} already exists; published versions are immutable.", inner)
    {
    }
}

public interface IWorkflowPackageRegistryWriter
{
    // #602: writes name the account's kind — it picks which of the private
    // pair the fork lives in. Null falls to personal (TextBlobNaming's rule).

    /// <summary>The version the name's latest pointer holds, or null when the name has never been published.</summary>
    Task<string?> ReadLatestVersionAsync(string? accountKind, string name, CancellationToken cancellationToken);

    Task UploadFileAsync(string? accountKind, string name, string version, string path, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads the manifest with a conditional create (If-None-Match: *) — the
    /// commit marker. The store resolves manifest-first, so a version is
    /// invisible until this succeeds; an existing manifest throws
    /// <see cref="WorkflowPackageVersionConflictException"/>.
    /// </summary>
    Task CreateManifestAsync(string? accountKind, string name, string version, string manifestJson, CancellationToken cancellationToken);

    Task SetLatestPointerAsync(string? accountKind, string name, string version, CancellationToken cancellationToken);

    /// <summary>
    /// #559: every blob of one package — every version, the latest pointer,
    /// the folder — from the private registry. Kind-BLIND on purpose (#602):
    /// destruction never trusts a derived attribute (#462's rule), so the
    /// sweep covers both private containers. The immutability rule is
    /// about republishing, not existence: account closure is the one caller.
    /// Returns how many blobs went.
    /// </summary>
    Task<int> DeletePackageAsync(string name, CancellationToken cancellationToken);
}

public sealed class WorkflowPackageRegistryWriter : IWorkflowPackageRegistryWriter
{
    // Read case-insensitively; write camelCase — the same one-member object the
    // repo publish scripts write ({"version": "..."}). The bytes differ in
    // whitespace, which registry-layout.md § 6 (consultologist-provenance)
    // states is insignificant; every reader here is case- and space-tolerant.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly WorkflowPackageBlobContainerFactory _containers;

    public WorkflowPackageRegistryWriter(WorkflowPackageBlobContainerFactory containerFactory)
    {
        // #602: the container is resolved per call by the account's kind — a
        // singleton cannot bind one container when the target varies by user.
        _containers = containerFactory;
    }

    public async Task<string?> ReadLatestVersionAsync(string? accountKind, string name, CancellationToken cancellationToken)
    {
        try
        {
            var blob = _containers.GetContainer(accountKind).GetBlobClient($"{name}/latest.json");
            var response = await blob.DownloadContentAsync(cancellationToken);
            var pointer = JsonSerializer.Deserialize<LatestPointer>(response.Value.Content.ToString(), JsonOptions);
            return pointer?.Version;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UploadFileAsync(string? accountKind, string name, string version, string path, string content, CancellationToken cancellationToken)
    {
        var blob = _containers.GetContainer(accountKind).GetBlobClient($"{name}/{version}/{path}");
        await blob.UploadAsync(BinaryData.FromString(content), overwrite: true, cancellationToken);
    }

    public async Task CreateManifestAsync(string? accountKind, string name, string version, string manifestJson, CancellationToken cancellationToken)
    {
        var blob = _containers.GetContainer(accountKind).GetBlobClient($"{name}/{version}/manifest.json");

        try
        {
            await blob.UploadAsync(
                BinaryData.FromString(manifestJson),
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                },
                cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            throw new WorkflowPackageVersionConflictException($"{name}@{version}", ex);
        }
    }

    public async Task<int> DeletePackageAsync(string name, CancellationToken cancellationToken)
    {
        var deleted = 0;
        foreach (var container in _containers.GetPrivateContainers())
        {
            await foreach (var blob in container.GetBlobsAsync(prefix: $"{name}/", cancellationToken: cancellationToken))
            {
                await container.GetBlobClient(blob.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
                deleted++;
            }
        }

        return deleted;
    }

    public async Task SetLatestPointerAsync(string? accountKind, string name, string version, CancellationToken cancellationToken)
    {
        var blob = _containers.GetContainer(accountKind).GetBlobClient($"{name}/latest.json");
        var pointerJson = JsonSerializer.Serialize(new LatestPointer(version), JsonOptions);
        await blob.UploadAsync(BinaryData.FromString(pointerJson), overwrite: true, cancellationToken);
    }

    private sealed record LatestPointer(string Version);
}
