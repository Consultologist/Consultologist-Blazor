using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Scriban.Syntax;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.9, part 01 §§ 6 and 7, D6): the renames pass, which runs on the 30 package
/// before any 31 file is written, so every later pass reads and writes the new ids.
/// </summary>
internal static partial class Migrate
{
    /// <summary>
    /// #1507 (part 10 § 1.9, D6): the ids 31 refuses and 30 admits, renamed. These are the reserved words and the names
    /// Windows reserves (<see cref="ReservedIds"/>), and an id past 31's 64 characters. 30's id pattern is 31's (part 01
    /// § 6), so no 30 id fails it. Object field names are never renamed: they are not ids.
    /// <list type="bullet">
    /// <item>A hand choice comes from <see cref="Migration31Options.Renames"/> and is used as given. One that is no good 31
    /// id, or is already taken, refuses the run (A10-033).</item>
    /// <item>Any other reserved id takes its kind as a suffix: <c>_for</c>, <c>_while</c>, <c>_if</c> and <c>_case</c> for a
    /// flow, <c>_node</c> for a leaf, <c>_check</c> for a <c>terms_subset</c> node (a check at 31), <c>_input</c>,
    /// <c>_macro</c>, <c>_snippet</c> (also a macro that becomes a snippet, D4, D6), <c>_format</c> and
    /// <c>_deliverable</c> (A10-006).</item>
    /// <item>A suffixed id that is taken gets the first free number (<c>_2</c>, <c>_3</c>, …; S10-006).</item>
    /// <item>A long id is cut to 64 characters (A10-034). The id is always cut before a suffix or a number is added, so the
    /// whole is never past 64.</item>
    /// </list>
    /// An id is taken when its namespace already holds it (part 01 § 9.1), or when the file the new id implies already
    /// exists. The namespaces are the nodes and checks, the inputs, the macros and snippets together (a macro may become
    /// a snippet), one folder's items, the formats (the registry's names too) and the deliverables. Every rename is
    /// reported (<see cref="Migrated.Renames"/>). Every reference is rewritten by token, never by text search, with the
    /// 30 renamers' machinery (<see cref="WorkflowNodeRename"/>, <see cref="WorkflowTextRename"/>,
    /// <see cref="WorkflowTemplateTokens"/>):
    /// <list type="bullet">
    /// <item>reads, in every prompt, snippet and macro body, header <c>if</c> and macro instruction;</item>
    /// <item><c>ret</c>, a body's <c>nodes</c>, and <c>for</c>'s <c>in</c>;</item>
    /// <item>every condition: <c>if</c>, <c>case</c>, <c>while</c>, <c>until</c>, <c>let</c>, and an instance's or a
    /// deliverable's <c>if</c>;</item>
    /// <item><c>pass.….node.&lt;id&gt;</c>, a deliverable's <c>sources</c> and <c>check</c>, and a node's
    /// <c>format</c> or <c>type</c>.</item>
    /// </list>
    /// Prose, string literals and longer ids are left alone. The renamed 30 package is then read again
    /// (<see cref="Migrated.Read"/>).
    /// </summary>
    internal static class Renames
    {
        public const string Node = "node";
        public const string Input = "input";
        public const string Macro = "macro";
        public const string Snippet = "snippet";
        public const string Format = "format";
        public const string Deliverable = "deliverable";

        /// <summary>An item of a macro folder; its id in a plan is <c>&lt;folder&gt;/&lt;item&gt;</c>.</summary>
        public const string MacroItem = "macro item";

        /// <summary>An item of a snippet folder; its id in a plan is <c>&lt;folder&gt;/&lt;item&gt;</c>.</summary>
        public const string SnippetItem = "snippet item";

        /// <summary>
        /// One rename. <see cref="Id"/> and <see cref="NewId"/> are bare ids. <see cref="Folder"/> is the 30 folder of an
        /// item. <see cref="IsFolder"/> marks a macro or snippet folder.
        /// </summary>
        internal sealed record Rename(string Kind, string Id, string NewId, string Notice, string? Folder = null, bool IsFolder = false);

