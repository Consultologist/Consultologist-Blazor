namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): every sentence the type system says, one method each, named
/// after what it says, as <see cref="SourceSentences"/> does for area 02. Each is the row of
/// <c>v31/sentences.json</c> (or the area's own <c>A03-NNN</c>) with its placeholders filled in,
/// character for character. No sentence carries a value from an input or a model: only ids,
/// member names, keywords, JSON pointers, counts and limits. The tasks that follow add theirs here.
/// </summary>
internal static class FormatSentences
{
    /// <summary>
    /// The <c>&lt;Subject&gt;</c> of a sentence about a place in a format (D11): the format as
    /// <paramref name="format"/> names it (<c>Format 'x'</c>, <c>Input 'y'</c>), then
    /// <c>at &lt;path&gt;</c>, with <c>the root</c> for the schema itself.
    /// </summary>
    public static string Subject(string format, string pointer) =>
        $"{format} at {(pointer.Length == 0 ? "the root" : pointer)}";
}
