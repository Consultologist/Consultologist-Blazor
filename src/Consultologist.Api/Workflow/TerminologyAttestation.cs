using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Workflow;

/// <summary>The SNOMED CT edition the terminology server had loaded, as it reported it (#403).</summary>
public sealed record TerminologySnapshot(string? Edition, string? Version, string? ImportDate);

/// <summary>What the terminology server said about itself, and when.</summary>
public sealed record TerminologyAttestation(TerminologySnapshot? Terminology, string? ServerRef, DateTimeOffset FetchedAtUtc);

public interface ITerminologyAttestationSource
{
    /// <summary>The current attestation, or null when none is configured or none has ever been read.</summary>
    ValueTask<TerminologyAttestation?> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// #403: reads GET Public/Terminology on the MCP function app (Terminology__InfoUrl)
/// — the edition Snowstorm has loaded and the server's build — once per
/// cache window, so the starter can stamp both on every record. Absent
/// setting: nothing is recorded, ever. Unreachable: the last good answer, else
/// null — a record then says nothing rather than something wrong.
/// </summary>
public sealed class TerminologyAttestationClient : ITerminologyAttestationSource
{
    public const string InfoUrlSetting = "Terminology__InfoUrl";
    public const string CacheMinutesSetting = "Terminology__CacheMinutes";
    public const string ServerName = "snomed-snowstorm-mcp";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TerminologyAttestationClient> _logger;
    private readonly string? _infoUrl;
    private readonly TimeSpan _cacheFor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TerminologyAttestation? _last;

    public TerminologyAttestationClient(IHttpClientFactory httpClientFactory, ILogger<TerminologyAttestationClient> logger)
        : this(httpClientFactory, logger,
            Environment.GetEnvironmentVariable(InfoUrlSetting),
            int.TryParse(Environment.GetEnvironmentVariable(CacheMinutesSetting), out var minutes) && minutes > 0 ? minutes : 60)
    {
    }

    public TerminologyAttestationClient(IHttpClientFactory httpClientFactory, ILogger<TerminologyAttestationClient> logger, string? infoUrl, int cacheMinutes)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _infoUrl = string.IsNullOrWhiteSpace(infoUrl) ? null : infoUrl;
        _cacheFor = TimeSpan.FromMinutes(cacheMinutes);
    }

    public bool IsConfigured => _infoUrl != null;

    public async ValueTask<TerminologyAttestation?> GetAsync(CancellationToken cancellationToken)
    {
        if (_infoUrl == null)
        {
            return null;
        }

        if (_last is { } fresh && DateTimeOffset.UtcNow - fresh.FetchedAtUtc < _cacheFor)
        {
            return fresh;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_last is { } stillFresh && DateTimeOffset.UtcNow - stillFresh.FetchedAtUtc < _cacheFor)
            {
                return stillFresh;
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var document = await client.GetFromJsonAsync<TerminologyInfoDocument>(_infoUrl, cancellationToken);
            var attestation = Describe(document, DateTimeOffset.UtcNow);
            if (attestation != null)
            {
                _last = attestation;
                _logger.LogInformation("Terminology attested. Edition={Edition}, Version={Version}, Server={Server}",
                    attestation.Terminology?.Edition, attestation.Terminology?.Version, attestation.ServerRef);
                Console.Error.WriteLine($"[Terminology] edition {attestation.Terminology?.Version ?? "—"}, server {attestation.ServerRef ?? "—"}");
            }

            return _last;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // The last good answer outlives an outage; a record from the
            // outage window carries what the server last said, dated.
            _logger.LogWarning(ex, "Terminology attestation unreachable at {Url}; records carry the last answer, if any.", _infoUrl);
            Console.Error.WriteLine($"[Terminology] could not read {_infoUrl}: {ex.GetType().Name}");
            return _last;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The rule: the edition as reported; the server ref from its commit, else its version; null when the document says nothing.</summary>
    public static TerminologyAttestation? Describe(TerminologyInfoDocument? document, DateTimeOffset now)
    {
        if (document == null)
        {
            return null;
        }

        var snapshot = document.Edition == null && document.Version == null && document.ImportDate == null
            ? null
            : new TerminologySnapshot(document.Edition, document.Version, document.ImportDate);
        var serverRef = document.Commit is { Length: > 0 } commit ? $"{ServerName}@{commit}"
            : document.ServerVersion is { Length: > 0 } version ? $"{ServerName}@{version}"
            : null;

        return snapshot == null && serverRef == null ? null : new TerminologyAttestation(snapshot, serverRef, now);
    }

    /// <summary>The wire shape of GET Public/Terminology on the server (PascalCase, case-insensitive here).</summary>
    public sealed record TerminologyInfoDocument(string? Edition, string? Version, string? ImportDate, string? ServerVersion, string? Commit);
}
