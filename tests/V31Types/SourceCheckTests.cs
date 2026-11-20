using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="SourceCheck.Run"/>, the reader then the type rules,
/// and Review Focus 2: no format, however broken, makes it throw.
/// </summary>
public class SourceCheckTests
{
    private static readonly Lazy<TypeRegistry> Registry = new(TypeFixture.Published);

    [Fact]
    public void Run_ParsesThenTypes()
    {
        var full = SourceCheck.Run(SourceFixture.Full(), Registry.Value);
        Assert.Empty(full.Refusals);
        Assert.NotNull(full.Parse.Package);
        Assert.NotNull(full.Types);
        Assert.Equal(25, full.Types!.NodeFormats.Count);

        // A parse fault: only the reader's refusals, and no types.
        var unparsed = SourceCheck.Run(SourceFixture.Full().WithJson("nodes/plan.md", h => h["kind"] = "lets"), Registry.Value);
        Assert.Null(unparsed.Parse.Package);
        Assert.Equal(unparsed.Parse.Refusals, unparsed.Refusals);
        Assert.Equal("S02-007", Assert.Single(unparsed.Refusals).Id);
        Assert.Null(unparsed.Types);

        // A type fault: the package parsed, the type rules refused it.
        var untyped = SourceCheck.Run(SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["format"] = "check_result"), Registry.Value);
        Assert.NotNull(untyped.Parse.Package);
        Assert.Empty(untyped.Parse.Refusals);
        Assert.Equal("S02-012", Assert.Single(untyped.Refusals).Id);
        Assert.Null(untyped.Types);
    }

    [Fact]
    public void Run_TakesNoNull()
    {
        Assert.Throws<ArgumentNullException>(() => SourceCheck.Run(null!, Registry.Value));
        Assert.Throws<ArgumentNullException>(() => SourceCheck.Run(SourceFixture.Minimal(), null!));
    }

    /// <summary>What replaces a format: none of them is a format.</summary>
    private static readonly string[] Replacements = ["{}", "[]", "null", "5", "\"x\"", "true"];

    /// <summary>
    /// Review Focus 2: every format file and every inline <c>format</c> of <see cref="SourceFixture.Full"/>
    /// in turn replaced by each of <see cref="Replacements"/>, or with one of its members deleted.
    /// Run returns, and refuses each (none of them is a valid format: every member of these formats is required).
    /// </summary>
    [Fact]
    public void AnyOneFormatBroken_IsRefused_AndNeverThrows()
    {
        var full = SourceFixture.Full();
        var cases = new List<(string Label, Dictionary<string, byte[]> Files)>();

        foreach (var path in full.Keys.Where(p => p.StartsWith("formats/", StringComparison.Ordinal) && p != "formats/index.json"))
        {
            foreach (var replacement in Replacements)
                cases.Add(($"{path} = {replacement}", SourceFixture.Full().With(path, replacement + "\n")));
            foreach (var member in JsonNode.Parse(SourceFixture.Text(full, path))!.AsObject().Select(p => p.Key).ToList())
                cases.Add(($"{path} without {member}", SourceFixture.Full().WithJson(path, json => json.Remove(member))));
        }

        foreach (var path in full.Keys)
        {
            if (Header(full, path)?["format"] is not JsonObject inline)
                continue;
            foreach (var replacement in Replacements)
                cases.Add(($"{path} format = {replacement}", SourceFixture.Full().WithJson(path, h => h["format"] = JsonNode.Parse(replacement))));
            foreach (var member in inline.Select(p => p.Key).ToList())
                cases.Add(($"{path} format without {member}", SourceFixture.Full().WithJson(path, h => h["format"]!.AsObject().Remove(member))));
        }

        Assert.True(cases.Count > 60, $"only {cases.Count} cases");
        foreach (var (label, files) in cases)
        {
            var result = SourceCheck.Run(files, Registry.Value);
            Assert.True(result.Refusals.Count > 0, $"{label}: accepted");
            Assert.Null(result.Types);
            Assert.All(result.Refusals, V31Contract.AssertSentence);
        }
    }

    /// <summary>The JSON a file holds: the whole of a <c>.json</c> file, the header of a <c>.md</c> one.</summary>
    private static JsonObject? Header(Dictionary<string, byte[]> files, string path)
    {
        var text = SourceFixture.Text(files, path);
        if (path.EndsWith(".md", StringComparison.Ordinal))
            return FrontMatter.Split(text) is { } split ? JsonNode.Parse(split.Header) as JsonObject : null;
        return path.EndsWith(".json", StringComparison.Ordinal) ? JsonNode.Parse(text) as JsonObject : null;
    }
}
