using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>
/// A reply that wraps its JSON in a Markdown code fence (```json … ```) is
/// unwrapped before parsing (package-format-v25-design.md § 3.4): the fence
/// is presentation, not content. Text without a fence is returned as it was.
/// </summary>
public static class WorkflowCodeFences
{
    private static readonly Regex Fenced = new(
        @"\A\s*```[A-Za-z0-9_-]*[ \t]*\r?\n(?<body>[\s\S]*?)\r?\n[ \t]*```\s*\z",
        RegexOptions.Compiled);

    public static string Strip(string text)
    {
        var match = Fenced.Match(text);
        return match.Success ? match.Groups["body"].Value.Trim() : text.Trim();
    }
}
