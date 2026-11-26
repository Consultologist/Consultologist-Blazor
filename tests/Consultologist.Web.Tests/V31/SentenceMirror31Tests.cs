using System.Text.RegularExpressions;
using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 14 (#1509): every sentence History says that the registry holds equals its row of
/// <c>refusal-sentences.md</c>, word for word (a placeholder in the registry is the same placeholder here). A missing file
/// or a missing id fails; nothing is skipped.
/// </summary>
public class SentenceMirror31Tests
{
    private static readonly Regex Row = new(@"^\|\s*(?<id>[A-Z]\d{2}-\d{3})\s*\|.*?\|\s*\*""(?<text>.*?)""\*", RegexOptions.Compiled);

    private static readonly Lazy<Dictionary<string, string>> Registry = new(Load);

    private static Dictionary<string, string> Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "docs", "customizable-workflow", "package-format-v31", "refusal-sentences.md");
        Assert.True(File.Exists(path), $"{path} is missing.");

        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (Row.Match(line) is { Success: true } match)
            {
                rows[match.Groups["id"].Value] = match.Groups["text"].Value.Replace("\\<", "<").Replace("\\>", ">").Replace("\\|", "|");
            }
        }

        return rows;
    }

    private static string Sentence(string id)
    {
        Assert.True(Registry.Value.TryGetValue(id, out var text), $"{id} is not in refusal-sentences.md.");
        return text!;
    }

    [Fact]
    public void TheConstants_AreTheirRows()
    {
        Assert.Equal(HistorySentences31.Delivering, Sentence("S09-019"));
        Assert.Equal(HistorySentences31.Stopped, Sentence("A12-001"));
        Assert.Equal(HistorySentences31.TextDeleted, Sentence("A12-002"));
        Assert.Equal(HistorySentences31.RunFactsDiffer, Sentence("A12-003"));
    }

    [Fact]
    public void Blocked_IsS06033_AndS06037()
    {
        Assert.Equal(
            Sentence("S06-033"),
            HistorySentences31.Blocked("<Deliverable label>", "blocked: check '<id>' failed (<detail>)"));
        Assert.Equal(
            Sentence("S06-037"),
            HistorySentences31.Blocked("<Deliverable label>", "blocked: check '<id>' could not run (<error>)"));
    }

    [Fact]
    public void ThePlaceholderMethods_AreTheirRows()
    {
        Assert.Equal(Sentence("S09-017"), HistorySentences31.CouldNotSend("<destination>"));
        Assert.Equal(Sentence("S09-004"), HistorySentences31.MigratedFrom("<old version>"));
    }

    [Fact]
    public void RunAgainLater_ContinuesEachStoredHalf_AsS09010ToS09012()
    {
        Assert.Equal(Sentence("S09-010"), HistorySentences31.RunAgainLater("The model service stayed busy for <n> minutes."));
        Assert.Equal(
            Sentence("S09-011"),
            HistorySentences31.RunAgainLater("The model service <timed out | returned an error | couldn't be reached> <n> times in a row."));
        Assert.Equal(Sentence("S09-012"), HistorySentences31.RunAgainLater("The job didn't finish within <n> minutes."));
    }

    [Fact]
    public void TheAreaSentences_A12001ToA12005_AreInTheRegistry()
    {
        foreach (var id in new[] { "A12-001", "A12-002", "A12-003", "A12-004", "A12-005", "A12-006" })
        {
            Assert.False(string.IsNullOrWhiteSpace(Sentence(id)));
        }
    }
}
