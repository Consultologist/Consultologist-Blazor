using Consultologist.PackageFormat;

namespace Consultologist.Api.Workflow;

/// <summary>
/// Account-package naming and the owner-only access rule. An account package is
/// "acct-" + the first 12 hex of the AppUserId (a 32-hex GUID string), which fits
/// the package name rule with no hashing. acct-* names are usable only by their
/// owning account — enforced in the pin resolver and at job start, which is what
/// makes per-account packages safe to introduce
/// (docs/customizable-workflow/in-app-editing.md). Repo-owned names remain open
/// to all accounts.
/// </summary>
public static class WorkflowPackageNaming
{
    public const string AccountPrefix = "acct-";
    private const int AccountIdHexLength = 12;

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

    public static bool CanAccess(string name, string appUserId) =>
        !IsAccountPackage(name) || string.Equals(name, ForAccount(appUserId), StringComparison.Ordinal);
}
