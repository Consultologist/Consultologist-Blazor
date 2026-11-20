using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="FormatTypes"/>, the two equalities and their hashes
/// (part 02 § 2.3, D8, D9), one-way fit, and what a format is (its shape, nullable, its text kind).
/// </summary>
public class FormatTypesTests
{
    private static readonly TypeRegistry Registry = TypeFixture.Published();
    private static readonly FormatSet Formats = FormatSet.ForPackage(Registry, []);

    private static JsonNode? J(string json) => JsonNode.Parse(json);

    private static SourceFormat Format(string id, string schema) =>
        new(id, $"formats/{id}.json", TypeFixture.Schema(schema));

    /// <summary>
    /// <c>example-nested-note</c>'s <c>verdict</c> (part 02 § 5.3) with a nullable note and a date;
    /// <paramref name="note"/>, <paramref name="on"/> and <paramref name="description"/> vary.
    /// </summary>
    private static JsonObject Verdict(
        string note = """{ "type": ["string", "null"] }""",
        string on = """{ "type": "string", "format": "date" }""",
        string description = "A judgement of a draft summary: good, or revise with what to change.") =>
        TypeFixture.Schema($$"""
            {
              "$schema": "urn:consultologist:spec:31:dialect",
              "type": "object",
              "description": "{{description}}",
              "required": ["verdict", "feedback", "note", "on"],
              "additionalProperties": false,
              "properties": {
                "verdict":  { "type": "string", "enum": ["good", "revise"] },
                "feedback": { "$ref": "plaintext" },
                "note": {{note}},
                "on": {{on}}
              }
            }
            """);

    [Fact]
    public void TypeHash_IgnoresOrderAndWording_FormatHashDoesNot()
    {
        var verdict = Verdict();
        var reordered = TypeFixture.Schema("""
            {
              "properties": {
                "on":       { "format": "date", "type": "string" },
                "note":     { "type": ["string", "null"] },
                "feedback": { "$ref": "plaintext" },
                "verdict":  { "enum": ["good", "revise"], "type": "string" }
              },
              "additionalProperties": false,
              "required": ["verdict", "feedback", "note", "on"],
              "description": "A judgement of a draft summary: good, or revise with what to change.",
              "type": "object",
              "$schema": "urn:consultologist:spec:31:dialect"
            }
            """);
        var nullFirst = Verdict(note: """{ "type": ["null", "string"] }""");
        var dateByName = Verdict(on: """{ "$ref": "date" }""");
        var dateSchemaInline = Verdict(on: """{ "type": "string", "format": "date", "description": "A calendar date, written YYYY-MM-DD." }""");
        var reworded = Verdict(description: "Good, or revise with what to change.");

        var typeHash = FormatTypes.TypeHash(verdict, Formats);
        Assert.StartsWith("sha256:", typeHash);
        Assert.All(
            new[] { reordered, nullFirst, dateByName, dateSchemaInline, reworded },
            v => Assert.Equal(typeHash, FormatTypes.TypeHash(v, Formats)));

        var formatHash = FormatTypes.FormatHash(verdict, Formats);
        Assert.Equal(formatHash, FormatTypes.FormatHash(reordered, Formats));
        Assert.Equal(formatHash, FormatTypes.FormatHash(nullFirst, Formats));
        Assert.Equal(FormatTypes.FormatHash(dateByName, Formats), FormatTypes.FormatHash(dateSchemaInline, Formats));
        Assert.NotEqual(formatHash, FormatTypes.FormatHash(reworded, Formats));

        // required is a set to the type form (D9); the full form keeps it as written.
        var requiredReversed = Verdict();
        requiredReversed["required"] = new JsonArray("on", "note", "feedback", "verdict");
        Assert.Equal(typeHash, FormatTypes.TypeHash(requiredReversed, Formats));
        Assert.NotEqual(formatHash, FormatTypes.FormatHash(requiredReversed, Formats));

        // A union's members in another order are one type; a labelled enum's titles are wording.
        Assert.True(FormatTypes.Same(J("""{ "anyOf": [ { "$ref": "judgement" }, { "type": "null" } ] }"""), J("""{ "anyOf": [ { "type": "null" }, { "$ref": "judgement" } ] }"""), Formats));
        Assert.True(FormatTypes.Same(
            J("""{ "type": "string", "oneOf": [ { "const": "a", "title": "A" }, { "const": "b", "title": "B" } ] }"""),
            J("""{ "type": "string", "oneOf": [ { "const": "a", "title": "Ay" }, { "const": "b" } ] }"""),
            Formats));
        Assert.False(FormatTypes.Same(J("""{ "type": "string", "enum": ["a", "b"] }"""), J("""{ "type": "string", "enum": ["b", "a"] }"""), Formats));
    }

