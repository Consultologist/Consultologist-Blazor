using System.Text.Json;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #423: the durable payloads a replay reads, pinned as the JSON Durable
/// stores. Until now no test deserialised a stored orchestration input or
/// activity input at all — the v8 trailing optional was proved only at its
/// two ends. These are the tests that cannot be added later: a job started
/// before a change must replay after it, and the only way to know is to
/// keep the bytes it was started with.
///
/// The Durable isolated worker serialises with System.Text.Json's default
/// JsonDataConverter: PascalCase property names, case-sensitive, nulls
/// written, IncludeFields on, no converter registered — so the
/// [JsonConverter] on ConsultInputValue is honoured. The options below are
/// that converter's, as closely as a test can stand in for it.
/// </summary>
public class DurablePayloadReplayTests
{
    private static readonly JsonSerializerOptions Durable = new() { IncludeFields = true };

    private static readonly WorkflowPromptTemplate Draft = new(
        "draft",
        "Draft from {{ consult_draft }} for {{ section_name }}.",
        new[] { "consult_draft", "section_name" },
        null);

    private static readonly WorkflowPromptTemplate Typed = new(
        "typed",
        "Seen {{ seen_on }}.{{ if billable }} Bill it.{{ end }}",
        new[] { "seen_on", "billable" },
        null);

    [Fact]
    public void AV7ActivityInput_WithoutVariableTypes_Replays()
    {
        // A v5–v7 job's activity input predates VariableTypes: the key is
        // absent from the stored JSON, and the record must bind it to null
        // — the renderer then behaves exactly as it did when the job ran.
        const string stored = """
            {"NodeId":"draft","PromptId":"draft","Variables":{"consult_draft":"Referral text.","section_name":"History"},"WorkflowPackage":"general@v2026.07.4","OutputContract":null,"ConceptSource":null}
            """;

        var input = JsonSerializer.Deserialize<ConsultPromptNodeActivityInput>(stored, Durable)!;

        Assert.Null(input.VariableTypes);
        Assert.Equal("general@v2026.07.4", input.WorkflowPackage);
        Assert.Equal(
            "Draft from Referral text. for History.",
            PromptTemplateRenderer.Render(Draft, input.Variables, input.VariableTypes));
    }

    [Fact]
    public void AV8ActivityInput_WithVariableTypes_ReplaysByteForByte()
    {
        // The v8 shape: every slot present, the trailing optional last. Pinned
        // as bytes, because a slot inserted anywhere but last would rebind
        // every argument after it and this is the payload a sleeping
        // instance re-reads.
        const string stored = """
            {"NodeId":"assemble:hpi","PromptId":"typed","Variables":{"seen_on":"2026-08-10","billable":"true"},"WorkflowPackage":"general@v2026.08.1","OutputContract":null,"ConceptSource":null,"VariableTypes":{"seen_on":"date","billable":"boolean"}}
            """;

        var input = JsonSerializer.Deserialize<ConsultPromptNodeActivityInput>(stored, Durable)!;

        // v10 (#495): the classifier's values are the one slot after it —
        // appended last, so the stored bytes bind unchanged and write back
        // with exactly one more trailing null.
        Assert.Equal(stored[..^1] + ",\"Values\":null}", JsonSerializer.Serialize(input, Durable));
        Assert.Null(input.Values);
        Assert.Equal(
            "Seen 2026-08-10. Bill it.",
            PromptTemplateRenderer.Render(Typed, input.Variables, input.VariableTypes));
    }

