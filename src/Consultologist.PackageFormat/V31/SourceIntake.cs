using System.Text;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499: what intake made of an upload.
/// </summary>
/// <param name="Files">The text of each file that was read, keyed by its path as given and ordinal-sorted; line ends are <c>\n</c>.</param>
/// <param name="Normalised">The files whose <c>\r\n</c> was changed to <c>\n</c>, sorted.</param>
/// <param name="Refusals">Every intake refusal, in the order intake finds them; none when the upload is fit to parse.</param>
public sealed record SourceIntakeResult(
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<string> Normalised,
    IReadOnlyList<SourceRefusal> Refusals)
{
    /// <summary>#1502 (specVersion 31, area 05): the compiled copies, the publish report and the stamp intake dropped unread at the root, sorted (part 07 § 7).</summary>
    public IReadOnlyList<string> Dropped { get; init; } = [];
}

/// <summary>
/// #1499 (specVersion 31, area 02): the first step of reading a package, bytes in and normalised
/// text out (part 01 § 2.4 and § 7). Any refusal here ends the parse (D4): the reader returns
/// only intake's refusals, so a hostile package costs a count, not a parse.
/// </summary>
public static class SourceIntake
{
    /// <summary>
    /// What publish writes at the package root, ignored in an upload: the compiled copies (part 01 § 2)
    /// and the publish report (part 07 § 5 step 0 and § 6); #1508 (area 11, Global Constraints): and the stamp. Dropped
    /// unread and counted against no bound.
    /// </summary>
    private static readonly string[] CompiledCopies = ["resolve.json", "lock.json", "publish-report.json", "publish.json"];

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Reads <paramref name="upload"/> (path to bytes; a path is used as given, nothing is
    /// rewritten). Drops the compiled copies, the publish report and the stamp at the root, naming them in
    /// <see cref="SourceIntakeResult.Dropped"/>; then, for each file in path order,
    /// refuses a byte-order mark (S01-014) and bytes that are not UTF-8 (A02-002), turns
    /// <c>\r\n</c> into <c>\n</c> and refuses a remaining <c>\r</c> (S01-015); then measures the
    /// normalised files against the bounds, reporting every one exceeded in the order of the
    /// table: files (S01-037), each file's bytes (S01-038), the total (S01-039), each path's
    /// length (S01-040). A file refused for its encoding is measured by its raw bytes. Never throws.
    /// </summary>
    public static SourceIntakeResult Read(IReadOnlyDictionary<string, byte[]> upload)
    {
        var paths = new List<string>();
        var dropped = new List<string>();
        foreach (var path in upload.Keys)
        {
            if (CompiledCopies.Contains(path, StringComparer.Ordinal))
                dropped.Add(path);
            else
                paths.Add(path ?? "");
        }

        paths.Sort(StringComparer.Ordinal);
        dropped.Sort(StringComparer.Ordinal);

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var sizes = new List<(string Path, long Bytes)>();
        var normalised = new List<string>();
        var refusals = new List<SourceRefusal>();

        foreach (var path in paths)
        {
            var bytes = upload.TryGetValue(path, out var found) ? found ?? [] : [];
            var refusal = ReadFile(path, bytes, out var text, out var changed);
            if (refusal != null)
            {
                // Refused for its encoding, it is still measured, by the bytes uploaded, so every bound it exceeds is said.
                refusals.Add(refusal);
                sizes.Add((path, bytes.LongLength));
                continue;
            }

            files[path] = text!;
            sizes.Add((path, Encoding.UTF8.GetByteCount(text!)));
            if (changed)
                normalised.Add(path);
        }

        if (paths.Count > SourceBounds.Files)
            refusals.Add(SourceSentences.TooManyFiles(paths.Count));

        foreach (var (path, size) in sizes)
        {
            if (size > SourceBounds.FileBytes)
                refusals.Add(SourceSentences.FileTooLarge(path, size));
        }

        var total = sizes.Sum(s => s.Bytes);
        if (total > SourceBounds.TotalBytes)
            refusals.Add(SourceSentences.PackageTooLarge(total));

        foreach (var path in paths)
        {
            if (path.Length > SourceBounds.PathLength)
                refusals.Add(SourceSentences.PathTooLong(path));
        }

        return new SourceIntakeResult(files, normalised, refusals) { Dropped = dropped };
    }

    private static SourceRefusal? ReadFile(string path, byte[] bytes, out string? text, out bool changed)
    {
        text = null;
        changed = false;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return SourceSentences.StartsWithByteOrderMark(path);

        string decoded;
        try
        {
            decoded = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return SourceSentences.NotUtf8(path);
        }

        var normalised = decoded.Contains("\r\n", StringComparison.Ordinal)
            ? decoded.Replace("\r\n", "\n", StringComparison.Ordinal)
            : decoded;

        var lone = normalised.IndexOf('\r');
        if (lone >= 0)
        {
            var line = 1;
            for (var i = 0; i < lone; i++)
            {
                if (normalised[i] == '\n')
                    line++;
            }

            return SourceSentences.LoneCarriageReturn(path, line);
        }

        text = normalised;
        changed = !ReferenceEquals(normalised, decoded);
        return null;
    }
}
