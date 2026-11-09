using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1339 A3 (package-format-v30-design.md § 5): <c>labelField</c> at 30, through the
/// seam — on an array of objects, and on every loop whose element is an object
/// (a node's value, an outer item's field): a required text field of the element.
/// </summary>
public class WorkflowV30LabelFieldTests
{
    private static JsonObject Node(JsonObject root, string id) => V30Package.Node(root, id);

    private static void NoErrors(List<string> errors) => Assert.True(errors.Count == 0, string.Join("\n", errors));

    private static void Says(string sentence, List<string> errors) =>
        Assert.True(errors.Contains(sentence), $"Expected: {sentence}\nGot:\n{string.Join("\n", errors)}");

    private static List<string> Errors(Action<JsonObject> change, Action<Dictionary<string, string>>? files = null) =>
        V30Package.Errors(V30Package.With(change, files));

    // ---- an array of objects -------------------------------------------------------------

    /// <summary>An array-of-objects input read by the plan, with a required text title, an enum kind and an optional aside.</summary>
    private static void AddCases(JsonObject root, string? labelField)
    {
        var input = new JsonObject
        {
            ["id"] = "cases", ["label"] = "Cases", ["type"] = "array", ["items"] = "object",
            ["fields"] = new JsonArray(
                new JsonObject { ["id"] = "title", ["label"] = "Title", ["type"] = "text" },
                new JsonObject { ["id"] = "kind", ["label"] = "Kind", ["type"] = "enum", ["values"] = new JsonArray("new", "follow_up") },
                new JsonObject { ["id"] = "aside", ["label"] = "Aside", ["type"] = "text", ["required"] = false }),
        };
        if (labelField is not null)
        {
            input["labelField"] = labelField;
        }

        root["inputs"]!.AsArray().Add(input);
    }

    private static List<string> CaseErrors(string? labelField, Action<JsonObject>? more = null) =>
        Errors(root => { AddCases(root, labelField); more?.Invoke(root); },
            files => files["prompts/plan.md"] = "Plan the sections: {{ input.notes }} {{ for c in input.cases }}{{ c.title }}{{ end }}");

    [Fact]
    public void AnInput_NamesItsItemsByARequiredTextField() => NoErrors(CaseErrors("title"));

    [Fact]
    public void AnInput_ByAnEnumField_IsRefused() =>
        Says("Input 'cases' labelField 'kind' names an enum field; labelField names a required text field.", CaseErrors("kind"));

    [Fact]
    public void AnInput_ByAnOptionalField_IsRefused() =>
        Says("Input 'cases' labelField 'aside' is optional; labelField names a required text field.", CaseErrors("aside"));

    [Fact]
    public void AnInput_ByAMissingField_IsRefused() =>
        Says("Input 'cases' labelField 'name' is not a field of its items (fields: aside, kind, title).", CaseErrors("name"));

    [Fact]
    public void AScalarInput_HasNoItemsToName() =>
        Says("Input 'notes' declares labelField, but it is not an array of objects; labelField names a field of each item.",
            Errors(root => root["inputs"]!.AsArray().OfType<JsonObject>().Single(i => i["id"]!.GetValue<string>() == "notes")["labelField"] = "title"));

