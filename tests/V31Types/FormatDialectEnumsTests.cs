using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="FormatDialect"/>'s enum rules (part 02 § 3.2, § 9):
/// the plain <c>enum</c> and the labelled <c>oneOf</c>, their values, titles and null, each refusal
/// its row's exact sentence, with D11's <c>&lt;Subject&gt;</c>.
/// </summary>
public class FormatDialectEnumsTests
{
    private const string At = "Format 'verdict' at /properties/a";

    /// <summary>A package format file of one property <c>a</c> whose schema is <paramref name="field"/>.</summary>
    private static string Field(string field) => TypeFixture.FormatFile(
        $$"""
        "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": {{field}} }
        """);

    private static IReadOnlyList<SourceRefusal> Check(string json, SchemaSite site = SchemaSite.PackageFormat) =>
        FormatDialect.Check(JsonNode.Parse(json), site, "Format 'verdict'", "verdict", "formats/verdict.json");

    /// <summary>Each field breaks one enum rule once: the field, the id and the sentence it gets.</summary>
    public static TheoryData<string, string, string> OneRuleBroken => new()
    {
        // Both enum and oneOf: neither is judged further (the one-value enum is not refused).
        {
            """{ "type": "string", "enum": ["a"], "oneOf": [{ "const": "a" }] }""",
            "S02-052", $"{At} declares both enum and oneOf; write one."
        },

        // An enum entry that is not text or null.
        { """{ "type": "string", "enum": ["a", 5] }""", "A03-003", $"{At} writes 'enum' as a list; it is a list of text." },
        { """{ "type": "string", "enum": [{}] }""", "A03-003", $"{At} writes 'enum' as a list; it is a list of text." },

        // A oneOf branch that is not exactly { const, title } or { const: null }.
        {
            """{ "type": "string", "oneOf": [{ "const": "a" }, { "const": "b", "title": "B" }, { "const": "c", "title": "C" }] }""",
            "S02-051", $$"""{{At}} writes oneOf branch 1 as {"const":"…"}; each branch is exactly { "const": <value>, "title": <label> }."""
        },
        {
            """{ "type": "string", "oneOf": [{ "const": "a", "title": "A" }, { "const": "b", "title": "" }, { "const": "c", "title": "C" }] }""",
            "S02-051", $$"""{{At}} writes oneOf branch 2 as {"const":"…","title":"…"}; each branch is exactly { "const": <value>, "title": <label> }."""
        },
        {
            """{ "type": "string", "oneOf": [{ "const": "a", "title": "A" }, { "const": "b", "title": "B" }, { "const": "c", "title": "C", "description": "x" }] }""",
            "S02-051", $$"""{{At}} writes oneOf branch 3 as {"const":"…","title":"…","description":"…"}; each branch is exactly { "const": <value>, "title": <label> }."""
        },
        {
            """{ "type": ["string", "null"], "oneOf": [{ "const": "a", "title": "A" }, { "const": "b", "title": "B" }, { "const": null, "title": "None" }] }""",
            "S02-051", $$"""{{At}} writes oneOf branch 3 as {"const":null,"title":"…"}; each branch is exactly { "const": <value>, "title": <label> }."""
        },
        {
            """{ "type": "string", "oneOf": [{ "const": "a", "title": "A" }, { "const": 5, "title": "B" }, { "const": "c", "title": "C" }] }""",
            "S02-051", $$"""{{At}} writes oneOf branch 2 as {"const":5,"title":"…"}; each branch is exactly { "const": <value>, "title": <label> }."""
        },
        {
            """{ "type": "string", "oneOf": [{ "const": "a", "title": "A" }, "b", { "const": "c", "title": "C" }] }""",
            "S02-051", $$"""{{At}} writes oneOf branch 2 as "…"; each branch is exactly { "const": <value>, "title": <label> }."""
        },

        // Fewer than two non-null values.
        { """{ "type": "string", "enum": ["only"] }""", "S02-047", $"{At} declares one value; an enum with one value is a constant, not a choice." },
        { """{ "type": "string", "enum": [] }""", "S02-047", $"{At} declares one value; an enum with one value is a constant, not a choice." },
        { """{ "type": ["string", "null"], "enum": ["only", null] }""", "S02-047", $"{At} declares one value; an enum with one value is a constant, not a choice." },
        {
            """{ "type": "string", "oneOf": [{ "const": "only", "title": "Only" }] }""",
            "S02-047", $"{At} declares one value; an enum with one value is a constant, not a choice."
        },

        // A value twice.
        { """{ "type": "string", "enum": ["a", "b", "a"] }""", "S02-048", $"{At} declares value 'a' twice." },
        {
            """{ "type": "string", "oneOf": [{ "const": "a", "title": "A" }, { "const": "a", "title": "B" }] }""",
            "S02-048", $"{At} declares value 'a' twice."
        },
        { """{ "type": ["string", "null"], "enum": ["a", "b", null, null] }""", "S02-048", $"{At} declares value 'null' twice." },

        // A value that is not snake_case.
        { """{ "type": "string", "enum": ["new_patient", "Follow-Up"] }""", "S02-049", $"{At} value 'Follow-Up' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores)." },
        {
            """{ "type": "string", "oneOf": [{ "const": "a", "title": "A" }, { "const": "2b", "title": "B" }] }""",
            "S02-049", $"{At} value '2b' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores)."
        },

        // Two branches with one title.
        {
            """{ "type": "string", "oneOf": [{ "const": "a", "title": "Same" }, { "const": "b", "title": "Same" }] }""",
            "S02-050", $"{At} gives two values the title 'Same'."
        },

        // Nullable, with no null value or arm.
        { """{ "type": ["string", "null"], "enum": ["a", "b"] }""", "S02-053", $"{At} is nullable but its enum doesn't list null; a nullable enum lists null among its values." },
        {
            """{ "type": ["null", "string"], "oneOf": [{ "const": "a", "title": "A" }, { "const": "b", "title": "B" }] }""",
            "S02-054", $"{At} is nullable but its oneOf has no {{ \"const\": null }} arm; a nullable labelled enum adds one."
        },

        // null, or a null arm, when the type is plain "string".
        { """{ "type": "string", "enum": ["a", "b", null] }""", "A03-010", $"{At} lists null but is not nullable; write \"type\": [\"string\", \"null\"], or remove null." },
        {
            """{ "type": "string", "oneOf": [{ "const": "a", "title": "A" }, { "const": "b", "title": "B" }, { "const": null }] }""",
            "A03-010", $"{At} lists null but is not nullable; write \"type\": [\"string\", \"null\"], or remove null."
        },
    };

