using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Engine;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

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

    // ---- #1509 (Task 6): record storage 12 ----

    private sealed record Sealed(string Document, Dictionary<string, string?> Texts);

    private static async Task<Sealed> SealedRecord()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var document = await job.Blobs.ReadRecordAsync(V31Job.AccountKind, "user-1", V31Job.JobId, CancellationToken.None)
            ?? throw new Xunit.Sdk.XunitException("No record.json was written.");
        var texts = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var d in JsonNode.Parse(document)!["deliverables"]!.AsArray())
        {
            var key = (string)d!["key"]!;
            var text = await job.Blobs.ReadTextAsync(V31Job.AccountKind, "user-1", V31Job.JobId, key, CancellationToken.None);
            texts[key] = SourceLayout.Text(text?["content"]);
        }

        return new Sealed(document, texts);
    }

    private static ConsultGenerationJobResponse Header12(string document, int? inputVersion = 7, string? inputHash = null) =>
        Record(inputVersion: inputVersion, inputHash: inputHash) with { SchemaVersion = 12, RecordHash = RecordDocument.Hash(document) };

    private static string Changed(string document, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(document)!.AsObject();
        change(node);
        return CanonicalJson.Write(node);
    }

    private static JsonObject FirstDeliverable(JsonObject document) => document["deliverables"]!.AsArray().First(d => d is JsonObject)!.AsObject();

    private static IReadOnlyList<HashCheck> CheckIt(string document, ConsultGenerationJobResponse header, IReadOnlyDictionary<string, string?> texts) =>
        ProvenanceRecordCheck.CheckRecord(header, Encoding.UTF8.GetBytes(document), texts);

    [Fact]
    public async Task ASealedRecord_ChecksOut()
    {
        var sealedRecord = await SealedRecord();
        Assert.NotEmpty(sealedRecord.Texts);

        var checks = CheckIt(sealedRecord.Document, Header12(sealedRecord.Document), sealedRecord.Texts);

        Assert.Equal("recordHash", checks[0].Name);
        Assert.All(checks, c => Assert.True(c.Matches, $"{c.Name}: {c.Note}"));
        Assert.Equal(1 + 2 * sealedRecord.Texts.Count, checks.Count);
    }

    [Fact]
    public async Task AByteChanged_FailsRecordHash()
    {
        var sealedRecord = await SealedRecord();
        var header = Header12(sealedRecord.Document);
        var altered = sealedRecord.Document.Replace("\"status\"", "\"statuS\"", StringComparison.Ordinal);
        Assert.NotEqual(sealedRecord.Document, altered);

        var checks = CheckIt(altered, header, sealedRecord.Texts);

        var line = checks.Single(c => c.Name == "recordHash");
        Assert.False(line.Matches);
        Assert.Equal(header.RecordHash, line.Recorded);
        Assert.Equal(RecordDocument.Hash(altered), line.Recomputed);
    }

    [Fact]
    public async Task ADeliverablesTextChanged_FailsComposedHash_AndNamesTheDeliverableKey()
    {
        var sealedRecord = await SealedRecord();
        var key = sealedRecord.Texts.Keys.First();
        var texts = new Dictionary<string, string?>(sealedRecord.Texts) { [key] = sealedRecord.Texts[key] + " (edited)" };

        var checks = CheckIt(sealedRecord.Document, Header12(sealedRecord.Document), texts);

        var failed = Assert.Single(checks, c => c.Matches == false);
        Assert.Contains(key, failed.Name, StringComparison.Ordinal);
        Assert.Contains("composedHash", failed.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASubstitutionMoved_FailsTheReplay_WithRecordReplaysFault()
    {
        var sealedRecord = await SealedRecord();
        var key = sealedRecord.Texts.Keys.First();
        var text = sealedRecord.Texts[key]!;
        // one substitution over the text's first five runes, its hash right; then moved one rune along
        var hash = CanonicalJson.HashText(string.Concat(text.EnumerateRunes().Take(5).Select(r => r.ToString())));
        string With(int start) => Changed(sealedRecord.Document, d =>
            FirstDeliverable(d)["substitutions"] = new JsonArray(new JsonObject { ["start"] = start, ["size"] = 5, ["hash"] = hash }));
        var good = With(0);
        var moved = With(1);

        Assert.All(CheckIt(good, Header12(good), sealedRecord.Texts), c => Assert.True(c.Matches, $"{c.Name}: {c.Note}"));
        var checks = CheckIt(moved, Header12(moved), sealedRecord.Texts);

        var failed = Assert.Single(checks, c => c.Matches == false);
        Assert.Contains("replay", failed.Name, StringComparison.Ordinal);
        Assert.True(RecordReplay.Verify(text, (JsonArray)JsonNode.Parse($"[{{\"start\":1,\"size\":5,\"hash\":\"{hash}\"}}]")!, out var fault) is false);
        Assert.Equal(fault, failed.Note);
    }

    [Fact]
    public async Task DroppedText_IsNotAFailure()
    {
        var sealedRecord = await SealedRecord();
        var key = sealedRecord.Texts.Keys.First();
        var texts = new Dictionary<string, string?>(sealedRecord.Texts) { [key] = null };

        var checks = CheckIt(sealedRecord.Document, Header12(sealedRecord.Document), texts);

        Assert.DoesNotContain(checks, c => c.Matches == false);
        var line = Assert.Single(checks, c => c.Name.Contains(key, StringComparison.Ordinal) && c.Matches == null);
        Assert.Contains("text deleted; hash kept", line.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Definition7_WithInputs_IsRecomputed()
    {
        var inputs = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["history"] = ConsultInputValue.OfText("Margaret Okafor, 61."),
            ["urgent"] = ConsultInputValue.OfBoolean(true),
        };
        var expected = Consultologist.Api.V31.V31JobStarter.EffectiveInputHash(new Dictionary<string, JsonNode?>
        {
            ["history"] = JsonValue.Create("Margaret Okafor, 61."),
            ["urgent"] = JsonValue.Create(true),
        });

        var good = ProvenanceRecordCheck.InputHash(Record(inputVersion: 7, inputHash: expected), inputs, null);
        var bad = ProvenanceRecordCheck.InputHash(Record(inputVersion: 7, inputHash: "sha256:00"), inputs, null);

        Assert.True(good.Matches);
        Assert.Equal(expected, good.Recomputed);
        Assert.False(bad.Matches);
    }

    [Fact]
    public void Definition7_WithoutInputs_AsksForThem()
    {
        var check = ProvenanceRecordCheck.InputHash(Record(inputVersion: 7, inputHash: "sha256:00"), null, null);

        Assert.Null(check.Matches);
        Assert.Contains("--inputs", check.Note);
    }

    [Fact]
    public async Task AStorage12Record_WithoutItsBytes_SaysWhereToFetchThem()
    {
        var sealedRecord = await SealedRecord();

        var checks = ProvenanceRecordCheck.Check(Header12(sealedRecord.Document));

        var line = Assert.Single(checks);
        Assert.Null(line.Matches);
        Assert.Equal("storage 12 is checked through the record: fetch /Record", line.Note);
    }
}