        private static readonly Regex ForHead = new(@"^(\s*[A-Za-z_][A-Za-z0-9_]*\s+in\s+)(.*)$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        /// <summary>
        /// The renames for <paramref name="m"/>'s 30 package, keyed by (kind, 30 id): an item's id is
        /// <c>&lt;folder&gt;/&lt;item&gt;</c>, and its value is the item's new id. They come in the order they are made:
        /// inputs, nodes, macros, snippets, folder items, formats, deliverables, each in 30's order. Empty when the run
        /// would be refused (<see cref="Apply"/>).
        /// </summary>
        public static IReadOnlyDictionary<(string Kind, string Id), string> Plan(Migrated m)
        {
            ArgumentNullException.ThrowIfNull(m);
            var (renames, refusals) = Decide(m);
            var plan = new Dictionary<(string Kind, string Id), string>();
            if (refusals.Count > 0)
                return plan;
            foreach (var rename in renames)
                plan[(rename.Kind, rename.Folder is null ? rename.Id : $"{rename.Folder}/{rename.Id}")] = rename.NewId;
            return plan;
        }

        /// <summary>
        /// <paramref name="m"/> with its plan applied. When nothing is renamed, <paramref name="m"/> itself is returned.
        /// Otherwise the 30 manifest and files are rewritten and read again, with the options' deliverable and folder
        /// keys following, and each rename is said and listed. A hand choice that cannot be made gives refusals and no
        /// working set.
        /// </summary>
        public static (Migrated? Migrated, IReadOnlyList<string> Refusals) Apply(Migrated m)
        {
            ArgumentNullException.ThrowIfNull(m);
            var (renames, refusals) = Decide(m);
            if (refusals.Count > 0)
                return (null, refusals);
            if (renames.Count == 0)
                return (m, []);

            var root = m.Root30.DeepClone().AsObject();
            var files = new Dictionary<string, string>(m.Files30, StringComparer.Ordinal);
            var folders = new Dictionary<(string Kind, string Id), string>();
            // The files with a header (a snippet's, a macro's, as Migrated.Read reads them); a prompt has none.
            var headed = new HashSet<string>(m.Resolved30.Headers.Keys, StringComparer.Ordinal);
            foreach (var rename in renames)
            {
                var refused = rename.Kind switch
                {
                    Input => RenameInput(root, files, headed, rename.Id, rename.NewId),
                    Node => RenameNode(root, files, headed, rename.Id, rename.NewId),
                    Format => RenameFormat(root, rename.Id, rename.NewId),
                    Deliverable => RenameDeliverable(root, files, headed, rename.Id, rename.NewId),
                    _ => RenameText(root, files, headed, rename, folders),
                };
                if (refused.Count > 0)
                    return (null, refused.Select(r => MigrationSentences.AtRung(WorkflowPackageMigrationChain.Target, r)).ToList());
            }

            var options = m.Options with
            {
                WhenEmpty = Rekeyed(m.Options.WhenEmpty, renames, Deliverable),
                FolderFormats = Rekeyed(m.Options.FolderFormats, renames, Snippet),
            };
            var (again, reread) = Migrated.Read(root.ToJsonString(), files, options);
            if (again is null)
                return (null, reread);

            foreach (var rename in renames)
            {
                again.Notice(rename.Notice);
                again.Renames.Add(rename.Notice);
                if (rename.Kind == Node)
                    again.NodeIds30[rename.NewId] = rename.Id;
                else if (rename.Kind == Deliverable)
                    again.DeliverableIds30[rename.NewId] = rename.Id;
            }

            return (again, []);
        }

        /// <summary>
        /// <paramref name="id"/> with <paramref name="tail"/> after it, <paramref name="id"/> cut first so that the whole
        /// is at most <see cref="SourceBounds.IdLength"/> characters (part 10 § 1.9).
        /// </summary>
        internal static string Fit(string id, string tail) =>
            (id.Length + tail.Length <= SourceBounds.IdLength ? id : id[..Math.Max(1, SourceBounds.IdLength - tail.Length)]) + tail;

        /// <summary>
        /// D4: whether the 30 macro <paramref name="id"/> becomes a snippet at 31. It must be read only as fixed text: placed
        /// on a deliverable, or read as <c>.content</c>, with no condition anywhere. So it has no <c>if</c> of its own, is
        /// not optional, every placement on a deliverable is bare of <c>if</c>, <c>when</c>, <c>before</c>, <c>after</c>,
        /// <c>forItem</c> and <c>slot</c>, no node places it, and no text reads its <c>.prompt</c>. A macro also placed by
        /// a prompt stays a macro, and so is suffixed as one.
        /// </summary>
        internal static bool BecomesSnippet(Migrated m, string id)
        {
            ArgumentNullException.ThrowIfNull(m);
            if (m.Manifest30.Macros?.FirstOrDefault(x => x.Id == id) is not { } macro || macro.File.EndsWith('/') || macro.Optional == true)
                return false;
            if (m.Resolved30.Headers.GetValueOrDefault(macro.File) is { } header && (header.If is not null || header.Optional == true))
                return false;
            if ((m.Manifest30.Nodes ?? []).Any(n => n.Macros?.Any(x => x?.Id == id) == true))
                return false;
            if (m.Walk30.Templates.Any(t => t.References.MacroPrompts.Contains(id)))
                return false;

            var placements = m.Manifest30.EffectiveDeliverables.SelectMany(d => d.Macros ?? []).Where(x => x?.Id == id).ToList();
            if (placements.Any(x => x.If is not null || x.When is not null || x.Before is not null || x.After is not null || x.ForItem is not null || x.Slot == true))
                return false;
            return placements.Count > 0 || m.Walk30.Templates.Any(t => t.References.MacroContents.Contains(id));
        }

        // ----- the plan -----

        private static (List<Rename> Renames, List<string> Refusals) Decide(Migrated m)
        {
            var renames = new List<Rename>();
            var refusals = new List<string>();
            var manifest = m.Manifest30;
            var files = m.Files30;

            void Consider(string kind, string id, string suffix, HashSet<string> taken, Func<string, bool> fileTaken, string? folder = null, bool isFolder = false)
            {
                if (SourceIds.Check(id, "") is null)
                    return;

                var noun = kind switch { MacroItem => Macro, SnippetItem => Snippet, _ => kind };
                string Shown(string x) => folder is null ? x : $"{folder}/{x}";
                bool Free(string x) => !taken.Contains(x) && !fileTaken(x);
                string newId;
                string notice;
                if (m.Options.Renames?.GetValueOrDefault(id) is { } chosen)
                {
                    var good = SourceIds.Check(chosen, "") is null;
                    if (!good || !Free(chosen))
                    {
                        refusals.Add(MigrationSentences.ChosenRenameRefused(Shown(id), Shown(chosen), taken: good));
                        return;
                    }

                    newId = chosen;
                    notice = MigrationSentences.RenamedReserved(noun, Shown(id), Shown(newId));
                }
                else if (id.Length > SourceBounds.IdLength)
                {
                    newId = Free(Fit(id, "")) ? Fit(id, "") : Numbered(id, "", Free);
                    notice = MigrationSentences.RenamedTooLong(noun, Shown(id), Shown(newId));
                }
                else
                {
                    var first = Fit(id, "_" + suffix);
                    newId = Free(first) ? first : Numbered(id, "_" + suffix, Free);
                    notice = newId == first
                        ? MigrationSentences.RenamedReserved(noun, Shown(id), Shown(newId))
                        : MigrationSentences.RenameTaken(Shown(id), Shown(newId), Shown(first));
                }

                taken.Add(newId);
                renames.Add(new Rename(kind, id, newId, notice, folder, isFolder));
            }

            var inputs = (manifest.Inputs ?? []).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var input in manifest.Inputs ?? [])
                Consider(Input, input.Id, "input", inputs, _ => false);

            var nodes = (manifest.Nodes ?? []).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var node in manifest.Nodes ?? [])
                Consider(Node, node.Id, NodeSuffix(m, node), nodes, x => files.ContainsKey(WorkflowImpliedPaths.PromptOf(x)));

