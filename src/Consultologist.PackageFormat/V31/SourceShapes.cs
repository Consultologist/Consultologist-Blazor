namespace Consultologist.PackageFormat.V31;

/// <summary>What a member's value must be, as far as the member tables care (part 01 § 5).</summary>
public enum MemberType
{
    /// <summary>Any JSON value; the member's owner judges it (an expression, a list of objects, a count).</summary>
    Any,

    /// <summary>A string.</summary>
    Text,

    /// <summary>A JSON number with no fraction.</summary>
    Integer,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>A description-style array of strings.</summary>
    Lines,

    /// <summary>An array of strings (ids, tags, delivery channels).</summary>
    Ids,

    /// <summary>A JSON object.</summary>
    Object,

    /// <summary>A format reference: a string, or an inline schema (an object).</summary>
    Format,

    /// <summary>A JSON Logic value: here, anything but a missing member (a boolean or a <c>null</c> literal is the owner's to judge).</summary>
    Condition,

    /// <summary>A reference string.</summary>
    Reference,
}

/// <summary>One member of a declaration: its name, what it must be, and whether <c>null</c> is a value it may hold.</summary>
public sealed record MemberShape(string Name, MemberType Type, bool Nullable);

/// <summary>The members a declaration writes, in the published schema's order.</summary>
public sealed record DeclarationShape(string Name, IReadOnlyList<MemberShape> Members);

/// <summary>
/// #1499 (specVersion 31, area 02): the member table of each file kind, read off the published
/// schemas under <c>v31/schemas</c> and the tables of parts 01 and 04 (where they differ, the
/// schema wins: D18). A member is nullable when its schema admits <c>null</c>. Where a schema
/// fixes a member to a <c>const</c> or an <c>enum</c> (<c>kind</c>, <c>producer</c>,
/// <c>onInvalid</c>), the table only types it: the closed values are other tasks' rules.
/// </summary>
public static class SourceShapes
{
    private const MemberType Any = MemberType.Any;
    private const MemberType Text = MemberType.Text;
    private const MemberType Bool = MemberType.Boolean;

    private static MemberShape M(string name, MemberType type, bool nullable = false) => new(name, type, nullable);

    private static readonly MemberShape Label = M("label", Text);
    private static readonly MemberShape Description = M("description", MemberType.Lines, true);
    private static readonly MemberShape If = M("if", MemberType.Condition, true);

    private static DeclarationShape D(string name, params MemberShape[] members) => new(name, members);

    /// <summary>The shared members of a node, in the order the schemas require them (agent, rendered, computed, call).</summary>
    private static MemberShape[] NodeHead(MemberType format) =>
        [M("kind", Text), M("producer", Text), Label, Description, M("format", format)];

    public static readonly DeclarationShape Input = D("Input",
        Label, Description, M("format", MemberType.Format), M("required", Bool), M("channel", Text, true));

    public static readonly DeclarationShape Manifest = D("Manifest",
        M("name", Text), M("version", Text), M("specVersion", MemberType.Integer),
        M("derivedFrom", Text, true), M("title", Text, true), Description,
        M("tags", MemberType.Ids), M("inputs", MemberType.Object));

    public static readonly DeclarationShape Index = D("Index", Description, M("items", MemberType.Ids));

    public static readonly DeclarationShape DeliverablesIndex = D("DeliverablesIndex",
        Description, M("items", MemberType.Ids), M("delivery", MemberType.Ids), M("bundle", Text, true));

    public static readonly DeclarationShape SnippetFolderIndex = D("SnippetFolderIndex",
        Description, M("items", MemberType.Ids), M("format", MemberType.Format));

    public static readonly DeclarationShape MacroFolderIndex = D("MacroFolderIndex",
        Description, M("items", MemberType.Ids), If);

    public static readonly DeclarationShape NodeAgent = D("NodeAgent",
        [.. NodeHead(Text),
         M("reproducible", Bool), M("modelDerived", Bool), M("runDependent", Bool), M("profileCarrying", Bool),
         If, M("onInvalid", Text), M("failIfEmpty", Text, true), M("parts", MemberType.Object, true), M("ordered", Bool)]);

    public static readonly DeclarationShape NodeRendered = D("NodeRendered",
        [.. NodeHead(Text),
         M("reproducible", Bool), M("modelDerived", Bool), M("runDependent", Bool), M("profileCarrying", Bool), If]);

    public static readonly DeclarationShape NodeComputed = D("NodeComputed",
        [.. NodeHead(MemberType.Format),
         M("reproducible", Bool), M("modelDerived", Bool), M("runDependent", Bool), M("profileCarrying", Bool),
         If, M("expression", Any)]);

