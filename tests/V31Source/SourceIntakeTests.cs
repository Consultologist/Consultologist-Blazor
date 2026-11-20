using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>#1499: intake — encoding, line endings and the size bounds (part 01 § 2.4 and § 7).</summary>
public class SourceIntakeTests
{
    private const string Path = "nodes/plan.md";

    private static Dictionary<string, byte[]> One(string path, byte[] bytes) => new() { [path] = bytes };

    private static Dictionary<string, byte[]> One(string path, string text) => One(path, SourceFixture.Utf8(text));

    [Fact]
    public void Intake_NormalisesCrLf_BeforeTheFenceIsRead()
    {
        var result = SourceIntake.Read(One(Path, "---\r\n{}\r\n---\r\nA\r\n"));

        Assert.Empty(result.Refusals);
        Assert.Equal("---\n{}\n---\nA\n", result.Files[Path]);
        Assert.Equal([Path], result.Normalised);
        Assert.NotNull(FrontMatter.Split(result.Files[Path]));
    }

    [Fact]
    public void Intake_LeavesAFileWithoutCrLfAlone_AndSortsTheFiles()
    {
        var upload = new Dictionary<string, byte[]>
        {
            ["nodes/b.md"] = SourceFixture.Utf8("b\r\n"),
            ["nodes/a.md"] = SourceFixture.Utf8("a\n"),
            ["Z.md"] = SourceFixture.Utf8("z\r\n"),
        };

        var result = SourceIntake.Read(upload);

        Assert.Equal(["Z.md", "nodes/a.md", "nodes/b.md"], result.Files.Keys);
        Assert.Equal(["Z.md", "nodes/b.md"], result.Normalised);
    }

    [Fact]
    public void Intake_RefusesAByteOrderMark()
    {
        var result = SourceIntake.Read(One(Path, [0xEF, 0xBB, 0xBF, (byte)'a']));

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("S01-014", refusal.Id);
        Assert.Equal("File 'nodes/plan.md' starts with a byte-order mark; save it as UTF-8 without one.", refusal.Sentence);
        Assert.Equal(Path, refusal.Path);
        Assert.DoesNotContain(Path, result.Files.Keys);
    }

    [Fact]
    public void Intake_RefusesALoneCarriageReturn_WithItsLine()
    {
        var result = SourceIntake.Read(One(Path, "a\nb\rc\n"));

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("S01-015", refusal.Id);
        Assert.Equal(
            "File 'nodes/plan.md' contains a carriage return on line 2 that doesn't end the line; lines end with \\n.",
            refusal.Sentence);
        Assert.Empty(result.Normalised);
    }

    [Fact]
    public void Intake_CountsLinesAfterCrLfIsNormalised()
    {
        var result = SourceIntake.Read(One(Path, "a\r\nb\r\nc\rd"));

        Assert.Contains("on line 3 ", Assert.Single(result.Refusals).Sentence);
    }

