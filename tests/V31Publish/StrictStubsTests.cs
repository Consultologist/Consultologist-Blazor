using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Publish;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 11; part 07 § 8.2; D6): an offline run that does not generate a missing stub
/// (<see cref="OfflineRequest.GenerateMissing"/> false). A call with no stub fails its node as a call that could not be made
/// does, is listed in <see cref="OfflineResult.MissingOutputs"/> and is never asked again; a stub that does not fit its node's
/// format is listed in <see cref="OfflineResult.InvalidOutputs"/> (path and keyword, never the value) and is not repaired by
/// asking again. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class StrictStubsTests
{
    private static readonly DateTime Start = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<string, string?> NoProfile = new Dictionary<string, string?>(StringComparer.Ordinal);

    [Fact]
    public async Task ACallWithNoStub_IsNotGenerated_AndItsDependantsAreNotCalledMissingToo()
    {
        var plan = RunFixture.Plan(RunGoldenTests.Macros());
        Assert.Contains("node.classify", plan.Entries["node.draft"].Depends);

        var run = await OfflineRun.RunAsync(plan, Request(plan, strict: true, new Dictionary<string, JsonNode?>(StringComparer.Ordinal)));

        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal(["node.plan"], run.MissingOutputs);
        Assert.Empty(run.InvalidOutputs);
        Assert.Empty(run.Outputs);
        Assert.Equal(RowState.Failed, run.Rows.Single(r => r.Key == "node.plan").State);
        Assert.DoesNotContain(run.Rows, r => r.Key is "node.draft" or "node.classify" or "node.terms");
        Assert.DoesNotContain(run.Prompts, p => p.Key != "node.plan");
    }

    [Fact]
    public async Task OnlyTheCallsActuallyAsked_AreMissing_WhenAnEarlierStubIsGiven()
    {
        var plan = RunFixture.Plan(RunGoldenTests.Macros());
        var stubs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["node.plan"] = JsonNode.Parse(Sections) };

        var run = await OfflineRun.RunAsync(plan, Request(plan, strict: true, stubs));

        Assert.Equal(RunState.Failed, run.State);
        Assert.Contains(run.Rows, r => r.Key == "node.plan" && r.State == RowState.Completed);
        Assert.All(run.MissingOutputs, key => Assert.Contains(run.Prompts, p => p.Key == key));
        Assert.DoesNotContain("node.plan", run.MissingOutputs);
        Assert.Equal(run.MissingOutputs.Order(StringComparer.Ordinal), run.MissingOutputs);
        Assert.DoesNotContain(run.MissingOutputs, key => key.StartsWith("deliverable.", StringComparison.Ordinal));
        Assert.Empty(run.InvalidOutputs);
    }

    [Fact]
    public async Task TheDefault_StillGeneratesAMissingStub()
    {
        var plan = RunFixture.Plan(RunGoldenTests.Macros());
        var stubs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["node.plan"] = JsonNode.Parse(Sections) };

        var run = await OfflineRun.RunAsync(plan, Request(plan, strict: false, stubs));

        Assert.Empty(run.MissingOutputs);
        Assert.Empty(run.InvalidOutputs);
        Assert.Contains("node.draft", run.Outputs.Keys);
    }

    [Fact]
    public async Task AStubThatDoesNotFitItsFormat_IsReported_AndNotAskedForAgain_UnderRetry()
    {
        var plan = RunFixture.Plan(RunGoldenTests.Macros().WithJson("nodes/plan.md", h => h["onInvalid"] = "retry"));
        Assert.Equal("retry", (string?)plan.Entries["node.plan"].Json["onInvalid"]);
        var stubs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["node.plan"] = JsonNode.Parse("""{"sections":[{"title":"Margaret Okafor","kind":"narrative","extra":1}]}"""),
        };

        var run = await OfflineRun.RunAsync(plan, Request(plan, strict: true, stubs));

        var fault = Assert.Single(run.InvalidOutputs);
        Assert.Equal("node.plan", fault.Key);
        Assert.Equal(StrictFormat(plan, "node.plan"), fault.Format);
        Assert.StartsWith("/sections/0 ", fault.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Okafor", fault.Error, StringComparison.Ordinal);
        Assert.Empty(run.MissingOutputs);
        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal(RowState.Failed, run.Rows.Single(r => r.Key == "node.plan").State);
        Assert.DoesNotContain(run.Rows, r => r.Key == "node.draft");
        Assert.DoesNotContain("node.plan", run.Outputs.Keys);
    }

    [Fact]
    public async Task AStubThatIsNotTheFormatsKindOfValue_IsReportedAtTheRoot()
    {
        var plan = RunFixture.Plan(RunGoldenTests.Macros());
        var stubs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["node.plan"] = JsonValue.Create("Margaret Okafor") };

        var run = await OfflineRun.RunAsync(plan, Request(plan, strict: true, stubs));

        var fault = Assert.Single(run.InvalidOutputs);
        Assert.Equal("/ type", fault.Error);
    }

    [Fact]
    public async Task AValidStubForAClassifier_DecidesTheCondition()
    {
        var plan = RunFixture.Plan(RunGoldenTests.Macros());
        var prompts = new Dictionary<bool, string>();
        foreach (var answer in new[] { true, false })
        {
            var stubs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
            {
                ["node.plan"] = JsonNode.Parse(Sections),
                ["node.classify"] = JsonNode.Parse($$"""{"answer":{{(answer ? "true" : "false")}},"reason":"The plan says so."}"""),
                // R14: a stub is served as written, so it holds the marker exactly when the macro is offered
                ["node.draft"] = "Margaret Okafor will start letrozole 2.5 mg daily." + (answer ? "\n{{~ macro.section_notes.hpi.content }}\n" : ""),
            };

            var run = await OfflineRun.RunAsync(plan, Request(plan, strict: true, stubs));
            Assert.Equal(RunState.Completed, run.State);

            Assert.Empty(run.InvalidOutputs);
            Assert.DoesNotContain("node.classify", run.MissingOutputs);
            Assert.DoesNotContain("node.draft", run.MissingOutputs);
            Assert.Equal(answer, (bool?)run.Outputs["node.classify"]?["answer"]);
            prompts[answer] = run.Prompts.Single(p => p.Key == "node.draft").Text;
        }

        Assert.NotEqual(prompts[true], prompts[false]);
        Assert.Contains("Place the history's closing line alone", prompts[true], StringComparison.Ordinal);
        Assert.DoesNotContain("Place the history's closing line alone", prompts[false], StringComparison.Ordinal);
    }

    [Fact]
    public async Task APinnedReply_IsUsedExactlyAsWritten()
    {
        // #1508 (2026-11-25, comment 6 item 2; R14): a pinned reply that leaves an offered marker out is served as written, so
        // the node's onInvalid decides, as live: fail fails it with S05-053; retry asks again, and the second attempt has no
        // stub (S07-008 at publish). Only a generated reply gets the offered markers.
        var stubs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["node.plan"] = JsonNode.Parse(Sections),
            ["node.classify"] = JsonNode.Parse("""{"answer":true,"reason":"The plan says so."}"""),
            ["node.draft"] = "Margaret Okafor will start letrozole 2.5 mg daily.",
        };
        var noMarker = $"Node 'draft': {PackageFormat.V31.Templates.ReplySentences.NoMarker("section_notes.hpi")}";

        foreach (var strict in new[] { true, false })
        {
            var fail = RunFixture.Plan(RunGoldenTests.Macros().WithJson("nodes/draft.md", h => h["onInvalid"] = "fail"));
            var failed = await OfflineRun.RunAsync(fail, Request(fail, strict, stubs));

            Assert.Equal(RunState.Failed, failed.State);
            Assert.Equal(noMarker, failed.Sentence);
            Assert.Equal(RowState.Failed, failed.Rows.Single(r => r.Key == "node.draft").State);
            Assert.Empty(failed.InvalidOutputs);
            Assert.Empty(failed.MissingOutputs);
            Assert.Equal("Margaret Okafor will start letrozole 2.5 mg daily.", (string?)failed.Outputs["node.draft"]);
        }

        var retry = RunFixture.Plan(RunGoldenTests.Macros().WithJson("nodes/draft.md", h => h["onInvalid"] = "retry"));
        var retried = await OfflineRun.RunAsync(retry, Request(retry, strict: true, stubs));

        Assert.Equal(RunState.Failed, retried.State);
        Assert.Equal(["node.draft"], retried.MissingOutputs);
        Assert.Empty(retried.InvalidOutputs);

        var test = TestFile(retry, stubs);
        var outcome = await PackageFormat.V31.Publish.PackageTests.RunAsync(
            retry, [new SourceTest("note", "tests/note.json", test)], new Dictionary<string, string> { ["tests/note.json"] = test.ToJsonString() }, TimeProvider.System);
        var refusal = Assert.Single(outcome.Refusals);
        Assert.Equal("S07-008", refusal.Id);
        Assert.Contains("node.draft", refusal.Sentence, StringComparison.Ordinal);

        // a generated reply still gets the markers its prompt offers
        var generated = new Dictionary<string, JsonNode?>(stubs, StringComparer.Ordinal);
        generated.Remove("node.draft");
        var completed = await OfflineRun.RunAsync(retry, Request(retry, strict: false, generated));
        Assert.Equal(RunState.Completed, completed.State);
        Assert.EndsWith(".\n{{~ macro.section_notes.hpi.content }}\n", (string?)completed.Outputs["node.draft"], StringComparison.Ordinal);
    }

    /// <summary>A test file for <paramref name="plan"/> that gives <paramref name="stubs"/> and expects nothing.</summary>
    private static JsonObject TestFile(RunPlan plan, IReadOnlyDictionary<string, JsonNode?> stubs)
    {
        var request = Request(plan, strict: true, stubs);
        var inputs = new JsonObject();
        foreach (var id in plan.InputOrder)
            inputs[id] = request.Inputs.GetValueOrDefault(id)?.DeepClone();
        var outputs = new JsonObject();
        foreach (var (key, value) in stubs)
            outputs[key] = value?.DeepClone();
        return new JsonObject
        {
            ["description"] = null,
            ["inputs"] = inputs,
            ["profile"] = new JsonObject(),
            ["run"] = new JsonObject { ["date"] = new JsonObject { ["value"] = "2026-11-21", ["timeZone"] = "America/Toronto" } },
            ["outputs"] = outputs,
            ["expect"] = new JsonObject(),
        };
    }

    private const string Sections ="""{"sections":[{"title":"History","kind":"narrative"},{"title":"Plan","kind":"table"}]}""";

    private static string StrictFormat(RunPlan plan, string key) =>
        plan.Entries[key].Format is JsonObject { Count: 1 } site && (string?)site["$ref"] is { } reference && reference.StartsWith("#/$defs/", StringComparison.Ordinal)
            ? reference["#/$defs/".Length..]
            : "its format";

    private static OfflineRequest Request(RunPlan plan, bool strict, IReadOnlyDictionary<string, JsonNode?> stubs)
    {
        var supplied = RunFixture.GoldenInputs().Inputs
            .Where(i => plan.Inputs.ContainsKey(i.Key))
            .ToDictionary(i => i.Key, i => i.Value?["value"]?.DeepClone(), StringComparer.Ordinal);
        return new OfflineRequest(supplied, NoProfile, new JsonObject { ["date"] = "2026-11-21" }, stubs, Start, GenerateMissing: !strict);
    }
}
