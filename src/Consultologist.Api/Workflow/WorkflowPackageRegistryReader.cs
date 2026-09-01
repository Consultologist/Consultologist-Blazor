using Azure;
using Azure.Storage.Blobs;

namespace Consultologist.Api.Workflow;

/// <summary>
/// #452: read access to both registries as a sweep needs it — every blob name
/// in one registry, and one blob of one package. The public side is the
/// anonymous container when configured; the private side is the account
/// registry the app's identity reads. Nothing here writes.
/// </summary>
public interface IWorkflowPackageRegistryReader
{
    /// <summary>Whether the public registry is configured at all (local dev may have none).</summary>
    bool HasPublicRegistry { get; }

    Task<IReadOnlyList<string>> ListBlobNamesAsync(bool privateRegistry, CancellationToken cancellationToken);

    /// <summary>The blob at <paramref name="blobPath"/> from the container <paramref name="packageName"/> resolves from; null when it is not there.</summary>
    Task<string?> TryDownloadAsync(string packageName, string blobPath, CancellationToken cancellationToken);
}

public sealed class WorkflowPackageRegistryReader : IWorkflowPackageRegistryReader
{
    private readonly WorkflowPackageBlobContainerFactory _containers;

    public WorkflowPackageRegistryReader(WorkflowPackageBlobContainerFactory containers)
    {
        _containers = containers;
    }

    public bool HasPublicRegistry => _containers.HasPublicContainer;

    public async Task<IReadOnlyList<string>> ListBlobNamesAsync(bool privateRegistry, CancellationToken cancellationToken)
    {
        // #602: "the private registry" is the org/personal pair — the union is
        // still one registry to every caller.
        var containers = privateRegistry
            ? _containers.GetPrivateContainers()
            : _containers.GetPublicContainer() is { } publicContainer
                ? new[] { publicContainer }
                : Array.Empty<BlobContainerClient>();

        var names = new List<string>();
        foreach (var container in containers)
        {
            try
            {
                await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
                {
                    names.Add(blob.Name);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // A missing container (local dev) is an empty one.
            }
        }

        return names;
    }

    public async Task<string?> TryDownloadAsync(string packageName, string blobPath, CancellationToken cancellationToken)
    {
        foreach (var container in _containers.GetContainersFor(packageName))
        {
            try
            {
                var response = await container.GetBlobClient(blobPath).DownloadContentAsync(cancellationToken);
                return response.Value.Content.ToString();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Not in this candidate — a fork lives in exactly one of the
                // private pair (#602), so keep trying.
            }
        }

        return null;
    }
}
