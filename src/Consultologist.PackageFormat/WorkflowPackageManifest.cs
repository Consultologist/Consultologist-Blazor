using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

public sealed record WorkflowPackageManifest(
    string Name,
    string Version,
    int SpecVersion,
    WorkflowTemplatingSpec? Templating = null,
    Dictionary<string, string>? Preludes = null,
    List<WorkflowPromptSpec>? Prompts = null,
    Dictionary<string, string>? Schemas = null,
    List<WorkflowNodeSpec>? Nodes = null,
    string? DerivedFrom = null,
    Dictionary<string, string>? Data = null,
    string? Result = null,
    List<WorkflowInputSpec>? Inputs = null,
    List<WorkflowResultSpec>? Results = null,
    // v9 (package-format-v9-design.md § 4, #432): what the package is called
    // and what it is for. Authored content, the safety class of a label.
    // Typed rather than tolerated so the publisher's re-serialisation keeps
    // them (#398); trailing optionals omitted when null, so every earlier
    // manifest writes the bytes it always wrote. The version gate is the
    // validator's, as inputs' and fields' are.
    string? Title = null,
    string? Description = null,
    // v9 § 4 (#453): the labels a package is found by. REQUIRED at 9 — an
    // empty array when the package has none, so a reader never wonders
    // whether absence was a choice; null is not a spelling of "none" on a v9
    // manifest, only the value a pre-v9 manifest carries. Authored content,
    // the safety class of a label; order as authored, never sorted.
    List<string>? Tags = null,
    // v11 (package-format-v11-design.md § 4, #513): named texts the package
    // owns — template files with placeholders from closed namespaces,
    // appended verbatim to the deliverables that name them. Trailing
    // optional, omitted when null, so every earlier manifest writes the
    // bytes it always wrote. The version gate is the validator's.
    List<WorkflowMacroSpec>? Macros = null);

/// <summary>
/// One declared macro (v11 § 4): a package-owned template file, applied to a
/// deliverable by substitution and appended verbatim — never through a model.
/// v12 (§ 3): an optional macro is a per-run choice and MUST declare its
/// default — the package decides what a formless run does. bool? on purpose:
/// a plain bool would write false onto every v11 manifest. Below 12 both are
/// refused by name by the validator.
/// </summary>
public sealed record WorkflowMacroSpec(
    string Id,
    string Label,
    string File,
    bool? Optional = null,
    bool? Default = null);

/// <summary>
/// One declared input slot of a specVersion-7 package: the id nodes bind as
/// "input:&lt;id&gt;" and callers supply per job
/// (external/consultologist-package-format/package-format-v7.md § 2).
///
/// v8 types the slot. <c>Type</c> is optional and absent means "text", so a
/// v7 declaration is a valid v8 one and the minimal migration is the
/// specVersion line alone (package-format-v8-design.md § 4). <c>Values</c>
/// belongs to <c>enum</c> and to nothing else.
///
/// Both are trailing optionals: a v5–v7 manifest deserialises unchanged.
/// Nothing here travels into the orchestrator — a typed value renders as its
/// canonical string, so the resolver and every durable payload are untouched.
/// </summary>
public sealed record WorkflowInputSpec(
    string Id,
    string Label,
    bool Required = true,
    string? Type = null,
    List<string>? Values = null,
    // v9 (package-format-v9-design.md § 4): the element type of an array —
    // required for `array`, forbidden otherwise — and the fields of an object,
    // whether the input IS an object or is an array OF objects. Trailing
    // optionals, omitted when null, so a v7/v8 manifest writes the bytes it
    // always wrote. v10 (package-format-v10-design.md § 7): `items` may be a
    // whole element spec rather than a type name — see WorkflowElementSpec,
    // which still writes the v9 string when it is only a name.
    WorkflowElementSpec? Items = null,
    List<WorkflowFieldSpec>? Fields = null);

