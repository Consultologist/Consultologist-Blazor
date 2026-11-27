using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.V31;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

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
        // #1509 (Task 6): a storage 12 record's hashes live in its sealed document, not on the header.
        if (record.SchemaVersion >= 12)
        {
            var through = new List<HashCheck>
            {
                new("recordHash", null, record.RecordHash, null, null, "storage 12 is checked through the record: fetch /Record")
            };
            if (inputs != null || draft != null)
            {
                through.Add(InputHash(record, inputs, draft));
            }

            return through;
        }

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

    /// <summary>
    /// #1509 (Task 6; part 08 § 1.2, § 10): a storage 12 record, checked through its sealed document. <c>recordHash</c> is the
    /// SHA-256 of <paramref name="recordBytes"/> against the header's; each deliverable in the document gets its
    /// <c>composedHash</c> against the hash of its retained text (<paramref name="composedTexts"/>, by deliverable key) and a
    /// replay of its substitutions (<see cref="RecordReplay.Verify"/>). A text that is <c>null</c> (dropped, or not given) is
    /// attested, not a failure: its hash is kept.
    /// </summary>
    public static IReadOnlyList<HashCheck> CheckRecord(
        ConsultGenerationJobResponse header, ReadOnlySpan<byte> recordBytes, IReadOnlyDictionary<string, string?> composedTexts)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(composedTexts);
        // a job that has not finished has no record to check; say so, and check nothing
        if (header.RecordHash == null && !ConsultGenerationJobEntity.IsTerminal(header.Status))
        {
            return [new HashCheck("recordHash", null, null, null, null, "the job has not finished: it has no record yet, so nothing was checked")];
        }

        var recomputed = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(recordBytes));
        var checks = new List<HashCheck>
        {
            new("recordHash", null, header.RecordHash, recomputed,
                header.RecordHash != null && string.Equals(header.RecordHash, recomputed, StringComparison.Ordinal),
                header.RecordHash == null ? "a finished job at storage 12 carries no recordHash" : null)
        };

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(recordBytes);
        }
        catch (JsonException)
        {
            document = null;
        }

        if (document is not JsonObject root || root["deliverables"] is not JsonArray deliverables)
        {
            checks.Add(new HashCheck("deliverables", null, null, null, false, "the record is not a canonical document with a deliverables array"));
            return checks;
        }

        foreach (var node in deliverables)
        {
            if (node is not JsonObject row || SourceLayout.Text(row["key"]) is not { } key)
            {
                checks.Add(new HashCheck("deliverables[?]", null, null, null, false, "a deliverable row without a key"));
                continue;
            }

            var composedName = $"deliverables[{key}].composedHash";
            var replayName = $"deliverables[{key}].replay";
            var substitutions = row["substitutions"] as JsonArray;

            // composedHash: the member is always written; null only for a deliverable that was never composed
            var recorded = SourceLayout.Text(row["composedHash"]);
            var state = SourceLayout.Text(row["state"]);
            var text = composedTexts.TryGetValue(key, out var held) ? held : null;
            if (!row.ContainsKey("composedHash") || (recorded == null && state is not ("skipped" or "failed")))
            {
                checks.Add(new HashCheck(composedName, RecordRows.DeliverableHashVersion, null, null, false,
                    row.ContainsKey("composedHash") ? $"the row has no composedHash, and a {state ?? "stateless"} deliverable is composed" : "the row has no composedHash member"));
            }
            else if (recorded == null)
            {
                checks.Add(new HashCheck(composedName, RecordRows.DeliverableHashVersion, null, null, null, $"never composed ({state})"));
            }
            else if (text == null)
            {
                checks.Add(new HashCheck(composedName, RecordRows.DeliverableHashVersion, recorded, null, null, "text deleted; hash kept"));
            }
            else
            {
                var textHash = CanonicalJson.HashText(text);
                checks.Add(new HashCheck(composedName, RecordRows.DeliverableHashVersion, recorded, textHash,
                    string.Equals(recorded, textHash, StringComparison.Ordinal), null));
            }

            // replay: the substitutions member is never absent (part 08 § 10)
            if (substitutions == null)
            {
                checks.Add(new HashCheck(replayName, null, null, null, false, "the row has no substitutions array"));
            }
            else if (text != null)
            {
                var replays = RecordReplay.Verify(text, substitutions, out var fault);
                checks.Add(new HashCheck(replayName, null, null, null, replays, replays ? null : fault));
            }
        }

        return checks;
    }

    /// <summary>
    /// #1509 (final review A1): a deliverable's retained text from a <c>Rows?key=</c> answer. The route offers
    /// <c>text.content</c> only when it hashes to the row's <c>composedHash</c>, so an absent content is a dropped text only
    /// when the answer says <c>textDeleted: true</c>; otherwise the text is missing or altered and <paramref name="failure"/>
    /// fails the deliverable's <c>composedHash</c>. A row never composed (<c>composedHash</c> null) owes no text.
    /// </summary>
    public static string? OfferedRowText(string key, JsonNode? answer, out HashCheck? failure)
    {
        ArgumentNullException.ThrowIfNull(key);
        failure = null;
        var content = answer?["text"] is JsonObject text ? SourceLayout.Text(text["content"]) : null;
        if (content != null)
        {
            return content;
        }

        var deleted = answer?["textDeleted"] is JsonValue flag && flag.TryGetValue<bool>(out var value) && value;
        var composed = answer?["row"] is JsonObject row && SourceLayout.Text(row["composedHash"]) != null;
        if (!deleted && composed)
        {
            failure = new HashCheck($"deliverables[{key}].composedHash", RecordRows.DeliverableHashVersion, null, null, false,
                "the row's text was not offered: missing, or it does not hash to composedHash");
        }

        return null;
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
                // #1509: definition 7, by the function the 31 start stamps EffectiveInputHash with: each input's value.
                case 7 when inputs != null:
                    recomputed = V31JobStarter.EffectiveInputHash(
                        inputs.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToNode(pair.Value), StringComparer.Ordinal));
                    break;
                // #574: the arm #499 forgot — the one definition every v10+
                // job stamps. Definition 6 is 5 recursed; the writer is the
                // same, the number names the wider value set.
                case 6 when inputs != null:
                    recomputed = ConsultGenerationProvenance.ComputeNestedInputsHash(inputs);
                    break;
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