    [Theory]
    [MemberData(nameof(OneRuleBroken))]
    public void EachRule_IsRefused_WithItsSentence(string field, string id, string sentence)
    {
        var refusal = Assert.Single(Check(Field(field)));
        Assert.Equal(id, refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
        Assert.Equal("formats/verdict.json", refusal.Path);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void ARepeatedValueOrTitle_IsRefusedOncePerValue()
    {
        var refusals = Check(Field("""
            { "type": "string", "oneOf": [
              { "const": "a", "title": "A" }, { "const": "b", "title": "A" }, { "const": "a", "title": "A" },
              { "const": "a", "title": "C" }, { "const": "c", "title": "C" } ] }
            """));

        Assert.Equal(
            [
                $"{At} declares value 'a' twice.",
                $"{At} gives two values the title 'A'.",
                $"{At} gives two values the title 'C'.",
            ],
            refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void ANullableEnum_ListsNull_AndCountsOnlyItsValues()
    {
        Assert.Empty(Check(Field("""{ "type": ["string", "null"], "enum": ["new_patient", "follow_up", null] }""")));

        var refusal = Assert.Single(Check(Field("""{ "type": ["string", "null"], "enum": ["new_patient", null] }""")));
        Assert.Equal("S02-047", refusal.Id);
    }

    [Fact]
    public void ANullableLabelledEnum_HasANullArm()
    {
        Assert.Empty(Check(Field("""
            { "type": ["string", "null"], "oneOf": [
              { "const": "new_patient", "title": "New patient" }, { "const": "follow_up", "title": "Follow-up" }, { "const": null } ] }
            """)));

        var refusal = Assert.Single(Check(Field("""
            { "type": ["string", "null"], "oneOf": [
              { "const": "new_patient", "title": "New patient" }, { "const": "follow_up", "title": "Follow-up" } ] }
            """)));
        Assert.Equal("S02-054", refusal.Id);
    }

    [Fact]
    public void RegistryEnumValues_NeedNotBeSnakeCase()
    {
        const string field = """{ "type": "string", "enum": ["new_patient", "Follow-Up"] }""";

        Assert.Empty(Check(Field(field), SchemaSite.RegistryFormat));
        Assert.Equal(["S02-049"], Check(Field(field)).Select(r => r.Id));
        Assert.Equal(
            ["S02-049"],
            FormatDialect.Check(JsonNode.Parse(field), SchemaSite.Inline, "Input 'visit'", "visit", "manifest.json").Select(r => r.Id));
    }

    [Fact]
    public void S02_051_NeverPrintsTheBranchsText()
    {
        var refusals = Check(Field("""
            { "type": "string", "oneOf": [{ "const": "a", "label": "Secret" }, { "const": "b", "title": "B" }, { "const": "c", "title": "C" }] }
            """));

        var refusal = Assert.Single(refusals);
        Assert.Equal("S02-051", refusal.Id);
        Assert.Contains("{\"const\":\"…\",\"label\":\"…\"}", refusal.Sentence);
        Assert.DoesNotContain("Secret", refusal.Sentence);
        Assert.DoesNotContain("\"a\"", refusal.Sentence);
    }

    [Fact]
    public void AMalformedBranch_LeavesTheCountAndNullRulesUnjudged()
    {
        // invalid-one-of-null-with-title: the null arm with a title is S02-051, not also S02-054.
        var refusals = Check(Field("""
            { "type": ["string", "null"], "oneOf": [{ "const": "a", "title": "A" }, { "const": null, "title": "None" }] }
            """));

        Assert.Equal(["S02-051"], refusals.Select(r => r.Id));
    }

    [Fact]
    public void AMisplacedEnum_IsNotJudgedFurther()
    {
        var refusals = Check(Field("""{ "type": "number", "enum": ["Only"] }"""));

        Assert.Equal(["A03-004"], refusals.Select(r => r.Id));
    }

    [Fact]
    public void EveryRefusal_MatchesItsRow()
    {
        var refusals = OneRuleBroken.SelectMany(row => Check(Field((string)row[0]))).ToList();

        Assert.NotEmpty(refusals);
        Assert.All(refusals, V31Contract.AssertSentence);
    }
}