/// <summary>
/// One field of a declared object (v9 § 4), in an input's vocabulary — id,
/// label, required, type, values — and, since v10 (§ 7), items and fields
/// of its own, so structure recurses. The one-level bound v9 held by
/// construction is now the validator's, keyed by version: a field carrying
/// items or fields on a manifest below 10 is refused by name.
/// </summary>
public sealed record WorkflowFieldSpec(
    string Id,
    string Label,
    bool Required = true,
    string? Type = null,
    List<string>? Values = null,
    // v10: trailing optionals, omitted when null — a v9 field writes the
    // bytes it always wrote.
    WorkflowElementSpec? Items = null,
    List<WorkflowFieldSpec>? Fields = null);

/// <summary>
/// What an array holds (v10 § 7). On the wire either the v9 type name —
/// <c>items: text</c> — or an element spec, <c>items: { type: array, items:
/// text }</c>, recursively. A bare spec (a type and nothing else) writes the
/// string form, so every v9 manifest round-trips byte for byte; the object
/// form below specVersion 10 is refused by the validator, never by the
/// reader.
/// </summary>
[JsonConverter(typeof(WorkflowElementSpecConverter))]
public sealed record WorkflowElementSpec(
    string Type,
    WorkflowElementSpec? Items = null,
    List<WorkflowFieldSpec>? Fields = null,
    List<string>? Values = null)
{
    /// <summary>A type name and nothing else — the v9 form.</summary>
    public bool IsBare => Items is null && Fields is null && Values is null;

    public static implicit operator WorkflowElementSpec?(string? type) => type is null ? null : new(type);

    public override string ToString() => Type;
}

public sealed class WorkflowElementSpecConverter : JsonConverter<WorkflowElementSpec>
{
    // The object form, read without this converter on the outer shape (the
    // nested `items` still goes through it, which is the recursion).
    private sealed record Shape(string? Type, WorkflowElementSpec? Items, List<WorkflowFieldSpec>? Fields, List<string>? Values);

