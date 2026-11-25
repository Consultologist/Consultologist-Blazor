using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): exports <see cref="CompiledGoldenTests.Cases"/> into the content repository named by
/// <c>CONFORMANCE_EXPORT_V31</c> (its root): it deletes and rewrites <c>v31/conformance/compiled-documents/</c>, one
/// folder per case (<see cref="Files"/>: the upload under <c>source/</c> byte for byte, <c>toolchain.json</c>, the
/// <c>resolve.json</c> and <c>lock.json</c> publish gives, #1508 (area 11) the <c>publish-report.json</c> and <c>publish.json</c>
/// it writes beside them, and <c>about.txt</c>), then sets the family's <c>cases</c> in
/// <c>v31/conformance/index.json</c> to the case names, leaving every other byte of that file as it was. A no-op without
/// the variable, as <see cref="RefusalPackagesExport"/> is: it is a generator, not a check.
/// <see cref="CompiledGoldenTests.ThePinnedFamily_IsWhatPublishGives"/> holds the pin to it.
/// </summary>
[Collection("V31 conformance export")] // #1505: the exporters each rewrite v31/conformance/index.json, so they run one at a time.
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
            foreach (var (path, bytes) in Files(golden, CompiledGoldenTests.Publish(golden)))
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
    /// <paramref name="published"/> compiled them (canonical, no final newline), #1508 (area 11) its <c>publish-report.json</c>
    /// and its stamp <c>publish.json</c> as publish writes them, and <c>about.txt</c>.
    /// </summary>
    internal static Dictionary<string, byte[]> Files(CompiledGoldenTests.GoldenCase golden, PublishOutcome published)
    {
        var compiled = published.Compiled!;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (path, bytes) in golden.Source())
            files[$"source/{path}"] = bytes;
        files["toolchain.json"] = Utf8NoBom.GetBytes(ToolchainJson(CompiledGoldenTests.Fixed));
        files["resolve.json"] = Utf8NoBom.GetBytes(compiled.Resolve);
        files["lock.json"] = Utf8NoBom.GetBytes(compiled.Lock);
        files[PublishReport.FileName] = published.Files.Single(f => f.Path == PublishReport.FileName).Bytes;
        files[PublishedStamp.FileName] = Utf8NoBom.GetBytes(published.Stamp!.ToJson());
        files["about.txt"] = Utf8NoBom.GetBytes(About(golden, published));
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

    /// <summary>
    /// The case's <c>about.txt</c>: what the package is, how to publish it (the limits, the clock, who publishes), the two
    /// hashes, what the report holds, and where the bodies' reads come from.
    /// </summary>
    private static string About(CompiledGoldenTests.GoldenCase golden, PublishOutcome published)
    {
        var limits = golden.Limits!;
        var report = published.Report;
        var tests = report.Tests.Count == 0
            ? "The package has no tests, so the report's tests are []."
            : $"The report's tests are {string.Join(", ", report.Tests.Select(t => $"{t.Id} ({(t.Pass ? "pass" : "fail")})"))}, run offline on their stubs.";
        return golden.About + "\n\n"
            + "Publishing source/ with toolchain.json gives resolve.json and lock.json, byte for byte (canonical by RFC 8785, "
            + "UTF-8, no final newline), against these publish limits: "
            + string.Create(CultureInfo.InvariantCulture, $"{limits.Ceiling} agent calls at once, {limits.ItemsPerLoop:N0} items a loop, ")
            + string.Create(CultureInfo.InvariantCulture, $"{limits.CallsPerJob:N0} agent calls a job, {limits.Instances:N0} instances a job and {limits.JobMinutes} minutes a job")
            + (limits == CompiledGoldenTests.EngineLimits
                ? " (the engine's defaults)."
                : " (the engine's defaults but the instances, raised so that the package publishes: at 10,000 it is refused, its worst case opening more).")
            + " Any notes they give (the per-job ceiling note among them) are resolve.json's warnings and the report's.\n\n"
            + $"resolve.json: {published.Compiled!.ResolveHash}\n"
            + $"lock.json: {published.Compiled.LockHash}\n\n"
            + "publish-report.json is the report publish writes beside them (canonical, no final newline), published by ci at "
            + "2026-12-01T09:00:00Z on a clock that does not move, so every test's durationMs is 0 (a real publish measures it). "
            + tests + " publish.json is the stamp written last, holding the two hashes above.\n\n"
            + "What each Scriban body reads, places, renders and where its run spots are is what the dialect "
            + "(scriban-dialect.md) finds in the bodies as written.\n";
    }
}
