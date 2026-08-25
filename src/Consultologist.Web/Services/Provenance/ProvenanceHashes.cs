using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Services.Provenance;

/// <summary>One hash on a record, recomputed in the browser from the record's own texts.</summary>
public sealed record HashCheck(string Name, string? Recorded, string? Recomputed, bool Matches, string? Note = null)
{
    /// <summary>#368: a hash whose text the retention policy deleted — attested, not checkable.</summary>
    public bool NotCheckable => Recomputed == null;
}

/// <summary>
/// #402: the workflow-output definitions (hash-definitions.md § 3, § 5) as
/// the browser computes them, so History can recompute a completed job's
/// deliverable hashes from the texts the record carries. A mirror of the
/// engine's ConsultGenerationProvenance, pinned to the same published worked
/// examples by ProvenanceHashMirrorTests — two implementations, one document.
/// </summary>
public static class ProvenanceHashes
{
    private static readonly JsonSerializerOptions Canonical = new() { WriteIndented = false };

    public static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Definitions 1 and 3: SHA-256 of the canonical JSON {id: sha256hex(text)}, ids ordinal-sorted.</summary>
    public static string MerkleHash(IReadOnlyDictionary<string, string> texts)
    {
        var leaves = texts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => Sha256Hex(pair.Value), StringComparer.Ordinal);
        return Sha256Hex(JsonSerializer.Serialize(leaves, Canonical));
    }

    /// <summary>The record's deliverable hashes, recomputed by the definition the record names; empty when it names none the record can support.</summary>
    public static IReadOnlyList<HashCheck> Check(ConsultGenerationJobResponse detail)
    {
        var checks = new List<HashCheck>();

        // #368: once the text is deleted nothing can be recomputed; every hash
        // is reported as not checkable, never as a mismatch.
        if (detail.TextDroppedAtUtc is { } droppedAt)
        {
            var note = $"text deleted on {droppedAt:yyyy-MM-dd} — not checkable";
            if (detail.WorkflowOutputHash != null)
            {
                checks.Add(new HashCheck("workflowOutputHash", detail.WorkflowOutputHash, null, false, note));
            }

            foreach (var document in detail.AssembledDocuments ?? Array.Empty<ConsultGenerationResultDocumentResponse>())
            {
                checks.Add(new HashCheck(document.ResultId, document.DocumentHash, null, false, note));
            }

            return checks;
        }

        if (detail.WorkflowOutputHash != null)
        {
            string? recomputed = detail.WorkflowOutputHashVersion switch
            {
                3 when detail.AssembledDocuments is { Count: > 0 } documents && documents.All(d => d.Text != null) =>
                    MerkleHash(documents.ToDictionary(d => d.ResultId, d => d.Text!, StringComparer.Ordinal)),
                2 when detail.AssembledDocument != null => Sha256Hex(detail.AssembledDocument),
                1 => MerkleHash(detail.GeneratedBlocks),
                _ => null
            };

            if (recomputed != null)
            {
                checks.Add(new HashCheck("workflowOutputHash", detail.WorkflowOutputHash, recomputed,
                    string.Equals(detail.WorkflowOutputHash, recomputed, StringComparison.Ordinal)));
            }
        }

        foreach (var document in (detail.AssembledDocuments ?? Array.Empty<ConsultGenerationResultDocumentResponse>()).Where(d => d.Text != null))
        {
            var recomputed = Sha256Hex(document.Text!);
            checks.Add(new HashCheck(document.ResultId, document.DocumentHash, recomputed,
                string.Equals(document.DocumentHash, recomputed, StringComparison.Ordinal)));
        }

        return checks;
    }
}
