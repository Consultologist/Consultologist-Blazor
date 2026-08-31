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
/// <summary>
/// #547: the shared plumbing of the text account's blob stores — the
/// Entra-first client (TextStorage__BlobServiceUri) with the
/// AzureWebJobsStorage connection-string fallback for Azurite, and the
/// JSON write/read/delete idioms. The two stores (outputs, inputs) differ
/// only in their container pair and payload type.
/// </summary>
internal sealed class TextBlobClientFactory
{
    private readonly Func<string, BlobContainerClient> _containerFor;
    private readonly bool _createContainers;

    public TextBlobClientFactory(IConfiguration configuration, TokenCredential credential, ILogger logger, string storeName)
    {
        var serviceUri = configuration["TextStorage:BlobServiceUri"];
        if (!string.IsNullOrWhiteSpace(serviceUri))
        {
            var service = new BlobServiceClient(new Uri(serviceUri), credential);
            _containerFor = service.GetBlobContainerClient;
            _createContainers = false;
            logger.LogInformation("{Store} using Entra ID auth. BlobServiceUri={BlobServiceUri}", storeName, serviceUri);
            return;
        }

        var connectionStringName = configuration["TextStorage:ConnectionStringName"] ?? "AzureWebJobsStorage";
        var connectionString = configuration[connectionStringName] ?? Environment.GetEnvironmentVariable(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{storeName} is not configured: set TextStorage__BlobServiceUri (managed identity) or a connection string for local development.");
        }

        _containerFor = name => new BlobContainerClient(connectionString, name);
        _createContainers = true;
        logger.LogWarning("{Store} using connection-string auth (local-dev fallback). Prefer TextStorage__BlobServiceUri with managed identity.", storeName);
    }

    public async Task WriteJsonAsync<T>(string container, string name, T payload, CancellationToken cancellationToken)
    {
        var client = _containerFor(container);

        if (_createContainers)
        {
            await client.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        await client.GetBlobClient(name).UploadAsync(BinaryData.FromObjectAsJson(payload), overwrite: true, cancellationToken);
    }

    public async Task<T?> ReadJsonAsync<T>(string container, string name, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var content = await _containerFor(container).GetBlobClient(name).DownloadContentAsync(cancellationToken);
            return content.Value.Content.ToObjectFromJson<T>();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task DeleteAsync(string container, string name, CancellationToken cancellationToken) =>
        _containerFor(container).GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}

/// <summary>
/// The container the account's kind names (storage-separation.md § 2.5).
/// Null falls to personal — #517's own default (no tenant is personal);
/// unreachable for stamped accounts, and every account is stamped (#556).
/// </summary>
internal static class TextBlobNaming
{
    public static string ContainerFor(string? accountKind, string organisationContainer, string personalContainer) =>
        string.Equals(accountKind, SignInKinds.Organisation, StringComparison.Ordinal)
            ? organisationContainer
            : personalContainer;

    public static string NameFor(string appUserId, string jobId) => $"{appUserId}/{jobId}.json";
}

public sealed class JobOutputsBlobStore : IJobOutputsBlobStore
{
    private const string OrganisationContainer = "org-job-outputs";
    private const string PersonalContainer = "personal-job-outputs";

    private readonly TextBlobClientFactory _blobs;

    public JobOutputsBlobStore(IConfiguration configuration, TokenCredential credential, ILogger<JobOutputsBlobStore> logger)
    {
        _blobs = new TextBlobClientFactory(configuration, credential, logger, "Job outputs store");
    }

    internal static string ContainerFor(string? accountKind) =>
        TextBlobNaming.ContainerFor(accountKind, OrganisationContainer, PersonalContainer);

    internal static string NameFor(string appUserId, string jobId) => TextBlobNaming.NameFor(appUserId, jobId);

    public async Task<ConsultOutputsBlobPointer> WriteAsync(string? accountKind, string appUserId, string jobId, JobOutputsPayload payload, CancellationToken cancellationToken)
    {
        var pointer = new ConsultOutputsBlobPointer(ContainerFor(accountKind), NameFor(appUserId, jobId));
        await _blobs.WriteJsonAsync(pointer.Container, pointer.Name, payload, cancellationToken);
        return pointer;
    }

    public Task<JobOutputsPayload?> ReadAsync(ConsultOutputsBlobPointer pointer, CancellationToken cancellationToken) =>
        _blobs.ReadJsonAsync<JobOutputsPayload>(pointer.Container, pointer.Name, cancellationToken);

    public Task DeleteAsync(ConsultOutputsBlobPointer pointer, CancellationToken cancellationToken) =>
        _blobs.DeleteAsync(pointer.Container, pointer.Name, cancellationToken);
}

/// <summary>
/// #547 (storage-separation.md § 2.1): one JSON blob per job — the effective
/// input map, exactly what ran: documents already extracted, previous-run
/// references already copied, values already coerced. Written by the starter
/// before the orchestration is scheduled; what History shows while held and
/// what a rerun (#549) resubmits.
/// </summary>
public sealed record JobInputsPayload(
    int Version,
    // Declared input id → the resolver-form string the orchestration read —
    // byte-for-byte what the effective-input hash was computed over.
    IReadOnlyDictionary<string, string>? Effective,
    // Supplied input id → the value's typed wire JSON (ConsultInputValue) —
    // what a rerun resubmits to reproduce the hash.
    IReadOnlyDictionary<string, string>? Supplied)
{
    public const int CurrentVersion = 1;
}

public interface IJobInputsBlobStore
{
    Task<ConsultInputsBlobPointer> WriteAsync(string? accountKind, string appUserId, string jobId, JobInputsPayload payload, CancellationToken cancellationToken);

    Task<JobInputsPayload?> ReadAsync(ConsultInputsBlobPointer pointer, CancellationToken cancellationToken);

    Task DeleteAsync(ConsultInputsBlobPointer pointer, CancellationToken cancellationToken);
}

public sealed class JobInputsBlobStore : IJobInputsBlobStore
{
    private const string OrganisationContainer = "org-job-inputs";
    private const string PersonalContainer = "personal-job-inputs";

    private readonly TextBlobClientFactory _blobs;

    public JobInputsBlobStore(IConfiguration configuration, TokenCredential credential, ILogger<JobInputsBlobStore> logger)
    {
        _blobs = new TextBlobClientFactory(configuration, credential, logger, "Job inputs store");
    }

    internal static string ContainerFor(string? accountKind) =>
        TextBlobNaming.ContainerFor(accountKind, OrganisationContainer, PersonalContainer);

    internal static string NameFor(string appUserId, string jobId) => TextBlobNaming.NameFor(appUserId, jobId);

    public async Task<ConsultInputsBlobPointer> WriteAsync(string? accountKind, string appUserId, string jobId, JobInputsPayload payload, CancellationToken cancellationToken)
    {
        var pointer = new ConsultInputsBlobPointer(ContainerFor(accountKind), NameFor(appUserId, jobId));
        await _blobs.WriteJsonAsync(pointer.Container, pointer.Name, payload, cancellationToken);
        return pointer;
    }

    public Task<JobInputsPayload?> ReadAsync(ConsultInputsBlobPointer pointer, CancellationToken cancellationToken) =>
        _blobs.ReadJsonAsync<JobInputsPayload>(pointer.Container, pointer.Name, cancellationToken);

    public Task DeleteAsync(ConsultInputsBlobPointer pointer, CancellationToken cancellationToken) =>
        _blobs.DeleteAsync(pointer.Container, pointer.Name, cancellationToken);
}
