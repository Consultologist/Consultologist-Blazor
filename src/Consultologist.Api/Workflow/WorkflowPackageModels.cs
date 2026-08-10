using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Consultologist.Api.Workflow;

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
    List<WorkflowResultSpec>? Results = null);

/// <summary>
/// One declared input slot of a specVersion-7 package: the id nodes bind as
/// "input:&lt;id&gt;" and callers supply per job
/// (docs/customizable-workflow/package-format-v7.md § 2).
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
    List<string>? Values = null);

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

    public static readonly IReadOnlyList<string> All = new[] { Text, Date, Enum, Boolean };

    /// <summary>An absent type is text — the default that keeps v7 declarations valid.</summary>
    public static string Of(WorkflowInputSpec input) => input.Type ?? Text;
}

/// <summary>
/// One declared deliverable of a specVersion-7 package: an authored id and
/// label naming an aggregator node. The string "result" form remains valid as
/// one-entry sugar (docs/customizable-workflow/package-format-v7.md § 3).
/// </summary>
public sealed record WorkflowResultSpec(
    string Id,
    string Node,
    string Label);

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
/// (docs/customizable-workflow/package-format-v5.md).
/// </summary>
public sealed record WorkflowNodeSpec(
    string Id,
    string Label,
    string? Prompt = null,
    Dictionary<string, WorkflowBindingValue>? Bindings = null,
    WorkflowNodeOutputSpec? Output = null,
    string? ForEach = null,
    List<string>? Aggregate = null);

public sealed record WorkflowNodeOutputSpec(
    string Schema,
    string? FailIfEmpty = null);

/// <summary>
/// One item of a resolved data collection: the declared fields materialized —
/// per-item file content becomes the "content" field
/// (docs/customizable-workflow/package-format-v5.md).
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
/// A deliverable of a resolved package: the declared (or sugar-derived) id and
/// label with the aggregator node it names. v5/v6 packages resolve with a null
/// set — ResultNodeId remains their single-result contract.
/// </summary>
public sealed record WorkflowResolvedResult(string Id, string NodeId, string Label);

public sealed record WorkflowPackage(
    WorkflowPackageManifest Manifest,
    IReadOnlyDictionary<string, WorkflowPromptTemplate>? Prompts = null,
    IReadOnlyList<WorkflowNodeSpec>? Nodes = null,
    IReadOnlyDictionary<string, string>? SchemaContracts = null,
    WorkflowPackageData? Data = null,
    string? ResultNodeId = null,
    IReadOnlyDictionary<string, string>? SourceFiles = null,
    IReadOnlyList<WorkflowResolvedResult>? Results = null)
{
    public string Ref => $"{Manifest.Name}@{Manifest.Version}";

    public bool HasPrompts => Prompts is { Count: > 0 };
}

public sealed record WorkflowPackageBlockResponse(string Id, string Name);

/// <summary>
/// One declared input slot on the current-package response — what the consult
/// setup form renders a field for (package-format-v7.md § inputs). Null on
/// v5/v6 packages, whose single slot is the frozen consult_draft convention.
/// </summary>
public sealed record WorkflowPackageInputResponse(string Id, string Label, bool Required);

/// <summary>
/// One declared deliverable on the current-package response: the authored
/// identity the client groups blocks and result tabs by. The result node is
/// engine-side and never leaves the API.
/// </summary>
public sealed record WorkflowPackageResultResponse(string Id, string Label);

public sealed record WorkflowPackageResponse(
    string Name,
    string Version,
    int SpecVersion,
    IReadOnlyList<WorkflowPackageBlockResponse>? Blocks = null,
    IReadOnlyList<WorkflowPackageInputResponse>? Inputs = null,
    IReadOnlyList<WorkflowPackageResultResponse>? Results = null);

/// <summary>
/// The pin-resolved package's full editable content: the typed manifest (the
/// binding-value converter round-trips it) plus every source file the store
/// downloaded — prompts (incl. preludes), schemas, and data files including each
/// collection's index.json. The editor's load half of the load→edit→publish
/// round-tripping contract (docs/customizable-workflow/in-app-editing.md).
/// </summary>
public sealed record WorkflowPackageContentResponse(
    string Name,
    string Version,
    int SpecVersion,
    WorkflowPackageManifest Manifest,
    IReadOnlyDictionary<string, string> Files);

/// <summary>
/// The publish half of the editor's round-tripping contract: the manifest and
/// files as loaded from the content endpoint (edited texts substituted), plus
/// Source — the concrete ref the content was loaded from, which the server
/// validates and stamps as the fork's derivedFrom. The client's manifest
/// name/version/derivedFrom are ignored.
/// </summary>
public sealed record WorkflowPackagePublishRequest(
    string? Source = null,
    WorkflowPackageManifest? Manifest = null,
    Dictionary<string, string>? Files = null);

public sealed record WorkflowPackagePublishResponse(
    string Name,
    string Version,
    string Ref,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A package reference of the form "name@vYYYY.MM.N" or "name@latest".
/// </summary>
public sealed record WorkflowPackageRef(string Name, string Version)
{
    public const string LatestVersion = "latest";

    private static readonly Regex NamePattern = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    public bool IsLatest => string.Equals(Version, LatestVersion, StringComparison.Ordinal);

    public static bool TryParse(string? value, out WorkflowPackageRef? packageRef)
    {
        packageRef = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('@');
        if (parts.Length != 2 || !NamePattern.IsMatch(parts[0]))
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
