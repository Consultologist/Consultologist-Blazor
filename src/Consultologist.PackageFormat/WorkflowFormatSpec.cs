using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Consultologist.PackageFormat;

/// <summary>
/// A format file (package-format-v25-design.md § 3.1): one file saying what a
/// node's output is (<c>type</c>, the input vocabulary), who produces it
/// (<c>producer</c>, § 3.2), and whether macros may be placed on it. The file
/// carries NO id — the id belongs to whoever lists the file: the manifest's
/// <c>formats[]</c> for a package-local format, the catalogue index for a
/// catalogue one. One shape, one validator, for both.
/// </summary>
/// <param name="Type">One of <see cref="WorkflowInputTypes.All"/>: a single type, never a union.</param>
/// <param name="Producer">One of <see cref="WorkflowFormatProducers.All"/>.</param>
/// <param name="OutputMacros">May macros be placed on this output; null reads as false. True only on <c>text</c>.</param>
/// <param name="Description">What the shape is for; shown by the editor's picker.</param>
/// <param name="Schema">Required on every <c>object</c> format — a custom one carries the package's shape, a catalogue one its own.</param>
/// <param name="Values">An <c>enum</c> format's answer set (strings; labels are v26) — or the set comes through a <c>values</c> parameter.</param>
/// <param name="Parameters">What a node supplies through <c>with</c>: on a computed format the operation's operands, on an attested one typed input the engine passes to the agent.</param>
public sealed record WorkflowFormatSpec(
    string Type,
    string Producer,
    bool? OutputMacros = null,
    string? Description = null,
    JsonNode? Schema = null,
    List<string>? Values = null,
    Dictionary<string, WorkflowFormatParameterSpec>? Parameters = null)
{
    /// <summary><c>outputMacros</c> with null read as false.</summary>
    [JsonIgnore]
    public bool AllowsOutputMacros => OutputMacros == true;

    [JsonIgnore]
    public bool HasParameters => Parameters is { Count: > 0 };

    /// <summary>The parameter of kind <c>values</c>, when the format takes its answer set from the node.</summary>
    [JsonIgnore]
    public string? ValuesParameter =>
        Parameters?.FirstOrDefault(pair => string.Equals(pair.Value.Kind, WorkflowFormatParameterKinds.Values, StringComparison.Ordinal)).Key;
}

/// <summary>
/// One declared parameter: its kind, and for a <c>node</c> parameter the
/// catalogue format the named node must have (e.g. terms-subset's <c>of</c>
/// and <c>in</c> name nodes of format concept-list).
/// </summary>
public sealed record WorkflowFormatParameterSpec(
    string Kind,
    string? Format = null);

/// <summary>The producer classes (§ 3.2) — a closed set, like every vocabulary here.</summary>
public static class WorkflowFormatProducers
{
    /// <summary>A purpose-built agent from the catalogue, git-attested.</summary>
    public const string Attested = "attested";

    /// <summary>The package's own shape, served by the catalogue's generic structured agent and validated by the engine (§ 3.4).</summary>
    public const string Custom = "custom";

    /// <summary>No agent: the output is the rendered prompt's bytes (the v12 template kind).</summary>
    public const string Rendered = "rendered";

    /// <summary>An engine operation over typed parameters; no prompt (the v12 check kind).</summary>
    public const string Computed = "computed";

    public static readonly IReadOnlyList<string> All = new[] { Attested, Custom, Rendered, Computed };

    public static bool IsKnown(string? producer) => producer != null && All.Contains(producer, StringComparer.Ordinal);

    /// <summary>Attested and computed formats may declare parameters; custom and rendered take only a prompt.</summary>
    public static bool TakesParameters(string? producer) =>
        producer is Attested or Computed;
}

/// <summary>The parameter kinds a format may declare (§ 3.1).</summary>
public static class WorkflowFormatParameterKinds
{
    /// <summary>A node reference (<c>node.&lt;id&gt;</c>), optionally constrained to a catalogue format.</summary>
    public const string Node = "node";

    /// <summary>A text literal.</summary>
    public const string Text = "text";

    /// <summary>A snake_case answer set — the way a classification receives its values.</summary>
    public const string Values = "values";

    public static readonly IReadOnlyList<string> All = new[] { Node, Text, Values };

    public static bool IsKnown(string? kind) => kind != null && All.Contains(kind, StringComparer.Ordinal);
}

