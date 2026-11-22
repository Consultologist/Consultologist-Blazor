using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 10 § 5; part 05 §§ 2, 5.3, 6, 7, 8.4; part 06 § 6.4; D7, D13, D17): the dialect
/// frozen as three conformance families, each generated from the code (<see cref="DialectGoldensExport"/>):
/// <c>dialect-goldens</c> (one case per allowed feature of <c>scriban-dialect.md</c> § 10, composed by
/// <see cref="BodyComposer.Compose"/> and, for a macro, finished by <see cref="AgentTurn.Finish"/>),
/// <c>renderer-goldens</c> (each built-in renderer over the 13 concept cases of <see cref="V31BuiltInRenderersTests"/>,
/// piped in a published prompt) and <c>markdown-escape-vectors</c> (<see cref="MarkdownEscape.Escape"/>). The goldens
/// in <c>tests/V31Templates/Golden/&lt;family&gt;/</c> are, byte for byte, what the code gives (with
/// <c>GOLDEN_UPDATE_V31=1</c> the test rewrites them instead; a rewritten golden is read before it is committed), and
/// the families pinned in <c>external/consultologist-package-format</c> are, byte for byte, those goldens. The pinned
/// test fails, never skips, when a family is missing. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class DialectGoldenTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static TheoryData<string> Families() => new(DialectGoldensExport.Families);

    /// <summary>The folder of a family's goldens in the source tree (<c>tests/V31Templates/Golden/&lt;family&gt;</c>).</summary>
    internal static string GoldenFolder(string family)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "V31Templates", "Golden", family);
    }

    /// <summary>Every file under <paramref name="folder"/>, by its path within it (<c>/</c>-separated); empty when the folder is missing.</summary>
    internal static Dictionary<string, byte[]> FilesUnder(string folder) =>
        !Directory.Exists(folder)
            ? new Dictionary<string, byte[]>(StringComparer.Ordinal)
            : Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
                f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Families))]
    public void TheGoldens_AreWhatTheCodeGives(string family)
    {
        var expected = DialectGoldensExport.Files(family);
        var folder = GoldenFolder(family);
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
            DialectGoldensExport.Write(folder, expected);

        var onDisk = FilesUnder(folder);
        Assert.True(onDisk.Count > 0, $"{family}'s goldens are missing: run with GOLDEN_UPDATE_V31=1, then read them.");
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), onDisk.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in expected)
            Assert.True(onDisk[path].AsSpan().SequenceEqual(bytes), $"{family}/{path} is not what the code gives.");
    }

    [Fact]
    public void ThePinnedFamilies_AreTheGoldens()
    {
        var index = V31Contract.Json("conformance", "index.json")["families"]!.AsArray();
        foreach (var family in DialectGoldensExport.Families)
        {
            var goldens = FilesUnder(GoldenFolder(family));
            var pinned = FilesUnder(V31Contract.ConformanceFolder(family));
            Assert.Equal(goldens.Keys.Order(StringComparer.Ordinal), pinned.Keys.Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in goldens)
                Assert.True(pinned[path].AsSpan().SequenceEqual(bytes), $"{family}/{path}: the pinned file is not the golden.");

            var entry = index.Single(f => (string?)f!["id"] == family)!;
            Assert.Equal(DialectGoldensExport.Cases(family), entry["cases"]!.AsArray().Select(c => (string)c!));
        }
    }

    [Fact]
    public void EveryDialectCase_HoldsItsFiles()
    {
        var files = DialectGoldensExport.Files(DialectGoldensExport.DialectGoldens);
        foreach (var golden in DialectGoldensExport.DialectCases())
        {
            string[] inputs = golden.Template != null ? ["template.md"] : ["entry.txt", "package/manifest.json"];
            string[] always = ["about.txt", "scope.json", "placed.json", "expected.txt"];
            foreach (var name in always.Concat(inputs))
                Assert.True(files.ContainsKey($"{golden.Name}/{name}"), $"{golden.Name} has no {name}.");

            // A run spot gives the run-neutral text beside the sent text, and only then (part 05 § 4).
            var spot = golden.Name.StartsWith("run-spot", StringComparison.Ordinal);
            Assert.Equal(spot, files.ContainsKey($"{golden.Name}/expected.neutral.txt"));
            Assert.Equal(golden.Reply != null, files.ContainsKey($"{golden.Name}/expected.reply.txt"));
        }

        // Each built-in with and without null; each position variable; each § 2.6 form placed and absent.
        var names = DialectGoldensExport.DialectCases().Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var builtIn in new[] { "string-upcase", "string-downcase", "string-capitalize", "string-strip", "array-join", "array-size", "date-to-string" })
        {
            Assert.Contains(builtIn, names);
            Assert.Contains(builtIn + "-null", names);
        }

        foreach (var position in new[] { "first", "last", "index", "even", "odd" })
            Assert.Contains("for-" + position, names);
        foreach (var form in new[] { "block", "inline", "alone" })
        {
            Assert.Contains($"placement-{form}-placed", names);
            Assert.Contains($"placement-{form}-absent", names);
        }
    }

    [Fact]
    public void AMacroBodysSnippet_IsPlacedAfterTheMacro_AndDecidedWithinIt()
    {
        // What macro-block's placed.json decides, in the entry's placements order (D2, D3): the macro, then the snippet its
        // body places, within it.
        var golden = DialectGoldensExport.DialectCases().Single(c => c.Name == "macro-block");
        var placements = Consultologist.Api.Tests.V31Run.RunFixture.Plan(golden.Package).Entries["node.golden"].Json["placements"]!.AsArray();

        Assert.Equal(
            [("macro.follow_up", null), ("snippet.clinician", "macro.follow_up")],
            placements.Select(p => ((string)p!["site"]!, (string?)p["within"])));
        Assert.Equal(golden.Placed.Select(p => p.Site), placements.Select(p => (string)p!["site"]!));
    }

    [Fact]
    public void TheRendererGoldens_AreTheCSharpRenderersBytes()
    {
        // Part 05 § 5.3: the C# renderers stay as the byte-equivalence reference.
        var files = DialectGoldensExport.Files(DialectGoldensExport.RendererGoldens);
        foreach (var (name, concepts) in V31BuiltInRenderersTests.Cases)
        {
            var slug = DialectGoldensExport.Slug(name);
            var list = V31BuiltInRenderersTests.ToConcepts(concepts);
            Assert.Equal(ConceptRenderers.Bullets(list), Utf8NoBom.GetString(files[$"concept_bullets/{slug}/expected.txt"]));
            Assert.Equal(ConceptRenderers.Context(list), Utf8NoBom.GetString(files[$"concept_context/{slug}/expected.txt"]));
        }

        Assert.Equal(2 * V31BuiltInRenderersTests.Cases.Count, DialectGoldensExport.Cases(DialectGoldensExport.RendererGoldens).Count);
    }

    [Fact]
    public void TheEscapeVectors_HoldEachPunctuationCharacter_AndTenSentences()
    {
        var vectors = JsonNode.Parse(DialectGoldensExport.Files(DialectGoldensExport.EscapeVectors)["vectors.json"])!.AsArray();
        var plains = vectors.Select(v => (string)v!["plain"]!).ToList();

        var punctuation = Enumerable.Range(0x21, 0x7E - 0x20).Select(c => ((char)c).ToString()).Where(c => !char.IsLetterOrDigit(c[0])).ToList();
        Assert.Equal(32, punctuation.Count);
        Assert.Equal(punctuation, plains.Take(32));
        Assert.Equal(punctuation.Select(c => "\\" + c), vectors.Take(32).Select(v => (string)v!["escaped"]!));
        Assert.Equal(42, vectors.Count);
        Assert.All(vectors, v =>
        {
            Assert.Equal(["plain", "escaped"], v!.AsObject().Select(m => m.Key));
            Assert.Equal(MarkdownEscape.Escape((string)v["plain"]!), (string)v["escaped"]!);
        });
    }
}
