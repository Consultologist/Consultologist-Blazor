using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="FormatDialect"/>'s keyword and structure rules
/// (part 02 § 3.1–3.3, § 5.3), each at every depth, each refusal its row's exact sentence.
/// </summary>
public class FormatDialectTests
{
    private const string Verdict = "Format 'verdict'";

    /// <summary>An object of one property <c>a</c> whose schema is <paramref name="field"/>.</summary>
    private static string Field(string field) =>
        $$"""{ "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": {{field}} } }""";

    /// <summary>
    /// <paramref name="json"/> checked as <paramref name="site"/>; a format file without <c>$schema</c>
    /// is given the dialect's first, so these tests see the keyword rules and not the root's.
    /// </summary>
    private static IReadOnlyList<SourceRefusal> Check(string json, SchemaSite site = SchemaSite.PackageFormat)
    {
        var schema = JsonNode.Parse(json);
        if (site != SchemaSite.Inline && schema is JsonObject root && !root.ContainsKey("$schema"))
            root.Insert(0, "$schema", "urn:consultologist:spec:31:dialect");
        return FormatDialect.Check(schema, site, Verdict, "verdict", "formats/verdict.json");
    }

    private static void AssertOne(IReadOnlyList<SourceRefusal> refusals, string id, string sentence)
    {
        var refusal = Assert.Single(refusals);
        Assert.Equal(id, refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
        Assert.Equal("formats/verdict.json", refusal.Path);
    }

    /// <summary>Each schema breaks one rule once: the schema, the id and the sentence it gets.</summary>
    public static TheoryData<string, string, string> OneRuleBroken => new()
    {
        // A schema that is not a JSON object: the member that holds it.
        { Field("\"integer\""), "A03-003", $"{Verdict} at /properties/a writes 'a' as text; it is an object." },
        { """{ "type": "array", "items": [] }""", "A03-003", $"{Verdict} at /items writes 'items' as a list; it is an object." },
        { Field("""{ "anyOf": [5, { "type": "null" }] }"""), "A03-003", $"{Verdict} at /properties/a/anyOf/0 writes 'anyOf' as a number; it is an object." },

        // A keyword outside the dialect, and the root's keywords below the root (the annotations are FormatDialectRootTests').
        { Field("""{ "type": "string", "minLength": 1 }"""), "S02-021", $"{Verdict} uses keyword 'minLength' at /properties/a, which is outside the formats dialect." },
        { Field("""{ "type": "string", "$schema": "urn:consultologist:spec:31:dialect" }"""), "S02-021", $"{Verdict} uses keyword '$schema' at /properties/a, which is outside the formats dialect." },
        { Field("""{ "type": "string", "$id": "urn:x" }"""), "S02-021", $"{Verdict} uses keyword '$id' at /properties/a, which is outside the formats dialect." },
        { Field("""{ "type": "string", "$defs": {} }"""), "S02-021", $"{Verdict} uses keyword '$defs' at /properties/a, which is outside the formats dialect." },
        { Field("""{ "type": "string", "contentMediaType": "text/plain" }"""), "S02-037", $"{Verdict} declares contentMediaType at /properties/a, where it doesn't apply." },
        { Field("""{ "type": "string", "x-markdown": "block" }"""), "S02-037", $"{Verdict} declares x-markdown at /properties/a, where it doesn't apply." },
        { Field("""{ "type": "string", "const": "x" }"""), "S02-021", $"{Verdict} uses keyword 'const' at /properties/a, which is outside the formats dialect." },
        { """{ "type": "array", "items": { "type": "string" }, "pattern": "x" }""", "S02-021", $"{Verdict} uses keyword 'pattern' at the root, which is outside the formats dialect." },

        // A keyword whose value has the wrong JSON type.
        { """{ "type": 5 }""", "A03-003", $"{Verdict} at the root writes 'type' as a number; it is text or a list." },
        { """{ "type": "object", "properties": null, "required": [], "additionalProperties": false }""", "A03-003", $"{Verdict} at the root writes 'properties' as null; it is an object." },
        { """{ "type": "object", "properties": {}, "required": [1], "additionalProperties": false }""", "A03-003", $"{Verdict} at the root writes 'required' as a list; it is a list of text." },
        { Field("""{ "type": "string", "enum": "a" }"""), "A03-003", $"{Verdict} at /properties/a writes 'enum' as text; it is a list." },
        { Field("""{ "type": "string", "oneOf": {} }"""), "A03-003", $"{Verdict} at /properties/a writes 'oneOf' as an object; it is a list." },
        { Field("""{ "anyOf": true }"""), "A03-003", $"{Verdict} at /properties/a writes 'anyOf' as true or false; it is a list." },
        { Field("""{ "type": "string", "title": 5 }"""), "A03-003", $"{Verdict} at /properties/a writes 'title' as a number; it is text." },
        { Field("""{ "type": "string", "description": ["x"] }"""), "A03-003", $"{Verdict} at /properties/a writes 'description' as a list; it is text." },
        { Field("""{ "$ref": 5 }"""), "A03-003", $"{Verdict} at /properties/a writes '$ref' as a number; it is text." },
        { Field("""{ "type": "string", "format": true }"""), "A03-003", $"{Verdict} at /properties/a writes 'format' as true or false; it is text." },

        // type: not one of the seven, or not one type plus "null".
        { """{ "type": "strin" }""", "S02-029", $"{Verdict} at the root declares type \"strin\"; a type array is one type plus \"null\"." },
        { Field("""{ "type": ["string", "number"] }"""), "S02-029", $"{Verdict} at /properties/a declares type [\"string\",\"number\"]; a type array is one type plus \"null\"." },
        { Field("""{ "type": ["null", "null"] }"""), "S02-029", $"{Verdict} at /properties/a declares type [\"null\",\"null\"]; a type array is one type plus \"null\"." },
        { Field("""{ "type": ["string", "null", "number"] }"""), "S02-029", $"{Verdict} at /properties/a declares type [\"string\",\"null\",\"number\"]; a type array is one type plus \"null\"." },
        { Field("""{ "type": [] }"""), "S02-029", $"{Verdict} at /properties/a declares type []; a type array is one type plus \"null\"." },
        { Field("""{ "type": ["string", 5] }"""), "S02-029", $"{Verdict} at /properties/a declares type [\"string\",5]; a type array is one type plus \"null\"." },

        // No type, $ref or anyOf.
        { Field("""{ "description": "Anything." }"""), "A03-005", $"{Verdict} at /properties/a declares no type; write type, $ref or anyOf." },

        // $ref beside anything but title and description.
        { Field("""{ "$ref": "plaintext", "type": "string" }"""), "S02-031", $"{Verdict} at /properties/a has 'type' beside $ref; only title and description may sit beside a $ref." },

        // $ref that is neither a name nor #/$defs/<name>, or names no definition.
        { Field("""{ "$ref": "#/$defs/row" }"""), "A03-002", $"{Verdict} at /properties/a refers to #/$defs/row, which the file doesn't define." },
        { Field("""{ "$ref": "https://example.com/x" }"""), "A03-002", $"{Verdict} at /properties/a refers to #/$defs/https://example.com/x, which the file doesn't define." },
        { Field("""{ "$ref": "Plaintext" }"""), "A03-002", $"{Verdict} at /properties/a refers to #/$defs/Plaintext, which the file doesn't define." },
        { """{ "type": "array", "items": { "$ref": "#/$defs/a/b" }, "$defs": { "a": { "type": "string" } } }""", "A03-002", $"{Verdict} at /items refers to #/$defs/#/$defs/a/b, which the file doesn't define." },

        // Keywords where the type is not theirs.
        { Field("""{ "type": "string", "properties": {} }"""), "A03-004", $"{Verdict} at /properties/a uses 'properties', which belongs on an object." },
        { Field("""{ "type": "string", "required": [] }"""), "A03-004", $"{Verdict} at /properties/a uses 'required', which belongs on an object." },
        { Field("""{ "type": "string", "additionalProperties": false }"""), "A03-004", $"{Verdict} at /properties/a uses 'additionalProperties', which belongs on an object." },
        { Field("""{ "type": "string", "items": { "type": "string" } }"""), "A03-004", $"{Verdict} at /properties/a uses 'items', which belongs on a list." },
        { Field("""{ "type": "number", "enum": ["a", "b"] }"""), "A03-004", $"{Verdict} at /properties/a uses 'enum', which belongs on a string." },
        { Field("""{ "type": ["number", "null"], "oneOf": [] }"""), "A03-004", $"{Verdict} at /properties/a uses 'oneOf', which belongs on a string." },

        // An object without one of its three keywords.
        { """{ "type": "object", "required": [], "additionalProperties": false }""", "A03-006", $"{Verdict} at the root is an object without 'properties'; every object writes properties, required and additionalProperties: false." },
        { """{ "type": "object", "properties": {}, "additionalProperties": false }""", "A03-006", $"{Verdict} at the root is an object without 'required'; every object writes properties, required and additionalProperties: false." },
        { Field("""{ "type": ["object", "null"], "properties": {}, "required": [] }"""), "A03-006", $"{Verdict} at /properties/a is an object without 'additionalProperties'; every object writes properties, required and additionalProperties: false." },

        // additionalProperties other than false.
        { """{ "type": "object", "properties": {}, "required": [], "additionalProperties": true }""", "S02-024", $"{Verdict} at the root declares additionalProperties true; every object declares additionalProperties: false." },
        { """{ "type": "object", "properties": {}, "required": [], "additionalProperties": { "type": "string" } }""", "S02-024", $"{Verdict} at the root declares additionalProperties {{\"type\":\"string\"}}; every object declares additionalProperties: false." },

        // required and properties disagree.
        { """{ "type": "object", "required": ["a", "b"], "additionalProperties": false, "properties": { "a": { "type": "string" } } }""", "S02-025", $"{Verdict} at the root lists 'b' in required but declares no such property." },
        {
            """{ "type": "object", "required": ["title"], "additionalProperties": false, "properties": { "title": { "type": "string" }, "notes": { "type": ["string","null"] } } }""",
            "S02-026", $"{Verdict} at the root leaves 'notes' out of required; every property is listed in required, and an optional one is written as nullable."
        },

        // A property name that is not an id (pattern and length only).
        {
            """{ "type": "object", "required": ["Notes"], "additionalProperties": false, "properties": { "Notes": { "type": "string" } } }""",
            "S01-030", "'Notes' is not a valid id: a lowercase letter, then lowercase letters, digits or underscores."
        },
        {
            Field("""{ "type": "string" }""").Replace("\"a\"", "\"" + new string('a', 65) + "\""),
            "S01-031", $"Id '{new string('a', 65)}' is 65 characters; the most is SourceBounds.idLength (64)."
        },

        // An array without items.
        { """{ "type": "array" }""", "A03-007", $"{Verdict} at the root is a list without items; write the one schema its items follow." },
        { Field("""{ "type": ["null", "array"] }"""), "A03-007", $"{Verdict} at /properties/a is a list without items; write the one schema its items follow." },

        // format other than date, or beside a type other than string.
        { Field("""{ "type": "string", "format": "date-time" }"""), "S02-030", $"{Verdict} at /properties/a uses format 'date-time'; the only format is date, on a string." },
        { Field("""{ "type": "number", "format": "date" }"""), "S02-030", $"{Verdict} at /properties/a uses format 'date'; the only format is date, on a string." },
        { Field("""{ "type": ["string", "null"], "format": "date" }"""), "S02-030", $"{Verdict} at /properties/a uses format 'date'; the only format is date, on a string." },

        // anyOf with fewer than two members.
        { Field("""{ "anyOf": [{ "$ref": "plaintext" }] }"""), "A03-008", $"{Verdict} at /properties/a writes anyOf with 1 member; a union has two or more." },
        { Field("""{ "anyOf": [] }"""), "A03-008", $"{Verdict} at /properties/a writes anyOf with 0 member; a union has two or more." },

        // A nullable scalar written with anyOf.
        { Field("""{ "anyOf": [{ "type": "integer" }, { "type": "null" }] }"""), "S02-028", $"{Verdict} at /properties/a writes a nullable integer with anyOf; write it as \"type\": [\"integer\", \"null\"]." },
        { Field("""{ "anyOf": [{ "type": "null" }, { "type": "string", "description": "A code." }] }"""), "S02-028", $"{Verdict} at /properties/a writes a nullable string with anyOf; write it as \"type\": [\"string\", \"null\"]." },
    };

    [Theory]
    [MemberData(nameof(OneRuleBroken))]
    public void EachRule_IsRefused_WithItsSentence(string schema, string id, string sentence) =>
        AssertOne(Check(schema), id, sentence);

    [Fact]
    public void AnObjectWithoutAnyOfItsKeywords_IsRefusedOncePerKeyword()
    {
        var refusals = Check("""{ "type": "object" }""");

        Assert.Equal(["A03-006", "A03-006", "A03-006"], refusals.Select(r => r.Id));
        Assert.Equal(
            ["'properties'", "'required'", "'additionalProperties'"],
            refusals.Select(r => r.Sentence.Split(' ')[9].TrimEnd(';')));
    }

    [Fact]
    public void EveryKeywordBesideARef_IsRefusedOnce()
    {
        var refusals = Check(Field("""{ "$ref": "plaintext", "title": "Notes", "type": "string", "description": "x", "items": {} }"""));

        Assert.Equal(
            [
                $"{Verdict} at /properties/a has 'type' beside $ref; only title and description may sit beside a $ref.",
                $"{Verdict} at /properties/a has 'items' beside $ref; only title and description may sit beside a $ref.",
            ],
            refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void AMissingRequiredEntry_IsRefused() =>
        AssertOne(Check("""
            { "type": "object", "required": ["title"], "additionalProperties": false,
              "properties": { "title": { "type": "string" }, "notes": { "type": ["string","null"] } } }
            """),
            "S02-026", "Format 'verdict' at the root leaves 'notes' out of required; every property is listed in required, and an optional one is written as nullable.");

    [Fact]
    public void ANullableScalarWrittenWithAnyOf_IsRefused() =>
        AssertOne(Check(Field("""{ "anyOf": [{ "type": "integer" }, { "type": "null" }] }""").Replace("\"a\"", "\"age\"")),
            "S02-028", "Format 'verdict' at /properties/age writes a nullable integer with anyOf; write it as \"type\": [\"integer\", \"null\"].");

    [Theory]
    [InlineData("""{ "anyOf": [{ "$ref": "plaintext" }, { "type": "null" }] }""")]
    [InlineData("""{ "anyOf": [{ "type": "null" }, { "$ref": "plaintext", "description": "Notes." }] }""")]
    [InlineData("""{ "anyOf": [{ "type": "string", "format": "date" }, { "type": "null" }] }""")]
    [InlineData("""{ "anyOf": [{ "type": "string" }, { "type": "number" }] }""")]
    public void ANullableReference_IsAccepted(string field) =>
        Assert.Empty(Check(Field(field)));

    [Theory]
    [InlineData("""["null", "string"]""")]
    [InlineData("""["string", "null"]""")]
    [InlineData("""["integer", "null"]""")]
    [InlineData("""["null", "boolean"]""")]
    public void ATypeArrayInEitherOrder_IsAccepted(string type) =>
        Assert.Empty(Check(Field($$"""{ "type": {{type}} }""")));

    [Fact]
    public void OrdinaryJsonSchemaKeywords_AreEachRefused()
    {
        // Review Focus 1: a format pasted from ordinary JSON Schema; each keyword once, the rest still judged.
        var refusals = Check("""
            {
              "$comment": "Pasted.",
              "type": "object",
              "required": ["title", "score", "notes", "tags"],
              "additionalProperties": false,
              "definitions": {},
              "properties": {
                "title": { "type": "string", "minLength": 1, "pattern": "^[A-Z]" },
                "score": { "type": "number", "default": 0, "examples": [1, 2] },
                "notes": { "type": "string", "nullable": true },
                "tags": { "type": "array", "items": { "type": "string", "allOf": [{ "type": "string" }] } }
              }
            }
            """);

        Assert.All(refusals, r => Assert.Equal("S02-021", r.Id));
        Assert.Equal(
            [
                ("$comment", "the root"),
                ("definitions", "the root"),
                ("minLength", "/properties/title"),
                ("pattern", "/properties/title"),
                ("default", "/properties/score"),
                ("examples", "/properties/score"),
                ("nullable", "/properties/notes"),
                ("allOf", "/properties/tags/items"),
            ],
            refusals.Select(r => KeywordAndPath(r.Sentence)));

        // The rest is still judged: the items schema with only allOf has no type.
        var withAnError = Check("""{ "type": "array", "items": { "allOf": [] }, "minItems": 1 }""");
        Assert.Equal(["A03-005", "S02-021", "S02-021"], withAnError.Select(r => r.Id).Order(StringComparer.Ordinal));
    }

    private static (string Keyword, string Path) KeywordAndPath(string sentence)
    {
        var match = System.Text.RegularExpressions.Regex.Match(sentence, "uses keyword '(.+?)' at (.+?), which");
        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    [Fact]
    public void ADefinition_IsResolvedAndWalked()
    {
        const string format = """
            {
              "type": "array",
              "items": { "$ref": "#/$defs/sectionRow" },
              "$defs": {
                "sectionRow": { "type": "object", "required": ["title"], "additionalProperties": false, "properties": { "title": { "type": "strin" } } }
              }
            }
            """;

        AssertOne(Check(format), "S02-029", $"{Verdict} at /$defs/sectionRow/properties/title declares type \"strin\"; a type array is one type plus \"null\".");
    }

    [Theory]
    [InlineData(SchemaSite.RegistryFormat, """{ "type": "string", "contentMediaType": "text/plain", "items": { "type": "string" } }""", "items", "a list")]
    [InlineData(SchemaSite.RegistryFormat, """{ "type": "array", "items": { "type": "string" }, "required": [] }""", "required", "an object")]
    [InlineData(SchemaSite.Inline, """{ "type": "string", "required": [] }""", "required", "an object")]
    [InlineData(SchemaSite.Inline, """{ "type": "number", "enum": ["a"] }""", "enum", "a string")]
    public void AKeywordOnTheWrongType_IsRefusedAtTheRoot(SchemaSite site, string schema, string keyword, string belongs) =>
        AssertOne(Check(schema, site), "A03-004", $"{Verdict} at the root uses '{keyword}', which belongs on {belongs}.");

    [Fact]
    public void AnUndefinedDefinition_IsRefusedInARegistryFormat()
    {
        AssertOne(Check(Field("""{ "$ref": "#/$defs/row" }"""), SchemaSite.RegistryFormat),
            "A03-002", $"{Verdict} at /properties/a refers to #/$defs/row, which the file doesn't define.");
        Assert.Empty(Check("""{ "type": "array", "items": { "$ref": "#/$defs/row" }, "$defs": { "row": { "type": "string" } } }""", SchemaSite.RegistryFormat));
    }

    [Fact]
    public void AnInlineSchema_HasNoDefinitions()
    {
        const string format = """{ "type": "array", "items": { "$ref": "#/$defs/row" }, "$defs": { "row": { "type": "string" } } }""";

        Assert.Empty(Check(format));
        Assert.Equal(["A03-002", "S02-021"], Check(format, SchemaSite.Inline).Select(r => r.Id));
    }

    [Fact]
    public void ADefinitionThatReachesItself_IsRefused()
    {
        const string format = """
            {
              "type": "array",
              "items": { "$ref": "#/$defs/a" },
              "$defs": {
                "a": { "type": "object", "required": ["b"], "additionalProperties": false, "properties": { "b": { "anyOf": [{ "$ref": "#/$defs/b" }, { "type": "null" }] } } },
                "b": { "type": "array", "items": { "$ref": "#/$defs/a" } },
                "c": { "type": "array", "items": { "$ref": "#/$defs/c" } },
                "d": { "$ref": "#/$defs/a" }
              }
            }
            """;

        var refusals = Check(format);

        Assert.All(refusals, r => Assert.Equal("S02-032", r.Id));
        Assert.Equal(
            [
                $"{Verdict} at /$defs/a refers to itself; recursive formats aren't supported.",
                $"{Verdict} at /$defs/b refers to itself; recursive formats aren't supported.",
                $"{Verdict} at /$defs/c refers to itself; recursive formats aren't supported.",
            ],
            refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void FieldNames_HaveNoReservedWords_AndPointersAreEscaped()
    {
        Assert.Empty(Check("""
            { "type": "object", "required": ["title", "value", "items", "con"], "additionalProperties": false,
              "properties": { "title": { "type": "string" }, "value": { "type": "number" }, "items": { "type": "boolean" }, "con": { "type": "string" } } }
            """));

        var refusals = Check("""
            { "type": "object", "required": ["a/b~c"], "additionalProperties": false, "properties": { "a/b~c": { "type": "strin" } } }
            """);

        Assert.Equal(["S01-030", "S02-029"], refusals.Select(r => r.Id));
        Assert.Equal($"{Verdict} at /properties/a~1b~0c declares type \"strin\"; a type array is one type plus \"null\".", refusals[1].Sentence);
    }

    [Fact]
    public void RegistryFieldNames_NeedNotBeIds()
    {
        const string format = """
            { "type": "object", "required": ["isActive"], "additionalProperties": false, "properties": { "isActive": { "type": "boolean" } } }
            """;

        Assert.Empty(Check(format, SchemaSite.RegistryFormat));
        Assert.Equal(["S01-030"], Check(format, SchemaSite.Inline).Select(r => r.Id));
    }

    [Fact]
    public void EveryRefusal_IsInDocumentOrder()
    {
        var refusals = Check("""
            { "type": "object", "required": ["a", "b"], "additionalProperties": false,
              "properties": { "a": { "type": "strin" }, "b": { "type": "array" } }, "maxProperties": 3 }
            """);

        Assert.Equal(["S02-029", "A03-007", "S02-021"], refusals.Select(r => r.Id));
    }

    [Fact]
    public void ASchemaThatIsNotAnObject_IsRefused_AndANameIsLeftToTheLookup()
    {
        Assert.Empty(FormatDialect.Check(JsonValue.Create("plaintext"), SchemaSite.Inline, "Input 'notes'", "notes", "manifest.json"));

        var refusal = Assert.Single(FormatDialect.Check(new JsonArray(), SchemaSite.Inline, "Input 'notes'", "notes", "manifest.json"));
        Assert.Equal("A03-003", refusal.Id);
        Assert.Equal("Input 'notes' at the root writes 'format' as a list; it is an object.", refusal.Sentence);
        Assert.Equal("manifest.json", refusal.Path);
        Assert.Single(FormatDialect.Check(null, SchemaSite.Inline, "Input 'notes'", "notes", "manifest.json"));
    }

    [Fact]
    public void ASchemaNestedPastTheJsonDepth_IsRefusedOnce_AndNeverOverflows()
    {
        var schema = new JsonObject { ["type"] = "string" };
        for (var i = 0; i < 40; i++)
        {
            schema = new JsonObject { ["type"] = "array", ["items"] = schema };
        }

        var refusal = Assert.Single(FormatDialect.Check(schema, SchemaSite.Inline, "Input 'notes'", "notes", "manifest.json"));
        Assert.Equal("S01-041", refusal.Id);

        var deep = new JsonObject { ["type"] = "string" };
        for (var i = 0; i < 10_000; i++)
        {
            deep = new JsonObject { ["anyOf"] = new JsonArray(deep, new JsonObject { ["type"] = "null" }) };
        }

        Assert.Single(FormatDialect.Check(deep, SchemaSite.Inline, "Input 'notes'", "notes", "manifest.json"), r => r.Id == "S01-041");
    }

    [Fact]
    public void ADefinitionLoopPastTheJsonDepth_NeverOverflows()
    {
        var defs = new JsonObject();
        for (var i = 0; i < 5_000; i++)
        {
            defs[$"d{i}"] = new JsonObject { ["$ref"] = $"#/$defs/d{i + 1}" };
        }

        defs["d5000"] = new JsonObject { ["$ref"] = "#/$defs/d0" };
        var schema = new JsonObject { ["$ref"] = "#/$defs/d0", ["$defs"] = defs };

        var refusals = FormatDialect.Check(schema, SchemaSite.PackageFormat, Verdict, "verdict", "formats/verdict.json");

        Assert.Equal(5_001, refusals.Count(r => r.Id == "S02-032"));
    }

    [Fact]
    public void EveryRefusal_MatchesItsRow()
    {
        var corpus = OneRuleBroken.Select(row => (string)row[0]).Concat(
        [
            """{ "type": "object" }""",
            Field("""{ "$ref": "plaintext", "title": "Notes", "type": "string", "items": {} }"""),
            """{ "type": "array", "items": { "$ref": "#/$defs/c" }, "$defs": { "c": { "type": "array", "items": { "$ref": "#/$defs/c" } } } }""",
        ]);

        var refusals = corpus.SelectMany(json => Check(json)).ToList();
        refusals.AddRange(FormatDialect.Check(new JsonArray(), SchemaSite.Inline, "Input 'notes'", "notes", "manifest.json"));

        Assert.NotEmpty(refusals);
        Assert.All(refusals, V31Contract.AssertSentence);
    }
}
