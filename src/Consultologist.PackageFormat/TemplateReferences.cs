namespace Consultologist.PackageFormat;

/// <summary>
/// What one template reads and depends on (package-format-v25-design.md § 2),
/// derived from its syntax tree: the inputs, snippets, macros and item members
/// it reads; the nodes it reads — the template's **edges**; the macros whose
/// <c>.prompt</c> it reads — its inline macros; the renderer pipes that rendered
/// a node's text — <see cref="Rendered"/>, for the editor's "Used by" rows (#1098);
/// and the sentences that refuse it. Edges inside a conditional branch count the
/// same: conservative, stated.
/// </summary>
public sealed record TemplateReferences(
    IReadOnlySet<string> Inputs,
    IReadOnlyDictionary<string, TemplateNodeRead> Nodes,
    IReadOnlySet<string> Snippets,
    IReadOnlySet<string> SnippetFolders,
    IReadOnlySet<string> MacroContents,
    IReadOnlySet<string> MacroPrompts,
    IReadOnlySet<string> MacroFolders,
    IReadOnlySet<string> ItemMembers,
    IReadOnlySet<string> RunFacts,
    IReadOnlySet<string> ProfileFacts,
    IReadOnlySet<TemplateRenderedRead> Rendered,
    IReadOnlyList<TemplateReadOccurrence> Occurrences,
    IReadOnlyList<TemplateBranchBlock> Branches,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    // #1101 (v26 design § 3.1): the deliverables whose composed text this
    // template reads — an edge on each one's sources (the walk expands it).
    IReadOnlySet<string>? Deliverables = null)
{
    public bool IsValid => Errors.Count == 0;

    /// <summary>#1101: the deliverable ids read, never null.</summary>
    public IReadOnlySet<string> DeliverableReads => Deliverables ?? TemplateReferenceContext.NoIds;

    /// <summary>The node ids this template depends on, in first-read order.</summary>
    public IEnumerable<string> Edges => Nodes.Keys;
}

/// <summary>How a template reads a node: its composed text, its parsed value, or both.</summary>
public sealed record TemplateNodeRead(bool ReadsContent, bool ReadsValue)
{
    public TemplateNodeRead Merge(TemplateNodeRead other) =>
        new(ReadsContent || other.ReadsContent, ReadsValue || other.ReadsValue);
}

/// <summary>
/// #1098: one renderer pipe applied to one node's text — <c>node.&lt;Node&gt;.content | &lt;Pipe&gt;</c> —
/// recorded only when the walk accepts the operand (a refused pipe names its edge, not its
/// rendering; an operand the walk cannot type is judged when it renders and records nothing).
/// The validator and the engine read nothing from it; the editor lists a renderer's "Used by"
/// rows from it. The set is unordered. #1138: <see cref="Guard"/> and <see cref="GuardCanonical"/>
/// are the enclosing <c>if</c> as <see cref="TemplateReadOccurrence"/> records it for a plain read —
/// the condition as written (null when none, or in a case's branch) and whether that <c>if</c> holds
/// only this piped read plus whitespace (no else, not an else-if). Record equality includes them:
/// one node piped under two ifs is two entries.
/// </summary>
public sealed record TemplateRenderedRead(string Node, string Pipe, string? Guard = null, bool GuardCanonical = false);

/// <summary>
/// #1088: one read as written — a statement that is one plain object chain
/// (<c>{{ macro.x.prompt }}</c>, <c>{{ snippet.y.content }}</c>, <c>{{ node.z.value }}</c>) — with
/// where it sits and the <c>if</c> around it. <see cref="Member"/> is the chain's words joined with
/// dots; <c>[Start, End)</c> is the whole <c>{{ … }}</c> block in the text. <see cref="Guard"/> is
/// the nearest enclosing <c>if</c>'s condition as written (null when none);
/// <see cref="GuardCanonical"/> says that <c>if</c> is the read's own parent, has no <c>else</c>,
/// is not an else-if and holds only this read plus whitespace — the shape the editor's row
/// rewrites — and <c>[GuardStart, GuardEnd)</c> is the whole <c>{{ if … }}…{{ end }}</c> block
/// (−1 when unguarded). The validator and the engine read nothing from it; the editor's read
/// rows set a read's guard from it. In text order.
/// </summary>
public sealed record TemplateReadOccurrence(string Member, int Start, int End, string? Guard, bool GuardCanonical, int GuardStart, int GuardEnd, int Branch = -1);

