using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Scriban.Syntax;

namespace Consultologist.PackageFormat;

/// <summary>What a <see cref="WorkflowTextRename"/> renames.</summary>
public enum WorkflowRenameKind
{
    Macro,
    Snippet,
    MacroFolder,
    SnippetFolder,
    MacroFolderItem,
    SnippetFolderItem,
}

/// <summary>
/// #1290: a macro, a snippet, a folder or a folder item renamed everywhere a 26–28 package names
/// it — by token, never by text search, as <see cref="WorkflowNodeRename"/> renames a node. A
/// template's reads (<c>macro.&lt;id&gt;.…</c>, <c>snippet.&lt;id&gt;.…</c>, a folder iterated or read
/// directly, <c>&lt;folder&gt;.&lt;item&gt;</c>) are rewritten in every text; the manifest's table
/// (the 26/27 map or the 28 list of ids), the node and deliverable macro instances and a loop's
/// <c>for</c> follow; a folder's index follows an item. An item id compared as a string literal
/// (<c>item.id == "hpi"</c>, <c>s.id == "hpi"</c>, <c>e.item.id == "hpi"</c>) is renamed only where the
/// item provably ranges over that folder — two folders may hold one item id. The file moves to the
/// path the new id implies at every version (decided 2026-11-03): a macro or snippet file, a
/// folder's directory, an item file. A rename that cannot be made refuses whole, naming why.
/// </summary>
public static class WorkflowTextRename
{
    private static readonly JsonSerializerOptions IndexWrite = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The rename over a manifest's JSON and its files, in the migrations' result shape; nothing applies on a refusal.</summary>
    public static Migration26Result Rename(string manifestJson, IReadOnlyDictionary<string, string> files, WorkflowRenameKind kind, string from, string to, string? folder = null)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(manifestJson)?.AsObject() ?? throw new JsonException("The manifest is not a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new Migration26Result(manifestJson, new Dictionary<string, string>(files, StringComparer.Ordinal), Array.Empty<string>(), new[] { $"The manifest does not read as a package: {ex.Message}" });
        }

