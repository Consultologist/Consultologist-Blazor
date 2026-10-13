using Consultologist.Api.Agents;

namespace Consultologist.Api.Tests;

/// <summary>
/// B1 (#1000): the pre-publish check the agents repo's CI runs over a
/// formats catalogue version — the engine's reader plus the attested
/// response-format rule, against a temp registry.
/// </summary>
public sealed class FormatsCatalogCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "formats-check-" + Guid.NewGuid().ToString("N"));

    public FormatsCatalogCheckTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "formats"));
        File.WriteAllText(Path.Combine(_dir, "formats.json"), WorkflowFormatCatalogIndexTests.Example
            .Replace("\"agentName\": \"consult-prose\",      \"agentVersion\": 3", "\"agentName\": \"test-json\",      \"agentVersion\": 48"));
        File.WriteAllText(Path.Combine(_dir, "formats", "prose.json"), FormatFixtures.Prose);
        File.WriteAllText(Path.Combine(_dir, "formats", "classification.json"), FormatFixtures.Classification);
        File.WriteAllText(Path.Combine(_dir, "formats", "concept-list.json"), FormatFixtures.ConceptList);
        File.WriteAllText(Path.Combine(_dir, "formats", "rendered.json"), FormatFixtures.Rendered);
        File.WriteAllText(Path.Combine(_dir, "formats", "terms-subset.json"), FormatFixtures.TermsSubset);
        Agent("test-json", "48", "type: text");
        Agent("classification", "1", JsonSchema("classification", FormatsCatalogCheck.EnumAnswerSchema));
        Agent("concept-extraction", "1", JsonSchema("concept_list", """{ "type": "object", "additionalProperties": false, "required": ["concepts"], "properties": { "concepts": { "type": "array", "items": { "type": "string" } } } }"""));
        Agent("custom-json", "1", "type: text");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Agent(string name, string version, string format)
    {
        var yaml = $"""
            name: {name}
            version: "{version}"
            status: active
            definition:
              kind: prompt
              model: gpt-5.6-sol
              instructions: |
                Do the thing.
              text:
                format:
                  {format.Replace("\n", "\n      ")}
            """;
        File.WriteAllText(Path.Combine(_dir, name + ".yaml"), yaml);
    }

    private static string JsonSchema(string name, string schema) =>
        $"type: json_schema\nname: {name}\nstrict: true\nschema: |\n  {schema}";

    private List<string> Errors() => FormatsCatalogCheck.Run(_dir).Errors.ToList();

    [Fact]
    public void TheRegistryShape_Passes_AndDescribesTheBindings()
    {
        var result = FormatsCatalogCheck.Run(_dir);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        var lines = FormatsCatalogCheck.Describe(result.Catalog!).ToList();
        Assert.Contains("  prose (text, attested) -> test-json@48", lines);
        Assert.Contains("  terms-subset (object, computed) -> op terms-subset", lines);
        Assert.Contains("  rendered (text, rendered) -> no agent (rendered)", lines);
        Assert.Contains("  custom -> custom-json@1", lines);
    }

    [Fact]
    public void NoIndex_IsNamed()
    {
        File.Delete(Path.Combine(_dir, "formats.json"));

        Assert.Contains(Errors(), e => e.StartsWith("No formats.json in ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheIndexReadersSentences_PassThrough()
    {
        File.WriteAllText(Path.Combine(_dir, "formats.json"), """{ "version": "nope", "formats": {}, "producers": {} }""");

        var errors = Errors();

        Assert.Contains(errors, e => e.Contains("must declare a CalVer version"));
        Assert.Contains(errors, e => e.Contains("declares no formats"));
    }

    [Fact]
    public void AMissingFormatFile_IsNamed()
    {
        File.Delete(Path.Combine(_dir, "formats", "rendered.json"));

        Assert.Contains(Errors(), e => e.Contains("format 'rendered' file 'formats/rendered.json' is missing"));
    }

    [Fact]
    public void AVersionMismatch_IsRefused()
    {
        Agent("test-json", "49", "type: text");

        Assert.Contains("Format 'prose' binds agent 'test-json' at version 48, but test-json.yaml declares version '49'; the index and the yaml must agree.", Errors());
    }

    [Fact]
    public void AMissingAgentYaml_IsRefused()
    {
        File.Delete(Path.Combine(_dir, "concept-extraction.yaml"));

        Assert.Contains("Format 'concept-list' names agent 'concept-extraction', but concept-extraction.yaml is not in the registry.", Errors());
    }

    [Fact]
    public void ATextFormat_NeedsATextAgent()
    {
        Agent("test-json", "48", JsonSchema("x", "{}"));

        Assert.Contains("Format 'prose' agent 'test-json' declares text.format.type 'json_schema', but the format is text.", Errors());
    }

    [Fact]
    public void AnObjectFormat_NeedsItsSchema_Strictly()
    {
        Agent("concept-extraction", "1", "type: text");
        Assert.Contains(Errors(), e => e.StartsWith("Format 'concept-list' agent 'concept-extraction' declares text.format.type 'text', but the format is an object", StringComparison.Ordinal));

        Agent("concept-extraction", "1", JsonSchema("concept_list", """{ "type": "object", "properties": { "terms": { "type": "array" } } }"""));
        Assert.Contains("Format 'concept-list' agent 'concept-extraction' text.format.schema differs from the format's schema; the agent must declare exactly the shape the catalogue publishes.", Errors());

        Agent("concept-extraction", "1", "type: json_schema\nname: concept_list\nstrict: false\nschema: |\n  " + """{ "type": "object", "additionalProperties": false, "required": ["concepts"], "properties": { "concepts": { "type": "array", "items": { "type": "string" } } } }""");
        Assert.Contains("Format 'concept-list' agent 'concept-extraction' json_schema response format is not strict.", Errors());
    }

    [Fact]
    public void AnEnumFormat_NeedsTheOneValueAnswer()
    {
        Agent("classification", "1", JsonSchema("classification", """{ "type": "object", "properties": { "label": { "type": "string" } } }"""));

        Assert.Contains("Format 'classification' agent 'classification' text.format.schema differs from the format's schema; the agent must declare exactly the shape the catalogue publishes.", Errors());
    }

    [Fact]
    public void TheCustomProducer_IsATextAgent_AtItsVersion()
    {
        Agent("custom-json", "1", JsonSchema("custom", "{}"));
        Assert.Contains("producers.custom agent 'custom-json' declares text.format.type 'json_schema'; the generic structured agent answers text the engine parses and validates against the package's schema.", Errors());

        Agent("custom-json", "2", "type: text");
        Assert.Contains("producers.custom binds agent 'custom-json' at version 1, but custom-json.yaml declares version '2'; the index and the yaml must agree.", Errors());

        File.Delete(Path.Combine(_dir, "custom-json.yaml"));
        Assert.Contains("producers.custom names agent 'custom-json', but custom-json.yaml is not in the registry.", Errors());
    }

    [Fact]
    public void AnOpTheEngineDoesNotPublish_IsRefused()
    {
        var index = File.ReadAllText(Path.Combine(_dir, "formats.json")).Replace("\"op\": \"terms-subset\"", "\"op\": \"terms-superset\"");
        File.WriteAllText(Path.Combine(_dir, "formats.json"), index);

        Assert.Contains(Errors(), e => e.Contains("names op 'terms-superset', which the engine does not publish"));
    }
}
