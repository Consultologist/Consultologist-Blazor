using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the <c>refusal-packages</c> cases of the file model. Each case
/// says what it claims (its valid package is accepted, its invalid one gives its sentence alone); every
/// sentence the checker can say has a case, except D17's three and <c>TypesNotShipped</c>; and the suite pinned in
/// <c>external/consultologist-package-format</c> is what the checker says and holds every case,
/// byte for byte. The pinned tests fail, never skip, when the folder is missing.
/// #1500 (specVersion 31, area 03): every case is judged by <see cref="SourceCheck.Run"/>, the reader
/// and then the type rules, and every sentence of <see cref="FormatSentences"/> a package can reach
/// has a case too.
/// #1501 (specVersion 31, area 04): every case is judged on its expressions too, the third step, and every sentence of
/// <see cref="ExpressionSentences"/> a package can reach has a case, except <c>LogicNotShipped</c>.
/// #1502 (specVersion 31, area 05): and every sentence of <see cref="ResolveSentences"/>.
/// </summary>
public class RefusalPackagesTests
{
    /// <summary>D17: the bounds whose packages the suite does not ship (513 files, a 257 KiB file, a package over 2 MiB).</summary>
    private static readonly string[] NotShipped = ["S01-037", "S01-038", "S01-039"];

    /// <summary>
    /// #1500 (area 03): the type sentences no package reaches through <see cref="SourceCheck.Run"/>, so
    /// the suite has no case for them.
    /// S02-018, S02-019 and S02-020 (D6): <see cref="FormatReads"/> says them when asked, but finding
    /// the reads (JSON Logic <c>var</c>, templates, reference strings) is areas 04, 05 and 07's; no
    /// caller asks yet.
    /// S02-027, S02-035, S02-036 and A03-009: only a registry or catalogue format reaches them. A
    /// package format whose root is an <c>anyOf</c> is A03-001 and an agent's format is named, so no
    /// package's agent returns a union root (S02-027); the two annotations are judged only at a registry
    /// format's root, and in a package's format or an inline one are S02-037 and S02-038.
    /// S02-009: the same words as S01-028, which has its case and is what the reader says.
    /// S02-011: the three flags follow from what a node reads, areas 04 and 05's.
    /// </summary>
    private static readonly string[] TypesNotShipped =
        ["S02-009", "S02-011", "S02-018", "S02-019", "S02-020", "S02-027", "S02-035", "S02-036", "A03-009"];

    /// <summary>The two ids of <see cref="TypesNotShipped"/> that no method of <see cref="FormatSentences"/> gives (S01-028's words; areas 04 and 05's flags).</summary>
    private static readonly string[] TypesWithNoMethod = ["S02-009", "S02-011"];

    /// <summary>
    /// #1501 (area 04): the expression sentences the suite has no case for.
    /// S03-001: the reason sentence of a decision (<c>skipped: … is false</c>), which the record holds; not a refusal.
    /// S03-036 and S03-044 (D28): the three flags are worked out from the compiled package's edges and from what
    /// templates read, areas 05 and 07's; no caller gives <see cref="LineageFlags"/> a graph yet.
    /// S03-037 and S03-038: warnings, and S03-039 a note; a package that gets them is accepted.
    /// S03-040, S03-041 and A04-011: failures at run time, said when a rule is evaluated, never at publish.
    /// </summary>
    private static readonly string[] LogicNotShipped =
        ["S03-001", "S03-036", "S03-037", "S03-038", "S03-039", "S03-040", "S03-041", "S03-044", "A04-011"];

    /// <summary>
    /// The ids of <see cref="LogicNotShipped"/> that no public method of <see cref="ExpressionSentences"/> returning a
    /// refusal gives: S03-001 (the record's), S03-036 (the flags', internal), S03-039 (a note, text) and the run-time
    /// sentences (<see cref="ExpressionSentences.RunTime"/>, text).
    /// </summary>
    private static readonly string[] LogicWithNoMethod = ["S03-001", "S03-036", "S03-039", "S03-040", "S03-041", "A04-011"];

    [Fact]
    public void EveryCase_SaysWhatItClaims()
    {
        var registry = V31Contract.PublishedTypes();
        foreach (var refusalCase in RefusalPackages.All())
        {
            var valid = SourceCheck.Run(refusalCase.Valid, registry);
            Assert.True(valid.Refusals.Count == 0,
                $"{refusalCase.Id} valid: {string.Join(" | ", valid.Refusals.Select(r => $"{r.Id} {r.Sentence}"))}");

            var invalid = SourceCheck.Run(refusalCase.Invalid, registry);
            Assert.True(invalid.Refusals.Count == 1 && invalid.Refusals[0].Id == refusalCase.Id,
                $"{refusalCase.Id} invalid: {string.Join(" | ", invalid.Refusals.Select(r => $"{r.Id} {r.Sentence}"))}");
            V31Contract.AssertSentence(invalid.Refusals[0]);
            Assert.False(string.IsNullOrWhiteSpace(refusalCase.About), $"{refusalCase.Id} has no about.");
        }
    }

