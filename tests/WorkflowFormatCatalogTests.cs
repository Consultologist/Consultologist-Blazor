using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// The bound catalogue (design § 3.3, A1 #996): the index and its five files
/// build; every producer↔entry cross-check is a sentence; a parameter's format
/// reference must resolve in the same index.
/// </summary>
public class WorkflowFormatCatalogTests
{
    private static Dictionary<string, string> Files() => new(StringComparer.Ordinal)
    {
        ["formats/prose.json"] = FormatFixtures.Prose,
        ["formats/classification.json"] = FormatFixtures.Classification,
        ["formats/concept-list.json"] = FormatFixtures.ConceptList,
        ["formats/rendered.json"] = FormatFixtures.Rendered,
        ["formats/terms-subset.json"] = FormatFixtures.TermsSubset,
    };

    private static WorkflowFormatCatalog Build(string indexJson, Dictionary<string, string> files)
    {
        var index = WorkflowFormatCatalogIndexTests.Read(indexJson);
        var errors = new List<string>();
        var catalog = WorkflowFormatCatalog.Build(index, path => files.GetValueOrDefault(path), errors);
        Assert.Empty(errors);
        return catalog!;
    }

    private static List<string> Errors(string indexJson, Dictionary<string, string> files)
    {
        var index = WorkflowFormatCatalogIndexTests.Read(indexJson);
        var errors = new List<string>();
        var catalog = WorkflowFormatCatalog.Build(index, path => files.GetValueOrDefault(path), errors);
        Assert.Null(catalog);
        Assert.NotEmpty(errors);
        return errors;
    }

    [Fact]
    public void TheDesignExample_Builds()
    {
        var catalog = Build(WorkflowFormatCatalogIndexTests.Example, Files());

        Assert.Equal("v2026.11.1", catalog.Version);
        Assert.Equal("formats@v2026.11.1", catalog.ResolvedRef);
        Assert.Equal(5, catalog.Formats.Count);
        Assert.Equal("custom-json@1", catalog.CustomProducer.AgentDisplay);

        Assert.True(catalog.TryGet("concept-list", out var conceptList));
        Assert.Equal("object", conceptList!.Spec.Type);
        Assert.Equal("attested", conceptList.Spec.Producer);
        Assert.Equal("concept-extraction@1", conceptList.Entry.AgentDisplay);
        Assert.Equal(WorkflowFormatSpecJson.CanonicalJson(FormatFixtures.ConceptList), conceptList.CanonicalJson);

        Assert.True(catalog.TryGet("terms-subset", out var check));
        Assert.Equal("terms-subset", check!.Entry.Op);
        Assert.Equal("concept-list", check.Spec.Parameters!["of"].Format);

        Assert.False(catalog.TryGet("custom", out _));
        Assert.False(catalog.TryGet("medication_list", out _));
    }

    [Fact]
    public void CanonicalJson_IsTheFilesBytes_NotTheIndexEntry()
    {
        var files = Files();
        files["formats/prose.json"] = """{"description":"Consult prose.","producer":"attested","type":"text","outputMacros":true}""";

        var catalog = Build(WorkflowFormatCatalogIndexTests.Example, files);

        Assert.True(catalog.TryGet("prose", out var prose));
        Assert.Equal(WorkflowFormatSpecJson.CanonicalJson(FormatFixtures.Prose), prose!.CanonicalJson);
        Assert.DoesNotContain("consult-prose", prose.CanonicalJson);
    }

    [Fact]
    public void AMissingFile_IsRefused()
    {
        var files = Files();
        files.Remove("formats/rendered.json");

        Assert.Contains(Errors(WorkflowFormatCatalogIndexTests.Example, files),
            e => e == "Format catalogue v2026.11.1 format 'rendered' file 'formats/rendered.json' is missing.");
    }

    [Fact]
    public void AFileThatDoesNotRead_IsRefused_WithTheFormatAsSubject()
    {
        var files = Files();
        files["formats/rendered.json"] = "{ nope";

        Assert.Contains(Errors(WorkflowFormatCatalogIndexTests.Example, files),
            e => e == "Format catalogue v2026.11.1 format 'rendered' file is not valid JSON.");
    }