    [Fact]
    public void AV8OrchestrationInput_ReplaysByteForByte()
    {
        // A v8 job as Durable stored it: the typed request (a boolean as
        // true, not "true") beside the string resolver map, the declared
        // types, hash definition 4. Nineteen positional slots; anything more
        // belongs at the end or nowhere.
        const string stored = """
            {"Request":{"ConsultDraft":null,"WorkflowPackage":null,"ScheduledAtUtc":null,"Inputs":{"consult_draft":"Referral text.","billable":true},"InputFiles":null},"AppUserId":"user-1","WorkflowPackage":"general@v2026.08.1","EffectiveInputHash":"0000000000000000000000000000000000000000000000000000000000000000","ItemSteps":[{"Id":"hpi","Label":"History"}],"Nodes":null,"ResultNodeId":null,"Items":[{"id":"hpi","name":"History"}],"DataScalars":null,"EffectiveInputHashVersion":4,"InputTypes":{"billable":"boolean"},"SkippedDocuments":null,"CatalogRef":"output-contracts@v2026.07.1","Collections":null,"Source":"app","ReplyToAddress":null,"Results":[{"Id":"consult","NodeId":"assemble-note","Label":"Consultation note"}],"Inputs":{"consult_draft":"Referral text.","billable":"true"},"InputOrigins":null}
            """;

        var input = JsonSerializer.Deserialize<ConsultGenerationOrchestrationInput>(stored, Durable)!;

        // #428 appended the twentieth slot; #398 the twenty-first and -second;
        // #403 the twenty-third and -fourth.
        // The bytes a v8 job was started with are the read side, untouched;
        // what they re-serialise to is those bytes plus exactly the trailing
        // nulls — nothing moved.
        // #496 the twenty-fifth and -sixth (the boundary's flag and inputs).
        Assert.Equal(stored[..^1] + ",\"InputDocumentOrigins\":null,\"PackageFormatRef\":null,\"ProvenanceRef\":null,\"Terminology\":null,\"TerminologyServerRef\":null,\"Deciding\":null,\"SuppliedInputs\":null,\"ApiHost\":null,\"EngineCommit\":null,\"EmailRequested\":null}", JsonSerializer.Serialize(input, Durable));
        Assert.Equal(ConsultInputValue.OfBoolean(true), input.Request.Inputs!["billable"]);
        Assert.Equal("true", input.Inputs!["billable"]);
        Assert.Equal(4, input.EffectiveInputHashVersion);
        Assert.Equal("boolean", input.InputTypes!["billable"]);
    }

    [Fact]
    public void A238OrchestrationInput_WithASingleOrigin_Replays()
    {
        // A job started before #428 with one document: its origin sits in the
        // single-valued slot, and there is no twentieth slot at all. This is
        // the pin that forbids changing the old slot's type in place — a
        // scheduled job sleeps up to seven days on exactly these bytes.
        const string stored = """
            {"Request":{"ConsultDraft":null,"WorkflowPackage":null,"ScheduledAtUtc":null,"Inputs":{"consult_draft":"Referral text."},"InputFiles":null},"AppUserId":"user-1","WorkflowPackage":"general@v2026.08.1","EffectiveInputHash":"0000000000000000000000000000000000000000000000000000000000000000","ItemSteps":null,"Nodes":null,"ResultNodeId":null,"Items":[{"id":"hpi","name":"History"}],"DataScalars":null,"EffectiveInputHashVersion":4,"InputTypes":null,"SkippedDocuments":null,"CatalogRef":null,"Collections":null,"Source":"app","ReplyToAddress":null,"Results":null,"Inputs":{"consult_draft":"Referral text."},"InputOrigins":{"consult_draft":{"Kind":"document","Extractor":"pdfpig/0.1.15","PageCount":3,"TrackedChangesResolved":false}}}
            """;

        var input = JsonSerializer.Deserialize<ConsultGenerationOrchestrationInput>(stored, Durable)!;

        var origin = Assert.Contains("consult_draft", input.InputOrigins!);
        Assert.Equal(new ConsultInputOrigin("document", "pdfpig/0.1.15", 3), origin);
        Assert.Null(input.InputDocumentOrigins);
    }

