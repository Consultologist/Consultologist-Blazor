namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// Whether a package name belongs to an account rather than the repo. Mirrors
/// <c>Consultologist.Api.Workflow.WorkflowPackageNaming.AccountPrefix</c> by
/// hand, because Consultologist.Web carries no ProjectReference and cannot see
/// the server's constant — the same wall that forced the spec-version mirrors
/// in #376. WorkflowPackageNamesTests pins the two together.
///
/// Note what this answers: <em>is this somebody's fork</em>, not <em>is it
/// mine</em>. The server's CanAccess makes another account's fork unloadable
/// and unpublishable, so for any package a surface here can actually hold, the
/// two coincide. Deriving the caller's own name is not possible client-side
/// anyway: it comes from the AppUserId, which the client never sees.
/// </summary>
public static class WorkflowPackageNames
{
    /// <summary>The prefix the server gives every per-account package.</summary>
    public const string AccountPrefix = "acct-";

    /// <summary>
    /// True for a fork. Accepts a bare name or a full <c>name@version</c> ref,
    /// since callers hold one or the other and the prefix is unambiguous either
    /// way.
    /// </summary>
    public static bool IsAccountPackage(string? nameOrRef) =>
        nameOrRef != null && nameOrRef.StartsWith(AccountPrefix, StringComparison.Ordinal);

    /// <summary>#447: mirrors WorkflowPackageNaming.MaxSlugLength; pinned in SpecVersionMirrorTests.</summary>
    public const int MaxSlugLength = 40;

    /// <summary>
    /// #447: the server's slug rule, mirrored so the desk refuses what the
    /// publish would: the name grammar, at most MaxSlugLength, no trailing
    /// hyphen.
    /// </summary>
    public static bool IsValidSlug(string? slug) =>
        !string.IsNullOrEmpty(slug)
        && slug.Length <= MaxSlugLength
        && !slug.EndsWith('-')
        && System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]*$");
}
