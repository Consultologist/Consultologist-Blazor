using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1510 (the owner's third fix-first group, item 4; G-D8, G-D10): the explicit hard breaks. 30's pipeline broke every
/// single line ending; 31's reads one inside a paragraph as a space. So, where a 30 body relied on a single line ending, the
/// migration ends the line with <c>\</c> (<see cref="MarkdownLineEndings.WithHardBreaks"/>), in every Markdown body:
/// deliverables, <c>rendered</c> nodes, macros, snippets (one or a folder's item) and package renderers whose format is
/// <c>markdown_block</c> or <c>markdown_inline</c> (a folder item's body is always <c>markdown_block</c>, part 05 § 6.2),
/// publish's S05-065 sites, read with the same finder. #1543 (ruling 1): a body a prompt reads or places is one of them, for
/// the engine sends a prompt as its template's text and the model reads the <c>\</c> the reader sees. Never a prompt or its
/// <c>parts</c> (they are not Markdown values; headers are not touched), nor a file the migration itself wrote (F1's
/// stripping nodes and F2's renderers: no 30 file becomes a package renderer, so every <c>renderers/</c> file is the
/// migration's). #1543 (ruling 2, D2): each tag's role is the checker's own typing of the body, read from the 31 set
/// written so far as the derived flags read it (<see cref="Read31"/>); a set the checker cannot read that far, or a file
/// it refuses, gives every tag the role that says least (<see cref="TagRole.Statement"/>), so no <c>\</c> is written beside
/// a line of only tags there.
/// </summary>
internal static partial class Migrate
{
    /// <summary>
    /// The hard-breaks pass, after <see cref="StripLoopItems"/> (every body written) and before <see cref="Derived"/>. Each
    /// site (the class's remarks) whose body has line endings to break gets its <c>\</c>, counted in
    /// <see cref="Migrated.HardBreaks"/> and said once (A10-091). Run again, it writes nothing: a <c>\</c> is a hard break.
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
        if (sites.Count == 0)
            return;
        var read = Read31(m);
        var roles = read is { } typed
            ? TagRoles.Of(typed.Bodies.Bodies, typed.Resolution.Tree, typed.Refusals.Select(r => r.Path).ToHashSet(StringComparer.Ordinal))
            : null;
        foreach (var path in sites)
        {
            var file = m.Sources[path];
            var role = roles?.Of(path, file.Body!) ?? (_ => TagRole.Statement);
            var (text, count) = MarkdownLineEndings.WithHardBreaks(file.Body!, role);
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

    /// <summary>The folder of <paramref name="path"/> (<c>a/b</c> of <c>a/b/c.md</c>); none at the top.</summary>
    private static string? FolderOf(string path) => path.LastIndexOf('/') is var slash and > 0 ? path[..slash] : null;
}