    [Fact]
    public void AV9OrchestrationInput_WithDocumentOrigins_ReplaysByteForByte()
    {
        // #428: a slot read from two documents — two origins, in order, in
        // the twentieth slot; the single-valued slot null.
        const string stored = """
            {"Request":{"ConsultDraft":null,"WorkflowPackage":null,"ScheduledAtUtc":null,"Inputs":{"prior_notes":["One.","Two."]},"InputFiles":null},"AppUserId":"user-1","WorkflowPackage":"general@v2026.08.1","EffectiveInputHash":"0000000000000000000000000000000000000000000000000000000000000000","ItemSteps":null,"Nodes":null,"ResultNodeId":null,"Items":[{"id":"hpi","name":"History"}],"DataScalars":null,"EffectiveInputHashVersion":5,"InputTypes":{"prior_notes":"array"},"SkippedDocuments":null,"CatalogRef":null,"Collections":null,"Source":"app","ReplyToAddress":null,"Results":null,"Inputs":{"prior_notes":"[\u0022One.\u0022,\u0022Two.\u0022]"},"InputOrigins":null,"InputDocumentOrigins":{"prior_notes":[{"Kind":"document","Extractor":"text/1","PageCount":null,"TrackedChangesResolved":false},{"Kind":"document","Extractor":"pdfpig/0.1.15","PageCount":2,"TrackedChangesResolved":false}]}}
            """;

        var input = JsonSerializer.Deserialize<ConsultGenerationOrchestrationInput>(stored, Durable)!;

        // #398 appended two slots after this payload was frozen: the bytes read
        // as they were, and re-serialise with exactly two trailing nulls.
        // #512 appended two digests to the origin record itself, so each
        // stored origin re-serialises with its own two trailing nulls too.
        var expected = stored
            .Replace("\"TrackedChangesResolved\":false}", "\"TrackedChangesResolved\":false,\"FileSha256\":null,\"TextSha256\":null}", StringComparison.Ordinal)
            .Replace("\"TrackedChangesResolved\":true}", "\"TrackedChangesResolved\":true,\"FileSha256\":null,\"TextSha256\":null}", StringComparison.Ordinal);
        Assert.Equal(expected[..^1] + ",\"PackageFormatRef\":null,\"ProvenanceRef\":null,\"Terminology\":null,\"TerminologyServerRef\":null,\"Deciding\":null,\"SuppliedInputs\":null,\"ApiHost\":null,\"EngineCommit\":null,\"EmailRequested\":null}", JsonSerializer.Serialize(input, Durable));
        var origins = Assert.Contains("prior_notes", input.InputDocumentOrigins!);
        Assert.Equal(2, origins.Count);
        Assert.Equal("pdfpig/0.1.15", origins[1].Extractor);
        Assert.Null(input.InputOrigins);
    }

    [Fact]
    public void AStructuredValue_SurvivesTheOrchestrationPayload()
    {
        // #421 made the typed request carry structure, and the request rides
        // inside the orchestration input. So structure is already durable —
        // what this layer adds is a road to the renderer, not a place to keep
        // it.
        var supplied = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["prior_notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("a"), ConsultInputValue.NullElement }),
            ["patient"] = ConsultInputValue.OfObject(new[] { new ConsultInputEntry("age", ConsultInputValue.OfNumber("1.50")) })
        };
        var input = new ConsultGenerationOrchestrationInput(
            new ConsultGenerationRequest(null, Inputs: supplied),
            "user-1",
            EffectiveInputHashVersion: 5);

        var replayed = JsonSerializer.Deserialize<ConsultGenerationOrchestrationInput>(
            JsonSerializer.Serialize(input, Durable), Durable)!;

        Assert.Equal(supplied["prior_notes"], replayed.Request.Inputs!["prior_notes"]);
        Assert.Equal(supplied["patient"], replayed.Request.Inputs["patient"]);
        Assert.Equal(5, replayed.EffectiveInputHashVersion);
    }

    [Fact]
    public void ACarrierString_ReplaysAsItself()
    {
        // The road: structure travels in Variables as its carrier, and
        // VariableTypes says what it is. The payload's shape is the v8 shape
        // — nothing new to replay. Since #425 the renderer materialises the
        // carrier, so the array iterates rather than rendering as text.
        var notes = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("a"), ConsultInputValue.OfText("b") });
        var input = new ConsultPromptNodeActivityInput(
            "summarise",
            "summarise",
            new Dictionary<string, string> { ["prior_notes"] = notes.AsJson() },
            VariableTypes: new Dictionary<string, string> { ["prior_notes"] = "array" });

        var replayed = JsonSerializer.Deserialize<ConsultPromptNodeActivityInput>(
            JsonSerializer.Serialize(input, Durable), Durable)!;

        Assert.Equal(notes, ConsultInputValue.FromJson(replayed.Variables["prior_notes"]));
        Assert.Equal("array", replayed.VariableTypes!["prior_notes"]);

        var rendered = PromptTemplateRenderer.Render(
            new WorkflowPromptTemplate("summarise", "Notes:{{ for n in prior_notes }} {{ n }}{{ end }}", new[] { "prior_notes" }, null),
            replayed.Variables,
            replayed.VariableTypes);
        Assert.Equal("Notes: a b", rendered);
    }
}
