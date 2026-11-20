using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>What a folder's members are on disk (part 01 § 2.2).</summary>
internal enum MemberForm
{
    /// <summary><c>&lt;id&gt;.md</c> or <c>&lt;id&gt;/</c>: nodes, checks, snippets, macros, deliverables.</summary>
    FileOrFolder,

    /// <summary><c>&lt;id&gt;.md</c> only: renderers, snippet and macro folder items.</summary>
    File,

    /// <summary><c>&lt;id&gt;.json</c> only: formats and tests.</summary>
    Json,

    /// <summary><c>&lt;id&gt;/</c> only: functions.</summary>
    Folder,
}

/// <summary>One listed member found on disk: its id and its file's path, or its folder's.</summary>
internal sealed record ListedMember(string Id, string Path, bool IsFolder);

/// <summary>A folder whose <c>index.json</c> was read: the index and the members its list names, in order.</summary>
internal sealed record ReadFolder(string Folder, string IndexPath, JsonObject Index, IReadOnlyList<ListedMember> Members);

/// <summary>
/// #1499 (specVersion 31, area 02): the state of one parse, shared by every folder the walk reads:
/// the normalised files, the registry, the refusals found so far, the READMEs, and which files are
/// accounted for. A file is accounted for when the walk read it or accepted it as a listed member;
/// at the end every other file is a stray (<see cref="SourceLayout.ReportStrays"/>), named against
/// the nearest folder the walk read (or should have read) through its index.
/// </summary>
internal sealed class SourceWalk(IReadOnlyDictionary<string, string> files, SourceRegistry registry)
{
    private readonly HashSet<string> accounted = new(StringComparer.Ordinal);
    private readonly HashSet<string> folders = new(StringComparer.Ordinal) { "" };
    private readonly HashSet<string> unreadable = new(StringComparer.Ordinal);

    /// <summary>The upload after intake: path to <c>\n</c>-normalised text.</summary>
    public IReadOnlyDictionary<string, string> Files { get; } = files;

    public SourceRegistry Registry { get; } = registry;

    public List<SourceRefusal> Refusals { get; } = [];

    /// <summary>Every <c>README.md</c> of a folder the walk read: path to text.</summary>
    public SortedDictionary<string, string> Readmes { get; } = new(StringComparer.Ordinal);

    public void Refuse(SourceRefusal refusal) => Refusals.Add(refusal);

    public bool HasFile(string path) => Files.ContainsKey(path);

    /// <summary>True when any file lies under <paramref name="folder"/>.</summary>
    public bool HasFolder(string folder)
    {
        var prefix = folder + "/";
        return Files.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));
    }

    public void Account(string path) => accounted.Add(path);

    /// <summary>Accounts for every file under <paramref name="folder"/> (one fault already said covers them all).</summary>
    public void AccountTree(string folder)
    {
        var prefix = folder + "/";
        foreach (var path in Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
            accounted.Add(path);
    }

    /// <summary>Marks <paramref name="folder"/> as one that has, or should have, an <c>index.json</c>.</summary>
    public void AddFolder(string folder) => folders.Add(folder);

    /// <summary>
    /// Marks <paramref name="folder"/> as read without its full list (its index is missing or could not
    /// be read that far): its unlisted files are not strays, since the index's own refusal says enough.
    /// </summary>
    public void MarkUnreadable(string folder)
    {
        folders.Add(folder);
        unreadable.Add(folder);
    }

    /// <summary>The files nobody accounted for, each with the nearest folder the walk read; null for a folder read without its list.</summary>
    public IEnumerable<(string Path, string? Folder)> Unaccounted()
    {
        foreach (var path in Files.Keys)
        {
            if (accounted.Contains(path))
                continue;
            var folder = Nearest(path);
            yield return (path, unreadable.Contains(folder) ? null : folder);
        }
    }

    private string Nearest(string path)
    {
        var best = "";
        foreach (var folder in folders)
        {
            if (folder.Length > best.Length && path.StartsWith(folder + "/", StringComparison.Ordinal))
                best = folder;
        }

        return best;
    }
}

