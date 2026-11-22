using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;

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
/// #1504 (specVersion 31, area 07): and every sentence of <see cref="DialectSentences"/>, except
/// <c>TemplatesNotShipped</c>.
/// #1505 (specVersion 31, area 08): and of <see cref="CheckSentences"/> and <see cref="DeliverableSentences"/>, except
/// <c>ChecksNotShipped</c>.
/// </summary>
public class RefusalPackagesTests
{
    /// <summary>D17: the bounds whose packages the suite does not ship (513 files, a 257 KiB file, a package over 2 MiB).</summary>
    private static readonly string[] NotShipped = ["S01-037", "S01-038", "S01-039"];

    /// <summary>
    /// #1500 (area 03): the type sentences no package reaches through <see cref="SourceCheck.Run"/>, so
    /// the suite has no case for them.
    /// S02-020 (D6): <see cref="FormatReads"/> says it when asked, of a title compared in a condition; no caller asks
    /// yet. (#1502, area 05: S02-018 is said of a loop's <c>in</c> that reads an input bare, and has its case; #1504, area
    /// 07: S02-019 is said of a template's read of a plain enum's title, and has its case.)
    /// S02-027, S02-035, S02-036 and A03-009: only a registry or catalogue format reaches them. A
    /// package format whose root is an <c>anyOf</c> is A03-001 and an agent's format is named, so no
    /// package's agent returns a union root (S02-027); the two annotations are judged only at a registry
    /// format's root, and in a package's format or an inline one are S02-037 and S02-038.
    /// S02-009: the same words as S01-028, which has its case and is what the reader says.
    /// S02-011: the three flags follow from what a node reads, areas 04 and 05's.
    /// </summary>
    private static readonly string[] TypesNotShipped =
        ["S02-009", "S02-011", "S02-020", "S02-027", "S02-035", "S02-036", "A03-009"];

    /// <summary>The two ids of <see cref="TypesNotShipped"/> that no method of <see cref="FormatSentences"/> gives (S01-028's words; areas 04 and 05's flags).</summary>
    private static readonly string[] TypesWithNoMethod = ["S02-009", "S02-011"];

    /// <summary>
    /// #1501 (area 04): the expression sentences the suite has no case for.
    /// S03-001: the reason sentence of a decision (<c>skipped: … is false</c>), which the record holds; not a refusal.
    /// (#1502, area 05: S03-036 and S03-044 are said of the resolver's graph, and have their cases.)
    /// S03-037 and S03-038: warnings, and S03-039 a note; a package that gets them is accepted.
    /// S03-040, S03-041 and A04-011: failures at run time, said when a rule is evaluated, never at publish.
    /// </summary>
    private static readonly string[] LogicNotShipped =
        ["S03-001", "S03-037", "S03-038", "S03-039", "S03-040", "S03-041", "A04-011"];

    /// <summary>
    /// #1502 (area 05): the resolver's sentences the suite has no case for. S04-074: a warning (a format a function lists
    /// in uses and never names); a package that gets it is accepted. (#1504, area 07: S04-063, a macro placed in a
    /// function whose if reads a node the function lacks, is judged through the dialect's reads, and has its case.) S04-072,
    /// S04-073, S04-079 and S04-080: part 04 § 10.2's notes, measured against the engine's limits (<c>PublishNotes</c>,
    /// D11); a package that gets them is accepted.
    /// Part 07 § 10's sentences, none a publish refusal of a package source (D10): S07-003, S07-004 and S07-005 are said at
    /// job start, of <c>requires</c> and the lock against an engine (<c>RequiresCheck.Engine</c>), and S06-031 and S06-032
    /// at job start, of <c>needs.profile</c> against the account's profile (<c>RequiresCheck.Profile</c>); S07-006 and
    /// S07-017 at load, of the stored pair against its stamp (<c>CompiledPackage.Verify</c>); S07-016 at import, of the
    /// compiled copies intake drops; S07-015 by CI, of a compiled copy committed to the examples repository.
    /// </summary>
    private static readonly string[] ResolveNotShipped =
    [
        "S04-072", "S04-073", "S04-074", "S04-079", "S04-080",
        "S06-031", "S06-032", "S07-003", "S07-004", "S07-005", "S07-006", "S07-015", "S07-016", "S07-017",
    ];

    /// <summary>
    /// #1504 (area 07): the dialect's sentences the suite has no case for. S05-041 (a node that is no agent declares
    /// parts): the reader refuses a parts member on any other kind first, so no package reaches it. S05-060 (W-1, a run
    /// spot) and S05-061 (W-2, a markdown_block renderer inside a line): warnings; a package that gets them is accepted.
    /// And part 05's run-time sentences, said while a body renders or a reply is read, never at publish: S05-012, S05-014
    /// and S05-062 (D12's limits), S05-053 to S05-059 (a reply's markers, part 05 § 8.6) and A07-006 (D13). A07-014 (two
    /// versions of one agent, #1504 ruling 4): the published agents catalogue binds each agent at one version, and a package
    /// cannot choose its agents' versions, so no package reaches it against the published registry; only a deployment's
    /// catalogue binding two formats to two versions of one agent does (<c>ResolveCompilerTests.TwoVersionsOfOneAgent_AreRefusedAtPublish</c>).
    /// </summary>
    private static readonly string[] TemplatesNotShipped =
    [
        "S05-041", "S05-060", "S05-061", "A07-014",
        "S05-012", "S05-014", "S05-053", "S05-054", "S05-055", "S05-056", "S05-057", "S05-058", "S05-059", "S05-062", "A07-006",
    ];

