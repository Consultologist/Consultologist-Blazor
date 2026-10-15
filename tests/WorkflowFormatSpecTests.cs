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
          "parameters": { "of": { "kind": "node", "format": "concept_list" },
                          "in": { "kind": "node", "format": "concept_list" },
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
        Assert.Equal("concept_list", spec.Parameters["of"].Format);
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

/// <summary>
/// The format-file rules (design §§ 3.1–3.2) as decided for A1 (#996): a
/// package-local file is always custom; every object format carries a schema;
/// parameters belong to attested and computed formats, kinds node | text |
/// values. One fact per rule, asserting the sentence.
/// </summary>
public class WorkflowFormatValidatorTests
{
    private static List<string> Validate(string json, WorkflowFormatOrigin origin = WorkflowFormatOrigin.Catalogue)
    {
        var spec = FormatFixtures.Read(json);
        var errors = new List<string>();
        WorkflowFormatValidator.Validate("Format 'x'", spec, origin, errors);
        return errors;
    }

    private static void AssertRefused(string json, string sentence, WorkflowFormatOrigin origin = WorkflowFormatOrigin.Catalogue)
    {
        var errors = Validate(json, origin);
        Assert.Contains(errors, e => e.StartsWith("Format 'x' ", StringComparison.Ordinal) && e.Contains(sentence));
    }

    [Theory]
    [InlineData(FormatFixtures.Prose)]
    [InlineData(FormatFixtures.Rendered)]
    [InlineData(FormatFixtures.Classification)]
    [InlineData(FormatFixtures.ConceptList)]
    [InlineData(FormatFixtures.TermsSubset)]
    public void TheCatalogueFormats_AreValid_AtOriginCatalogue(string json)
    {
        Assert.Empty(Validate(json));
    }

    [Fact]
    public void ALocalCustomFormat_IsValid()
    {
        Assert.Empty(Validate(FormatFixtures.LocalCustom, WorkflowFormatOrigin.PackageLocal));
    }

    [Theory]
    [InlineData(FormatFixtures.Prose)]
    [InlineData(FormatFixtures.Rendered)]
    [InlineData(FormatFixtures.TermsSubset)]
    public void ALocalFile_MustBeCustom(string json)
    {
        AssertRefused(json, "is package-local and must declare producer custom; attested, rendered and computed formats come from the catalogue", WorkflowFormatOrigin.PackageLocal);
    }

    [Theory]
    [InlineData("""{ "type": "prose", "producer": "attested" }""", "type 'prose' is not one of text, date, enum, boolean, number, object, array")]
    [InlineData("""{ "type": "text", "producer": "agent" }""", "producer 'agent' is not one of attested, custom, rendered, computed")]
    public void UnknownVocabulary_IsRefusedByName_AndStopsThere(string json, string sentence)
    {
        var errors = Validate(json);

        Assert.Single(errors);
        Assert.Contains(sentence, errors[0]);
    }

    [Fact]
    public void Custom_RequiresTypeObject()
    {
        AssertRefused("""{ "type": "text", "producer": "custom" }""", "declares producer custom, which requires type object");
    }

    [Fact]
    public void Computed_RequiresTypeObject()
    {
        AssertRefused("""{ "type": "text", "producer": "computed", "parameters": { "of": { "kind": "node" } } }""", "declares producer computed, which requires type object");
    }

    [Fact]
    public void Rendered_RequiresTypeText()
    {
        AssertRefused("""{ "type": "enum", "producer": "rendered", "values": ["a", "b"] }""", "declares producer rendered, which requires type text");
    }

    [Fact]
    public void OutputMacros_RequireTypeText()
    {
        AssertRefused("""{ "type": "enum", "producer": "attested", "outputMacros": true, "values": ["a", "b"] }""", "declares outputMacros, which requires type text; macros are placed on prose only");
    }

    [Theory]
    [InlineData("rendered")]
    [InlineData("computed")]
    public void OutputMacros_OnANoAgentProducer_AreRefused(string producer)
    {
        var json = producer == "rendered"
            ? """{ "type": "text", "producer": "rendered", "outputMacros": true }"""
            : """{ "type": "object", "producer": "computed", "outputMacros": true, "schema": { "type": "object", "additionalProperties": false, "required": [], "properties": {} }, "parameters": { "of": { "kind": "node" } } }""";

        AssertRefused(json, $"declares outputMacros, which a {producer} format may not");
    }

    [Fact]
    public void AnObjectFormat_MustDeclareASchema()
    {
        AssertRefused("""{ "type": "object", "producer": "attested" }""", "is type 'object' and must declare a schema");
    }

    [Fact]
    public void ASchema_RequiresTypeObject()
    {
        AssertRefused("""{ "type": "text", "producer": "attested", "schema": { "type": "object" } }""", "declares a schema, which requires type object");
    }

    [Fact]
    public void ASchema_MustBeAJsonObject()
    {
        AssertRefused("""{ "type": "object", "producer": "attested", "schema": ["x"] }""", "schema must be a JSON object");
    }

    [Fact]
    public void ASchema_ObeysTheStrictStructuredOutputSubset_WithTheFormatAsSubject()
    {
        var errors = Validate("""{ "type": "object", "producer": "custom", "schema": { "type": "object", "properties": { "a": { "type": "string" } } } }""");

        Assert.Contains(errors, e => e.StartsWith("Format 'x' schema object must set additionalProperties:false", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Format 'x' schema property 'a' must be listed in required", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEnumFormat_NeedsValuesOrAValuesParameter()
    {
        AssertRefused("""{ "type": "enum", "producer": "attested" }""", "is type 'enum' and must declare values or a parameter of kind values");
    }

    [Fact]
    public void AnEnumFormat_MayNotHaveBoth()
    {
        AssertRefused("""{ "type": "enum", "producer": "attested", "values": ["a", "b"], "parameters": { "values": { "kind": "values" } } }""", "declares values and a parameter of kind values; an enum's answer set comes from exactly one");
    }

    [Fact]
    public void Values_RequireTypeEnum()
    {
        AssertRefused("""{ "type": "text", "producer": "attested", "values": ["a", "b"] }""", "declares values, which requires type enum");
    }

    [Theory]
    [InlineData("""["only"]""", "declares one enum value; an enum with one value is a constant, not a choice")]
    [InlineData("""["a", "a"]""", "declares duplicate enum value 'a'")]
    [InlineData("""["Yes", "no"]""", "enum value 'Yes' must be snake_case")]
    [InlineData("""[]""", "is type 'enum' and must declare values.")]
    public void Values_ObeyTheEnumRules(string values, string sentence)
    {
        AssertRefused($$"""{ "type": "enum", "producer": "attested", "values": {{values}} }""", sentence);
    }

    [Theory]
    [InlineData("custom", """{ "type": "object", "producer": "custom", "schema": { "type": "object", "additionalProperties": false, "required": [], "properties": {} }, "parameters": { "n": { "kind": "text" } } }""")]
    [InlineData("rendered", """{ "type": "text", "producer": "rendered", "parameters": { "n": { "kind": "text" } } }""")]
    public void Parameters_BelongToAttestedAndComputedOnly(string producer, string json)
    {
        AssertRefused(json, $"declares parameters, which a {producer} format may not; only attested and computed formats take input through with");
    }

    [Theory]
    [InlineData("""{ "type": "object", "producer": "computed", "schema": { "type": "object", "additionalProperties": false, "required": [], "properties": {} } }""")]
    [InlineData("""{ "type": "object", "producer": "computed", "schema": { "type": "object", "additionalProperties": false, "required": [], "properties": {} }, "parameters": {} }""")]
    public void Computed_MustDeclareAParameter(string json)
    {
        AssertRefused(json, "declares producer computed and must declare at least one parameter");
    }

    [Fact]
    public void AnAttestedTextFormat_MayTakeTypedInput()
    {
        Assert.Empty(Validate("""{ "type": "text", "producer": "attested", "parameters": { "targetLength": { "kind": "text" } } }"""));
    }

    [Theory]
    [InlineData("""{ "Fail-With": { "kind": "text" } }""", "parameter 'Fail-With' must be an identifier")]
    [InlineData("""{ "of": { } }""", "parameter 'of' must declare a kind (one of node, text, values)")]
    [InlineData("""{ "of": { "kind": "input" } }""", "parameter 'of' kind 'input' is not one of node, text, values")]
    [InlineData("""{ "of": { "kind": "text", "format": "concept_list" } }""", "parameter 'of' declares a format, which only a node parameter may")]
    [InlineData("""{ "of": { "kind": "node", "format": " " } }""", "parameter 'of' format must not be blank")]
    public void ParameterDeclarations_AreChecked(string parameters, string sentence)
    {
        AssertRefused($$"""{ "type": "text", "producer": "attested", "parameters": {{parameters}} }""", sentence);
    }

    [Fact]
    public void AValuesParameter_OnlyOnAnEnumFormat()
    {
        AssertRefused("""{ "type": "text", "producer": "attested", "parameters": { "values": { "kind": "values" } } }""", "parameter 'values' is kind values, which only an enum format may declare");
    }

    [Fact]
    public void OneValuesParameter_AtMost()
    {
        AssertRefused("""{ "type": "enum", "producer": "attested", "parameters": { "a": { "kind": "values" }, "b": { "kind": "values" } } }""", "declares more than one parameter of kind values");
    }

    [Fact]
    public void ABlankDescription_IsRefused()
    {
        AssertRefused("""{ "type": "text", "producer": "attested", "description": "  " }""", "description must not be blank");
    }

    [Fact]
    public void TheCustomSchemaSentences_AreUnchanged()
    {
        // The subset walk is shared; the v22 caller still opens with "Custom schema '<id>'".
        var errors = new List<string>();
        WorkflowPackageValidator.ValidateStructuredOutputSubset("Custom schema 'meds'", JsonNode.Parse("""{ "type": "object", "properties": { "a": {} } }"""), errors);

        Assert.Contains("Custom schema 'meds' object must set additionalProperties:false (the strict structured-output subset).", errors);
        Assert.Contains("Custom schema 'meds' property 'a' must be listed in required (optionality is expressed by a nullable type, not omission).", errors);
    }
}
