using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1510 (the owner's third fix-first group, item 4; G-D8, G-D10): the explicit hard breaks. 30's pipeline broke every
/// single line ending; 31's reads one inside a paragraph as a space. So, where a 30 body relied on a single line ending, the
/// migration ends the line with <c>\</c> (<see cref="MarkdownLineEndings.WithHardBreaks"/>), in the bodies a reader sees:
/// deliverables, <c>rendered</c> nodes, macros, snippets (one or a folder's item) and package renderers whose format is
/// <c>markdown_block</c> or <c>markdown_inline</c> (a folder item's body is always <c>markdown_block</c>, part 05 § 6.2).
/// Never a prompt or its <c>parts</c> (headers are not touched), nor a body whose text reaches a prompt, for a <c>\</c> there
/// changes what the model reads: a snippet a prompt places, a rendered node whose text a prompt reads, and what those read
/// or place in turn (<see cref="ReachPrompts"/>); nor a file the migration itself wrote (F1's stripping nodes and F2's
/// renderers: no 30 file becomes a package renderer, so every <c>renderers/</c> file is the migration's). A deliverable a
/// prompt reads (a check's prompt reads the deliverable it checks) stays a site, as G-D8 lists it.
/// </summary>
internal static partial class Migrate
{
    /// <summary>A reference by root and dotted path (<c>snippet.a.b</c>), not inside a longer name.</summary>
    private static readonly Regex BodyReference = new(
        @"(?<![A-Za-z0-9_])(?<root>node|snippet)\.(?<path>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*)",
        RegexOptions.CultureInvariant);

    /// <summary>A read of a loop's item's body (<c>item.content</c>), not behind another name.</summary>
    private static readonly Regex ItemBodyRead = new(@"(?<![A-Za-z0-9_.])item\.content(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);

    /// <summary>
    /// The hard-breaks pass, after <see cref="StripLoopItems"/> (every body written) and before <see cref="Derived"/>. Each
    /// site (the class's remarks) whose body has line endings to break gets its <c>\</c>, counted in
    /// <see cref="Migrated.HardBreaks"/> and said once (A10-091). A body excluded because a prompt reads it, which
    /// <see cref="MarkdownLineEndings.Find"/> would break and a deliverable, a rendered node or a macro also reads or
    /// places, keeps its line endings and is said (A10-092). Run again, it writes nothing: a <c>\</c> is a hard break.
    /// </summary>
    public static void HardBreaks(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var set = FormatSet.ForPackage(m.Options.Registry, m.PackageFormats());
        var written = m.StripNodes.Keys.Select(k => "nodes/" + k["node.".Length..].Replace('.', '/') + ".md").ToHashSet(StringComparer.Ordinal);
        var sites = m.Sources
            .Where(f => f.Value.Body is not null && !written.Contains(f.Key) && IsMarkdownSite(m, f.Key, f.Value, set))
            .Select(f => f.Key)
            .ToList();
        var prompted = ReachPrompts(m);
        var shown = Shown(m, prompted);

        foreach (var path in sites)
        {
            var file = m.Sources[path];
            if (prompted.Contains(path))
            {
                if (shown.Contains(path) && MarkdownLineEndings.Find(file.Body!).Count > 0)
                    m.Notice(MigrationSentences.LineEndingsKeptForAPrompt(path));
                continue;
            }

            var (text, count) = MarkdownLineEndings.WithHardBreaks(file.Body!);
            if (count == 0)
                continue;
            file.Body = text;
            m.HardBreaks[path] = count;
            m.Notice(MigrationSentences.ExplicitHardBreaks(path, count));
        }
    }

    /// <summary>
    /// Whether the body at <paramref name="path"/> is a G-D8 site: a deliverable, a macro, a snippet or a <c>rendered</c> node
    /// (<c>nodes/</c>, <c>functions/</c>) whose format's text kind is Markdown; a snippet folder's item always.
    /// </summary>
    private static bool IsMarkdownSite(Migrated m, string path, MigratedFile file, FormatSet set)
    {
        var top = path[..Math.Max(path.IndexOf('/'), 0)];
        switch (top)
        {
            case "deliverables" or "macros":
                break;
            case "snippets" when FolderOf(path) is { } folder && folder != "snippets" && m.Sources.ContainsKey($"{folder}/{WorkflowDataResolver.IndexFileName}"):
                return true;
            case "snippets":
                break;
            case "nodes" or "functions" when SourceLayout.Text(file.Json["kind"]) == "rendered":
                break;
            default:
                return false;
        }

        return FormatTypes.TextKind(file.Json["format"], set) is "markdown_block" or "markdown_inline";
    }

    /// <summary>
    /// The bodies whose text reaches a prompt: every file a prompt (an <c>agent</c>'s body or its <c>parts</c>' text, or a
    /// macro's <c>instruction</c>, which the placing prompt holds) reads or places, and every file such a body reads or places
    /// in turn. A prompt's macro read is the macro's marker, never its body, so it reaches nothing; so is one in a snippet a
    /// prompt places, which reads what the prompt reads (part 05 § 3.1), so a macro is never one of these bodies.
    /// </summary>
    private static HashSet<string> ReachPrompts(Migrated m)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var work = new Queue<string>();
        foreach (var (path, file) in m.Sources)
        {
            foreach (var text in PromptTexts(path, file))
            {
                foreach (var read in ReadsOf(m, path, text))
                {
                    if (reached.Add(read))
                        work.Enqueue(read);
                }
            }
        }

        Spread(m, reached, work);
        return reached;
    }

    /// <summary>
    /// The bodies shown: every file a deliverable, a macro or a rendered node that no prompt reads (<paramref name="prompted"/>)
    /// reads or places, and every file those read or place in turn.
    /// </summary>
    private static HashSet<string> Shown(Migrated m, HashSet<string> prompted)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        var work = new Queue<string>();
        foreach (var (path, file) in m.Sources)
        {
            var seen = path.StartsWith("deliverables/", StringComparison.Ordinal) || path.StartsWith("macros/", StringComparison.Ordinal)
                || (SourceLayout.Text(file.Json["kind"]) == "rendered" && !path.StartsWith("renderers/", StringComparison.Ordinal) && !prompted.Contains(path));
            if (!seen || file.Body is not { } body)
                continue;
            foreach (var read in ReadsOf(m, path, body))
            {
                if (shown.Add(read))
                    work.Enqueue(read);
            }
        }

        Spread(m, shown, work);
        return shown;
    }

    /// <summary>Adds to <paramref name="found"/> every file the bodies queued in <paramref name="work"/> read or place, outward until none is new.</summary>
    private static void Spread(Migrated m, HashSet<string> found, Queue<string> work)
    {
        while (work.TryDequeue(out var path))
        {
            if (m.Sources.GetValueOrDefault(path)?.Body is not { } body)
                continue;
            foreach (var read in ReadsOf(m, path, body))
            {
                if (found.Add(read))
                    work.Enqueue(read);
            }
        }
    }

    /// <summary>The prompt texts of <paramref name="file"/>: an <c>agent</c>'s body and its <c>parts</c>' text; a macro's <c>instruction</c>.</summary>
    private static IEnumerable<string> PromptTexts(string path, MigratedFile file)
    {
        if (SourceLayout.Text(file.Json["kind"]) == "agent")
        {
            if (file.Body is { } body)
                yield return body;
            foreach (var (_, part) in file.Json["parts"] as JsonObject ?? [])
                yield return Lines(part?["text"]);
        }
        else if (path.StartsWith("macros/", StringComparison.Ordinal))
        {
            yield return Lines(file.Json["instruction"]);
        }
    }

    /// <summary>A header's array of lines (a part's <c>text</c>, a macro's <c>instruction</c>) as one text.</summary>
    private static string Lines(JsonNode? lines) =>
        lines is JsonArray array ? string.Join('\n', array.Select(SourceLayout.Text).OfType<string>()) : SourceLayout.Text(lines) ?? "";

    /// <summary>
    /// The files whose text <paramref name="text"/>, written in <paramref name="reader"/>, reads or places: a snippet (a folder
    /// read whole is every item; <c>item.content</c> in a loop over a folder is that folder's items), a <c>rendered</c> node
    /// (resolved outward from the reader's folder, part 01 § 9; a flow is the <c>ret</c>s it returns; an agent's text is its
    /// reply, never its prompt). A node's <c>.state</c> is no text. A deliverable read is none of these: a deliverable is
    /// shown, and stays a site whoever reads it. Nor is a macro read: only a prompt, a part, a macro instruction or a snippet
    /// placed in one may read <c>macro.*</c> (part 05 § 3.1), and there it is the macro's marker, never its body; elsewhere
    /// it is refused (the migration's <c>macro.&lt;id&gt;.fixed_text</c> in a deliverable is a hand edit, D3), and a macro
    /// body is a site and shown whoever places it.
    /// </summary>
    private static IEnumerable<string> ReadsOf(Migrated m, string reader, string text)
    {
        var reads = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in BodyReference.Matches(text))
        {
            var path = match.Groups["path"].Value.Split('.');
            switch (match.Groups["root"].Value)
            {
                case "snippet":
                    reads.UnionWith(Named(m, "snippets", path));
                    break;
                case "node":
                    if (NodeFile(m, reader, path) is { } node)
                        Returned(m, node, reads, 0);
                    break;
            }
        }

        if (ItemBodyRead.IsMatch(text) && LoopFolder(m, reader) is { } folder)
            reads.UnionWith(Named(m, "snippets", [folder]));
        return reads;
    }

    /// <summary>
    /// The files under <paramref name="top"/> that <paramref name="path"/> names: the longest prefix that is a file
    /// (<c>&lt;top&gt;/a/b.md</c>), or a folder (<c>&lt;top&gt;/a/index.json</c>), whose every <c>.md</c> it then names.
    /// </summary>
    private static IEnumerable<string> Named(Migrated m, string top, string[] path)
    {
        for (var i = path.Length; i > 0; i--)
        {
            var at = $"{top}/{string.Join('/', path[..i])}";
            if (m.Sources.ContainsKey(at + ".md"))
                return [at + ".md"];
            if (m.Sources.ContainsKey($"{at}/{WorkflowDataResolver.IndexFileName}"))
                return m.Sources.Keys.Where(k => k.StartsWith(at + "/", StringComparison.Ordinal) && k.EndsWith(".md", StringComparison.Ordinal)).ToList();
        }

        return [];
    }

    /// <summary>
    /// The node file (<c>.md</c>, or a flow's <c>index.json</c>) <paramref name="path"/> names from <paramref name="reader"/>,
    /// resolved outward from the reader's folder to the top (part 01 § 9), the longest prefix first; none for a <c>.state</c> read.
    /// </summary>
    private static string? NodeFile(Migrated m, string reader, string[] path)
    {
        var scopes = new List<string>();
        if (reader.StartsWith("nodes/", StringComparison.Ordinal))
        {
            for (var folder = FolderOf(reader); folder is not null && folder != "nodes"; folder = FolderOf(folder))
                scopes.Add(folder);
        }

        scopes.Add("nodes");
        foreach (var scope in scopes)
        {
            for (var i = path.Length; i > 0; i--)
            {
                if (i < path.Length && path[i] == "state")
                    continue;
                var at = $"{scope}/{string.Join('/', path[..i])}";
                if (m.Sources.ContainsKey(at + ".md"))
                    return at + ".md";
                if (m.Sources.ContainsKey($"{at}/{WorkflowDataResolver.IndexFileName}"))
                    return $"{at}/{WorkflowDataResolver.IndexFileName}";
            }
        }

        return null;
    }

    /// <summary>Adds <paramref name="node"/> to <paramref name="reads"/> when it is a <c>rendered</c> node, or, for a flow's <c>index.json</c>, the files of the <c>ret</c>s it returns, outward.</summary>
    private static void Returned(Migrated m, string node, HashSet<string> reads, int depth)
    {
        if (!node.EndsWith("/" + WorkflowDataResolver.IndexFileName, StringComparison.Ordinal))
        {
            if (SourceLayout.Text(m.Sources.GetValueOrDefault(node)?.Json["kind"]) == "rendered")
                reads.Add(node);
            return;
        }

        if (depth > SourceBounds.JsonDepth || !m.Sources.TryGetValue(node, out var flow))
            return;
        foreach (var ret in RetsOf(flow.Json))
        {
            if (ret.StartsWith("node.", StringComparison.Ordinal) && NodeFile(m, "nodes", ret["node.".Length..].Split('.')) is { } file)
                Returned(m, file, reads, depth + 1);
        }
    }

    /// <summary>The snippet folder of the nearest <c>for</c> around <paramref name="reader"/> when it loops over one (<c>"in": "snippet.&lt;folder&gt;"</c>).</summary>
    private static string? LoopFolder(Migrated m, string reader)
    {
        for (var folder = FolderOf(reader); folder is not null && folder.StartsWith("nodes/", StringComparison.Ordinal); folder = FolderOf(folder))
        {
            if (m.Sources.GetValueOrDefault($"{folder}/{WorkflowDataResolver.IndexFileName}")?.Json is not { } header
                || SourceLayout.Text(header["kind"]) != "for")
            {
                continue;
            }

            return SourceLayout.Text(header["in"]) is { } collection && collection.StartsWith("snippet.", StringComparison.Ordinal)
                ? collection["snippet.".Length..]
                : null;
        }

        return null;
    }

    /// <summary>The folder of <paramref name="path"/> (<c>a/b</c> of <c>a/b/c.md</c>); none at the top.</summary>
    private static string? FolderOf(string path) => path.LastIndexOf('/') is var slash and > 0 ? path[..slash] : null;
}
