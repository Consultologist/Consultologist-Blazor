using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1508 (ruled by the owner, Q5 of 2026-11-26; R17; Task 26 step 1): what the run does with a member read through a
/// nullable object when that object is null. For each kind (an optional object input left unset, a nullable property that
/// is an object, a node that was skipped, read as <c>.value.&lt;member&gt;</c>), in a prompt and in a deliverable, the
/// render fails (strict variables: a member of nothing is not there), so the node or the deliverable fails and the job
/// with it. The same read inside <c>{{ if &lt;object&gt; }}</c> renders nothing there and the job completes; a nullable value
/// read as the last step renders empty. Publish now refuses the unguarded read (A11-009), so its run is the guarded package's
/// compiled document with the guard's two tags blanked to spaces (every other tag keeps its line and column), loaded as the
/// engine loads one (<see cref="RunPlan.Build"/>). Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class NullableObjectRunTests
{
    private const string Draft = "Margaret Okafor, 58, referred by Dr A. Sample for fatigue (fictional).";

    /// <summary>Each kind: the read through the object, and the inputs that make the object null.</summary>
    public static TheoryData<string, string, string> Kinds => new()
    {
        { "an unset optional object input", "input.patient.value.name", "patient unset" },
        { "a nullable property that is an object", "input.referrer.value.contact.phone", "contact null" },
        { "a node that was skipped", "node.verdict_check.value.verdict", "billing unset" },
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task AMemberReadThroughANullObject_FailsTheRender(string kind, string read, string inputs)
    {
        foreach (var site in new[] { "prompt", "deliverable" })
        {
            var (files, _, subject, _) = NullableFixture.At(site, Guarded(read));
            var plan = Unguarded(files, read);

            var result = await Run(plan, Inputs(inputs));

            Assert.True(result.State == RunState.Failed, $"{kind}, {site}: {result.State}");
            Assert.Equal($"{subject} couldn't be rendered.", result.Sentence);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task TheSameRead_InsideAnIfOnTheObject_RendersNothingThere(string kind, string read, string inputs)
    {
        foreach (var site in new[] { "prompt", "deliverable" })
        {
            var (files, _, _, _) = NullableFixture.At(site, Guarded(read));
            var plan = RunFixture.Plan(files);

            var result = await Run(plan, Inputs(inputs));

            Assert.True(result.State == RunState.Completed, $"{kind}, {site}: {result.State} {result.Sentence}");
            Assert.Contains("[]", Text(result, site), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheGuardedRead_RendersTheMember_WhenTheObjectIsThere()
    {
        var (files, _, _, _) = NullableFixture.At("prompt", "[{{ if input.patient.value }}{{ input.patient.value.name }}{{ end }}]");
        var plan = RunFixture.Plan(files);

        var result = await Run(plan, Inputs("patient given"));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Contains("[Margaret Okafor]", Text(result, "prompt"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConjunction_StopsAtTheFirstNullObject()
    {
        // Scriban's && does not read its right side when its left is false, so the second test never reads through null.
        var (files, _, _, _) = NullableFixture.At("prompt",
            "[{{ if input.patient.value && input.patient.value.contact }}{{ input.patient.value.contact.phone }}{{ end }}]");
        var plan = RunFixture.Plan(files);

        var result = await Run(plan, Inputs("patient unset"));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Contains("[]", Text(result, "prompt"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANullableLastStep_RendersEmpty()
    {
        var (files, _, _, _) = NullableFixture.At("prompt", "[{{ input.referrer.value.nickname }}]");
        var plan = RunFixture.Plan(files);

        var result = await Run(plan, Inputs("contact null"));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Contains("[]", Text(result, "prompt"), StringComparison.Ordinal);
    }

    /// <summary>The object <paramref name="read"/> steps through last: the read without its last step.</summary>
    private static string Through(string read) => read[..read.LastIndexOf('.')];

    private static string Guard(string read) => $"{{{{ if {Through(read)} }}}}";

    private const string End = "{{ end }}";

    /// <summary><paramref name="read"/> inside its guard, between brackets.</summary>
    private static string Guarded(string read) => $"[{Guard(read)}{{{{ {read} }}}}{End}]";

    /// <summary>
    /// The plan of <paramref name="files"/> (which publish), with the guard around <paramref name="read"/> blanked in the
    /// compiled document: its two tags written as spaces, so every other tag keeps its place.
    /// </summary>
    private static RunPlan Unguarded(Dictionary<string, byte[]> files, string read)
    {
        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => r.Sentence)));
        var guarded = $"{Guard(read)}{{{{ {read} }}}}{End}";
        var blanked = new string(' ', Guard(read).Length) + $"{{{{ {read} }}}}" + new string(' ', End.Length);
        Assert.Contains(guarded, compiled!.Resolve, StringComparison.Ordinal);
        var (plan, refusal) = RunPlan.Build(compiled.Resolve.Replace(guarded, blanked, StringComparison.Ordinal), compiled.ResolveHash, compiled.LockHash, null);
        Assert.Null(refusal);
        return plan!;
    }

    /// <summary>The raw inputs: the draft and the required referrer always; the patient and billing as <paramref name="inputs"/> says.</summary>
    private static Dictionary<string, JsonNode?> Inputs(string inputs)
    {
        var given = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["consult_draft"] = Draft,
            ["referrer"] = JsonNode.Parse(inputs == "contact null"
                ? "{ \"name\": \"Dr A. Sample\", \"nickname\": null, \"contact\": null }"
                : "{ \"name\": \"Dr A. Sample\", \"nickname\": null, \"contact\": { \"phone\": \"0100 000 000\" } }"),
        };
        if (inputs != "patient unset")
            given["patient"] = JsonNode.Parse("{ \"name\": \"Margaret Okafor\", \"nickname\": null, \"contact\": null, \"allergies\": [] }");
        if (inputs != "billing unset")
            given["include_billing"] = true;
        return given;
    }

    private static Task<OfflineResult> Run(RunPlan plan, Dictionary<string, JsonNode?> inputs) =>
        OfflineRun.RunAsync(plan, new OfflineRequest(
            inputs, new Dictionary<string, string?>(StringComparer.Ordinal), new JsonObject(),
            new Dictionary<string, JsonNode?>(StringComparer.Ordinal), new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Utc)));

    /// <summary>The text the body under test gave: <c>draft</c>'s prompt, or the deliverable <c>note</c>.</summary>
    private static string Text(OfflineResult result, string site) => site == "prompt"
        ? Assert.Single(result.Prompts, p => p.Key == "node.draft").Text
        : Assert.Single(result.Deliverables, d => d.Key == "deliverable.note").Text ?? "";
}
