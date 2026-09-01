using Microsoft.Extensions.Configuration;

namespace Consultologist.Api;

/// <summary>
/// #596: the accounts are derivable from the geography — the naming rule
/// (storage-separation § 1.1, `consult&lt;role&gt;&lt;region&gt;`) in executable
/// form. One `Storage__Region` setting names the five app-level stores the
/// way one location entry names the client's API; an explicit
/// `{section}:…ServiceUri` setting always wins, and the connection-string
/// rung below stays for local dev. The Functions host's own storage
/// (`AzureWebJobsStorage__*`, the deployment container) is read before app
/// code runs and stays explicit forever — outside this mechanism by
/// construction.
/// </summary>
public static class StorageAccounts
{
    public const string RegionSetting = "Storage:Region";

    internal const string Prefix = "consult";

    // The role words of the naming rule. `host` exists in the rule too but
    // is never derived here — the host's storage is the host's.
    internal const string TextRole = "text";
    internal const string RecordsRole = "jobrecs";
    internal const string PublicRole = "pub";

    /// <summary>
    /// Which account a table-store config section reads when it derives:
    /// the § 1.2 tables and the private packages live on the records
    /// account; the text section on the text account. Unknown sections
    /// derive nothing — their chain falls through unchanged.
    /// </summary>
    internal static string? RoleForSection(string section) => section switch
    {
        "AccountStorage" => RecordsRole,
        "TextStorage" => TextRole,
        "WorkflowPackages" => RecordsRole,
        _ => null,
    };

    /// <summary>The rule itself: https://consult{role}{region}.{service}.core.windows.net</summary>
    internal static string UriFor(string role, string service, string region) =>
        $"https://{Prefix}{role}{region}.{service}.core.windows.net";

    /// <summary>
    /// The middle rung for a known role: the derived URI when the region
    /// is configured, null otherwise (the caller falls through).
    /// </summary>
    public static string? DerivedUri(IConfiguration configuration, string role, string service)
    {
        var region = configuration[RegionSetting];

        return string.IsNullOrWhiteSpace(region) ? null : UriFor(role, service, region);
    }

    /// <summary>
    /// The same rung for the two sites that read raw environment variables
    /// before or outside IConfiguration (Program's catalog pin,
    /// AgentAttestationService).
    /// </summary>
    public static string? DerivedUriFromEnvironment(string role, string service)
    {
        var region = Environment.GetEnvironmentVariable("Storage__Region");

        return string.IsNullOrWhiteSpace(region) ? null : UriFor(role, service, region);
    }

    /// <summary>The rung keyed by section, for the shared table factory.</summary>
    public static string? DerivedUriForSection(IConfiguration configuration, string section, string service) =>
        RoleForSection(section) is { } role ? DerivedUri(configuration, role, service) : null;
}
