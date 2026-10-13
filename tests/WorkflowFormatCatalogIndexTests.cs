using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// The format catalogue index (design § 3.3, A1 #996): the two-section shape
/// reads; every structural refusal is a sentence; agent identity is two fields,
/// the version an integer.
/// </summary>
public class WorkflowFormatCatalogIndexTests
{
    /// <summary>The § 3.3 example, verbatim in shape.</summary>
    public const string Example = """
        {
          "version": "v2026.11.1",
          "formats": {
            "prose":          { "file": "formats/prose.json",          "agentName": "consult-prose",      "agentVersion": 3 },
            "classification": { "file": "formats/classification.json", "agentName": "classification",     "agentVersion": 1 },
            "concept-list":   { "file": "formats/concept-list.json",   "agentName": "concept-extraction", "agentVersion": 1 },
            "rendered":       { "file": "formats/rendered.json" },
            "terms-subset":   { "file": "formats/terms-subset.json",   "op": "terms-subset" }
          },
          "producers": {
            "custom": { "agentName": "custom-json", "agentVersion": 1,
                        "trailer": "Answer with one JSON object conforming to this schema." }
          }
        }
        """;

    public static WorkflowFormatCatalogIndex Read(string json)
    {
        var errors = new List<string>();
        Assert.True(WorkflowFormatCatalogIndexJson.TryRead(json, "Format catalogue", errors, out var index), string.Join(" | ", errors));
        return index!;
    }

    private static List<string> Errors(string? json)
    {
        var errors = new List<string>();
        Assert.False(WorkflowFormatCatalogIndexJson.TryRead(json, "Format catalogue", errors, out var index));
        Assert.Null(index);
        return errors;
    }

    [Fact]
    public void TheDesignExample_Reads()
    {
        var index = Read(Example);

        Assert.Equal("v2026.11.1", index.Version);
        Assert.Equal(5, index.Formats.Count);

        var prose = index.Formats["prose"];
        Assert.Equal("formats/prose.json", prose.File);
        Assert.Equal("consult-prose", prose.AgentName);
        Assert.Equal(3, prose.AgentVersion);
        Assert.Equal("consult-prose@3", prose.AgentDisplay);
        Assert.True(prose.NamesAgent);
        Assert.False(prose.NamesOp);

        var rendered = index.Formats["rendered"];
        Assert.False(rendered.NamesAgent);
        Assert.False(rendered.NamesOp);
        Assert.Null(rendered.AgentDisplay);

        var check = index.Formats["terms-subset"];
        Assert.Equal("terms-subset", check.Op);
        Assert.True(check.NamesOp);

        Assert.Equal("custom-json", index.Producers.Custom!.AgentName);
        Assert.Equal(1, index.Producers.Custom.AgentVersion);
        Assert.Equal("custom-json@1", index.Producers.Custom.AgentDisplay);
        Assert.StartsWith("Answer with one JSON object", index.Producers.Custom.Trailer);
    }

    [Theory]
    [InlineData("", "is empty")]
    [InlineData("{ nope", "is not valid JSON")]
    [InlineData("null", "is not valid JSON")]
    public void MalformedIndex_IsRefused(string json, string sentence)
    {
        Assert.Contains(Errors(json), e => e.Contains(sentence));
    }

    [Fact]
    public void UnknownProperty_IsRefused_NamingItsPath()
    {
        var json = Example.Replace("\"agentVersion\": 3 }", "\"agentVersion\": 3, \"schemaFile\": \"x\" }");

        Assert.Contains(Errors(json), e => e.Contains("property '$.formats.prose.schemaFile' is not part of the format catalogue index"));
    }

    [Fact]
    public void AQuotedAgentVersion_IsRefused()
    {
        // output-contracts.json carries "47"; the format catalogue carries an integer.
        var json = Example.Replace("\"agentVersion\": 3", "\"agentVersion\": \"3\"");

        Assert.Contains(Errors(json), e => e.Contains("property '$.formats.prose.agentVersion' is not part of the format catalogue index"));
    }

    [Theory]
    [InlineData("\"version\": \"v2026.11.1\"", "\"version\": \"2026.11.1\"", "must declare a CalVer version")]
    [InlineData("\"version\": \"v2026.11.1\"", "\"version\": \"latest\"", "must declare a CalVer version")]
    [InlineData("\"formats\": {", "\"formats\": {}, \"x\": {", "declares no formats")]
    public void VersionAndFormats_AreRequired(string find, string replace, string sentence)
    {
        var json = Example.Replace(find, replace);

        // "x" is not part of the index; the formats-empty case reads the sentence it can.
        var errors = new List<string>();
        WorkflowFormatCatalogIndexJson.TryRead(json, "Format catalogue", errors, out _);
        Assert.Contains(errors, e => e.Contains(sentence) || e.Contains("not part of the format catalogue index"));
    }

    [Fact]
    public void NoFormats_IsRefused()
    {
        const string json = """{ "version": "v2026.11.1", "formats": {}, "producers": { "custom": { "agentName": "custom-json", "agentVersion": 1, "trailer": "t" } } }""";

        Assert.Contains(Errors(json), e => e.Contains("Format catalogue declares no formats."));
    }

