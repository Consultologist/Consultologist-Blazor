using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>#1503 (specVersion 31, area 06): what an entry of the plan is: a node's kind, or a deliverable.</summary>
public enum PlanKind
{
    /// <summary>An agent node (<c>kind: "agent"</c>).</summary>
    Agent,

    /// <summary>A rendered node in <c>nodes</c> (<c>kind: "rendered"</c>).</summary>
    Rendered,

    /// <summary>A computed node or check (<c>kind: "computed"</c>).</summary>
    Computed,

    /// <summary>A <c>for</c> loop.</summary>
    For,

    /// <summary>A <c>while</c> loop.</summary>
    While,

    /// <summary>An <c>if</c>.</summary>
    If,

    /// <summary>A <c>case</c>.</summary>
    Case,

    /// <summary>A <c>call</c> of a function.</summary>
    Call,

    /// <summary>An entry of <c>deliverables</c> (there <c>kind: "rendered"</c>).</summary>
    Deliverable,
}

/// <summary>
/// #1503 (specVersion 31, area 06): one branch of an <c>if</c> (then, else) or a <c>case</c> (each branch, then else): its
/// children's keys in order and the key it returns, or null. <paramref name="When"/> is a <c>case</c> branch's values, and
/// null for a then and an else. An <c>else</c> that is <c>null</c> is a branch with no nodes, which runs nothing
/// (part 04 § 5: the node is <c>skipped</c>, as after an empty branch).
/// </summary>
public sealed record PlanBranch(IReadOnlyList<string> Nodes, string? Ret, IReadOnlyList<JsonNode?>? When);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 9; D2, D3): one unit an entry's body places, as its <c>placements</c>
/// lists it: its <paramref name="Site"/> (<c>part.&lt;id&gt;</c>, <c>snippet.&lt;id&gt;</c>, <c>macro.&lt;id&gt;</c>,
/// <c>macro-folder.&lt;id&gt;</c>), the site that places it (<paramref name="Within"/>, null for the body itself), its
/// header <c>if</c> rooted for the entry's place and compiled with its <c>plainConverts</c> (<paramref name="If"/>, null
/// when it has none), that rule as the printer writes it (<paramref name="Printed"/>), and a macro's marker form
/// (<paramref name="Form"/>: <c>block</c> or <c>inline</c>; null for other kinds).
/// </summary>
public sealed record PlanPlacement(string Site, string? Within, IJsonLogicRule? If, string? Printed, string? Form);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 1.2 step 5): one entry of the plan: a node, check or deliverable of the package,
/// or a node of a function's body, with its rules compiled (each with its <c>plainConverts</c> list, D3) and what it
/// depends on. Built only by <see cref="RunPlan.Build"/>; its JSON is the document's, read-only.
/// </summary>
public sealed class PlanEntry
{
    internal PlanEntry(
        string key,
        string id,
        PlanKind kind,
        string? parent,
        string? function,
        int order,
        JsonObject json,
        IReadOnlyDictionary<string, IJsonLogicRule> rules,
        IReadOnlyList<string> body,
        IReadOnlyList<PlanBranch> branches,
        string? ret,
        IReadOnlyList<string> reads,
        IReadOnlyList<string> depends,
        IReadOnlyList<PlanPlacement> placements,
        IReadOnlyList<string> neutralReads,
        string? check,
        IReadOnlyList<string> gates,
        IReadOnlySet<string> readsComposed)
    {
        Key = key;
        Id = id;
        Kind = kind;
        Parent = parent;
        Function = function;
        Order = order;
        Json = json;
        If = rules.GetValueOrDefault("if");
        Expression = rules.GetValueOrDefault("expression");
        While = rules.GetValueOrDefault("while");
        Until = rules.GetValueOrDefault("until");
        Body = body;
        Branches = branches;
        Ret = ret;
        Reads = reads;
        Depends = depends;
        Placements = placements;
        NeutralReads = neutralReads;
        Check = check;
        Gates = gates;
        ReadsComposed = readsComposed;
    }

    /// <summary>The rooted key (<c>node.sections.paragraphs.draft_paragraph</c>); in a function, local to it.</summary>
    public string Key { get; }

    /// <summary>The bare id.</summary>
    public string Id { get; }

    /// <summary>What the entry is.</summary>
    public PlanKind Kind { get; }

    /// <summary>The enclosing flow node's key, or null at the top level (of the package, or of a function).</summary>
    public string? Parent { get; }