/// <summary>#1091: one branch of a <c>case</c> — the <c>when</c> literal (null for the <c>else</c>) and the one read it holds (null when empty).</summary>
public sealed record TemplateBranch(string? Literal, string? Member);

/// <summary>
/// #1091: a <c>{{ case … }}</c> block as written — its subject chain (<c>input.kind</c>,
/// <c>node.x.value</c>), its branches in text order, whether the shape is <b>canonical</b>
/// (the subject is a declared enum, every <c>when</c> one string literal among the enum's
/// values, each body at most one plain read plus whitespace, the <c>else</c> last) — the
/// shape the editor's row rewrites — and <c>[Start, End)</c>, the whole block in the text.
/// A read in a branch is a <see cref="TemplateReadOccurrence"/> whose <c>Branch</c> indexes
/// this list. The validator and the engine read nothing from it; the editor's read rows
/// show and rewrite a case from it.
/// </summary>
public sealed record TemplateBranchBlock(string Subject, IReadOnlyList<TemplateBranch> Branches, bool Canonical, int Start, int End, TemplateBranchKind Kind = TemplateBranchKind.Case);

/// <summary>
/// #1121: what a <see cref="TemplateBranchBlock"/> was written as. A <c>case</c> (#1091) chooses by an enum's
/// value; an <c>if … else</c> chooses by a condition — its <c>Subject</c> is the condition text and its two
/// branches are <c>(condition, the then-read)</c> and <c>(null, the else-read)</c>, either read possibly
/// absent but not both. An <c>if … else</c> is a block only in that canonical shape (a top-level <c>if</c>,
/// a plain <c>else</c>, each body at most one plain read plus whitespace); any other <c>if</c> stays a guard
/// on its reads' occurrences.
/// </summary>
public enum TemplateBranchKind
{
    Case,
    If,
}

/// <summary>Gathers a template's references while the walker runs; <see cref="ToReferences"/> freezes them.</summary>
internal sealed class TemplateReferenceCollector
{
    private readonly HashSet<string> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TemplateNodeRead> _nodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _snippets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _snippetFolders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _macroContents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _macroPrompts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _macroFolders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _itemMembers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _runFacts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _profileFacts = new(StringComparer.Ordinal);
    private readonly HashSet<TemplateRenderedRead> _rendered = new();
    private readonly HashSet<string> _deliverables = new(StringComparer.Ordinal);
    private readonly List<TemplateReadOccurrence> _occurrences = new();
    private readonly List<TemplateBranchBlock?> _branches = new();

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();

    public void ReadInput(string id) => _inputs.Add(id);

    public void ReadNode(string id, bool content, bool value)
    {
        var read = new TemplateNodeRead(content, value);
        _nodes[id] = _nodes.TryGetValue(id, out var existing) ? existing.Merge(read) : read;
    }

    public void ReadSnippet(string id) => _snippets.Add(id);

    public void ReadSnippetFolder(string folder) => _snippetFolders.Add(folder);

    public void ReadMacroContent(string id) => _macroContents.Add(id);

    public void ReadMacroPrompt(string id) => _macroPrompts.Add(id);

    public void ReadMacroFolder(string folder) => _macroFolders.Add(folder);

    public void ReadItem(string member) => _itemMembers.Add(member);

    public void ReadDeliverable(string id) => _deliverables.Add(id);

    public void ReadRunFact(string member) => _runFacts.Add(member);

    public void ReadProfileFact(string member) => _profileFacts.Add(member);

    public void ReadRendered(string node, string pipe, string? guard = null, bool guardCanonical = false) =>
        _rendered.Add(new TemplateRenderedRead(node, pipe, guard, guardCanonical));

    public void ReadOccurrence(TemplateReadOccurrence occurrence) => _occurrences.Add(occurrence);

    /// <summary>#1091: a case's slot is reserved on entry (its reads index it) and filled on exit.</summary>
    public int ReserveBranchBlock()
    {
        _branches.Add(null);
        return _branches.Count - 1;
    }

    public void SetBranchBlock(int index, TemplateBranchBlock block) => _branches[index] = block;

    public TemplateReferences ToReferences() => new(
        _inputs, _nodes, _snippets, _snippetFolders, _macroContents, _macroPrompts, _macroFolders, _itemMembers,
        _runFacts, _profileFacts, _rendered, _occurrences, _branches.Where(b => b is not null).Cast<TemplateBranchBlock>().ToList(), Errors, Warnings,
        _deliverables);
}
