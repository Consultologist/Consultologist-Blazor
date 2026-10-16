using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// What a v25 template may reference (package-format-v25-design.md § 1): the
/// package's inputs (typed, for <c>input.&lt;id&gt;.&lt;field&gt;</c> paths), its
/// nodes with their formats (for <c>node.&lt;id&gt;.value.&lt;path&gt;</c>), its
/// snippets and snippet folders, its macros and macro folders, and whether the
/// node this template belongs to fans — which decides what <c>item</c> is. The
/// validator builds one per template from the manifest (A4); the walker only
/// reads it.
/// </summary>
public sealed record TemplateReferenceContext(
    IReadOnlyDictionary<string, WorkflowDeclarationNode> Inputs,
    IReadOnlyDictionary<string, TemplateNodeShape> Nodes,
    IReadOnlySet<string> Snippets,
    IReadOnlySet<string> SnippetFolders,
    IReadOnlySet<string> Macros,
    IReadOnlySet<string> MacroFolders,
    TemplateFan? Fan,
    // C6 (#1012): a snippet or macro text is walked once, with no fan of its own —
    // whether it runs for a fan item is the reading node's business. With this set
    // an item read is recorded (members id, name, text, value; value untyped)
    // instead of refused; the validator refuses it at the reading site when that
    // node does not fan.
    bool ItemDeferred = false,
    // #1037: the ids of the folder's items when the fan is a snippet folder — what a
    // condition's `item.id == "x"` is typed against.
    IReadOnlyList<string>? FanItemIds = null)
{
    public static readonly IReadOnlySet<string> NoIds = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>A context with nothing declared — a template may then reference nothing.</summary>
    public static readonly TemplateReferenceContext Empty = new(
        new Dictionary<string, WorkflowDeclarationNode>(StringComparer.Ordinal),
        new Dictionary<string, TemplateNodeShape>(StringComparer.Ordinal),
        NoIds, NoIds, NoIds, NoIds, null);
}

/// <summary>
/// A node as a template sees it: the format id it declares, the format's type
/// (the input vocabulary — <c>text</c> has no <c>.value</c>; <c>object</c>,
/// <c>array</c> and <c>enum</c> do), and the format's schema when it carries one.
/// </summary>
public sealed record TemplateNodeShape(
    string Format,
    string Type,
    JsonNode? Schema = null,
    // #1037: an enum format's answer set (the format's own values, or the node's
    // with.values) — what a condition's `node.x.value == "v"` is typed against.
    IReadOnlyList<string>? Values = null)
{
    /// <summary>Whether <c>node.&lt;id&gt;.value</c> exists: every type but text parses.</summary>
    public bool HasValue => Type != WorkflowInputTypes.Text;
}

/// <summary>What the template's node fans over — null when it does not fan.</summary>
public abstract record TemplateFan;

/// <summary>A fan over a snippet folder: <c>item</c> carries id, name and text (the item rendered).</summary>
public sealed record SnippetFolderFan(string Folder) : TemplateFan;

/// <summary>A fan over an array input: <c>item</c> carries id, name and value (the element, typed as declared).</summary>
public sealed record InputArrayFan(WorkflowDeclarationNode Element) : TemplateFan;

/// <summary>The five reserved objects of § 1, and the roots a 25 template must not use.</summary>
public static class TemplateObjects
{
    public const string Input = "input";
    public const string Node = "node";
    public const string Snippet = "snippet";
    public const string Item = "item";
    public const string Macro = "macro";

    /// <summary>A4 (#999, 2026-10-13): the run facts (date, job, package, host) — the v11 `{{run:…}}` namespace as an object.</summary>
    public const string Run = "run";

    /// <summary>A4 (#999): the profile facts (name, signature) — the v11/v12 `{{profile:…}}` namespace as an object.</summary>
    public const string Profile = "profile";

    public static readonly IReadOnlyList<string> All = new[] { Input, Node, Snippet, Item, Macro, Run, Profile };

    /// <summary><c>data</c> became <c>snippet</c> at 25 (§ 4).</summary>
    public const string RetiredData = "data";

    /// <summary>Declared bindings and variables are retired at 25 (§ 2); a template reads objects.</summary>
    public static readonly IReadOnlyList<string> RetiredRoots = new[] { RetiredData, "bindings", "variables" };

    public static bool IsReserved(string? name) =>
        name != null && (All.Contains(name, StringComparer.Ordinal) || RetiredRoots.Contains(name, StringComparer.Ordinal));

    /// <summary>
    /// Scriban's own globals, opaque to the walker: the builtin modules, the
    /// loop state objects and the empty/blank singletons. Anything else that
    /// is not an object, a loop variable or a captured name is unresolved.
    /// </summary>
    public static readonly IReadOnlySet<string> ScribanBuiltins = new HashSet<string>(StringComparer.Ordinal)
    {
        "date", "string", "array", "math", "object", "regex", "timespan", "html",
        "for", "while", "tablerow", "empty", "blank", "include"
    };
}

/// <summary>The closed member sets of § 1.</summary>
public static class TemplateMembers
{
    public const string Id = "id";
    public const string Name = "name";
    public const string Content = "content";
    public const string Value = "value";
    public const string Prompt = "prompt";

