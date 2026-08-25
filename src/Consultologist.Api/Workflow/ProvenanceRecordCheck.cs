using Consultologist.Api.Models;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Workflow;

/// <summary>One hash on a record, recomputed. Matches is null when it could not be recomputed; Note says why.</summary>
public sealed record HashCheck(string Name, int? Definition, string? Recorded, string? Recomputed, bool? Matches, string? Note);

/// <summary>
/// #402: recompute a job record's hashes from the record itself (the
/// deliverable hashes) and from what the holder supplies (the inputs), by the
/// definition numbers the record carries — the engine's own functions, which
/// the published worked examples pin. Nothing here reads storage: a holder of
/// a record and this code can check it anywhere.
/// </summary>
public static class ProvenanceRecordCheck
{
    public static IReadOnlyList<HashCheck> Check(
        ConsultGenerationJobResponse record,
        IReadOnlyDictionary<string, ConsultInputValue>? inputs = null,
        string? draft = null)
    {
        // #368: after the retention policy deleted the text nothing can be
        // recomputed; every hash is attested, not checkable.
        if (record.TextDroppedAtUtc is { } droppedAt)
        {
            var note = $"text deleted on {droppedAt:yyyy-MM-dd} under the retention policy — attested, not checkable";
            var dropped = new List<HashCheck> { new("workflowOutputHash", record.WorkflowOutputHashVersion, record.WorkflowOutputHash, null, null, note) };
            dropped.AddRange((record.AssembledDocuments ?? Array.Empty<ConsultGenerationResultDocumentResponse>())
                .Select(d => new HashCheck($"assembledDocuments[{d.ResultId}].documentHash", null, d.DocumentHash, null, null, note)));
            return dropped;
        }

        var checks = new List<HashCheck> { OutputHash(record) };

        foreach (var document in (record.AssembledDocuments ?? Array.Empty<ConsultGenerationResultDocumentResponse>()).Where(d => d.Text != null))
        {
            var recomputed = ConsultGenerationProvenance.Sha256Hex(document.Text!);
            checks.Add(new HashCheck($"assembledDocuments[{document.ResultId}].documentHash", null, document.DocumentHash, recomputed,
                document.DocumentHash == null ? null : string.Equals(document.DocumentHash, recomputed, StringComparison.Ordinal),
                document.DocumentHash == null ? "the record carries no documentHash for this document" : null));
        }

        if (inputs != null || draft != null)
        {
            checks.Add(InputHash(record, inputs, draft));
        }

        return checks;
    }

    /// <summary>The deliverable hash, by the definition the record names — the same dispatch the engine's response uses.</summary>
    public static HashCheck OutputHash(ConsultGenerationJobResponse record)
    {
        const string name = "workflowOutputHash";
        if (record.WorkflowOutputHash == null)
        {
            return new HashCheck(name, record.WorkflowOutputHashVersion, null, null, null,
                "the record carries no workflowOutputHash — the deliverable hash of a job that is not Completed is undefined");
        }

        string? recomputed;
        switch (record.WorkflowOutputHashVersion)
        {
            case 3 when record.AssembledDocuments is { Count: > 0 } documents && documents.All(d => d.Text != null):
                recomputed = ConsultGenerationProvenance.ComputeResultSetHash(
                    documents.ToDictionary(d => d.ResultId, d => d.Text!, StringComparer.Ordinal));
                break;
            case 2 when record.AssembledDocument != null:
                recomputed = ConsultGenerationProvenance.ComputeAssembledDocumentHash(record.AssembledDocument);
                break;
            case 1:
                recomputed = ConsultGenerationProvenance.ComputeWorkflowOutputHash(record.GeneratedBlocks);
                break;
            default:
                return new HashCheck(name, record.WorkflowOutputHashVersion, record.WorkflowOutputHash, null, null,
                    $"definition {record.WorkflowOutputHashVersion?.ToString() ?? "—"} needs a field this record does not carry");
        }

        return new HashCheck(name, record.WorkflowOutputHashVersion, record.WorkflowOutputHash, recomputed,
            string.Equals(record.WorkflowOutputHash, recomputed, StringComparison.Ordinal), null);
    }

    /// <summary>The effective-input hash, by the definition the record names, over what the holder supplies.</summary>
    public static HashCheck InputHash(ConsultGenerationJobResponse record, IReadOnlyDictionary<string, ConsultInputValue>? inputs, string? draft)
    {
        const string name = "effectiveInputHash";
        var definition = record.EffectiveInputHashVersion ?? 1;
        string? recomputed;

        try
        {
            switch (definition)
            {
                case 5 when inputs != null:
                    recomputed = ConsultGenerationProvenance.ComputeStructuredInputsHash(inputs);
                    break;
                case 4 when inputs != null:
                    recomputed = ConsultGenerationProvenance.ComputeTypedInputsHash(inputs);
                    break;
                case 3 when inputs != null:
                    recomputed = ConsultGenerationProvenance.ComputeDeclaredInputsHash(
                        inputs.ToDictionary(pair => pair.Key, pair => pair.Value.Canonical, StringComparer.Ordinal));
                    break;
                case 2 when draft != null:
                    recomputed = ConsultGenerationProvenance.ComputeDraftOnlyHash(new ConsultGenerationRequest(draft));
                    break;
                case 1:
                    return new HashCheck(name, 1, record.EffectiveInputHash, null, null,
                        "definition 1 is historical (draft and sections, before specVersion 5) and no engine computes it");
                default:
                    return new HashCheck(name, definition, record.EffectiveInputHash, null, null,
                        definition == 2 ? "definition 2 hashes the draft — supply it with --draft" : $"definition {definition} hashes the inputs — supply them with --inputs");
            }
        }
        catch (InvalidOperationException ex)
        {
            // Definition 4 refuses structure and says which definition covers it.
            return new HashCheck(name, definition, record.EffectiveInputHash, null, null, ex.Message);
        }

        return new HashCheck(name, definition, record.EffectiveInputHash, recomputed,
            record.EffectiveInputHash == null ? null : string.Equals(record.EffectiveInputHash, recomputed, StringComparison.Ordinal),
            record.EffectiveInputHash == null ? "the record carries no effectiveInputHash" : null);
    }
}
