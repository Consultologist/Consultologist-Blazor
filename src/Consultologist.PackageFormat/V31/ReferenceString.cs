namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): a reference string (part 04 §§ 2.2, 5.2, 7.7; D13): a root, then names joined by
/// dots, as a <c>var</c> path is written (<see cref="JsonLogicShape.IsPath"/>), with no segment of digits (a list is
/// never read by position) and no white space or control character in a name. <see cref="Text"/> is the reference as
/// written; <see cref="Segments"/> its names, never empty.
/// </summary>
public sealed record ReferenceString(string Text, IReadOnlyList<string> Segments)
{
    /// <summary><paramref name="text"/> read as a reference; null when it is not one (null, empty, an empty name, a position, a space).</summary>
    public static ReferenceString? Parse(string? text)
    {
        if (text is null || !JsonLogicShape.IsPath(text) || JsonLogicShape.IsPosition(text)
            || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return null;
        }

        return new ReferenceString(text, text.Split('.'));
    }
}
