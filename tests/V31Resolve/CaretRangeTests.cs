using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): caret ranges (part 07 § 4.2; D10): <c>^a.b.c</c> admits at least <c>a.b.c</c> and
/// below <c>(a+1).0.0</c>; a version is three numeric parts, and anything else never admits and compares as lowest.
/// </summary>
public class CaretRangeTests
{
    [Theory]
    [InlineData("^7.2.5", "7.2.5", true)]
    [InlineData("^7.2.5", "7.2.6", true)]
    [InlineData("^7.2.5", "7.9.0", true)]
    [InlineData("^7.2.5", "7.10.0", true)]
    [InlineData("^7.2.5", "7.2.4", false)]
    [InlineData("^7.2.5", "7.1.9", false)]
    [InlineData("^7.2.5", "8.0.0", false)]
    [InlineData("^7.2.5", "6.9.9", false)]
    [InlineData("^7.2.5", "", false)]
    [InlineData("^7.2.5", "7.2", false)]
    [InlineData("^7.2.5", "x", false)]
    [InlineData("^7.2.5", "7.2.5.1", false)]
    [InlineData("^7.2.5", "7.2.5-beta", false)]
    [InlineData("^7.2.5", " 7.2.5", false)]
    [InlineData("^7.2.5", "7.-2.5", false)]
    [InlineData("^7.2.5", "99999999999.0.0", false)]
    [InlineData("7.2.5", "7.2.5", false)]
    [InlineData("^7.2", "7.2.5", false)]
    [InlineData("", "7.2.5", false)]
    [InlineData("^", "7.2.5", false)]
    [InlineData("~7.2.5", "7.2.5", false)]
    [InlineData("^1.0.0", "1.0.0", true)]
    public void Admits(string range, string version, bool admitted) =>
        Assert.Equal(admitted, CaretRange.Admits(range, version));

    [Fact]
    public void From_IsTheCaretOfTheExactVersion() =>
        Assert.Equal("^1.0.0", CaretRange.From("1.0.0"));

    [Theory]
    [InlineData("7.2.5", "7.2.5", 0)]
    [InlineData("7.2.5", "7.2.6", -1)]
    [InlineData("7.10.0", "7.9.0", 1)]
    [InlineData("8.0.0", "7.99.99", 1)]
    [InlineData("x", "0.0.0", -1)]
    [InlineData("0.0.0", "", 1)]
    [InlineData("x", "", 0)]
    public void Compare(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(CaretRange.Compare(a, b)));

    [Fact]
    public void NullArguments_NeverAdmit_NeverThrow()
    {
        Assert.False(CaretRange.Admits(null!, "1.0.0"));
        Assert.False(CaretRange.Admits("^1.0.0", null!));
        Assert.True(CaretRange.Compare(null!, "1.0.0") < 0);
    }
}
