using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="SourceTypes.Check"/> on the manifest's inputs
/// (part 02 § 8, D17–D19): which formats an input may take, <c>channel</c>, and a title on every
/// field of an object input. An input is judged only when its format passed the dialect and the lookup.
/// </summary>
public class SourceTypesInputsTests
{
    /// <summary>Minimal, with an input <paramref name="id"/> of <paramref name="format"/> (JSON) and <paramref name="channel"/> added after its own.</summary>
    private static Dictionary<string, byte[]> WithInput(string id, string format, string? channel = null, Dictionary<string, byte[]>? files = null) =>
        (files ?? SourceFixture.Minimal()).WithJson("manifest.json", m => m["inputs"]![id] = new JsonObject
        {
            ["label"] = "Notes",
            ["description"] = null,
            ["format"] = JsonNode.Parse(format),
            ["required"] = false,
            ["channel"] = channel,
        });

    private static IReadOnlyList<SourceRefusal> Refusals(Dictionary<string, byte[]> files)
    {
        var result = TypeFixture.CheckTypes(files);
        Assert.Equal(result.Refusals.Count == 0, result.Types != null);
        Assert.All(result.Refusals, V31Contract.AssertSentence);
        Assert.All(result.Refusals, r => Assert.Equal("manifest.json", r.Path));
        return result.Refusals;
    }

    private static SourceRefusal Only(Dictionary<string, byte[]> files)
    {
        var refusals = Refusals(files);
        Assert.True(refusals.Count == 1, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Sentence}")));
        return refusals[0];
    }

    private const string TitledObject =
        """{ "type": "object", "properties": { "name": { "type": "string", "title": "Name" } }, "required": ["name"], "additionalProperties": false }""";

    [Fact]
    public void Part02s_ExampleInputs_AreAccepted()
    {
        // Part 02 § 8.4, as written.
        const string inputs = """
            {
              "consult_draft": {
                "label": "Consult draft",
                "description": null,
                "format": "markdown_block",
                "required": true,
                "channel": null
              },
              "sections": {
                "label": "Sections to produce",
                "description": null,
                "format": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["title", "kind", "notes"],
                    "properties": {
                      "title": { "type": "string", "title": "Title" },
                      "kind": {
                        "type": "string",
                        "title": "Kind",
                        "oneOf": [
                          { "const": "narrative", "title": "Narrative" },
                          { "const": "table",     "title": "Table" },
                          { "const": "omit",      "title": "Leave out" }
                        ]
                      },
                      "notes": {
                        "anyOf": [ { "$ref": "markdown_block" }, { "type": "null" } ],
                        "title": "Notes for this section"
                      }
                    }
                  }
                },
                "required": true,
                "channel": null
              },
              "include_billing": {
                "label": "Include the billing section",
                "description": null,
                "format": "boolean",
                "required": false,
                "channel": null
              }
            }
            """;
        var files = SourceFixture.Minimal().WithJson("manifest.json", m => m["inputs"] = JsonNode.Parse(inputs));

        Assert.Empty(Refusals(files));
    }

    [Fact]
    public void AnInlineFormat_IsNamedByItsOwner()
    {
        var refusal = Only(WithInput("sections", """{ "type": "array", "minItems": 1, "items": { "$ref": "plaintext" } }"""));

        Assert.Equal("S02-021", refusal.Id);
        Assert.Equal("Input 'sections' uses keyword 'minItems' at the root, which is outside the formats dialect.", refusal.Sentence);
    }

    [Fact]
    public void AnInputWhoseFormatWasRefused_IsNotJudgedFurther()
    {
        var unknown = Only(WithInput("notes", "\"ghost\"", "form"));
        var outside = Only(WithInput("notes", """{ "type": "string", "minLength": 1 }""", "transcript"));

        Assert.Equal("S02-017", unknown.Id);
        Assert.Equal("S02-021", outside.Id);
    }

    [Theory]
    [InlineData("\"markdown_inline\"")]
    [InlineData("""{ "$ref": "markdown_inline" }""")]
    [InlineData("""{ "$ref": "markdown_inline", "title": "Notes" }""")]
    public void MarkdownInline_IsNoInputsFormat(string format)
    {
        var refusal = Only(WithInput("notes", format));

        Assert.Equal("S02-040", refusal.Id);
        Assert.Equal("Input 'notes' takes markdown_inline; use plaintext or markdown_block.", refusal.Sentence);
    }

    [Theory]
    [InlineData("""{ "anyOf": [{ "$ref": "plaintext" }, { "$ref": "number" }] }""")]
    [InlineData("""{ "anyOf": [{ "$ref": "markdown_block" }, { "type": "null" }] }""")]
    public void AChoiceOfShapes_IsNoInputsFormat(string format)
    {
        var refusal = Only(WithInput("notes", format));

        Assert.Equal("S02-041", refusal.Id);
        Assert.Equal("Input 'notes' takes a choice of shapes (anyOf); an input has one shape.", refusal.Sentence);
    }

    [Fact]
    public void AChannelOutsideTheFour_IsRefused()
    {
        var refusal = Only(WithInput("notes", "\"plaintext\"", "audio"));

        Assert.Equal("S02-042", refusal.Id);
        Assert.Equal("Input 'notes' declares channel 'audio' (accepted: transcript, image, ambient-note, form).", refusal.Sentence);
    }