    [Fact]
    public void ABuiltInsName_EqualsItsSchema()
    {
        Assert.True(FormatTypes.Same(J("\"date\""), J("""{ "type": "string", "format": "date" }"""), Formats));
        Assert.True(FormatTypes.Same(J("\"string\""), J("""{ "type": "string" }"""), Formats));
        Assert.True(FormatTypes.Same(J("\"number\""), J("""{ "type": "number", "description": "How many." }"""), Formats));
        Assert.True(FormatTypes.Same(J("\"plaintext\""), J("""{ "$ref": "plaintext", "title": "Notes" }"""), Formats));
        Assert.False(FormatTypes.Same(J("\"plaintext\""), J("""{ "type": "string" }"""), Formats));
        Assert.False(FormatTypes.Same(J("\"number\""), J("""{ "type": "integer" }"""), Formats));
        Assert.Equal(FormatTypes.TypeHash(J("\"date\""), Formats), FormatTypes.TypeHash(Registry.BuiltInFormats["date"], Formats));
        Assert.Equal("""{"$ref":"date"}""", CanonicalJson.Write(FormatTypes.TypeForm(J("\"date\""), Formats)));
        Assert.Equal("""{"type":"integer"}""", CanonicalJson.Write(FormatTypes.TypeForm(J("""{ "type": "integer" }"""), Formats)));

        // The nullable date the dialect refuses still has the one normal form.
        Assert.True(FormatTypes.Same(
            J("""{ "type": ["string", "null"], "format": "date" }"""),
            J("""{ "anyOf": [ { "$ref": "date" }, { "type": "null" } ] }"""),
            Formats));
    }

    [Fact]
    public void FormatHash_LeavesOutId()
    {
        var other = Registry.CatalogueFormats["classification"].DeepClone().AsObject();
        other["$id"] = "urn:consultologist:format:classification@v2027.1.1";

        Assert.Equal(FormatTypes.FormatHash(J("\"classification\""), Formats), FormatTypes.FormatHash(other, Formats));
        Assert.Equal("urn:consultologist:format:classification@v2026.12.1", (string?)FormatTypes.FullForm(J("\"classification\""), Formats)!["$id"]);

        other["description"] = "Another wording.";
        Assert.NotEqual(FormatTypes.FormatHash(J("\"classification\""), Formats), FormatTypes.FormatHash(other, Formats));
    }

