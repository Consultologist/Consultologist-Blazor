using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// The five catalogue formats of package-format-v25-design.md § 3.3 and one
/// package-local custom format, as the files the tests read. The files carry
/// no id: the id is the fixture's name here, the lister's elsewhere.
/// </summary>
public static class FormatFixtures
{
    public const string Prose = """
        { "type": "text", "producer": "attested", "outputMacros": true,
          "description": "Consult prose." }
        """;

    public const string Rendered = """
        { "type": "text", "producer": "rendered", "outputMacros": false,
          "description": "The rendered prompt's bytes." }
        """;

    public const string Classification = """
        { "type": "enum", "producer": "attested", "outputMacros": false,
          "description": "One value from the node's answer set.",
          "parameters": { "values": { "kind": "values" } } }
        """;

    public const string ConceptList = """
        { "type": "object", "producer": "attested", "outputMacros": false,
          "description": "Clinical concepts named in the input.",
          "schema": { "type": "object", "additionalProperties": false, "required": ["concepts"],
                      "properties": { "concepts": { "type": "array", "items": { "type": "string" } } } } }
        """;

    public const string TermsSubset = """
        { "type": "object", "producer": "computed", "outputMacros": false,
          "description": "Every concept of 'of' appears in 'in'.",
          "schema": { "type": "object", "additionalProperties": false, "required": ["pass", "uncovered", "untested"],
                      "properties": { "pass": { "type": "boolean" },
                                      "uncovered": { "type": "array", "items": { "type": "string" } },
                                      "untested": { "type": "array", "items": { "type": "string" } } } },
          "parameters": { "of": { "kind": "node", "format": "concept-list" },
                          "in": { "kind": "node", "format": "concept-list" },
                          "failWith": { "kind": "text" } } }
        """;

    public const string LocalCustom = """
        { "type": "object", "producer": "custom", "outputMacros": false,
          "description": "A medication list.",
          "schema": { "type": "object", "additionalProperties": false, "required": ["medications"],
                      "properties": { "medications": { "type": "array", "items": { "type": "string" } } } } }
        """;

    public static WorkflowFormatSpec Read(string json, string subject = "Format 'x'")
    {
        var errors = new List<string>();
        Assert.True(WorkflowFormatSpecJson.TryRead(json, subject, errors, out var spec), string.Join(" | ", errors));
        return spec!;
    }

    public static List<string> ReadErrors(string? json, string subject = "Format 'x'")
    {
        var errors = new List<string>();
        Assert.False(WorkflowFormatSpecJson.TryRead(json, subject, errors, out _));
        return errors;
    }
}

