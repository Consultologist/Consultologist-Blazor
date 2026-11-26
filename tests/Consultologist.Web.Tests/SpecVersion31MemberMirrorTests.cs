using System.Linq;
using System.Text.Json;
using ApiModels = Consultologist.Api.Models;
using ApiWorkflow = Consultologist.Api.Workflow;
using WebAI = Consultologist.Web.Services.AI;
using WebWorkflow = Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1509 (specVersion 31, area 12): the Web mirrors the 31 trailing members the
/// Api added in area 11, by hand. This project references both assemblies.
/// </summary>
public class SpecVersion31MemberMirrorTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string[] Members(Type record) =>
        record.GetConstructors().Single(c => c.GetParameters().Length > 0)
            .GetParameters().Select(p => p.Name!).ToArray();

    [Fact]
    public void TheWebInputRecord_MirrorsTheApisTrailingMembers()
    {
        Assert.Equal(
            Members(typeof(ApiWorkflow.WorkflowPackageInputResponse)),
            Members(typeof(WebWorkflow.WorkflowPackageInputResponse)));
    }

    [Fact]
    public void ThePublishResponse_MirrorsTheApisMembers()
    {
        Assert.Equal(
            Members(typeof(ApiWorkflow.WorkflowPackagePublishResponse)),
            Members(typeof(WebWorkflow.WorkflowPackagePublishResponse)));
    }

    [Fact]
    public void TheStartChecksResponse_MirrorsTheApisMembers()
    {
        Assert.Equal(
            Members(typeof(ApiWorkflow.WorkflowPackageStartChecksResponse)),
            Members(typeof(WebWorkflow.WorkflowPackageStartChecksResponse)));
    }

    [Fact]
    public void TheContentResponse_MirrorsTheApisMembers()
    {
        Assert.Equal(
            Members(typeof(ApiWorkflow.WorkflowPackageContentResponse)),
            Members(typeof(WebWorkflow.WorkflowPackageContentResponse)));
    }

    [Fact]
    public void AStartRequest_WithNoSignaturePick_WritesNoSignatureBlockId()
    {
        var json = JsonSerializer.Serialize(new WebAI.ConsultGenerationRequest(null), Web);

        Assert.DoesNotContain("signatureBlockId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AStartRequest_WithAPick_WritesIt()
    {
        var json = JsonSerializer.Serialize(
            new WebAI.ConsultGenerationRequest(null, SignatureBlockId: "sig-2"), Web);

        var received = JsonSerializer.Deserialize<ApiModels.ConsultGenerationRequest>(json, Web)!;
        Assert.Equal("sig-2", received.SignatureBlockId);
    }

    [Fact]
    public void A5To30Response_ReadsFormatAndChannelAsNull()
    {
        var json = JsonSerializer.Serialize(
            new ApiWorkflow.WorkflowPackageInputResponse("note", "Note", true), Web);

        var read = JsonSerializer.Deserialize<WebWorkflow.WorkflowPackageInputResponse>(json, Web)!;
        Assert.Null(read.Format);
        Assert.Null(read.Channel);
    }
}
