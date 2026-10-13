using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// B2 (#1001, design § 3.7): a 25 node's format identity — id, producer, the
/// file's canonical hash, the agent pin or the op — resolved at job-start from
/// the package's OWN stamped formats version and threaded onto the descriptor;
/// and the activity's refusal to fall back to the engine's current catalogue
/// for such a node.
/// </summary>
public class DescribeNodeFormatTests
{
    private static readonly WorkflowFormatCatalog Formats = V25Package.Catalog();
    private static readonly OutputContractCatalog Contracts = OutputContractCatalog.Load(TestFormats.AgentsDirectory());

    private static IReadOnlyDictionary<string, WorkflowStampedFormat> Stamped()
    {
        var (manifest, files) = V25Package.Minimal();
        var errors = new List<string>();
        var stamp = WorkflowPackageStamp.Compute(manifest, files, Contracts, Formats, errors);
        Assert.Empty(errors);
        return stamp.Formats!;
    }

    [Fact]
    public void AnAttestedNode_CarriesTheStampedAgentHashAndProducer()
    {
        var node = new WorkflowNodeSpec("assessment", "Assessment", Prompt: "assessment", Format: "prose");

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, null, formats: Formats, stampedFormats: Stamped());

        Assert.Equal("prose", descriptor.Format);
        Assert.Equal("attested", descriptor.Producer);
        Assert.Equal("consult-prose", descriptor.AgentName);
        Assert.Equal("3", descriptor.AgentVersion);
        Assert.Equal(WorkflowFormatSpecJson.Sha256Hex(Formats.Formats["prose"].CanonicalJson), descriptor.FormatHash);
        Assert.Null(descriptor.Op);
        Assert.Null(descriptor.OutputContract);
        Assert.Null(descriptor.Template);
    }

    [Fact]
    public void AComputedNode_CarriesTheOp_AndNoAgent()
    {
        var node = new WorkflowNodeSpec("coverage", "Coverage", Format: "terms-subset");

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, null, formats: Formats, stampedFormats: Stamped());

        Assert.Equal("computed", descriptor.Producer);
        Assert.Equal("terms-subset", descriptor.Op);
        Assert.Null(descriptor.AgentName);
        Assert.Null(descriptor.PromptId);
    }

    [Fact]
    public void ARenderedNode_IsATemplate_WithNoAgent()
    {
        var node = new WorkflowNodeSpec("letter", "Letter", Prompt: "letter", Format: "rendered");

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, null, formats: Formats);

        Assert.Equal("rendered", descriptor.Producer);
        Assert.True(descriptor.Template);
        Assert.Null(descriptor.AgentName);
    }

    [Fact]
    public void ALocalCustomFormat_RunsTheGenericAgent()
    {
        var node = new WorkflowNodeSpec("meds", "Meds", Prompt: "meds", Format: "medication_list", OnInvalid: "fail");
        var stamped = new Dictionary<string, WorkflowStampedFormat>
        {
            ["medication_list"] = new(new string('c', 64), "custom", "custom-json", 1),
        };

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, null, formats: Formats, stampedFormats: stamped);

        Assert.Equal("custom", descriptor.Producer);
        Assert.Equal("custom-json", descriptor.AgentName);
        Assert.Equal("1", descriptor.AgentVersion);
        Assert.Equal(new string('c', 64), descriptor.FormatHash);
    }

    [Fact]
    public void WithoutAStamp_TheCatalogueResolves_AndALocalFormatIsCustom()
    {
        var attested = ConsultGenerationJobStarter.DescribeNode(new WorkflowNodeSpec("x", "X", Prompt: "p", Format: "classification"), null, formats: Formats);
        Assert.Equal("classification", attested.AgentName);
        Assert.Equal("attested", attested.Producer);

        var local = ConsultGenerationJobStarter.DescribeNode(new WorkflowNodeSpec("y", "Y", Prompt: "p", Format: "mine"), null, formats: Formats);
        Assert.Equal("custom", local.Producer);
        Assert.Equal("custom-json", local.AgentName);
        Assert.Null(local.FormatHash);
    }

    [Fact]
    public void WithoutAnyCatalogue_A25Node_IsRefused()
    {
        var node = new WorkflowNodeSpec("x", "X", Prompt: "p", Format: "prose");

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultGenerationJobStarter.DescribeNode(node, null));

        Assert.Equal("Node 'x' declares format 'prose', but the package carries no formats catalogue to resolve it against.", ex.Message);
    }

    [Fact]
    public void A24Node_IsDescribedAsBefore()
    {
        var node = new WorkflowNodeSpec("prose", "Prose", Prompt: "p");

        var descriptor = ConsultGenerationJobStarter.DescribeNode(node, null, formats: Formats);

        Assert.Null(descriptor.Format);
        Assert.Null(descriptor.Producer);
        Assert.Null(descriptor.FormatHash);
    }

    // ----- the activity's fallback ----------------------------------------------------

    private static ConsultPromptNodeActivityInput Input(string? agentName, string? agentVersion, string? format) =>
        new("n", "p", new Dictionary<string, string>(), AgentName: agentName, AgentVersion: agentVersion, Format: format);

    [Fact]
    public void AThreadedPin_IsUsed()
    {
        Assert.Equal(("test-json", "48"), RunPromptNodeActivity.ResolveAgentPin(Input("test-json", "48", "prose"), Contracts));
    }

    [Fact]
    public void A24Payload_WithoutAPin_FallsBackToTheCatalogue()
    {
        var (name, version) = RunPromptNodeActivity.ResolveAgentPin(Input(null, null, null), Contracts);

        Assert.Equal(Contracts.GetEntry("text").AgentName, name);
        Assert.Equal(Contracts.GetEntry("text").AgentVersion, version);
    }

    [Fact]
    public void A25Node_WithoutAPin_Refuses_RatherThanResolvingTheCurrentCatalogue()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => RunPromptNodeActivity.ResolveAgentPin(Input(null, null, "prose"), Contracts));

        Assert.StartsWith("Node 'n' is a specVersion 25 node of format 'prose' and carries no agent pin; a 25 node runs the agent its package was published against, never the engine's current catalogue.", ex.Message);
    }
}
