using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Consultologist.Api.Auth;
using Consultologist.Api.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #557 (storage-separation.md § 2.2): one JSON blob per completed job — the
/// deliverables with their text, the block texts, the node concepts — on the
/// text account, in the container the account's kind names. What the blob
/// holds is exactly the four text species the entity stops carrying; names
/// and flags (Appended, Unsigned, hashes, statuses) stay on the entity.
/// </summary>
public sealed record JobOutputsPayload(
    // The payload's own shape version, for readers of stored blobs.
    int Version,
    string? AssembledDocument,
    IReadOnlyList<JobOutputsDocument>? Documents,
    IReadOnlyDictionary<string, string>? BlockTexts,
    IReadOnlyDictionary<string, IReadOnlyList<ClinicalConcept>>? NodeConcepts)
{
    public const int CurrentVersion = 1;
}

/// <summary>One deliverable's text; the hash rides for self-checkability.</summary>
public sealed record JobOutputsDocument(string ResultId, string Text, string? DocumentHash);

public interface IJobOutputsBlobStore
{
    /// <summary>Writes the payload and returns the pointer the record stores.</summary>
    Task<ConsultOutputsBlobPointer> WriteAsync(string? accountKind, string appUserId, string jobId, JobOutputsPayload payload, CancellationToken cancellationToken);

    /// <summary>The stored payload, or null when the blob is gone (dropped, or never written).</summary>
    Task<JobOutputsPayload?> ReadAsync(ConsultOutputsBlobPointer pointer, CancellationToken cancellationToken);

    Task DeleteAsync(ConsultOutputsBlobPointer pointer, CancellationToken cancellationToken);
}

/// <summary>
/// The text-account blob store (TextStorage__BlobServiceUri, #556's account):
/// Entra ID first, the AzureWebJobsStorage connection string as the local-dev
/// (Azurite) fallback — the WorkflowPackageBlobContainerFactory posture. The
/// production containers are operator-created (M1); CreateIfNotExists runs
/// only on the connection-string path, for Azurite.
/// </summary>
public sealed class JobOutputsBlobStore : IJobOutputsBlobStore
{
    private const string OrganisationContainer = "org-job-outputs";
    private const string PersonalContainer = "personal-job-outputs";

    private readonly ILogger<JobOutputsBlobStore> _logger;
    private readonly Func<string, BlobContainerClient> _containerFor;
    private readonly bool _createContainers;

    public JobOutputsBlobStore(IConfiguration configuration, TokenCredential credential, ILogger<JobOutputsBlobStore> logger)
    {
        _logger = logger;

        var serviceUri = configuration["TextStorage:BlobServiceUri"];
        if (!string.IsNullOrWhiteSpace(serviceUri))
        {
            var service = new BlobServiceClient(new Uri(serviceUri), credential);
            _containerFor = service.GetBlobContainerClient;
            _createContainers = false;
            _logger.LogInformation("Job outputs store using Entra ID auth. BlobServiceUri={BlobServiceUri}", serviceUri);
            return;
        }

        var connectionStringName = configuration["TextStorage:ConnectionStringName"] ?? "AzureWebJobsStorage";
        var connectionString = configuration[connectionStringName] ?? Environment.GetEnvironmentVariable(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Job outputs storage is not configured: set TextStorage__BlobServiceUri (managed identity) or a connection string for local development.");
        }

        _containerFor = name => new BlobContainerClient(connectionString, name);
        _createContainers = true;
        _logger.LogWarning("Job outputs store using connection-string auth (local-dev fallback). Prefer TextStorage__BlobServiceUri with managed identity.");
    }

    /// <summary>
    /// The container the account's kind names (storage-separation.md § 2.5).
    /// Null falls to personal — #517's own default (no tenant is personal);
    /// unreachable for stamped accounts, and every account is stamped (#556).
    /// </summary>
    internal static string ContainerFor(string? accountKind) =>
        string.Equals(accountKind, SignInKinds.Organisation, StringComparison.Ordinal)
            ? OrganisationContainer
            : PersonalContainer;

    internal static string NameFor(string appUserId, string jobId) => $"{appUserId}/{jobId}.json";

    public async Task<ConsultOutputsBlobPointer> WriteAsync(string? accountKind, string appUserId, string jobId, JobOutputsPayload payload, CancellationToken cancellationToken)
    {
        var pointer = new ConsultOutputsBlobPointer(ContainerFor(accountKind), NameFor(appUserId, jobId));
        var container = _containerFor(pointer.Container);

        if (_createContainers)
        {
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        await container.GetBlobClient(pointer.Name).UploadAsync(
            BinaryData.FromObjectAsJson(payload),
            overwrite: true,
            cancellationToken);

        return pointer;
    }

    public async Task<JobOutputsPayload?> ReadAsync(ConsultOutputsBlobPointer pointer, CancellationToken cancellationToken)
    {
        try
        {
            var content = await _containerFor(pointer.Container).GetBlobClient(pointer.Name).DownloadContentAsync(cancellationToken);
            return content.Value.Content.ToObjectFromJson<JobOutputsPayload>();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task DeleteAsync(ConsultOutputsBlobPointer pointer, CancellationToken cancellationToken) =>
        _containerFor(pointer.Container).GetBlobClient(pointer.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}
