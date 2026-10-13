namespace Consultologist.PackageFormat;

/// <summary>
/// What one template reads and depends on (package-format-v25-design.md § 2),
/// derived from its syntax tree: the inputs, snippets, macros and item members
/// it reads; the nodes it reads — the template's **edges**; the macros whose
/// <c>.prompt</c> it reads — its inline slots; and the sentences that refuse
/// it. Edges inside a conditional branch count the same: conservative, stated.
/// </summary>
public sealed record TemplateReferences(
    IReadOnlySet<string> Inputs,
    IReadOnlyDictionary<string, TemplateNodeRead> Nodes,
    IReadOnlySet<string> Snippets,
    IReadOnlySet<string> SnippetFolders,
    IReadOnlySet<string> MacroTexts,
    IReadOnlySet<string> MacroPrompts,
    IReadOnlySet<string> MacroFolders,
    IReadOnlySet<string> ItemMembers,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;

    /// <summary>The node ids this template depends on, in first-read order.</summary>
    public IEnumerable<string> Edges => Nodes.Keys;
}

/// <summary>How a template reads a node: its composed text, its parsed value, or both.</summary>
public sealed record TemplateNodeRead(bool ReadsText, bool ReadsValue)
{
    public TemplateNodeRead Merge(TemplateNodeRead other) =>
        new(ReadsText || other.ReadsText, ReadsValue || other.ReadsValue);
}

/// <summary>Gathers a template's references while the walker runs; <see cref="ToReferences"/> freezes them.</summary>
internal sealed class TemplateReferenceCollector
{
    private readonly HashSet<string> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TemplateNodeRead> _nodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _snippets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _snippetFolders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _macroTexts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _macroPrompts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _macroFolders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _itemMembers = new(StringComparer.Ordinal);

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();

    public void ReadInput(string id) => _inputs.Add(id);

    public void ReadNode(string id, bool text, bool value)
    {
        var read = new TemplateNodeRead(text, value);
        _nodes[id] = _nodes.TryGetValue(id, out var existing) ? existing.Merge(read) : read;
    }

    public void ReadSnippet(string id) => _snippets.Add(id);

    public void ReadSnippetFolder(string folder) => _snippetFolders.Add(folder);

    public void ReadMacroText(string id) => _macroTexts.Add(id);

    public void ReadMacroPrompt(string id) => _macroPrompts.Add(id);

    public void ReadMacroFolder(string folder) => _macroFolders.Add(folder);

    public void ReadItem(string member) => _itemMembers.Add(member);

    public TemplateReferences ToReferences() => new(
        _inputs, _nodes, _snippets, _snippetFolders, _macroTexts, _macroPrompts, _macroFolders, _itemMembers,
        Errors, Warnings);
}
