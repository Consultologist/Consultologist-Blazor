using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Email;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1543, ruling 7: the 5-30 PDFs' text is what <c>origin/main</c>'s walker drew. The golden was written by main's
/// walker (before the 31 refactor) from a detached worktree; this branch's walker must draw each input the same.
/// Uses only what main and this branch share: <c>ConsultDocumentPdf.Render(markdown, password)</c> and PdfPig.
/// </summary>
public class Pdf30GoldenTests
{
    private const string Password = "correct-horse-battery-16";
    private const string UpdateVariable = "GOLDEN_UPDATE_PDF30";

    private static string TestsDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private static string GoldenPath() => Path.Combine(TestsDirectory(), "Golden", "Pdf30", "page-texts.json");

    private static IReadOnlyList<string> PageTexts(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(
            new MemoryStream(pdf),
            new UglyToad.PdfPig.ParsingOptions { Password = Password });

        return document.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))).ToList();
    }

    /// <summary>Every input: each composed deliverable of every capture, and the every-block note.</summary>
    private static SortedDictionary<string, string> Inputs()
    {
        var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var captures = Path.Combine(TestsDirectory(), "Fixtures", "ByteEquivalence", "captures");
        foreach (var file in Directory.EnumerateFiles(captures, "*.json", SearchOption.AllDirectories))
        {
            var capture = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            foreach (var row in capture["deliverables"]!.AsArray())
            {
                if (row!["state"]!.GetValue<string>() != "composed")
                {
                    continue;
                }

                inputs[$"{capture["package"]!.GetValue<string>()}/{capture["scenario"]!.GetValue<string>()}#{row["id"]!.GetValue<string>()}"] =
                    row["text"]!.GetValue<string>();
            }
        }

        inputs["every-block"] = File.ReadAllText(Path.Combine(TestsDirectory(), "Fixtures", "Markdown31", "every-block.md"));
        return inputs;
    }

    [Fact]
    public void TheWalker_DrawsEachFixtureAsMainDid()
    {
        var drawn = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, markdown) in Inputs())
        {
            drawn[key] = PageTexts(ConsultDocumentPdf.Render(markdown, Password));
        }

        var commit = Environment.GetEnvironmentVariable(UpdateVariable);
        if (!string.IsNullOrEmpty(commit))
        {
            var texts = new JsonObject();
            foreach (var (key, pages) in drawn)
            {
                texts[key] = new JsonArray(pages.Select(page => (JsonNode?)JsonValue.Create(page)).ToArray());
            }

            var golden = new JsonObject { ["generatedFrom"] = commit, ["texts"] = texts };
            File.WriteAllText(
                GoldenPath(),
                golden.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
            return;
        }

        var expected = JsonNode.Parse(File.ReadAllText(GoldenPath()))!["texts"]!.AsObject();
        Assert.Equal(expected.Select(pair => pair.Key).Order(StringComparer.Ordinal), drawn.Keys);
        foreach (var (key, pages) in drawn)
        {
            var golden = expected[key]!.AsArray().Select(page => page!.GetValue<string>()).ToList();
            Assert.True(
                golden.SequenceEqual(pages),
                $"{key}: the walker drew different text than main's.\nmain:\n{string.Join("\n--page--\n", golden)}\nhere:\n{string.Join("\n--page--\n", pages)}");
        }
    }
}
