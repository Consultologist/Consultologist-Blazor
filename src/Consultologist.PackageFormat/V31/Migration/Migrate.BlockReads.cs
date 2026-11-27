using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1543 (ruling 2, D7): a <c>markdown_block</c> read stands alone on its line at 31, and publish refuses one with text
/// beside it (S05-066, S05-067). Where a 30 body has such a read (now a <c>markdown_block</c> value: the profile's signature
/// and closing, a 30 <c>text</c> input and its fields), the migration moves the read onto a line of its own and drops
/// nothing (<see cref="MarkdownLineEndings.WithBlockReadsAlone"/>): the text before it ends with <c>\</c>, an explicit hard
/// break, and the read starts the next line (<c>Sincerely,\</c> then <c>{{ profile.closing }}</c>); the text after it starts
/// the next line, after a plain line ending, which 31 reads as a space as 30 read its line. The sites are the hard-breaks
/// pass's, and the reads are found as publish finds them (<see cref="MarkdownLineEndings.BlockReadsBeside"/>, each tag's
/// role publish's own, read from the 31 set written so far: every body the checker typed, a file refused before the
/// line rules typing none), so a migrated package with no hand edit is refused neither.
/// </summary>
internal static partial class Migrate
{
    /// <summary>
    /// The block-reads pass, after <see cref="StripLoopItems"/> and directly before <see cref="HardBreaks"/> (which reads
    /// the lines as moved, its roles typed again). Each site whose body has a block read with text beside it gets the read
    /// on its own line, counted in <see cref="Migrated.MovedReads"/> and said once (A10-093). Run again, it moves nothing.
    /// </summary>
    public static void BlockReads(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var sites = MarkdownSites(m);
        if (sites.Count == 0 || Read31(m)?.Roles is not { } roles)
            return;
        foreach (var path in sites)
        {
            var file = m.Sources[path];
            var body = file.Body!;
            var (text, moved) = MarkdownLineEndings.WithBlockReadsAlone(body, roles.Of(path, body));
            if (moved.Count == 0)
                continue;
            file.Body = text;
            m.MovedReads[path] = m.MovedReads.GetValueOrDefault(path) + moved.Count;
            var reads = moved.Select(t => roles.ReadOf(path, body, t) ?? body[t.Start..t.End]).Distinct(StringComparer.Ordinal).ToList();
            m.Notice(MigrationSentences.BlockReadsMoved(path, reads));
        }
    }
}
