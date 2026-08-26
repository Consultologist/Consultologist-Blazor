using Consultologist.Api.Workflow;

namespace Consultologist.Api.Agents;

/// <summary>
/// #245: what a prompt node writes to stderr about the text it sent and got
/// back — how much, and its digest, never the text. The Functions host
/// forwards stderr to Application Insights line by line, which has no place
/// in the PHI story; the digests are the node's InputHash/OutputHash, so a
/// run can still be tied to its record.
///
/// <see cref="Setting"/> = "true" appends the text. That is for a dev/test
/// deployment fed fictional inputs only — never production — and every
/// start with it on says so on the console.
/// </summary>
public static class PromptDiagnostics
{
    public const string Setting = "Diagnostics__LogPromptText";

    private static readonly Lazy<bool> Enabled = new(() =>
        IsOn(Environment.GetEnvironmentVariable(Setting)));

    public static bool LogPromptText => Enabled.Value;

    /// <summary>Exactly "true" (any case); "1", "yes" and the rest are off.</summary>
    public static bool IsOn(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    public static string Describe(string tag, string stage, string text) =>
        Describe(tag, stage, text, LogPromptText);

    /// <summary>One line: tag, stage, length, SHA-256 — and the text only when told to.</summary>
    public static string Describe(string tag, string stage, string text, bool includeText)
    {
        var line = $"[{tag}] Stage={stage}; Length={text.Length}; Sha256={ConsultGenerationProvenance.Sha256Hex(text)}";
        return includeText ? line + "; Text=" + text : line;
    }

    public static string StartupLine() => StartupLine(LogPromptText);

    public static string StartupLine(bool includeText) => includeText
        ? $"[Diagnostics] Prompt text logging ON ({Setting}=true) — clinical text is being written to telemetry; never set this on production"
        : "[Diagnostics] Prompt text logging OFF — prompt and response lines carry length and SHA-256 only";
}