        var outFiles = new Dictionary<string, string>(files, StringComparer.Ordinal);
        var (notices, refusals) = Apply(root, outFiles, kind, from, to, folder);
        return refusals.Count > 0
            ? new Migration26Result(manifestJson, new Dictionary<string, string>(files, StringComparer.Ordinal), Array.Empty<string>(), refusals)
            : new Migration26Result(root.ToJsonString(WorkflowPackageManifestJson.WriteOptions), outFiles, notices, Array.Empty<string>());
    }

    /// <summary>
    /// The rename applied in place to a manifest tree and its files (the editor's shape). On a
    /// refusal nothing is changed and the sentences say why.
    /// </summary>
    public static (IReadOnlyList<string> Notices, IReadOnlyList<string> Refusals) Apply(JsonObject root, IDictionary<string, string> files, WorkflowRenameKind kind, string from, string to, string? folder = null)
    {
        var plan = Plan(root, files, kind, from, to, folder);
        if (plan.Refusals.Count > 0)
        {
            return (Array.Empty<string>(), plan.Refusals);
        }

        var notices = new List<string>();
        var rewritten = 0;

        // ----- the texts: every template file's body and header if -----
        var context = ItemContext(root, files, kind, folder ?? from, from);
        foreach (var path in files.Keys.Where(p => p.EndsWith(".md", StringComparison.Ordinal)).ToList())
        {
            var itemRoots = context.ItemFiles.Contains(path) ? ItemRoot : NoRoots;
            if (WorkflowTemplateTokens.RewriteFile(files[path], text => RenameInTemplate(text, kind, from, to, folder, itemRoots, context.Loops)) is (var renamed, true))
            {
                files[path] = renamed;
                rewritten++;
            }
        }

        // ----- the manifest -----
        var table = TableKey(kind);
        var directory = DirectoryOf(kind);
        if (IsFolderItem(kind))
        {
            RenameInIndex(files, plan.Directory!, root, from, to);
        }
        else
        {
            RenameInTable(root, table, directory, from, to, IsFolder(kind));
        }

        if (kind == WorkflowRenameKind.Macro)
        {
            foreach (var owner in Owners(root))
            {
                RenameInstances(owner, from, to);
            }
        }

        if (kind == WorkflowRenameKind.SnippetFolder && root["nodes"] is JsonArray loops)
        {
            foreach (var node in loops.OfType<JsonObject>())
            {
                if (node["for"] is JsonValue v && v.TryGetValue<string>(out var forText) && LoopOver(forText) == from)
                {
                    node["for"] = $"item in snippet.{to}";
                }
            }
        }

        // An item id compared in a gate, where the item ranges over the folder.
        foreach (var (owner, key) in context.Gates)
        {
            if (owner[key] is JsonValue g && g.TryGetValue<string>(out var gate)
                && WorkflowTemplateTokens.RewriteCondition(gate, text => RenameInTemplate(text, kind, from, to, folder, ItemRoot, context.Loops)) is (var renamedGate, true))
            {
                owner[key] = renamedGate;
            }
        }

        // ----- the file or the directory follows the id -----
        foreach (var (source, target) in plan.Moves)
        {
            var text = files[source];
            files.Remove(source);
            files[target] = text;
        }

        var noun = Noun(kind);
        var where = IsFolderItem(kind) ? $" in folder '{folder}'" : string.Empty;
        notices.Add($"Renamed {noun} '{from}'{where} to '{to}'.");
        if (plan.Moves.Count > 0)
        {
            notices.Add(IsFolder(kind)
                ? $"{directory}/{from}/ → {directory}/{to}/ ({plan.Moves.Count} file{(plan.Moves.Count == 1 ? "" : "s")})."
                : $"{plan.Moves[0].From} → {plan.Moves[0].To}.");
        }

        if (rewritten > 0)
        {
            notices.Add($"{rewritten} text{(rewritten == 1 ? "" : "s")} read it and now read '{to}'.");
        }

        return (notices, Array.Empty<string>());
    }

    // ----- the plan: what exists, what is refused, what moves -----

    private sealed record RenamePlan(IReadOnlyList<string> Refusals, IReadOnlyList<(string From, string To)> Moves, string? Directory);

    private static RenamePlan Plan(JsonObject root, IDictionary<string, string> files, WorkflowRenameKind kind, string from, string to, string? folder)
    {
        var refusals = new List<string>();
        var moves = new List<(string From, string To)>();
        var noun = Noun(kind);

        if (from == to)
        {
            refusals.Add($"The {noun} is already called '{to}'.");
            return new RenamePlan(refusals, moves, null);
        }

        if (!WorkflowDeclaredIds.IsValid(to))
        {
            refusals.Add($"'{to}' is not a valid id — a lowercase letter, then lowercase letters, digits or underscores (a template reads it as a Scriban member).");
            return new RenamePlan(refusals, moves, null);
        }

        var directory = DirectoryOf(kind);
        var entries = TableEntries(root, TableKey(kind));

        if (IsFolderItem(kind))
        {
            var dir = folder is null ? null : entries.Where(e => e.Id == folder && e.Path.EndsWith('/')).Select(e => e.Path).FirstOrDefault();
            if (dir is null)
            {
                refusals.Add($"There is no {(kind == WorkflowRenameKind.MacroFolderItem ? "macro" : "snippet")} folder '{folder}'.");
                return new RenamePlan(refusals, moves, null);
            }

            var version = SpecVersion(root);
            var index = files.TryGetValue(dir + WorkflowDataResolver.IndexFileName, out var indexJson)
                ? WorkflowTextHeaders.ReadIndex(indexJson, folder, "Snippet", version, new List<string>())
                : null;
            if (index is null)
            {
                refusals.Add($"Folder '{folder}' has no readable index ({dir}{WorkflowDataResolver.IndexFileName}).");
                return new RenamePlan(refusals, moves, dir);
            }

            if (index.Items.FirstOrDefault(i => i.Id == from) is not { File: { } file })
            {
                refusals.Add($"Folder '{folder}' has no item '{from}'.");
                return new RenamePlan(refusals, moves, dir);
            }

            if (index.Items.Any(i => i.Id == to))
            {
                refusals.Add($"Folder '{folder}' already has an item '{to}'.");
                return new RenamePlan(refusals, moves, dir);
            }

            var source = dir + file;
            var target = dir + to + ".md";
            if (files.ContainsKey(target) && target != source)
            {
                refusals.Add($"A file '{target}' already exists; rename or remove it first.");
            }
            else if (files.ContainsKey(source) && source != target)
            {
                moves.Add((source, target));
            }

            return new RenamePlan(refusals, moves, dir);
        }

        var found = entries.Where(e => e.Id == from && e.Path.EndsWith('/') == IsFolder(kind)).Select(e => e.Path).FirstOrDefault();
        if (found is not { } path)
        {
            refusals.Add($"There is no {noun} '{from}'.");
            return new RenamePlan(refusals, moves, null);
        }

        if (entries.Any(e => e.Id == to))
        {
            refusals.Add($"{(kind is WorkflowRenameKind.Macro or WorkflowRenameKind.MacroFolder ? "A macro or macro folder" : "A snippet or snippet folder")} '{to}' already exists.");
            return new RenamePlan(refusals, moves, null);
        }

        if (IsFolder(kind))
        {
            var newDirectory = $"{directory}/{to}/";
            if (files.Keys.Any(p => p.StartsWith(newDirectory, StringComparison.Ordinal)))
            {
                refusals.Add($"The folder '{newDirectory}' already holds files; rename or remove them first.");
                return new RenamePlan(refusals, moves, path);
            }

            foreach (var file in files.Keys.Where(p => p.StartsWith(path, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                moves.Add((file, newDirectory + file[path.Length..]));
            }

            return new RenamePlan(refusals, moves, path);
        }

        var implied = $"{directory}/{to}.md";
        if (files.ContainsKey(implied) && implied != path)
        {
            refusals.Add($"A file '{implied}' already exists; rename or remove it first.");
        }
        else if (files.ContainsKey(path) && path != implied)
        {
            moves.Add((path, implied));
        }

        return new RenamePlan(refusals, moves, null);
    }

    // ----- the template rewrite -----

    private static readonly IReadOnlySet<string> NoRoots = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ItemRoot = new HashSet<string>(StringComparer.Ordinal) { "item" };

    /// <summary>
    /// A template with every token naming the macro, snippet, folder or item renamed. <paramref name="itemRoots"/>
    /// are the root variables that range over the folder in this text (<c>item</c> in a loop body's
    /// prompt, a gate under the loop, the folder's own item file); <paramref name="loops"/> the loop
    /// nodes over the folder, whose elements' <c>.item.id</c> name its items.
    /// </summary>
    public static (string Text, bool Changed, bool Parsed) RenameInTemplate(string text, WorkflowRenameKind kind, string from, string to, string? folder = null,
        IReadOnlySet<string>? itemRoots = null, IReadOnlySet<string>? loops = null)
    {
        if (string.IsNullOrEmpty(text) || from == to || !text.Contains(from, StringComparison.Ordinal))
        {
            return (text, false, true);
        }

        return WorkflowTemplateTokens.Rewrite(text, to, source => new Renamer(source, kind, from, folder, itemRoots ?? NoRoots, loops ?? NoRoots));
    }

    private sealed class Renamer : WorkflowTemplateTokens.EditingVisitor
    {
        private readonly WorkflowRenameKind _kind;
        private readonly string _root;
        private readonly string? _folder;
        private readonly IReadOnlySet<string> _itemRoots;
        private readonly IReadOnlySet<string> _loops;
        private readonly List<(HashSet<string> Items, HashSet<string> Elements)> _scopes = new();

        public Renamer(string text, WorkflowRenameKind kind, string oldId, string? folder, IReadOnlySet<string> itemRoots, IReadOnlySet<string> loops) : base(text, oldId)
        {
            _kind = kind;
            _root = kind is WorkflowRenameKind.Macro or WorkflowRenameKind.MacroFolder or WorkflowRenameKind.MacroFolderItem ? "macro" : "snippet";
            _folder = folder;
            _itemRoots = itemRoots;
            _loops = loops;
        }

        public override void Visit(ScriptMemberExpression node)
        {
            Consider(node);
            base.Visit(node);
        }

        public override void Visit(ScriptIndexerExpression node)
        {
            Consider(node);
            base.Visit(node);
        }

        public override void Visit(ScriptBinaryExpression node)
        {
            // An item id compared with a string literal, where the chain names an item of the folder.
            if (IsFolderItem(_kind) && node.Operator is ScriptBinaryOperator.CompareEqual or ScriptBinaryOperator.CompareNotEqual)
            {
                if (node.Right is ScriptLiteral right && NamesAnItemId(node.Left))
                {
                    TakeLiteral(right);
                }
                else if (node.Left is ScriptLiteral left && NamesAnItemId(node.Right))
                {
                    TakeLiteral(left);
                }
            }

            base.Visit(node);
        }

        public override void Visit(ScriptForStatement node)
        {
            Visit(node.Iterator);
            var items = new HashSet<string>(StringComparer.Ordinal);
            var elements = new HashSet<string>(StringComparer.Ordinal);
            if (node.Variable is ScriptVariable variable && WorkflowTemplateTokens.Flatten(node.Iterator) is { Segments.Count: 1 } chain && chain.Segments[0].Name is { } name)
            {
                var folderOfItem = IsFolderItem(_kind) ? _folder : null;
                // for x in <root>.<folder>: x is an item of the folder.
                if (chain.Root == _root && name == folderOfItem)
                {
                    items.Add(variable.Name);
                }

                // for e in node.<loop over the folder>: e.item is an item of the folder.
                if (chain.Root == "node" && _loops.Contains(name))
                {
                    elements.Add(variable.Name);
                }
            }

            _scopes.Add((items, elements));
            Visit(node.Body);
            _scopes.RemoveAt(_scopes.Count - 1);
            Visit(node.Else);
        }

        private bool IsItemVariable(string name) => _itemRoots.Contains(name) || _scopes.Any(s => s.Items.Contains(name));

        private bool IsElementVariable(string name) => _scopes.Any(s => s.Elements.Contains(name));

        /// <summary><c>&lt;x&gt;.id</c> for an item variable, <c>&lt;e&gt;.item.id</c> for a loop element, <c>node.&lt;loop&gt;[i].item.id</c>.</summary>
        private bool NamesAnItemId(ScriptExpression? expression)
        {
            if (WorkflowTemplateTokens.Flatten(expression) is not { } chain)
            {
                return false;
            }

            var names = chain.Segments.Select(s => s.Name).ToList();
            if (IsItemVariable(chain.Root) && names is ["id"])
            {
                return true;
            }

            if (IsElementVariable(chain.Root) && names is ["item", "id"])
            {
                return true;
            }

            return chain.Root == "node" && names.Count == 4 && names[0] is { } loop && _loops.Contains(loop) && names[1] is null && names[2] == "item" && names[3] == "id";
        }

        private void Consider(ScriptExpression expression)
        {
            if (WorkflowTemplateTokens.Flatten(expression) is not { } chain || chain.Root != _root || chain.Segments.Count == 0)
            {
                return;
            }

            var segments = chain.Segments;
            if (IsFolderItem(_kind))
            {
                // <root>.<folder>.<item>… — the direct read of an item (#1116).
                if (segments.Count >= 2 && segments[0].Name == _folder)
                {
                    Take(segments[1]);
                }

                return;
            }

            // <root>.<id>… — a macro, a snippet or a folder read, iterated or piped.
            Take(segments[0]);
        }
    }

    // ----- where an item ranges over the folder -----

    private sealed record ItemScope(IReadOnlySet<string> ItemFiles, IReadOnlySet<string> Loops, IReadOnlyList<(JsonObject Owner, string Key)> Gates);

    /// <summary>
    /// For an item rename: the loops over the folder, the texts in which <c>item</c> is one of its
    /// items (each loop body node's prompt, the folder's own item files) and the gates read under
    /// those loops (a body node's macro instance <c>if</c>, a flow node's <c>if</c> inside the loop).
    /// Only snippet folders are iterated by a <c>for</c>, so a macro folder's items have none.
    /// </summary>
    private static ItemScope ItemContext(JsonObject root, IDictionary<string, string> files, WorkflowRenameKind kind, string folder, string from)
    {
        var empty = new ItemScope(new HashSet<string>(), new HashSet<string>(), Array.Empty<(JsonObject, string)>());
        if (!IsFolderItem(kind))
        {
            return empty;
        }

        var itemFiles = new HashSet<string>(StringComparer.Ordinal);
        var loops = new HashSet<string>(StringComparer.Ordinal);
        var gates = new List<(JsonObject, string)>();
        var directory = TableEntries(root, TableKey(kind)).FirstOrDefault(e => e.Id == folder).Path;
        if (directory is not null)
        {
            foreach (var path in files.Keys.Where(p => p.StartsWith(directory, StringComparison.Ordinal) && p.EndsWith(".md", StringComparison.Ordinal)))
            {
                itemFiles.Add(path);
            }
        }

        if (kind != WorkflowRenameKind.SnippetFolderItem || root["nodes"] is not JsonArray nodes)
        {
            return new ItemScope(itemFiles, loops, gates);
        }

        var byId = nodes.OfType<JsonObject>()
            .Where(n => n["id"] is JsonValue)
            .GroupBy(n => n["id"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var version = SpecVersion(root);

        foreach (var loop in nodes.OfType<JsonObject>())
        {
            if (loop["for"] is not JsonValue f || !f.TryGetValue<string>(out var forText) || LoopOver(forText) != folder || loop["id"]?.GetValue<string>() is not { } loopId)
            {
                continue;
            }

            loops.Add(loopId);
            var body = Ids(loop["nodes"]).ToList();
            foreach (var bodyId in body.ToList())
            {
                if (byId.TryGetValue(bodyId, out var flow))
                {
                    // 27: a flow node in the body — its condition and its branches' nodes run per item.
                    if (flow["if"] is not null)
                    {
                        gates.Add((flow, "if"));
                    }

                    body.AddRange(Ids((flow["then"] as JsonObject)?["nodes"]).Concat(Ids((flow["else"] as JsonObject)?["nodes"])));
                    if (flow["branches"] is JsonArray branches)
                    {
                        body.AddRange(branches.OfType<JsonObject>().SelectMany(b => Ids(b["nodes"])));
                    }
                }
            }

            foreach (var bodyId in body.Distinct(StringComparer.Ordinal))
            {
                if (!byId.TryGetValue(bodyId, out var node))
                {
                    continue;
                }

                var prompt = node["prompt"] is JsonValue p && p.TryGetValue<string>(out var path) ? path : version >= 28 ? WorkflowImpliedPaths.PromptOf(bodyId) : null;
                if (prompt is not null)
                {
                    itemFiles.Add(prompt);
                }

                if (node["macros"] is JsonArray instances)
                {
                    gates.AddRange(instances.OfType<JsonObject>().Where(i => i["if"] is not null).Select(i => (i, "if")));
                }
            }
        }

        return new ItemScope(itemFiles, loops, gates);
    }

    // ----- the manifest sites -----

    private static readonly Regex LoopHead = new(@"^\s*item\s+in\s+snippet\.([a-z][a-z0-9_]*)\s*$", RegexOptions.Compiled);

    private static string? LoopOver(string forText) => LoopHead.Match(forText) is { Success: true } m ? m.Groups[1].Value : null;

    private static IEnumerable<JsonObject> Owners(JsonObject root) =>
        (root["nodes"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Concat((root["deliverables"] as JsonArray ?? new JsonArray()).OfType<JsonObject>());

    /// <summary>A node's or deliverable's macro instances naming the macro — a bare id or an object's <c>id</c>.</summary>
    private static void RenameInstances(JsonObject owner, string from, string to)
    {
        if (owner["macros"] is not JsonArray instances)
        {
            return;
        }

        for (var i = 0; i < instances.Count; i++)
        {
            if (instances[i] is JsonValue v && v.TryGetValue<string>(out var bare) && bare == from)
            {
                instances[i] = to;
            }
            else if (instances[i] is JsonObject o && o["id"] is JsonValue id && id.TryGetValue<string>(out var named) && named == from)
            {
                o["id"] = to;
            }
        }
    }

    /// <summary>The table's entry renamed in its shape — the 26/27 map (key and path, order kept) or the 28 list of ids.</summary>
    private static void RenameInTable(JsonObject root, string table, string directory, string from, string to, bool isFolder)
    {
        var key = root.ContainsKey(table) ? table : char.ToUpperInvariant(table[0]) + table[1..];
        switch (root[key])
        {
            case JsonArray list:
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] is JsonValue v && v.TryGetValue<string>(out var entry) && WorkflowTableIds.Entry(directory, entry).Id == from)
                    {
                        list[i] = isFolder ? to + "/" : to;
                    }
                }

                break;
            case JsonObject map:
                var rebuilt = new JsonObject();
                foreach (var (id, value) in map.ToList())
                {
                    map.Remove(id);
                    rebuilt[id == from ? to : id] = id == from ? (isFolder ? $"{directory}/{to}/" : $"{directory}/{to}.md") : value;
                }

                root[key] = rebuilt;
                break;
        }
    }

    /// <summary>The folder index's item renamed in the shape it has — the 26/27 [{ id, file }] or the 28 { items }.</summary>
    private static void RenameInIndex(IDictionary<string, string> files, string directory, JsonObject root, string from, string to)
    {
        var path = directory + WorkflowDataResolver.IndexFileName;
        if (!files.TryGetValue(path, out var json) || JsonNode.Parse(json) is not { } index)
        {
            return;
        }

        if (index is JsonArray list)
        {
            foreach (var item in list.OfType<JsonObject>().Where(i => i["id"]?.GetValue<string>() == from))
            {
                item["id"] = to;
                item["file"] = to + ".md";
            }
        }
        else if (index is JsonObject obj && obj["items"] is JsonArray items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is JsonValue v && v.TryGetValue<string>(out var id) && id == from)
                {
                    items[i] = to;
                }
            }
        }

        files[path] = index.ToJsonString(IndexWrite) + "\n";
    }

    private static IReadOnlyList<(string Id, string Path)> TableEntries(JsonObject root, string table)
    {
        var node = root[table] ?? root[char.ToUpperInvariant(table[0]) + table[1..]];
        var directory = table;
        return node switch
        {
            JsonObject map => map.Where(p => p.Value is JsonValue v && v.TryGetValue<string>(out _)).Select(p => (p.Key, p.Value!.GetValue<string>())).ToList(),
            JsonArray list when list.All(e => e is JsonValue v && v.TryGetValue<string>(out _)) =>
                list.Select(e => WorkflowTableIds.Entry(directory, e!.GetValue<string>())).ToList(),
            _ => Array.Empty<(string, string)>(),
        };
    }

    private static IEnumerable<string> Ids(JsonNode? array) =>
        array is JsonArray a ? a.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>() : Array.Empty<string>();

    private static int SpecVersion(JsonObject root) =>
        (root["specVersion"] ?? root["SpecVersion"]) is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

    private static bool IsFolder(WorkflowRenameKind kind) => kind is WorkflowRenameKind.MacroFolder or WorkflowRenameKind.SnippetFolder;

    private static bool IsFolderItem(WorkflowRenameKind kind) => kind is WorkflowRenameKind.MacroFolderItem or WorkflowRenameKind.SnippetFolderItem;

    private static string TableKey(WorkflowRenameKind kind) =>
        kind is WorkflowRenameKind.Macro or WorkflowRenameKind.MacroFolder or WorkflowRenameKind.MacroFolderItem ? "macros" : "snippets";

    private static string DirectoryOf(WorkflowRenameKind kind) => TableKey(kind);

    private static string Noun(WorkflowRenameKind kind) => kind switch
    {
        WorkflowRenameKind.Macro => "macro",
        WorkflowRenameKind.Snippet => "snippet",
        WorkflowRenameKind.MacroFolder => "macro folder",
        WorkflowRenameKind.SnippetFolder => "snippet folder",
        WorkflowRenameKind.MacroFolderItem => "macro folder item",
        _ => "snippet folder item",
    };
}