    [Fact]
    public void AFileTheValidatorRefuses_IsRefused_AndNotBound()
    {
        var files = Files();
        files["formats/concept-list.json"] = """{ "type": "object", "producer": "attested" }""";

        var errors = Errors(WorkflowFormatCatalogIndexTests.Example, files);

        Assert.Contains(errors, e => e.StartsWith("Format catalogue v2026.11.1 format 'concept-list' is type 'object' and must declare a schema", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains("must name the agent"));
    }

    [Fact]
    public void AnAttestedFormat_MustNameAnAgent()
    {
        var index = WorkflowFormatCatalogIndexTests.Example.Replace(
            "\"file\": \"formats/prose.json\",          \"agentName\": \"consult-prose\",      \"agentVersion\": 3",
            "\"file\": \"formats/prose.json\"");

        Assert.Contains(Errors(index, Files()),
            e => e == "Format catalogue v2026.11.1 format 'prose' is attested and its index entry must name the agent (agentName and agentVersion).");
    }

    [Fact]
    public void AnAttestedFormat_MustNotNameAnOp()
    {
        var index = WorkflowFormatCatalogIndexTests.Example.Replace(
            "\"file\": \"formats/prose.json\",          \"agentName\": \"consult-prose\",      \"agentVersion\": 3",
            "\"file\": \"formats/prose.json\", \"op\": \"terms-subset\"");

        var errors = Errors(index, Files());

        Assert.Contains(errors, e => e.Contains("format 'prose' is attested and its index entry must name the agent"));
        Assert.Contains(errors, e => e == "Format catalogue v2026.11.1 format 'prose' is attested and its index entry must not name an op.");
    }

    [Fact]
    public void AComputedFormat_MustNameAnOp()
    {
        var index = WorkflowFormatCatalogIndexTests.Example.Replace(",   \"op\": \"terms-subset\"", "");
        Assert.NotEqual(WorkflowFormatCatalogIndexTests.Example, index);

        Assert.Contains(Errors(index, Files()),
            e => e == "Format catalogue v2026.11.1 format 'terms-subset' is computed and its index entry must name the op (one of terms_subset).");
    }

    [Fact]
    public void AComputedFormat_MayNameTheOp_InEitherSpelling()
    {
        // #1037: terms_subset is the 25 spelling; terms-subset (formats@v2026.10.1) is read until the re-pin.
        var index = WorkflowFormatCatalogIndexTests.Read(WorkflowFormatCatalogIndexTests.Example.Replace("\"op\": \"terms-subset\"", "\"op\": \"terms_subset\""));
        var errors = new List<string>();
        var files = Files();

        var catalog = WorkflowFormatCatalog.Build(index, path => files.GetValueOrDefault(path), errors);

        Assert.Empty(errors);
        Assert.Equal("terms_subset", catalog!.Formats["terms-subset"].Entry.Op);
    }

    [Fact]
    public void AComputedFormat_MustNameAnOpTheEnginePublishes()
    {
        var index = WorkflowFormatCatalogIndexTests.Example.Replace("\"op\": \"terms-subset\"", "\"op\": \"terms-superset\"");

        Assert.Contains(Errors(index, Files()),
            e => e == "Format catalogue v2026.11.1 format 'terms-subset' names op 'terms-superset', which the engine does not publish (one of terms_subset).");
    }

    [Fact]
    public void AComputedFormat_MustNotNameAnAgent()
    {
        var index = WorkflowFormatCatalogIndexTests.Example.Replace(
            ",   \"op\": \"terms-subset\"",
            ", \"agentName\": \"checker\", \"agentVersion\": 1");

        Assert.Contains(Errors(index, Files()),
            e => e == "Format catalogue v2026.11.1 format 'terms-subset' is computed and its index entry must not name an agent.");
    }

    [Theory]
    [InlineData("\"file\": \"formats/rendered.json\", \"agentName\": \"renderer\", \"agentVersion\": 1")]
    [InlineData("\"file\": \"formats/rendered.json\", \"op\": \"terms-subset\"")]
    public void ARenderedFormat_NamesNeither(string entry)
    {
        var index = WorkflowFormatCatalogIndexTests.Example.Replace("\"file\": \"formats/rendered.json\"", entry);

        Assert.Contains(Errors(index, Files()),
            e => e == "Format catalogue v2026.11.1 format 'rendered' is rendered and its index entry must name neither an agent nor an op.");
    }

    [Fact]
    public void ACustomFormat_IsNeverACatalogueEntry()
    {
        var files = Files();
        files["formats/rendered.json"] = FormatFixtures.LocalCustom;

        Assert.Contains(Errors(WorkflowFormatCatalogIndexTests.Example, files),
            e => e.StartsWith("Format catalogue v2026.11.1 format 'rendered' declares producer custom, which is never a catalogue entry", StringComparison.Ordinal));
    }

    [Fact]
    public void AParameterFormatReference_MustResolveInTheIndex()
    {
        var files = Files();
        files["formats/terms-subset.json"] = FormatFixtures.TermsSubset.Replace("\"format\": \"concept-list\"", "\"format\": \"concept-set\"");

        var errors = Errors(WorkflowFormatCatalogIndexTests.Example, files);

        Assert.Contains(errors, e => e == "Format catalogue v2026.11.1 format 'terms-subset' parameter 'of' names format 'concept-set', which the catalogue does not publish.");
        Assert.Contains(errors, e => e.Contains("parameter 'in' names format 'concept-set'"));
    }

    [Fact]
    public void EveryDefect_IsListed_AtOnce()
    {
        var files = Files();
        files.Remove("formats/prose.json");
        files["formats/rendered.json"] = FormatFixtures.LocalCustom;
        var index = WorkflowFormatCatalogIndexTests.Example.Replace("\"op\": \"terms-subset\"", "\"op\": \"nope\"");

        var errors = Errors(index, files);

        Assert.Equal(3, errors.Count);
    }
}
