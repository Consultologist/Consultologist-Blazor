using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Documents;

/// <summary>
/// #239: OCR for image-only PDFs via Azure AI Document Intelligence
/// (prebuilt-read). The one impure document reader — a cloud call — invoked
/// only when the pure parser returns <c>no-text-layer</c> and OCR is
/// configured (<see cref="DocumentExtraction.ExtractAsync"/>).
///
/// Auth mirrors <see cref="Agents.AgentSectionGenerator"/>: identity-only, the
/// user-assigned managed identity via the injected <see cref="TokenCredential"/>
/// and AZURE_CLIENT_ID — never account keys. Entra ID auth requires the
/// resource's custom-subdomain endpoint (a regional endpoint does not support
/// token credentials), so the endpoint app setting must be that form.
///
/// Confidence is logged, never gated: refusing a low-confidence scan would
/// refuse a readable fax, and the product is verifiability-first — the
/// clinician reviews the consult, and the docintel/ extractor id on the record
/// says it came from an OCR'd scan.
/// </summary>
internal sealed class AzureDocumentIntelligenceOcr : IDocumentOcr
{
    internal const string EndpointSetting = "DocumentExtraction__OcrEndpoint";

    // Provenance: pinned by the SDK assembly version, computed once — the same
    // shape as pdfpig/… and html/… (docs/DOCUMENT_INPUT.md § 7).
    private static readonly string ExtractorId =
        ExtractorIdentity.For("docintel", typeof(DocumentIntelligenceClient).Assembly);

    private readonly ILogger<AzureDocumentIntelligenceOcr> _logger;
    private readonly TokenCredential _credential;
    private readonly Lazy<DocumentIntelligenceClient?> _client;
    private readonly int _timeoutSeconds;

    public AzureDocumentIntelligenceOcr(ILogger<AzureDocumentIntelligenceOcr> logger, TokenCredential credential)
    {
        _logger = logger;
        _credential = credential;
        _timeoutSeconds = GetEnvironmentInt("DocumentExtraction__OcrTimeoutSeconds", 60);
        // One client, built once and reused (SDK clients are thread-safe and
        // meant to be shared). Read config at build time — app-setting changes
        // restart the worker, which is exactly when this is rebuilt.
        _client = new Lazy<DocumentIntelligenceClient?>(BuildClient);
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EndpointSetting));

    public async Task<DocumentOcrResult> ReadAsync(byte[] pdfBytes, int? pageCount, CancellationToken cancellationToken)
    {
        DocumentIntelligenceClient? client;
        try
        {
            client = _client.Value;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A misconfiguration (e.g. endpoint set but AZURE_CLIENT_ID missing
            // in Azure). Transient from the caller's view — never a file fault.
            _logger.LogError(ex, "OCR client could not be created.");
            return Unavailable;
        }

        if (client is null)
        {
            return Unavailable;
        }

        try
        {
            // OCR's own clock, separate from the parser's 20s MaxParseDuration
            // (this runs after that gate has already released its slot).
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            var options = new AnalyzeDocumentOptions("prebuilt-read", BinaryData.FromBytes(pdfBytes));
            Operation<AnalyzeResult> operation =
                await client.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken: timeout.Token);

            var analysis = operation.Value;
            var meanConfidence = LogAndMeanConfidence(analysis, pageCount);

            return string.IsNullOrWhiteSpace(analysis.Content)
                ? new DocumentOcrResult(DocumentOcrStatus.Empty, null, null)
                : new DocumentOcrResult(DocumentOcrStatus.Extracted, analysis.Content, ExtractorId, meanConfidence);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our timeout fired, not the caller's cancellation. A readable fax
            // must never be reported unreadable — this is "try again".
            _logger.LogWarning("OCR timed out after {TimeoutSeconds}s.", _timeoutSeconds);
            return Unavailable;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "OCR service request failed. Status={Status}", ex.Status);
            return Unavailable;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "OCR failed unexpectedly.");
            return Unavailable;
        }
    }

    private static readonly DocumentOcrResult Unavailable = new(DocumentOcrStatus.Unavailable, null, null);

    private DocumentIntelligenceClient? BuildClient()
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointSetting);

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        var azureClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        var isRunningInAzure = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

        if (isRunningInAzure && string.IsNullOrWhiteSpace(azureClientId))
        {
            _logger.LogError(
                "AZURE_CLIENT_ID is missing while running in Azure. Configure it so DefaultAzureCredential uses the attached user-assigned managed identity.");

            throw new InvalidOperationException(
                "AZURE_CLIENT_ID must be set when running in Azure so DefaultAzureCredential uses the attached user-assigned managed identity.");
        }

        var options = new DocumentIntelligenceClientOptions();
        options.Retry.MaxRetries = GetEnvironmentInt("DocumentExtraction__OcrMaxRetries", 0);
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(_timeoutSeconds);

        _logger.LogInformation(
            "OCR client created. EndpointHost={EndpointHost}, HasAzureClientId={HasAzureClientId}",
            new Uri(endpoint).Host,
            !string.IsNullOrWhiteSpace(azureClientId));

        return new DocumentIntelligenceClient(new Uri(endpoint), _credential, options);
    }

    /// <summary>
    /// Logs min and mean word confidence and returns the mean (0..1), which the
    /// extraction edge gates against the account's minimum. Bytes and text never
    /// appear; only the aggregate numbers and the page count. Null when the scan
    /// held no words (the read is Empty, so there is nothing to gate).
    /// </summary>
    private double? LogAndMeanConfidence(AnalyzeResult analysis, int? pageCount)
    {
        var min = 1.0;
        var sum = 0.0;
        var words = 0;

        foreach (var page in analysis.Pages ?? [])
        {
            foreach (var word in page.Words ?? [])
            {
                min = Math.Min(min, word.Confidence);
                sum += word.Confidence;
                words++;
            }
        }

        if (words == 0)
        {
            _logger.LogInformation("OCR read no words. Pages={Pages}", pageCount);
            return null;
        }

        var mean = sum / words;

        _logger.LogInformation(
            "OCR confidence. Words={Words}, Pages={Pages}, MinConfidence={MinConfidence:F3}, MeanConfidence={MeanConfidence:F3}",
            words,
            pageCount,
            min,
            mean);

        return mean;
    }

    private static int GetEnvironmentInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= 0
            ? value
            : fallback;
}
