using System.Globalization;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 8.6, *Invalid replies* V-1 to V-7): the sentences a reply's marker check says,
/// S05-053 to S05-059, built in one place. Each names only macro ids (a folder macro's qualified,
/// <c>section_notes.hpi</c>) and a count, never what the reply holds around them.
/// </summary>
public static class ReplySentences
{
    /// <summary>S05-053 (V-1): an offered macro's marker is not in the reply.</summary>
    public static string NoMarker(string macro) => $"The reply has no marker for macro '{macro}'.";

    /// <summary>S05-054 (V-2): an offered macro's marker is in the reply <paramref name="times"/> times.</summary>
    public static string MarkerTimes(string macro, int times) =>
        $"The reply has the marker for macro '{macro}' {times.ToString(CultureInfo.InvariantCulture)} times.";

    /// <summary>S05-055 (V-3): the reply holds the marker of a macro of the package that was not offered.</summary>
    public static string NotOffered(string macro) => $"The reply has a marker for macro '{macro}', which wasn't offered.";

    /// <summary>S05-056 (V-4): the reply holds a marker whose id is no macro of the package. Fixed text: it quotes nothing
    /// from the reply, since the sentence is kept in the record outside text retention (#1506, ruled by the owner).</summary>
    public static string NotAMacro() => "The reply has a marker that names no macro.";

    /// <summary>
    /// S05-057 (V-5): the marker is written in the other form than the instruction asked: for a block
    /// (<paramref name="askedBlock"/>), inside a sentence; for an inline one, alone on a line.
    /// </summary>
    public static string WrongForm(string macro, bool askedBlock) => askedBlock
        ? $"The reply writes the marker for macro '{macro}' inside a sentence, but it was asked for alone on a line."
        : $"The reply writes the marker for macro '{macro}' alone on a line, but it was asked for inside a sentence.";

    /// <summary>S05-058 (V-6): a block marker is followed on its line by something other than spaces.</summary>
    public static string TextAfterMarker(string macro) => $"The reply has text after the marker for macro '{macro}' on its line.";

    /// <summary>S05-059 (V-7): with <c>ordered</c>, <paramref name="macro"/>'s marker stands before <paramref name="other"/>'s, which the prompt placed first.</summary>
    public static string OutOfOrder(string macro, string other) => $"The reply has the markers out of order: '{macro}' before '{other}'.";
}