            var texts = (manifest.Macros ?? []).Select(x => x.Id).Concat((manifest.Snippets ?? []).Keys).ToHashSet(StringComparer.Ordinal);
            bool TextFileTaken(string x) => files.Keys.Any(p =>
                p == $"macros/{x}.md" || p == $"snippets/{x}.md"
                || p.StartsWith($"macros/{x}/", StringComparison.Ordinal) || p.StartsWith($"snippets/{x}/", StringComparison.Ordinal));
            foreach (var macro in manifest.Macros ?? [])
            {
                var folder = macro.File.EndsWith('/');
                Consider(Macro, macro.Id, !folder && BecomesSnippet(m, macro.Id) ? "snippet" : "macro", texts, TextFileTaken, isFolder: folder);
            }

            foreach (var (id, path) in manifest.Snippets ?? [])
                Consider(Snippet, id, "snippet", texts, TextFileTaken, isFolder: path.EndsWith('/'));

            var folders = (manifest.Snippets ?? []).Where(s => s.Value.EndsWith('/')).Select(s => (Kind: SnippetItem, s.Key, Path: s.Value))
                .Concat((manifest.Macros ?? []).Where(x => x.File.EndsWith('/')).Select(x => (Kind: MacroItem, x.Id, Path: x.File)));
            foreach (var (kind, folder, path) in folders)
            {
                var index = files.TryGetValue(path + WorkflowDataResolver.IndexFileName, out var indexJson)
                    ? WorkflowTextHeaders.ReadIndex(indexJson, folder, kind == SnippetItem ? "Snippet" : "Macro", manifest.SpecVersion, [])
                    : null;
                var items = (index?.Items ?? []).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var (item, _) in index?.Items ?? [])
                    Consider(kind, item, kind == SnippetItem ? "snippet" : "macro", items, x => files.ContainsKey(path + x + ".md"), folder);
            }