/// <summary>
/// #1499 (specVersion 31, area 02): the folder rules of part 01 § 2.2, one folder at a time: its
/// <c>index.json</c> must be there (S01-007) and hold one JSON object (S01-017, S01-018, S01-041,
/// A02-005); each listed id is held to a trailing <c>/</c> (S01-011), the id rules (S01-030 to
/// S01-033), listing twice (S01-010), one file or one folder (S01-012) and a member on disk
/// (S01-009). Every file is read through here, so the strays are what is left at the end (S01-006).
/// </summary>
internal static class SourceLayout
{
    /// <summary><paramref name="folder"/>/<paramref name="name"/>, or <paramref name="name"/> at the root (<c>""</c>).</summary>
    public static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

    /// <summary>
    /// Reads <paramref name="folder"/>'s <c>index.json</c> and accounts for it and the folder's
    /// <c>README.md</c>. Null when the index is missing or is not one JSON object; the folder is then
    /// read without its list.
    /// </summary>
    public static JsonObject? ReadIndex(SourceWalk walk, string folder)
    {
        walk.AddFolder(folder);
        var readme = Join(folder, "README.md");
        if (walk.Files.TryGetValue(readme, out var text))
        {
            walk.Account(readme);
            walk.Readmes[readme] = text;
        }

        var path = Join(folder, "index.json");
        if (!walk.HasFile(path))
        {
            walk.Refuse(SourceSentences.NoIndex(folder));
            walk.MarkUnreadable(folder);
            return null;
        }

        walk.Account(path);
        var index = ReadJson(walk, path);
        if (index == null)
            walk.MarkUnreadable(folder);
        return index;
    }

    /// <summary>
    /// Reads a folder whose index is checked by <paramref name="shape"/> (its declaration name is its
    /// path, D9) and lists its members in <c>items</c>. Null when the index could not be read.
    /// </summary>
    public static ReadFolder? Read(SourceWalk walk, string folder, DeclarationShape shape, MemberForm form)
    {
        var index = ReadIndex(walk, folder);
        if (index == null)
            return null;

        var path = Join(folder, "index.json");
        SourceMembers.Check(index, shape, path, path, walk.Refusals);
        var members = ReadList(walk, folder, path, index["items"], form);
        return new ReadFolder(folder, path, index, members);
    }

    /// <summary>
    /// Checks one list of ids in <paramref name="folder"/>'s index and returns the members found on
    /// disk, accounting for their files. <paramref name="list"/> is the sentences' <c>&lt;list&gt;</c>:
    /// the index's path, then the member for a body (<c>nodes/sections/index.json nodes</c>). A list
    /// that is not a JSON array leaves the folder read without its list; an entry that is not text is
    /// skipped (its type is the shape's refusal: a list of text, A02-001).
    /// </summary>
    /// <param name="body">
    /// For a flow node's or a function's body (part 04): the ids the folder's earlier bodies listed,
    /// shared by them all (the bodies of an <c>if</c> or a <c>case</c> sit flat in one folder), and
    /// the sentence for an id listed twice, in this body or in an earlier one (S04-013, in place of
    /// S01-010). An id an earlier body listed is said once here and not read again.
    /// </param>
    public static IReadOnlyList<ListedMember> ReadList(
        SourceWalk walk, string folder, string list, JsonNode? ids, MemberForm form,
        (HashSet<string> Listed, Func<string, SourceRefusal> Twice)? body = null)
    {
        var members = new List<ListedMember>();
        if (ids is not JsonArray array)
        {
            walk.MarkUnreadable(folder);
            return members;
        }

        var indexPath = Join(folder, "index.json");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var twice = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in array)
        {
            if (Text(entry) is not { } written)
                continue;

            var id = written;
            if (id.EndsWith('/'))
            {
                id = id[..^1];
                walk.Refuse(SourceSentences.ListedWithSlash(list, id));
            }

            if (!seen.Add(id) || (body is { } earlier && !earlier.Listed.Add(id)))
            {
                if (twice.Add(id))
                    walk.Refuse(body is { } b ? b.Twice(id) : SourceSentences.ListedTwice(list, id));
                continue;
            }

            var file = Join(folder, id + (form == MemberForm.Json ? ".json" : ".md"));
            var directory = Join(folder, id);
            if (SourceIds.Check(id, indexPath) is { } bad)
            {
                // The id is refused; what it names is covered by that refusal, not stray.
                walk.Refuse(bad);
                walk.Account(file);
                if (form is MemberForm.FileOrFolder or MemberForm.Folder && walk.HasFolder(directory))
                    walk.MarkUnreadable(directory);
                continue;
            }

            var hasFile = form != MemberForm.Folder && walk.HasFile(file);
            var hasFolder = form is MemberForm.FileOrFolder or MemberForm.Folder && walk.HasFolder(directory);
            if (hasFile && hasFolder)
            {
                walk.Refuse(SourceSentences.FileAndFolder(folder, id));
                walk.Account(file);
                walk.MarkUnreadable(directory);
            }
            else if (hasFile)
            {
                walk.Account(file);
                members.Add(new ListedMember(id, file, false));
            }
            else if (hasFolder)
            {
                members.Add(new ListedMember(id, directory, true));
            }
            else
            {
                // The sentence names .md or a folder as written, in formats/ and tests/ too.
                walk.Refuse(SourceSentences.ListedButMissing(list, id, folder));
            }
        }

