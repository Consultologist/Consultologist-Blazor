using System.Text.Json;
using ApiModels = Consultologist.Api.Models;
using WebAI = Consultologist.Web.Services.AI;

namespace Consultologist.Web.Tests;

/// <summary>
/// #428: the request's document map and the response's origin map are
/// hand-mirrored on the client, and this layer changed both shapes — slot →
/// list of documents, slot → list of origins. This project references both
/// assemblies, which is what makes the mirror provable (the pattern of
/// ConsultInputValueMirrorTests).
/// </summary>
public class ProvenanceMirrorTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(typeof(ApiModels.ConsultInputOrigin), typeof(WebAI.ConsultInputOrigin))]
    [InlineData(typeof(ApiModels.InputFilePayload), typeof(WebAI.InputFilePayload))]
    [InlineData(typeof(ApiModels.ConsultGenerationNodeStatusResponse), typeof(WebAI.ConsultGenerationNodeStatus))]
    public void TheMirroredRecords_ExposeTheSameProperties(Type api, Type web)
    {
        static IEnumerable<string> Shape(Type type) =>
            type.GetProperties().Select(p => $"{p.Name}:{p.PropertyType}").Order();

        Assert.Equal(Shape(api), Shape(web));
    }

    [Fact]
    public void TwoOriginsForOneSlot_ReachTheClientInOrder()
    {
        var response = new ApiModels.ConsultGenerationJobResponse(
            "job-1", "user-1", "Completed", 1, 1, 0,
            new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            InputOrigins: new Dictionary<string, IReadOnlyList<ApiModels.ConsultInputOrigin>>
            {
                ["prior_notes"] = new[]
                {
                    new ApiModels.ConsultInputOrigin(ApiModels.ConsultInputOriginKinds.Document, "text/1"),
                    new ApiModels.ConsultInputOrigin(ApiModels.ConsultInputOriginKinds.Document, "pdfpig/0.1.15", 2, TrackedChangesResolved: true)
                }
            });

        var mirrored = JsonSerializer.Deserialize<WebAI.ConsultGenerationJobResponse>(
            JsonSerializer.Serialize(response, Web), Web)!;

        var origins = Assert.Contains("prior_notes", mirrored.InputOrigins!);
        Assert.Equal(2, origins.Count);
        Assert.Equal("text/1", origins[0].Extractor);
        Assert.Equal(new WebAI.ConsultInputOrigin("document", "pdfpig/0.1.15", 2, true), origins[1]);
    }

    [Fact]
    public void TheRegistryRefs_ReachTheClient()
    {
        // #398: the two trailing refs, through the wire into the mirror.
        var response = new ApiModels.ConsultGenerationJobResponse(
            "job-1", "user-1", "Completed", 1, 1, 0,
            new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            PackageFormatRef: "package-format@v2026.08.6",
            ProvenanceRef: "provenance@v2026.08.4");

        var mirrored = JsonSerializer.Deserialize<WebAI.ConsultGenerationJobResponse>(
            JsonSerializer.Serialize(response, Web), Web)!;

        Assert.Equal("package-format@v2026.08.6", mirrored.PackageFormatRef);
        Assert.Equal("provenance@v2026.08.4", mirrored.ProvenanceRef);
    }

    [Fact]
    public void ThePackageTitle_ReachesTheClient()
    {
        // #432: the job response's trailing title, through the wire into the
        // hand-mirrored record.
        var response = new ApiModels.ConsultGenerationJobResponse(
            "job-1", "user-1", "Completed", 1, 1, 0,
            new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            PackageSpecVersion: 9,
            PackageTitle: "Breast oncology consults");

        var mirrored = JsonSerializer.Deserialize<WebAI.ConsultGenerationJobResponse>(
            JsonSerializer.Serialize(response, Web), Web)!;

        Assert.Equal("Breast oncology consults", mirrored.PackageTitle);
        Assert.Equal(9, mirrored.PackageSpecVersion);
    }

    [Fact]
    public void ThePackageTags_ReachTheClient_InOrder_AndEmptyStaysEmpty()
    {
        // #453: the trailing tags, through the wire. An empty list is a stated
        // none and must not arrive as null.
        var tagged = new ApiModels.ConsultGenerationJobResponse(
            "job-1", "user-1", "Completed", 1, 1, 0,
            new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            PackageSpecVersion: 9,
            PackageTags: new[] { "oncology", "Breast" });
        var none = tagged with { PackageTags = Array.Empty<string>() };

        var mirroredTagged = JsonSerializer.Deserialize<WebAI.ConsultGenerationJobResponse>(JsonSerializer.Serialize(tagged, Web), Web)!;
        var mirroredNone = JsonSerializer.Deserialize<WebAI.ConsultGenerationJobResponse>(JsonSerializer.Serialize(none, Web), Web)!;

        Assert.Equal(new[] { "oncology", "Breast" }, mirroredTagged.PackageTags);
        Assert.NotNull(mirroredNone.PackageTags);
        Assert.Empty(mirroredNone.PackageTags!);
    }

    [Fact]
    public void TwoDocumentsForOneSlot_ReachTheServerInOrder()
    {
        var request = new WebAI.ConsultGenerationRequest(
            null,
            InputFiles: new Dictionary<string, List<WebAI.InputFilePayload>>
            {
                ["prior_notes"] = new()
                {
                    new("text/plain", "One."u8.ToArray()),
                    new("application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 })
                }
            });

        var received = JsonSerializer.Deserialize<ApiModels.ConsultGenerationRequest>(
            JsonSerializer.Serialize(request, Web), Web)!;

        var documents = Assert.Contains("prior_notes", received.InputFiles!);
        Assert.Equal(2, documents.Count);
        Assert.Equal("One.", System.Text.Encoding.UTF8.GetString(documents[0].Content));
        Assert.Equal("application/pdf", documents[1].ContentType);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, documents[1].Content);
    }
}
