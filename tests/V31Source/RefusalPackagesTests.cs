using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the <c>refusal-packages</c> cases of the file model. Each case
/// says what it claims (its valid package parses, its invalid one gives its sentence alone); every
/// sentence the parser can say has a case, except D17's three; and the suite pinned in
/// <c>external/consultologist-package-format</c> is what the parser says and holds every case,
/// byte for byte. The pinned tests fail, never skip, when the folder is missing.
/// </summary>
public class RefusalPackagesTests
{
    /// <summary>D17: the bounds whose packages the suite does not ship (513 files, a 257 KiB file, a package over 2 MiB).</summary>
    private static readonly string[] NotShipped = ["S01-037", "S01-038", "S01-039"];

    [Fact]
    public void EveryCase_SaysWhatItClaims()
    {
        var registry = V31Contract.PublishedRegistry();
        foreach (var refusalCase in RefusalPackages.All())
        {
            var valid = SourceParser.Parse(refusalCase.Valid, registry);
            Assert.True(valid.Refusals.Count == 0,
                $"{refusalCase.Id} valid: {string.Join(" | ", valid.Refusals.Select(r => $"{r.Id} {r.Sentence}"))}");

            var invalid = SourceParser.Parse(refusalCase.Invalid, registry);
            Assert.True(invalid.Refusals.Count == 1 && invalid.Refusals[0].Id == refusalCase.Id,
                $"{refusalCase.Id} invalid: {string.Join(" | ", invalid.Refusals.Select(r => $"{r.Id} {r.Sentence}"))}");
            V31Contract.AssertSentence(invalid.Refusals[0]);
            Assert.False(string.IsNullOrWhiteSpace(refusalCase.About), $"{refusalCase.Id} has no about.");
        }
    }

    [Fact]
    public void EverySentence_TheParserCanSay_HasACase()
    {
        // Each method of SourceSentences makes one sentence with a fixed id; calling it with any
        // arguments gives that id.
        var said = typeof(SourceSentences)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(SourceRefusal))
            .Select(m => ((SourceRefusal)m.Invoke(null, m.GetParameters().Select(Argument).ToArray())!).Id)
            .Except(NotShipped)
            .Order(StringComparer.Ordinal)
            .ToList();

        var cases = RefusalPackages.All().Select(c => c.Id).ToList();

        Assert.Equal(cases.Distinct().Count(), cases.Count);
        Assert.Equal(said, cases.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void ThePinnedSuite_IsWhatTheParserSays()
    {
        var registry = V31Contract.PublishedRegistry();
        var folders = PinnedCaseFolders();
        Assert.NotEmpty(folders);

        foreach (var folder in folders)
        {
            var name = Path.GetRelativePath(V31Contract.RefusalPackagesFolder(), folder).Replace('\\', '/');
            var expect = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "expect.json")))!;
            var result = SourceParser.Parse(ReadPackage(Path.Combine(folder, "package")), registry);

            Assert.True((bool)expect["valid"]! == (result.Refusals.Count == 0), $"{name}: valid disagrees with the parser.");
            Assert.Equal(
                expect["sentences"]!.AsArray().Select(s => (string)s!).ToList(),
                result.Refusals.Select(r => r.Sentence).ToList());
        }
    }

    [Fact]
    public void ThePinnedSuite_HoldsEveryCase()
    {
        var root = V31Contract.RefusalPackagesFolder();
        var registry = V31Contract.PublishedRegistry();
        var cases = RefusalPackages.All();

        var onDisk = Directory.GetDirectories(root).Select(d => Path.GetFileName(d)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(cases.Select(c => c.Id).Order(StringComparer.Ordinal).ToList(), onDisk);

        foreach (var refusalCase in cases)
        {
            Assert.Equal(["invalid", "valid"],
                Directory.GetDirectories(Path.Combine(root, refusalCase.Id)).Select(d => Path.GetFileName(d)).Order(StringComparer.Ordinal));
            foreach (var (side, files) in new[] { ("valid", refusalCase.Valid), ("invalid", refusalCase.Invalid) })
            {
                var folder = Path.Combine(root, refusalCase.Id, side);
                var package = ReadPackage(Path.Combine(folder, "package"));
                Assert.True(
                    package.Keys.Order(StringComparer.Ordinal).SequenceEqual(files.Keys.Order(StringComparer.Ordinal))
                    && files.All(f => package[f.Key].AsSpan().SequenceEqual(f.Value)),
                    $"{refusalCase.Id}/{side}: the pinned package is not the case's files, byte for byte.");

                var expected = RefusalPackages.Expect(refusalCase.About, SourceParser.Parse(files, registry));
                Assert.Equal(expected, File.ReadAllText(Path.Combine(folder, "expect.json"), Encoding.UTF8));
            }
        }
    }

    /// <summary>Every <c>&lt;id&gt;/&lt;valid | invalid&gt;</c> folder of the pinned suite.</summary>
    private static List<string> PinnedCaseFolders() =>
        Directory.GetDirectories(V31Contract.RefusalPackagesFolder())
            .SelectMany(Directory.GetDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>A package folder as an upload: each file's path from the folder, with <c>/</c>, to its bytes.</summary>
    private static Dictionary<string, byte[]> ReadPackage(string folder)
    {
        Assert.True(Directory.Exists(folder), $"{folder} is missing.");
        return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(
                file => Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);
    }

    private static object Argument(ParameterInfo parameter) => parameter.ParameterType switch
    {
        var t when t == typeof(int) => 1,
        var t when t == typeof(long) => 1L,
        _ => "x",
    };
}
