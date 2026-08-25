using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #400: the provenance record contract and the hash definitions are a
/// published artifact — consultologist-provenance's provenance-versions.json
/// and hash-definitions.md, vendored here as a submodule. These hold the
/// engine's own numbers against that document, and recompute the document's
/// worked examples through the engine's own code, so the published prose and
/// the bytes the engine hashes cannot silently disagree. Read off the
/// submodule, not the registry, for the reason SpecVersionSetTests gives.
/// </summary>
public class ProvenanceVersionSetTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private static string Vendored(string file)
    {
        var path = Path.Combine(RepoRoot(), "external", "consultologist-provenance", file);
        Assert.True(File.Exists(path), $"{path} is missing — the submodule is not checked out (git submodule update --init).");
        return File.ReadAllText(path);
    }

    private static JsonNode Index() => JsonNode.Parse(Vendored("provenance-versions.json"))!;

    private static int[] Ladder(string name) => Index()[name]!.AsArray().Select(v => (int)v!).ToArray();

    [Fact]
    public void TheRecordStorageVersions_AreThePublishedOnes()
    {
        Assert.Equal(Ladder("recordStorageVersions"), ConsultGenerationJobState.StorageVersions);
    }

    [Fact]
    public void TheEffectiveInputLadder_IsThePublishedOne()
    {
        // 1 is historical and 2 has no named constant (ComputeDraftOnlyHash);
        // 3, 4 and 5 are the engine's constants.
        Assert.Equal(
            new[] { 1, 2, ConsultGenerationProvenance.DeclaredInputsHashVersion, ConsultGenerationProvenance.TypedInputsHashVersion, ConsultGenerationProvenance.StructuredInputsHashVersion },
            Ladder("effectiveInputHashVersions"));
    }

    [Fact]
    public void TheWorkflowOutputLadder_IsThePublishedOne()
    {
        Assert.Equal(
            new[] { ConsultGenerationProvenance.WorkflowOutputHashVersion, ConsultGenerationProvenance.AssembledDocumentHashVersion, ConsultGenerationProvenance.ResultSetHashVersion },
            Ladder("workflowOutputHashVersions"));
    }

    [Fact]
    public void ThePerNodeHash_IsPublishedAsUnversioned()
    {
        // #375: a stated fact, not an omission. When the ladder gets its first
        // number, this test and the document move together.
        Assert.Empty(Ladder("nodeHashVersions"));
    }

    [Fact]
    public void EveryDocumentTheIndexNames_IsVendored()
    {
        foreach (var (_, file) in Index()["documents"]!.AsObject())
        {
            Vendored((string)file!);
        }
    }

    // The worked examples, recomputed through the engine — the digests below
    // are copied from hash-definitions.md, and the bytes are what the engine
    // produces, so a definition that drifts from its prose fails here.

    [Fact]
    public void Definition2_DraftOnly() =>
        Assert.Equal("b18720c3b2a3f220df2570021d79cb18ceaa4b1531ea0d1ea1ef9f91bb4e5c79",
            ConsultGenerationProvenance.ComputeDraftOnlyHash(new ConsultGenerationRequest("Hello")));

    [Fact]
    public void Definition3_SortedTextMap_EscapesNonAscii()
    {
        Assert.Equal("8f770258ab53f8b20001e6ba82ae42d66479db3053a3b74776bafa2a92674514",
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(new Dictionary<string, string> { ["b"] = "two", ["a"] = "one" }));
        Assert.Equal("3849b4da05d4d8716eca76c57d1d952e687bd164ef2e3dd53e31b1f1666ca979",
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(new Dictionary<string, string> { ["accent"] = "café" }));
    }

    [Fact]
    public void Definition4_TypedScalars() =>
        Assert.Equal("cba13032e0d21f1098f9c60b8253ace104e399ff0a9c0ded9a26a356ce172756",
            ConsultGenerationProvenance.ComputeTypedInputsHash(new Dictionary<string, ConsultInputValue>
            {
                ["reason"] = ConsultInputValue.OfText("follow-up"),
                ["billable"] = ConsultInputValue.OfBoolean(true)
            }));

    [Fact]
    public void Definition5_StructuredValues_Utf8AsIs() =>
        Assert.Equal("52593837462725201bb86daf11e60f1aee9374ec207aaf234457c4713835032b",
            ConsultGenerationProvenance.ComputeStructuredInputsHash(new Dictionary<string, ConsultInputValue>
            {
                ["patient"] = ConsultInputValue.OfObject(new[]
                {
                    new ConsultInputEntry("name", ConsultInputValue.OfText("Ada")),
                    new ConsultInputEntry("age", ConsultInputValue.OfNumber("36"))
                }),
                ["notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("x"), ConsultInputValue.OfText("y") }),
                ["accent"] = ConsultInputValue.OfText("café")
            }));

    [Fact]
    public void OutputDefinitions_1_2_3_AndTheAggregateInput()
    {
        Assert.Equal("208471a047a8964edc58a50d8317ad24a711e04b59445006ec06e8e44dc38f85",
            ConsultGenerationProvenance.ComputeWorkflowOutputHash(new Dictionary<string, string> { ["plan"] = "Plan", ["hpi"] = "History" }));
        Assert.Equal("480ef298782bf4aab9a0b181ed6e0a22e049c1f0c51d85cf9659ae6220c23149",
            ConsultGenerationProvenance.ComputeAssembledDocumentHash("Consultation note\n"));
        Assert.Equal("c8f784623550f4da6037fa84eab103b246880be1be5ed5cb8ba061d08c45d6e5",
            ConsultGenerationProvenance.ComputeResultSetHash(new Dictionary<string, string> { ["note"] = "Consultation note", ["letter"] = "Patient letter" }));
        Assert.Equal("d8429debeb9facdd005d84147a126b01bbb0b5ea60944dad1b96ee1bd2d73c8d",
            ConsultGenerationProvenance.ComputeAggregateInputHash(new[]
            {
                ConsultGenerationProvenance.Sha256Hex("Consultation note"),
                ConsultGenerationProvenance.Sha256Hex("Patient letter")
            }));
        Assert.Equal("185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969", ConsultGenerationProvenance.Sha256Hex("Hello"));
    }
}
