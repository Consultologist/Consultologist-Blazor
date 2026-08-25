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
        var container = privateRegistry ? _containers.GetContainer() : _containers.GetPublicContainer();
        if (container == null)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            names.Add(blob.Name);
        }

        return names;
    }

    public async Task<string?> TryDownloadAsync(string packageName, string blobPath, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _containers.GetContainerFor(packageName).GetBlobClient(blobPath).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
