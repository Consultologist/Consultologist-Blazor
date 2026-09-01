using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>#402: a holder of a record recomputes its hashes by the definitions the record names.</summary>
public class ProvenanceRecordCheckTests
{
    private static ConsultGenerationJobResponse Record(
        string status = "Completed",
        IReadOnlyList<ConsultGenerationResultDocumentResponse>? documents = null,
        string? assembledDocument = null,
        Dictionary<string, string>? blocks = null,
        int? outputVersion = null,
        string? outputHash = null,
        int? inputVersion = null,
        string? inputHash = null) =>
        new("0123456789abcdef0123456789abcdef", "user-1", status, 2, 2, 0,
            blocks ?? new Dictionary<string, string>(), new Dictionary<string, string>(), status == "Completed",
            SchemaVersion: 7,
            EffectiveInputHash: inputHash,
            EffectiveInputHashVersion: inputVersion,
            WorkflowOutputHash: outputHash,
            WorkflowOutputHashVersion: outputVersion,
            AssembledDocument: assembledDocument,
            AssembledDocuments: documents);

    private static ConsultGenerationResultDocumentResponse Doc(string id, string label, string text) =>
        new(id, label, text, ConsultGenerationProvenance.Sha256Hex(text));

    [Fact]
    public void AV7Record_RecomputesFromItself_AndMatches()
    {
        var docs = new[] { Doc("note", "Consultation note", "Consultation note"), Doc("letter", "Patient letter", "Patient letter") };
        var record = Record(documents: docs, outputVersion: 3, outputHash: "c8f784623550f4da6037fa84eab103b246880be1be5ed5cb8ba061d08c45d6e5");

        var checks = ProvenanceRecordCheck.Check(record);

        Assert.Equal(3, checks.Count);
        Assert.All(checks, c => Assert.True(c.Matches));
        Assert.Equal("workflowOutputHash", checks[0].Name);
        Assert.Equal(3, checks[0].Definition);
        Assert.Equal("assembledDocuments[note].documentHash", checks[1].Name);
    }

    [Fact]
    public void AnAlteredDeliverable_FailsItsOwnDigest_AndTheOutputHash_ByName()
    {
        var docs = new[]
        {
            Doc("note", "Consultation note", "Consultation note"),
            new ConsultGenerationResultDocumentResponse("letter", "Patient letter", "Patient letter, altered.", ConsultGenerationProvenance.Sha256Hex("Patient letter"))
        };
        var record = Record(documents: docs, outputVersion: 3, outputHash: "c8f784623550f4da6037fa84eab103b246880be1be5ed5cb8ba061d08c45d6e5");

        var checks = ProvenanceRecordCheck.Check(record);

        Assert.False(checks.Single(c => c.Name == "workflowOutputHash").Matches);
        Assert.True(checks.Single(c => c.Name == "assembledDocuments[note].documentHash").Matches);
        Assert.False(checks.Single(c => c.Name == "assembledDocuments[letter].documentHash").Matches);
    }

    [Fact]
    public void OlderRecords_RecomputeByTheirOwnDefinition()
    {
        var v6 = Record(assembledDocument: "Consultation note\n", outputVersion: 2, outputHash: "480ef298782bf4aab9a0b181ed6e0a22e049c1f0c51d85cf9659ae6220c23149");
        Assert.True(ProvenanceRecordCheck.OutputHash(v6).Matches);

        var v5 = Record(blocks: new Dictionary<string, string> { ["plan"] = "Plan", ["hpi"] = "History" }, outputVersion: 1,
            outputHash: "208471a047a8964edc58a50d8317ad24a711e04b59445006ec06e8e44dc38f85");
        Assert.True(ProvenanceRecordCheck.OutputHash(v5).Matches);

        var partial = Record(status: "Failed");
        var check = ProvenanceRecordCheck.OutputHash(partial);
        Assert.Null(check.Matches);
        Assert.Contains("undefined", check.Note);
    }