/// <summary>
/// Where a format file is listed from. Never on the wire: the file does not
/// say, the lister does. A package-local file must be <c>custom</c>; every
/// other producer comes from the catalogue (§ 3.2).
/// </summary>
public enum WorkflowFormatOrigin
{
    Catalogue,
    PackageLocal
}

/// <summary>
/// The strict read and the deterministic write of a format file, and the
/// canonical JSON that is a shape's identity in the record (§ 3.3: "the
/// identity of a shape in the record is the bytes of its file, not its name").
/// Refusals are sentences, not throws: the read runs at the publish desk.
/// </summary>
public static class WorkflowFormatSpecJson
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        // Byte-identical between the Function App and a developer's or CI's
        // machine: the default follows the OS.
        NewLine = "\n"
    };

    /// <summary>
    /// Reads one format file. <paramref name="subject"/> names it in every
    /// sentence ("Format 'medication_list'"). False with the reasons added to
    /// <paramref name="errors"/>; true with the spec, which still awaits
    /// <see cref="WorkflowFormatValidator"/>.
    /// </summary>
    public static bool TryRead(string? json, string subject, List<string> errors, out WorkflowFormatSpec? spec)
    {
        spec = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add($"{subject} file is empty.");
            return false;
        }

        FormatDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<FormatDocument>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            // Malformed JSON fails at the root ("$"); an unknown member names
            // its own path below it.
            errors.Add(string.IsNullOrWhiteSpace(ex.Path) || ex.Path == "$"
                ? $"{subject} file is not valid JSON."
                : $"{subject} file property '{ex.Path}' is not part of a format file.");
            return false;
        }

        if (document is null)
        {
            errors.Add($"{subject} file is not valid JSON.");
            return false;
        }

        var complete = true;

        if (string.IsNullOrWhiteSpace(document.Type))
        {
            errors.Add($"{subject} must declare a type (one of {string.Join(", ", WorkflowInputTypes.All)}).");
            complete = false;
        }

        if (string.IsNullOrWhiteSpace(document.Producer))
        {
            errors.Add($"{subject} must declare a producer (one of {string.Join(", ", WorkflowFormatProducers.All)}).");
            complete = false;
        }

        if (!complete)
        {
            return false;
        }

        spec = new WorkflowFormatSpec(
            document.Type!,
            document.Producer!,
            document.OutputMacros,
            document.Description,
            document.Schema,
            document.Values,
            document.Parameters);
        return true;
    }

    /// <summary>Indented camelCase, nulls omitted, trailing newline — what the editor writes for a package-local format.</summary>
    public static string ToJson(WorkflowFormatSpec spec) =>
        JsonSerializer.Serialize(spec, WriteOptions) + "\n";

    /// <summary>
    /// Sorted keys at every level, compact, nothing stripped: the bytes behind
    /// a format's <c>formatHash</c>. Unlike <see cref="WorkflowPackageValidator.CanonicalizeSchema"/>
    /// this keeps title and description — a format file's description is part
    /// of the file. Throws on malformed JSON; callers read first.
    /// </summary>
    public static string CanonicalJson(string json) =>
        Canonical(JsonNode.Parse(json));

    /// <summary>The canonical JSON of a spec: its written form, canonicalized.</summary>
    public static string CanonicalJson(WorkflowFormatSpec spec) =>
        CanonicalJson(ToJson(spec));

    /// <summary>
    /// The SHA-256 of a canonical JSON string, lowercase hex — the
    /// <c>formatHash</c> a stamp and a record carry (§ 3.7). Hash the
    /// canonical form, never the file's bytes: whitespace and key order are
    /// not the shape.
    /// </summary>
    public static string Sha256Hex(string canonicalJson) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalJson)));

    private static string Canonical(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var parts = obj
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{JsonSerializer.Serialize(pair.Key)}:{Canonical(pair.Value)}");
            return "{" + string.Join(",", parts) + "}";
        }

        if (node is JsonArray array)
        {
            return "[" + string.Join(",", array.Select(Canonical)) + "]";
        }

        return node?.ToJsonString() ?? "null";
    }

    private sealed record FormatDocument(
        string? Type,
        string? Producer,
        bool? OutputMacros,
        string? Description,
        JsonNode? Schema,
        List<string>? Values,
        Dictionary<string, WorkflowFormatParameterSpec>? Parameters);
}
