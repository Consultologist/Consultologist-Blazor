using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.Speech.Transcription;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Documents;

/// <summary>
/// #1063 (design § 7.2): speech-to-text for recordings via Azure AI Speech's
/// fast transcription (Canada Central; diarisation on). The impure reader a
/// recording routes to when the pure parser returns <c>audio-needs-transcription</c>
/// and the service is configured (<see cref="DocumentExtraction.ExtractAsync"/>).
///
/// Auth mirrors <see cref="AzureDocumentIntelligenceOcr"/>: identity-only, the
/// user-assigned managed identity via the injected <see cref="TokenCredential"/>
/// and AZURE_CLIENT_ID — never keys. Entra ID auth requires the Speech
/// resource's custom-domain endpoint (<c>https://&lt;name&gt;.cognitiveservices.azure.com</c>),
/// so the endpoint app setting must be that form.
///
/// The bytes are sent and never kept; what comes back is folded into turns by
/// the one rule (<see cref="TranscriptTurns.Render"/>) so a recording hashes as
/// its transcript file would. Duration and confidence are logged, never
/// recorded.
/// </summary>
internal sealed class AzureSpeechToText : ISpeechToText
{
    internal const string EndpointSetting = "SpeechToText__Endpoint";
    internal const string LocalesSetting = "SpeechToText__Locales";
    internal const string DefaultLocales = "en-CA,fr-CA";

    // Provenance: pinned by the SDK assembly version, computed once — the same
    // shape as docintel/… (docs/DOCUMENT_INPUT.md § 7).
    private static readonly string ExtractorId =
        ExtractorIdentity.For("speech", typeof(TranscriptionClient).Assembly);

    private readonly ILogger<AzureSpeechToText> _logger;
    private readonly TokenCredential _credential;
    private readonly Lazy<TranscriptionClient?> _client;
    private readonly int _timeoutSeconds;
    private readonly int _maxSpeakers;
    private readonly TimeSpan _maxDuration;
    private readonly IReadOnlyList<string> _locales;

    public AzureSpeechToText(ILogger<AzureSpeechToText> logger, TokenCredential credential)
    {
        _logger = logger;
        _credential = credential;
        _timeoutSeconds = GetEnvironmentInt("SpeechToText__TimeoutSeconds", 120);
        _maxSpeakers = Math.Max(2, GetEnvironmentInt("SpeechToText__MaxSpeakers", 4));
        _maxDuration = TimeSpan.FromMinutes(Math.Max(1, GetEnvironmentInt("SpeechToText__MaxDurationMinutes", 60)));
        _locales = ParseLocales(Environment.GetEnvironmentVariable(LocalesSetting));
        _client = new Lazy<TranscriptionClient?>(BuildClient);
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EndpointSetting));

    /// <summary>The locale candidates the service identifies among; blank entries dropped, the default when unset.</summary>
    internal static IReadOnlyList<string> ParseLocales(string? configured)
    {
        var raw = string.IsNullOrWhiteSpace(configured) ? DefaultLocales : configured;
        return raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SpeechToTextResult> TranscribeAsync(byte[] audio, CancellationToken cancellationToken)
    {
        TranscriptionClient? client;
        try
        {
            client = _client.Value;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Speech-to-text client could not be created.");
            return Unavailable;
        }

        if (client is null)
        {
            return Unavailable;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            using var stream = new MemoryStream(audio, writable: false);
            var options = new TranscriptionOptions(stream)
            {
                DiarizationOptions = new TranscriptionDiarizationOptions { MaxSpeakers = _maxSpeakers },
            };
            foreach (var locale in _locales)
            {
                options.Locales.Add(locale);
            }

            var response = await client.TranscribeAsync(options, timeout.Token);
            var result = response.Value;

            return Fold(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Speech-to-text timed out after {TimeoutSeconds}s.", _timeoutSeconds);
            return Unavailable;
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            // A cold or saturated service answers 429 first; that is "try
            // again", the same shape as the parse gate's busy.
            _logger.LogWarning("Speech-to-text rate-limited. Status={Status}", ex.Status);
            return new SpeechToTextResult(SpeechToTextStatus.Busy);
        }
        catch (ClientResultException ex)
        {
            _logger.LogWarning(ex, "Speech-to-text request failed. Status={Status}", ex.Status);
            return Unavailable;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Speech-to-text failed unexpectedly.");
            return Unavailable;
        }
    }

    /// <summary>
    /// The service's phrases to the seam's cues: one cue per phrase in the
    /// order returned, the speaker as the service numbered it. Numbers and the
    /// duration are logged; no text ever is.
    /// </summary>
    private SpeechToTextResult Fold(TranscriptionResult result)
    {
        var duration = result.Duration;
        if (duration > _maxDuration)
        {
            _logger.LogInformation("Speech-to-text refused a recording over the cap. DurationSeconds={Duration:F0}, CapMinutes={Cap}", duration.TotalSeconds, _maxDuration.TotalMinutes);
            return new SpeechToTextResult(SpeechToTextStatus.TooLong, Duration: duration);
        }

        var cues = new List<TranscriptCue>();
        var sum = 0.0;
        var counted = 0;
        foreach (var phrase in result.Phrases ?? [])
        {
            if (string.IsNullOrWhiteSpace(phrase.Text))
            {
                continue;
            }

            cues.Add(new TranscriptCue(phrase.Speaker is int speaker ? $"Speaker {speaker}" : null, phrase.Text.Trim()));
            sum += phrase.Confidence;
            counted++;
        }

        if (cues.Count == 0)
        {
            _logger.LogInformation("Speech-to-text recognised nothing. DurationSeconds={Duration:F0}", duration.TotalSeconds);
            return new SpeechToTextResult(SpeechToTextStatus.Empty, Duration: duration);
        }

        var mean = sum / counted;
        _logger.LogInformation(
            "Speech-to-text transcribed. Phrases={Phrases}, Speakers={Speakers}, DurationSeconds={Duration:F0}, MeanConfidence={MeanConfidence:F3}",
            cues.Count,
            cues.Select(cue => cue.Speaker).Where(speaker => speaker is not null).Distinct().Count(),
            duration.TotalSeconds,
            mean);

        return new SpeechToTextResult(SpeechToTextStatus.Transcribed, cues, ExtractorId, mean, duration);
    }

    private static readonly SpeechToTextResult Unavailable = new(SpeechToTextStatus.Unavailable);

    private TranscriptionClient? BuildClient()
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
            _logger.LogError("AZURE_CLIENT_ID is missing while running in Azure. Configure it so DefaultAzureCredential uses the attached user-assigned managed identity.");
            throw new InvalidOperationException(
                "AZURE_CLIENT_ID must be set when running in Azure so DefaultAzureCredential uses the attached user-assigned managed identity.");
        }

        var options = new TranscriptionClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(_timeoutSeconds),
            RetryPolicy = new ClientRetryPolicy(GetEnvironmentInt("SpeechToText__MaxRetries", 1)),
        };

        _logger.LogInformation(
            "Speech-to-text client created. EndpointHost={EndpointHost}, Locales={Locales}, HasAzureClientId={HasAzureClientId}",
            new Uri(endpoint).Host,
            string.Join(',', _locales),
            !string.IsNullOrWhiteSpace(azureClientId));

        return new TranscriptionClient(new Uri(endpoint), _credential, options);
    }

    private static int GetEnvironmentInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= 0
            ? value
            : fallback;
}
