using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>#1501: the plain-text conversion (D13), pinned by goldens that were read before they were frozen.</summary>
public class PlainConversionTests
{
    [Theory]
    [InlineData("**Letrozole** 2.5 mg daily", "Letrozole 2.5 mg daily")]
    [InlineData("# Plan\n\nContinue [letrozole](https://example.org).", "Plan\nContinue letrozole.")]
    [InlineData("- hot flushes\n- joint pain", "hot flushes\njoint pain")]
    [InlineData("| Drug | Dose |\n|---|---|\n| Letrozole | 2.5 mg |", "Drug Dose Letrozole 2.5 mg")]
    [InlineData("", "")]
    [InlineData("plain text, no marks", "plain text, no marks")]
    public void ToPlain_Goldens(string markdown, string plain) =>
        Assert.Equal(plain, PlainConversion.ToPlain(markdown));

    [Fact]
    public void ToPlain_HasNoCarriageReturn()
    {
        var plain = PlainConversion.ToPlain("a\r\nb");
        Assert.NotNull(plain);
        Assert.DoesNotContain('\r', plain);
    }

    [Theory]
    [InlineData("para\n\n")]
    [InlineData("# Plan\n\n")]
    [InlineData("- one\n- two\n")]
    [InlineData("a\r\n")]
    [InlineData("| Drug | Dose |\n|---|---|\n| Letrozole | 2.5 mg |")]
    [InlineData("a  \n")]
    [InlineData("a\t")]
    public void ToPlain_NeverEndsWithASpaceOrALineBreak(string markdown) =>
        Assert.DoesNotContain(PlainConversion.ToPlain(markdown)!.LastOrDefault(), " \t\r\n");

    /// <summary>Markdown Markdig refuses as nested too deeply: 200 quotes, 300 list levels, 50,000 brackets.</summary>
    public static TheoryData<string> TooDeep() =>
    [
        string.Concat(Enumerable.Repeat("> ", 200)) + "Margaret Okafor",
        string.Concat(Enumerable.Range(0, 300).Select(i => new string(' ', i * 2) + "- level\n")),
        new string('[', 50_000),
    ];

    [Theory]
    [MemberData(nameof(TooDeep))]
    public void ToPlain_MarkdownTooDeepToConvert_IsNull_NeverAThrow(string markdown)
    {
        var plain = PlainConversion.ToPlain(markdown);
        Assert.Null(plain);
    }

    [Fact]
    public void Version_IsTheContractsVersion() =>
        Assert.Equal(PlainConversion.Version, V31Source.V31Contract.Json("constants.json")["builtins"]!["plain_conversion"]!.GetValue<string>());
}
