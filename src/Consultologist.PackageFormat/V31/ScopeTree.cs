using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): what a reference names at one place. <paramref name="Kind"/>, <paramref name="Format"/>
/// and <paramref name="Nullable"/> are an <see cref="ScopeHead"/>'s; <paramref name="Key"/> is the rooted key of the thing
/// read for <c>node</c>, <c>check</c>, <c>input</c> (<c>input.&lt;id&gt;</c>) and <c>param</c> (<c>param.&lt;id&gt;</c>),
/// null otherwise; <paramref name="Length"/> is how many segments the head consumed. For a
/// <see cref="ScopeHeadKind.Format"/> head the rest go to <see cref="FormatReads.Read"/> (the head is the thing, read
/// through <c>.value</c>, <c>.content</c>, <c>.title</c>); for a <see cref="ScopeHeadKind.Value"/> head they are fields
/// (the head included the member: <c>node.x.items</c>, <c>node.x.state</c>, <c>item.value</c>, <c>item.id</c>,
/// <c>pass.number</c>, <c>run.date</c>). <paramref name="Refusal"/> is set exactly for <see cref="ScopeHeadKind.Refused"/>.
/// </summary>
public sealed record Resolved(ScopeHeadKind Kind, string? Key, int Length, JsonNode? Format, bool Nullable, SourceRefusal? Refusal);

/// <summary>
/// #1502 (specVersion 31, area 05): one body of a package (part 01 § 9.2): the top of <c>nodes/</c>, of <c>checks/</c>
/// or of a function, or the body of a flow node (a <c>for</c>'s, a <c>while</c>'s, each branch of an <c>if</c> or a
/// <c>case</c>). Made only by <see cref="ScopeTree.Build"/>.
/// </summary>
public sealed class ScopeFrame
{
    private readonly ScopeTree tree;

    internal ScopeFrame(ScopeTree tree, ScopeFrame? parent, SourceFunction? function, SourceNode? owner, IReadOnlyList<SourceNode> nodes, bool inChecks)
    {
        this.tree = tree;
        Parent = parent;
        Function = function;
        Owner = owner;
        Nodes = nodes;
        InChecks = inChecks;
    }

    /// <summary>The body that encloses this one; null at a root (a function's root included: its scope is sealed).</summary>
    public ScopeFrame? Parent { get; }

    /// <summary>The function this body is in; null outside one.</summary>
    public SourceFunction? Function { get; }

    /// <summary>The <see cref="SourceFor"/>, <see cref="SourceWhile"/>, <see cref="SourceIf"/> or <see cref="SourceCase"/> whose body this is; null at a root.</summary>
    public SourceNode? Owner { get; }

    /// <summary>The nodes of this body, in listed order.</summary>
    public IReadOnlyList<SourceNode> Nodes { get; }

    /// <summary>Whether this body is in <c>checks/</c>.</summary>
    internal bool InChecks { get; }

    /// <summary>
    /// The node <paramref name="root"/><c>.</c><paramref name="id"/> names here (<paramref name="root"/> <c>node</c> or
    /// <c>check</c>), or null: for the root of this body's own tree, this body, then each enclosing body, stopping at the
    /// function's root; the other root sees the top level of the other folder (in <c>checks/</c>, <c>node.&lt;id&gt;</c>
    /// sees the top level of <c>nodes/</c>, part 06 § 1.2; in <c>nodes/</c>, <c>check.&lt;id&gt;</c> the top of
    /// <c>checks/</c>). Inside a function no check is visible.
    /// </summary>
    public SourceNode? Visible(string root, string id)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(id);
        var own = InChecks ? "check" : "node";
        if (root == own)
        {
            for (var frame = this; frame != null; frame = frame.Parent)
            {
                if (frame.Nodes.FirstOrDefault(n => n.Id == id) is { } found)
                    return found;
            }

            return null;
        }

