using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="TypeStrings"/>, how people see a format
/// (part 02 § 2.4, D7).
/// </summary>
public class TypeStringsTests
{
    private static readonly FormatSet Formats = FormatSet.ForPackage(TypeFixture.Published(), []);

    [Theory]
    [InlineData("\"plaintext\"", "plaintext")]
    [InlineData("{ \"$ref\": \"plaintext\" }", "plaintext")]
    [InlineData("{ \"type\": \"number\" }", "number")]
    [InlineData("{ \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }", "list<plaintext>")]
    [InlineData("{ \"type\": \"array\", \"items\": { \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } } }", "list<list<plaintext>>")]
    [InlineData("{ \"anyOf\": [ { \"$ref\": \"plaintext\" }, { \"$ref\": \"classification\" } ] }", "plaintext | classification")]
    [InlineData("{ \"type\": [\"string\", \"null\"] }", "string | null")]
    [InlineData("{ \"anyOf\": [ { \"$ref\": \"judgement\" }, { \"type\": \"null\" } ] }", "judgement | null")]
    [InlineData("{ \"type\": \"object\", \"required\": [\"title\",\"notes\"], \"additionalProperties\": false, \"properties\": { \"title\": { \"type\": \"string\" }, \"notes\": { \"anyOf\": [ { \"$ref\": \"plaintext\" }, { \"type\": \"null\" } ] } } }", "{ title: string, notes: plaintext | null }")]
    [InlineData("{ \"type\": \"string\", \"enum\": [\"narrative\", \"table\", \"omit\"] }", "string {narrative, table, omit}")]
    [InlineData("{ \"type\": \"string\", \"oneOf\": [ { \"const\": \"narrative\", \"title\": \"Narrative\" }, { \"const\": \"table\", \"title\": \"Table\" } ] }", "string {narrative, table}")]
    [InlineData("{ \"type\": \"string\", \"format\": \"date\" }", "date")]
    [InlineData("{ \"type\": [\"string\", \"null\"], \"enum\": [\"a_b\", \"c_d\", null] }", "string {a_b, c_d} | null")]
    public void Print_IsTheTablesText(string format, string expected)
    {
        Assert.Equal(expected, TypeStrings.Print(JsonNode.Parse(format), Formats));
    }

    [Theory]
    [InlineData("{ \"type\": [\"null\", \"integer\"] }", "integer | null")]
    [InlineData("{ \"type\": \"boolean\" }", "boolean")]
    [InlineData("{ \"type\": \"null\" }", "null")]
    [InlineData("{ \"type\": \"object\", \"properties\": {}, \"required\": [], \"additionalProperties\": false }", "{}")]
    [InlineData("{ \"type\": [\"array\", \"null\"], \"items\": { \"type\": \"string\" } }", "list<string> | null")]
    [InlineData("{ \"type\": \"string\", \"oneOf\": [ { \"const\": \"a\", \"title\": \"A\" }, { \"const\": \"b\", \"title\": \"B\" }, { \"const\": null } ] }", "string {a, b}")]
    [InlineData("{ \"$ref\": \"verdict\", \"description\": \"The verdict.\" }", "verdict")]
    [InlineData("\"nothing_known\"", "nothing_known")]
    public void Print_TheRestOfD7(string format, string expected)
    {
        Assert.Equal(expected, TypeStrings.Print(JsonNode.Parse(format), Formats));
    }

    [Fact]
    public void ADefinition_PrintsWhatItNames()
    {
        var format = TypeFixture.Schema("""
            {
              "type": "object", "required": ["rows"], "additionalProperties": false,
              "properties": { "rows": { "type": "array", "items": { "$ref": "#/$defs/row" } } },
              "$defs": { "row": { "type": "object", "required": ["n"], "additionalProperties": false, "properties": { "n": { "type": "integer" } } } }
            }
            """);

        Assert.Equal("{ rows: list<{ n: integer }> }", TypeStrings.Print(format, Formats));
    }

    [Fact]
    public void ATextFormatsOwnSchema_PrintsItsKind()
    {
        var registry = TypeFixture.Published();

        Assert.Equal("plaintext", TypeStrings.Print(registry.BuiltInFormats["plaintext"], Formats));
        Assert.Equal("markdown_block", TypeStrings.Print(registry.BuiltInFormats["markdown_block"], Formats));
        Assert.Equal("markdown_inline", TypeStrings.Print(registry.BuiltInFormats["markdown_inline"], Formats));
        Assert.Equal("{ answer: boolean, reason: plaintext }", TypeStrings.Print(registry.BuiltInFormats["judgement"], Formats));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("[1, 2]")]
    [InlineData("{}")]
    [InlineData("{ \"type\": 5 }")]
    [InlineData("{ \"type\": [\"string\", \"number\"] }")]
    [InlineData("{ \"$ref\": 5 }")]
    [InlineData("{ \"$ref\": \"#/$defs/missing\" }")]
    [InlineData("{ \"anyOf\": [] }")]
    [InlineData("null")]
    public void NotAFormat_PrintsAQuestionMark(string format)
    {
        Assert.Equal("?", TypeStrings.Print(JsonNode.Parse(format), Formats));
    }

    [Fact]
    public void ADefinitionLoop_AndDeepNesting_Return()
    {
        var loop = TypeFixture.Schema("""{ "type": "array", "items": { "$ref": "#/$defs/x" }, "$defs": { "x": { "$ref": "#/$defs/x" } } }""");
        Assert.Equal("list<?>", TypeStrings.Print(loop, Formats));

        var deep = "{ \"type\": \"string\" }";
        for (var i = 0; i < 60; i++)
        {
            deep = $$"""{ "type": "array", "items": {{deep}} }""";
        }

        var printed = TypeStrings.Print(JsonNode.Parse(deep, documentOptions: new() { MaxDepth = 200 }), Formats);
        Assert.EndsWith("?" + new string('>', SourceBounds.JsonDepth + 1), printed);
    }

    [Fact]
    public void ANullFormatSet_IsTheCallersFault()
    {
        Assert.Throws<ArgumentNullException>(() => TypeStrings.Print(JsonValue.Create("plaintext"), null!));
    }
}