    [Fact]
    public void FullForm_InlinesEveryReference_AndJoinsDescriptions()
    {
        var full = FormatTypes.FullForm(J("\"judgement\""), Formats)!.AsObject();

        Assert.Equal("urn:consultologist:spec:31:dialect", (string?)full["$schema"]);
        var reason = full["properties"]!["reason"]!.AsObject();
        Assert.Equal(
            """{"contentMediaType":"text/plain","description":"Plain text, with no markup.\nWhy, in a sentence or two, from the text given.","type":"string"}""",
            CanonicalJson.Write(reason));

        // The site's title replaces the target's; a site without a description keeps the target's.
        var titled = FormatTypes.FullForm(J("""{ "type": "array", "items": { "$ref": "plaintext", "title": "Line" } }"""), Formats)!;
        Assert.Equal(
            """{"items":{"contentMediaType":"text/plain","description":"Plain text, with no markup.","title":"Line","type":"string"},"type":"array"}""",
            CanonicalJson.Write(titled));

        // #/$defs are inlined and the $defs go; a name at the root keeps its $schema and $id, and so does its $ref.
        var defs = TypeFixture.Schema("""
            {
              "type": "object", "required": ["rows"], "additionalProperties": false,
              "properties": { "rows": { "type": "array", "items": { "$ref": "#/$defs/row", "description": "One row." } } },
              "$defs": { "row": { "type": "object", "description": "A row.", "required": ["d"], "additionalProperties": false, "properties": { "d": "date" } } }
            }
            """);
        Assert.Equal(
            """{"additionalProperties":false,"properties":{"rows":{"items":{"additionalProperties":false,"description":"A row.\nOne row.","properties":{"d":{"description":"A calendar date, written YYYY-MM-DD.","format":"date","type":"string"}},"required":["d"],"type":"object"},"type":"array"}},"required":["rows"],"type":"object"}""",
            CanonicalJson.Write(FormatTypes.FullForm(defs, Formats)));
        Assert.Equal(
            CanonicalJson.Write(FormatTypes.FullForm(J("\"judgement\""), Formats)),
            CanonicalJson.Write(FormatTypes.FullForm(J("""{ "$ref": "judgement" }"""), Formats)));
    }

    [Fact]
    public void TypeForm_KeepsReferencesByName()
    {
        var form = FormatTypes.TypeForm(J("\"judgement\""), Formats)!;

        Assert.Equal(
            """{"additionalProperties":false,"properties":{"answer":{"$ref":"boolean"},"reason":{"$ref":"plaintext"}},"required":["answer","reason"],"type":"object"}""",
            CanonicalJson.Write(form));

        var nullable = FormatTypes.TypeForm(J("""{ "type": "object", "properties": { "title": { "type": ["null", "integer"], "title": "T" }, "x": "plaintext" }, "required": ["x", "title"], "additionalProperties": false }"""), Formats);
        Assert.Equal(
            """{"additionalProperties":false,"properties":{"title":{"anyOf":[{"type":"integer"},{"type":"null"}]},"x":{"$ref":"plaintext"}},"required":["title","x"],"type":"object"}""",
            CanonicalJson.Write(nullable));
    }

    public static TheoryData<string, string, bool> Fit => new()
    {
        { """{ "type": "integer" }""", "\"number\"", true },
        { "\"number\"", """{ "type": "integer" }""", false },
        { "\"markdown_block\"", "\"string\"", true },
        { """{ "$ref": "plaintext" }""", """{ "type": "string" }""", true },
        { "\"string\"", "\"markdown_block\"", false },
        { "\"plaintext\"", """{ "anyOf": [ { "$ref": "plaintext" }, { "$ref": "classification" } ] }""", true },
        { """{ "anyOf": [ { "$ref": "plaintext" }, { "$ref": "classification" } ] }""", "\"plaintext\"", false },
        { """{ "type": "array", "items": { "type": "integer" } }""", """{ "type": "array", "items": { "type": "number" } }""", true },
        { """{ "type": "array", "items": { "type": "number" } }""", """{ "type": "array", "items": { "type": "integer" } }""", false },
        { "\"plaintext\"", """{ "anyOf": [ { "$ref": "plaintext" }, { "type": "null" } ] }""", true },
        { """{ "anyOf": [ { "$ref": "plaintext" }, { "type": "null" } ] }""", "\"plaintext\"", false },
        { """{ "type": "string" }""", """{ "type": ["string", "null"] }""", true },
        { """{ "type": ["string", "null"] }""", """{ "type": "string" }""", false },
        { """{ "anyOf": [ { "$ref": "plaintext" }, { "type": "null" } ] }""", """{ "anyOf": [ { "type": "null" }, { "$ref": "judgement" }, { "$ref": "plaintext" } ] }""", true },
        { """{ "anyOf": [ { "$ref": "plaintext" }, { "$ref": "judgement" }, { "type": "null" } ] }""", """{ "anyOf": [ { "$ref": "plaintext" }, { "type": "null" } ] }""", false },
        { """{ "type": "array", "items": { "$ref": "plaintext" } }""", """{ "type": "array", "items": { "type": "string" } }""", true },
        { "\"judgement\"", "\"classification\"", false },
        { "\"judgement\"", "\"judgement\"", true },
        { "5", "5", true },
        { "5", "6", false },
    };

