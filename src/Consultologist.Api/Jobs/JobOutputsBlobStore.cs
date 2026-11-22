using Consultologist.PackageFormat;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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

    /// <summary>#559: every blob under {appUserId}/ in BOTH kind containers — kind-blind, safer than trusting the stamp.</summary>
    Task<int> DeleteAccountAsync(string appUserId, CancellationToken cancellationToken);
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
        var explicitUri = configuration["TextStorage:BlobServiceUri"];
        var serviceUri = !string.IsNullOrWhiteSpace(explicitUri)
            ? explicitUri
            : StorageAccounts.DerivedUri(configuration, StorageAccounts.TextRole, "blob"); // #596
        if (!string.IsNullOrWhiteSpace(serviceUri))
        {
            var service = new BlobServiceClient(new Uri(serviceUri), credential);
            _containerFor = service.GetBlobContainerClient;
            _createContainers = false;
            logger.LogInformation(
                "{Store} using Entra ID auth ({Rung}). BlobServiceUri={BlobServiceUri}",
                storeName,
                string.IsNullOrWhiteSpace(explicitUri) ? "derived from Storage__Region" : "explicit setting",
                serviceUri);
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

    /// <summary>
    /// #1506: text exactly as given (UTF-8, no BOM). With <paramref name="onlyIfAbsent"/> the upload carries
    /// <c>If-None-Match: *</c> and answers false when the blob is there already.
    /// </summary>
    public async Task<bool> WriteTextAsync(string container, string name, string text, bool onlyIfAbsent, CancellationToken cancellationToken)
    {
        var client = _containerFor(container);

        if (_createContainers)
        {
            await client.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        var content = BinaryData.FromBytes(new System.Text.UTF8Encoding(false).GetBytes(text));
        var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" } };
        if (onlyIfAbsent)
        {
            options.Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All };
        }

        try
        {
            await client.GetBlobClient(name).UploadAsync(content, options, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (onlyIfAbsent && (ex.Status == 409 || ex.Status == 412))
        {
            return false;
        }
    }

    /// <summary>#1506: the blob's text, or null when it is not there.</summary>
    public async Task<string?> ReadTextAsync(string container, string name, CancellationToken cancellationToken)
    {
        try
        {
            var content = await _containerFor(container).GetBlobClient(name).DownloadContentAsync(cancellationToken);
            return new System.Text.UTF8Encoding(false).GetString(content.Value.Content.ToArray());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
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

    /// <summary>#1503 (D9): the names of the blobs under a prefix, ordinally; a missing container (local dev) holds none.</summary>
    public async Task<IReadOnlyList<string>> ListNamesAsync(string container, string prefix, CancellationToken cancellationToken)
    {
        var names = new List<string>();

        try
        {
            await foreach (var blob in _containerFor(container).GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                names.Add(blob.Name);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// #559: every blob under a prefix — the closure's per-container sweep.
    /// A missing container (local dev) reads as nothing to delete. Returns
    /// how many blobs went.
    /// </summary>
    public async Task<int> DeleteByPrefixAsync(string container, string prefix, CancellationToken cancellationToken)
    {
        var client = _containerFor(container);
        var deleted = 0;

        try
        {
            await foreach (var blob in client.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                await client.GetBlobClient(blob.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
                deleted++;
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        return deleted;
    }
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

    public async Task<int> DeleteAccountAsync(string appUserId, CancellationToken cancellationToken) =>
        await _blobs.DeleteByPrefixAsync(OrganisationContainer, $"{appUserId}/", cancellationToken)
        + await _blobs.DeleteByPrefixAsync(PersonalContainer, $"{appUserId}/", cancellationToken);
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

    /// <summary>#559: every blob under {appUserId}/ in BOTH kind containers — kind-blind, safer than trusting the stamp.</summary>
    Task<int> DeleteAccountAsync(string appUserId, CancellationToken cancellationToken);
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
        var pointer = PointerFor(accountKind, appUserId, jobId);
        await _blobs.WriteJsonAsync(pointer.Container, pointer.Name, payload, cancellationToken);
        return pointer;
    }

    public Task<JobInputsPayload?> ReadAsync(ConsultInputsBlobPointer pointer, CancellationToken cancellationToken) =>
        _blobs.ReadJsonAsync<JobInputsPayload>(pointer.Container, pointer.Name, cancellationToken);

    /// <summary>
    /// #1503 (specVersion 31): where a job's held inputs live, the 5–30 payload's or a 31 job's run-inputs document: the
    /// kind's container, <c>{appUserId}/{jobId}.json</c>. So the retention drop and account closure sweep either.
    /// </summary>
    internal static ConsultInputsBlobPointer PointerFor(string? accountKind, string appUserId, string jobId) =>
        new(ContainerFor(accountKind), NameFor(appUserId, jobId));

    /// <summary>#1503 (specVersion 31): writes a 31 job's run-inputs document where its held inputs live, and returns the pointer.</summary>
    public async Task<ConsultInputsBlobPointer> WriteDocumentAsync(string? accountKind, string appUserId, string jobId, System.Text.Json.Nodes.JsonObject document, CancellationToken cancellationToken)
    {
        var pointer = PointerFor(accountKind, appUserId, jobId);
        await _blobs.WriteJsonAsync(pointer.Container, pointer.Name, document, cancellationToken);
        return pointer;
    }

    /// <summary>#1503 (specVersion 31): a 31 job's run-inputs document, or null when it is gone (dropped, or never written).</summary>
    public Task<System.Text.Json.Nodes.JsonObject?> ReadDocumentAsync(ConsultInputsBlobPointer pointer, CancellationToken cancellationToken) =>
        _blobs.ReadJsonAsync<System.Text.Json.Nodes.JsonObject>(pointer.Container, pointer.Name, cancellationToken);

    public Task DeleteAsync(ConsultInputsBlobPointer pointer, CancellationToken cancellationToken) =>
        _blobs.DeleteAsync(pointer.Container, pointer.Name, cancellationToken);

    public async Task<int> DeleteAccountAsync(string appUserId, CancellationToken cancellationToken) =>
        await _blobs.DeleteByPrefixAsync(OrganisationContainer, $"{appUserId}/", cancellationToken)
        + await _blobs.DeleteByPrefixAsync(PersonalContainer, $"{appUserId}/", cancellationToken);
}
