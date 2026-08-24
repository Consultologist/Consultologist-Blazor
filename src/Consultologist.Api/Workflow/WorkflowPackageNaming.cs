using Consultologist.PackageFormat;

namespace Consultologist.Api.Workflow;

/// <summary>
/// Account-package naming. An account's first package is "acct-" + the first
/// 12 hex of the AppUserId (a 32-hex GUID string), which fits the package name
/// rule with no hashing; its further packages (#447) are that root plus an
/// author-chosen slug — acct-7bca2dcc1ed4-breast-oncology — so every package
/// of an account shares the root, which is what routes it to the private
/// registry and what #448 later lets become a path. Ownership is a RECORD
/// (WorkflowPackageOwnership), asked through WorkflowPackageAccess — the name
/// says nothing about who may read it (#462). Repo-owned names remain open
/// to all accounts.
/// </summary>
public static partial class WorkflowPackageNaming
{
    public const string AccountPrefix = "acct-";
    private const int AccountIdHexLength = 12;
    public const int MaxSlugLength = 40;

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial System.Text.RegularExpressions.Regex SlugPattern();

    /// <summary>A slug: the package-name grammar, at most MaxSlugLength, not ending in a hyphen.</summary>
    public static bool IsValidSlug(string? slug) =>
        !string.IsNullOrEmpty(slug)
        && slug.Length <= MaxSlugLength
        && !slug.EndsWith('-')
        && SlugPattern().IsMatch(slug);

    /// <summary>
    /// #448: a path under the account's root — one to three slugs joined by
    /// '/', so the whole name (root included) stays within
    /// WorkflowPackageRef.MaxSegments. A single slug is a one-segment path.
    /// </summary>
    public const int MaxPathSegments = WorkflowPackageRef.MaxSegments - 1;

    public static bool IsValidPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var segments = path.Split('/');
        return segments.Length <= MaxPathSegments && segments.All(IsValidSlug);
    }

    /// <summary>
    /// The account's further package: its root, then the path (#448) —
    /// acct-&lt;root&gt;/oncology/breast. The root is the drive; folders follow.
    /// (#447's flat acct-&lt;root&gt;-&lt;slug&gt; names stay legal forever; new ones
    /// take this form.)
    /// </summary>
    public static string ForAccount(string appUserId, string path)
    {
        if (!IsValidPath(path))
        {
            throw new ArgumentException($"'{path}' is not a valid package path.", nameof(path));
        }

        return $"{ForAccount(appUserId)}/{path}";
    }

    /// <summary>
    /// The 12-hex account root of an account-package name — acct-&lt;root&gt;,
    /// acct-&lt;root&gt;-&lt;slug&gt; (#447's flat form) or acct-&lt;root&gt;/&lt;path&gt; (#448) —
    /// or null when the name is not shaped like one.
    /// </summary>
    public static string? AccountRootOf(string name)
    {
        if (!IsAccountPackage(name) || name.Length < AccountPrefix.Length + AccountIdHexLength)
        {
            return null;
        }

        var root = name.Substring(AccountPrefix.Length, AccountIdHexLength);
        if (!root.All(character => char.IsAsciiHexDigitLower(character)))
        {
            return null;
        }

        var rest = name[(AccountPrefix.Length + AccountIdHexLength)..];
        return rest.Length == 0
            || (rest[0] == '-' && IsValidSlug(rest[1..]))
            || (rest[0] == '/' && IsValidPath(rest[1..]))
                ? root
                : null;
    }

    public static string ForAccount(string appUserId)
    {
        var normalized = appUserId?.Trim().ToLowerInvariant()
            ?? throw new ArgumentNullException(nameof(appUserId));

        if (normalized.Length < AccountIdHexLength)
        {
            throw new ArgumentException($"AppUserId '{appUserId}' is too short for account-package naming.", nameof(appUserId));
        }

        return AccountPrefix + normalized[..AccountIdHexLength];
    }

    public static bool IsAccountPackage(string name) =>
        name.StartsWith(AccountPrefix, StringComparison.Ordinal);
}