    [Fact]
    public void EverySentence_TheCheckerCanSay_HasACase()
    {
        // Each method of SourceSentences, FormatSentences, ExpressionSentences and ResolveSentences that returns a refusal makes one sentence
        // with a fixed id; calling it with any arguments gives that id (FormatSentences.Subject, which
        // returns text, is not one; nor is ExpressionSentences.Shape, which gives the sentence of a fault of the
        // written form by calling the others).
        var said = SaidBy(typeof(SourceSentences), typeof(FormatSentences), typeof(ExpressionSentences), typeof(ResolveSentences))
            .Except(NotShipped)
            .Except(TypesNotShipped)
            .Except(LogicNotShipped)
            .Order(StringComparer.Ordinal)
            .ToList();

        var cases = RefusalPackages.All().Select(c => c.Id).ToList();

        Assert.Equal(cases.Distinct().Count(), cases.Count);
        Assert.Equal(said, cases.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void EveryTypeNotShipped_IsASentenceTheCheckerHas_OrOneOfTheTwoWithNoMethod()
    {
        // A stale or mistyped id in TypesNotShipped would hide nothing and excuse nothing; hold the
        // list to the ids FormatSentences gives, plus the two documented ones with no method.
        var said = SaidBy(typeof(FormatSentences));
        var known = said.Concat(TypesWithNoMethod).ToHashSet(StringComparer.Ordinal);

        Assert.All(TypesNotShipped, id => Assert.Contains(id, known));
        Assert.All(TypesWithNoMethod, id => Assert.DoesNotContain(id, said));
    }

    [Fact]
    public void EveryLogicNotShipped_IsASentenceTheCheckerHas_OrOneOfThoseWithNoMethod()
    {
        var said = SaidBy(typeof(ExpressionSentences));
        var known = said.Concat(LogicWithNoMethod).ToHashSet(StringComparer.Ordinal);

        Assert.All(LogicNotShipped, id => Assert.Contains(id, known));
        Assert.All(LogicWithNoMethod, id => Assert.DoesNotContain(id, said));
    }

    [Fact]
    public void ThePinnedSuite_IsWhatTheCheckerSays()
    {
        var registry = V31Contract.PublishedTypes();
        var folders = PinnedCaseFolders();
        Assert.NotEmpty(folders);

        foreach (var folder in folders)
        {
            var name = Path.GetRelativePath(V31Contract.RefusalPackagesFolder(), folder).Replace('\\', '/');
            var expect = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "expect.json")))!;
            var result = SourceCheck.Run(ReadPackage(Path.Combine(folder, "package")), registry);

            Assert.True((bool)expect["valid"]! == (result.Refusals.Count == 0), $"{name}: valid disagrees with the checker.");
            Assert.Equal(
                expect["sentences"]!.AsArray().Select(s => (string)s!).ToList(),
                result.Refusals.Select(r => r.Sentence).ToList());
        }
    }

    [Fact]
    public void ThePinnedSuite_HoldsEveryCase()
    {
        var root = V31Contract.RefusalPackagesFolder();
        var registry = V31Contract.PublishedTypes();
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

                var expected = RefusalPackages.Expect(refusalCase.About, SourceCheck.Run(files, registry).Refusals);
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

    /// <summary>The ids the public static methods of <paramref name="types"/> that return a refusal give, each once.</summary>
    private static List<string> SaidBy(params Type[] types) =>
        types
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.ReturnType == typeof(SourceRefusal) && m.GetParameters().All(p => p.ParameterType != typeof(ShapeFault)))
            .Select(m => ((SourceRefusal)m.Invoke(null, m.GetParameters().Select(Argument).ToArray())!).Id)
            .Distinct()
            .ToList();

    private static object Argument(ParameterInfo parameter) => parameter.ParameterType switch
    {
        var t when t == typeof(int) => 1,
        var t when t == typeof(long) => 1L,
        var t when t == typeof(bool) => true,
        var t when t == typeof(ExpressionSite) => new ExpressionSite(ExpressionSiteKind.NodeIf, "x", "if", "x", null, null),
        _ => "x",
    };
}
