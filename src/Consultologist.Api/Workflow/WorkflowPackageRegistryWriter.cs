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
    /// <summary>The version the name's latest pointer holds, or null when the name has never been published.</summary>
    Task<string?> ReadLatestVersionAsync(string name, CancellationToken cancellationToken);

    Task UploadFileAsync(string name, string version, string path, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads the manifest with a conditional create (If-None-Match: *) — the
    /// commit marker. The store resolves manifest-first, so a version is
    /// invisible until this succeeds; an existing manifest throws
    /// <see cref="WorkflowPackageVersionConflictException"/>.
    /// </summary>
    Task CreateManifestAsync(string name, string version, string manifestJson, CancellationToken cancellationToken);

    Task SetLatestPointerAsync(string name, string version, CancellationToken cancellationToken);
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

    private readonly BlobContainerClient _container;

    public WorkflowPackageRegistryWriter(WorkflowPackageBlobContainerFactory containerFactory)
    {
        _container = containerFactory.GetContainer();
    }

    public async Task<string?> ReadLatestVersionAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var blob = _container.GetBlobClient($"{name}/latest.json");
            var response = await blob.DownloadContentAsync(cancellationToken);
            var pointer = JsonSerializer.Deserialize<LatestPointer>(response.Value.Content.ToString(), JsonOptions);
            return pointer?.Version;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UploadFileAsync(string name, string version, string path, string content, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient($"{name}/{version}/{path}");
        await blob.UploadAsync(BinaryData.FromString(content), overwrite: true, cancellationToken);
    }

    public async Task CreateManifestAsync(string name, string version, string manifestJson, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient($"{name}/{version}/manifest.json");

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

    public async Task SetLatestPointerAsync(string name, string version, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient($"{name}/latest.json");
        var pointerJson = JsonSerializer.Serialize(new LatestPointer(version), JsonOptions);
        await blob.UploadAsync(BinaryData.FromString(pointerJson), overwrite: true, cancellationToken);
    }

    private sealed record LatestPointer(string Version);
}
