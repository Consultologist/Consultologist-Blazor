using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1503 (specVersion 31, area 06; R4, #1507 item 5): a numeric <c>enum</c> in a format is supported, as JSON Schema
/// has it: the dialect accepts one on a <c>number</c> or an <c>integer</c>, a value is validated against it, and an
/// input of that format compiles. An enum on a string, and one that mixes text and numbers, is judged as before.
/// </summary>
public class NumericEnumTests
{
    private const string At = "Format 'verdict' at /properties/a";

    /// <summary>A package format file of one property <c>a</c> whose schema is <paramref name="field"/>.</summary>
    private static string Field(string field) => TypeFixture.FormatFile(
        $$"""
        "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": {{field}} }
        """);

    private static IReadOnlyList<SourceRefusal> Check(string json, SchemaSite site = SchemaSite.PackageFormat) =>
        FormatDialect.Check(JsonNode.Parse(json), site, "Format 'verdict'", "verdict", "formats/verdict.json");

    [Theory]
    [InlineData("""{ "type": "integer", "enum": [1, 2, 3] }""")]
    [InlineData("""{ "type": "number", "enum": [0.5, 1] }""")]
    [InlineData("""{ "type": ["integer", "null"], "enum": [1, 2, null] }""")]
    public void ANumericEnum_PassesTheDialect(string field)
    {
        Assert.Empty(Check(Field(field)));
        Assert.Empty(FormatDialect.Check(JsonNode.Parse(field), SchemaSite.Inline, "Input 'visit'", "visit", "manifest.json"));
    }

    /// <summary>A numeric enum keeps the enum rules that are not about text: two values at least, none twice, null as the type says.</summary>
    public static TheoryData<string, string, string> NumericRuleBroken => new()
    {
        { """{ "type": "integer", "enum": [1] }""", "S02-047", $"{At} declares one value; an enum with one value is a constant, not a choice." },
        { """{ "type": "integer", "enum": [1, 2, 1] }""", "S02-048", $"{At} declares value '1' twice." },
        { """{ "type": "number", "enum": [1, 1.0] }""", "S02-048", $"{At} declares value '1' twice." },
        { """{ "type": ["integer", "null"], "enum": [1, 2] }""", "S02-053", $"{At} is nullable but its enum doesn't list null; a nullable enum lists null among its values." },
        { """{ "type": "integer", "enum": [1, 2, null] }""", "A03-003", $"{At} writes 'enum' as a list; it is a list of numbers." },
        { """{ "type": "integer", "enum": [1, 2.5] }""", "A03-003", $"{At} writes 'enum' as a list; it is a list of whole numbers." },
    };

    [Theory]
    [MemberData(nameof(NumericRuleBroken))]
    public void ANumericEnum_KeepsTheEnumRules(string field, string id, string sentence)
    {
        var refusal = Assert.Single(Check(Field(field)));
        Assert.Equal((id, sentence, "formats/verdict.json"), (refusal.Id, refusal.Sentence, refusal.Path));
        V31Contract.AssertSentence(refusal);
    }

    [Theory]
    [InlineData("""{ "type": "string", "enum": ["a", 1] }""", "A03-003", $"{At} writes 'enum' as a list; it is a list of text.")]
    [InlineData("""{ "type": "number", "enum": ["a", 1] }""", "A03-004", $"{At} uses 'enum', which belongs on a string.")]
    public void AMixedEnum_IsRefusedAsBefore(string field, string id, string sentence)
    {
        var refusal = Assert.Single(Check(Field(field)));
        Assert.Equal((id, sentence), (refusal.Id, refusal.Sentence));
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void AValue_IsValidatedAgainstTheNumericEnum()
    {
        var formats = FormatSet.ForPackage(TypeFixture.Published(), []);
        var validator = new CorvusFormatValueValidator();
        var format = JsonNode.Parse("""{ "type": "integer", "enum": [1, 2, 3] }""");

        Assert.True(validator.Validate(JsonValue.Create(2), format, formats).Valid);

        var refused = validator.Validate(JsonValue.Create(4), format, formats);
        Assert.False(refused.Valid);
        Assert.Equal([new FormatValueFault("", "enum")], refused.Faults);
    }

    [Fact]
    public void AnInputOfANumericEnum_Compiles()
    {
        var files = SourceFixture.Minimal().WithJson("manifest.json", m => m["inputs"]!.AsObject()["visit_number"] = JsonNode.Parse(
            """{ "label": "Visit number", "description": null, "format": { "type": "integer", "enum": [1, 2, 3] }, "required": true, "channel": null }"""));

        var check = ResolveFixture.Run(files);
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));
        var result = ResolveCompiler.Compile(check, ResolveFixture.Published(), "7.2.5");

        Assert.True(result.Refusals.Count == 0, string.Join(" | ", result.Refusals.Select(r => $"{r.Id} {r.Sentence}")));
        Assert.Equal("""{"enum":[1,2,3],"type":"integer"}""", CanonicalJson.Write(result.Document!["inputs"]!["visit_number"]!["format"]));
    }
}
