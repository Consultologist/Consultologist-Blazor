using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): exports <see cref="CompiledGoldenTests.Cases"/> into the content repository named by
/// <c>CONFORMANCE_EXPORT_V31</c> (its root): it deletes and rewrites <c>v31/conformance/compiled-documents/</c>, one
/// folder per case (<see cref="Files"/>: the upload under <c>source/</c> byte for byte, <c>toolchain.json</c>, the
/// <c>resolve.json</c> and <c>lock.json</c> publish gives, and <c>about.txt</c>), then sets the family's <c>cases</c> in
/// <c>v31/conformance/index.json</c> to the case names, leaving every other byte of that file as it was. A no-op without
/// the variable, as <see cref="RefusalPackagesExport"/> is: it is a generator, not a check.
/// <see cref="CompiledGoldenTests.ThePinnedFamily_IsWhatPublishGives"/> holds the pin to it.
/// </summary>
public class CompiledDocumentsExport
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n" };

    [Fact]
    public void Export()
    {
        var root = Environment.GetEnvironmentVariable("CONFORMANCE_EXPORT_V31");
        if (string.IsNullOrWhiteSpace(root))
            return;

        var family = Path.Combine(root, "v31", "conformance", "compiled-documents");
        if (Directory.Exists(family))
            Directory.Delete(family, recursive: true);

        var cases = new List<string>();
        foreach (var golden in CompiledGoldenTests.Cases())
        {
            foreach (var (path, bytes) in Files(golden, CompiledGoldenTests.Published(golden)))
            {
                var file = Path.Combine([family, golden.Name, .. path.Split('/')]);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, bytes);
            }

            cases.Add(golden.Name);
        }

        var index = Path.Combine(root, "v31", "conformance", "index.json");
        File.WriteAllText(
            index,
            RefusalPackagesExport.WithCases(File.ReadAllText(index, Utf8NoBom), "compiled-documents", cases.Order(StringComparer.Ordinal).ToList()),
            Utf8NoBom);
    }

    /// <summary>
    /// The files of <paramref name="golden"/>'s case folder, by path within it: <c>source/&lt;path&gt;</c> for each file of
    /// the upload, <c>toolchain.json</c> (<see cref="ToolchainJson"/>), <c>resolve.json</c> and <c>lock.json</c> as
    /// <paramref name="compiled"/> holds them (canonical, no final newline), and <c>about.txt</c>.
    /// </summary>
    internal static Dictionary<string, byte[]> Files(CompiledGoldenTests.GoldenCase golden, CompiledPackage compiled)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (path, bytes) in golden.Source())
            files[$"source/{path}"] = bytes;
        files["toolchain.json"] = Utf8NoBom.GetBytes(ToolchainJson(CompiledGoldenTests.Fixed));
        files["resolve.json"] = Utf8NoBom.GetBytes(compiled.Resolve);
        files["lock.json"] = Utf8NoBom.GetBytes(compiled.Lock);
        files["about.txt"] = Utf8NoBom.GetBytes(About(golden, compiled));
        return files;
    }

    /// <summary>
    /// <paramref name="toolchain"/> as the case gives it to an implementation, in <c>lock.json</c>'s names
    /// (<c>packageFormat</c>, <c>dialect</c>, <c>libraries</c>, <c>evaluator</c>, <c>runtime</c>, <c>catalogues</c>): 2-space
    /// indentation, <c>\n</c> line ends and a final newline.
    /// </summary>
    internal static string ToolchainJson(Toolchain toolchain)
    {
        var libraries = new JsonObject();
        foreach (var (name, version) in toolchain.Libraries.OrderBy(l => l.Key, StringComparer.Ordinal))
            libraries[name] = version;
        var document = new JsonObject
        {
            ["packageFormat"] = toolchain.PackageFormat,
            ["dialect"] = toolchain.Dialect,
            ["libraries"] = libraries,
            ["evaluator"] = toolchain.Evaluator,
            ["runtime"] = new JsonObject { ["dotnet"] = toolchain.DotNet, ["unicode"] = toolchain.Unicode },
            ["catalogues"] = new JsonObject
            {
                ["agents"] = toolchain.AgentsCatalogue,
                ["formats"] = toolchain.FormatsCatalogue,
                ["outputContracts"] = toolchain.OutputContracts,
            },
        };
        return document.ToJsonString(Indented) + "\n";
    }

    /// <summary>The case's <c>about.txt</c>: what the package is, how to compile it, the two hashes, and where the bodies' reads come from.</summary>
    private static string About(CompiledGoldenTests.GoldenCase golden, CompiledPackage compiled) =>
        golden.About + "\n\n"
        + "Publishing source/ with toolchain.json gives resolve.json and lock.json, byte for byte (canonical by RFC 8785, "
        + "UTF-8, no final newline), with no publish limits, so no ceiling note.\n\n"
        + $"resolve.json: {compiled.ResolveHash}\n"
        + $"lock.json: {compiled.LockHash}\n\n"
        + "What each Scriban body reads, places, renders and where its run spots are is what the dialect "
        + "(scriban-dialect.md) finds in the bodies as written.\n";
}