    [Theory]
    [MemberData(nameof(Fit))]
    public void Fits_IsOneWay(string value, string expected, bool fits)
    {
        Assert.Equal(fits, FormatTypes.Fits(J(value), J(expected), Formats));
    }

    [Fact]
    public void ListOf_WritesANameAsARef()
    {
        Assert.Equal("""{"items":{"$ref":"plaintext"},"type":"array"}""", CanonicalJson.Write(FormatTypes.ListOf(J("\"plaintext\"")!)));
        var item = J("""{ "type": "integer" }""")!;
        var list = FormatTypes.ListOf(item);
        Assert.Equal("""{"items":{"type":"integer"},"type":"array"}""", CanonicalJson.Write(list));
        Assert.NotSame(item, list["items"]);
    }

    [Fact]
    public void UnionOf_OneFormat_IsThatFormat_AndTwoAreAnAnyOfInOrder()
    {
        var one = FormatTypes.UnionOf([J("\"date\"")!, J("""{ "type": "string", "format": "date" }""")!, J("""{ "$ref": "date" }""")!], Formats);
        Assert.Equal("\"date\"", CanonicalJson.Write(one));

        var two = FormatTypes.UnionOf([J("\"plaintext\"")!, J("\"judgement\"")!, J("""{ "$ref": "plaintext" }""")!, J("""{ "type": "null" }""")!], Formats);
        Assert.Equal("""{"anyOf":[{"$ref":"plaintext"},{"$ref":"judgement"},{"type":"null"}]}""", CanonicalJson.Write(two));
        Assert.Equal("plaintext | judgement | null", TypeStrings.Print(two, Formats));
    }

