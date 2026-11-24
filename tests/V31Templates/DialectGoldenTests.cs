using System.Security.Cryptography;
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

    /// <summary>
    /// #1505 (area 08): the 40 dialect goldens area 07 exported, each case folder by the SHA-256 of its listing (a line
    /// <c>&lt;sha256 of the file&gt;  &lt;path&gt;\n</c> per file, by path in ordinal order), taken before area 08 added its
    /// cases: the export merges, so each is still there, byte for byte.
    /// </summary>
    private static readonly Dictionary<string, string> AreaSevenCases = new(StringComparer.Ordinal)
    {
        ["array-join"] = "6e4d02e655837778479b15beb52c9fbe487b3c9d5f53d1977ddcf9c808ba1161",
        ["array-join-null"] = "4cc437920bcd3769b922548332f0121ca4eb37bf849bf6f987cb878d06dbcdd3",
        ["array-size"] = "2cbaa56b29931f03256e29bcd91b707541cefbc65cf4d1e44beb1109283c75e6",
        ["array-size-null"] = "9c8cc3c331dc93a0a215c03f6a8ab277207953625a17ea6a0a01042b86c47783",
        ["comment"] = "df5933f2291c75afd7ba7ed908623e7f622a019cfe47bd08c4a491b986dd66f6",
        ["date-to-string"] = "7412c328b97e2fdf68ac5784620de23e7d29fe4658571d768a390e1fb770c92a",
        ["date-to-string-null"] = "2c639782a4a65262674e516ee3adeb13f31de8c07fad62e0b9a14277655fee3d",
        ["folder-item"] = "c52bd647cb3f29acd07d6c6b2751e27592476bceb0400dd2d2fcd882085a82f3",
        ["for-even"] = "14f928b383a4aea7e93d1c7c74025fd3870bc6f24517f192e22099d48eacbdfc",
        ["for-first"] = "68cd12c102b0e7905bc4df2078c69d83b54c6f248d01e2b77d650845401db5f8",
        ["for-index"] = "b06909e0b5de7c35b70c39d90c3101d011a77df2b3c84992e12bf5d4fa22c654",
        ["for-last"] = "5389d7f684ab40e507bc562cfd64d27b2e625b14e5740f0ed0b079c58cc6b66f",
        ["for-odd"] = "7dc323dc72e2c4c38c94b9153129f92ae872c26d5682e67c3b8a80944cbe537b",
        ["if-check-pass"] = "85575203850c3d2d926217fe6e6bc084651842ce481dc7ea1fb0d34de9006d88",
        ["if-state"] = "ea5ef2a54a5416871074191b2d1966459b9999aefc4708c285eb0724186d9a8c",
        ["macro-block"] = "e585a75172661206050e6ed729a50c85f95bfdbb625618ec17e607625e7e98fa",
        ["macro-inline"] = "1bbc2828be9bb8f123515f5aef90e5ca3888ed15a653b118fbe7a18909227ae3",
        ["output-text"] = "9d0787e9c5ae0a948c0b2991d6698447481727bc5c2e1802e7a5db28860fbefc",
        ["output-values"] = "8d9307391f0eee7a9a3fe47bef1909faf870a4c6709c7c27bd48f7ecb3e01f58",
        ["part"] = "e079534cecd2ccf227d17b64c8acf7ae86869edd0d3fde70c9dff9732f46f62a",
        ["pipe-renderer"] = "a59f79d80683294bc6fe26396d9b6ef325b89d742536aa1288ddfe444ec8c099",
        ["placement-alone-absent"] = "3c842a48a4da5a2536de05ce28fb44fbb048b1c5f6874ab046f5909ad649766b",
        ["placement-alone-placed"] = "3a606919dff063a58a1c376e6b38057e4554fb884f85b43e894d7ed19851f6ce",
        ["placement-block-absent"] = "c6c73cb7f02e83f59c413d06bdc37d292571b03bc8234a31ad16b2be9b0c528b",
        ["placement-block-placed"] = "9b4882a03977f35048929d549fa54d7511bdac74cf43ca83ba8f0f807b23317f",
        ["placement-inline-absent"] = "7d4ee0669516105d1584b48a37a7ffc636000bb3943ed02093158b2fac3a3954",
        ["placement-inline-placed"] = "ec5dfaa2ad958ee92bcdbe85a843df22d13882107dad426609d2841989d86641",
        ["raw-block"] = "86d5c1fc040250ccba08913ea066a74fe043e5d6a5d95aec4838611701ee337e",
        ["renderer-if-value"] = "a6e7f339bdd810d73ee0c9d56dced8ec86203b73a4f66ee41cf43b818f933b49",
        ["run-spot"] = "aecfc800c1a5061a7309762e535fc2ea7fa5aaea043c5f8327cbe53d9416c6dd",
        ["snippet"] = "98e51273122ab7420a41df244eb844875036cfb7299be2238f4ef1c2b7abe9cd",
        ["string-capitalize"] = "a58aa57b133cceb9a06e17e021b5b9bb4cc66421fb8084410d726d51231cc99f",
        ["string-capitalize-null"] = "a4a0300f9025b711f35f1dcd22f3ca3aabce1368d65ec14cbaad36040d520f27",
        ["string-downcase"] = "4b9515c5160e2c9ab6c90747f55e6dfec979ef8f36f1b138cfdbab04a09a3d8c",
        ["string-downcase-null"] = "24202b632863ec34ff3d888582c019793819ed441bd392feca85bf09fbf9db9b",
        ["string-strip"] = "95a914b50b7e83b599eb741be77852335af3a1d906c40f7c81321a01354717e7",
        ["string-strip-null"] = "90489764f687b4f6a11d8c985e7d1c847b8f78e7581390ae6168fd57ad4b6054",
        ["string-upcase"] = "c3242053fdda35ab1ba63339a6e009a2723ef2a76a6321060c64c628c40bf608",
        ["string-upcase-null"] = "18cfed8ed6157cb91e92e53e6decf29c02495ab7852f772328e051c0dc9536cf",
        ["tilde-statements"] = "3853b6913e71f2b2075a7445a34ef4efd134476a7903405dad23943d58fd5500",
    };

    [Fact]
    public void TheCasesAreaSevenExported_AreUnchanged()
    {
        Assert.Equal(40, AreaSevenCases.Count);
        var files = DialectGoldensExport.Files(DialectGoldensExport.DialectGoldens);
        foreach (var (name, hash) in AreaSevenCases)
        {
            var listing = new StringBuilder();
            foreach (var (path, bytes) in files.Where(f => f.Key.StartsWith(name + "/", StringComparison.Ordinal)).OrderBy(f => f.Key, StringComparer.Ordinal))
                listing.Append(Convert.ToHexStringLower(SHA256.HashData(bytes))).Append("  ").Append(path[(name.Length + 1)..]).Append('\n');
            Assert.True(listing.Length > 0, $"{name} is no longer exported.");
            Assert.True(hash == Convert.ToHexStringLower(SHA256.HashData(Utf8NoBom.GetBytes(listing.ToString()))), $"{name} is not what area 07 exported.");
        }
    }

    /// <summary>
    /// #1505 comment 4, ruling 3 (R11): the 26 renderer goldens at the pin before area 08's Task 15 (bdc1ee6), by name. The
    /// export adds to the family and never drops one of these; their bytes are pinned by <see cref="ThePinnedFamilies_AreTheGoldens"/>.
    /// </summary>
    private static readonly string[] RendererCasesAtThePin =
    [
        .. new[] { "concept_bullets", "concept_context" }.SelectMany(r => new[]
        {
            "different-source", "empty", "empty-source", "empty-support", "markdown-characters", "one-active-snomed",
            "one-inactive", "one-not-snomed", "tab-and-newline-only-support", "three-mixed", "whitespace-support",
            "with-support", "without-support",
        }.Select(c => $"{r}/{c}")),
    ];

    /// <summary>
    /// R11: the 42 escape vectors at the pin before Task 15 (bdc1ee6), by their <c>plain</c> (a vector's name): each of the
    /// 32 ASCII punctuation characters, then the ten sentences. A vector's <c>escaped</c> may change (R9); a vector may not vanish.
    /// </summary>
    private static readonly string[] EscapeVectorsAtThePin =
    [
        .. "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~".Select(c => c.ToString()),
        "Margaret Okafor, 58, seen by Dr A. Sample on 20/11/2026.",
        "BP 128/82 mmHg; HR 88 bpm (regular); SpO2 98% on air.",
        "Plan: FBC + ferritin -> review in 2 weeks [if Hb < 110 g/L].",
        "* Not a bullet: she said \"I'm tired all the time\" & wants answers!",
        "# Not a heading; _not emphasis_ and `not code` either.",
        "Metformin 500 mg BD ~ 1 g/day; aim HbA1c <= 53 mmol/mol.",
        "1. Not a list item: Dr A. Sample's {follow-up} at 09:30 = done?",
        "Allergies: penicillin (rash) | none known @ home; see note #4.",
        "    Seen with her daughter; the four spaces before this line stay as written.",
        "Fatigue – two weeks, worse ≥ 3 pm; café visit with her daughter, née Adeyemi.\nSecond line: \\ back-slashed.",
    ];

    [Fact]
    public void TheSharedFamilies_KeepEveryCase()
    {
        Assert.Equal(26, RendererCasesAtThePin.Length);
        var renderers = DialectGoldensExport.Cases(DialectGoldensExport.RendererGoldens);
        foreach (var name in RendererCasesAtThePin)
            Assert.True(renderers.Contains(name, StringComparer.Ordinal), $"renderer-goldens/{name} is no longer exported.");

        Assert.Equal(42, EscapeVectorsAtThePin.Length);
        var plains = JsonNode.Parse(DialectGoldensExport.Files(DialectGoldensExport.EscapeVectors)["vectors.json"])!.AsArray()
            .Select(v => (string)v!["plain"]!).ToList();
        foreach (var plain in EscapeVectorsAtThePin)
            Assert.True(plains.Contains(plain, StringComparer.Ordinal), $"The escape vector of {JsonValue.Create(plain).ToJsonString()} is no longer exported.");
    }

    [Fact]
    public void TheExport_KeepsTheCasesItDoesNotProduce()
    {
        // R11: a case already in the content repository that the export does not produce (another area's) is kept, files
        // and index entry, in each of the three shared families; a case it produces is written afresh.
        var root = Path.Combine(Path.GetTempPath(), "v31-merge-" + Guid.NewGuid().ToString("N"));
        var conformance = Path.Combine(root, "v31", "conformance");
        try
        {
            Directory.CreateDirectory(conformance);
            File.Copy(Path.Combine(V31Contract.ConformanceFolder(DialectGoldensExport.DialectGoldens), "..", "index.json"), Path.Combine(conformance, "index.json"));
            var index = File.ReadAllText(Path.Combine(conformance, "index.json"), Utf8NoBom);
            foreach (var (family, kept) in new[] { (DialectGoldensExport.DialectGoldens, "kept-case"), (DialectGoldensExport.RendererGoldens, "concept_kept/empty") })
            {
                DialectGoldensExport.Write(Path.Combine(conformance, family), new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    [$"{kept}/about.txt"] = Utf8NoBom.GetBytes("Another area's case.\n"),
                    [$"{DialectGoldensExport.Cases(family)[0]}/stale.txt"] = Utf8NoBom.GetBytes("A file the export no longer writes.\n"),
                });
                index = RefusalPackagesExport.WithCases(index, family, [kept, .. DialectGoldensExport.Cases(family)]);
            }

            var keptVector = new JsonObject { ["plain"] = "Kept by another area.", ["escaped"] = "Kept by another area\\." };
            DialectGoldensExport.Write(Path.Combine(conformance, DialectGoldensExport.EscapeVectors), new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["vectors.json"] = Utf8NoBom.GetBytes(new JsonArray(keptVector, new JsonObject { ["plain"] = "!", ["escaped"] = "stale" }).ToJsonString()),
            });
            File.WriteAllText(Path.Combine(conformance, "index.json"), index, Utf8NoBom);

            DialectGoldensExport.ExportTo(root);

            var after = JsonNode.Parse(File.ReadAllText(Path.Combine(conformance, "index.json"), Utf8NoBom))!["families"]!.AsArray();
            foreach (var (family, kept) in new[] { (DialectGoldensExport.DialectGoldens, "kept-case"), (DialectGoldensExport.RendererGoldens, "concept_kept/empty") })
            {
                var files = FilesUnder(Path.Combine(conformance, family));
                Assert.True(files.ContainsKey($"{kept}/about.txt"), $"{family}/{kept} was dropped.");
                Assert.False(files.ContainsKey($"{DialectGoldensExport.Cases(family)[0]}/stale.txt"), $"{family}: a produced case keeps a stale file.");
                foreach (var (path, bytes) in DialectGoldensExport.Files(family))
                    Assert.True(files[path].AsSpan().SequenceEqual(bytes), $"{family}/{path} is not what the code gives.");
                var cases = after.Single(f => (string?)f!["id"] == family)!["cases"]!.AsArray().Select(c => (string)c!).ToList();
                Assert.Equal([.. DialectGoldensExport.Cases(family).Append(kept).Order(StringComparer.Ordinal)], cases);
            }

            var vectors = JsonNode.Parse(File.ReadAllBytes(Path.Combine(conformance, DialectGoldensExport.EscapeVectors, "vectors.json")))!.AsArray();
            var produced = JsonNode.Parse(DialectGoldensExport.Files(DialectGoldensExport.EscapeVectors)["vectors.json"])!.AsArray();
            Assert.Equal(produced.Count + 1, vectors.Count);
            Assert.Equal(produced.ToJsonString(), new JsonArray(vectors.Take(produced.Count).Select(v => v!.DeepClone()).ToArray()).ToJsonString());
            Assert.Equal(keptVector.ToJsonString(), vectors[^1]!.ToJsonString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheExport_RecomputesAKeptVectorsEscaped()
    {
        // #1507, 2026-11-22 (2), 3 (R10): a vector the export keeps (another area's) is exported with its escaped computed
        // by MarkdownEscape.Escape, never as written, so a stale one cannot reach the freeze.
        var root = Path.Combine(Path.GetTempPath(), "v31-merge-" + Guid.NewGuid().ToString("N"));
        var conformance = Path.Combine(root, "v31", "conformance");
        try
        {
            Directory.CreateDirectory(conformance);
            File.Copy(Path.Combine(V31Contract.ConformanceFolder(DialectGoldensExport.DialectGoldens), "..", "index.json"), Path.Combine(conformance, "index.json"));
            const string plain = "# Kept by another area, for Dr A. Sample";
            DialectGoldensExport.Write(Path.Combine(conformance, DialectGoldensExport.EscapeVectors), new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["vectors.json"] = Utf8NoBom.GetBytes(new JsonArray(new JsonObject { ["plain"] = plain, ["escaped"] = "stale" }).ToJsonString()),
            });

            DialectGoldensExport.ExportTo(root);

            var vectors = JsonNode.Parse(File.ReadAllBytes(Path.Combine(conformance, DialectGoldensExport.EscapeVectors, "vectors.json")))!.AsArray();
            var kept = Assert.Single(vectors, v => (string?)v!["plain"] == plain)!;
            Assert.Equal(["plain", "escaped"], kept.AsObject().Select(m => m.Key));
            Assert.Equal(MarkdownEscape.Escape(plain), (string?)kept["escaped"]);
            Assert.All(vectors, v => Assert.Equal(MarkdownEscape.Escape((string)v!["plain"]!), (string?)v["escaped"]));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
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
    public void TheEscapeVectors_HoldEachPunctuationCharacter_AndFifteenSentences()
    {
        var vectors = JsonNode.Parse(DialectGoldensExport.Files(DialectGoldensExport.EscapeVectors)["vectors.json"])!.AsArray();
        var plains = vectors.Select(v => (string)v!["plain"]!).ToList();

        var punctuation = Enumerable.Range(0x21, 0x7E - 0x20).Select(c => ((char)c).ToString()).Where(c => !char.IsLetterOrDigit(c[0])).ToList();
        Assert.Equal(32, punctuation.Count);
        Assert.Equal(punctuation, plains.Take(32));
        Assert.Equal(punctuation.Select(c => "\\" + c), vectors.Take(32).Select(v => (string)v!["escaped"]!));
        Assert.Equal(47, vectors.Count);
        Assert.Equal(EscapeVectorsAtThePin, plains.Take(42));
        Assert.All(vectors, v =>
        {
            Assert.Equal(["plain", "escaped"], v!.AsObject().Select(m => m.Key));
            Assert.Equal(MarkdownEscape.Escape((string)v["plain"]!), (string)v["escaped"]!);
        });
    }
}
