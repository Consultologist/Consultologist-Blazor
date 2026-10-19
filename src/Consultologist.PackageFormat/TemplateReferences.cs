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
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;

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
/// rows from it. The set is unordered.
/// </summary>
public sealed record TemplateRenderedRead(string Node, string Pipe);

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

    public void ReadRunFact(string member) => _runFacts.Add(member);

    public void ReadProfileFact(string member) => _profileFacts.Add(member);

    public void ReadRendered(string node, string pipe) => _rendered.Add(new TemplateRenderedRead(node, pipe));

    public TemplateReferences ToReferences() => new(
        _inputs, _nodes, _snippets, _snippetFolders, _macroContents, _macroPrompts, _macroFolders, _itemMembers,
        _runFacts, _profileFacts, _rendered, Errors, Warnings);
}