    [Fact]
    public void Shape_ReadsThroughNamesAndDefs()
    {
        var defs = TypeFixture.Schema("""
            { "type": "array", "items": { "$ref": "#/$defs/kind" }, "$defs": { "kind": { "$ref": "#/$defs/inner" }, "inner": { "type": "string", "enum": ["a_b", "c_d"] } } }
            """);
        var item = defs["items"];

        Assert.Equal(FormatShape.Text, FormatTypes.Shape(J("\"plaintext\""), Formats));
        Assert.Equal(FormatShape.Text, FormatTypes.Shape(J("""{ "$ref": "markdown_inline" }"""), Formats));
        Assert.Equal(FormatShape.Date, FormatTypes.Shape(J("\"date\""), Formats));
        Assert.Equal(FormatShape.String, FormatTypes.Shape(J("\"string\""), Formats));
        Assert.Equal(FormatShape.Number, FormatTypes.Shape(J("\"number\""), Formats));
        Assert.Equal(FormatShape.Integer, FormatTypes.Shape(J("""{ "type": "integer" }"""), Formats));
        Assert.Equal(FormatShape.Boolean, FormatTypes.Shape(J("\"boolean\""), Formats));
        Assert.Equal(FormatShape.Object, FormatTypes.Shape(J("\"judgement\""), Formats));
        Assert.Equal(FormatShape.List, FormatTypes.Shape(defs, Formats));
        Assert.Equal(FormatShape.Enum, FormatTypes.Shape(J("""{ "type": "string", "oneOf": [ { "const": "a", "title": "A" } ] }"""), Formats));
        Assert.Equal(FormatShape.Null, FormatTypes.Shape(J("""{ "type": "null" }"""), Formats));
        Assert.Equal(FormatShape.Union, FormatTypes.Shape(J("""{ "anyOf": [ { "$ref": "plaintext" }, { "$ref": "judgement" } ] }"""), Formats));
        Assert.Equal(FormatShape.Unknown, FormatTypes.Shape(J("\"nothing\""), Formats));
        Assert.Equal(FormatShape.Unknown, FormatTypes.Shape(J("{}"), Formats));
        Assert.Equal(FormatShape.Unknown, FormatTypes.Shape(null, Formats));

        // A nullable type reports the type's shape; IsNullable says the rest.
        var nullableJudgement = J("""{ "anyOf": [ { "type": "null" }, { "$ref": "judgement" } ] }""");
        Assert.Equal(FormatShape.Object, FormatTypes.Shape(nullableJudgement, Formats));
        Assert.True(FormatTypes.IsNullable(nullableJudgement, Formats));
        Assert.Equal(FormatShape.Integer, FormatTypes.Shape(J("""{ "type": ["null", "integer"] }"""), Formats));
        Assert.True(FormatTypes.IsNullable(J("""{ "type": ["null", "integer"] }"""), Formats));
        Assert.False(FormatTypes.IsNullable(J("\"judgement\""), Formats));
        Assert.False(FormatTypes.IsNullable(J("""{ "anyOf": [ { "$ref": "plaintext" }, { "$ref": "judgement" } ] }"""), Formats));

        // The text kind is read from the root's annotations, through names.
        Assert.Equal("plaintext", FormatTypes.TextKind(J("\"plaintext\""), Formats));
        Assert.Equal("markdown_block", FormatTypes.TextKind(J("""{ "$ref": "markdown_block", "description": "d" }"""), Formats));
        Assert.Equal("markdown_inline", FormatTypes.TextKind(Registry.BuiltInFormats["markdown_inline"], Formats));
        Assert.Null(FormatTypes.TextKind(J("\"string\""), Formats));
        Assert.Null(FormatTypes.TextKind(J("""{ "type": "array", "items": { "$ref": "plaintext" } }"""), Formats));
        Assert.Null(FormatTypes.TextKind(J("""{ "type": "string", "contentMediaType": "text/markdown" }"""), Formats));
    }

    [Fact]
    public void ALoop_DoesNotHangTheTypeServices()
    {
        var set = FormatSet.ForPackage(Registry,
        [
            Format("a", """{ "type": "array", "items": { "$ref": "b" } }"""),
            Format("b", """{ "type": "array", "items": { "anyOf": [ { "$ref": "a" }, { "type": "null" } ] } }"""),
            Format("c", """{ "$ref": "d" }"""),
            Format("d", """{ "$ref": "c" }"""),
            Format("e", """{ "type": "array", "items": { "$ref": "#/$defs/x" }, "$defs": { "x": { "type": "array", "items": { "$ref": "#/$defs/x" } } } }"""),
        ]);
        Assert.True(set.IsCyclic("a"));

        var formats = new[] { "a", "b", "c", "d", "e" }
            .SelectMany(n => new[] { J($"\"{n}\""), J($$"""{ "$ref": "{{n}}" }"""), J($$"""{ "type": "array", "items": { "$ref": "{{n}}" } }""") })
            .ToList();
        foreach (var format in formats)
        {
            Assert.NotNull(FormatTypes.TypeForm(format, set));
            Assert.NotNull(FormatTypes.FullForm(format, set));
            Assert.StartsWith("sha256:", FormatTypes.TypeHash(format, set));
            Assert.StartsWith("sha256:", FormatTypes.FormatHash(format, set));
            Assert.NotEmpty(TypeStrings.Print(format, set));
            _ = FormatTypes.Shape(format, set);
            _ = FormatTypes.IsNullable(format, set);
            _ = FormatTypes.TextKind(format, set);
            foreach (var other in formats)
            {
                _ = FormatTypes.Fits(format, other, set);
                _ = FormatTypes.Same(format, other, set);
            }
        }

        Assert.Equal("list<b>", TypeStrings.Print(set.Find("a")!.Schema, set));
        Assert.Equal(FormatShape.Unknown, FormatTypes.Shape(J("\"c\""), set));
        Assert.Equal("list<list<?>>", TypeStrings.Print(set.Find("e")!.Schema, set));
    }

