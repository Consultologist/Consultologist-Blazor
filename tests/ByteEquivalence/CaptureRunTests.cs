using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498: the faithful runner. A scenario file is read whole or refused by name; a
/// <see cref="CaptureRun"/> drives the real starter, entity and orchestrator with the
/// production activities' pure halves in place of the model, so what it captures — the
/// text each agent node is SENT, trailers included, the stubs it served, the deliverables'
/// final text — is what today's engine does with those replies. A pin that never runs and
/// a generated stub the engine flags are refused; a job that fails is a result.
/// </summary>
public class CaptureRunTests
{
    private const string Notes = "Margaret Okafor, 58, referred for fatigue and a raised TSH (fictional).";

    private const string Signature = "Dr A. Sample\nInternal Medicine";

    /// <summary>A scenario over the minimal's two inputs, with <paramref name="outputs"/> pinned.</summary>
    private static CaptureScenario Scenario(string outputs = "{}", string? signature = null) => CaptureScenario.Read($$"""
        {
          "description": ["The 30 minimal, its stubs generated."],
          "inputs": { "notes": {{JsonSerializer.Serialize(Notes)}}, "kind": "new" },
          "profile": { "name": "Dr A. Sample", "signature": {{(signature is null ? "null" : JsonSerializer.Serialize(signature))}} },
          "macroChoices": {},
          "outputs": {{outputs}}
        }
        """);

    /// <summary>The 30 minimal, changed, loaded as the fixtures load a package.</summary>
    private static WorkflowPackage Minimal(Action<JsonObject>? change = null, Action<Dictionary<string, string>>? files = null)
    {
        var (json, bundle) = V30Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        change?.Invoke(root);
        files?.Invoke(bundle);
        return ByteEquivalenceFixtures.Load("general", root.ToJsonString(WorkflowPackageManifestJson.WriteOptions), bundle);
    }

