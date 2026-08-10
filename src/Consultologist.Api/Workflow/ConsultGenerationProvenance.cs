using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Consultologist.Api.Models;

namespace Consultologist.Api.Workflow;

internal static class ConsultGenerationProvenance
{
    // Canonical serialization: fixed property order and naming, no whitespace. The hash
    // must be stable across runtimes so provenance records stay comparable over time.
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// The effective-input hash, definition version 2: the draft only — sections are
    /// package data, covered by the workflowPackage ref. Jobs record
    /// EffectiveInputHashVersion = 2; the retired version-1 definition (draft +
    /// sections, pre-v5 jobs) is historical (package-format-v5.md).
    /// </summary>
    public static string ComputeDraftOnlyHash(ConsultGenerationRequest request)
    {
        return Sha256Hex(JsonSerializer.Serialize(
            new { consultDraft = request.ConsultDraft },
            CanonicalJsonOptions));
    }

    /// <summary>
    /// The effective-input hash, definition version 3 (v7 jobs): SHA-256 of the
    /// canonical JSON of the supplied inputs as an ordinal-sorted {id: text} map.
    /// Hashes the SUPPLIED map — absent optional inputs are omitted, never
    /// empty-string-filled — so a job that leaves an optional slot blank and one
    /// that never had the slot hash identically only when the package agrees
    /// (package-format-v7.md). v2 (draft only) stays the definition for v5/v6.
    /// </summary>
    public const int DeclaredInputsHashVersion = 3;

    /// <summary>
    /// The effective-input hash, definition version 4 (v8 jobs): SHA-256 of
    /// the canonical JSON of the supplied inputs **as typed values** — a
    /// boolean serialises as <c>true</c>, not <c>"true"</c>
    /// (package-format-v8-design.md § 6).
    ///
    /// A genuinely different function from 3, not the same bytes under a new
    /// name: `{"billable": true}` and `{"billable": "true"}` hash differently,
    /// which is exactly the property that makes the version move necessary
    /// rather than ceremonial. v7 keeps hashing canonical strings; the two are
    /// never compared, per provenance.md.
    /// </summary>
    public const int TypedInputsHashVersion = 4;

    public static string ComputeTypedInputsHash(IReadOnlyDictionary<string, ConsultInputValue> suppliedInputs)
    {
        var canonical = suppliedInputs
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return Sha256Hex(JsonSerializer.Serialize(canonical, CanonicalJsonOptions));
    }

    public static string ComputeDeclaredInputsHash(IReadOnlyDictionary<string, string> suppliedInputs)
    {
        var canonical = suppliedInputs
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return Sha256Hex(JsonSerializer.Serialize(canonical, CanonicalJsonOptions));
    }

    /// <summary>
    /// The workflow-output hash, definition version 1: SHA-256 of the canonical JSON
    /// {sectionId: Sha256Hex(sectionText)} with ordinal-sorted keys — a Merkle-style
    /// root over the deliverable. Derived at response time from GeneratedSections
    /// (never stored): anyone holding the record can recompute it, and two completed
    /// runs produced the byte-identical note iff their hashes match (#88;
    /// docs/customizable-workflow/provenance.md).
    /// </summary>
    public const int WorkflowOutputHashVersion = 1;

    public static string ComputeWorkflowOutputHash(IReadOnlyDictionary<string, string> generatedSections)
    {
        var canonical = generatedSections
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => Sha256Hex(pair.Value));

        return Sha256Hex(JsonSerializer.Serialize(canonical, CanonicalJsonOptions));
    }

    /// <summary>
    /// The workflow-output hash, definition version 2 (v6 jobs): SHA-256 of the
    /// assembled document's UTF-8 bytes — the deliverable is one document, so its
    /// digest is the whole story. Derived at response time from the stored
    /// document (package-format-v6-design.md § 4); v1 remains the definition for
    /// v5 jobs' per-section deliverable.
    /// </summary>
    public const int AssembledDocumentHashVersion = 2;

    public static string ComputeAssembledDocumentHash(string assembledDocument)
        => Sha256Hex(assembledDocument);

    /// <summary>
    /// The workflow-output hash, definition version 3 (v7 jobs): SHA-256 of the
    /// canonical JSON {resultId: Sha256Hex(documentText)} with ordinal-sorted
    /// keys — the v1 Merkle recipe generalized from section ids to the result
    /// set. Derived at response time from the stored per-result documents; v2
    /// remains the definition for v6's single document (package-format-v7.md).
    /// </summary>
    public const int ResultSetHashVersion = 3;

    public static string ComputeResultSetHash(IReadOnlyDictionary<string, string> documents)
        => ComputeWorkflowOutputHash(documents);

    /// <summary>
    /// The aggregator's input hash: canonical JSON array of the source instance
    /// output hashes, in aggregation order — the composition is a pure function
    /// of exactly these outputs (package-format-v6-design.md § 3).
    /// </summary>
    public static string ComputeAggregateInputHash(IReadOnlyList<string> sourceOutputHashes)
        => Sha256Hex(JsonSerializer.Serialize(sourceOutputHashes, CanonicalJsonOptions));

    /// <summary>
    /// Lowercase-hex SHA-256 of the UTF-8 text — the per-node provenance hash: a node's
    /// InputHash covers the exact rendered prompt the agent receives (template +
    /// prelude + variables), its OutputHash the raw assistant text, so two runs can be
    /// compared node by node (dag-improvements #6).
    /// </summary>
    public static string Sha256Hex(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