    /// <summary>
    /// #1505 (area 08): the sentences of this area the suite has no case for. A08-006 (#1505 ruling 6): the engine failed
    /// reading a template, a fault of the engine and not of the package, so no package content produces it.
    /// </summary>
    private static readonly string[] ChecksNotShipped = ["A08-006"];

    /// <summary>The ids of <see cref="TemplatesNotShipped"/> said at run time, by no method of <see cref="DialectSentences"/>.</summary>
    private static readonly string[] TemplatesRunTime =
        ["S05-012", "S05-014", "S05-053", "S05-054", "S05-055", "S05-056", "S05-057", "S05-058", "S05-059", "S05-062", "A07-006"];

    /// <summary>
    /// The ids of <see cref="LogicNotShipped"/> that no public method of <see cref="ExpressionSentences"/> returning a
    /// refusal gives: S03-001 (the record's), S03-039 (a note, text) and the run-time sentences
    /// (<see cref="ExpressionSentences.RunTime"/>, text).
    /// </summary>
    private static readonly string[] LogicWithNoMethod = ["S03-001", "S03-039", "S03-040", "S03-041", "A04-011"];

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
        // Each method of SourceSentences, FormatSentences, ExpressionSentences, ResolveSentences, DialectSentences, CheckSentences and DeliverableSentences that returns a refusal makes one sentence
        // with a fixed id; calling it with any arguments gives that id (FormatSentences.Subject, which
        // returns text, is not one; nor is ExpressionSentences.Shape, which gives the sentence of a fault of the
        // written form by calling the others).
        var said = SaidBy(typeof(SourceSentences), typeof(FormatSentences), typeof(ExpressionSentences), typeof(ResolveSentences), typeof(DialectSentences), typeof(CheckSentences), typeof(DeliverableSentences))
            .Except(NotShipped)
            .Except(TypesNotShipped)
            .Except(LogicNotShipped)
            .Except(ResolveNotShipped)
            .Except(TemplatesNotShipped)
            .Except(ChecksNotShipped)
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
    public void EveryTemplateNotShipped_IsASentenceTheDialectHas_OrARunTimeOne()
    {
        var said = SaidBy(typeof(DialectSentences));
        Assert.All(TemplatesNotShipped, id => Assert.True(said.Contains(id) || TemplatesRunTime.Contains(id), id));
        Assert.All(TemplatesRunTime, id => Assert.DoesNotContain(id, said));
    }

    [Fact]
    public void EveryCheckNotShipped_IsASentenceTheCheckerHas()
    {
        var said = SaidBy(typeof(DialectSentences), typeof(CheckSentences), typeof(DeliverableSentences));
        Assert.All(ChecksNotShipped, id => Assert.Contains(id, said));
    }

    [Fact]
    public void TheWorkedExamples_AreTheirSentencesCases()
    {
        // #1504: S05-008 and S05-027 are part 05's worked examples of S05-009 and S05-026; their cases say them word for word.
        var registry = V31Contract.PublishedTypes();
        var rows = V31Contract.Json("sentences.json")["sentences"]!.AsArray().ToDictionary(r => (string)r!["id"]!, r => (string)r!["sentence"]!);
        foreach (var (example, id) in new[] { ("S05-008", "S05-009"), ("S05-027", "S05-026") })
        {
            var refusalCase = RefusalPackages.All().Single(c => c.Id == id);
            Assert.Equal(rows[example], Assert.Single(SourceCheck.Run(refusalCase.Invalid, registry).Refusals).Sentence);
        }
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
            .Where(m => m.ReturnType == typeof(SourceRefusal)
                && m.GetParameters().All(p => p.ParameterType != typeof(ShapeFault) && p.ParameterType != typeof(DialectFault)))
            .Select(m => ((SourceRefusal)m.Invoke(null, m.GetParameters().Select(Argument).ToArray())!).Id)
            .Distinct()
            .ToList();

    private static object Argument(ParameterInfo parameter) => parameter.ParameterType switch
    {
        var t when t == typeof(int) => 1,
        var t when t == typeof(long) => 1L,
        var t when t == typeof(bool) => true,
        var t when t == typeof(ExpressionSite) => new ExpressionSite(ExpressionSiteKind.NodeIf, "x", "if", "x", null, null),
        var t when t == typeof(LineageValue) => new LineageValue(true, "x"),
        var t when t == typeof(IReadOnlyList<string>) => new[] { "resolve.json" },
        _ => "x",
    };
}
