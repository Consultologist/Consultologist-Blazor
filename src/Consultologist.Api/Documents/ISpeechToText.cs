namespace Consultologist.Api.Documents;

/// <summary>
/// #1063 (design § 7.2): transcribes a recording — the audio formats
/// <see cref="AudioDocumentExtractor"/> recognises — into the conversation's
/// turns. The second impure document reader beside <see cref="IDocumentOcr"/>:
/// a cloud call, so it lives behind an interface and is invoked only at the
/// impure edge (<see cref="DocumentExtraction.ExtractAsync"/>), never in the
/// pure <see cref="DocumentExtraction.Extract"/> format core.
///
/// The implementation never throws for a service fault: it catches its own SDK
/// exceptions and timeouts and returns <see cref="SpeechToTextStatus.Unavailable"/>
/// (or <see cref="SpeechToTextStatus.Busy"/> for a rate limit), so the edge maps
/// a transient outage to a "try again" outcome without knowing the SDK's types.
/// </summary>
public interface ISpeechToText
{
    /// <summary>
    /// True when the speech-to-text endpoint app setting is present. When
    /// false a recording is refused as <c>audio-not-enabled</c> — the feature
    /// deploys dark and is enabled per environment, as OCR is.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Transcribes the recording's bytes. Returns the phrases as cues in the
    /// order spoken — the speaker as the service numbered it, or null when
    /// diarisation labelled none — for <see cref="TranscriptTurns.Render"/> to
    /// fold into turns by the one rule a VTT follows; the mean phrase
    /// confidence the edge gates; the audio's duration the edge caps.
    /// </summary>
    Task<SpeechToTextResult> TranscribeAsync(byte[] audio, CancellationToken cancellationToken);
}

public enum SpeechToTextStatus
{
    Transcribed,
    Empty,
    Unavailable,
    Busy,
    TooLong
}

/// <summary>
/// #1063: <paramref name="MeanConfidence"/> is the mean per-phrase confidence
/// (0..1) the service reported, or null when it recognised nothing; the edge
/// compares it to the account's minimum (<c>stt.minConfidence</c>) and refuses
/// as <see cref="DocumentExtractionOutcomes.SttLowConfidence"/> below it.
/// <paramref name="Duration"/> is the audio's length as the service measured
/// it — telemetry, never record.
/// </summary>
public sealed record SpeechToTextResult(
    SpeechToTextStatus Status,
    IReadOnlyList<TranscriptCue>? Cues = null,
    string? ExtractorId = null,
    double? MeanConfidence = null,
    TimeSpan? Duration = null);
