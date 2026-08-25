namespace Consultologist.Web.Services.AppUpdate;

/// <summary>
/// #412: the client's build, read from <c>Build:Commit</c> in appsettings.json.
/// The SWA workflow stamps the commit there before Oryx publishes; a build
/// without it (local, or a publish outside that workflow) says so rather than
/// showing nothing.
/// </summary>
public static class BuildLabel
{
    public const string CommitKey = "Build:Commit";

    public const string Unstamped = "local";

    public static string? Commit(IConfiguration configuration)
    {
        var commit = configuration[CommitKey];
        return string.IsNullOrWhiteSpace(commit) ? null : commit.Trim();
    }

    /// <summary>Seven characters, the way git abbreviates, or <see cref="Unstamped"/>.</summary>
    public static string Short(IConfiguration configuration)
    {
        var commit = Commit(configuration);
        return commit is null ? Unstamped : commit[..Math.Min(7, commit.Length)];
    }

    /// <summary>The full commit, or the named unstamped state.</summary>
    public static string Describe(IConfiguration configuration) =>
        Commit(configuration) ?? "Unstamped (local build)";
}
