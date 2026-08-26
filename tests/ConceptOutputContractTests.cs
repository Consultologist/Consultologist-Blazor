using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

public class ConceptOutputContractTests
{
    [Fact]
    public void Deserialize_MapsSchemaConformantOutput()
    {
        const string json = """
            {
              "concepts": [
                { "term": "Adenocarcinoma of prostate", "type": "disorder", "id": "399490008",
                  "isSnomedConcept": true, "isActive": true, "support": "newly diagnosed prostate adenocarcinoma" },
                { "term": "Retired concept", "type": "disorder", "id": "12345",
                  "isSnomedConcept": true, "isActive": false, "support": null },
                { "term": "Strong family support", "type": "finding", "id": null,
                  "isSnomedConcept": false, "isActive": false, "support": null }
              ]
            }
            """;

        var concepts = ConceptOutputContract.Deserialize(json, "patient");

        Assert.Equal(3, concepts.Count);
        Assert.Equal("Adenocarcinoma of prostate", concepts[0].Term);
        Assert.Equal("399490008", concepts[0].Id);
        Assert.True(concepts[0].IsSnomedConcept);
        Assert.True(concepts[0].IsActive);
        Assert.Equal("newly diagnosed prostate adenocarcinoma", concepts[0].Support);
        Assert.All(concepts, c => Assert.Equal("patient", c.Source));

        Assert.False(concepts[1].IsActive);
        Assert.Null(concepts[1].Support);

        // Null id (unmapped finding) preserves the ClinicalConcept empty-string
        // invariant the text renderers rely on.
        Assert.Equal(string.Empty, concepts[2].Id);
        Assert.False(concepts[2].IsSnomedConcept);
    }

    [Fact]
    public void Deserialize_EmptyConceptListIsValid()
    {
        var concepts = ConceptOutputContract.Deserialize("""{ "concepts": [] }""", "problem");

        Assert.Empty(concepts);
    }

    [Theory]
    [InlineData("- Adenocarcinoma of prostate (disorder) - 399490008")] // legacy bullet output
    [InlineData("{ not json")]
    [InlineData("""{ "something": [] }""")]
    public void Deserialize_ThrowsContractExceptionOnMalformedOutput(string payload)
    {
        var ex = Assert.ThrowsAny<Exception>(() => ConceptOutputContract.Deserialize(payload, "patient"));

        // Must be the retryable contract exception — never InvalidOperationException,
        // which the Durable retry policy treats as a non-retryable config error.
        Assert.IsType<ConceptOutputContractException>(ex);
    }

    // The former AgentManifestSchema_MatchesEngineContract pin moved to
    // OutputContractCatalogTests.Load_RealCatalog_ConceptListSchemaMatchesAgentManifest:
    // the schema's source of truth is the catalog file, not an engine constant.

    // #245: the model's raw output is clinical text; a malformed list must not
    // quote it in the exception that reaches the log, the job and Durable history.
    [Fact]
    public void MalformedJson_NamesThePosition_NeverTheText()
    {
        const string sentinel = "SENTINEL-CLINICAL-CONTENT-0f1e2d";
        var json = "{\"concepts\": [ {\"term\": \"" + sentinel + "\" ] }";

        var ex = Assert.Throws<ConceptOutputContractException>(() => ConceptOutputContract.Deserialize(json, "test"));

        Assert.DoesNotContain(sentinel, ex.Message, StringComparison.Ordinal);
        Assert.Contains("line ", ex.Message);
        Assert.Contains("byte ", ex.Message);
    }
}
