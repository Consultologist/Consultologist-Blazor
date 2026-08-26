using System.Text.Json;
using System.Text.Json.Serialization;
using Consultologist.Api.Models;

namespace Consultologist.Api.Workflow;

/// <summary>
/// The typed deserializer for the concept-list output contract: materializes
/// ClinicalConcept records from schema-conformant agent output. The schema itself
/// lives in the output-contract catalog (agents/schemas/concept-list.json), welded to
/// the attested structured-output agent; this class is the code-by-nature half —
/// typed consumption of the shape (docs/customizable-workflow/output-contract-catalog.md).
/// </summary>
public static class ConceptOutputContract
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Deserializes schema-conformant agent output into ClinicalConcept records,
    /// stamping the engine-side source. A null id (unmapped finding) maps to an empty
    /// string, preserving the ClinicalConcept invariant the text renderers rely on.
    /// Malformed payloads throw <see cref="ConceptOutputContractException"/> — retryable
    /// under the agent activity retry policy, unlike configuration errors.
    /// </summary>
    public static IReadOnlyList<ClinicalConcept> Deserialize(string json, string source)
    {
        ConceptListOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<ConceptListOutput>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // #245: position only. JsonException.Message quotes the offending
            // token, and this json is model output — clinical text — that would
            // otherwise reach the log, the job's failure text and Durable history.
            throw new ConceptOutputContractException(
                $"Agent output is not valid concept-list JSON at {ex.Path ?? "(unknown path)"}"
                + $" (line {ex.LineNumber?.ToString() ?? "?"}, byte {ex.BytePositionInLine?.ToString() ?? "?"}).", ex);
        }

        if (output?.Concepts == null)
        {
            throw new ConceptOutputContractException("Agent output is missing the required 'concepts' array.");
        }

        return output.Concepts
            .Select(item => new ClinicalConcept(
                item.Term ?? throw new ConceptOutputContractException("A concept is missing the required 'term'."),
                item.Type ?? string.Empty,
                item.Id ?? string.Empty,
                item.IsSnomedConcept,
                item.IsActive,
                source,
                string.IsNullOrWhiteSpace(item.Support) ? null : item.Support))
            .ToList();
    }

    private sealed record ConceptListOutput(
        [property: JsonPropertyName("concepts")] List<ConceptOutputItem>? Concepts);

    private sealed record ConceptOutputItem(
        [property: JsonPropertyName("term")] string? Term,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("isSnomedConcept")] bool IsSnomedConcept,
        [property: JsonPropertyName("isActive")] bool IsActive,
        [property: JsonPropertyName("support")] string? Support);
}

/// <summary>
/// Schema-conformance failure of structured agent output. Deliberately not
/// InvalidOperationException: the Durable retry policy excludes that type (config
/// errors fail fast), while a malformed structured payload is anomalous but worth the
/// retries before failing loud.
/// </summary>
public sealed class ConceptOutputContractException : Exception
{
    public ConceptOutputContractException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
