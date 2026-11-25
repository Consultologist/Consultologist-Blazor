using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 02 § 8, § 11; part 10 § 1.2's input rows, § 1.8): 30's inputs array becomes
/// 31's object keyed by id, one row of part 02 § 11 at a time. Each test adds 30 inputs to
/// <c>example-nested-note</c> (whose own input is <c>consult_draft</c>) and reads the 31 manifest's <c>inputs</c>.
/// </summary>
public class MigrateInputsTests
{
    private static string Compact(string json) => JsonNode.Parse(json)!.ToJsonString();

    private static Migration31Result MigrateWith(params string[] inputs)
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage("example-nested-note");
        var root = JsonNode.Parse(json)!.AsObject();
        foreach (var input in inputs)
            root["inputs"]!.AsArray().Add(JsonNode.Parse(input));

        var result = WorkflowPackageMigration30To31.Migrate(root.ToJsonString(), files, MigrationFixture.Options("example-nested-note"));
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        return result;
    }

    private static JsonObject InputsWith(params string[] inputs) =>
        JsonNode.Parse(MigrateWith(inputs).ManifestJson)!["inputs"]!.AsObject();

    private static void AssertInput(string expected, JsonNode? actual) =>
        Assert.Equal(Compact(expected), actual!.ToJsonString());

    [Fact]
    public void Text_BecomesMarkdownBlock()
    {
        var inputs = InputsWith("""{ "id": "history", "label": "History", "required": true, "type": "text" }""");

        AssertInput("""{ "label": "History", "description": null, "format": "markdown_block", "required": true, "channel": null }""", inputs["history"]);
    }

    [Fact]
    public void NoType_BecomesMarkdownBlock()
    {
        var inputs = InputsWith();

        AssertInput("""{ "label": "Consult draft", "description": null, "format": "markdown_block", "required": true, "channel": null }""", inputs["consult_draft"]);
    }

    [Fact]
    public void AnEnum_BecomesAPlainEnum_WithNoTitles()
    {
        var inputs = InputsWith("""{ "id": "visit", "label": "Visit", "required": false, "type": "enum", "values": ["new_patient", "follow_up"] }""");

        AssertInput("""
            { "label": "Visit", "description": null, "format": { "type": "string", "enum": ["new_patient", "follow_up"] },
              "required": false, "channel": null }
            """, inputs["visit"]);
    }

    [Fact]
    public void ADate_BecomesDate()
    {
        var inputs = InputsWith("""{ "id": "seen_on", "label": "Seen on", "required": false, "type": "date" }""");

        AssertInput("""{ "label": "Seen on", "description": null, "format": "date", "required": false, "channel": null }""", inputs["seen_on"]);
    }

    [Fact]
    public void ABooleanAndANumber_KeepTheirNames()
    {
        var inputs = InputsWith(
            """{ "id": "urgent", "label": "Urgent", "required": false, "type": "boolean" }""",
            """{ "id": "age", "label": "Age", "required": true, "type": "number" }""");

        AssertInput("""{ "label": "Urgent", "description": null, "format": "boolean", "required": false, "channel": null }""", inputs["urgent"]);
        AssertInput("""{ "label": "Age", "description": null, "format": "number", "required": true, "channel": null }""", inputs["age"]);
    }

    [Fact]
    public void AnObject_BecomesProperties_EveryFieldRequired_OptionalOnesNullable_TitledByLabel()
    {
        var inputs = InputsWith("""
            { "id": "patient", "label": "Patient record", "required": false, "type": "object",
              "fields": [
                { "id": "name", "label": "Name", "type": "text" },
                { "id": "age", "label": "Age", "type": "number" },
                { "id": "sex", "label": "Sex", "required": false, "type": "enum", "values": ["female", "male"] },
                { "id": "notes", "label": "Notes", "required": false },
                { "id": "born", "label": "Born", "required": false, "type": "date" },
                { "id": "smoker", "label": "Smoker", "required": false, "type": "boolean" },
                { "id": "seen", "label": "Seen", "type": "date" } ] }
            """);

        AssertInput("""
            { "label": "Patient record", "description": null,
              "format": {
                "type": "object", "additionalProperties": false,
                "required": ["name", "age", "sex", "notes", "born", "smoker", "seen"],
                "properties": {
                  "name": { "$ref": "markdown_block", "title": "Name" },
                  "age": { "type": "number", "title": "Age" },
                  "sex": { "type": ["string", "null"], "enum": ["female", "male", null], "title": "Sex" },
                  "notes": { "anyOf": [{ "$ref": "markdown_block" }, { "type": "null" }], "title": "Notes" },
                  "born": { "anyOf": [{ "$ref": "date" }, { "type": "null" }], "title": "Born" },
                  "smoker": { "type": ["boolean", "null"], "title": "Smoker" },
                  "seen": { "$ref": "date", "title": "Seen" } } },
              "required": false, "channel": null }
            """, inputs["patient"]);
    }

    [Fact]
    public void ANestedObjectField_IsTypedTheSameWay()
    {
        var inputs = InputsWith("""
            { "id": "referral", "label": "Referral", "required": true, "type": "object",
              "fields": [
                { "id": "from", "label": "From", "required": false, "type": "object",
                  "fields": [ { "id": "clinic", "label": "Clinic", "type": "text" } ] },
                { "id": "reasons", "label": "Reasons", "type": "array", "items": "text" } ] }
            """);

        AssertInput("""
            { "type": "object", "additionalProperties": false, "required": ["from", "reasons"],
              "properties": {
                "from": {
                  "anyOf": [
                    { "type": "object", "additionalProperties": false, "required": ["clinic"],
                      "properties": { "clinic": { "$ref": "markdown_block", "title": "Clinic" } } },
                    { "type": "null" } ],
                  "title": "From" },
                "reasons": { "type": "array", "items": { "$ref": "markdown_block" }, "title": "Reasons" } } }
            """, inputs["referral"]!["format"]);
    }

    [Fact]
    public void AnArray_KeepsItsItems()
    {
        var inputs = InputsWith(
            """{ "id": "prior_notes", "label": "Prior notes", "required": false, "type": "array", "items": "text" }""",
            """
            { "id": "sections", "label": "Sections", "required": true, "type": "array", "items": "object",
              "fields": [ { "id": "title", "label": "Title", "type": "text" },
                          { "id": "kind", "label": "Kind", "type": "enum", "values": ["narrative", "table"] } ] }
            """,
            """{ "id": "scores", "label": "Scores", "required": false, "type": "array", "items": { "type": "array", "items": "number" } }""");

        AssertInput("""
            { "label": "Prior notes", "description": null, "format": { "type": "array", "items": { "$ref": "markdown_block" } },
              "required": false, "channel": null }
            """, inputs["prior_notes"]);
        AssertInput("""
            { "type": "array",
              "items": { "type": "object", "additionalProperties": false, "required": ["title", "kind"],
                         "properties": { "title": { "$ref": "markdown_block", "title": "Title" },
                                         "kind": { "type": "string", "enum": ["narrative", "table"], "title": "Kind" } } } }
            """, inputs["sections"]!["format"]);
        AssertInput("""{ "type": "array", "items": { "type": "array", "items": { "type": "number" } } }""", inputs["scores"]!["format"]);
    }

    [Fact]
    public void AUnion_BecomesItsArrayForm()
    {
        var inputs = InputsWith(
            """{ "id": "letters", "label": "Letters", "required": false, "type": ["text", "array"], "items": "text" }""",
            """{ "id": "counts", "label": "Counts", "required": false, "type": ["number", "array"], "items": "number" }""");

        AssertInput("""{ "type": "array", "items": { "$ref": "markdown_block" } }""", inputs["letters"]!["format"]);
        AssertInput("""{ "type": "array", "items": { "type": "number" } }""", inputs["counts"]!["format"]);
    }

    [Fact]
    public void AUnionWithNoArray_IsAHandEdit()
    {
        var result = MigrateWith("""{ "id": "when", "label": "When", "required": false, "type": ["date", "text"] }""");

        var edit = Assert.Single(result.HandEdits);
        Assert.Equal("Hand edit needed in manifest.json, inputs.when.format: the input takes one of several types, which a 31 input cannot; choose one format.", edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, "manifest.json"));
        AssertInput("""{ "anyOf": [{ "$ref": "date" }, { "$ref": "markdown_block" }] }""", JsonNode.Parse(result.ManifestJson)!["inputs"]!["when"]!["format"]);
    }

    [Fact]
    public void AUnionWhoseArrayHoldsAnotherType_IsAHandEdit()
    {
        // ["text", "array"] with items "number": an array of x would drop the text 30 accepted.
        var result = MigrateWith("""{ "id": "mixed", "label": "Mixed", "required": false, "type": ["text", "array"], "items": "number" }""");

        var edit = Assert.Single(result.HandEdits);
        Assert.Equal("Hand edit needed in manifest.json, inputs.mixed.format: the input takes one of several types, which a 31 input cannot; choose one format.", edit);
        AssertInput("""{ "anyOf": [{ "$ref": "markdown_block" }, { "type": "array", "items": { "type": "number" } }] }""",
            JsonNode.Parse(result.ManifestJson)!["inputs"]!["mixed"]!["format"]);
    }

    [Fact]
    public void ExpectedContent_BecomesChannel()
    {
        var inputs = InputsWith(
            """{ "id": "transcript", "label": "Transcript", "required": false, "type": "text", "expectedContent": "transcript" }""",
            """{ "id": "intake", "label": "Intake", "required": false, "type": "object", "expectedContent": "form", "fields": [ { "id": "age", "label": "Age", "type": "number" } ] }""");

        Assert.Equal("transcript", (string?)inputs["transcript"]!["channel"]);
        Assert.Equal("form", (string?)inputs["intake"]!["channel"]);
        Assert.False(inputs["transcript"]!.AsObject().ContainsKey("expectedContent"));
    }

    [Fact]
    public void EveryInput_HasDescriptionNull_AndKeepsRequired()
    {
        var inputs = InputsWith(
            """{ "id": "a", "label": "A", "required": false }""",
            """{ "id": "b", "label": "B" }""");

        foreach (var (_, input) in inputs)
        {
            Assert.True(input!.AsObject().ContainsKey("description"));
            Assert.Null(input["description"]);
        }

        Assert.False(inputs["a"]!["required"]!.GetValue<bool>());
        Assert.True(inputs["b"]!["required"]!.GetValue<bool>());
    }

    [Fact]
    public void TheOrder_IsThe30ArraysOrder()
    {
        var inputs = InputsWith(
            """{ "id": "zebra", "label": "Z" }""",
            """{ "id": "apple", "label": "A" }""",
            """{ "id": "mango", "label": "M" }""");

        Assert.Equal(["consult_draft", "zebra", "apple", "mango"], inputs.Select(i => i.Key));
        Assert.All(inputs, i => Assert.Equal(["label", "description", "format", "required", "channel"], i.Value!.AsObject().Select(m => m.Key)));
    }

    [Fact]
    public void AStructuredIntakeInput_IsWrittenAsPart02Says()
    {
        var inputs = JsonNode.Parse(MigrationFixture.Migrate("example-structured-intake").ManifestJson)!["inputs"]!.AsObject();

        Assert.Equal(["consult_draft", "prior_notes", "patient", "length_of_stay"], inputs.Select(i => i.Key));
        AssertInput("""
            { "label": "Patient record", "description": null,
              "format": { "type": "object", "additionalProperties": false, "required": ["age", "sex"],
                          "properties": { "age": { "type": "number", "title": "Age" },
                                          "sex": { "type": ["string", "null"], "enum": ["female", "male", null], "title": "Sex" } } },
              "required": false, "channel": null }
            """, inputs["patient"]);
    }

    [Fact]
    public void TheConditionalSectionsInputs_AreWrittenAsPart02Says()
    {
        // Part 02 § 8.4 before the author's later edits: title stays text (markdown_block), kind a plain enum.
        var inputs = JsonNode.Parse(MigrationFixture.Migrate("example-conditional-sections").ManifestJson)!["inputs"]!.AsObject();

        AssertInput("""
            { "label": "Sections to produce", "description": null,
              "format": { "type": "array",
                          "items": { "type": "object", "additionalProperties": false, "required": ["title", "kind", "notes"],
                                     "properties": {
                                       "title": { "$ref": "markdown_block", "title": "Title" },
                                       "kind": { "type": "string", "enum": ["narrative", "table", "omit"], "title": "Kind" },
                                       "notes": { "anyOf": [{ "$ref": "markdown_block" }, { "type": "null" }], "title": "Notes for this section" } } } },
              "required": true, "channel": null }
            """, inputs["sections"]);
        AssertInput("""{ "label": "Include the billing section", "description": null, "format": "boolean", "required": false, "channel": null }""",
            inputs["include_billing"]);
    }

    [Fact]
    public void NoInputField_IsSuggestedAFormat_SinceEvery30TextBecomesMarkdown()
    {
        // 30's inputs have no data string type: text, and a field with no type, become markdown_block, a text format. A
        // free-text string field is a package format's (A10-021, MigrateLayoutTests.AFreeTextStringField_IsSuggested_NotChanged).
        var result = MigrateWith("""
            { "id": "patient", "label": "Patient", "required": false, "type": "object",
              "fields": [ { "id": "name", "label": "Name" }, { "id": "story", "label": "Story", "type": "text" } ] }
            """);

        Assert.DoesNotContain(result.Report.Suggestions, s => s.StartsWith("Input ", StringComparison.Ordinal));
    }
}
