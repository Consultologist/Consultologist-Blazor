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
        // #510: the request inside the payload gained InputRefs, appended last.
        // v11 #513: each stored result descriptor re-serialises with its own
        // trailing null too — Macros, appended last on the record it rides.
        var withRefs = stored
            .Replace("\"InputFiles\":null}", "\"InputFiles\":null,\"InputRefs\":null,\"InputFormRefs\":null,\"MacroChoices\":null}", StringComparison.Ordinal)
            .Replace("\"Label\":\"Consultation note\"}", "\"Label\":\"Consultation note\",\"Macros\":null,\"Signature\":null,\"MacroPlacements\":null,\"Check\":null}", StringComparison.Ordinal);
        Assert.Equal(withRefs[..^1] + ",\"InputDocumentOrigins\":null,\"PackageFormatRef\":null,\"ProvenanceRef\":null,\"Terminology\":null,\"TerminologyServerRef\":null,\"Deciding\":null,\"SuppliedInputs\":null,\"ApiHost\":null,\"EngineCommit\":null,\"EmailRequested\":null,\"MacroTexts\":null,\"ProfileName\":null,\"Signature\":null,\"AccountKind\":null,\"MacroChoices\":null}", JsonSerializer.Serialize(input, Durable));
        // v11 #513/#516: the macro and signature slots and the descriptor's
        // trailing fields bind null.
        Assert.Null(input.MacroTexts);
        Assert.Null(input.ProfileName);
        Assert.Null(input.Signature);
        Assert.Null(input.Results![0].Macros);
        Assert.Null(input.Results[0].Signature);
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
            .Replace("\"InputFiles\":null}", "\"InputFiles\":null,\"InputRefs\":null,\"InputFormRefs\":null,\"MacroChoices\":null}", StringComparison.Ordinal)
            .Replace("\"TrackedChangesResolved\":false}", "\"TrackedChangesResolved\":false,\"FileSha256\":null,\"TextSha256\":null,\"SourceJobId\":null,\"SourceResultId\":null,\"SourceFormId\":null,\"SourceResponseId\":null}", StringComparison.Ordinal)
            .Replace("\"TrackedChangesResolved\":true}", "\"TrackedChangesResolved\":true,\"FileSha256\":null,\"TextSha256\":null,\"SourceJobId\":null,\"SourceResultId\":null,\"SourceFormId\":null,\"SourceResponseId\":null}", StringComparison.Ordinal);
        Assert.Equal(expected[..^1] + ",\"PackageFormatRef\":null,\"ProvenanceRef\":null,\"Terminology\":null,\"TerminologyServerRef\":null,\"Deciding\":null,\"SuppliedInputs\":null,\"ApiHost\":null,\"EngineCommit\":null,\"EmailRequested\":null,\"MacroTexts\":null,\"ProfileName\":null,\"Signature\":null,\"AccountKind\":null,\"MacroChoices\":null}", JsonSerializer.Serialize(input, Durable));
        var origins = Assert.Contains("prior_notes", input.InputDocumentOrigins!);
        Assert.Equal(2, origins.Count);
        Assert.Equal("pdfpig/0.1.15", origins[1].Extractor);
        Assert.Null(input.InputOrigins);
    }

    [Fact]
    public void AStoredFinalize_WithoutAccountKind_BindsNull()
    {
        // #557: completed sleeping jobs replay this payload — the kind binds
        // null and re-serialises with exactly one more trailing null; the
        // writer's rule then falls to personal.
        const string stored = """
            {"Status":"Completed","Error":null}
            """;

        var finalize = JsonSerializer.Deserialize<ConsultGenerationJobFinalize>(stored, Durable)!;

        Assert.Null(finalize.AccountKind);
        Assert.Equal(stored[..^1] + ",\"AccountKind\":null}", JsonSerializer.Serialize(finalize, Durable));
    }

    [Fact]
    public void AStoredRequest_WithoutMacroChoices_BindsNull()
    {
        // v12 #618: the request is itself a durable payload (it rides the
        // orchestration input) — a stored pre-v12 request binds the choices
        // null and re-serialises with exactly one more trailing null.
        const string stored = """
            {"ConsultDraft":null,"WorkflowPackage":null,"ScheduledAtUtc":null,"Inputs":null,"InputFiles":null,"InputRefs":null,"InputFormRefs":null}
            """;

        var request = JsonSerializer.Deserialize<Consultologist.Api.Models.ConsultGenerationRequest>(stored, Durable)!;

        Assert.Null(request.MacroChoices);
        Assert.Equal(stored[..^1] + ",\"MacroChoices\":null}", JsonSerializer.Serialize(request, Durable));
    }

    [Fact]
    public void AStoredInitialize_WithoutMacroChoices_BindsNull()
    {
        // v12 #618: Initialize's slot count is large, so the pre-v12 bytes
        // are produced by stripping exactly the new trailing slot from a
        // fresh serialization — which also proves the slot IS trailing: a
        // mid-list insertion would leave the strip target unmatched.
        var current = JsonSerializer.Serialize(
            new ConsultGenerationJobInitialize("job-1", "user-1", new List<IReadOnlyDictionary<string, string>>()), Durable);
        Assert.EndsWith(",\"MacroChoices\":null,\"ExcludedMacros\":null}", current);
        var stored = current.Replace(",\"MacroChoices\":null,\"ExcludedMacros\":null}", "}", StringComparison.Ordinal);

        var initialize = JsonSerializer.Deserialize<ConsultGenerationJobInitialize>(stored, Durable)!;

        Assert.Null(initialize.MacroChoices);
        Assert.Equal(current, JsonSerializer.Serialize(initialize, Durable));
    }

    [Fact]
    public void AStoredDecision_WithoutExcludedMacros_BindsNull()
    {
        // v12 #631: the boundary's decision signal is a durable payload — a
        // stored pre-(i) decision binds the exclusions null and re-serialises
        // with exactly one more trailing null (which also proves the slot IS
        // trailing).
        var current = JsonSerializer.Serialize(
            new ConsultGenerationDecision(
                new List<IReadOnlyDictionary<string, string>>(), null, null, null, null, null, null,
                new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero)), Durable);
        Assert.EndsWith(",\"ExcludedMacros\":null}", current);
        var stored = current.Replace(",\"ExcludedMacros\":null}", "}", StringComparison.Ordinal);

        var decision = JsonSerializer.Deserialize<ConsultGenerationDecision>(stored, Durable)!;

        Assert.Null(decision.ExcludedMacros);
        Assert.Equal(current, JsonSerializer.Serialize(decision, Durable));
    }

    [Fact]
    public void AStoredDecisionResult_WithoutExcludedMacros_BindsNull()
    {
        // v12 #631: the activity's recorded result, same rule.
        var current = JsonSerializer.Serialize(
            new ConsultDecisionResult(
                Array.Empty<Consultologist.Api.Models.ConsultResultDescriptor>(),
                Array.Empty<Consultologist.Api.Models.ConsultSkippedDocument>(),
                Array.Empty<Consultologist.Api.Models.ConsultNodeDescriptor>(),
                Array.Empty<IReadOnlyDictionary<string, string>>(),
                new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(),
                Array.Empty<Consultologist.Api.Models.ConsultCollectionRoster>(),
                Array.Empty<Consultologist.Api.Models.ConsultItemStepDescriptor>(),
                Array.Empty<string>()), Durable);
        Assert.EndsWith(",\"ExcludedMacros\":null}", current);
        var stored = current.Replace(",\"ExcludedMacros\":null}", "}", StringComparison.Ordinal);

        var result = JsonSerializer.Deserialize<ConsultDecisionResult>(stored, Durable)!;

        Assert.Null(result.ExcludedMacros);
        Assert.Equal(current, JsonSerializer.Serialize(result, Durable));
    }

    [Fact]
    public void AStoredDescriptor_WithoutPlacements_BindsNull()
    {
        // v12 #619: a rung-b descriptor with a non-null macro list re-reads
        // with placements null and re-serialises with exactly one more
        // trailing null — the parallel slot, never a reshape of Macros.
        const string stored = """
            {"Id":"consult","NodeId":"assemble-note","Label":"Consultation note","Macros":["closing"],"Signature":true}
            """;

        var descriptor = JsonSerializer.Deserialize<ConsultResultDescriptor>(stored, Durable)!;

        Assert.Null(descriptor.MacroPlacements);
        Assert.Equal(new[] { "closing" }, descriptor.Macros);
        Assert.Equal(stored[..^1] + ",\"MacroPlacements\":null,\"Check\":null}", JsonSerializer.Serialize(descriptor, Durable));
    }

    [Fact]
    public void AStoredNodeRunResult_WithoutTokens_BindsNull()
    {
        // #551: the file's first activity-OUTPUT pin — NodeRunResult is
        // recorded in the Durable history and re-read on every replay, so a
        // mid-flight job's stored result must bind the new trailing field as
        // null and re-serialise with exactly one more trailing null. (The
        // record gained HashVersion and Classification with only a weaker
        // read-side pin behind them; this closes that gap.)
        const string stored = """
            {"RawOutput":"x","Concepts":null,"InputHash":"a","OutputHash":"b","HashVersion":5,"Classification":null}
            """;

        var result = JsonSerializer.Deserialize<NodeRunResult>(stored, Durable)!;

        Assert.Null(result.Tokens);
        Assert.Equal(stored[..^1] + ",\"Tokens\":null}", JsonSerializer.Serialize(result, Durable));
    }

    [Fact]
    public void AStoredResultDocument_WithoutAppended_BindsNull()
    {
        // v11 #513: the entity payload every pre-macro job sends — Appended
        // binds null and re-serialises with exactly one more trailing null.
        // The pin that cannot be added later: completed jobs re-read exactly
        // these bytes.
        const string stored = """
            {"ResultId":"note","Label":"Consultation note","Text":"Consultation note","Ordinal":0}
            """;

        var document = JsonSerializer.Deserialize<ConsultGenerationResultDocument>(stored, Durable)!;

        Assert.Null(document.Appended);
        Assert.Equal(stored[..^1] + ",\"Appended\":null,\"Unsigned\":null}", JsonSerializer.Serialize(document, Durable));
    }

    [Fact]
    public void AStoredAppendedEntry_WithoutAsOf_BindsNull()
    {
        // v11 #516: rung-b jobs already stored macro entries with two fields;
        // AsOf binds null and re-serialises with exactly one trailing null.
        const string stored = """
            {"Kind":"macro","Id":"disclaimer"}
            """;

        var entry = JsonSerializer.Deserialize<ConsultAppendedEntry>(stored, Durable)!;

        Assert.Null(entry.AsOf);
        Assert.Equal(stored[..^1] + ",\"AsOf\":null}", JsonSerializer.Serialize(entry, Durable));
    }

    [Fact]
    public void AStoredNodeDescriptor_WithoutReproducible_BindsNull()
    {
        // v11 #550: node snapshots stored before the flag existed —
        // Reproducible binds null and re-serialises with exactly one more
        // trailing null. The pin that cannot be added later: this is the
        // snapshot a sleeping job re-reads.
        const string stored = """
            {"Id":"scope","Label":"Scope","PromptId":"classify","Bindings":null,"OutputContract":"classification.v1","FailIfEmpty":null,"ForEach":null,"ConceptSource":"scope","Aggregate":null,"Values":["in_scope","out_of_scope"]}
            """;

        var node = JsonSerializer.Deserialize<ConsultNodeDescriptor>(stored, Durable)!;

        Assert.Null(node.Reproducible);
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, node.Values);
        Assert.Equal(stored[..^1] + ",\"Reproducible\":null,\"Check\":null}", JsonSerializer.Serialize(node, Durable));
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
