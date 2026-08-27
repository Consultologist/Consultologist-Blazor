using System.Text.Json;
using System.Text.Json.Serialization;

namespace Consultologist.Api.Workflow;

/// <summary>
/// The code-by-nature half of the classification output contract (package
/// format v10 § 4, #495): the schema — an object with one required string
/// `value` — lives in the catalog (agents/schemas/classification.json),
/// welded to its attested agent; this class turns the answer into the one
/// value a classifying node declared, or refuses it.
///
/// The answer is model output over the referral and is never printed: a
/// refusal names the node and nothing else.
/// </summary>
public static class ClassificationOutputContract
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// What the engine appends to a classifier's rendered prompt, so the value
    /// set is in front of the model in one deterministic form and is hashed as
    /// part of the message sent (v10 § 4, amended #495). Values in declared
    /// order.
    /// </summary>
    public static string Trailer(IReadOnlyList<string> values) =>
        $"\n\nAnswer with exactly one of: {string.Join(", ", values)}.";

    /// <summary>
    /// The normalised value: parsed from the JSON the agent returned, trimmed,
    /// lower-cased, and one of the declared values — or a
    /// <see cref="ClassificationOutputContractException"/>, retryable under
    /// the agent activity retry policy because a re-ask may land inside the set.
    /// </summary>
    public static string Normalize(string json, IReadOnlyList<string> values, string nodeId)
    {
        ClassificationOutput? output;

        try
        {
            output = JsonSerializer.Deserialize<ClassificationOutput>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // #245: position only — the payload is model output.
            throw new ClassificationOutputContractException(
                $"Classifier '{nodeId}' answered with something that is not classification JSON at {ex.Path ?? "(unknown path)"}"
                + $" (line {ex.LineNumber?.ToString() ?? "?"}, byte {ex.BytePositionInLine?.ToString() ?? "?"}).", ex);
        }

        if (output?.Value is null)
        {
            throw new ClassificationOutputContractException($"Classifier '{nodeId}' answered without a value.");
        }

        var normalised = output.Value.Trim().ToLowerInvariant();

        if (!values.Contains(normalised, StringComparer.Ordinal))
        {
            throw new ClassificationOutputContractException($"Classifier '{nodeId}' answered outside its values.");
        }

        return normalised;
    }

    private sealed record ClassificationOutput([property: JsonPropertyName("value")] string? Value);
}

/// <summary>
/// A classifier's answer that is not one of its values, or not the shape the
/// contract promises. Not InvalidOperationException (which the Durable retry
/// policy excludes): the model may answer inside the set on a retry.
/// </summary>
public sealed class ClassificationOutputContractException : Exception
{
    public ClassificationOutputContractException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