    [Fact]
    public void AWideChainOfReferences_StaysBounded()
    {
        // f0 refers to f1 twice, f1 to f2 twice, and so on: inlining every reference would write 2^40 copies.
        var chain = Enumerable.Range(0, 40)
            .Select(i => Format($"f{i}", i == 39
                ? """{ "type": "object", "properties": {}, "required": [], "additionalProperties": false }"""
                : $$"""{ "type": "object", "properties": { "l": { "$ref": "f{{i + 1}}" }, "r": { "$ref": "f{{i + 1}}" } }, "required": ["l", "r"], "additionalProperties": false }"""))
            .ToList();
        var set = FormatSet.ForPackage(Registry, chain);

        Assert.StartsWith("sha256:", FormatTypes.FormatHash(J("\"f0\""), set));
        Assert.True(FormatTypes.Fits(J("\"f0\""), J("\"f0\""), set));
        Assert.Equal("{ l: f1, r: f1 }", TypeStrings.Print(set.Find("f0")!.Schema, set));
    }

    [Fact]
    public void Garbage_NeverThrows()
    {
        var deep = "{ \"type\": \"string\" }";
        for (var i = 0; i < 60; i++)
        {
            deep = $$"""{ "type": "array", "items": {{deep}} }""";
        }

        var garbage = new[]
        {
            "null", "5", "true", "[1, 2]", "\"\"", "\"#/$defs/x\"", "{}",
            """{ "properties": null, "required": [1], "type": [] }""",
            """{ "type": "object", "properties": { "a": null, "b": 5, "c": [] }, "required": "a" }""",
            """{ "enum": [{}], "oneOf": 3, "anyOf": [null, 1, "plaintext"], "items": [] }""",
            """{ "$ref": 5, "title": [1], "description": {} }""",
            """{ "$ref": "#/$defs/x", "$defs": 7 }""",
            """{ "$ref": "plaintext", "type": "number", "description": 3 }""",
            """{ "type": ["string", "null", "number"], "enum": "x" }""",
            """{ "type": ["null", "null"] }""",
            """{ "anyOf": [] }""",
            deep,
        }.Select(t => JsonNode.Parse(t, documentOptions: new() { MaxDepth = 200 })).ToList();

        foreach (var format in garbage)
        {
            var copy = format?.ToJsonString();
            _ = FormatTypes.TypeForm(format, Formats);
            _ = FormatTypes.FullForm(format, Formats);
            _ = FormatTypes.TypeHash(format, Formats);
            _ = FormatTypes.FormatHash(format, Formats);
            _ = FormatTypes.Shape(format, Formats);
            _ = FormatTypes.IsNullable(format, Formats);
            _ = FormatTypes.TextKind(format, Formats);
            _ = TypeStrings.Print(format, Formats);
            foreach (var other in garbage)
            {
                _ = FormatTypes.Same(format, other, Formats);
                _ = FormatTypes.Fits(format, other, Formats);
            }

            Assert.Equal(copy, format?.ToJsonString());
        }

        // Not a format at all: the node as written, and the same only as itself.
        Assert.Equal("[1,2]", CanonicalJson.Write(FormatTypes.TypeForm(J("[1, 2]"), Formats)));
        Assert.Equal("[1,2]", CanonicalJson.Write(FormatTypes.FullForm(J("[1, 2]"), Formats)));
        Assert.Null(FormatTypes.TypeForm(null, Formats));
        Assert.True(FormatTypes.Same(J("[1, 2]"), J("[1, 2]"), Formats));
        Assert.False(FormatTypes.Same(J("[1, 2]"), J("[2, 1]"), Formats));
        Assert.False(FormatTypes.Fits(J("[1, 2]"), J("\"number\""), Formats));
        Assert.Equal(FormatShape.Unknown, FormatTypes.Shape(J("[1, 2]"), Formats));
        Assert.Throws<ArgumentNullException>(() => FormatTypes.TypeForm(J("\"date\""), null!));
    }
}
