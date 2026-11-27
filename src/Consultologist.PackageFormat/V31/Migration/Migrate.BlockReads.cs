using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1543 (ruling 2, D7, as the owner amended it on 2026-11-27): a <c>markdown_block</c> read stands alone on its line at 31,
/// and publish refuses one with text beside it (S05-066, S05-067). Where a 30 body has such a read (now a
/// <c>markdown_block</c> value: the profile's signature and closing, a 30 <c>text</c> input and its fields), the migration
/// separates it from that text by a blank line, on both sides, and drops nothing
/// (<see cref="MarkdownLineEndings.WithBlockReadsSeparated"/>): <c>Sincerely, {{ profile.closing }}</c> becomes
/// <c>Sincerely, </c>, an empty line, <c>{{ profile.closing }}</c>; the text after a read starts its own line after a blank
/// line; and a line of text next to a line holding only a block read gets a blank line between them, never a <c>\</c>
/// (<see cref="LineEndingKind.BesideABlockRead"/>). A blank line is right for every value, where <c>\</c> breaks only
/// inside a paragraph and a value may start or end with any block. Where a blank line would break the read's line out of
/// its Markdown block (the text before it starts a list item, a quote or a heading, <c>- Seen: {{ … }}</c>; the text after
/// it would start one, or indented code), nothing on the line is moved and it is a hand edit (A10-094; the owner's item 4
/// as the controller extended it in Task A5's review, for the owner). The sites are the
/// hard-breaks pass's, and the reads are found as publish finds them (<see cref="MarkdownLineEndings.BlockReadsBeside"/>,
/// <see cref="MarkdownLineEndings.Find"/>, each tag's role publish's own, read from the 31 set written so far: every body
/// the checker typed, a file refused before the line rules typing none), so a migrated package with no hand edit is
/// refused and warned neither.
/// </summary>
internal static partial class Migrate
{
    /// <summary>
    /// The block-reads pass, after <see cref="StripLoopItems"/> and directly before <see cref="HardBreaks"/> (which reads
    /// the lines as separated, its roles typed again). Each site whose body has a block read with text beside it, or a line
    /// of text next to a line of only block reads, gets its blank lines, the reads counted in
    /// <see cref="Migrated.MovedReads"/> and said once per file (A10-093); a read on a line that a blank line would break
    /// out of its block is a hand edit (A10-094). Run again, it writes nothing.
    /// </summary>
    public static void BlockReads(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var sites = MarkdownSites(m);
        if (sites.Count > 0)
            BlockReads(m, sites, Read31(m)?.Roles);
    }

    /// <summary>
    /// The two line-ending passes in turn (<see cref="BlockReads"/>, then <see cref="HardBreaks"/>) on one reading of the
    /// 31 set: it is read again for the hard breaks only where the block-reads pass wrote a blank line.
    /// </summary>
    public static void LineEndings(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var sites = MarkdownSites(m);
        if (sites.Count == 0)
            return;
        var read = Read31(m);
        if (BlockReads(m, sites, read?.Roles))
            read = Read31(m);
        HardBreaks(m, sites, read is { } typed ? KeptRoles(typed.Bodies, typed.Resolution, typed.Refusals) : null);
    }

    /// <summary>The block-reads pass on <paramref name="sites"/> with publish's <paramref name="roles"/> (none: nothing is read); whether it wrote anything.</summary>
    private static bool BlockReads(Migrated m, List<string> sites, SiteRoles? roles)
    {
        if (roles is null)
            return false;
        var wrote = false;
        foreach (var path in sites)
        {
            var file = m.Sources[path];
            var body = file.Body!;
            var (text, separated, marked) = MarkdownLineEndings.WithBlockReadsSeparated(body, roles.Of(path, body));
            string Read(TemplateTag t) => roles.ReadOf(path, body, t) ?? body[t.Start..t.End];
            foreach (var tag in marked)
                m.HandEdit(path, "body", MigrationSentences.ReadBreaksItsBlock(Read(tag), tag.Line));
            if (separated.Count == 0)
                continue;
            file.Body = text;
            wrote = true;
            m.MovedReads[path] = m.MovedReads.GetValueOrDefault(path) + separated.Count;
            if (!m.SeparatedReads.TryGetValue(path, out var tags))
                m.SeparatedReads[path] = tags = [];
            tags.AddRange(separated.Select(t => body[t.Start..t.End]));
            m.Notice(MigrationSentences.BlockReadsMoved(path, separated.Select(Read).Distinct(StringComparer.Ordinal).ToList()));
        }

        return wrote;
    }
}