        if (Function != null || root is not ("node" or "check"))
            return null;
        return (InChecks ? tree.PackageRoot : tree.ChecksRoot).Nodes.FirstOrDefault(n => n.Id == id);
    }

    /// <summary>The loop whose item is <c>item</c> (0), <c>item.parent</c> (1), …, skipping whiles; null past the outermost (or the function's root).</summary>
    public SourceFor? Loop(int up) => Outward<SourceFor>(up);

    /// <summary>The while whose pass is <c>pass</c> (0), <c>pass.parent</c> (1), …, skipping loops; null past the outermost (or the function's root).</summary>
    public SourceWhile? While(int up) => Outward<SourceWhile>(up);

    /// <summary>
    /// A node <paramref name="root"/><c>.</c><paramref name="id"/> that is not visible here but exists, inside a loop (a
    /// <c>for</c> or a <c>while</c>) this body is outside of (S04-039); the first in tree order, or null.
    /// </summary>
    public SourceNode? Hidden(string root, string id)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(id);
        return tree.HiddenFrom(this, root, id)?.Node;
    }

    private T? Outward<T>(int up) where T : SourceNode
    {
        for (var frame = this; frame != null && up >= 0; frame = frame.Parent)
        {
            if (frame.Owner is T owner && up-- == 0)
                return owner;
        }

        return null;
    }
}

/// <summary>
/// #1502 (specVersion 31, area 05): who encloses whom in a package (part 01 § 9; part 04 §§ 2.5, 2.6, 3.3, 3.4, 4, 7.4),
/// and what a reference names at a place: <see cref="Resolve"/>, the one resolution every caller uses (a <c>var</c>
/// path, an <c>in</c>, a <c>case</c>, a <c>with</c>). Members are typed by D14. Never throws on package content, and
/// every walk is bounded by the reader's nesting limit; a loop whose source reads itself is not typed. A tree caches
/// each loop's source (<see cref="Source"/>, in <c>ScopeTree.Items.cs</c>) as it is first asked for, so it serves one
/// caller at a time.
/// </summary>
public sealed partial class ScopeTree
{
    private static readonly string[] States = ["completed", "skipped", "not_run"];

    private readonly SourcePackage package;
    private readonly PackageTypes types;
    private readonly PackageScope outside;
    private readonly Dictionary<SourceNode, ScopeFrame> frames = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SourceNode, ScopeFrame> bodies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SourceFunction, ScopeFrame> functions = new(ReferenceEqualityComparer.Instance);
    private ScopeTree(SourcePackage package, PackageTypes types)
    {
        this.package = package;
        this.types = types;
        outside = new PackageScope(package, inFunction: false);
        PackageRoot = new ScopeFrame(this, null, null, null, package.Nodes.Nodes, inChecks: false);
        ChecksRoot = new ScopeFrame(this, null, null, null, package.Checks.Nodes, inChecks: true);
    }

    /// <summary>The top level of <c>nodes/</c>.</summary>
    public ScopeFrame PackageRoot { get; }

    /// <summary>The top level of <c>checks/</c>.</summary>
    public ScopeFrame ChecksRoot { get; }

    /// <summary>Every node of the package, <c>nodes/</c>, <c>checks/</c>, then each function, a flow node before its children, with its key; built once, by <see cref="Build"/>.</summary>
    internal IReadOnlyDictionary<SourceNode, string> Keys { get; private set; } = new Dictionary<SourceNode, string>();

    /// <summary>Every node of the package in <see cref="Keys"/>'s order: <c>nodes/</c>, <c>checks/</c>, then each function, a flow node before its children.</summary>
    internal IEnumerable<SourceNode> All() =>
        Walk(package.Nodes.Nodes).Concat(Walk(package.Checks.Nodes))
            .Concat(package.Functions.Items.SelectMany(f => Walk(f.Body.Nodes)));

    /// <summary>The frames of <paramref name="package"/>: every body, and the body each node is in. A null argument is the caller's error.</summary>
    public static ScopeTree Build(SourcePackage package, PackageTypes types)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(types);

        var tree = new ScopeTree(package, types);
        tree.Body(tree.PackageRoot, package.Nodes.Nodes);
        tree.Body(tree.ChecksRoot, package.Checks.Nodes);
        foreach (var function in package.Functions.Items)
        {
            var root = new ScopeFrame(tree, null, function, null, function.Body.Nodes, inChecks: false);
            tree.functions.TryAdd(function, root);
            tree.Body(root, function.Body.Nodes);
        }