    public override WorkflowElementSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new WorkflowElementSpec(reader.GetString()!);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("items must be a type name or an element spec object.");
        }

        var shape = JsonSerializer.Deserialize<Shape>(ref reader, options)
            ?? throw new JsonException("items must be a type name or an element spec object.");

        if (string.IsNullOrWhiteSpace(shape.Type))
        {
            throw new JsonException("An items spec must declare type.");
        }

        return new WorkflowElementSpec(shape.Type, shape.Items, shape.Fields, shape.Values);
    }

    public override void Write(Utf8JsonWriter writer, WorkflowElementSpec value, JsonSerializerOptions options)
    {
        if (value.IsBare)
        {
            writer.WriteStringValue(value.Type);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", value.Type);

        if (value.Items != null)
        {
            writer.WritePropertyName("items");
            Write(writer, value.Items, options);
        }

        if (value.Fields != null)
        {
            writer.WritePropertyName("fields");
            JsonSerializer.Serialize(writer, value.Fields, options);
        }

        if (value.Values != null)
        {
            writer.WritePropertyName("values");
            JsonSerializer.Serialize(writer, value.Values, options);
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// The declared input types (package-format-v8-design.md § 4). Typed values
/// travel as text; the type decides which text is accepted, checked at job
/// start against the canonical form and rejected — never normalised — when
/// it does not match.
/// </summary>
public static class WorkflowInputTypes
{
    public const string Text = "text";
    public const string Date = "date";
    public const string Enum = "enum";
    public const string Boolean = "boolean";

    // v9 (package-format-v9-design.md § 4).
    public const string Number = "number";
    public const string Object = "object";
    public const string Array = "array";

    /// <summary>Every type the format has, as of the newest version.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Text, Date, Enum, Boolean, Number, Object, Array };

    /// <summary>
    /// The types a field or an array element may be. Structure is one level
    /// deep: an object's field and an array's element are scalars, and an
    /// array's element may also be an object.
    /// </summary>
    public static readonly IReadOnlyList<string> Scalars = new[] { Text, Date, Enum, Boolean, Number };

    /// <summary>What an array may hold: a scalar, or a one-level object.</summary>
    public static readonly IReadOnlyList<string> ElementTypes = new[] { Text, Date, Enum, Boolean, Number, Object };

    private static readonly IReadOnlyList<string> V8Types = new[] { Text, Date, Enum, Boolean };

    /// <summary>
    /// The type set one specVersion admits. Keyed by version rather than a
    /// single list because an error message lists the accepted names, and a
    /// v8 manifest's refusal must keep reading exactly as the published
    /// conformance suite recorded it.
    /// </summary>
    public static IReadOnlyList<string> ForSpecVersion(int specVersion) => specVersion >= 9 ? All : V8Types;

    /// <summary>An absent type is text — the default that keeps v7 declarations valid.</summary>
    public static string Of(WorkflowInputSpec input) => input.Type ?? Text;

    /// <summary>The type an array's elements have; text when items is absent.</summary>
    public static string ElementTypeOf(WorkflowInputSpec input) => input.Items?.Type ?? Text;

    public static string ElementTypeOf(WorkflowFieldSpec field) => field.Items?.Type ?? Text;

    /// <summary>
    /// v10 (§ 7): the types a field or an element may have, keyed by version
    /// — the v9 constants Scalars and ElementTypes stay what the v9 sentences
    /// and the client's mirrors pin; at 10 a field may be anything and an
    /// element may be an array.
    /// </summary>
    public static IReadOnlyList<string> ScalarsFor(int specVersion) => specVersion >= 10 ? All : Scalars;

    public static IReadOnlyList<string> ElementTypesFor(int specVersion) => specVersion >= 10 ? All : ElementTypes;

    /// <summary>The same default for a field.</summary>
    public static string Of(WorkflowFieldSpec field) => field.Type ?? Text;

    /// <summary>Whether the declaration has fields: an object, or an array of objects.</summary>
    public static bool DeclaresObject(WorkflowInputSpec input) =>
        Of(input) == Object || (Of(input) == Array && input.Items?.Type == Object);

    /// <summary>v10: whether a field has fields — an object, or an array of objects.</summary>
    public static bool DeclaresObject(WorkflowFieldSpec field) =>
        Of(field) == Object || (Of(field) == Array && field.Items?.Type == Object);
}

/// <summary>
/// One declared deliverable of a specVersion-7 package: an authored id and
/// label naming an aggregator node. The string "result" form remains valid as
/// one-entry sugar (external/consultologist-package-format/package-format-v7.md § 3).
/// </summary>
public sealed record WorkflowResultSpec(
    string Id,
    string Node,
    string Label,
    // v8: produced only when this holds (package-format-v8-design.md § 5).
    // Trailing optional — a v7 results block stays valid, and a deliverable
    // without one always fires.
    string? When = null,
    // v11 (§ 4/§ 5, #513/#516): the macros appended after this deliverable's
    // aggregated sections, in this order, and whether the profile's signature
    // is appended last. Trailing optionals — bool? on purpose: a plain bool
    // would write "signature": false onto every earlier manifest. Below 11
    // both are refused by name by the validator. v12 (§ 4): each entry is a
    // bare id (the v11 form, byte-for-byte) or a placed object — see
    // WorkflowResultMacroSpec.
    List<WorkflowResultMacroSpec>? Macros = null,
    bool? Signature = null,
    // v12 (§ 13): the check node gating this deliverable — the
    // post-production mirror of when. Below 12 refused by name.
    string? Check = null);

/// <summary>
/// One entry in a deliverable's macro list. On the wire either the v11 bare
/// id — <c>macros: [closing]</c> — or a placed object, <c>{ id: disclaimer,
/// before: node:findings }</c> (v12 § 4). A bare entry writes the string
/// form, so every v11 manifest round-trips byte for byte; the object form
/// below specVersion 12 is refused by the validator, never by the reader.
/// </summary>
[JsonConverter(typeof(WorkflowResultMacroSpecConverter))]
public sealed record WorkflowResultMacroSpec(
    string Id,
    string? Before = null,
    string? After = null,
    // v12 (§ 14): the entry's data gate — the result-level condition grammar,
    // written on the macro. Trailing so the placed pair keeps its positions.
    string? When = null)
{
    /// <summary>An id and nothing else — the v11 form. When must count:
    /// the writer keys the bare-string form on this, and a when-only entry
    /// serialized bare would silently drop its clause on republish.</summary>
    public bool IsBare => Before is null && After is null && When is null;

    public static implicit operator WorkflowResultMacroSpec?(string? id) => id is null ? null : new(id);

    public override string ToString() => Id;
}

public sealed class WorkflowResultMacroSpecConverter : JsonConverter<WorkflowResultMacroSpec>
{
    // The object form, read without this converter on the outer shape.
    private sealed record Shape(string? Id, string? Before, string? After, string? When);

    public override WorkflowResultMacroSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new WorkflowResultMacroSpec(reader.GetString()!);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A result macro entry must be a macro id or a placement object.");
        }

        var shape = JsonSerializer.Deserialize<Shape>(ref reader, options)
            ?? throw new JsonException("A result macro entry must be a macro id or a placement object.");

        if (string.IsNullOrWhiteSpace(shape.Id))
        {
            throw new JsonException("A placed macro entry must declare id.");
        }

        return new WorkflowResultMacroSpec(shape.Id, shape.Before, shape.After, shape.When);
    }

    public override void Write(Utf8JsonWriter writer, WorkflowResultMacroSpec value, JsonSerializerOptions options)
    {
        if (value.IsBare)
        {
            writer.WriteStringValue(value.Id);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("id", value.Id);

        if (value.Before != null)
        {
            writer.WriteString("before", value.Before);
        }

        if (value.After != null)
        {
            writer.WriteString("after", value.After);
        }

        if (value.When != null)
        {
            writer.WriteString("when", value.When);
        }

        writer.WriteEndObject();
    }
}

public sealed record WorkflowTemplatingSpec(
    string Engine,
    string EngineVersion);

public sealed record WorkflowPromptSpec(
    string Id,
    string File,
    List<string> Variables,
    string? Prelude = null);

/// <summary>
/// One node of the workflow DAG: one kind, with ForEach as the multiplicity
/// property. Edges are implicit in the bindings' node: references
/// (external/consultologist-package-format/package-format-v5.md).
/// </summary>
public sealed record WorkflowNodeSpec(
    string Id,
    string Label,
    string? Prompt = null,
    Dictionary<string, WorkflowBindingValue>? Bindings = null,
    WorkflowNodeOutputSpec? Output = null,
    string? ForEach = null,
    List<string>? Aggregate = null,
    // v10 (package-format-v10-design.md § 4): a classifying node — kind
    // "classifier" with the values it may answer. Trailing optionals, omitted
    // when null; below 10 both are refused by name by the validator.
    string? Kind = null,
    List<string>? Values = null,
    // v11 (§ 6, #550): the package's claim that this node's output is the
    // same for the same input — carried for the rerun verdict, never
    // enforced at run time. bool? on purpose (see WorkflowResultSpec); below
    // 11 refused by name by the validator.
    bool? Reproducible = null,
    // v12 (§ 13): the check node's declaration — kind "check" with the
    // operation, its two concept-list operands (node:<id> refs) and the
    // sentence a failed check speaks. Trailing optionals, omitted when
    // null; below 12 each is refused by name by the validator.
    string? Op = null,
    string? Of = null,
    string? In = null,
    string? FailWith = null);

/// <summary>
/// The node kinds a manifest may spell (v10 § 4; check since v12 § 13).
/// Absent is a prompt node — or an aggregator when aggregate is present,
/// which is a property, not a kind, and may not be spelled as one.
/// </summary>
public static class WorkflowNodeKinds
{
    public const string Prompt = "prompt";
    public const string Classifier = "classifier";
    // v12 (§ 13): the deterministic gate on a deliverable — the second
    // non-model executor after the aggregator. Below 12 the validator's
    // gate refuses it by name before the unknown-kind sentence can fire.
    public const string Check = "check";

    public static readonly IReadOnlyList<string> All = new[] { Prompt, Classifier, Check };

    private static readonly IReadOnlyList<string> AllV10 = new[] { Prompt, Classifier };

    /// <summary>The kinds the given format version may spell — the unknown-kind sentence names these, so it stays true per version.</summary>
    public static IReadOnlyList<string> AllFor(int specVersion) =>
        specVersion >= 12 ? All : AllV10;

    public static bool IsClassifier(WorkflowNodeSpec node) =>
        string.Equals(node.Kind, Classifier, StringComparison.Ordinal);

    public static bool IsCheck(WorkflowNodeSpec node) =>
        string.Equals(node.Kind, Check, StringComparison.Ordinal);
}

/// <summary>The check operations a manifest may spell (v12 § 13) — a closed set, like every vocabulary here.</summary>
public static class WorkflowCheckOps
{
    public const string TermsSubset = "terms-subset";

    public static readonly IReadOnlyList<string> All = new[] { TermsSubset };
}

public sealed record WorkflowNodeOutputSpec(
    string Schema,
    string? FailIfEmpty = null);

/// <summary>
/// One item of a resolved data collection: the declared fields materialized —
/// per-item file content becomes the "content" field
/// (external/consultologist-package-format/package-format-v5.md).
/// </summary>
public sealed record WorkflowDataItem(
    string Id,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>A resolved data collection: the declared item shape plus the items, in index order.</summary>
public sealed record WorkflowDataCollection(
    IReadOnlyList<string> Fields,
    IReadOnlyList<WorkflowDataItem> Items);

/// <summary>The resolved data table of a specVersion-5 package.</summary>
public sealed record WorkflowPackageData(
    IReadOnlyDictionary<string, string> Scalars,
    IReadOnlyDictionary<string, WorkflowDataCollection> Collections);

/// <summary>The parsed shape of a collection's index.json.</summary>
public sealed record WorkflowDataIndexFile(
    List<string>? Fields,
    List<WorkflowDataIndexItem>? Items);

public sealed record WorkflowDataIndexItem(
    string? Id,
    string? Name,
    string? File);

/// <summary>
/// A binding value in a manifest: either a plain source string
/// ("input:consult_draft") or an object selecting a renderer
/// ({ "from": "node:x", "as": "concept-context" }).
/// </summary>
[JsonConverter(typeof(WorkflowBindingValueConverter))]
public sealed record WorkflowBindingValue(string From, string? As = null);

public sealed class WorkflowBindingValueConverter : JsonConverter<WorkflowBindingValue>
{
    public override WorkflowBindingValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new WorkflowBindingValue(reader.GetString()!);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A binding value must be a source string or a { from, as } object.");
        }

        string? from = null;
        string? renderAs = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();

            switch (property?.ToLowerInvariant())
            {
                case "from":
                    from = reader.GetString();
                    break;
                case "as":
                    renderAs = reader.GetString();
                    break;
                default:
                    throw new JsonException($"Unknown binding value property '{property}' (expected 'from' or 'as').");
            }
        }

        return from is null
            ? throw new JsonException("A binding value object requires 'from'.")
            : new WorkflowBindingValue(from, renderAs);
    }

    public override void Write(Utf8JsonWriter writer, WorkflowBindingValue value, JsonSerializerOptions options)
    {
        if (value.As is null)
        {
            writer.WriteStringValue(value.From);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("from", value.From);
        writer.WriteString("as", value.As);
        writer.WriteEndObject();
    }
}

/// <summary>A prompt template loaded from a specVersion-2+ package, ready to render.</summary>
public sealed record WorkflowPromptTemplate(
    string Id,
    string TemplateText,
    IReadOnlyList<string> Variables,
    string? PreludeText);

/// <summary>
/// A package reference of the form "name@vYYYY.MM.N" or "name@latest". Since
/// #448 a name may be a path — "oncology/breast" — of up to MaxSegments
/// segments, each the flat name grammar; a flat name is a one-segment path,
/// so every ref ever published still parses. The path is the identity: it
/// is the registry address ({name}/{version}/…), the recorded ref, and the
/// lineage hop.
/// </summary>
public sealed record WorkflowPackageRef(string Name, string Version)
{
    public const string LatestVersion = "latest";
    public const int MaxSegments = 4;
    public const string ManifestFileName = "manifest.json";

    private static readonly Regex NamePattern = new("^[a-z0-9][a-z0-9-]*(/[a-z0-9][a-z0-9-]*)*$", RegexOptions.Compiled);

    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && NamePattern.IsMatch(name) && name.Count(c => c == '/') < MaxSegments;

    /// <summary>
    /// Reads {name}/{version}/manifest.json from the right: the leaf, then a
    /// CalVer directory, then the name — which a nested name makes deeper.
    /// Unambiguous because a name segment can never be a CalVer (no dots).
    /// The one filter every registry listing uses (#448); anything else
    /// ({name}/latest.json, a prompt, a stamp) is not a manifest path.
    /// </summary>
    public static bool TryParseManifestPath(string blobPath, out string name, out string version)
    {
        name = string.Empty;
        version = string.Empty;
        var parts = blobPath.Split('/');

        if (parts.Length < 3 || parts[^1] != ManifestFileName || !CalVerVersion.TryParse(parts[^2], out _))
        {
            return false;
        }

        var candidate = string.Join('/', parts[..^2]);
        if (!IsValidName(candidate))
        {
            return false;
        }

        name = candidate;
        version = parts[^2];
        return true;
    }

    public bool IsLatest => string.Equals(Version, LatestVersion, StringComparison.Ordinal);

    public static bool TryParse(string? value, out WorkflowPackageRef? packageRef)
    {
        packageRef = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('@');
        if (parts.Length != 2 || !IsValidName(parts[0]))
        {
            return false;
        }

        var version = parts[1];
        if (!string.Equals(version, LatestVersion, StringComparison.Ordinal)
            && !CalVerVersion.TryParse(version, out _))
        {
            return false;
        }

        packageRef = new WorkflowPackageRef(parts[0], version);
        return true;
    }

    public override string ToString() => $"{Name}@{Version}";
}

/// <summary>
/// Package version in the form "vYYYY.MM.N": zero-padded month, and a within-month
/// release counter starting at 1. Comparison is numeric, never lexicographic —
/// "v2026.07.10" sorts after "v2026.07.2" even though it sorts before it as a string.
/// </summary>
public readonly record struct CalVerVersion(int Year, int Month, int Counter) : IComparable<CalVerVersion>
{
    private static readonly Regex Pattern = new(@"^v(?<year>\d{4})\.(?<month>\d{2})\.(?<counter>[1-9]\d*)$", RegexOptions.Compiled);

    public static bool TryParse(string? value, out CalVerVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Pattern.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        var month = int.Parse(match.Groups["month"].Value);
        if (month is < 1 or > 12)
        {
            return false;
        }

        version = new CalVerVersion(
            int.Parse(match.Groups["year"].Value),
            month,
            int.Parse(match.Groups["counter"].Value));
        return true;
    }

    public int CompareTo(CalVerVersion other)
    {
        var byYear = Year.CompareTo(other.Year);
        if (byYear != 0)
        {
            return byYear;
        }

        var byMonth = Month.CompareTo(other.Month);
        return byMonth != 0 ? byMonth : Counter.CompareTo(other.Counter);
    }

    /// <summary>
    /// The next version to publish under a name: the current month's counter
    /// starts at 1 and increments past the latest published version. Pure —
    /// the publish endpoint injects the clock. A latest that sorts at or above
    /// the current month's opener (clock skew, imported history) keeps its own
    /// month and increments, so the latest pointer never moves backwards.
    /// </summary>
    public static CalVerVersion AssignNext(CalVerVersion? latest, DateTimeOffset nowUtc)
    {
        var opener = new CalVerVersion(nowUtc.Year, nowUtc.Month, 1);

        return latest is { } current && current.CompareTo(opener) >= 0
            ? current with { Counter = current.Counter + 1 }
            : opener;
    }

    public override string ToString() => $"v{Year:D4}.{Month:D2}.{Counter}";
}
