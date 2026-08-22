using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
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

    /// <summary>
    /// Version 4's domain is v8's scalars — text and a boolean — and it
    /// refuses anything else rather than hashing it. Since #421 the wire
    /// carries numbers and structure, and a structured value reaching this
    /// function can only mean the version gate routed a v9 job here: the one
    /// failure that would otherwise produce a plausible hash stamped 4, wrong
    /// and saying it is right. A throw makes that a failed start instead.
    /// </summary>
    public static string ComputeTypedInputsHash(IReadOnlyDictionary<string, ConsultInputValue> suppliedInputs)
    {
        foreach (var (id, value) in suppliedInputs)
        {
            if (value.Kind is not (ConsultInputKind.Text or ConsultInputKind.Boolean))
            {
                throw new InvalidOperationException(
                    $"Effective-input hash 4 covers text and booleans only; input '{id}' is {value.Described}. A job carrying structure is hashed by definition 5.");
            }
        }

        var canonical = suppliedInputs
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return Sha256Hex(JsonSerializer.Serialize(canonical, CanonicalJsonOptions));
    }

    /// <summary>
    /// The effective-input hash, definition version 5 (v9 jobs): SHA-256 of
    /// the canonical JSON of the supplied inputs as structured values
    /// (package-format-v9-design.md § 8). Canonical means: top-level slot ids
    /// and object field ids ordinal-sorted at every level — UTF-16 code-unit
    /// order, which is also RFC 8785's; array elements in supplied order,
    /// which is significant and is the caller's; a number as the digits the
    /// caller sent; absent optionals omitted; text normalised per element
    /// before it arrives here; no insignificant whitespace; and **UTF-8
    /// written as-is**, with only what JSON requires escaped.
    ///
    /// That last rule is where this definition parts from 2–4, which use
    /// System.Text.Json's default encoder and escape non-ASCII and a handful
    /// of ASCII (&lt;, &gt;, &amp;, ', +) as \uXXXX. So 4 and 5 agree byte for
    /// byte on an ASCII map of scalars and on nothing wider — which is fine,
    /// because they are never compared, per provenance.md. Written by its own
    /// writer rather than the wire converter, so the replay form (supplied
    /// order) and the hashed form (sorted) cannot drift each other.
    /// </summary>
    public const int StructuredInputsHashVersion = 5;

    private static readonly JsonWriterOptions CanonicalWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false
    };

    public static string ComputeStructuredInputsHash(IReadOnlyDictionary<string, ConsultInputValue> suppliedInputs)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, CanonicalWriterOptions))
        {
            writer.WriteStartObject();

            foreach (var (id, value) in suppliedInputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(id);
                WriteCanonical(writer, value);
            }

            writer.WriteEndObject();
        }

        return Sha256Hex(buffer.WrittenSpan);
    }

    /// <summary>
    /// One value in definition 5's canonical form. Total over every kind the
    /// wire carries: a null element never reaches a hash — the starter
    /// refuses it first — but the writer does not assume so.
    /// </summary>
    internal static void WriteCanonical(Utf8JsonWriter writer, ConsultInputValue value)
    {
        switch (value.Kind)
        {
            case ConsultInputKind.Boolean:
                writer.WriteBooleanValue(value.Flag!.Value);
                break;

            case ConsultInputKind.Number:
                writer.WriteRawValue(value.Number!);
                break;

            case ConsultInputKind.Object:
                writer.WriteStartObject();
                foreach (var field in value.Fields!.OrderBy(field => field.Id, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(field.Id);
                    WriteCanonical(writer, field.Value);
                }

                writer.WriteEndObject();
                break;

            case ConsultInputKind.Array:
                writer.WriteStartArray();
                foreach (var element in value.Elements!)
                {
                    WriteCanonical(writer, element);
                }

                writer.WriteEndArray();
                break;

            case ConsultInputKind.Null:
                writer.WriteNullValue();
                break;

            default:
                writer.WriteStringValue(value.Text ?? string.Empty);
                break;
        }
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

    /// <summary>The same digest over bytes already in UTF-8 — what a writer produces.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> utf8)
    {
        return Convert.ToHexStringLower(SHA256.HashData(utf8));
    }
}
