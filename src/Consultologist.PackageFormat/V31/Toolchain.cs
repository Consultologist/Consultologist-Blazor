using System.Globalization;
using System.Reflection;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the toolchain that checked a package, which <c>lock.json</c> records (part 07 § 4.1;
/// D9). <paramref name="PackageFormat"/> is the format library and registry release (<c>package-format@v2026.12.1</c>),
/// <paramref name="Dialect"/> the Scriban dialect spec's version (<c>scriban-dialect@1.0.0</c>),
/// <paramref name="Libraries"/> each locked library's exact version by its key (<c>Scriban</c>, <c>Markdig</c>,
/// <c>Corvus.Text.Json.JsonLogic</c>, <c>Corvus.JsonSchema</c>), <paramref name="Evaluator"/> our evaluator's exact
/// version, <paramref name="DotNet"/> and <paramref name="Unicode"/> the runtime's, and the catalogues' versions
/// (<paramref name="OutputContracts"/> null when none is named). The caller's: a test builds one by hand.
/// </summary>
public sealed record Toolchain(
    string PackageFormat,
    string Dialect,
    IReadOnlyDictionary<string, string> Libraries,
    string Evaluator,
    string DotNet,
    string Unicode,
    string FormatsCatalogue,
    string AgentsCatalogue,
    string? OutputContracts)
{
    /// <summary>The Scriban dialect spec's version, <c>constants.json</c>'s <c>dialects.templates</c>.</summary>
    public const string TemplatesDialect = "scriban-dialect@1.0.0";

    /// <summary>The locked Scriban version, which <c>requires.scriban</c>'s range is taken from (D8).</summary>
    public string Scriban => Libraries["Scriban"];

    /// <summary>
    /// The toolchain of this process (D9): the release and the catalogues as the caller names them; the four libraries'
    /// versions read off their loaded assemblies (<c>Corvus.Text.Json.Validator</c>'s under the key
    /// <c>Corvus.JsonSchema</c>, 03-D1), each the package's version trimmed to <c>major.minor.patch</c>; the evaluator's
    /// version; <see cref="Environment.Version"/> for the runtime, and its Unicode version by major version.
    /// </summary>
    public static Toolchain Current(string packageFormat, string formatsCatalogue, string agentsCatalogue, string? outputContracts = null)
    {
        ArgumentNullException.ThrowIfNull(packageFormat);
        ArgumentNullException.ThrowIfNull(formatsCatalogue);
        ArgumentNullException.ThrowIfNull(agentsCatalogue);

        // Each assembly is reached by a type it defines, so it is loaded if it was not.
        var libraries = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["Scriban"] = VersionOf(typeof(global::Scriban.Template).Assembly),
            ["Markdig"] = VersionOf(typeof(global::Markdig.Markdown).Assembly),
            ["Corvus.Text.Json.JsonLogic"] = VersionOf(typeof(global::Corvus.Text.Json.JsonLogic.JsonLogicEvaluator).Assembly),
            ["Corvus.JsonSchema"] = VersionOf(typeof(global::Corvus.Text.Json.Validator.JsonSchema).Assembly),
        };
        var runtime = Environment.Version;
        return new Toolchain(
            packageFormat,
            TemplatesDialect,
            libraries,
            JsonLogicOperations.EvaluatorVersion,
            Version3(runtime.Major, runtime.Minor, runtime.Build),
            UnicodeOf(runtime.Major),
            formatsCatalogue,
            agentsCatalogue,
            outputContracts);
    }

    /// <summary>The Unicode version a .NET major version's text handling follows (D9): 10 → <c>16.0</c>; any other, <c>unknown</c>.</summary>
    internal static string UnicodeOf(int major) => major switch
    {
        10 => "16.0",
        _ => "unknown",
    };

    /// <summary>
    /// The package version of <paramref name="assembly"/>: its informational version (NuGet's, without <c>-prerelease</c>
    /// or <c>+metadata</c>), else its file version, else its assembly version, as <c>major.minor.patch</c>.
    /// </summary>
    private static string VersionOf(Assembly assembly)
    {
        foreach (var text in new[]
        {
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version,
        })
        {
            if (text != null && Version.TryParse(text.Split('+', '-')[0], out var parsed))
                return Version3(parsed.Major, parsed.Minor, parsed.Build);
        }

        var version = assembly.GetName().Version ?? new Version(0, 0, 0);
        return Version3(version.Major, version.Minor, version.Build);
    }

    private static string Version3(int major, int minor, int patch) =>
        string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{Math.Max(patch, 0)}");
}
