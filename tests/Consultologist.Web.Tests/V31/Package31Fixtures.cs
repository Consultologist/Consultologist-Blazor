using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 9 (#1509): a specVersion 31 package as <c>WorkflowPackages/Current</c> serves it, and the content response the
/// Create page reads its inputs' help text from (D4). The formats are written as the Api sends them: one compound
/// document, the schema at the root and the built-in formats it reaches under <c>$defs</c>, referenced by <c>$id</c>.
/// </summary>
public static class Package31Fixtures
{
    public const string SpecFormat = "urn:consultologist:spec:31:format:";

    public static string Plaintext => Builtin("plaintext", """{"contentMediaType":"text/plain","type":"string"}""");

    public static string MarkdownBlock => Builtin("markdown_block", """{"contentMediaType":"text/markdown","type":"string","x-markdown":"block"}""");

    public static string Date => Builtin("date", """{"format":"date","type":"string"}""");

    /// <summary>The definition of a built-in format, for a schema's own <c>$defs</c>.</summary>
    public static string Def(string name) => name switch
    {
        "plaintext" => """{"$id":"urn:consultologist:spec:31:format:plaintext","contentMediaType":"text/plain","type":"string"}""",
        "markdown_block" => """{"$id":"urn:consultologist:spec:31:format:markdown_block","contentMediaType":"text/markdown","type":"string","x-markdown":"block"}""",
        "date" => """{"$id":"urn:consultologist:spec:31:format:date","format":"date","type":"string"}""",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>A reference to a built-in format, as a field of an object writes it.</summary>
    public static string Ref(string name) => "{\"$ref\":\"" + SpecFormat + name + "\"}";

    /// <summary>A nullable reference (part 02 § 3.2): <c>anyOf</c> the reference and null.</summary>
    public static string NullableRef(string name, string title) =>
        "{\"anyOf\":[" + Ref(name) + ",{\"type\":\"null\"}],\"title\":\"" + title + "\"}";

    private static string Builtin(string name, string body) =>
        "{\"$ref\":\"" + SpecFormat + name + "\",\"$defs\":{\"" + name + "\":" + Def(name) + "}}";

    public static WorkflowPackageResponse Package(params WorkflowPackageInputResponse[] inputs) =>
        new(
            "example-nested-note",
            "v2026.12.1",
            31,
            Blocks: [new("node.plan", "Planning the sections"), new("node.sections", "Sections")],
            Inputs: inputs,
            Results: [new("deliverable.note", "Note")],
            Title: "Nested note",
            Macros: []);

    /// <summary>
    /// One input as <c>Current</c> serves it at 31: its <c>Format</c> and <c>Channel</c>, and <c>ExpectedContent</c>
    /// filled from the channel (part 09 § 4.3).
    /// </summary>
    public static WorkflowPackageInputResponse Input(string id, string label, bool required, string formatJson, string? channel = null) =>
        new(id, label, required,
            ExpectedContent: channel,
            Format: JsonDocument.Parse(formatJson).RootElement.Clone(),
            Channel: channel);

    /// <summary>The content response whose <c>Resolve.inputs.&lt;id&gt;.description</c> holds each input's help (null for none).</summary>
    public static WorkflowPackageContentResponse Content(params (string Id, string[]? Description)[] inputs)
    {
        var table = new JsonObject();
        foreach (var (id, description) in inputs)
        {
            table[id] = new JsonObject
            {
                ["description"] = description is null ? null : new JsonArray(description.Select(line => (JsonNode?)JsonValue.Create(line)).ToArray()),
            };
        }

        var resolve = new JsonObject
        {
            ["inputs"] = table,
            ["inputOrder"] = new JsonArray(inputs.Select(input => (JsonNode?)JsonValue.Create(input.Id)).ToArray()),
        };

        return new WorkflowPackageContentResponse(
            "example-nested-note",
            "v2026.12.1",
            31,
            JsonDocument.Parse("""{"specVersion":31}""").RootElement.Clone(),
            new Dictionary<string, string>(),
            Resolve: JsonDocument.Parse(resolve.ToJsonString()).RootElement.Clone());
    }
}