    /// <summary>The id of the function a <c>call</c> calls; null for every other kind.</summary>
    public string? Function { get; }

    /// <summary>
    /// The entry's position in <c>graph.order</c>; for a node of a function, its position in the walk of the function's
    /// body (each node, then its children, in order).
    /// </summary>
    public int Order { get; }

    /// <summary>The entry as <c>resolve.json</c> holds it. Read-only: clone before placing it in another tree.</summary>
    public JsonObject Json { get; }

    /// <summary>The entry's <c>format</c> member as written (a <c>$ref</c> into the bundle, or an inline schema).</summary>
    public JsonNode? Format => Json["format"];

    /// <summary>The compiled <c>if</c>, or null when the entry has none.</summary>
    public IJsonLogicRule? If { get; }

    /// <summary>The compiled <c>expression</c> of a computed entry, or null.</summary>
    public IJsonLogicRule? Expression { get; }

    /// <summary>The compiled <c>while</c> of a while, or null.</summary>
    public IJsonLogicRule? While { get; }

    /// <summary>The compiled <c>until</c> of a loop, or null.</summary>
    public IJsonLogicRule? Until { get; }

    /// <summary>The children's keys of a <c>for</c> or a <c>while</c>, in order; empty for every other kind.</summary>
    public IReadOnlyList<string> Body { get; }

    /// <summary>An <c>if</c>'s then and else; a <c>case</c>'s branches, then its else; empty for every other kind.</summary>
    public IReadOnlyList<PlanBranch> Branches { get; }

    /// <summary>The key a loop returns, or null.</summary>
    public string? Ret { get; }

    /// <summary>
    /// Every path the entry reads, sorted, without repeats: its <c>reads</c>, every <c>var</c> of its rules and (#1504)
    /// of its placements' rules, and its <c>in</c>, its <c>case</c> subject and its <c>with</c> values.
    /// </summary>
    public IReadOnlyList<string> Reads { get; }

    /// <summary>
    /// The keys of the entry's map (the package's, or its function's) that <see cref="Reads"/> name, sorted: for each read,
    /// the longest key that is the path or a prefix of it ending at a dot; and for <c>pass.(parent.)*node.&lt;id&gt;</c>, that
    /// pass's node (part 04 § 3.3). Any other read through <c>item</c>, <c>pass</c>, <c>param</c>, <c>run</c>,
    /// <c>input</c>, <c>profile</c> or <c>snippet</c> names none, and an entry never depends on itself.
    /// </summary>
    public IReadOnlyList<string> Depends { get; }

    /// <summary>
    /// #1504 (area 07; D2, D3): every unit an agent's, a rendered node's or a deliverable's body places, in <c>placements</c>
    /// order (placed units expanded in place); empty for every other kind.
    /// </summary>
    public IReadOnlyList<PlanPlacement> Placements { get; }

    /// <summary>
    /// #1504 (area 07; part 05 § 4; D4): the run-neutral texts the entry's body reads: <c>&lt;rooted head&gt;.neutral</c>
    /// for each of its <c>runSpots</c> whose <c>neutral</c> is <c>"text"</c> (a run-dependent <c>rendered</c> node, or a
    /// flow node or a parameter whose value is one), its head rooted through <c>names</c> (D1), sorted, without repeats.
    /// A call's scope holds them beside its compiled <c>reads</c>.
    /// </summary>
    internal IReadOnlyList<string> NeutralReads { get; }

    /// <summary>#1505 (area 08; part 06 § 7): a deliverable's gate, the key of the check its <c>check</c> names; null for an ungated deliverable and every other kind.</summary>
    public string? Check { get; }

    /// <summary>#1505 (area 08): the keys of the deliverables this check gates (whose <c>check</c> names it), in <c>graph.order</c>; empty otherwise.</summary>
    public IReadOnlyList<string> Gates { get; }

    /// <summary>
    /// #1505 (area 08; D3): the keys of the deliverables this entry reads as composed: it reads them and is in their
    /// gate set (their <c>check</c> and every check that check depends on, transitively, its loops' bodies included), so
    /// it reads each as soon as it is composed, not its outcome. Empty for every entry outside <c>checks</c>.
    /// </summary>
    public IReadOnlySet<string> ReadsComposed { get; }

    /// <summary><see cref="JsonLogicPrinter.Print"/> of the rule held in <paramref name="member"/> (<c>if</c>, <c>expression</c>, <c>while</c>, <c>until</c>).</summary>
    public string Printed(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return JsonLogicPrinter.Print(Json[member]);
    }
}
