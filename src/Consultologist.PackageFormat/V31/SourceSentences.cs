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

    /// <summary>S01-014: the file starts with a byte-order mark.</summary>
    public static SourceRefusal StartsWithByteOrderMark(string path) =>
        new("S01-014", $"File '{path}' starts with a byte-order mark; save it as UTF-8 without one.", path);

    /// <summary>S01-015: a <c>\r</c> that does not end a line, on the 1-based <paramref name="line"/> (counted after <c>\r\n</c> became <c>\n</c>).</summary>
    public static SourceRefusal LoneCarriageReturn(string path, int line) =>
        new("S01-015", $"File '{path}' contains a carriage return on line {line} that doesn't end the line; lines end with \\n.", path);

    /// <summary>A02-002: the bytes are not UTF-8 (D5).</summary>
    public static SourceRefusal NotUtf8(string path) =>
        new("A02-002", $"File '{path}' is not valid UTF-8; save it as UTF-8.", path);

    /// <summary>S01-037: more than <see cref="SourceBounds.Files"/> files.</summary>
    public static SourceRefusal TooManyFiles(int count) =>
        new("S01-037", $"The package has {count} files; the most is SourceBounds.files ({SourceBounds.Files}).", "");

    /// <summary>S01-038: a file over <see cref="SourceBounds.FileBytes"/>; <c>&lt;n&gt;</c> is KiB, rounded up (D3).</summary>
    public static SourceRefusal FileTooLarge(string path, long bytes) =>
        new("S01-038", $"File '{path}' is {Kib(bytes)} KiB; the most is SourceBounds.fileBytes ({SourceBounds.FileBytes / 1024} KiB).", path);

    /// <summary>S01-039: all files over <see cref="SourceBounds.TotalBytes"/>; <c>&lt;n&gt;</c> is MiB, rounded up to one decimal place (D3).</summary>
    public static SourceRefusal PackageTooLarge(long bytes) =>
        new("S01-039", $"The package is {Mib(bytes)} MiB; the most is SourceBounds.totalBytes ({SourceBounds.TotalBytes / 1048576} MiB).", "");

    /// <summary>S01-040: a path over <see cref="SourceBounds.PathLength"/> characters.</summary>
    public static SourceRefusal PathTooLong(string path) =>
        new("S01-040", $"Path '{path}' is {path.Length} characters; the most is SourceBounds.pathLength ({SourceBounds.PathLength}), counted from the package root.", path);

    /// <summary>S01-021: a member the declaration does not write.</summary>
    public static SourceRefusal MemberMissing(string declaration, string member, string path) =>
        new("S01-021", $"{declaration} is missing '{member}'; every member is written, and null means none.", path);

    /// <summary>S01-022: <c>null</c> in a member that can never be null.</summary>
    public static SourceRefusal MemberNull(string declaration, string member, string path) =>
        new("S01-022", $"{declaration} sets '{member}' to null, which it can never be.", path);

    /// <summary>S01-023: a member the declaration's kind does not have; <paramref name="closest"/> is D20's.</summary>
    public static SourceRefusal MemberUnknown(string declaration, string path, string member, string closest) =>
        new("S01-023", $"{declaration} in '{path}' declares unknown member '{member}'. Did you mean '{closest}'?", path);

    /// <summary>S01-024: <c>"description": []</c>.</summary>
    public static SourceRefusal DescriptionEmpty(string declaration, string path) =>
        new("S01-024", $"{declaration} has an empty description; write null for none.", path);

    /// <summary>S01-025: a description of more than 500 code points, its lines joined with <c>\n</c>.</summary>
    public static SourceRefusal DescriptionTooLong(string declaration, int characters, string path) =>
        new("S01-025", $"{declaration}'s description is {characters} characters; the most is 500.", path);

    /// <summary>S01-026: a label that is empty or only whitespace.</summary>
    public static SourceRefusal LabelEmpty(string declaration, string path) =>
        new("S01-026", $"{declaration} has an empty label.", path);

    /// <summary>A02-001 (D7): a member of the wrong JSON type.</summary>
    public static SourceRefusal MemberWrongType(string declaration, string member, string whatItIs, string whatItMustBe, string path) =>
        new("A02-001", $"{declaration} declares '{member}' as {whatItIs}; it is {whatItMustBe}.", path);

    private static string Kib(long bytes) =>
        ((bytes + 1023) / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Mib(long bytes)
    {
        var tenths = (bytes * 10 + 1048575) / 1048576;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{tenths / 10}.{tenths % 10}");
    }
}
