using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>#1501: the plain-text conversion (D13), pinned by goldens that were read before they were frozen.</summary>
public class PlainConversionTests
{
    [Theory]
    [InlineData("**Letrozole** 2.5 mg daily", "Letrozole 2.5 mg daily")]
    [InlineData("# Plan\n\nContinue [letrozole](https://example.org).", "Plan\nContinue letrozole.")]
    [InlineData("- hot flushes\n- joint pain", "hot flushes\njoint pain")]
    [InlineData("| Drug | Dose |\n|---|---|\n| Letrozole | 2.5 mg |", "Drug Dose Letrozole 2.5 mg ")]
    [InlineData("", "")]
    [InlineData("plain text, no marks", "plain text, no marks")]
    public void ToPlain_Goldens(string markdown, string plain) =>
        Assert.Equal(plain, PlainConversion.ToPlain(markdown));

    [Fact]
    public void ToPlain_HasNoCarriageReturn()
    {
        var plain = PlainConversion.ToPlain("a\r\nb");
        Assert.DoesNotContain('\r', plain);
    }

    [Theory]
    [InlineData("para\n\n")]
    [InlineData("# Plan\n\n")]
    [InlineData("- one\n- two\n")]
    [InlineData("a\r\n")]
    public void ToPlain_NeverEndsWithALineBreak(string markdown) =>
        Assert.False(PlainConversion.ToPlain(markdown).EndsWith('\n'));

    [Fact]
    public void Version_IsTheContractsVersion() =>
        Assert.Equal(V31Source.V31Contract.Json("constants.json")["builtins"]!["plain_conversion"]!.GetValue<string>(), PlainConversion.Version);
}
