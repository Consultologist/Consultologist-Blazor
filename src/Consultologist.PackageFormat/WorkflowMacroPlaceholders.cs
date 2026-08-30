using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>
/// The one grammar of macro placeholders (v11 § 4): {{namespace:id}} over
/// closed namespaces. Publish-time validation (WorkflowPackageValidator) and
/// the run-time expander read tokens through this class only, so the two
/// readers cannot drift — what publish guaranteed is what assembly resolves.
/// </summary>
public static class WorkflowMacroPlaceholders
{
    public static readonly Regex Pattern = new(@"\{\{([^}]*)\}\}", RegexOptions.Compiled);

    /// <summary>The closed run: vocabulary (v11 § 4) — facts about the run itself.</summary>
    public static readonly IReadOnlySet<string> RunFacts =
        new HashSet<string>(StringComparer.Ordinal) { "date", "job", "package", "host" };

    /// <summary>The closed profile: vocabulary (v11 § 4). The signature is § 5's flag, deliberately absent.</summary>
    public static readonly IReadOnlySet<string> ProfileFacts =
        new HashSet<string>(StringComparer.Ordinal) { "name" };

    /// <summary>The token of one match, trimmed — the form every sentence names.</summary>
    public static string TokenOf(Match match) => match.Groups[1].Value.Trim();

    /// <summary>
    /// Splits a trimmed token into namespace and id. False when there is no
    /// namespace — a malformed token, refused at publish and unreachable at
    /// run time.
    /// </summary>
    public static bool TryParse(string token, out string ns, out string id)
    {
        var colon = token.IndexOf(':');
        if (colon <= 0)
        {
            ns = string.Empty;
            id = string.Empty;
            return false;
        }

        ns = token[..colon];
        id = token[(colon + 1)..];
        return true;
    }
}
