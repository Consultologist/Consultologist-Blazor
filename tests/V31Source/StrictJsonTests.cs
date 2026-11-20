using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>#1499: the strict JSON reader refuses what RFC 8259 refuses, duplicates and depth.</summary>
public class StrictJsonTests
{
    [Fact]
    public void Parse_KeepsMemberOrder()
    {
        var result = StrictJson.Parse("{\"b\":1,\"a\":2}");
        Assert.Null(result.Fault);
        Assert.Equal(["b", "a"], result.Value!.AsObject().Select(p => p.Key));
    }

    [Theory]
    [InlineData("{\"a\":1,}")]
    [InlineData("{\"a\":1} // x")]
    [InlineData("{\"a\":1}{}")]
    [InlineData("{a:1}")]
    [InlineData("")]
    public void Parse_RefusesWhatRfc8259Refuses(string text)
    {
        var result = StrictJson.Parse(text);
        Assert.Equal(StrictJsonFault.NotJson, result.Fault);
        Assert.NotNull(result.Position);
        Assert.Null(result.Detail);
    }

    [Theory]
    [InlineData("{\"a\":1,}", 0, 7)]
    [InlineData("[1,\n2", 1, 1)]
    [InlineData("", 0, 0)]
    [InlineData("{\"a\":1}\n{}", 1, 0)]
    [InlineData("[\"x\",\n \"\\ud800\"]", 1, 1)]
    public void ANotJsonFault_CarriesItsPosition_AsTheReaderCountsIt(string text, long line, long byteInLine)
    {
        var result = StrictJson.Parse(text);

        Assert.Equal(StrictJsonFault.NotJson, result.Fault);
        Assert.Equal(new StrictJsonPosition(line, byteInLine), result.Position);
    }

    [Theory]
    [InlineData("{ x }", 0, 2, 1, 3)]
    [InlineData("{\n  \"é\": x }", 1, 8, 2, 8)]
    [InlineData("{\"a\":1", 0, 6, 1, 7)]
    [InlineData("", 0, 0, 1, 1)]
    public void Locate_CountsLinesFromOne_AndColumnsInCharacters(string text, long line, long byteInLine, int expectedLine, int expectedColumn)
    {
        Assert.Equal((expectedLine, expectedColumn), StrictJson.Locate(text, new StrictJsonPosition(line, byteInLine)));
    }

    [Fact]
    public void DuplicateKey_AtAnyDepth_IsRefused()
    {
        var result = StrictJson.Parse("{\"x\":{\"y\":[{\"label\":1,\"label\":2}]}}");
        Assert.Equal(StrictJsonFault.DuplicateMember, result.Fault);
        Assert.Equal("label", result.Detail);
    }

    [Fact]
    public void Depth32_IsAccepted()
    {
        var result = StrictJson.Parse(new string('[', 32) + new string(']', 32));
        Assert.Null(result.Fault);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void Depth33_IsRefused_WithItsPointer()
    {
        // The object is level 1, so the 33rd container is 31 arrays down: /expression/0 x 30 below the first array.
        var text = "{\"expression\":" + new string('[', 32) + new string(']', 32) + "}";
        var result = StrictJson.Parse(text);
        Assert.Equal(StrictJsonFault.TooDeep, result.Fault);
        Assert.Equal("/expression" + string.Concat(Enumerable.Repeat("/0", 31)), result.Detail);
    }

    [Fact]
    public void Parse_TellsNullFromAFault()
    {
        var result = StrictJson.Parse("null");
        Assert.Null(result.Value);
        Assert.True(result.IsNull);
        Assert.Null(result.Fault);
    }

    [Fact]
    public void Parse_NeverThrows()
    {
        foreach (var text in new[] { "\"\ud800\"", "[", "{\"a\"", "\u0000", new string('[', 100000), "1e999999" })
            StrictJson.Parse(text);
    }
}
