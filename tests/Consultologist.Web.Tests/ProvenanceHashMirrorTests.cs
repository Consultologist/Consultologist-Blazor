using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;

namespace Consultologist.Web.Tests;

/// <summary>
/// #402: the browser's recompute and the engine's are two implementations of
/// one published document. The digests here are copied from
/// hash-definitions.md (provenance@v2026.08.2) — the same rows
/// tests/ProvenanceVersionSetTests.cs runs through the engine — so both sides
/// answer to the document, not to each other. (The Web tests do not read the
/// submodule; the copy is deliberate and the comment says where it came from.)
/// </summary>
public class ProvenanceHashMirrorTests
{
    [Fact]
    public void ThePerDocumentHash_IsSha256OfTheText() =>
        Assert.Equal("ce812380ce3cdf680340cb1b7e40d336685f0cc698b10a5e3277ba807361c970", ProvenanceHashes.Sha256Hex("Consultation note"));

    [Fact]
    public void Definition1And3_AreTheMerkleRecipe_OrdinalSorted()
    {
        Assert.Equal("208471a047a8964edc58a50d8317ad24a711e04b59445006ec06e8e44dc38f85",
            ProvenanceHashes.MerkleHash(new Dictionary<string, string> { ["plan"] = "Plan", ["hpi"] = "History" }));
        Assert.Equal("c8f784623550f4da6037fa84eab103b246880be1be5ed5cb8ba061d08c45d6e5",
            ProvenanceHashes.MerkleHash(new Dictionary<string, string> { ["note"] = "Consultation note", ["letter"] = "Patient letter" }));
    }

    [Fact]
    public void Definition2_IsTheDocumentsBytes() =>
        Assert.Equal("480ef298782bf4aab9a0b181ed6e0a22e049c1f0c51d85cf9659ae6220c23149", ProvenanceHashes.Sha256Hex("Consultation note\n"));

    [Fact]
    public void Check_DispatchesByTheRecordsDefinition()
    {
        var v7 = Job(3, "c8f784623550f4da6037fa84eab103b246880be1be5ed5cb8ba061d08c45d6e5", documents: new[]
        {
            new ConsultGenerationResultDocumentResponse("note", "Consultation note", "Consultation note", ProvenanceHashes.Sha256Hex("Consultation note")),
            new ConsultGenerationResultDocumentResponse("letter", "Patient letter", "Patient letter", "not-the-digest")
        });
        var checks = ProvenanceHashes.Check(v7);
        Assert.Equal(new[] { ("workflowOutputHash", true), ("note", true), ("letter", false) }, checks.Select(c => (c.Name, c.Matches)));

        var v6 = Job(2, "480ef298782bf4aab9a0b181ed6e0a22e049c1f0c51d85cf9659ae6220c23149", assembled: "Consultation note\n");
        Assert.True(Assert.Single(ProvenanceHashes.Check(v6)).Matches);

        var v5 = Job(1, "208471a047a8964edc58a50d8317ad24a711e04b59445006ec06e8e44dc38f85", blocks: new Dictionary<string, string> { ["plan"] = "Plan", ["hpi"] = "History" });
        Assert.True(Assert.Single(ProvenanceHashes.Check(v5)).Matches);
    }

    private static ConsultGenerationJobResponse Job(int version, string outputHash,
        IReadOnlyList<ConsultGenerationResultDocumentResponse>? documents = null, string? assembled = null, Dictionary<string, string>? blocks = null) =>
        new("0123456789abcdef0123456789abcdef", "user-1", "Completed", 1, 1, 0, blocks ?? new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            WorkflowOutputHash: outputHash, WorkflowOutputHashVersion: version, AssembledDocument: assembled, AssembledDocuments: documents);
}