    [Fact]
    public void AScenario_ReadsEveryMember_AndRefusesAMissingOne()
    {
        var scenario = Scenario("""{ "plan": { "sections": [] }, "draft_problem:1": "A pinned draft." }""");

        Assert.Equal(["The 30 minimal, its stubs generated."], scenario.Description);
        Assert.Equal(Notes, scenario.Inputs["notes"]!.GetValue<string>());
        Assert.Equal(new CaptureProfile("Dr A. Sample", null), scenario.Profile);
        Assert.Empty(scenario.MacroChoices);
        Assert.Equal("A pinned draft.", scenario.Outputs["draft_problem:1"]!.GetValue<string>());

        var missing = Assert.Throws<InvalidDataException>(() => CaptureScenario.Read("""
            { "description": null, "inputs": {}, "profile": { "name": null, "signature": null }, "outputs": {} }
            """));
        Assert.Contains("macroChoices", missing.Message, StringComparison.Ordinal);

        var extra = Assert.Throws<InvalidDataException>(() => CaptureScenario.Read("""
            { "description": null, "inputs": {}, "profile": { "name": null, "signature": null }, "macroChoices": {}, "outputs": {}, "expect": {} }
            """));
        Assert.Contains("expect", extra.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMinimal_Completes_AndEveryAgentNodesSentPromptIsCaptured()
    {
        var run = await CaptureRun.StartAsync(Minimal(), Scenario());

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.Status);
        Assert.Null(run.Failure);
        var calls = run.Context.Calls.Count(call => call.Kind == "activity" && call.Name == ConsultGenerationActivityNames.RunPromptNode);
        Assert.Equal(calls - run.Renders.Count, run.Prompts.Count);
        // Every prompt is keyed by its instance, and every one sent was answered by one stub.
        Assert.Equal(run.Outputs.Select(output => output.Key), run.Prompts.Select(prompt => prompt.Key));
        Assert.Contains(run.Prompts, prompt => prompt.Key == "draft_paragraph:1/1");
        Assert.Contains(run.Prompts, prompt => prompt.Key == "section_notes:hpi");
        Assert.Contains(run.Prompts, prompt => prompt.Key == "plan" && prompt.Text.Contains(Notes, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSentText_IncludesTheClassifiersTrailer()
    {
        var run = await CaptureRun.StartAsync(Minimal(), Scenario());

        var scope = Assert.Single(run.Prompts, prompt => prompt.Key == "scope").Text;
        Assert.EndsWith("\n\nAnswer with exactly one of: in_scope, out_of_scope.", scope, StringComparison.Ordinal);
        Assert.StartsWith("Is this in scope? " + Notes, scope, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStructuredNode_IsStubbedFromItsFormat_AndTheStubIsRecorded()
    {
        var run = await CaptureRun.StartAsync(Minimal(), Scenario());

        var plan = Assert.Single(run.Outputs, output => output.Key == "plan").Value;
        Assert.Equal(WorkflowStubOutputs.FromSchema(JsonNode.Parse(V30Package.SectionPlan)!["schema"], "plan")!.ToJsonString(), plan!.ToJsonString());
        Assert.Empty(JsonSchemaSubsetValidator.Validate(JsonNode.Parse(plan.ToJsonString()), JsonNode.Parse(V30Package.SectionPlan)!["schema"]!));
        // A text node's stub is the text format's, seeded by its instance key.
        Assert.Equal(WorkflowStubOutputs.Text("draft_paragraph:1/1"), Assert.Single(run.Outputs, output => output.Key == "draft_paragraph:1/1").Value!.GetValue<string>());
    }

    [Fact]
    public async Task APinnedOutput_IsServedInsteadOfTheStub()
    {
        var run = await CaptureRun.StartAsync(Minimal(), Scenario("""
            { "plan": { "sections": [ { "title": "History", "kind": "history", "topics": [ { "topic": "onset" } ] } ] } }
            """));

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.Status);
        Assert.Equal("History", Assert.Single(run.Outputs, output => output.Key == "plan").Value!["sections"]![0]!["title"]!.GetValue<string>());
        var outline = Assert.Single(run.Prompts, prompt => prompt.Key.StartsWith("outline:", StringComparison.Ordinal));
        Assert.Equal("outline:1", outline.Key);
        Assert.StartsWith("Outline the section History.\n\n", outline.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APinForAnInstanceThatDidNotRun_IsRefused()
    {
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CaptureRun.StartAsync(Minimal(), Scenario("""{ "nowhere:9": "Never asked." }""")));

        Assert.Equal("Scenario pins nowhere:9, which did not run.", refused.Message);
    }

    [Theory]
    [InlineData("flag")]
    [InlineData("fail")]
    public async Task AGeneratedStubTheEngineFlags_IsRefused(string onInvalid)
    {
        // A format whose schema the stub cannot meet: an integer whose declared values are
        // strings — the stub takes a declared value, and the engine flags its type.
        var package = Minimal(
            root => V30Package.Node(root, "outline")["onInvalid"] = onInvalid,
            files => files["formats/outline.json"] = """
                { "type": "object", "producer": "custom", "outputMacros": false,
                  "description": "One section's outline.",
                  "schema": { "type": "object", "additionalProperties": false, "required": ["points"],
                              "properties": { "points": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["point"],
                                                                                      "properties": { "point": { "type": "integer", "enum": ["one", "two"] } } } } } } }
                """);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => CaptureRun.StartAsync(package, Scenario()));

        Assert.StartsWith("Generated stub for outline:1 is not valid for its format: ", refused.Message, StringComparison.Ordinal);
        Assert.Contains("is a string, not integer.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoRuns_AreByteIdentical()
    {
        var first = PackageCapture.Write("general", null, "default", await CaptureRun.StartAsync(Minimal(), Scenario()));
        var second = PackageCapture.Write("general", null, "default", await CaptureRun.StartAsync(Minimal(), Scenario()));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ADeliverable_IsCapturedWithItsFinalText()
    {
        var package = Minimal(root => root["deliverables"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "letter", ["label"] = "Follow-up letter", ["if"] = "input.kind == \"follow_up\"",
            ["sources"] = new JsonArray("node.problems"),
        }));

        var run = await CaptureRun.StartAsync(package, Scenario());

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.Status);
        Assert.Equal(["consult", "letter"], run.Deliverables.Select(deliverable => deliverable.Id));
        var consult = run.Deliverables[0];
        Assert.Equal("composed", consult.State);
        Assert.Null(consult.Reason);
        Assert.Equal(Assert.Single(run.State.AssembledDocuments!).Text, consult.Text);
        Assert.Contains(WorkflowStubOutputs.Text("draft_paragraph:1/1"), consult.Text, StringComparison.Ordinal);
        var letter = run.Deliverables[1];
        Assert.Equal("skipped", letter.State);
        Assert.Equal(Assert.Single(run.State.SkippedDocuments!).Reason, letter.Reason);
        Assert.Null(letter.Text);
    }

    [Fact]
    public async Task TheProfile_ReachesASignedDeliverable()
    {
        var run = await CaptureRun.StartAsync(Minimal(root => root["deliverables"]![0]!["signature"] = true), Scenario(signature: Signature));

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.Status);
        Assert.EndsWith("\n\n" + Signature, Assert.Single(run.Deliverables).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedJob_IsCaptured_NotThrown()
    {
        var package = Minimal(root => V30Package.Node(root, "assessment")["failIfEmpty"] = "The assessment came back empty.");

        var run = await CaptureRun.StartAsync(package, Scenario("""{ "assessment": "" }"""));

        Assert.Equal(ConsultGenerationJobStatuses.Failed, run.Status);
        Assert.NotNull(run.Failure);
        Assert.Contains("The assessment came back empty.", run.Failure, StringComparison.Ordinal);
        Assert.Contains(run.Prompts, prompt => prompt.Key == "extract");
        Assert.Contains(run.Prompts, prompt => prompt.Key == "assessment");
        Assert.Contains(run.Outputs, output => output.Key == "assessment" && output.Value!.GetValue<string>() == "");
        // Every deliverable has its row: the job failed before this one settled.
        Assert.Equal(new CapturedDeliverable("consult", "unsettled", null, null), Assert.Single(run.Deliverables));
    }

    [Fact]
    public async Task AStartTheEngineRefuses_IsCaptured_WithEveryDeliverableUnsettled()
    {
        // No deliverable's condition holds for these inputs: the starter refuses the job before
        // any model call (#434). That refusal is today's engine's answer, so it is a result.
        var package = Minimal(root => root["deliverables"]![0]!["if"] = "input.kind == \"follow_up\"");

        var run = await CaptureRun.StartAsync(package, Scenario());

        Assert.Equal(CaptureRun.Refused, run.Status);
        Assert.StartsWith("NoApplicableDeliverable: No document applies to these inputs. ", run.Failure, StringComparison.Ordinal);
        Assert.Empty(run.Outputs);
        Assert.Empty(run.Prompts);
        Assert.Empty(run.Renders);
        Assert.Equal(new CapturedDeliverable("consult", "unsettled", null, null), Assert.Single(run.Deliverables));
        var root = JsonNode.Parse(PackageCapture.Write("general", null, "refused", run))!;
        Assert.Equal("Refused", root["status"]!.GetValue<string>());
        Assert.Equal(run.Failure, root["failure"]!.GetValue<string>());
    }

    [Fact]
    public async Task AStartRefusedForTheScenariosInputs_Throws()
    {
        // A required input left out is the scenario's mistake, not the engine's refusal.
        var scenario = CaptureScenario.Read("""
            { "description": null, "inputs": { "kind": "new" }, "profile": { "name": "Dr A. Sample", "signature": null }, "macroChoices": {}, "outputs": {} }
            """);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => CaptureRun.StartAsync(Minimal(), scenario));

        Assert.StartsWith("The job did not start: InputsMismatch", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnActivityTheRunnerDoesNotAnswer_IsRefused_EvenWhenProductionSwallowsIt()
    {
        // A reply address makes the orchestrator call the reply leg at the end; production's own
        // catch swallows its failure, so only the calls read back can say it was not answered.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => CaptureRun.StartAsync(Minimal(), Scenario(),
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App, ReplyToAddress: "margaret.okafor@example.org")));

        Assert.Equal($"The test answers no activity '{Email.SendEmailIntakeReplyActivity.Name}'.", refused.Message);
    }

    [Fact]
    public void AFaultOfTheRunner_IsRecorded_AndProductionsExceptionsPassUntouched()
    {
        var faults = new List<string>();

        Assert.ThrowsAny<Exception>(() =>
            CaptureRun.Guard<ConsultPromptNodeActivityInput, NodeRunResult>("run-prompt-node", "not a payload", faults, _ => throw new InvalidOperationException("unreached")));
        Assert.Equal(["activity 'run-prompt-node' was handed String, not ConsultPromptNodeActivityInput."], faults);

        faults.Clear();
        var production = new InvalidOperationException("Node 'plan' reply is empty: No plan.");
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            CaptureRun.Guard<string, NodeRunResult>("run-prompt-node", "payload", faults, _ => throw production));
        Assert.Same(production, thrown);
        Assert.Empty(faults);
        Assert.Equal("ok", CaptureRun.Guard<string, string>("run-computed-node", "payload", faults, _ => "ok"));
        Assert.Empty(faults);
    }

    [Fact]
    public async Task APromptReadingTheRunsJob_IsRefused()
    {
        var package = Minimal(files: files => files["prompts/plan.md"] = "Plan the sections this referral needs (run {{ run.job }}): {{ input.notes }}");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => CaptureRun.StartAsync(package, Scenario()));

        Assert.Equal("The capture holds the job's id (prompt plan), which differs on every run; a package read run.job.", refused.Message);
    }

    [Theory]
    [InlineData("""{ "description": [1], "inputs": {}, "profile": { "name": null, "signature": null }, "macroChoices": {}, "outputs": {} }""", "description[0]")]
    [InlineData("""{ "description": null, "inputs": {}, "profile": { "name": 7, "signature": null }, "macroChoices": {}, "outputs": {} }""", "profile.name")]
    [InlineData("""{ "description": null, "inputs": {}, "profile": { "name": null, "signature": true }, "macroChoices": {}, "outputs": {} }""", "profile.signature")]
    [InlineData("""{ "description": null, "inputs": {}, "profile": { "name": null, "signature": null }, "macroChoices": { "closing": "yes" }, "outputs": {} }""", "macroChoices.closing")]
    [InlineData("""{ "description": null, "inputs": {}, "profile": { "name": null, "signature": null }, "macroChoices": {}, "outputs": { "plan": null } }""", "outputs.plan is null; pin a value or remove the key.")]
    public void AMemberOfTheWrongType_IsRefusedByName(string json, string named)
    {
        var refused = Assert.Throws<InvalidDataException>(() => CaptureScenario.Read(json));

        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCaptureDocument_HasItsMembersInOrder_AndEndsWithOneNewline()
    {
        var text = PackageCapture.Write("general", null, "default", await CaptureRun.StartAsync(Minimal(), Scenario()));

        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        var root = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(["package", "tag", "scenario", "status", "failure", "outputs", "prompts", "renders", "deliverables"], root.Select(member => member.Key));
        Assert.Null(root["tag"]);
        Assert.Equal("Completed", root["status"]!.GetValue<string>());
        var prompt = root["prompts"]![0]!.AsObject();
        Assert.Equal(["key", "hash", "text"], prompt.Select(member => member.Key));
        Assert.Equal(ByteEquivalenceFixtures.HashOf(prompt["text"]!.GetValue<string>()), prompt["hash"]!.GetValue<string>());
        var deliverable = root["deliverables"]![0]!.AsObject();
        Assert.Equal(["id", "state", "reason", "hash", "text"], deliverable.Select(member => member.Key));
        // The relaxed encoder: a prompt's quotes and angle brackets are written as they are.
        Assert.DoesNotContain("\\u0022", text, StringComparison.Ordinal);
    }
}
