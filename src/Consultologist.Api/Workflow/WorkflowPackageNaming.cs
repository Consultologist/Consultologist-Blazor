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

    /// <summary>The account's further package: its root plus the slug (#447).</summary>
    public static string ForAccount(string appUserId, string slug)
    {
        if (!IsValidSlug(slug))
        {
            throw new ArgumentException($"'{slug}' is not a valid package slug.", nameof(slug));
        }

        return $"{ForAccount(appUserId)}-{slug}";
    }

    /// <summary>
    /// The 12-hex account root of an account-package name — acct-&lt;root&gt; or
    /// acct-&lt;root&gt;-&lt;slug&gt; — or null when the name is not shaped like one.
    /// The backfill maps names to accounts by it.
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
        return rest.Length == 0 || (rest[0] == '-' && IsValidSlug(rest[1..])) ? root : null;
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
