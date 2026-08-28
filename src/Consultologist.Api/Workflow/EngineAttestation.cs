using System.Reflection;
using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Workflow;

/// <summary>
/// What the deployed engine is, stated by the engine (#449): the commit it was
/// built from, the output-contract catalog it resolved, the format and
/// provenance registry versions it was built against, the spec versions it accepts and runs, and
/// the Scriban the renderer uses. Deployment facts only — nothing here is a
/// secret or names a person — which is what lets Public/Engine be anonymous.
/// The property set is pinned by test: a new field lands through that test or
/// not at all.
/// </summary>
public sealed record EngineAttestationResponse(
    string? Commit,
    string Version,
    string OutputContracts,
    string? PackageFormat,
    string? Provenance,
    IReadOnlyList<int> AcceptedSpecVersions,
    IReadOnlyList<int> SupportedSpecVersions,
    string Scriban,
    DateTimeOffset GeneratedAtUtc,
    // #514: the canonical public host of this deployment (Public__ApiHost) —
    // the data-residency statement every record carries as apiHost. Null
    // when the deployment names none, which the record then says.
    string? ApiHost = null);

public static class EngineAttestation
{
    /// <summary>Where the csproj lands the vendored registry index (relative to the app base).</summary>
    public const string PackageFormatIndexPath = "package-format/spec-versions.json";

    /// <summary>Where the csproj lands the vendored provenance index (#400).</summary>
    public const string ProvenanceIndexPath = "provenance/provenance-versions.json";

    private const int FullCommitLength = 40;

    /// <summary>The rule, separated from reflection and the file system so it can be tested on strings.</summary>
    public static EngineAttestationResponse Describe(string? informationalVersion, string catalogRef, string? packageFormat, string? provenance, DateTimeOffset now, string? apiHost = null)
    {
        var raw = string.IsNullOrWhiteSpace(informationalVersion) ? "unknown" : informationalVersion;
        var separator = raw.IndexOf('+');

        return new EngineAttestationResponse(
            CommitOf(informationalVersion),
            separator < 0 ? raw : raw[..separator],
            catalogRef,
            packageFormat,
            provenance,
            WorkflowPackageValidator.AcceptedSpecVersions,
            WorkflowPackageStore.SupportedSpecVersions,
            WorkflowPackageValidator.EngineScribanVersion.ToString(),
            now,
            ApiHostOf(apiHost));
    }

    /// <summary>
    /// A host name and nothing else: trimmed, lower-cased, no scheme, no path,
    /// no port — so two records of one deployment compare equal. Blank is null.
    /// </summary>
    internal static string? ApiHostOf(string? configured)
    {
        var value = configured?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Contains("://", StringComparison.Ordinal))
        {
            value = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..];
        }

        var end = value.IndexOfAny(new[] { '/', ':', '?', '#' });
        return (end < 0 ? value : value[..end]).ToLowerInvariant();
    }

    /// <summary>The configuration key: <c>Public__ApiHost</c> as an app setting.</summary>
    public const string ApiHostSetting = "Public:ApiHost";

    /// <summary>
    /// The full commit the build stamped as +metadata (-p:SourceRevisionId in the
    /// deploy workflow), or null when the build carried none. Full, not short:
    /// a checkout wants the whole sha, and a prefix could be padded into one.
    /// </summary>
    internal static string? CommitOf(string? informationalVersion)
    {
        if (informationalVersion == null)
        {
            return null;
        }

        var separator = informationalVersion.IndexOf('+');
        if (separator < 0)
        {
            return null;
        }

        foreach (var token in informationalVersion[(separator + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == FullCommitLength && token.All(Uri.IsHexDigit))
            {
                return token.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>The registry version in the copied index, or null when the file is not there.</summary>
    internal static string? PackageFormatVersionIn(string baseDirectory) => VersionIn(baseDirectory, PackageFormatIndexPath);

    internal static string? ProvenanceVersionIn(string baseDirectory) => VersionIn(baseDirectory, ProvenanceIndexPath);

    private static string? VersionIn(string baseDirectory, string indexPath)
    {
        var path = Path.Combine(baseDirectory, indexPath);
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
            ? version.GetString()
            : null;
    }

    /// <summary>A registry ref for a vendored version — `package-format@v…` — or null when the build carries none (#398).</summary>
    public static string? RefOf(string registry, string? version) =>
        string.IsNullOrWhiteSpace(version) ? null : $"{registry}@{version}";

    public const string PackageFormatRegistry = "package-format";
    public const string ProvenanceRegistry = "provenance";

    public static EngineAttestationResponse Current(OutputContractCatalog catalog, string? apiHost = null) =>
        Describe(
            typeof(EngineAttestation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            catalog.ResolvedRef,
            PackageFormatVersionIn(AppContext.BaseDirectory),
            ProvenanceVersionIn(AppContext.BaseDirectory),
            DateTimeOffset.UtcNow,
            apiHost);
}
