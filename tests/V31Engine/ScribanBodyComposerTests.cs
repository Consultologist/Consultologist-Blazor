using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; D12): the default body composer until area 07: an entry's body rendered with Scriban
/// over the scope the runner built, a loop held to the step budget, a read the scope does not hold a failure (A06-008),
/// never an empty render. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class ScribanBodyComposerTests
{
    private const string Summary = "Margaret Okafor is tired; Dr A. Sample will review her thyroid results (fictional).";

    private static readonly JsonObject RefineScope = new()
    {
        ["node"] = new JsonObject { ["refine"] = new JsonObject { ["content"] = Summary } },
    };

    [Fact]
    public void ScribanBodyComposer_RendersTheScope()
    {
        var plan = Plan("nested-note");

        var composed = new ScribanBodyComposer().Compose(plan, "deliverable.note", null, RefineScope, 100_000);

        Assert.Equal(new ComposedBody($"## Summary\n\n{Summary}\n", false, null), composed);
    }

    [Fact]
    public void ScribanBodyComposer_RendersAFunctionsEntry()
    {
        var plan = Plan("functions");
        var scope = new JsonObject { ["param"] = new JsonObject { ["draft"] = new JsonObject { ["content"] = Summary } } };

        var composed = new ScribanBodyComposer().Compose(plan, "node.patient_draft", "draft_section", scope, 100_000);

        Assert.Equal($"Using the consult draft below, write what it says about Margaret Okafor for this section.\n\n{Summary}\n", composed.Text);
    }

    [Fact]
    public void ScribanBodyComposer_StopsAtItsStepLimit()
    {
        var plan = Plan("nested-note", body: "{{ for point in node.refine.value }}- {{ point }}\n{{ end }}");
        var scope = new JsonObject
        {
            ["node"] = new JsonObject { ["refine"] = new JsonObject { ["value"] = new JsonArray("one", "two", "three", "four", "five") } },
        };

        Assert.Equal("- one\n- two\n- three\n- four\n- five\n", new ScribanBodyComposer().Compose(plan, "deliverable.note", null, scope, 5).Text);
        Assert.Equal(new ComposedBody(null, true, null), new ScribanBodyComposer().Compose(plan, "deliverable.note", null, scope, 4));
    }

    [Fact]
    public void AReadTheScopeDoesNotHold_FailsWithA06008()
    {
        var plan = Plan("nested-note");

        var composed = new ScribanBodyComposer().Compose(plan, "deliverable.note", null, new JsonObject(), 100_000);

        Assert.Null(composed.Text);
        Assert.False(composed.TooManySteps);
        V31Contract.AssertSentence(new SourceRefusal("A06-008", composed.Failure!, ""));
        Assert.Equal("Deliverable 'note' couldn't be rendered.", composed.Failure);
    }

    [Fact]
    public void AnEntryThePlanLacks_IsNotWhatPublishWrote()
    {
        var plan = Plan("nested-note");

        var composed = new ScribanBodyComposer().Compose(plan, "deliverable.letter", null, RefineScope, 100_000);

        Assert.Equal(ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence, composed.Failure);
    }

    /// <summary>The plan of the golden <paramref name="name"/>, its deliverable's body replaced by <paramref name="body"/> when given.</summary>
    private static RunPlan Plan(string name, string? body = null)
    {
        var pair = CompiledGoldenTests.Published(CompiledGoldenTests.Cases().Single(c => c.Name == name));
        var document = JsonNode.Parse(pair.Resolve)!.AsObject();
        if (body != null)
            document["deliverables"]!["deliverable.note"]!["body"] = body;
        var text = CanonicalJson.Write(document);
        var (plan, refusal) = RunPlan.Build(text, CanonicalJson.HashText(text), pair.LockHash, null);
        Assert.True(plan != null, refusal?.Sentence);
        return plan!;
    }
}
