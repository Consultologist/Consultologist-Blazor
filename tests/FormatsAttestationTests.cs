using Consultologist.Api.Agents;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>B2 (#1001): the startup attestation's registry leg for the formats catalogue — runtime must equal the bundled git copy.</summary>
public class FormatsAttestationTests
{
    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal)
    {
        ["formats/prose.json"] = FormatFixtures.Prose,
        ["formats/classification.json"] = FormatFixtures.Classification,
        ["formats/concept-list.json"] = FormatFixtures.ConceptList,
        ["formats/rendered.json"] = FormatFixtures.Rendered,
        ["formats/terms-subset.json"] = FormatFixtures.TermsSubset,
    };

    private static WorkflowFormatCatalog Build(string index, Dictionary<string, string>? files = null)
    {
        var errors = new List<string>();
        var catalog = WorkflowFormatCatalog.Build(WorkflowFormatCatalogIndexTests.Read(index), path => (files ?? Files).GetValueOrDefault(path), errors);
        Assert.Empty(errors);
        return catalog!;
    }

    [Fact]
    public void IdenticalCatalogues_Agree()
    {
        Assert.Empty(AgentAttestationService.CompareFormatsToBundled(Build(WorkflowFormatCatalogIndexTests.Example), Build(WorkflowFormatCatalogIndexTests.Example)));
    }

    [Fact]
    public void TheBundledCatalogue_AgreesWithItself()
    {
        Assert.Empty(AgentAttestationService.CompareFormatsToBundled(TestFormats.Catalog, FormatCatalogLoader.Load(TestFormats.AgentsDirectory())));
    }

    [Fact]
    public void AVersionDrift_IsNamed()
    {
        var mismatches = AgentAttestationService.CompareFormatsToBundled(
            Build(WorkflowFormatCatalogIndexTests.Example.Replace("v2026.11.1", "v2026.12.1")),
            Build(WorkflowFormatCatalogIndexTests.Example));

        Assert.Contains("formats: runtime formats@v2026.12.1 != bundled formats@v2026.11.1 (publish/pin/deploy drift)", mismatches);
    }

    [Fact]
    public void ABindingDrift_IsNamed()
    {
        var mismatches = AgentAttestationService.CompareFormatsToBundled(
            Build(WorkflowFormatCatalogIndexTests.Example.Replace("\"agentVersion\": 3", "\"agentVersion\": 4")),
            Build(WorkflowFormatCatalogIndexTests.Example));

        Assert.Contains("formats: format 'prose' binds consult-prose@4 (runtime) != consult-prose@3 (bundled)", mismatches);
    }

    [Fact]
    public void AFileDrift_IsNamed()
    {
        var drifted = new Dictionary<string, string>(Files, StringComparer.Ordinal) { ["formats/prose.json"] = FormatFixtures.Prose.Replace("Consult prose.", "Prose.") };

        var mismatches = AgentAttestationService.CompareFormatsToBundled(Build(WorkflowFormatCatalogIndexTests.Example, drifted), Build(WorkflowFormatCatalogIndexTests.Example));

        Assert.Contains("formats: format 'prose' file differs between the runtime and bundled catalogues", mismatches);
    }

    [Fact]
    public void AMissingFormat_IsNamed()
    {
        var fewer = WorkflowFormatCatalogIndexTests.Example.Replace("\"rendered\":       { \"file\": \"formats/rendered.json\" },\n", string.Empty);
        Assert.NotEqual(WorkflowFormatCatalogIndexTests.Example, fewer);

        var mismatches = AgentAttestationService.CompareFormatsToBundled(Build(fewer), Build(WorkflowFormatCatalogIndexTests.Example));

        Assert.Contains("formats: format 'rendered' is not in the runtime catalogue", mismatches);
    }

    [Fact]
    public void ACustomProducerDrift_IsNamed()
    {
        var mismatches = AgentAttestationService.CompareFormatsToBundled(
            Build(WorkflowFormatCatalogIndexTests.Example.Replace("\"custom-json\", \"agentVersion\": 1", "\"custom-json\", \"agentVersion\": 2")),
            Build(WorkflowFormatCatalogIndexTests.Example));

        Assert.Contains(mismatches, m => m.StartsWith("formats: producers.custom custom-json@2 (runtime) != custom-json@1 (bundled)", StringComparison.Ordinal));
    }
}
