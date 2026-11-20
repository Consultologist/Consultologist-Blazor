namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): the bounds of a package source, as part 01 § 7 states them
/// and <c>v31/bounds.json</c> publishes them under <c>sourceBounds</c>. A source over a bound is
/// refused before anything is parsed, so a hostile package costs the reader a count, not a parse.
/// </summary>
public static class SourceBounds
{
    /// <summary>The most files a package may hold.</summary>
    public const int Files = 512;

    /// <summary>The most bytes one file may hold (256 KiB).</summary>
    public const int FileBytes = 262144;

    /// <summary>The most bytes all files may hold together (2 MiB).</summary>
    public const int TotalBytes = 2097152;

    /// <summary>The most characters a file's path may have.</summary>
    public const int PathLength = 200;

    /// <summary>The most characters an id may have.</summary>
    public const int IdLength = 64;

    /// <summary>The deepest a JSON document may nest.</summary>
    public const int JsonDepth = 32;

    /// <summary>The most nodes one expression may have.</summary>
    public const int ExpressionNodes = 1000;
}

/// <summary>
/// The bounds on what a format may do, published in <c>v31/bounds.json</c> under
/// <c>formatBounds</c>.
/// </summary>
public static class FormatBounds
{
    /// <summary>The most passes a format may make.</summary>
    public const int Passes = 15;

    /// <summary>The deepest a format may nest.</summary>
    public const int Depth = 15;

    /// <summary>The most a format may run in parallel.</summary>
    public const int Parallel = 15;
}

/// <summary>
/// The ids no source may use, in the order <c>v31/bounds.json</c> lists them: the names Windows
/// reserves for devices (<c>windowsReservedIds</c>) and the words references read
/// (<c>reservedIds</c>).
/// </summary>
public static class ReservedIds
{
    /// <summary>The device names Windows reserves; none can name a file.</summary>
    public static readonly IReadOnlyList<string> Windows = [
        "con",
        "prn",
        "aux",
        "nul",
        "com1",
        "com2",
        "com3",
        "com4",
        "com5",
        "com6",
        "com7",
        "com8",
        "com9",
        "lpt1",
        "lpt2",
        "lpt3",
        "lpt4",
        "lpt5",
        "lpt6",
        "lpt7",
        "lpt8",
        "lpt9",
    ];

    /// <summary>The words references read as roots or members.</summary>
    public static readonly IReadOnlyList<string> Words = [
        "input",
        "inputs",
        "node",
        "nodes",
        "check",
        "checks",
        "function",
        "functions",
        "test",
        "tests",
        "snippet",
        "snippets",
        "macro",
        "macros",
        "part",
        "parts",
        "renderer",
        "renderers",
        "format",
        "formats",
        "deliverable",
        "deliverables",
        "param",
        "params",
        "item",
        "items",
        "pass",
        "passes",
        "run",
        "runs",
        "profile",
        "profiles",
        "value",
        "values",
        "content",
        "contents",
    ];
}
