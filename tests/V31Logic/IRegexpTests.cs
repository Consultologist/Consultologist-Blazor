using System.Text.RegularExpressions;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: I-Regexp (RFC 9485) as part 03 § 5.3 uses it: the grammar and its reasons (S03-031), the
/// bounds (S03-032, S03-042, S03-040), the compile (S03-043), and the translation to .NET counting
/// code points, not UTF-16 units.
/// </summary>
public class IRegexpTests
{
    [Theory]
    [InlineData("letrozole|femara")]
    [InlineData("a{2,3}")]
    [InlineData("[a-c]+")]
    [InlineData("[^0-9]")]
    [InlineData("\\p{Lu}\\P{Nd}")]
    [InlineData("a\\.b")]
    [InlineData("(ab)*c?")]
    [InlineData("\\[x\\]")]
    [InlineData("^")]
    [InlineData("$")]
    [InlineData("")]
    public void ValidPatterns_Compile(string pattern)
    {
        var check = IRegexp.Check(pattern);

        Assert.Equal(IRegexpFault.None, check.Fault);
        Assert.NotNull(check.Regex);
        Assert.NotNull(IRegexp.ToDotNet(pattern));
    }

    [Theory]
    [InlineData("letrozole|femara", "started Femara then letrozole", true)]
    [InlineData("femara", "Femara", false)]
    [InlineData("a.c", "a\nc", false)]
    [InlineData("a.c", "abc", true)]
    [InlineData("^a", "a", false)]
    [InlineData("^a", "x^a", true)]
    [InlineData("a$", "a$", true)]
    [InlineData("\\p{Lu}{2}", "xABy", true)]
    [InlineData("[^a]", "a", false)]
    public void Search_FindsAnywhere(string pattern, string text, bool expected)
    {
        Assert.Equal(expected, IRegexp.IsMatch(IRegexp.Check(pattern).Regex!, text));
    }

    [Theory]
    [InlineData("a.b", "a😀b", true)]
    [InlineData("a..b", "a😀b", false)]
    [InlineData("😀{2}", "😀😀", true)]
    [InlineData("😀{2}", "😀", false)]
    [InlineData("[😀🙂]", "🙂", true)]
    [InlineData("[^a]b", "😀b", true)]
    [InlineData("a[^😀]b", "a😀b", false)]
    [InlineData("a[^😀]b", "a🙂b", true)]
    [InlineData("[😀-🙂]", "😁", true)]
    [InlineData("a\\P{L}b", "a😀b", true)]
    [InlineData("\\p{C}", "😀", false)]
    [InlineData("[\\p{L}]", "😀", false)]
    [InlineData("[a\\P{L}]", "😀", true)]
    [InlineData("[^\\P{L}]", "😀", false)]
    [InlineData("[^\\p{L}]", "😀", true)]
    [InlineData("[a-😀]", "😀", true)]
    [InlineData("[a-😀]", "😁", false)]
    public void Translation_CountsCodePoints(string pattern, string text, bool expected)
    {
        Assert.Equal(expected, IRegexp.IsMatch(IRegexp.Check(pattern).Regex!, text));
    }

    [Theory]
    [InlineData("\\d", "'\\d' at character 1 is not an escape of I-Regexp")]
    [InlineData("a\\b", "'\\b' at character 2 is not an escape of I-Regexp")]
    [InlineData("(a", "the pattern ends inside a group")]
    [InlineData("[a", "the pattern ends inside a class")]
    [InlineData("a\\", "the pattern ends inside an escape")]
    [InlineData("a{2", "the pattern ends inside a repetition")]
    [InlineData("a)", "')' at character 2 is not allowed there")]
    [InlineData("*a", "'*' at character 1 is not allowed there")]
    [InlineData("\\p{Greek}", "'Greek' at character 1 is not a Unicode category of I-Regexp")]
    [InlineData("[b-a]", "the range at character 2 is backwards")]
    [InlineData("a{3,2}", "the repetition at character 2 has its numbers backwards")]
    [InlineData("a(?=b)", "'?' at character 3 is not allowed there")]
    [InlineData("[]", "']' at character 2 is not allowed there")]
    [InlineData("a**", "'*' at character 3 is not allowed there")]
    [InlineData("[a-\\p{L}]", "'-' at character 3 is not allowed there")]
    [InlineData("a{101,2}", "the repetition at character 2 has its numbers backwards")]
    public void NotIRegexp_SaysWhy(string pattern, string reason)
    {
        var check = IRegexp.Check(pattern);

        Assert.Equal(IRegexpFault.NotIRegexp, check.Fault);
        Assert.Equal(reason, check.Reason);
        Assert.Null(check.Regex);
        Assert.Null(IRegexp.ToDotNet(pattern));
    }

