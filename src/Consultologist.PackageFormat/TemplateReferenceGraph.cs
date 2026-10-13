namespace Consultologist.PackageFormat;

/// <summary>Which kind of template a graph entry is; prompts are sources only (nothing reads a prompt).</summary>
public enum TemplateKind
{
    Prompt,
    Snippet,
    Macro
}

/// <summary>
/// The derived reference graph over templates (package-format-v25-design.md
/// § 5.2): a template reads snippets (by id, or every item of a folder) and
/// macro texts; those are its edges here. Node edges are not in this graph —
/// a node's composed output already has its slots filled, and node cycles are
/// the manifest's acyclic check. Two questions are asked of it: is there a
/// cycle anywhere (snippet to snippet, macro text to snippet to macro), named
/// by its path; and does any macro's text reach a <c>macro.&lt;id&gt;.prompt</c>
/// read — the one transitive rule, since text placed after a reply has no
/// second call to honour a marker it would carry.
/// </summary>
public sealed class TemplateReferenceGraph
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _snippetFolders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _macroFolders = new(StringComparer.Ordinal);

    private sealed record Entry(TemplateKind Kind, string Id, TemplateReferences References)
    {
        public string Display => $"{Kind.ToString().ToLowerInvariant()} '{Id}'";
    }

    /// <summary>A folder's members, so a folder read edges to each of them.</summary>
    public void AddSnippetFolder(string folder, IReadOnlyList<string> snippetIds) => _snippetFolders[folder] = snippetIds;

    public void AddMacroFolder(string folder, IReadOnlyList<string> macroIds) => _macroFolders[folder] = macroIds;

    public void Add(TemplateKind kind, string id, TemplateReferences references)
    {
        var key = Key(kind, id);

        if (_entries.ContainsKey(key))
        {
            throw new ArgumentException($"{kind} '{id}' is already in the graph.", nameof(id));
        }

        _entries[key] = new Entry(kind, id, references);
        _order.Add(key);
    }

    private static string Key(TemplateKind kind, string id) => $"{kind}:{id}";

    /// <summary>The templates this entry reads, in reference order; unknown targets are skipped (the validator refuses them by name).</summary>
    private IEnumerable<string> EdgesOf(Entry entry)
    {
        var refs = entry.References;

        foreach (var snippet in refs.Snippets)
        {
            yield return Key(TemplateKind.Snippet, snippet);
        }

        foreach (var folder in refs.SnippetFolders)
        {
            if (_snippetFolders.TryGetValue(folder, out var members))
            {
                foreach (var member in members)
                {
                    yield return Key(TemplateKind.Snippet, member);
                }
            }
        }

        foreach (var macro in refs.MacroTexts)
        {
            yield return Key(TemplateKind.Macro, macro);
        }

        foreach (var folder in refs.MacroFolders)
        {
            if (_macroFolders.TryGetValue(folder, out var members))
            {
                foreach (var member in members)
                {
                    yield return Key(TemplateKind.Macro, member);
                }
            }
        }
    }

    /// <summary>
    /// Depth-first, in insertion order, so the first cycle found is the same
    /// every time; <paramref name="path"/> names it from its first template
    /// back to itself: ["snippet 'a'", "macro 'b'", "snippet 'a'"].
    /// </summary>
    public bool TryFindCycle(out IReadOnlyList<string> path)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 = on the stack, 2 = done
        var stack = new List<string>();

        foreach (var start in _order)
        {
            if (Visit(start, out var found))
            {
                path = found;
                return true;
            }
        }

        path = Array.Empty<string>();
        return false;

        bool Visit(string key, out IReadOnlyList<string> found)
        {
            found = Array.Empty<string>();

            if (state.TryGetValue(key, out var s))
            {
                if (s == 1)
                {
                    var from = stack.IndexOf(key);
                    found = stack.Skip(from).Append(key).Select(k => _entries[k].Display).ToList();
                    return true;
                }

                return false;
            }

            if (!_entries.TryGetValue(key, out var entry))
            {
                return false;
            }

            state[key] = 1;
            stack.Add(key);

            foreach (var next in EdgesOf(entry))
            {
                if (Visit(next, out found))
                {
                    return true;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[key] = 2;
            return false;
        }
    }

    /// <summary>
    /// The transitive rule: from every macro's text, over read edges, no
    /// template on the way — the macro's own text included — may read a
    /// <c>macro.&lt;id&gt;.prompt</c>. <paramref name="path"/> runs from the
    /// macro to the template holding the read; <paramref name="promptMacroId"/>
    /// is the macro whose prompt it reads.
    /// </summary>
    public bool TryFindMacroTextReachingPrompt(out IReadOnlyList<string> path, out string promptMacroId)
    {
        foreach (var start in _order.Where(k => _entries[k].Kind == TemplateKind.Macro))
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stack = new List<string>();

            if (Visit(start, visited, stack, out path, out promptMacroId))
            {
                return true;
            }
        }

        path = Array.Empty<string>();
        promptMacroId = string.Empty;
        return false;

        bool Visit(string key, HashSet<string> visited, List<string> stack, out IReadOnlyList<string> found, out string prompt)
        {
            found = Array.Empty<string>();
            prompt = string.Empty;

            if (!visited.Add(key) || !_entries.TryGetValue(key, out var entry))
            {
                return false;
            }

            stack.Add(key);

            if (entry.References.MacroPrompts.Count > 0)
            {
                found = stack.Select(k => _entries[k].Display).ToList();
                prompt = entry.References.MacroPrompts.First();
                return true;
            }

            foreach (var next in EdgesOf(entry))
            {
                if (Visit(next, visited, stack, out found, out prompt))
                {
                    return true;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            return false;
        }
    }

    /// <summary>"snippet 'a' → macro 'b' → snippet 'a'"</summary>
    public static string Describe(IReadOnlyList<string> path) => string.Join(" → ", path);
}
