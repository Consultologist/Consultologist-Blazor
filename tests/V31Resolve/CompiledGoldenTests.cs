using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): the compiled documents of four packages frozen as goldens (part 07 § 1: the same
/// source and the same toolchain give the same bytes; § 4.3). <see cref="CompiledPackage.Publish"/> on
/// <see cref="ResolveFixture.NestedNote"/>, <see cref="ResolveFixture.Conditional"/>,
/// <see cref="ResolveFixture.Functions"/> and (#1505, area 08) <see cref="ResolveFixture.CheckedNote"/> with
/// <see cref="Fixed"/>, the toolchain written by hand so that a machine's patch level never moves a golden, gives exactly
/// the bytes of <c>Golden/&lt;name&gt;.resolve.json</c> and <c>Golden/&lt;name&gt;.lock.json</c>: canonical, one line, no final newline. With <c>GOLDEN_UPDATE_V31=1</c> the test
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

    /// <summary>The goldens, by name.</summary>
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
        new("checked-note",
            () => Named(ResolveFixture.CheckedNote(), "example-checked-note", "Checked note demo"),
            "example-checked-note, chapter 6 § 6.3.4's package cut down: an agent planning the sections, the loop sections "
            + "drafting each with its planned terms and the terms it covers; the check loop part_coverage over the loop's "
            + ".items, the aggregate all_sections_covered over its .items, and the check proofread, which reads the "
            + "composed note's .content and gates it; the note (markdown_block, delivered to emr, failing when empty) and an "
            + "ungated patient letter (plaintext) in the delivery folder patient, delivered by email and pdf and held when "
            + "empty."),
        new("macros",
            () => Named(ResolveFixture.Macros(), "example-macros", "Macros demo"),
            "example-macros (#1508): an agent planning the sections; the classifier classify, a judgement on the plan; the "
            + "run-dependent rendered node stamp, a run spot on run.date; the agent draft, reading stamp and placing the part "
            + "close, whose text places the macro hpi of the folder section_notes (the folder and the macro each decided on "
            + "the classifier's answer); and the deliverable, reading the draft. The agent terms "
            + "returns a concept_list, which the rendered node terms_text reads through the built-in renderer renderer.concept_bullets."),
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

    [Theory]
    [MemberData(nameof(Names))]
    public void GraphOrder_IsTopological_OverEveryEdgeButGates(string name)
    {
        // #1505 (area 08, D19): every edge but gates goes forward in graph.order, but where one end encloses the other
        // (a flow node comes before its body, and its ret edge comes back from it).
        var document = JsonNode.Parse(Published(Cases().Single(c => c.Name == name)).Resolve)!;
        var order = document["graph"]!["order"]!.AsArray().Select(k => (string)k!).ToList();
        foreach (var edge in document["graph"]!["edges"]!.AsArray())
        {
            var (from, to, kind) = ((string)edge!["from"]!, (string)edge["to"]!, (string)edge["kind"]!);
            if (kind == "gates" || from.StartsWith(to + ".", StringComparison.Ordinal) || to.StartsWith(from + ".", StringComparison.Ordinal)
                || !order.Contains(from) || !order.Contains(to))
            {
                continue;
            }

            Assert.True(order.IndexOf(from) < order.IndexOf(to), $"{name}: {from} -{kind}-> {to} goes back in {string.Join(", ", order)}");
        }

        if (name == "checked-note")
            Assert.True(order.IndexOf("deliverable.note") < order.IndexOf("check.proofread"));
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
    public void EveryGolden_ValidatesAgainstTheSchema()
    {
        // #1508 (area 11): the closed entry names every member the compiler writes, so each golden fits resolve.schema.json
        // and lock.schema.json as the pinned content repository holds them.
        var resolve = ResolveFixture.PinnedSchema("resolve");
        var @lock = ResolveFixture.PinnedSchema("lock");
        foreach (var name in Cases().Select(c => c.Name))
        {
            Assert.True(resolve.Validate(File.ReadAllText(Path.Combine(GoldenFolder(), $"{name}.resolve.json")), null), $"{name}.resolve.json does not fit resolve.schema.json.");
            Assert.True(@lock.Validate(File.ReadAllText(Path.Combine(GoldenFolder(), $"{name}.lock.json")), null), $"{name}.lock.json does not fit lock.schema.json.");
        }
    }

    [Fact]
    public void ABundledEntry_WithAnUnnamedMember_DoesNotFitTheSchema()
    {
        var resolve = ResolveFixture.PinnedSchema("resolve");
        var document = JsonNode.Parse(Published(Cases().Single(c => c.Name == "macros")).Resolve)!;
        Assert.True(resolve.Validate(document.ToJsonString(), null));
        foreach (var map in new[] { "nodes", "checks", "macros", "renderers", "deliverables" })
        {
            var changed = document.DeepClone();
            var entries = changed[map]!.AsObject();
            if (entries.Count == 0)
                continue; // the macros case has no check; every other map has an entry
            entries.First().Value!["madeUp"] = 1;
            Assert.False(resolve.Validate(changed.ToJsonString(), null), $"{map}: an unnamed member still fits.");
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
