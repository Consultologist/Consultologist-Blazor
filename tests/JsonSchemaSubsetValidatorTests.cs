using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>B4 #1003: the engine's reply validator over the strict structured-output subset, and the keyword whitelist a format must obey.</summary>
public class JsonSchemaSubsetValidatorTests
{
    private static readonly JsonNode Schema = JsonNode.Parse("""
        {
          "type": "object", "additionalProperties": false, "required": ["name", "age", "tags", "kind", "note", "nested"],
          "properties": {
            "name": { "type": "string" },
            "age": { "type": "integer" },
            "tags": { "type": "array", "items": { "type": "string" } },
            "kind": { "type": "string", "enum": ["a", "b"] },
            "note": { "type": ["string", "null"], "description": "free" },
            "nested": { "type": "object", "additionalProperties": false, "required": ["ok"], "properties": { "ok": { "type": "boolean" } } }
          }
        }
        """)!;

    private static IReadOnlyList<string> Validate(string json) => JsonSchemaSubsetValidator.Validate(JsonNode.Parse(json), Schema);

    [Fact]
    public void AConformingInstance_HasNoErrors()
    {
        Assert.Empty(Validate("""{ "name": "x", "age": 3, "tags": ["t"], "kind": "a", "note": null, "nested": { "ok": true } }"""));
    }

    [Fact]
    public void EveryMiss_IsNamedWithItsPath()
    {
        var errors = Validate("""{ "name": 1, "age": 2.5, "tags": ["t", 2], "kind": "c", "extra": 1, "nested": { } }""");

        Assert.Equal(new[]
        {
            "'name' is a number, not string.",
            "'age' is a number, not integer.",
            "'tags[1]' is a number, not string.",
            "'kind' is not one of the declared values.",
            "'$' lacks required property 'note'.",
            "'$' has undeclared property 'extra'.",
            "'nested' lacks required property 'ok'.",
        }.Order(), errors.Order());
    }

    [Fact]
    public void TheRoot_MustBeTheDeclaredType()
    {
        Assert.Equal(new[] { "'$' is an array, not object." }, Validate("[]"));
        Assert.Equal(new[] { "'$' is null, not object." }, Validate("null"));
    }

    [Fact]
    public void AnIntegerIsANumber_ButANumberIsNotAnInteger()
    {
        var number = JsonNode.Parse("""{ "type": "number" }""")!;
        var integer = JsonNode.Parse("""{ "type": "integer" }""")!;

        Assert.Empty(JsonSchemaSubsetValidator.Validate(JsonNode.Parse("3"), number));
        Assert.Empty(JsonSchemaSubsetValidator.Validate(JsonNode.Parse("3.5"), number));
        Assert.Empty(JsonSchemaSubsetValidator.Validate(JsonNode.Parse("3"), integer));
        Assert.Single(JsonSchemaSubsetValidator.Validate(JsonNode.Parse("3.5"), integer));
    }

    [Fact]
    public void AKeywordOutsideTheSubset_IsAnError_NeverASilentValid()
    {
        var schema = JsonNode.Parse("""{ "type": "object", "additionalProperties": false, "required": ["s"], "properties": { "s": { "type": "string", "pattern": "^a" } } }""")!;

        var errors = JsonSchemaSubsetValidator.Validate(JsonNode.Parse("""{ "s": "zzz" }"""), schema);

        Assert.Equal("keyword 'pattern' at 's' is outside the strict structured-output subset ($schema, additionalProperties, description, enum, items, properties, required, title, type); the engine would not enforce it.", Assert.Single(errors));
        Assert.Equal(errors, JsonSchemaSubsetValidator.UnsupportedKeywords(schema));
    }

    [Fact]
    public void TheCataloguesFormats_UseOnlyTheSubset()
    {
        foreach (var format in TestFormats.Catalog.Formats.Values.Where(f => f.Spec.Schema is not null))
        {
            Assert.Empty(JsonSchemaSubsetValidator.UnsupportedKeywords(format.Spec.Schema));
        }
    }

    [Fact]
    public void AFormatFile_WithAKeywordOutsideTheSubset_IsRefusedAtPublish()
    {
        var spec = new WorkflowFormatSpec("object", "custom", Schema: JsonNode.Parse("""{ "type": "object", "additionalProperties": false, "required": ["n"], "properties": { "n": { "type": "number", "minimum": 0 } } }"""));
        var errors = new List<string>();

        WorkflowFormatValidator.Validate("Format 'mine'", spec, WorkflowFormatOrigin.PackageLocal, errors);

        Assert.Contains(errors, e => e.StartsWith("Format 'mine' schema keyword 'minimum' at 'n' is outside", StringComparison.Ordinal));
    }
}

public class WorkflowCodeFencesTests
{
    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("  {\"a\":1}\n", "{\"a\":1}")]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("```\n{\n \"a\": 1\n}\n```\n", "{\n \"a\": 1\n}")]
    [InlineData("```JSON \r\n{\"a\":1}\r\n```", "{\"a\":1}")]
    [InlineData("Here: ```json\n{}\n```", "Here: ```json\n{}\n```")]
    public void Strip_UnwrapsAWholeFence_AndLeavesEverythingElse(string text, string expected)
    {
        Assert.Equal(expected, WorkflowCodeFences.Strip(text));
    }
}

public class WorkflowCustomPromptTests
{
    [Fact]
    public void TheLayout_IsPromptBlankLineTrailerNewlineSchema()
    {
        Assert.Equal("Do it.\n\nAnswer with one JSON object conforming to this schema.\n{\"type\":\"object\"}",
            WorkflowCustomPrompt.Compose("Do it.\n", "Answer with one JSON object conforming to this schema.", "{\"type\":\"object\"}"));
    }

    [Fact]
    public void TheBundledTrailer_IsTheCataloguesSentence()
    {
        var composed = WorkflowCustomPrompt.Compose("p", TestFormats.Catalog.CustomProducer.Trailer, "{}");

        Assert.Equal("p\n\nAnswer with one JSON object conforming to this schema.\n{}", composed);
    }
}