        var keys = new Dictionary<SourceNode, string>(ReferenceEqualityComparer.Instance);
        foreach (var node in tree.All())
            keys.TryAdd(node, node.Key);
        tree.Keys = keys;
        return tree;
    }

    /// <summary>The body <paramref name="node"/> is listed in; a node not of this package is the caller's error (<see cref="ArgumentException"/>).</summary>
    public ScopeFrame FrameOf(SourceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return frames.TryGetValue(node, out var frame) ? frame : throw new ArgumentException("The node is not of this package.", nameof(node));
    }

    /// <summary>The root of <paramref name="function"/>'s body; a function not of this package is the caller's error (<see cref="ArgumentException"/>).</summary>
    public ScopeFrame FunctionRoot(SourceFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return functions.TryGetValue(function, out var frame) ? frame : throw new ArgumentException("The function is not of this package.", nameof(function));
    }

    /// <summary>The (first) body of the flow node <paramref name="node"/>: where a <c>while</c>'s or an <c>until</c>'s rule is read; null for a leaf.</summary>
    internal ScopeFrame? BodyOf(SourceNode node) => bodies.GetValueOrDefault(node);

    /// <summary>
    /// What <paramref name="reference"/> names at <paramref name="frame"/>. <paramref name="subject"/> is the
    /// <c>&lt;site&gt;</c> of a sentence and <paramref name="path"/> the file it names; <paramref name="condition"/> is
    /// true for a JSON Logic read (a <c>var</c> or <c>missing</c> path) and false for a reference string (<c>in</c>,
    /// <c>case</c>, <c>with</c>): they resolve alike, and the caller tells them apart where a rule does (the edge a read
    /// makes, D7). <paramref name="reader"/> is the node that reads, whose id S04-050 and S04-051 name inside a function
    /// (when null, the id of the flow node whose body <paramref name="frame"/> is, else the function's).
    /// <list type="bullet">
    /// <item><c>node.&lt;id&gt;</c>, <c>check.&lt;id&gt;</c>: the visible node (<see cref="ScopeFrame.Visible"/>), a
    /// <see cref="ScopeHeadKind.Format"/> head of two with its format, or a <see cref="ScopeHeadKind.Value"/> head of three
    /// for <c>.state</c>, a loop's <c>.items</c>, <c>.stopped</c> and <c>.content</c> (<c>markdown_block</c>, #1507
    /// D23; a flow's whose <c>ret</c> chain reaches a <c>for</c> too, <see cref="RenderedContent"/>), a while's
    /// <c>.stopped</c>, <c>.passes</c> and <c>.history</c> (D14); a node hidden inside a loop is S04-039; inside a
    /// function, a check or a node outside it is S04-051; anything else is <see cref="ScopeHeadKind.Unknown"/>.</item>
    /// <item><c>input.&lt;id&gt;</c> and <c>run.&lt;fact&gt;</c>: as <see cref="PackageScope"/> gives them; inside a
    /// function S04-050 and S04-051.</item>
    /// <item><c>param.&lt;id&gt;</c>: a parameter of the enclosing function, a Format head of two; otherwise unknown.</item>
    /// <item><c>item</c>, <c>item.parent</c>, …: the loop's <c>.id</c> (a <c>string</c>), <c>.value</c> (the source's
    /// element, <see cref="Source"/>'s; <see cref="ScopeHeadKind.Deferred"/> when the source is refused), a snippet folder item's
    /// <c>.content</c>; over a loop's <c>.items</c>, the element of § 2.6 itself (a Value head ending at <c>item</c>). No
    /// loop: unknown, S04-040 for <c>item.parent</c> in an outermost loop, S04-051 inside a function.</item>
    /// <item>#1505 (area 08; D1, D2): <c>deliverable.&lt;id&gt;</c> under <c>checks/</c> only (elsewhere unknown; the
    /// expression checker says S03-019 first): the longest key that names a deliverable (<c>deliverable.emr.note</c>),
    /// then <c>.content</c> (a nullable <c>string</c>), <c>.value</c> (an object format's value, else the text; nullable)
    /// or <c>.state</c> (<c>released</c>, <c>held</c>, <c>skipped</c> or <c>blocked</c>), a Value head ending at the
    /// member; any other member, or none, is unknown.</item>
    /// <item><c>pass</c>, <c>pass.parent</c>, …: <c>.number</c>, <c>.history</c>, <c>.node.&lt;id&gt;</c> (read as
    /// <c>node.&lt;id&gt;</c> is, the id anywhere in the while; #1504: one hidden inside an inner loop is S04-039) and
    /// <c>.previous</c> (nullable); S04-036, S04-037, S04-038, S04-051 inside a function.</item>
    /// </list>
    /// A null argument is the caller's error.
    /// </summary>
    public Resolved Resolve(ReferenceString reference, ScopeFrame frame, string subject, string path, bool condition, SourceNode? reader = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(path);

        var read = new Read(reference, frame, subject, path, reader);
        var s = reference.Segments;
        if (s.Count == 0)
            return Unknown;

        switch (s[0])
        {
            case "node" or "check":
                return Named(read);
            case "input" when frame.Function is { } function:
                return s.Count < 2 ? Unknown : Refused(ResolveSentences.FunctionReadsInput(ReaderId(read), function.Id, s[1], path));
            case "run" when frame.Function != null:
                return Outside(read);
            case "input" or "run":
                return FromPackageScope(s);
            case "param":
                var parameter = s.Count < 2 ? null : frame.Function?.Inputs.FirstOrDefault(p => p.Id == s[1]);
                return parameter == null ? Unknown : Head(ScopeHeadKind.Format, $"param.{parameter.Id}", 2, parameter.Json["format"], false);
            case "item":
                return Item(read);
            case "pass":
                return Pass(read);
            case "deliverable" when frame.InChecks && frame.Function == null:
                return Deliverable(s);
            default:
                return Unknown;
        }
    }

    /// <summary>One call of <see cref="Resolve"/>: what is read, where, and who says it.</summary>
    private sealed record Read(ReferenceString Reference, ScopeFrame Frame, string Subject, string Path, SourceNode? Reader)
    {
        public IReadOnlyList<string> Segments => Reference.Segments;
    }

    private static readonly Resolved Unknown = new(ScopeHeadKind.Unknown, null, 0, null, false, null);

    private static readonly Resolved Deferred = new(ScopeHeadKind.Deferred, null, 0, null, false, null);

    private static Resolved Refused(SourceRefusal refusal) => new(ScopeHeadKind.Refused, null, 0, null, false, refusal);

    private static Resolved Head(ScopeHeadKind kind, string? key, int length, JsonNode? format, bool nullable) =>
        new(kind, key, length, format, nullable, null);

    /// <summary>S04-051, naming the reference as written.</summary>
    private static Resolved Outside(Read read) =>
        Refused(ResolveSentences.FunctionReadsOutside(ReaderId(read), read.Frame.Function!.Id, read.Reference.Text, read.Path));

    private static string ReaderId(Read read) => read.Reader?.Id ?? read.Frame.Owner?.Id ?? read.Frame.Function?.Id ?? "";

    /// <summary><c>input.&lt;id&gt;</c> and <c>run.&lt;fact&gt;</c> outside a function: <see cref="PackageScope"/>'s head, an input's with its key.</summary>
    private Resolved FromPackageScope(IReadOnlyList<string> s)
    {
        var head = outside.Head(s);
        var key = head.Kind == ScopeHeadKind.Format && s[0] == "input" ? $"input.{s[1]}" : null;
        return new Resolved(head.Kind, key, head.Length, head.Format, head.Nullable, head.Refusal);
    }

    // ---- node and check ------------------------------------------------------------------------

    private Resolved Named(Read read)
    {
        var s = read.Segments;
        var frame = read.Frame;
        if (s.Count < 2)
            return Unknown;
        if (s[0] == "check" && frame.Function != null)
            return Outside(read);

        if (frame.Visible(s[0], s[1]) is { } found)
            return Member(found, found.Key, s, 1, false);
        if (s[0] == "node" && HiddenFrom(frame, "node", s[1]) is { } hidden)
            return Refused(ResolveSentences.NodeInsideLoop(read.Subject, s[1], hidden.Loop.Id, read.Path));
        if (frame.Function != null && Walk(package.Nodes.Nodes).Any(n => n.Id == s[1]))
            return Outside(read);
        return Unknown;
    }

    /// <summary>
    /// The node <paramref name="node"/>, named by the segment at <paramref name="at"/>: a member of § 2.5 or § 3.4 that
    /// it has (a Value head ending at the member), else the node itself (a Format head ending at its id).
    /// </summary>
    private Resolved Member(SourceNode node, string? key, IReadOnlyList<string> s, int at, bool nullable)
    {
        var member = at + 1 < s.Count ? s[at + 1] : null;
        var length = at + 2;
        return (member, node) switch
        {
            ("state", _) => Head(ScopeHeadKind.Value, key, length, Enum(States), nullable),
            ("items", SourceFor loop) => Head(ScopeHeadKind.Value, key, length, FormatTypes.ListOf(Element(loop)), nullable),
            // #1507 (D23): a for loop's .content is the standard rendering, markdown_block whatever its items' format, and a
            // flow whose ret chain reaches a for holds that rendering (Task 24's fix round 1)
            ("content", _) when RenderedContent(node) is { } content =>
                Head(ScopeHeadKind.Value, key, length, content, nullable || !AlwaysValued(node)),
            ("stopped", SourceFor) => Head(ScopeHeadKind.Value, key, length, Enum(["until", "bound", "end"]), nullable),
            ("stopped", SourceWhile) => Head(ScopeHeadKind.Value, key, length, Enum(["check", "until", "bound"]), nullable),
            ("passes", SourceWhile) => Head(ScopeHeadKind.Value, key, length, Integer(), nullable),
            ("history", SourceWhile) => Head(ScopeHeadKind.Value, key, length, History(), nullable),
            _ => Head(ScopeHeadKind.Format, key, at + 1, FormatOf(node), nullable || !AlwaysValued(node)),
        };
    }

    /// <summary>
    /// The first node <paramref name="root"/><c>.</c><paramref name="id"/> in tree order of the reader's own tree (a
    /// function's, or <c>nodes/</c>, or <c>checks/</c>) that is not visible from <paramref name="frame"/>, with the
    /// outermost loop or while around it that does not enclose <paramref name="frame"/>.
    /// </summary>
    internal (SourceNode Node, SourceNode Loop)? HiddenFrom(ScopeFrame frame, string root, string id)
    {
        if (frame.Visible(root, id) != null || (root == "check" && frame.Function != null))
            return null;
        var top = frame.Function?.Body.Nodes ?? (root == "check" ? package.Checks.Nodes : package.Nodes.Nodes);

        var enclosing = new HashSet<SourceNode>(ReferenceEqualityComparer.Instance);
        for (var f = frame; f != null; f = f.Parent)
        {
            if (f.Owner != null)
                enclosing.Add(f.Owner);
        }

        foreach (var node in Walk(top).Where(n => n.Id == id))
        {
            SourceNode? loop = null;
            for (var f = FrameOf(node); f != null; f = f.Parent)
            {
                if (f.Owner is SourceFor or SourceWhile && !enclosing.Contains(f.Owner))
                    loop = f.Owner;
            }

            if (loop != null)
                return (node, loop);
        }

        return null;
    }

    // ---- deliverable ---------------------------------------------------------------------------

    /// <summary>D2's states of a deliverable a check reads (a <c>failed</c> one fails the job, so is never read).</summary>
    private static readonly string[] DeliverableStates = ["released", "held", "skipped", "blocked"];

    /// <summary>
    /// <c>deliverable.&lt;key path&gt;.&lt;member&gt;</c> (D2): the deliverable whose key is the longest prefix of the
    /// path that names one, read through <c>.content</c>, <c>.value</c> or <c>.state</c>.
    /// </summary>
    private Resolved Deliverable(IReadOnlyList<string> s)
    {
        for (var length = s.Count - 1; length >= 2; length--)
        {
            var key = string.Join('.', s.Take(length));
            if (Deliverables(package.Deliverables).FirstOrDefault(d => d.Key == key) is not { } deliverable)
                continue;
            var format = deliverable.Header["format"];
            var text = format is null || FormatTypes.TextKind(format, types.Formats) != null;
            return s[length] switch
            {
                "content" => Head(ScopeHeadKind.Value, key, length + 1, JsonValue.Create("string"), true),
                "value" => Head(ScopeHeadKind.Value, key, length + 1, text ? JsonValue.Create("string") : format!.DeepClone(), true),
                "state" => Head(ScopeHeadKind.Value, key, length + 1, Enum(DeliverableStates), false),
                _ => Unknown,
            };
        }

        return Unknown;
    }

    /// <summary>Every deliverable of <paramref name="folder"/> and its delivery folders, in folder order.</summary>
    internal static IEnumerable<SourceDeliverable> Deliverables(SourceDeliveryFolder folder) =>
        folder.Deliverables.Concat(folder.Folders.SelectMany(Deliverables));

    // ---- item ----------------------------------------------------------------------------------

    private Resolved Item(Read read)
    {
        var s = read.Segments;
        var frame = read.Frame;
        var up = 0;
        while (1 + up < s.Count && s[1 + up] == "parent")
            up++;

        if (frame.Loop(0) == null)
            return frame.Function != null ? Outside(read) : Unknown;
        if (frame.Loop(up) is not { } loop)
            return frame.Function != null ? Outside(read) : Refused(ResolveSentences.ItemParentOutsideLoop(read.Subject, read.Path));

        var at = 1 + up;
        var member = at < s.Count ? s[at] : null;
        if (member == "id")
            return Head(ScopeHeadKind.Value, null, at + 1, JsonValue.Create("string"), false);
        if (Source(loop) is not { Element: { } element } source)
            return Deferred;
        if (source.OverItems)
            return Head(ScopeHeadKind.Value, null, at, element, false);
        return member switch
        {
            "value" => Head(ScopeHeadKind.Value, null, at + 1, element, false),
            "content" when source.KnownLength != null => Head(ScopeHeadKind.Value, null, at + 1, JsonValue.Create("markdown_block"), false),
            null => Head(ScopeHeadKind.Format, null, at, element, false),
            _ => Unknown,
        };
    }

    // ---- pass ----------------------------------------------------------------------------------

    private Resolved Pass(Read read)
    {
        var s = read.Segments;
        var frame = read.Frame;
        var up = 0;
        while (1 + up < s.Count && s[1 + up] == "parent")
            up++;

        if (frame.While(0) == null)
        {
            if (frame.Function != null)
                return Outside(read);
            return s.Count > 1 && s[1] == "number" ? Refused(ResolveSentences.PassOutsideWhile(read.Subject, read.Path)) : Unknown;
        }

        if (frame.While(up) is not { } loop)
            return frame.Function != null ? Outside(read) : Refused(ResolveSentences.PassParentOutsideWhile(read.Subject, read.Path));

        var at = 1 + up;
        var previous = at < s.Count && s[at] == "previous";
        if (previous)
            at++;
        var member = at < s.Count ? s[at] : null;
        switch (member)
        {
            case "number":
                return Head(ScopeHeadKind.Value, null, at + 1, Integer(), previous);
            case "history" when !previous:
                return Head(ScopeHeadKind.Value, null, at + 1, History(), false);
            case "node" when at + 1 < s.Count:
                var id = s[at + 1];
                if (Walk(loop.Body.Nodes).FirstOrDefault(n => n.Id == id) is { } node)
                {
                    // #1504 (ruled by the owner, ruling 7): this pass's node is read as node.<id> is, so one inside an
                    // inner loop of the body that does not enclose the reader is a list from here (S04-039).
                    if (!previous && HiddenFrom(frame, "node", id) is { } hidden && ReferenceEquals(hidden.Node, node))
                        return Refused(ResolveSentences.NodeInsideLoop(read.Subject, id, hidden.Loop.Id, read.Path));
                    return Member(node, null, s, at + 1, previous);
                }

                return previous ? Refused(ResolveSentences.PassPreviousNotInWhile(read.Subject, id, loop.Id, read.Path)) : Unknown;
            default:
                return Unknown;
        }
    }

    // ---- Types ---------------------------------------------------------------------------------

    /// <summary>The formats a body's reads are typed against: its function's own set, else the package's.</summary>
    internal FormatSet FormatsOf(ScopeFrame frame) =>
        frame.Function != null && types.FunctionFormats.TryGetValue(frame.Function.Id, out var own) ? own : types.Formats;

    /// <summary>The node's declared format as the type rules read it (the model's own node: read-only).</summary>
    private JsonNode? FormatOf(SourceNode node) => types.NodeFormats.GetValueOrDefault(node.Path);

    /// <summary>What <c>.content</c> of a value of <paramref name="format"/> is (D10): a text format itself, otherwise plaintext.</summary>
    private static JsonNode ContentOf(JsonNode format, FormatSet formats) =>
        JsonValue.Create(FormatTypes.TextKind(format, formats) ?? "plaintext");

    /// <summary>
    /// #1507 (D23; part 04 § 2.5, part 05 § 5.3): what <c>.content</c> of a <c>for</c> loop is, whatever its items'
    /// format: the standard rendering (<c>## &lt;item name&gt;</c>, the item's text, joined by blank lines) that keeps
    /// 30's bytes, typed <c>markdown_block</c>. A <c>while</c>'s is its last pass's <c>ret</c>'s (§ 3): see
    /// <see cref="RenderedContent"/>.
    /// </summary>
    private static JsonNode LoopContent() => JsonValue.Create("markdown_block");

    /// <summary>
    /// #1507 (D23; Task 24's fix round 1): what <c>.content</c> of <paramref name="node"/> is when a <c>for</c> gives its
    /// text, else null (D10 gives it, from the node's format). A flow's <c>.content</c> is its <c>ret</c>'s (part 04 §§ 2.6,
    /// 3, 5), so the type follows the <c>ret</c> chain: a <c>for</c> is <see cref="LoopContent"/>; a <c>while</c> is its
    /// <c>ret</c>'s; an <c>if</c> or a <c>case</c> is the union of its branches' <c>ret</c>s' (D10's for a branch with no
    /// <c>for</c>), so branches that differ give a union a Markdown deliverable escapes (R10). Null when no <c>ret</c> on
    /// the chain is a <c>for</c>, and for a leaf. The chain only descends into bodies, so it ends.
    /// </summary>
    private JsonNode? RenderedContent(SourceNode node)
    {
        switch (node)
        {
            case SourceFor:
                return LoopContent();
            case SourceWhile loop:
                return RetOf(loop.Body) is { } ret ? RenderedContent(ret) : null;
            case SourceIf or SourceCase:
                var rets = Blocks(node).Select(RetOf).OfType<SourceNode>().ToList();
                var rendered = rets.Select(RenderedContent).ToList();
                if (rendered.All(r => r is null))
                    return null;
                var formats = FormatsOf(FrameOf(node));
                var members = rets
                    .Select((ret, i) => rendered[i] ?? (FormatOf(ret) is { } format ? ContentOf(format, formats) : null))
                    .OfType<JsonNode>()
                    .Select(Schema)
                    .ToList();
                return FormatTypes.UnionOf(members, formats);
            default:
                return null;
        }
    }

    /// <summary>The node <paramref name="block"/>'s <c>ret</c> names among its nodes, or null.</summary>
    private static SourceNode? RetOf(SourceBlock block) =>
        block.Ret is { } ret ? block.Nodes.FirstOrDefault(n => n.Key == ret) : null;

    private static JsonNode OrNull(JsonNode format, FormatSet formats) =>
        FormatTypes.UnionOf([Schema(format), new JsonObject { ["type"] = "null" }], formats)!;

    /// <summary><paramref name="format"/> as a schema to place in a new tree: a name as <c>{ "$ref": name }</c>, a schema cloned.</summary>
    private static JsonNode Schema(JsonNode format) =>
        FormatSet.NameOf(format) is { } name ? new JsonObject { ["$ref"] = name } : format.DeepClone();

    private static JsonObject Object(JsonObject properties) => new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray(properties.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray()),
        ["additionalProperties"] = false,
        ["properties"] = properties,
    };

    private static JsonObject Enum(IEnumerable<string> values) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) };

    private static JsonObject Integer() => new() { ["type"] = "integer" };

    /// <summary><c>.history</c> and <c>pass.history</c>: a list read only through <c>size</c> (D14), of passes whose fields are not typed.</summary>
    private static JsonObject History() => (JsonObject)FormatTypes.ListOf(Object(new JsonObject()));

    // ---- The walk ------------------------------------------------------------------------------

    private void Body(ScopeFrame frame, IReadOnlyList<SourceNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!frames.TryAdd(node, frame))
                continue;
            var first = true;
            foreach (var block in Blocks(node))
            {
                var body = new ScopeFrame(this, frame, frame.Function, node, block.Nodes, frame.InChecks);
                if (first)
                    bodies[node] = body;
                first = false;
                Body(body, block.Nodes);
            }
        }
    }

    /// <summary>Every node under <paramref name="nodes"/>, a flow node before its children.</summary>
    internal static IEnumerable<SourceNode> Walk(IReadOnlyList<SourceNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var block in Blocks(node))
                foreach (var inner in Walk(block.Nodes))
                    yield return inner;
        }
    }

    /// <summary>The bodies of a flow node, in written order (a loop's, an <c>if</c>'s then and else, a <c>case</c>'s branches then else); none for a leaf. The one such walk every step uses.</summary>
    internal static IEnumerable<SourceBlock> Blocks(SourceNode node) => node switch
    {
        SourceFor loop => [loop.Body],
        SourceWhile loop => [loop.Body],
        SourceIf branch => branch.Else == null ? [branch.Then] : [branch.Then, branch.Else],
        SourceCase choice => choice.Branches.Select(b => b.Body).Concat(choice.Else == null ? [] : [choice.Else]),
        _ => [],
    };
}
