using System.Xml.Linq;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): the toolchain <c>lock.json</c> records (part 07 § 4.1; D9): the four libraries'
/// versions read off the loaded assemblies, each the version <c>Directory.Packages.props</c> pins; the runtime and its
/// Unicode version; the dialect from <c>constants.json</c>; and what the caller names.
/// </summary>
public class ToolchainTests
{
    /// <summary>Each library key of <c>lock.json</c> and the package <c>Directory.Packages.props</c> pins it under (03-D1).</summary>
    private static readonly (string Key, string Package)[] Libraries =
    [
        ("Scriban", "Scriban"),
        ("Markdig", "Markdig"),
        ("Corvus.Text.Json.JsonLogic", "Corvus.Text.Json.JsonLogic"),
        ("Corvus.JsonSchema", "Corvus.Text.Json.Validator"),
    ];

    private static string Pinned(string package)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var props = XDocument.Load(Path.Combine(dir!.FullName, "Directory.Packages.props"));
        return props.Descendants("PackageVersion")
            .Single(p => (string?)p.Attribute("Include") == package)
            .Attribute("Version")!.Value;
    }

    [Fact]
    public void Current_ReadsTheLoadedLibraries()
    {
        var toolchain = Toolchain.Current("package-format@v2026.12.1", "formats@v2026.12.1", "agents@v2026.12.1");

        Assert.Equal(Libraries.Select(l => l.Key).Order(StringComparer.Ordinal), toolchain.Libraries.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, package) in Libraries)
            Assert.Equal(Pinned(package), toolchain.Libraries[key]);
        Assert.Equal(Pinned("Scriban"), toolchain.Scriban);
        Assert.StartsWith("10.", toolchain.DotNet);
        Assert.Equal("16.0", toolchain.Unicode);
        Assert.Equal(JsonLogicOperations.EvaluatorVersion, toolchain.Evaluator);
    }

    [Fact]
    public void Current_CarriesWhatTheCallerNames()
    {
        var toolchain = Toolchain.Current("package-format@v2026.12.1", "formats@v2026.12.1", "agents@v2026.12.2");
        Assert.Equal("package-format@v2026.12.1", toolchain.PackageFormat);
        Assert.Equal("formats@v2026.12.1", toolchain.FormatsCatalogue);
        Assert.Equal("agents@v2026.12.2", toolchain.AgentsCatalogue);
        Assert.Null(toolchain.OutputContracts);

        Assert.Equal(
            "output-contracts@v2026.12.1",
            Toolchain.Current("package-format@v2026.12.1", "formats@v2026.12.1", "agents@v2026.12.1", "output-contracts@v2026.12.1").OutputContracts);
    }

    [Fact]
    public void Dialect_IsConstantsJsons()
    {
        var constants = V31Contract.Json("constants.json");
        Assert.Equal(
            (string)constants["dialects"]!["templates"]!,
            Toolchain.Current("package-format@v2026.12.1", "formats@v2026.12.1", "agents@v2026.12.1").Dialect);
    }

    [Theory]
    [InlineData(10, "16.0")]
    [InlineData(9, "unknown")]
    [InlineData(11, "unknown")]
    public void Unicode_IsTheTableByMajor(int major, string unicode) =>
        Assert.Equal(unicode, Toolchain.UnicodeOf(major));
}
