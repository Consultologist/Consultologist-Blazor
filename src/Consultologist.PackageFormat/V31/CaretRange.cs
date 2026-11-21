namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): caret ranges, the only ranges <c>requires</c> writes (part 07 § 4.2; D10). A version
/// is three numeric parts (<c>7.2.5</c>: ASCII digits, each fitting an <see cref="int"/>); <c>^a.b.c</c> admits every
/// version at least <c>a.b.c</c> and below <c>(a+1).0.0</c>. Anything else (a prerelease, two or four parts, spaces,
/// null) is no version: it never admits and is never admitted, and compares as lowest. Never throws.
/// </summary>
public static class CaretRange
{
    /// <summary>The range the registry sets from the locked version <paramref name="exact"/> (§ 4.2): <c>^</c> and it.</summary>
    public static string From(string exact) => "^" + exact;

    /// <summary>Whether <paramref name="range"/>, a caret range, admits <paramref name="version"/>.</summary>
    public static bool Admits(string range, string version)
    {
        if (range is not { Length: > 1 } || range[0] != '^' || Parse(range[1..]) is not { } floor || Parse(version) is not { } found)
            return false;
        return found.Major == floor.Major && Order(found, floor) >= 0;
    }

    /// <summary>
    /// The order of two versions: negative when <paramref name="a"/> is lower, zero when equal, positive when higher. A
    /// text that is no version is lower than every version, and equal to any other that is not.
    /// </summary>
    public static int Compare(string a, string b) => (Parse(a), Parse(b)) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        var (x, y) => Order(x!.Value, y!.Value),
    };

    /// <summary>Whether <paramref name="version"/> is a version: three numeric parts.</summary>
    public static bool IsVersion(string? version) => Parse(version) != null;

    private static int Order((int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        var major = a.Major.CompareTo(b.Major);
        if (major != 0)
            return major;
        var minor = a.Minor.CompareTo(b.Minor);
        return minor != 0 ? minor : a.Patch.CompareTo(b.Patch);
    }

    private static (int Major, int Minor, int Patch)? Parse(string? text)
    {
        if (text == null)
            return null;
        var parts = text.Split('.');
        if (parts.Length != 3 || Part(parts[0]) is not { } major || Part(parts[1]) is not { } minor || Part(parts[2]) is not { } patch)
            return null;
        return (major, minor, patch);
    }

    private static int? Part(string text)
    {
        if (text.Length == 0 || text.Length > 9 || !text.All(char.IsAsciiDigit))
            return null;
        return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }
}