        return members;
    }

    /// <summary>Reports every file nobody accounted for (S01-006), against the nearest folder the walk read.</summary>
    public static void ReportStrays(SourceWalk walk)
    {
        foreach (var (path, folder) in walk.Unaccounted())
        {
            if (folder != null)
                walk.Refuse(SourceSentences.NotListed(path, folder.Length == 0 ? "the package root" : folder));
        }
    }

    /// <summary>
    /// Reads a plain JSON file (<c>index.json</c>, <c>manifest.json</c>, a format, a test): strict
    /// JSON (S01-017, S01-018, S01-041, as written per D6) holding one object (A02-005).
    /// </summary>
    public static JsonObject? ReadJson(SourceWalk walk, string path)
    {
        var text = walk.Files[path];
        var parsed = StrictJson.Parse(text);
        if (Fault(walk, path, parsed, text, linesBefore: 0))
            return null;
        if (parsed.Value is JsonObject json)
            return json;
        walk.Refuse(SourceSentences.NotAnObject(path));
        return null;
    }

    /// <summary>
    /// Reads a file with front matter: the fences and one JSON object between them (S01-016), held to
    /// strict JSON (S01-017, S01-018, S01-041). Null when it fails.
    /// </summary>
    public static (JsonObject Header, string Body)? ReadHeaderFile(SourceWalk walk, string path)
    {
        var split = FrontMatter.Split(walk.Files[path]);
        if (split == null)
        {
            walk.Refuse(SourceSentences.NoFrontMatter(path));
            return null;
        }

        // The header starts on the file's second line, after the opening fence.
        var parsed = StrictJson.Parse(split.Header);
        if (Fault(walk, path, parsed, split.Header, linesBefore: 1))
            return null;
        if (parsed.Value is JsonObject header)
            return (header, split.Body);
        walk.Refuse(SourceSentences.NoFrontMatter(path));
        return null;
    }

    /// <summary>The value of a JSON string, or null for anything else (never throws).</summary>
    public static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
            return null;
        return value.TryGetValue<string>(out var text) ? text : null;
    }

    /// <summary>A description-style list of strings, or null for <c>null</c> or anything that is not one.</summary>
    public static IReadOnlyList<string>? Lines(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;
        var lines = new List<string>();
        foreach (var item in array)
        {
            if (Text(item) is not { } line)
                return null;
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// Says <paramref name="parsed"/>'s fault, if any. <paramref name="text"/> is the JSON that was
    /// parsed and <paramref name="linesBefore"/> the lines of the file above it, so S01-017 names the
    /// file's own line.
    /// </summary>
    private static bool Fault(SourceWalk walk, string path, StrictJsonResult parsed, string text, int linesBefore)
    {
        switch (parsed.Fault)
        {
            case null:
                return false;
            case StrictJsonFault.DuplicateMember:
                walk.Refuse(SourceSentences.DuplicateMember(path, parsed.Detail ?? ""));
                return true;
            case StrictJsonFault.TooDeep:
                walk.Refuse(SourceSentences.JsonTooDeep(path, parsed.Detail ?? ""));
                return true;
            default:
                var (line, column) = StrictJson.Locate(text, parsed.Position ?? new StrictJsonPosition(0, 0));
                walk.Refuse(SourceSentences.NotJson(path, linesBefore + line, column));
                return true;
        }
    }
}