    [Fact]
    public void ALoopOverAnInput_TakesTheInputsLabelField() =>
        Says("Loop 'per_case' declares labelField, but it iterates input.cases — the input names its items (input.cases's labelField).",
            CaseErrors("title", root =>
            {
                root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "case_note", ["label"] = "Case note", ["format"] = "prose" });
                root["nodes"]!.AsArray().Add(new JsonObject
                {
                    ["id"] = "per_case", ["label"] = "Per case", ["for"] = "item in input.cases", ["labelField"] = "title",
                    ["nodes"] = new JsonArray("case_note"), ["ret"] = "node.case_note"
                });
            }));

    // ---- a loop over a node's value --------------------------------------------------------

    [Fact]
    public void ALoopOverANodesValue_NamesItsItemsByARequiredTextField() =>
        NoErrors(Errors(root => Node(root, "problems")["labelField"] = "name"));

    [Fact]
    public void ALoopOverANodesValue_ByAMissingField_IsRefused() =>
        Says("Loop 'problems' labelField 'title' is not a field of the elements of node.find_problems.value.problems (fields: name, severity).",
            Errors(root => Node(root, "problems")["labelField"] = "title"));

    [Fact]
    public void ALoopOverANodesValue_ByAnEnumField_IsRefused() =>
        Says("Loop 'problems' labelField 'severity' names an enum field; labelField names a required text field.",
            Errors(root => Node(root, "problems")["labelField"] = "severity"));

    [Fact]
    public void ALoopOverANodesValue_ByAFieldTheSchemaLeavesOptional_IsRefused()
    {
        const string Asides = """
            { "type": "object", "producer": "custom", "outputMacros": false, "description": "Points with optional asides.",
              "schema": { "type": "object", "additionalProperties": false, "required": ["points"],
                          "properties": { "points": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["point", "aside"],
                                                                                  "properties": { "point": { "type": "string" }, "aside": { "type": ["string", "null"] } } } } } } }
            """;
        var errors = Errors(root =>
        {
            root["formats"]!.AsArray().Add(new JsonObject { ["id"] = "asides", ["file"] = "formats/asides.json" });
            Node(root, "outline")["format"] = "asides";
            Node(root, "paragraphs")["labelField"] = "aside";
        }, files => files["formats/asides.json"] = Asides);

        Says("Loop 'paragraphs' labelField 'aside' is optional; labelField names a required text field.", errors);
    }

    [Fact]
    public void ALoopOverAWholeListOfText_HasNoFieldToName()
    {
        const string Words = """
            { "type": "object", "producer": "custom", "outputMacros": false, "description": "Words.",
              "schema": { "type": "object", "additionalProperties": false, "required": ["words"],
                          "properties": { "words": { "type": "array", "items": { "type": "string" } } } } }
            """;
        var errors = Errors(root =>
        {
            root["formats"]!.AsArray().Add(new JsonObject { ["id"] = "words", ["file"] = "formats/words.json" });
            Node(root, "outline")["format"] = "words";
            Node(root, "paragraphs")["for"] = "item in node.outline.value.words";
            Node(root, "paragraphs")["labelField"] = "word";
        }, files =>
        {
            files["formats/words.json"] = Words;
            files["prompts/draft_paragraph.md"] = "Section {{ item.parent.value.title }}, word {{ item.value }}.";
        });

        Says("Loop 'paragraphs' labelField 'word': each element of node.outline.value.words is a text, not an object; labelField names a field of each item.", errors);
    }

    // ---- a nested loop over the outer item's field ---------------------------------------------

    [Fact]
    public void ANestedLoopOverTheOuterItemsField_NamesItsItems()
    {
        NoErrors(Errors(root =>
        {
            Node(root, "paragraphs")["for"] = "item in item.value.topics";
            Node(root, "paragraphs")["labelField"] = "topic";
            Node(root, "parts")["labelField"] = "title";
        }, files => files["prompts/draft_paragraph.md"] = "Part {{ item.parent.value.title }}, topic {{ item.value.topic }}, of {{ node.outline.value.points | array.size }} points."));
    }

    [Fact]
    public void ANestedLoopOverTheOuterItemsField_ByAMissingField_IsRefused() =>
        Says("Loop 'paragraphs' labelField 'name' is not a field of the elements of item.value.topics (fields: topic).",
            Errors(root =>
            {
                Node(root, "paragraphs")["for"] = "item in item.value.topics";
                Node(root, "paragraphs")["labelField"] = "name";
            }, files => files["prompts/draft_paragraph.md"] = "Part {{ item.parent.value.title }}, topic {{ item.value.topic }}, of {{ node.outline.value.points | array.size }} points."));

    // ---- not on other nodes ----------------------------------------------------------------------

    [Fact]
    public void APlainNode_HasNoItemsToName() =>
        Says("Node 'plan' declares labelField, which belongs to a for node.", Errors(root => Node(root, "plan")["labelField"] = "title"));

    [Fact]
    public void AWhile_DoesNotCarryLabelField() =>
        Says("Node 'refine' declares labelField, which a while node does not carry.",
            V30Package.Errors(WorkflowV30WhileTests.Refining(root => V30Package.Node(root, "refine")["labelField"] = "title")));

    // ---- below 30: the gate ----------------------------------------------------------------------

    [Fact]
    public void At29_LabelField_IsGatedByName_OnALoopAndAnInput()
    {
        var (json, files) = V29Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        V29Package.Node(root, "problems")["labelField"] = "name";
        AddCases(root, "title");

        var errors = V29Package.Errors((V29Package.Read(root.ToJsonString()), files));
        Says("Node 'problems' declares labelField, which requires specVersion 30.", errors);
        Says("Input 'cases' declares labelField, which requires specVersion 30.", errors);
    }
}