    [Theory]
    [InlineData("\"prose\":          {", "\"Prose\":          {", "format 'Prose' id must be lowercase letters, digits and hyphens, letter first")]
    [InlineData("\"prose\":", "\"custom\":", "format 'custom' is a producer binding, never a format entry")]
    [InlineData("\"file\": \"formats/prose.json\",", "", "format 'prose' must declare a file")]
    [InlineData("\"file\": \"formats/prose.json\",", "\"file\": \"../prose.json\",", "format 'prose' file '../prose.json' must be a relative path within the catalogue")]
    [InlineData("\"file\": \"formats/prose.json\",", "\"file\": \"/prose.json\",", "must be a relative path within the catalogue")]
    [InlineData("\"agentName\": \"consult-prose\",", "", "format 'prose' must declare agentName and agentVersion together")]
    [InlineData(",      \"agentVersion\": 3", "", "format 'prose' must declare agentName and agentVersion together")]
    [InlineData("\"agentVersion\": 3", "\"agentVersion\": 0", "format 'prose' agentVersion must be a positive integer")]
    [InlineData("\"agentName\": \"consult-prose\"", "\"agentName\": \" \"", "format 'prose' agentName must not be blank")]
    [InlineData("\"op\": \"terms-subset\"", "\"op\": \"\"", "format 'terms-subset' op must not be blank")]
    [InlineData("\"agentVersion\": 3 }", "\"agentVersion\": 3, \"op\": \"terms-subset\" }", "format 'prose' names both an agent and an op")]
    [InlineData("\"rendered\":       { \"file\": \"formats/rendered.json\" }", "\"rendered\": null", "format 'rendered' must be an object with a file")]
    public void EntryDefects_AreRefusedByName(string find, string replace, string sentence)
    {
        var json = Example.Replace(find, replace);
        Assert.NotEqual(Example, json);

        Assert.Contains(Errors(json), e => e.StartsWith("Format catalogue ", StringComparison.Ordinal) && e.Contains(sentence));
    }

    [Theory]
    [InlineData("\"producers\": {", "\"producers\": { \"x\": 1,", "not part of the format catalogue index")]
    [InlineData("\"agentName\": \"custom-json\", ", "", "producers.custom must declare agentName")]
    [InlineData("\"agentVersion\": 1,\n", "\"agentVersion\": -1,\n", "producers.custom agentVersion must be a positive integer")]
    [InlineData("\"agentVersion\": 1,\n", "", "producers.custom agentVersion must be a positive integer")]
    [InlineData("\"trailer\": \"Answer with one JSON object conforming to this schema.\"", "\"trailer\": \"\"", "producers.custom must declare the trailer sentence")]
    public void ProducerDefects_AreRefusedByName(string find, string replace, string sentence)
    {
        var json = Example.Replace(find, replace);
        Assert.NotEqual(Example, json);

        Assert.Contains(Errors(json), e => e.Contains(sentence));
    }

    [Theory]
    [InlineData("""{ "version": "v2026.11.1", "formats": { "prose": { "file": "f.json", "agentName": "a", "agentVersion": 1 } } }""")]
    [InlineData("""{ "version": "v2026.11.1", "formats": { "prose": { "file": "f.json", "agentName": "a", "agentVersion": 1 } }, "producers": {} }""")]
    public void ProducersCustom_IsRequired(string json)
    {
        Assert.Contains(Errors(json), e => e.Contains("Format catalogue must declare producers.custom"));
    }

    [Fact]
    public void EveryDefect_IsListed_AtOnce()
    {
        const string json = """
            { "version": "nope",
              "formats": { "prose": { "file": "", "agentName": "a" }, "custom": { "file": "f.json", "op": "x", "agentName": "a", "agentVersion": 1 } },
              "producers": { "custom": { "agentName": "", "agentVersion": 0, "trailer": "" } } }
            """;

        var errors = Errors(json);

        Assert.Contains(errors, e => e.Contains("CalVer"));
        Assert.Contains(errors, e => e.Contains("format 'prose' must declare a file"));
        Assert.Contains(errors, e => e.Contains("format 'prose' must declare agentName and agentVersion together"));
        Assert.Contains(errors, e => e.Contains("format 'custom' is a producer binding"));
        Assert.Contains(errors, e => e.Contains("format 'custom' names both an agent and an op"));
        Assert.Contains(errors, e => e.Contains("producers.custom must declare agentName"));
        Assert.Contains(errors, e => e.Contains("producers.custom agentVersion must be a positive integer"));
        Assert.Contains(errors, e => e.Contains("producers.custom must declare the trailer sentence"));
        Assert.True(errors.Count >= 8, string.Join(" | ", errors));
    }

    [Theory]
    [InlineData("prose", true)]
    [InlineData("concept-list", true)]
    [InlineData("a1", true)]
    [InlineData("Prose", false)]
    [InlineData("concept_list", false)]
    [InlineData("-x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CatalogueIds_AreHyphenated(string? id, bool valid)
    {
        Assert.Equal(valid, WorkflowFormatCatalogIds.IsValid(id));
    }
}