    [Theory]
    [InlineData("\"plaintext\"", "transcript")]
    [InlineData("\"markdown_block\"", "image")]
    [InlineData("""{ "$ref": "markdown_block" }""", "ambient-note")]
    [InlineData("""{ "type": "array", "items": { "$ref": "plaintext" } }""", "transcript")]
    [InlineData("""{ "type": "array", "items": { "$ref": "markdown_block", "title": "Page" } }""", "image")]
    [InlineData(TitledObject, "form")]
    [InlineData("\"boolean\"", null)]
    public void AChannelOnTheInputItIsFor_IsAccepted(string format, string? channel)
    {
        Assert.Empty(Refusals(WithInput("notes", format, channel)));
    }

    [Theory]
    [InlineData("\"boolean\"", "transcript")]
    [InlineData("\"string\"", "image")]
    [InlineData("\"markdown_inline\"", "ambient-note")]
    [InlineData("""{ "type": "array", "items": { "$ref": "string" } }""", "transcript")]
    [InlineData(TitledObject, "image")]
    public void ATextChannel_IsOnlyForTextOrAListOfText(string format, string channel)
    {
        var refusal = Assert.Single(Refusals(WithInput("notes", format, channel)), r => r.Id == "S02-043");

        Assert.Equal($"Input 'notes' declares channel '{channel}', which is only for a text input or an array of text.", refusal.Sentence);
    }

    [Theory]
    [InlineData("\"markdown_block\"")]
    [InlineData("""{ "type": "object", "properties": {}, "required": [], "additionalProperties": false }""")]
    [InlineData("""{ "type": "array", "items": """ + TitledObject + " }")]
    public void TheFormChannel_NeedsAnObjectWithAProperty(string format)
    {
        var refusal = Only(WithInput("notes", format, "form"));

        Assert.Equal("S02-044", refusal.Id);
        Assert.Equal("Input 'notes' declares channel 'form', which needs an object with at least one property.", refusal.Sentence);
    }

    [Fact]
    public void TheFormChannel_TakesANamedObject()
    {
        var files = SourceFixture.Minimal()
            .With("formats/index.json", SourceFixture.Index(["referral"]))
            .With("formats/referral.json", TypeFixture.FormatFile(
                "\"type\": \"object\", \"properties\": { \"reason\": { \"$ref\": \"plaintext\", \"title\": \"Reason for referral\" } }, "
                + "\"required\": [\"reason\"], \"additionalProperties\": false"));

        Assert.Empty(Refusals(WithInput("referral", "\"referral\"", "form", files)));
        Assert.Empty(Refusals(WithInput("referral", """{ "$ref": "referral" }""", "form", files)));
    }

    [Fact]
    public void EveryFieldWithoutATitle_IsNamed_AtAnyDepth_ThroughNamesListsAndNullableUnions()
    {
        // section_plan: sections (a list) of objects with title ($ref plaintext) and kind (an enum); none has a title.
        var files = SourceFixture.Full();
        WithInput("plan_input", "\"section_plan\"", null, files);
        WithInput("plans", """
            { "type": "object", "required": ["main", "spare"], "additionalProperties": false, "properties": {
                "main": { "$ref": "section_plan", "title": "Main plan" },
                "spare": { "anyOf": [{ "$ref": "outline" }, { "type": "null" }] } } }
            """, null, files);

        var refusals = Refusals(files);

        Assert.All(refusals, r => Assert.Equal("S02-045", r.Id));
        Assert.Equal(
            [
                "Input 'plan_input' field 'sections' has no title; the Create page labels each field by its title.",
                "Input 'plan_input' field 'sections.title' has no title; the Create page labels each field by its title.",
                "Input 'plan_input' field 'sections.kind' has no title; the Create page labels each field by its title.",
                "Input 'plans' field 'main.sections' has no title; the Create page labels each field by its title.",
                "Input 'plans' field 'main.sections.title' has no title; the Create page labels each field by its title.",
                "Input 'plans' field 'main.sections.kind' has no title; the Create page labels each field by its title.",
                "Input 'plans' field 'spare' has no title; the Create page labels each field by its title.",
                "Input 'plans' field 'spare.points' has no title; the Create page labels each field by its title.",
            ],
            refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void AFieldUsedTwice_IsNamedOncePerPlace()
    {
        var files = WithInput("pair", """
            { "type": "object", "required": ["a", "b"], "additionalProperties": false, "properties": {
                "a": { "type": "object", "title": "A", "required": ["x"], "additionalProperties": false, "properties": { "x": { "type": "string" } } },
                "b": { "type": "array", "title": "B", "items": { "type": "object", "required": ["x"], "additionalProperties": false, "properties": { "x": { "type": "string" } } } } } }
            """);

        Assert.Equal(
            ["a.x", "b.x"],
            Refusals(files).Select(r => r.Sentence.Split("field '")[1].Split('\'')[0]));
    }

    [Fact]
    public void IndependentInputFaults_AreAllSaid_InWrittenOrder()
    {
        var files = WithInput("first", "\"markdown_inline\"", "audio");
        WithInput("second", "\"boolean\"", "transcript", files);

        Assert.Equal(["S02-040", "S02-042", "S02-043"], Refusals(files).Select(r => r.Id));
    }
}