    [Fact]
    public void TheInputHash_RecomputesByTheRecordsDefinition_OverWhatTheHolderSupplies()
    {
        var structured = new Dictionary<string, ConsultInputValue>
        {
            ["patient"] = ConsultInputValue.OfObject(new[] { new ConsultInputEntry("name", ConsultInputValue.OfText("Ada")), new ConsultInputEntry("age", ConsultInputValue.OfNumber("36")) }),
            ["notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("x"), ConsultInputValue.OfText("y") }),
            ["accent"] = ConsultInputValue.OfText("café")
        };
        Assert.True(ProvenanceRecordCheck.InputHash(Record(inputVersion: 5, inputHash: "52593837462725201bb86daf11e60f1aee9374ec207aaf234457c4713835032b"), structured, null).Matches);

        var typed = new Dictionary<string, ConsultInputValue> { ["reason"] = ConsultInputValue.OfText("follow-up"), ["billable"] = ConsultInputValue.OfBoolean(true) };
        Assert.True(ProvenanceRecordCheck.InputHash(Record(inputVersion: 4, inputHash: "cba13032e0d21f1098f9c60b8253ace104e399ff0a9c0ded9a26a356ce172756"), typed, null).Matches);

        var text = new Dictionary<string, ConsultInputValue> { ["b"] = ConsultInputValue.OfText("two"), ["a"] = ConsultInputValue.OfText("one") };
        Assert.True(ProvenanceRecordCheck.InputHash(Record(inputVersion: 3, inputHash: "8f770258ab53f8b20001e6ba82ae42d66479db3053a3b74776bafa2a92674514"), text, null).Matches);

        Assert.True(ProvenanceRecordCheck.InputHash(Record(inputVersion: 2, inputHash: "b18720c3b2a3f220df2570021d79cb18ceaa4b1531ea0d1ea1ef9f91bb4e5c79"), null, "Hello").Matches);

        // A wrong recipe is a mismatch, said plainly — never a false match:
        // definition 3 hashed "true" as text; checked as 4 over a boolean the
        // bytes differ, which is the whole reason 4 exists.
        var asText = ConsultGenerationProvenance.ComputeDeclaredInputsHash(new Dictionary<string, string> { ["billable"] = "true" });
        var asBoolean = new Dictionary<string, ConsultInputValue> { ["billable"] = ConsultInputValue.OfBoolean(true) };
        Assert.False(ProvenanceRecordCheck.InputHash(Record(inputVersion: 4, inputHash: asText), asBoolean, null).Matches);
    }

    [Fact]
    public void WhatCannotBeRecomputed_SaysWhy()
    {
        Assert.Contains("historical", ProvenanceRecordCheck.InputHash(Record(inputVersion: 1, inputHash: "x"), null, null).Note);
        Assert.Contains("--inputs", ProvenanceRecordCheck.InputHash(Record(inputVersion: 5, inputHash: "x"), null, null).Note);
        Assert.Contains("--draft", ProvenanceRecordCheck.InputHash(Record(inputVersion: 2, inputHash: "x"), null, null).Note);

        var structure = new Dictionary<string, ConsultInputValue> { ["notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("x") }) };
        var refused = ProvenanceRecordCheck.InputHash(Record(inputVersion: 4, inputHash: "x"), structure, null);
        Assert.Null(refused.Matches);
        Assert.Contains("definition 5", refused.Note);
    }

    // ----- #574: definition 6, the arm #499 forgot -----

    /// <summary>
    /// A v10 map with a NESTED value — what distinguishes the right primitive
    /// from every wrong dispatch: definition 4 refuses structure outright,
    /// and 5 agrees with 6 only on flat maps.
    /// </summary>
    private static Dictionary<string, ConsultInputValue> NestedInputs() => new(StringComparer.Ordinal)
    {
        ["consult_draft"] = ConsultInputValue.OfText("The referral text."),
        ["context"] = ConsultInputValue.OfObject(new[]
        {
            new ConsultInputEntry("site", ConsultInputValue.OfText("clinic-a")),
            new ConsultInputEntry("billable", ConsultInputValue.OfBoolean(true))
        })
    };

    [Fact]
    public void ADefinition6Record_RecomputesFromSuppliedNestedInputs_AndMatches()
    {
        var inputs = NestedInputs();
        var record = Record(inputVersion: 6, inputHash: ConsultGenerationProvenance.ComputeNestedInputsHash(inputs));

        var check = ProvenanceRecordCheck.InputHash(record, inputs, null);

        Assert.Equal(6, check.Definition);
        Assert.True(check.Matches);
        Assert.Null(check.Note);
    }

    [Fact]
    public void ADefinition6Record_WithDifferentInputs_SaysMismatch()
    {
        var record = Record(inputVersion: 6, inputHash: "not-the-digest");

        var check = ProvenanceRecordCheck.InputHash(record, NestedInputs(), null);

        Assert.False(check.Matches);
    }

    [Fact]
    public void ADefinition6Record_WithoutInputs_AsksForThem_NeverAFalseVerdict()
    {
        var check = ProvenanceRecordCheck.InputHash(Record(inputVersion: 6, inputHash: "x"), null, null);

        Assert.Null(check.Matches);
        Assert.Contains("--inputs", check.Note);
    }
}