    public static readonly DeclarationShape NodeCall = D("NodeCall",
        [.. NodeHead(MemberType.Format),
         M("reproducible", Bool), M("modelDerived", Bool), M("runDependent", Bool), M("profileCarrying", Bool),
         If, M("function", Text), M("with", MemberType.Object)]);

    public static readonly DeclarationShape Check = D("Check",
        [.. NodeHead(Text),
         M("reproducible", Bool), M("modelDerived", Bool), M("runDependent", Bool), M("profileCarrying", Bool),
         If, M("expression", Any)]);

    /// <summary>The nine members every flow node shares (D18), in the schemas' order.</summary>
    private static MemberShape[] FlowHead() =>
    [
        M("kind", Text), Label, Description, M("format", MemberType.Format), M("reproducible", Bool),
        M("producer", Text), M("modelDerived", Bool), M("runDependent", Bool), M("profileCarrying", Bool),
    ];

    // maxParallel and max are typed Any here: a number that is not a whole number, or out of range,
    // has its own sentence in Task 7, not A02-001.
    public static readonly DeclarationShape FlowFor = D("FlowFor",
        [.. FlowHead(),
         M("in", MemberType.Reference), M("parallel", Bool), M("maxParallel", Any, true),
         M("until", MemberType.Condition, true), M("max", Any, true), M("itemLabel", Text, true),
         M("nodes", MemberType.Ids), M("ret", Text)]);

    public static readonly DeclarationShape FlowWhile = D("FlowWhile",
        [.. FlowHead(),
         M("while", MemberType.Condition), M("max", Any), M("until", MemberType.Condition, true),
         M("checkFirst", Bool), M("nodes", MemberType.Ids), M("ret", Text)]);

    public static readonly DeclarationShape FlowIf = D("FlowIf",
        [.. FlowHead(),
         M("if", MemberType.Condition), M("then", MemberType.Object), M("else", MemberType.Object, true)]);

    // branches is a list of objects, which no MemberType says; Task 7 walks it and judges its shape.
    public static readonly DeclarationShape FlowCase = D("FlowCase",
        [.. FlowHead(),
         M("case", MemberType.Reference), M("branches", Any), M("else", MemberType.Object, true)]);

    public static readonly DeclarationShape Body = D("Body", M("nodes", MemberType.Ids), M("ret", Text));

    public static readonly DeclarationShape Branch = D("Branch",
        M("when", MemberType.Ids), M("nodes", MemberType.Ids), M("ret", Text, true));

    public static readonly DeclarationShape Parameter = D("Parameter", Label, Description, M("format", MemberType.Format));

    public static readonly DeclarationShape Uses = D("Uses",
        M("snippets", MemberType.Ids), M("macros", MemberType.Ids), M("formats", MemberType.Ids), M("renderers", MemberType.Ids));

    public static readonly DeclarationShape FunctionIndex = D("FunctionIndex",
        Label, Description, M("inputs", MemberType.Object), M("uses", MemberType.Object),
        M("nodes", MemberType.Ids), M("ret", Text), M("format", MemberType.Format));

    public static readonly DeclarationShape Renderer = D("Renderer",
        Label, M("kind", Text), Description, M("input", MemberType.Format), M("format", Text));

    public static readonly DeclarationShape Snippet = D("Snippet", Label, Description, If, M("format", Text));

    public static readonly DeclarationShape Macro = D("Macro",
        Label, Description, If, M("format", Text), M("instruction", MemberType.Lines));

    public static readonly DeclarationShape Deliverable = D("Deliverable",
        Label, M("kind", Text), Description, M("format", Text), If, M("check", Text, true), M("whenEmpty", Text));

    public static readonly DeclarationShape Test = D("Test",
        Description, M("inputs", MemberType.Object), M("profile", MemberType.Object), M("run", MemberType.Object),
        M("outputs", MemberType.Object), M("expect", MemberType.Object));

    /// <summary>The shape of the published schema <c>&lt;schemaName&gt;.schema.json</c>, or null when this reader has none for it.</summary>
    public static DeclarationShape? ForSchema(string schemaName) => schemaName switch
    {
        "manifest" => Manifest,
        "index" => Index,
        "deliverables-index" => DeliverablesIndex,
        "snippet-folder-index" => SnippetFolderIndex,
        "macro-folder-index" => MacroFolderIndex,
        "node-agent" => NodeAgent,
        "node-rendered" => NodeRendered,
        "node-computed" => NodeComputed,
        "node-call" => NodeCall,
        "check" => Check,
        "flow-for" => FlowFor,
        "flow-while" => FlowWhile,
        "flow-if" => FlowIf,
        "flow-case" => FlowCase,
        "function-index" => FunctionIndex,
        "renderer" => Renderer,
        "snippet" => Snippet,
        "macro" => Macro,
        "deliverable" => Deliverable,
        "test" => Test,
        _ => null,
    };
}