    public static readonly IReadOnlyList<string> Node = new[] { Content, Value };
    public static readonly IReadOnlyList<string> Snippet = new[] { Id, Name, Content };

    /// <summary>C6 (#1012): the members a fan item may carry under either fan kind — what a text walked with no fan of its own may read.</summary>
    public static readonly IReadOnlyList<string> AnyFanItem = new[] { Id, Name, Content, Value };
    public static readonly IReadOnlyList<string> Macro = new[] { Id, Name, Content, Prompt };
    public static readonly IReadOnlyList<string> SnippetFanItem = new[] { Id, Name, Content };
    public static readonly IReadOnlyList<string> InputFanItem = new[] { Id, Name, Value };

    /// <summary><c>run.date</c>, <c>.job</c>, <c>.package</c>, <c>.host</c> — the v11 run facts.</summary>
    public static readonly IReadOnlyList<string> Run = WorkflowMacroPlaceholders.RunFacts.Order(StringComparer.Ordinal).ToList();

    /// <summary><c>profile.name</c>, <c>.signature</c> — the v12 profile facts.</summary>
    public static readonly IReadOnlyList<string> Profile = WorkflowMacroPlaceholders.ProfileFactsFor(25).Order(StringComparer.Ordinal).ToList();
}

/// <summary>
/// One shape over both worlds a path can be typed against: an input's
/// declaration (<see cref="WorkflowDeclarationNode"/>) and a format's JSON
/// schema. A null shape means untyped: members pass.
/// </summary>
public sealed record TemplateValueShape(
    string Kind,
    IReadOnlyDictionary<string, TemplateValueShape>? Fields,
    TemplateValueShape? Items,
    IReadOnlyList<string>? Values)
{
    public const string Unknown = "unknown";

    public bool IsObject => Kind == WorkflowInputTypes.Object;
    public bool IsArray => Kind == WorkflowInputTypes.Array;
    public bool IsUnknown => Kind == Unknown;

    public static readonly TemplateValueShape Text = new(WorkflowInputTypes.Text, null, null, null);

    public static TemplateValueShape FromDeclaration(WorkflowDeclarationNode node) =>
        new(
            node.Type,
            node.Fields is null ? null : node.Fields.ToDictionary(f => f.Id, FromDeclaration, StringComparer.Ordinal),
            node.Items is null ? null : FromDeclaration(node.Items),
            node.Values);

    /// <summary>
    /// A JSON schema folded to a shape: <c>type</c> (a nullable union counts as
    /// its non-null arm), <c>properties</c>, <c>items</c>, <c>enum</c>. A schema
    /// with no type and no structure is unknown.
    /// </summary>
    public static TemplateValueShape FromSchema(JsonNode? schema)
    {
        if (schema is not JsonObject obj)
        {
            return new TemplateValueShape(Unknown, null, null, null);
        }

        var properties = obj["properties"] as JsonObject;
        var items = obj["items"];
        var enumValues = obj["enum"] as JsonArray;
        var type = TypeOf(obj);

        if (enumValues is not null)
        {
            return new TemplateValueShape(
                WorkflowInputTypes.Enum, null, null,
                enumValues.Select(v => v?.GetValue<string>() ?? "").ToList());
        }

        if (type == "object" || (type is null && properties is not null))
        {
            return new TemplateValueShape(
                WorkflowInputTypes.Object,
                properties?.ToDictionary(p => p.Key, p => FromSchema(p.Value), StringComparer.Ordinal)
                    ?? new Dictionary<string, TemplateValueShape>(StringComparer.Ordinal),
                null, null);
        }

        if (type == "array" || (type is null && items is not null))
        {
            return new TemplateValueShape(WorkflowInputTypes.Array, null, items is null ? null : FromSchema(items), null);
        }

        return type switch
        {
            "string" => Text,
            "number" or "integer" => new TemplateValueShape(WorkflowInputTypes.Number, null, null, null),
            "boolean" => new TemplateValueShape(WorkflowInputTypes.Boolean, null, null, null),
            _ => new TemplateValueShape(Unknown, null, null, null)
        };
    }

    private static string? TypeOf(JsonObject obj)
    {
        var type = obj["type"];

        if (type is JsonValue value && value.TryGetValue(out string? single))
        {
            return single;
        }

        if (type is JsonArray union)
        {
            return union
                .Select(arm => arm is JsonValue v && v.TryGetValue(out string? s) ? s : null)
                .FirstOrDefault(s => s is not null and not "null");
        }

        return null;
    }

    /// <summary>"an object", "an array of text", "a boolean" — for a sentence about a path.</summary>
    public string Describe() => Kind switch
    {
        WorkflowInputTypes.Array => Items is null ? "an array" : $"an array of {Items.DescribePlural()}",
        WorkflowInputTypes.Enum or WorkflowInputTypes.Object => $"an {Kind}",
        Unknown => "an untyped value",
        _ => $"a {Kind}"
    };

    private string DescribePlural() => Kind switch
    {
        WorkflowInputTypes.Array => Items is null ? "arrays" : $"arrays of {Items.DescribePlural()}",
        Unknown => "untyped values",
        _ => Kind
    };
}
