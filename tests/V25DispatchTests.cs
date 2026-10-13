using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>B4 #1003: a 25 node's with and onInvalid resolved at job start; the computed activity; the custom stamp on the producer; validation on the record.</summary>
public class V25DispatchTests
{
    private static readonly WorkflowFormatCatalog Formats = V25Package.Catalog();

    private static ConsultNodeDescriptor Describe(string nodeId, Func<WorkflowPackageManifest, WorkflowPackageManifest>? change = null, IReadOnlyDictionary<string, string>? inputs = null)
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = change?.Invoke(manifest) ?? manifest;
        var node = manifest.Nodes!.First(n => n.Id == nodeId);
        return ConsultGenerationJobStarter.DescribeNode(node, null, macroFiles: files, formats: Formats, effectiveInputs: inputs, manifest: manifest);
    }

    [Fact]
    public void AClassificationNode_CarriesItsValues_FromWith()
    {
        var scope = Describe("scope");

        Assert.Equal(new[] { "in_scope", "out_of_scope" }, scope.Values);
        Assert.Null(scope.With);
        Assert.Equal("fail", scope.OnInvalid);
    }

    [Fact]
    public void AClassificationNode_CarriesItsValues_FromAnEnumInputReference()
    {
        var scope = Describe("scope", m => V25Package.WithNode(m, "scope", n => n with { With = new Dictionary<string, JsonNode?> { ["values"] = "input.kind" } }));

        Assert.Equal(new[] { "new", "follow_up" }, scope.Values);
    }

    [Fact]
    public void AComputedNode_CarriesItsOperandsBare_AndItsTextLiteral()
    {
        var coverage = Describe("coverage");

        Assert.Equal("computed", coverage.Producer);
        Assert.Equal("terms-subset", coverage.Op);
        Assert.Null(coverage.PromptId);
        Assert.Equal(new Dictionary<string, string> { ["of"] = "extract", ["in"] = "extract", ["failWith"] = "The note names problems the source does not." }, coverage.With);
        Assert.Null(coverage.Values);
    }

    [Fact]
    public void ATextParameter_ReferencingAnInput_ResolvesToItsValue()
    {
        var coverage = Describe("coverage",
            m => V25Package.WithNode(m, "coverage", n => n with { With = new Dictionary<string, JsonNode?> { ["of"] = "node.extract", ["in"] = "node.extract", ["failWith"] = "input.notes" } }),
            new Dictionary<string, string> { ["notes"] = "Referral text." });

        Assert.Equal("Referral text.", coverage.With!["failWith"]);
    }

    [Fact]
    public void ACustomNode_IsStamped_WithItsFormatHash_AndTheGenericAgent()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Formats = new List<WorkflowFormatReference> { new("mine", "formats/mine.json") },
            Nodes = manifest.Nodes!.Select(n => n.Id == "assessment" ? n with { Format = "mine", OnInvalid = "flag" } : n).ToList(),
        };
        files = new Dictionary<string, string>(files, StringComparer.Ordinal) { ["formats/mine.json"] = FormatFixtures.LocalCustom };
        var node = ConsultGenerationJobStarter.DescribeNode(manifest.Nodes!.First(n => n.Id == "assessment"), null, macroFiles: files, formats: Formats, manifest: manifest);

        Assert.Equal("custom", node.Producer);
        Assert.Equal("flag", node.OnInvalid);
        Assert.Equal("custom-json", node.AgentName);
        var stamp = ConsultGenerationOrchestrator.CustomStamp(node);
        Assert.True(stamp.Unattested);
        Assert.Equal("custom-json@1", stamp.CustomAgent);
        Assert.Null(node.OutputContract);
    }

    [Fact]
    public void AnAttestedNode_IsNotStamped()
    {
        var stamp = ConsultGenerationOrchestrator.CustomStamp(Describe("assessment"));

        Assert.Null(stamp.Unattested);
        Assert.Null(stamp.CustomAgent);
    }

    [Fact]
    public void TheUpdates_CarryTheValidation_AndTheNodeUpdate_TheCheck()
    {
        var node = Describe("coverage");
        var validation = new ConsultValidationOutcome(false, new[] { "'$' lacks required property 'pass'." });
        var check = new ConsultCheckOutcome(false, new[] { "Asthma" }, null);
        var result = new NodeRunResult("{}", null, "i", "o", 5, Validation: validation, Check: check);

        var update = ConsultGenerationOrchestrator.NodeUpdateFrom(node, result, 1, 2);
        var item = ConsultGenerationOrchestrator.ItemUpdateFrom(node, "hpi", "HPI", result, 1, 2);

        Assert.Same(validation, update.Validation);
        Assert.Same(check, update.Check);
        Assert.Same(validation, item.Validation);
    }

    private const string Concepts = """{ "concepts": [ { "term": "Asthma", "type": "disorder", "id": "195967001", "isSnomedConcept": true, "isActive": true, "support": null }, { "term": "Cough", "type": "finding", "id": "49727002", "isSnomedConcept": true, "isActive": true, "support": null } ] }""";
    private const string Fewer = """{ "concepts": [ { "term": "Asthma", "type": "disorder", "id": "195967001", "isSnomedConcept": true, "isActive": true, "support": null } ] }""";

    private static ConsultComputedNodeActivityInput Computed(string of, string inDocument) =>
        new("coverage", "terms-subset",
            new Dictionary<string, string> { ["of"] = "extract", ["in"] = "source", ["failWith"] = "x" },
            new Dictionary<string, string> { ["extract"] = of, ["source"] = inDocument },
            "terms-subset", "hash");

    [Fact]
    public void TermsSubset_Passes_WhenEveryCodableTermIsCovered()
    {
        var result = RunComputedNodeActivity.Compute(Computed(Fewer, Concepts));

        Assert.Equal("""{"pass":true,"uncovered":[],"untested":[]}""", result.RawOutput);
        Assert.True(result.Check!.Passed);
        Assert.True(result.Validation!.Valid);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex($"{Fewer}\n{Concepts}"), result.InputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(result.RawOutput), result.OutputHash);
    }

    [Fact]
    public void TermsSubset_Fails_NamingTheUncoveredTerms()
    {
        var result = RunComputedNodeActivity.Compute(Computed(Concepts, Fewer));

        Assert.Equal("""{"pass":false,"uncovered":["Cough"],"untested":[]}""", result.RawOutput);
        Assert.False(result.Check!.Passed);
        Assert.Equal(new[] { "Cough" }, result.Check.Uncovered);
    }

    [Fact]
    public void AnUnknownOperation_OrAMissingOperand_FailsWithoutRetry()
    {
        var unknown = Assert.Throws<InvalidOperationException>(() => RunComputedNodeActivity.Compute(Computed(Fewer, Concepts) with { Op = "nope" }));
        Assert.Equal("Computed node 'coverage' declares operation 'nope', which this engine does not implement.", unknown.Message);

        var missing = Assert.Throws<InvalidOperationException>(() => RunComputedNodeActivity.Compute(Computed(Fewer, Concepts) with { NodeTexts = new Dictionary<string, string> { ["extract"] = Fewer } }));
        Assert.Equal("Computed node 'coverage' reads node 'source' for parameter 'in', whose output the payload does not carry.", missing.Message);
    }
}
