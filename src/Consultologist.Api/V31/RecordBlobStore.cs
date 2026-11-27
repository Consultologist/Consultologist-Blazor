using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Consultologist.Api.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>#1506 (D2, D10): a 31 job's retained text and its canonical document, in blobs beside the job's outputs.</summary>
public interface IRecordBlobStore
{
    /// <param name="part"><c>produced</c> (written by the activity that made it) or <c>settled</c> (written with the row).</param>
    Task WriteTextAsync(string? accountKind, string appUserId, string jobId, string key, string part, JsonObject text, CancellationToken ct);

    /// <summary>The two parts merged into one object (a member in both: <c>settled</c> wins); null when neither is there.</summary>
    Task<JsonObject?> ReadTextAsync(string? accountKind, string appUserId, string jobId, string key, CancellationToken ct);

    /// <summary>The job's <c>text/</c> prefix only; the canonical document stays.</summary>
    Task DeleteTextAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct);

    /// <summary>Writes the document exactly as given (UTF-8, no BOM) unless one is there; false when one is.</summary>
    Task<bool> WriteRecordAsync(string? accountKind, string appUserId, string jobId, string document, CancellationToken ct);

    Task<string?> ReadRecordAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct);

    /// <summary>R5 (#1509): every blob under <c>{appUserId}/</c> in the records container (account closure).</summary>
    Task DeleteAccountRecordsAsync(string appUserId, CancellationToken ct);
}

/// <summary>The few blob calls <see cref="BlobRecordStore"/> makes; the tests drive the store over an in-memory one.</summary>
internal interface IRecordBlobs
{
    /// <summary>With <paramref name="onlyIfAbsent"/>, false when the blob is there already (nothing is changed).</summary>
    Task<bool> PutAsync(string container, string name, string content, bool onlyIfAbsent, CancellationToken ct);

    Task<string?> GetAsync(string container, string name, CancellationToken ct);

    Task DeleteByPrefixAsync(string container, string prefix, CancellationToken ct);
}

/// <summary>The text account's client for the text blobs; <paramref name="records"/> (the records account's, else the text account's) for <see cref="BlobRecordStore.RecordContainer"/>.</summary>
internal sealed class FactoryRecordBlobs(TextBlobClientFactory text, TextBlobClientFactory records) : IRecordBlobs
{
    private TextBlobClientFactory For(string container) =>
        string.Equals(container, BlobRecordStore.RecordContainer, StringComparison.Ordinal) ? records : text;

    private volatile bool _recordsContainerEnsured;

    public async Task<bool> PutAsync(string container, string name, string content, bool onlyIfAbsent, CancellationToken ct)
    {
        if (!_recordsContainerEnsured && string.Equals(container, BlobRecordStore.RecordContainer, StringComparison.Ordinal))
        {
            // made on the first write, as the row table is
            await records.EnsureContainerAsync(container, ct);
            _recordsContainerEnsured = true;
        }

        return await For(container).WriteTextAsync(container, name, content, onlyIfAbsent, ct);
    }

    public Task<string?> GetAsync(string container, string name, CancellationToken ct) =>
        For(container).ReadTextAsync(container, name, ct);

    public async Task DeleteByPrefixAsync(string container, string prefix, CancellationToken ct) =>
        await For(container).DeleteByPrefixAsync(container, prefix, ct);
}

/// <summary>
/// #1506 (D10): text at <c>{appUserId}/{jobId}/text/{sha256 hex of the key}.{part}.json</c>, the document at
/// <c>{appUserId}/{jobId}/record.json</c>. The text is in the account kind's outputs container of the text account; the
/// document (permanent, no patient data; R5, #1509) is in <see cref="RecordContainer"/> of the records account
/// (<c>ConsultGenerationRecordStorage:BlobServiceUri</c>, else the text account's, with a warning). Constructing it touches no storage.
/// </summary>
public sealed class BlobRecordStore : IRecordBlobStore
{
    /// <summary>R5 (#1509): the records account's container for <c>record.json</c>; no lifecycle rule applies to it.</summary>
    public const string RecordContainer = "job-records";

    internal const string RecordServiceSetting = "ConsultGenerationRecordStorage:BlobServiceUri";

    private static readonly string[] Parts = ["produced", "settled"];

    private readonly IRecordBlobs _blobs;

    public BlobRecordStore(IConfiguration configuration, TokenCredential credential, ILogger<BlobRecordStore> logger)
        : this(Blobs(configuration, credential, logger))
    {
    }

    private static FactoryRecordBlobs Blobs(IConfiguration configuration, TokenCredential credential, ILogger logger)
    {
        var text = new TextBlobClientFactory(configuration, credential, logger, "Record store");
        var uri = configuration[RecordServiceSetting];
        if (string.IsNullOrWhiteSpace(uri))
        {
            logger.LogWarning(
                "{Setting} is not set: record.json is written to the text account, whose lifecycle rule deletes it after 30 days (it is restored from the rows on the next read).",
                RecordServiceSetting);
            return new FactoryRecordBlobs(text, text);
        }

        return new FactoryRecordBlobs(text, new TextBlobClientFactory(uri, credential, logger, "Record store (records account)", RecordServiceSetting));
    }

    internal BlobRecordStore(IRecordBlobs blobs) => _blobs = blobs;

    internal static string TextPrefix(string appUserId, string jobId) => $"{appUserId}/{jobId}/text/";

    internal static string TextName(string appUserId, string jobId, string key, string part) =>
        $"{TextPrefix(appUserId, jobId)}{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()}.{part}.json";

    internal static string RecordName(string appUserId, string jobId) => $"{appUserId}/{jobId}/record.json";

    public Task WriteTextAsync(string? accountKind, string appUserId, string jobId, string key, string part, JsonObject text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(text);
        if (!Parts.Contains(part, StringComparer.Ordinal))
            throw new ArgumentException("The part is 'produced' or 'settled'.", nameof(part));

        return _blobs.PutAsync(JobOutputsBlobStore.ContainerFor(accountKind), TextName(appUserId, jobId, key, part), text.ToJsonString(), false, ct);
    }

    public async Task<JsonObject?> ReadTextAsync(string? accountKind, string appUserId, string jobId, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        var container = JobOutputsBlobStore.ContainerFor(accountKind);
        JsonObject? merged = null;
        foreach (var part in Parts) // produced first, so a member in both is settled's
        {
            var content = await _blobs.GetAsync(container, TextName(appUserId, jobId, key, part), ct);
            if (content is null || JsonNode.Parse(content) is not JsonObject stored)
                continue;

            merged ??= new JsonObject();
            foreach (var (name, value) in stored.ToList())
            {
                stored.Remove(name);
                merged[name] = value;
            }
        }

        return merged;
    }

    public Task DeleteTextAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct) =>
        _blobs.DeleteByPrefixAsync(JobOutputsBlobStore.ContainerFor(accountKind), TextPrefix(appUserId, jobId), ct);

    public Task<bool> WriteRecordAsync(string? accountKind, string appUserId, string jobId, string document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _blobs.PutAsync(RecordContainer, RecordName(appUserId, jobId), document, true, ct);
    }

    public Task<string?> ReadRecordAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct) =>
        _blobs.GetAsync(RecordContainer, RecordName(appUserId, jobId), ct);

    public Task DeleteAccountRecordsAsync(string appUserId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(appUserId);
        return _blobs.DeleteByPrefixAsync(RecordContainer, $"{appUserId}/", ct);
    }
}