    [Fact]
    public void Intake_RefusesAFinalCarriageReturn()
    {
        var result = SourceIntake.Read(One(Path, "a\r"));

        Assert.Contains("on line 1 ", Assert.Single(result.Refusals).Sentence);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41 })]
    [InlineData(new byte[] { 0xC3, 0x28 })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]
    [InlineData(new byte[] { 0xF0, 0x9F })]
    public void Intake_RefusesBytesThatAreNotUtf8(byte[] bytes)
    {
        var result = SourceIntake.Read(One(Path, bytes));

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("A02-002", refusal.Id);
        Assert.Equal("File 'nodes/plan.md' is not valid UTF-8; save it as UTF-8.", refusal.Sentence);
        Assert.Equal(Path, refusal.Path);
    }

    [Fact]
    public void Intake_ReadsMultiByteUtf8()
    {
        var result = SourceIntake.Read(One(Path, "Zoë – 患者 😀\n"));

        Assert.Empty(result.Refusals);
        Assert.Equal("Zoë – 患者 😀\n", result.Files[Path]);
    }

    [Fact]
    public void Intake_DropsCompiledCopiesAtTheRoot()
    {
        var upload = new Dictionary<string, byte[]>
        {
            ["resolve.json"] = new byte[SourceBounds.FileBytes + 1],
            ["lock.json"] = [0xFF],
            ["nodes/resolve.json"] = SourceFixture.Utf8("{}"),
            ["manifest.json"] = SourceFixture.Utf8("{}"),
        };

        var result = SourceIntake.Read(upload);

        Assert.Empty(result.Refusals);
        Assert.Equal(["manifest.json", "nodes/resolve.json"], result.Files.Keys);
    }

    [Fact]
    public void Intake_DoesNotCountTheDroppedCopies()
    {
        var upload = Enumerable.Range(0, SourceBounds.Files)
            .ToDictionary(i => $"f{i:D4}.md", _ => SourceFixture.Utf8("x"));
        upload["resolve.json"] = SourceFixture.Utf8("{}");
        upload["lock.json"] = SourceFixture.Utf8("{}");

        Assert.Empty(SourceIntake.Read(upload).Refusals);
    }

    [Fact]
    public void Intake_Counts513Files()
    {
        var over = Enumerable.Range(0, SourceBounds.Files + 1)
            .ToDictionary(i => $"f{i:D4}.md", _ => SourceFixture.Utf8("x"));
        var refusal = Assert.Single(SourceIntake.Read(over).Refusals);
        Assert.Equal("S01-037", refusal.Id);
        Assert.Equal("The package has 513 files; the most is SourceBounds.files (512).", refusal.Sentence);
        Assert.Equal("", refusal.Path);

        over.Remove("f0000.md");
        Assert.Empty(SourceIntake.Read(over).Refusals);
    }

    [Fact]
    public void Intake_MeasuresAFileAfterNormalisation()
    {
        Assert.Empty(SourceIntake.Read(One(Path, new string('a', SourceBounds.FileBytes))).Refusals);

        var refusal = Assert.Single(SourceIntake.Read(One(Path, new string('a', SourceBounds.FileBytes + 1))).Refusals);
        Assert.Equal("S01-038", refusal.Id);
        Assert.Equal("File 'nodes/plan.md' is 257 KiB; the most is SourceBounds.fileBytes (256 KiB).", refusal.Sentence);
        Assert.Equal(Path, refusal.Path);

        // 262143 'a' and a CRLF is 262145 bytes as uploaded but 262144 once normalised, so it passes.
        var crlf = new string('a', SourceBounds.FileBytes - 1) + "\r\n";
        Assert.Empty(SourceIntake.Read(One(Path, crlf)).Refusals);
    }

    [Fact]
    public void Intake_MeasuresBytesNotCharacters()
    {
        // 2 bytes each: 131073 of them is 262146 bytes.
        var refusal = Assert.Single(SourceIntake.Read(One(Path, new string('é', SourceBounds.FileBytes / 2 + 1))).Refusals);
        Assert.Equal("S01-038", refusal.Id);
        Assert.Empty(SourceIntake.Read(One(Path, new string('é', SourceBounds.FileBytes / 2))).Refusals);
    }

    [Fact]
    public void Intake_MeasuresThePackage()
    {
        var nine = Enumerable.Range(0, 9)
            .ToDictionary(i => $"nodes/f{i}.md", _ => SourceFixture.Utf8(new string('a', SourceBounds.FileBytes)));

        var refusal = Assert.Single(SourceIntake.Read(nine).Refusals);
        Assert.Equal("S01-039", refusal.Id);
        Assert.Equal("The package is 2.3 MiB; the most is SourceBounds.totalBytes (2 MiB).", refusal.Sentence);

        nine.Remove("nodes/f0.md");
        Assert.Empty(SourceIntake.Read(nine).Refusals);
    }

    [Fact]
    public void Intake_AcceptsExactlyTwoMiB_AndRoundsTheOverageUp()
    {
        var eight = Enumerable.Range(0, 8)
            .ToDictionary(i => $"nodes/f{i}.md", _ => SourceFixture.Utf8(new string('a', SourceBounds.FileBytes)));
        Assert.Empty(SourceIntake.Read(eight).Refusals);

        eight["nodes/extra.md"] = SourceFixture.Utf8("a");
        Assert.Equal(
            "The package is 2.1 MiB; the most is SourceBounds.totalBytes (2 MiB).",
            Assert.Single(SourceIntake.Read(eight).Refusals).Sentence);
    }

    [Fact]
    public void Intake_MeasuresAPath()
    {
        var ok = "nodes/" + new string('a', SourceBounds.PathLength - "nodes/".Length);
        Assert.Equal(SourceBounds.PathLength, ok.Length);
        Assert.Empty(SourceIntake.Read(One(ok, "x")).Refusals);

        var long201 = ok + "a";
        var refusal = Assert.Single(SourceIntake.Read(One(long201, "x")).Refusals);
        Assert.Equal("S01-040", refusal.Id);
        Assert.Equal(
            $"Path '{long201}' is 201 characters; the most is SourceBounds.pathLength (200), counted from the package root.",
            refusal.Sentence);
        Assert.Equal(long201, refusal.Path);
    }

    [Fact]
    public void Intake_ReportsEveryExceededBound_InTableOrder()
    {
        var upload = new Dictionary<string, byte[]>();
        for (var i = 0; i < SourceBounds.Files; i++)
        {
            upload[$"nodes/f{i:D4}.md"] = SourceFixture.Utf8(new string('a', 4100));
        }

        upload["nodes/big.md"] = SourceFixture.Utf8(new string('a', SourceBounds.FileBytes + 1));
        upload["nodes/" + new string('p', 200)] = SourceFixture.Utf8("x");

        var result = SourceIntake.Read(upload);

        Assert.Equal(["S01-037", "S01-038", "S01-039", "S01-040"], result.Refusals.Select(r => r.Id));
    }

    [Fact]
    public void Intake_ListsFileRefusalsBeforeTheBounds_InPathOrder()
    {
        var upload = new Dictionary<string, byte[]>
        {
            ["nodes/z.md"] = SourceFixture.Utf8("a\rb"),
            ["nodes/a.md"] = [0xEF, 0xBB, 0xBF],
            ["nodes/m.md"] = [0xFF],
            ["nodes/" + new string('p', 200)] = SourceFixture.Utf8("x"),
        };

        var ids = SourceIntake.Read(upload).Refusals.Select(r => r.Id);

        Assert.Equal(["S01-014", "A02-002", "S01-015", "S01-040"], ids);
    }

    [Fact]
    public void Intake_NeverThrows_OnAnyInput()
    {
        Assert.Empty(SourceIntake.Read(new Dictionary<string, byte[]>()).Refusals);

        var odd = new Dictionary<string, byte[]>
        {
            [""] = [],
            ["\\back\\slash.md"] = SourceFixture.Utf8("x"),
            ["a\0b"] = [0x00],
            ["\ud800"] = [0xFF, 0xFF],
            [" "] = null!,
            ["/leading"] = SourceFixture.Utf8("\r"),
            ["日本語/ファイル.md"] = [0xEF, 0xBB],
        };

        var result = SourceIntake.Read(odd);

        Assert.NotNull(result);
        Assert.Equal("x", result.Files["\\back\\slash.md"]);
        Assert.Contains(result.Refusals, r => r.Id == "A02-002" && r.Path == "日本語/ファイル.md");
    }

    [Fact]
    public void Intake_UsesAPathAsGiven()
    {
        var result = SourceIntake.Read(One("nodes\\plan.md", "x"));

        Assert.Equal(["nodes\\plan.md"], result.Files.Keys);
    }

    [Fact]
    public void Intake_AcceptsTheFixtures()
    {
        foreach (var fixture in new[] { SourceFixture.Minimal(), SourceFixture.Full() })
        {
            var result = SourceIntake.Read(fixture);
            Assert.Empty(result.Refusals);
            Assert.Empty(result.Normalised);
            Assert.Equal(fixture.Keys.Order(StringComparer.Ordinal), result.Files.Keys);
        }
    }

    [Fact]
    public void Fixtures_AreFictionalAndComplete()
    {
        string[] folders = ["nodes", "checks", "functions", "renderers", "snippets", "macros", "formats", "deliverables", "tests"];
        foreach (var fixture in new[] { SourceFixture.Minimal(), SourceFixture.Full() })
        {
            Assert.Contains("manifest.json", fixture.Keys);
            foreach (var folder in folders)
            {
                Assert.Contains($"{folder}/index.json", fixture.Keys);
            }
        }

        Assert.DoesNotContain(SourceFixture.Full().Keys, k => k.StartsWith('.') || k.Contains("/."));
    }

    [Fact]
    public void Fixtures_HoldStrictJsonAndNoCarriageReturns()
    {
        foreach (var fixture in new[] { SourceFixture.Minimal(), SourceFixture.Full() })
        {
            foreach (var (path, bytes) in fixture)
            {
                var text = Encoding.UTF8.GetString(bytes);
                Assert.DoesNotContain('\r', text);
                if (path.EndsWith(".json", StringComparison.Ordinal))
                {
                    Assert.Null(StrictJson.Parse(text).Fault);
                }
                else if (path.EndsWith(".md", StringComparison.Ordinal) && path != "README.md")
                {
                    var split = FrontMatter.Split(text);
                    Assert.True(split != null, path);
                    Assert.Null(StrictJson.Parse(split!.Header).Fault);
                }
            }
        }
    }

    [Fact]
    public void Fixtures_ListEveryMemberOfFull()
    {
        var full = SourceFixture.Full();

        // The structures the brief names are all there.
        Assert.Contains("README.md", full.Keys);
        Assert.Contains("nodes/sections/paragraphs/index.json", full.Keys);
        Assert.Contains("functions/draft_section/formats/index.json", full.Keys);
        Assert.Contains("tests/note_basic.json", full.Keys);
        var kinds = full.Where(p => p.Key.EndsWith("index.json", StringComparison.Ordinal) || p.Key.EndsWith(".md", StringComparison.Ordinal))
            .Select(p => Encoding.UTF8.GetString(p.Value))
            .SelectMany(t => new[] { "for", "while", "if", "case", "call", "agent", "rendered", "computed" }
                .Where(k => t.Contains($"\"kind\": \"{k}\"")))
            .ToHashSet();
        Assert.Equal(8, kinds.Count);
        Assert.Contains("\"nodes\": []", Encoding.UTF8.GetString(full["nodes/sections_draft/route/index.json"]));
    }

    [Fact]
    public void Helpers_EditWithoutDisturbingTheRest()
    {
        var files = SourceFixture.Minimal();
        var plan = SourceFixture.Text(files, "nodes/plan.md");
        var body = FrontMatter.Split(plan)!.Body;

        files.WithJson("nodes/plan.md", header => header["label"] = "Planning");
        var edited = FrontMatter.Split(SourceFixture.Text(files, "nodes/plan.md"))!;
        Assert.Equal(body, edited.Body);
        Assert.Equal("Planning", (string?)JsonNode.Parse(edited.Header)!["label"]);

        files.WithJson("nodes/index.json", index => index["description"] = "x");
        Assert.Equal("x", (string?)JsonNode.Parse(SourceFixture.Text(files, "nodes/index.json"))!["description"]);

        files.With("extra.md", "hello");
        Assert.Equal("hello", SourceFixture.Text(files, "extra.md"));
        files.Without("extra.md");
        Assert.DoesNotContain("extra.md", files.Keys);
    }

    [Fact]
    public void EveryIntakeSentence_IsTheDesigns()
    {
        var sentences = new[]
        {
            SourceSentences.StartsWithByteOrderMark(Path),
            SourceSentences.LoneCarriageReturn(Path, 2),
            SourceSentences.NotUtf8(Path),
            SourceSentences.TooManyFiles(513),
            SourceSentences.FileTooLarge(Path, 262145),
            SourceSentences.PackageTooLarge(9 * 262144),
            SourceSentences.PathTooLong(new string('a', 201)),
        };

        Assert.Equal(
            ["S01-014", "S01-015", "A02-002", "S01-037", "S01-038", "S01-039", "S01-040"],
            sentences.Select(s => s.Id));
        foreach (var sentence in sentences)
        {
            V31Contract.AssertSentence(sentence);
        }
    }
}
