using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): the compiled documents of three packages frozen as goldens (part 07 § 1: the same
/// source and the same toolchain give the same bytes; § 4.3). <see cref="CompiledPackage.Publish"/> on
/// <see cref="ResolveFixture.NestedNote"/>, <see cref="ResolveFixture.Conditional"/> and
/// <see cref="ResolveFixture.Functions"/> with <see cref="Fixed"/>, the toolchain written by hand so that a machine's patch
/// level never moves a golden, gives exactly the bytes of <c>Golden/&lt;name&gt;.resolve.json</c> and
/// <c>Golden/&lt;name&gt;.lock.json</c>: canonical, one line, no final newline. With <c>GOLDEN_UPDATE_V31=1</c> the test
/// rewrites them instead; a rewritten golden is read before it is committed. What each body reads is what the dialect
/// says (#1504, area 07, D18: no stub). The same cases are the conformance family <c>compiled-documents</c>
/// (<see cref="CompiledDocumentsExport"/>), which the pinned content repo must hold as published here.
/// </summary>
public class CompiledGoldenTests
{
    /// <summary>One golden: its file name, the package and its <c>about</c>.</summary>
    internal sealed record GoldenCase(string Name, Func<Dictionary<string, byte[]>> Source, string About);

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// The goldens' toolchain (D9's members, by hand): the release and catalogues of v2026.12.1, Scriban 7.2.5, Markdig
    /// 0.41.3, both Corvus libraries 5.8.12, .NET 10.0.0 and Unicode 16.0; the dialect and the evaluator are this
    /// library's own constants, which move a golden only when they change.
    /// </summary>
    public static readonly Toolchain Fixed = new(
        "package-format@v2026.12.1",
        Toolchain.TemplatesDialect,
        new Dictionary<string, string>
        {
            ["Corvus.JsonSchema"] = "5.8.12",
            ["Corvus.Text.Json.JsonLogic"] = "5.8.12",
            ["Markdig"] = "0.41.3",
            ["Scriban"] = "7.2.5",
        },
        JsonLogicOperations.EvaluatorVersion,
        "10.0.0",
        "16.0",
        "formats@v2026.12.1",
        "agents@v2026.12.1",
        null);

    /// <summary>The three goldens, by name.</summary>
    internal static IReadOnlyList<GoldenCase> Cases() =>
    [
        new("nested-note",
            () => Named(ResolveFixture.NestedNote(), "example-nested-note", "Nested note demo"),
            "example-nested-note: an agent planning the sections, a computed count, the loop sections over the plan's "
            + "sections holding an outline and the loop paragraphs over its points, and the while refine of a summary and "
            + "its judge, after chapter 4 § 4.2.8 and § 4.3.5, with the package formats of chapter 7 § 7.2.9."),
        new("conditional",
            () => Named(ResolveFixture.Conditional(), "example-conditional-sections", "Conditional sections demo"),
            "example-conditional-sections: a required list input of sections, each with a title and a kind; a loop "
            + "routing each section by a case on its kind (the omit branch empty), a loop holding an if on the kind, and "
            + "a loop holding an if on the section's id and a boolean input, after chapter 4 § 4.5.4."),
        new("functions",
            () => Named(ResolveFixture.Functions(), "example-functions", "Functions demo"),
            "example-functions, after chapter 4 § 4.7.11 (with an agent plan and a second agent extracting terms, which "
            + "the example does not have): the function draft_section of three agents, called in a loop over the "
            + "snippet folder standards; the check loop section_coverage over the loop's .items and the check "
            + "all_sections; and the deliverable deliverable.emr.consult, gated by all_sections, in the delivery folder "
            + "emr, which delivers to emr and email as one zip."),
    ];

    public static TheoryData<string> Names() => new(Cases().Select(c => c.Name));

    /// <summary><paramref name="files"/> with the manifest's <c>name</c> and <c>title</c> set.</summary>
    private static Dictionary<string, byte[]> Named(Dictionary<string, byte[]> files, string name, string title) =>
        files.WithJson("manifest.json", m =>
        {
            m["name"] = name;
            m["title"] = title;
        });

    /// <summary><see cref="CompiledPackage.Publish"/> of <paramref name="golden"/> with <see cref="Fixed"/>, asserted accepted and complete.</summary>
    internal static CompiledPackage Published(GoldenCase golden)
    {
        var (refusals, compiled) = CompiledPackage.Publish(golden.Source(), ResolveFixture.Published(), Fixed);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}")));
        Assert.NotNull(compiled);
        Assert.True(compiled!.Complete, $"{golden.Name}: a body's reads are not known.");
        return compiled;
    }

    /// <summary>The folder of the goldens in the source tree (<c>tests/V31Resolve/Golden</c>).</summary>
    internal static string GoldenFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "V31Resolve", "Golden");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Publish_GivesTheGolden(string name)
    {
        var golden = Cases().Single(c => c.Name == name);
        var compiled = Published(golden);
        var files = new[] { ($"{name}.resolve.json", compiled.Resolve), ($"{name}.lock.json", compiled.Lock) };

        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
        {
            Directory.CreateDirectory(GoldenFolder());
            foreach (var (file, text) in files)
                File.WriteAllText(Path.Combine(GoldenFolder(), file), text, Utf8NoBom);
        }

        foreach (var (file, text) in files)
        {
            var path = Path.Combine(GoldenFolder(), file);
            Assert.True(File.Exists(path), $"{file} is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.AsSpan().SequenceEqual(Utf8NoBom.GetBytes(text)), $"{file} is not what publish gives.");
            Assert.DoesNotContain('\n', text);
            Assert.Equal(CanonicalJson.Write(JsonNode.Parse(text)), text);
        }
    }

    [Fact]
    public void EveryGolden_VerifiesAgainstItsOwnHashes()
    {
        var names = Cases().Select(c => c.Name).ToList();
        var onDisk = Directory.GetFiles(GoldenFolder()).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(names.SelectMany(n => new[] { $"{n}.lock.json", $"{n}.resolve.json" }).Order(StringComparer.Ordinal), onDisk);

        foreach (var name in names)
        {
            var resolve = File.ReadAllBytes(Path.Combine(GoldenFolder(), $"{name}.resolve.json"));
            var @lock = File.ReadAllBytes(Path.Combine(GoldenFolder(), $"{name}.lock.json"));
            var resolveHash = CanonicalJson.HashText(Utf8NoBom.GetString(resolve));
            var lockHash = CanonicalJson.HashText(Utf8NoBom.GetString(@lock));
            var package = JsonNode.Parse(resolve)!["package"]!;

            Assert.Null(CompiledPackage.Verify((string)package["name"]!, (string)package["version"]!, resolve, @lock, resolveHash, lockHash));
            Assert.Equal(resolveHash, (string?)JsonNode.Parse(@lock)!["resolve"]!["hash"]);
        }
    }

    [Fact]
    public void ThePinnedFamily_IsWhatPublishGives()
    {
        var family = V31Contract.CompiledDocumentsFolder();
        var cases = Cases();
        Assert.Equal(
            cases.Select(c => c.Name).Order(StringComparer.Ordinal),
            Directory.GetDirectories(family).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal));

        foreach (var golden in cases)
        {
            var folder = Path.Combine(family, golden.Name);
            var expected = CompiledDocumentsExport.Files(golden, Published(golden));
            var pinned = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);

            Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), pinned.Keys.Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in expected)
                Assert.True(pinned[path].AsSpan().SequenceEqual(bytes), $"{golden.Name}/{path}: the pinned file is not what publish gives.");
        }

        var index = V31Contract.Json("conformance", "index.json")["families"]!.AsArray()
            .Single(f => (string?)f!["id"] == "compiled-documents")!;
        Assert.Equal(cases.Select(c => c.Name).Order(StringComparer.Ordinal), index["cases"]!.AsArray().Select(c => (string)c!));
    }
}
