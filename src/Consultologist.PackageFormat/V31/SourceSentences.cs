namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): every sentence the source reader says, one method each,
/// named after what it says. Each is the row of <c>v31/sentences.json</c> (or the area's own
/// <c>A02-NNN</c>) with its placeholders filled in, character for character, and carries that
/// row's id. The tests hold each against the published table.
/// </summary>
internal static class SourceSentences
{
    /// <summary>S01-030: the id does not match <c>^[a-z][a-z0-9_]*$</c>.</summary>
    public static SourceRefusal IdNotValid(string id, string path) =>
        new("S01-030", $"'{id}' is not a valid id: a lowercase letter, then lowercase letters, digits or underscores.", path);

    /// <summary>S01-031: the id is longer than <see cref="SourceBounds.IdLength"/>.</summary>
    public static SourceRefusal IdTooLong(string id, string path) =>
        new("S01-031", $"Id '{id}' is {id.Length} characters; the most is SourceBounds.idLength ({SourceBounds.IdLength}).", path);

    /// <summary>S01-032: the id is a name Windows reserves.</summary>
    public static SourceRefusal IdReservedOnWindows(string id, string path) =>
        new("S01-032", $"Id '{id}' is reserved on Windows (con, prn, aux, nul, com1–com9, lpt1–lpt9) and can't name a file: rename it.", path);

    /// <summary>S01-033: the id is a word references read; <paramref name="rootOrMember"/> is <c>root</c> or <c>member</c>.</summary>
    public static SourceRefusal IdReserved(string id, string rootOrMember, string word, string path) =>
        new("S01-033", $"Id '{id}' is reserved: {rootOrMember} '{word}' is read in references, so nothing may be called '{id}'.", path);
}