public class WorkflowFormatSpecJsonTests
{
    [Fact]
    public void EveryField_RoundTrips()
    {
        var spec = FormatFixtures.Read(FormatFixtures.TermsSubset);

        Assert.Equal("object", spec.Type);
        Assert.Equal("computed", spec.Producer);
        Assert.False(spec.AllowsOutputMacros);
        Assert.Equal("Every concept of 'of' appears in 'in'.", spec.Description);
        Assert.NotNull(spec.Schema);
        Assert.Equal(3, spec.Parameters!.Count);
        Assert.Equal("concept-list", spec.Parameters["of"].Format);
        Assert.Equal("text", spec.Parameters["failWith"].Kind);
        Assert.Null(spec.Parameters["failWith"].Format);

        var written = WorkflowFormatSpecJson.ToJson(spec);
        var again = FormatFixtures.Read(written);

        Assert.Equal(spec.Type, again.Type);
        Assert.Equal(spec.Producer, again.Producer);
        Assert.Equal(spec.Description, again.Description);
        Assert.Equal(spec.Parameters.Count, again.Parameters!.Count);
        Assert.Equal(WorkflowFormatSpecJson.CanonicalJson(spec), WorkflowFormatSpecJson.CanonicalJson(again));
        Assert.EndsWith("\n", written);

        using var document = JsonDocument.Parse(written);
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("schema").ValueKind);
        Assert.False(document.RootElement.TryGetProperty("values", out _), "nulls are omitted");
        Assert.False(document.RootElement.TryGetProperty("Type", out _), "camelCase on the wire");
    }

    [Fact]
    public void Values_RoundTrip_AsStrings()
    {
        var spec = FormatFixtures.Read("""{ "type": "enum", "producer": "custom", "values": ["yes", "no"] }""");

        Assert.Equal(new[] { "yes", "no" }, spec.Values);
        Assert.Null(spec.ValuesParameter);

        var classification = FormatFixtures.Read(FormatFixtures.Classification);
        Assert.Equal("values", classification.ValuesParameter);
    }

    [Fact]
    public void OutputMacros_Absent_ReadsAsFalse()
    {
        var spec = FormatFixtures.Read("""{ "type": "text", "producer": "attested" }""");

        Assert.Null(spec.OutputMacros);
        Assert.False(spec.AllowsOutputMacros);
        Assert.False(spec.HasParameters);
    }

    [Fact]
    public void CanonicalJson_IsIndependentOfKeyOrderAndWhitespace()
    {
        var a = WorkflowFormatSpecJson.CanonicalJson("""{ "type": "text", "producer": "attested", "description": "Prose" }""");
        var b = WorkflowFormatSpecJson.CanonicalJson("""{"description":"Prose","producer":"attested",  "type":"text"}""");

        Assert.Equal(a, b);
        Assert.Equal("""{"description":"Prose","producer":"attested","type":"text"}""", a);
    }

    [Fact]
    public void CanonicalJson_KeepsDescription_UnlikeTheSchemaCanonicalizer()
    {
        const string json = """{ "type": "text", "producer": "attested", "description": "Prose", "schema": { "title": "T" } }""";

        Assert.Contains("\"description\"", WorkflowFormatSpecJson.CanonicalJson(json));
        Assert.Contains("\"title\"", WorkflowFormatSpecJson.CanonicalJson(json));
        Assert.DoesNotContain("\"title\"", WorkflowPackageValidator.CanonicalizeSchema(JsonNode.Parse(json)));
    }

    [Fact]
    public void CanonicalJson_SortsNestedObjects()
    {
        var canonical = WorkflowFormatSpecJson.CanonicalJson(
            """{ "type": "object", "producer": "custom", "schema": { "properties": { "b": {}, "a": {} }, "type": "object" } }""");

        Assert.Equal("""{"producer":"custom","schema":{"properties":{"a":{},"b":{}},"type":"object"},"type":"object"}""", canonical);
    }

    [Theory]
    [InlineData("", "file is empty")]
    [InlineData("   ", "file is empty")]
    [InlineData("{ not json", "file is not valid JSON")]
    [InlineData("null", "file is not valid JSON")]
    [InlineData("""{ "producer": "attested" }""", "must declare a type")]
    [InlineData("""{ "type": "text" }""", "must declare a producer")]
    [InlineData("""{ "type": "text", "producer": "attested", "id": "prose" }""", "property '$.id' is not part of a format file")]
    [InlineData("""{ "type": "text", "producer": "attested", "parameters": { "x": { "kind": "text", "label": "X" } } }""", "property '$.parameters.x.label' is not part of a format file")]
    public void Read_RefusesByName(string json, string sentence)
    {
        var errors = FormatFixtures.ReadErrors(json, "Format 'prose'");

        Assert.Contains(errors, e => e.StartsWith("Format 'prose'", StringComparison.Ordinal) && e.Contains(sentence));
    }

    [Fact]
    public void Read_NamesBothMissingFields_AtOnce()
    {
        var errors = FormatFixtures.ReadErrors("{}");

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Read_IsCaseInsensitive_LikeTheManifest()
    {
        var spec = FormatFixtures.Read("""{ "Type": "text", "PRODUCER": "attested", "OutputMacros": true }""");

        Assert.True(spec.AllowsOutputMacros);
    }
}
