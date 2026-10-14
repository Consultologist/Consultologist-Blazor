using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>B4 #1003: the one dispatch on the producer — what is sent and to whom, and how the reply settles under onInvalid.</summary>
public class ConsultNodeDispatchTests
{
    private static readonly OutputContractCatalog Contracts = OutputContractCatalog.Load(TestFormats.AgentsDirectory());
    private static readonly WorkflowFormatCatalog Engine = TestFormats.Catalog;

    private static (WorkflowPackage Package, ConsultPromptNodeActivityInput Input) At25(string nodeId, Func<WorkflowPackageManifest, WorkflowPackageManifest>? change = null, Func<Dictionary<string, string>, Dictionary<string, string>>? files = null, string? onInvalid = null, WorkflowFormatCatalog? formats = null)
    {
        var package = V25JobStartTests.Package(change, files);

        if (formats is not null)
        {
            // The fixture catalogue is the design's example; the bundled one
            // carries the published agents and shapes.
            package = package with { Formats = formats, FormatsRef = formats.ResolvedRef };
        }

        var node = ConsultGenerationJobStarter.DescribeNode(
            package.Nodes!.First(n => n.Id == nodeId), null, macroFiles: package.SourceFiles, formats: package.Formats,
            effectiveInputs: new Dictionary<string, string>(), manifest: package.Manifest);
        var input = new ConsultPromptNodeActivityInput(
            node.Id, node.PromptId ?? "none", new Dictionary<string, string>(), package.Ref,
            null, node.ConceptSource, Values: node.Values, AgentName: node.AgentName, AgentVersion: node.AgentVersion,
            Format: node.Format, Producer: node.Producer, FormatHash: node.FormatHash, Op: node.Op,
            With: node.With, OnInvalid: onInvalid ?? node.OnInvalid);

        return (package, input);
    }

    private static ConsultNodeDispatch.DispatchPlan Plan((WorkflowPackage Package, ConsultPromptNodeActivityInput Input) at, string rendered = "Rendered.") =>
        ConsultNodeDispatch.Plan(at.Input, at.Package, rendered, Contracts, Engine);

