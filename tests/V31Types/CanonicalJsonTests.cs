using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>#1500 (specVersion 31, area 03): <see cref="CanonicalJson"/> is RFC 8785 and the hash over its bytes.</summary>
public class CanonicalJsonTests
{
    [Fact]
    public void Write_SortsMembers_ByUtf16CodeUnits()
    {
        // RFC 8785 § 3.2.3's example.
        var node = new JsonObject
        {
            ["€"] = "Euro Sign",
            ["\r"] = "Carriage Return",
            ["דּ"] = "Hebrew Letter Dalet With Dagesh",
            ["1"] = "One",
            ["\U0001F600"] = "Emoji: Grinning Face",
            ["\u0080"] = "Control",
            ["ö"] = "Latin Small Letter O With Diaeresis",
        };

        var keys = JsonNode.Parse(CanonicalJson.Write(node))!.AsObject().Select(p => p.Key).ToArray();

        Assert.Equal(["\r", "1", "\u0080", "ö", "€", "\U0001F600", "דּ"], keys);
    }

    [Theory]
    [InlineData("1.0", "1")]
    [InlineData("1e2", "100")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("1e-7", "1e-7")]
    [InlineData("1e21", "1e+21")]
    [InlineData("-0", "0")]
    [InlineData("333333333.33333329", "333333333.3333333")]
    [InlineData("4.50", "4.5")]
    [InlineData("2e-3", "0.002")]
    public void Write_PrintsNumbers_AsEcmaScriptDoes(string written, string expected)
    {
        Assert.Equal(expected, CanonicalJson.Write(JsonNode.Parse(written)));
    }

    [Fact]
    public void Write_EscapesOnlyWhatItMust()
    {
        // RFC 8785 § 3.2.2.2: the control characters short or as \u00xx, the quote and the backslash; nothing else.
        var node = JsonValue.Create("€$\u000f\nA'B\"\\\\\"/");

        Assert.Equal("\"€$\\u000f\\nA'B\\\"\\\\\\\\\\\"/\"", CanonicalJson.Write(node));
    }

    [Fact]
    public void Write_KeepsArrayOrder_AndWritesNoWhitespace()
    {
        Assert.Equal("{\"a\":null,\"b\":[3,1,2]}", CanonicalJson.Write(JsonNode.Parse("{ \"b\": [3, 1, 2], \"a\": null }")));
    }

    [Fact]
    public void Write_ReadsNoneAsNull_AndBooleansAsWords()
    {
        Assert.Equal("null", CanonicalJson.Write(null));
        Assert.Equal("[true,false]", CanonicalJson.Write(JsonNode.Parse("[true,false]")));
    }

    [Fact]
    public void Hash_IsSha256OfTheCanonicalBytes()
    {
        Assert.Equal(
            "sha256:44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
            CanonicalJson.Hash(JsonNode.Parse("{}")));
        Assert.Equal(CanonicalJson.Hash(JsonNode.Parse("{\"a\":1,\"b\":2}")), CanonicalJson.Hash(JsonNode.Parse("{ \"b\": 2.0, \"a\": 1 }")));
    }

    [Fact]
    public void HashText_HashesTheUtf8OfTheText()
    {
        Assert.Equal(CanonicalJson.Hash(JsonNode.Parse("{}")), CanonicalJson.HashText("{}"));
    }
}