    [Fact]
    public void Bounds()
    {
        Assert.Equal(IRegexpFault.None, IRegexp.Check(new string('a', 200)).Fault);

        var tooLong = IRegexp.Check(new string('a', 201));
        Assert.Equal(IRegexpFault.TooLong, tooLong.Fault);
        Assert.Equal(201, tooLong.Count);
        Assert.Null(tooLong.Regex);

        Assert.Equal(IRegexpFault.None, IRegexp.Check(string.Concat(Enumerable.Repeat("😀", 200))).Fault);
        Assert.Equal(IRegexpFault.None, IRegexp.Check("a{100}").Fault);

        foreach (var pattern in new[] { "a{101}", "a{2,101}", "a{101,}" })
        {
            var repeats = IRegexp.Check(pattern);
            Assert.Equal(IRegexpFault.Repeats, repeats.Fault);
            Assert.Equal(101, repeats.Count);
            Assert.Null(repeats.Regex);
        }
    }

    /// <summary>
    /// .NET's non-backtracking engine refuses a pattern whose unfolded counted repetitions are too
    /// large, so a pattern within every other bound can still fail to compile (S03-043, so Task 10
    /// ships a case for it): <c>((a{100}){100}){100}</c> is refused, and so is
    /// <c>(a{100}){100}</c>, while <c>a{100}</c> compiles.
    /// </summary>
    [Fact]
    public void APatternTooLargeForLinearMatching_DoesNotCompile()
    {
        foreach (var pattern in new[] { "((a{100}){100}){100}", "(a{100}){100}" })
        {
            var check = IRegexp.Check(pattern);
            Assert.Equal(IRegexpFault.DoesNotCompile, check.Fault);
            Assert.Equal("it needs too large an automaton", check.Reason);
            Assert.Null(check.Regex);
            Assert.NotNull(IRegexp.ToDotNet(pattern));
        }
    }

    [Fact]
    public void Compiled_WithTheEnginesOptions()
    {
        Assert.Equal(
            RegexOptions.NonBacktracking | RegexOptions.CultureInvariant,
            IRegexp.Check("a").Regex!.Options);
        Assert.Equal(Regex.InfiniteMatchTimeout, IRegexp.Check("a").Regex!.MatchTimeout);
    }

    [Fact]
    public void Check_IsCachedByPattern()
    {
        Assert.Same(IRegexp.Check("letrozole"), IRegexp.Check("letrozole"));
    }

    /// <summary>A distinct valid pattern for each number: a literal word, then the number's digits.</summary>
    private static string Distinct(string prefix, int i) => $"{prefix}{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    [Fact]
    public void Cache_IsCapped()
    {
        for (var i = 0; i < 3_000; i++)
            Assert.Equal(IRegexpFault.None, IRegexp.Check(Distinct("capped", i)).Fault);
        Assert.InRange(IRegexp.CachedCount, 0, IRegexp.MaxCachedPatterns);
        Assert.Equal(1_024, IRegexp.MaxCachedPatterns);
    }

    [Fact]
    public void Cache_HoldsNoPatternTooLong_OrNotIRegexp()
    {
        var tooLong = new string('a', IRegexp.MaxCodePoints + 1);
        Assert.Equal(IRegexpFault.TooLong, IRegexp.Check(tooLong).Fault);
        Assert.False(IRegexp.IsCached(tooLong));

        Assert.Equal(IRegexpFault.NotIRegexp, IRegexp.Check("(uncached").Fault);
        Assert.False(IRegexp.IsCached("(uncached"));

        Assert.Equal(IRegexpFault.None, IRegexp.Check("cached").Fault);
        Assert.True(IRegexp.IsCached("cached"));
    }

    [Fact]
    public void Cache_AfterAClear_ResultsStayCorrect()
    {
        var before = IRegexp.Check("letrozole|femara");
        for (var i = 0; i <= IRegexp.MaxCachedPatterns; i++)
            IRegexp.Check(Distinct("cleared", i));

        var after = IRegexp.Check("letrozole|femara");
        Assert.Equal(IRegexpFault.None, after.Fault);
        Assert.True(IRegexp.IsMatch(after.Regex!, "started letrozole"));
        Assert.False(IRegexp.IsMatch(after.Regex!, "started tamoxifen"));
        Assert.Equal(before.Regex!.ToString(), after.Regex!.ToString());
        Assert.Equal(IRegexpFault.Repeats, IRegexp.Check("a{101}").Fault);
        Assert.Equal(IRegexpFault.NotIRegexp, IRegexp.Check("(a").Fault);
    }

    [Fact]
    public void TooLong_CountsUtf8Bytes()
    {
        Assert.False(IRegexp.TooLong(new string('a', 1_048_576)));
        Assert.True(IRegexp.TooLong(new string('a', 1_048_577)));
        Assert.True(IRegexp.TooLong(new string('€', 349_526)));
        Assert.False(IRegexp.TooLong(new string('€', 349_525)));
    }

    [Fact]
    public void AnyText_NeverThrows()
    {
        var alphabet = new[]
        {
            "a", "|", "(", ")", "[", "]", "{", "}", "*", "+", "?", ".", "\\", "^", "$", "-", ",",
            "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "p", "P", "😀",
        };
        var random = new Random(31);

        for (var i = 0; i < 2000; i++)
        {
            var length = random.Next(0, 13);
            var pattern = string.Concat(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]));

            var check = IRegexp.Check(pattern);

            Assert.Equal(check.Fault == IRegexpFault.None, check.Regex != null);
            if (check.Fault == IRegexpFault.None)
            {
                IRegexp.IsMatch(check.Regex!, "a😀b");
            }
        }
    }
}