            var formats = (manifest.Formats ?? []).Select(f => f.Id)
                .Concat(m.Options.Registry.Names.BuiltInFormats).Concat(m.Options.Registry.Names.CatalogueFormats)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var format in manifest.Formats ?? [])
                Consider(Format, format.Id, "format", formats, _ => false);

            var deliverables = manifest.EffectiveDeliverables.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var deliverable in manifest.EffectiveDeliverables)
                Consider(Deliverable, deliverable.Id, "deliverable", deliverables, _ => false);

            return (renames, refusals);
        }

        /// <summary>The first of <c>&lt;id&gt;&lt;tail&gt;_2</c>, <c>_3</c>, … (cut to fit) that is free (S10-006).</summary>
        private static string Numbered(string id, string tail, Func<string, bool> free)
        {
            for (var n = 2; ; n++)
            {
                var candidate = Fit(id, string.Create(CultureInfo.InvariantCulture, $"{tail}_{n}"));
                if (free(candidate))
                    return candidate;
            }
        }

        /// <summary>A node's kind as its suffix: a flow's member, <c>check</c> for a <c>terms_subset</c> node, else <c>node</c>.</summary>
        private static string NodeSuffix(Migrated m, WorkflowNodeSpec node) =>
            node.For is not null ? "for"
            : node.While is not null ? "while"
            : node.If is not null ? "if"
            : node.Case is not null ? "case"
            : IsCheck(m, node) ? "check"
            : "node";

        /// <summary>A dictionary keyed by 30 ids of <paramref name="kind"/> (a deliverable, a snippet folder) keyed by the new ids.</summary>
        private static IReadOnlyDictionary<string, string>? Rekeyed(IReadOnlyDictionary<string, string>? byId, List<Rename> renames, string kind)
        {
            if (byId is null)
                return null;
            var rekeyed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (id, value) in byId)
                rekeyed[renames.FirstOrDefault(r => r.Kind == kind && r.Folder is null && r.Id == id)?.NewId ?? id] = value;
            return rekeyed;
        }

        // ----- the rewrite, kind by kind -----

        private static readonly IReadOnlyList<string> NoRefusals = [];

        private static IReadOnlyList<string> RenameInput(JsonObject root, Dictionary<string, string> files, HashSet<string> headed, string from, string to)
        {
            foreach (var input in Objects(root["inputs"]))
            {
                if (Str(input["id"]) == from)
                    input["id"] = to;
            }

            (string, bool, bool) text(string t) => Reads(t, "input", from, to);
            Everywhere(root, files, headed, text);
            foreach (var node in Objects(root["nodes"]))
            {
                if (node["with"] is not JsonObject with)
                    continue;
                foreach (var key in with.Select(p => p.Key).ToList())
                {
                    if (with[key] is JsonValue value && value.TryGetValue<string>(out var s)
                        && (s == $"input.{from}" || s.StartsWith($"input.{from}.", StringComparison.Ordinal)))
                        with[key] = $"input.{to}" + s[$"input.{from}".Length..];
                }
            }

            return NoRefusals;
        }

        private static IReadOnlyList<string> RenameNode(JsonObject root, Dictionary<string, string> files, HashSet<string> headed, string from, string to)
        {
            var loops = WorkflowNodeRename.LoopsHolding(root, from);
            if (Objects(root["nodes"]).FirstOrDefault(n => Str(n["id"]) == from) is { } node && node["prompt"] is null)
            {
                var implied = WorkflowImpliedPaths.PromptOf(from);
                if (files.Remove(implied, out var prompt))
                    files[WorkflowImpliedPaths.PromptOf(to)] = prompt;
            }

            // The structural sites and the gates RenameInManifest knows; then every gate again with the sites it misses
            // (while, until, let, for) and the pass reads (pass.previous.node.<id>), which it does not rename.
            WorkflowNodeRename.RenameInManifest(root, from, to);
            (string, bool, bool) text(string t)
            {
                var (renamed, _, parsed) = WorkflowNodeRename.RenameInTemplate(t, from, to, loops);
                var (passed, _, _) = Reads(renamed, "pass", from, to);
                return (passed, passed != t, parsed);
            }

            Everywhere(root, files, headed, text);
            return NoRefusals;
        }

        private static IReadOnlyList<string> RenameFormat(JsonObject root, string from, string to)
        {
            foreach (var format in Objects(root["formats"]))
            {
                if (Str(format["id"]) == from)
                    format["id"] = to;
            }

            foreach (var node in Objects(root["nodes"]))
            {
                foreach (var member in new[] { "format", "type" })
                {
                    if (Str(node[member]) == from)
                        node[member] = to;
                }
            }

            return NoRefusals;
        }

        private static IReadOnlyList<string> RenameDeliverable(JsonObject root, Dictionary<string, string> files, HashSet<string> headed, string from, string to)
        {
            foreach (var deliverable in Objects(root["deliverables"]))
            {
                if (Str(deliverable["id"]) == from)
                    deliverable["id"] = to;
            }

            Everywhere(root, files, headed, t => Reads(t, "deliverable", from, to));
            return NoRefusals;
        }

        /// <summary>A macro, a snippet, a folder or an item, by <see cref="WorkflowTextRename"/>; then each macro instruction that reads it.</summary>
        private static IReadOnlyList<string> RenameText(
            JsonObject root, Dictionary<string, string> files, HashSet<string> headed, Rename rename, Dictionary<(string Kind, string Id), string> folders)
        {
            var macro = rename.Kind is Macro or MacroItem;
            var kind = rename.Kind switch
            {
                Macro => rename.IsFolder ? WorkflowRenameKind.MacroFolder : WorkflowRenameKind.Macro,
                Snippet => rename.IsFolder ? WorkflowRenameKind.SnippetFolder : WorkflowRenameKind.Snippet,
                MacroItem => WorkflowRenameKind.MacroFolderItem,
                _ => WorkflowRenameKind.SnippetFolderItem,
            };
            var folder = rename.Folder is null ? null : folders.GetValueOrDefault((macro ? Macro : Snippet, rename.Folder), rename.Folder);

            // The rename reads every .md file; a root README.md is no template and is kept as it is.
            var templates = files.Where(f => IsTemplate(f.Key) || f.Key.EndsWith(".json", StringComparison.Ordinal))
                .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
            var (_, refusals) = WorkflowTextRename.Apply(root, templates, kind, rename.Id, rename.NewId, folder);
            if (refusals.Count > 0)
                return refusals;

            var before = files.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (var path in files.Keys.Where(p => IsTemplate(p) || p.EndsWith(".json", StringComparison.Ordinal)).ToList())
                files.Remove(path);
            foreach (var (path, text) in templates)
                files[path] = text;

            // A snippet or macro file the rename moved keeps its header at its new path.
            foreach (var path in files.Keys.Where(p => !before.Contains(p) && IsTemplate(p) && !p.StartsWith("prompts/", StringComparison.Ordinal)))
                headed.Add(path);
            foreach (var path in files.Keys.Where(headed.Contains).ToList())
                files[path] = InstructionOnly(files[path], t => WorkflowTextRename.RenameInTemplate(t, kind, rename.Id, rename.NewId, folder));

            if (rename.IsFolder)
                folders[(macro ? Macro : Snippet, rename.Id)] = rename.NewId;
            return NoRefusals;
        }

        // ----- where a reference can be -----

        private delegate (string Text, bool Changed, bool Parsed) Rewrite(string text);

        /// <summary>
        /// Every template file (body; for a file in <paramref name="headed"/>, its header's <c>if</c> and instruction too) and
        /// every manifest condition and <c>for</c>, rewritten.
        /// </summary>
        private static void Everywhere(JsonObject root, Dictionary<string, string> files, HashSet<string> headed, Rewrite rewrite)
        {
            foreach (var path in files.Keys.Where(IsTemplate).ToList())
                files[path] = headed.Contains(path) ? File(files[path], rewrite) : Whole(files[path], rewrite);

            foreach (var (owner, member) in Gates(root))
            {
                if (owner[member] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var (renamed, changed) = WorkflowTemplateTokens.RewriteCondition(text, t => rewrite(t));
                    if (changed)
                        owner[member] = renamed;
                }
            }

            foreach (var node in Objects(root["nodes"]))
            {
                if (node["for"] is JsonValue value && value.TryGetValue<string>(out var text) && ForHead.Match(text) is { Success: true } head)
                {
                    var (renamed, changed) = WorkflowTemplateTokens.RewriteCondition(head.Groups[2].Value, t => rewrite(t));
                    if (changed)
                        node["for"] = head.Groups[1].Value + renamed;
                }
            }
        }

        /// <summary>The 30 templates: the files under <c>prompts/</c>, <c>snippets/</c> and <c>macros/</c> that end in <c>.md</c>.</summary>
        private static bool IsTemplate(string path) =>
            path.EndsWith(".md", StringComparison.Ordinal)
            && (path.StartsWith("prompts/", StringComparison.Ordinal) || path.StartsWith("snippets/", StringComparison.Ordinal) || path.StartsWith("macros/", StringComparison.Ordinal));

        /// <summary>Every condition the 30 manifest holds: a node's <c>if</c>, <c>case</c>, <c>while</c>, <c>until</c> and <c>let</c>, a deliverable's <c>if</c>, and each instance's <c>if</c>.</summary>
        private static IEnumerable<(JsonObject Owner, string Member)> Gates(JsonObject root)
        {
            foreach (var node in Objects(root["nodes"]))
            {
                foreach (var member in new[] { "if", "case", "while", "until", "let" })
                    yield return (node, member);
                foreach (var instance in Objects(node["macros"]))
                    yield return (instance, "if");
            }

            foreach (var deliverable in Objects(root["deliverables"]))
            {
                yield return (deliverable, "if");
                foreach (var instance in Objects(deliverable["macros"]))
                    yield return (instance, "if");
            }
        }

        private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

        private static IEnumerable<JsonObject> Objects(JsonNode? array) =>
            array is JsonArray items ? items.OfType<JsonObject>().ToList() : [];

        /// <summary>A file with no header (a prompt: a line <c>{</c> opening it is text) rewritten whole.</summary>
        private static string Whole(string fileText, Rewrite rewrite)
        {
            var (text, changed, _) = rewrite(fileText);
            return changed ? text : fileText;
        }

        /// <summary>A snippet or macro file rewritten: its body, and its header's <c>if</c> (a condition) and <c>instruction</c> (a template).</summary>
        private static string File(string fileText, Rewrite rewrite)
        {
            var (headerJson, body) = WorkflowTextHeaders.Split(fileText);
            var (newBody, bodyChanged, _) = rewrite(body);
            if (headerJson is null)
                return bodyChanged ? newBody : fileText;

            var header = WorkflowTextHeaders.Parse(WorkflowTextKind.Macro, "rename", headerJson, []);
            if (header is not null)
            {
                var newIf = header.If is { } gate ? WorkflowTemplateTokens.RewriteCondition(gate, t => rewrite(t)).Text : null;
                var newInstruction = header.Instruction is { } instruction ? rewrite(instruction).Text : null;
                if (newIf != header.If || newInstruction != header.Instruction)
                    return WorkflowTextHeaders.Compose(header with { If = newIf, Instruction = newInstruction }) + newBody;
            }

            return bodyChanged ? headerJson + "\n" + newBody : fileText;
        }

        /// <summary>A macro file with its header's <c>instruction</c> rewritten, the rest as it is (<see cref="WorkflowTextRename"/> rewrites the body and the <c>if</c>).</summary>
        private static string InstructionOnly(string fileText, Rewrite rewrite)
        {
            var (headerJson, body) = WorkflowTextHeaders.Split(fileText);
            if (headerJson is null || WorkflowTextHeaders.Parse(WorkflowTextKind.Macro, "rename", headerJson, []) is not { Instruction: { } instruction } header)
                return fileText;
            var (renamed, changed, _) = rewrite(instruction);
            return changed ? WorkflowTextHeaders.Compose(header with { Instruction = renamed }) + body : fileText;
        }

        /// <summary>
        /// <paramref name="text"/> with the reads of <paramref name="from"/> renamed. For <c>input</c> and
        /// <c>deliverable</c> these are <c>&lt;root&gt;.&lt;from&gt;…</c> and <c>&lt;root&gt;["&lt;from&gt;"]…</c>; for
        /// <c>pass</c>, the node read through a pass (<c>pass.previous.node.&lt;from&gt;</c>, <c>pass.history[i].node.&lt;from&gt;</c>).
        /// </summary>
        private static (string Text, bool Changed, bool Parsed) Reads(string text, string root, string from, string to) =>
            string.IsNullOrEmpty(text) || !text.Contains(from, StringComparison.Ordinal)
                ? (text, false, true)
                : WorkflowTemplateTokens.Rewrite(text, to, source => new ReadRenamer(source, root, from));

        private sealed class ReadRenamer(string text, string root, string oldId) : WorkflowTemplateTokens.EditingVisitor(text, oldId)
        {
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

            private void Consider(ScriptExpression expression)
            {
                if (WorkflowTemplateTokens.Flatten(expression) is not { } chain || chain.Root != root || chain.Segments.Count == 0)
                    return;
                if (root != "pass")
                {
                    Take(chain.Segments[0]);
                    return;
                }

                for (var i = 0; i + 1 < chain.Segments.Count; i++)
                {
                    if (chain.Segments[i].Name == "node")
                    {
                        Take(chain.Segments[i + 1]);
                        return;
                    }
                }
            }
        }
    }
}