    [Fact]
    public void AnAttestedEnum_IsTrailedWithItsValues_AndNormalises()
    {
        var plan = Plan(At25("scope"));

        Assert.Equal(ConsultNodeDispatch.ReplyKind.Enum, plan.Kind);
        Assert.Equal("Rendered.\n\nAnswer with exactly one of: in_scope, out_of_scope.", plan.Sent);
        Assert.Equal(("classification", "1"), (plan.AgentName, plan.AgentVersion));

        var result = ConsultNodeDispatch.Settle(plan, "scope", """{ "value": "In_Scope " }""", "i", null);

        Assert.Equal("in_scope", result.Classification);
        Assert.True(result.Validation!.Valid);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("in_scope"), result.OutputHash);
        // B5 #1004 (design § 6): the reply's own digest is over the bytes as
        // returned, beside the normalised answer's; a 25 pair stamps 6.
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("""{ "value": "In_Scope " }"""), result.AgentOutputHash);
        Assert.Equal(ConsultGenerationProvenance.NodeHashVersionAt25, result.HashVersion);
    }

    [Fact]
    public void AnAttestedObject_IsSentBare_ValidatedAgainstItsSchema_AndConceptsFill()
    {
        var plan = Plan(At25("extract", formats: TestFormats.Catalog));
        const string reply = "```json\n{ \"concepts\": [ { \"term\": \"Asthma\", \"type\": \"disorder\", \"id\": \"195967001\", \"isSnomedConcept\": true, \"isActive\": true, \"support\": null } ] }\n```";

        Assert.Equal(ConsultNodeDispatch.ReplyKind.Structured, plan.Kind);
        Assert.Equal("Rendered.", plan.Sent);
        Assert.True(plan.ConceptList);
        Assert.Equal(("concept-extraction", "1"), (plan.AgentName, plan.AgentVersion));

        var result = ConsultNodeDispatch.Settle(plan, "extract", reply, "i", null);

        Assert.True(result.Validation!.Valid);
        Assert.StartsWith("{ \"concepts\"", result.RawOutput, StringComparison.Ordinal);
        Assert.Equal("Asthma", Assert.Single(result.Concepts!).Term);
        Assert.Equal("extract", result.Concepts![0].Source);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(result.RawOutput), result.OutputHash);
        // B5 #1004: agentOutputHash is taken BEFORE the fences are stripped —
        // the two digests differ exactly when something was stripped.
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(reply), result.AgentOutputHash);
        Assert.NotEqual(result.OutputHash, result.AgentOutputHash);
        Assert.Equal(ConsultGenerationProvenance.NodeHashVersionAt25, result.HashVersion);
    }

    [Fact]
    public void AnAttestedText_IsSentBare_AndNotValidated()
    {
        var plan = Plan(At25("assessment"));

        Assert.Equal(ConsultNodeDispatch.ReplyKind.Text, plan.Kind);
        Assert.Equal(("consult-prose", "3"), (plan.AgentName, plan.AgentVersion));

        var result = ConsultNodeDispatch.Settle(plan, "assessment", "The plan.", "i", null);

        Assert.Null(result.Validation);
        Assert.Equal("The plan.", result.RawOutput);
        // B5 #1004: nothing stripped, nothing composed here — one digest, twice.
        Assert.Equal(result.OutputHash, result.AgentOutputHash);
        Assert.Equal(ConsultGenerationProvenance.NodeHashVersionAt25, result.HashVersion);
    }

    private static (WorkflowPackage, ConsultPromptNodeActivityInput) Custom(string onInvalid = "fail") => At25(
        "assessment",
        m => m with
        {
            Formats = new List<WorkflowFormatReference> { new("mine", "formats/mine.json") },
            // The closing macro moves to a prose node: an object format places none.
            Nodes = m.Nodes!.Select(n => n.Id switch
            {
                "assessment" => n with { Format = "mine", OnInvalid = onInvalid, Macros = null },
                "section_notes" => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After) } },
                _ => n,
            }).ToList(),
        },
        f => { f["formats/mine.json"] = FormatFixtures.LocalCustom; return f; });

    [Fact]
    public void ACustomNode_SendsPromptTrailerAndSchema_ToTheGenericAgent()
    {
        var (package, input) = Custom();
        var spec = ConsultGenerationJobStarter.FormatSpecFor("mine", package.Formats, package.SourceFiles, package.Manifest)!;

        var plan = ConsultNodeDispatch.Plan(input, package, "Rendered.", Contracts, Engine);

        Assert.Equal(WorkflowCustomPrompt.Compose("Rendered.", "Answer with one JSON object conforming to this schema.", spec.Schema!.ToJsonString()), plan.Sent);
        Assert.Equal(("custom-json", "1"), (plan.AgentName, plan.AgentVersion));
        Assert.Equal(WorkflowFormatSpecJson.Sha256Hex(WorkflowFormatSpecJson.CanonicalJson(spec)), input.FormatHash);
    }

    [Fact]
    public void ACustomNode_WhoseLoadedFormatDiffersFromTheStamp_IsRefused()
    {
        var (package, input) = Custom();

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultNodeDispatch.Plan(input with { FormatHash = "stamped" }, package, "x", Contracts, Engine));

        Assert.Contains("the package's format changed under the job", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidReply_UnderFail_FailsWithoutRetry_NamingThePathsOnly()
    {
        var (package, input) = Custom();
        var plan = ConsultNodeDispatch.Plan(input, package, "x", Contracts, Engine);

        var ex = Assert.Throws<ConsultNodeInvalidReplyException>(() => ConsultNodeDispatch.Settle(plan, "assessment", "{ \"extra\": \"PATIENT TEXT\" }", "i", null));

        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.DoesNotContain("PATIENT TEXT", ex.Message, StringComparison.Ordinal);
        Assert.StartsWith("Node 'assessment' reply does not conform to format 'mine' (onInvalid: fail):", ex.Message);
        Assert.Contains(ex.Errors, e => e.Contains("undeclared property 'extra'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnInvalidReply_UnderFlag_IsKept_WithItsValidationRow()
    {
        var (package, input) = Custom("flag");
        var plan = ConsultNodeDispatch.Plan(input, package, "x", Contracts, Engine);

        var result = ConsultNodeDispatch.Settle(plan, "assessment", "not json at all", "i", null);

        Assert.False(result.Validation!.Valid);
        Assert.StartsWith("the reply is not JSON:", Assert.Single(result.Validation.Errors));
        Assert.Equal("not json at all", result.RawOutput);
    }

    [Fact]
    public void TheReservedRetry_RunsAsFail()
    {
        var (package, input) = Custom("retry");
        var plan = ConsultNodeDispatch.Plan(input, package, "x", Contracts, Engine);

        Assert.Throws<ConsultNodeInvalidReplyException>(() => ConsultNodeDispatch.Settle(plan, "assessment", "{}", "i", null));
    }

    [Fact]
    public void AnEnumMiss_IsUnderThePolicyToo()
    {
        var plan = Plan(At25("scope", onInvalid: "flag"));

        var result = ConsultNodeDispatch.Settle(plan, "scope", """{ "value": "maybe" }""", "i", null);

        Assert.Null(result.Classification);
        Assert.False(result.Validation!.Valid);

        var failing = Plan(At25("scope"));
        Assert.Throws<ConsultNodeInvalidReplyException>(() => ConsultNodeDispatch.Settle(failing, "scope", """{ "value": "maybe" }""", "i", null));
    }

    [Fact]
    public void AComputedOrRenderedNode_ReachingThePromptActivity_IsRefused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Plan(At25("coverage")));

        Assert.Equal("Prompt node 'coverage' has producer 'computed', which runs no model and must not reach the prompt activity.", ex.Message);
    }

    [Fact]
    public void A22EraCustomNode_TakesTheGenericPath_WithTheEnginesCatalogue()
    {
        // #760 retired: the package's own schema into the prompt, the engine's
        // pinned catalogue's trailer and generic agent, validated by the engine, policy fail.
        var (manifest, files) = V22Fixtures.Custom();
        var package = new WorkflowPackage(manifest, Nodes: manifest.Nodes, SourceFiles: files,
            SchemaContracts: new Dictionary<string, string> { ["my-note"] = OutputContracts.Custom });
        var node = ConsultGenerationJobStarter.DescribeNode(manifest.Nodes!.First(n => n.Id == "create-typical-trajectory"), package.SchemaContracts, formats: Engine);
        var input = new ConsultPromptNodeActivityInput(node.Id, node.PromptId!, new Dictionary<string, string>(), package.Ref,
            node.OutputContract, node.ConceptSource, AgentName: node.AgentName, AgentVersion: node.AgentVersion);

        Assert.True(ConsultNodeDispatch.Applies(input));
        var plan = ConsultNodeDispatch.Plan(input, package, "Rendered.", Contracts, Engine);

        var schema = JsonNode.Parse(files[V22Fixtures.CustomSchemaPath])!.ToJsonString();
        Assert.Equal(WorkflowCustomPrompt.Compose("Rendered.", Engine.CustomProducer.Trailer, schema), plan.Sent);
        Assert.Equal(("custom-json", "1"), (plan.AgentName, plan.AgentVersion));
        Assert.Equal(WorkflowOnInvalid.Fail, plan.OnInvalid);
        Assert.Throws<ConsultNodeInvalidReplyException>(() => ConsultNodeDispatch.Settle(plan, node.Id, "{}", "i", null));
    }

    [Fact]
    public void Below25_NothingButCustom_Applies()
    {
        Assert.False(ConsultNodeDispatch.Applies(new ConsultPromptNodeActivityInput("n", "p", new Dictionary<string, string>(), OutputContract: OutputContracts.ConceptList)));
        Assert.False(ConsultNodeDispatch.Applies(new ConsultPromptNodeActivityInput("n", "p", new Dictionary<string, string>())));
    }
}
