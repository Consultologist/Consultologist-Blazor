using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

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
    public async Task AGuardOnAForVariable_DoesNotHoldUnderAForThatBindsTheNameAgain()
    {
        // Fix round 1: the outer e's patient is there and the inner e's is null; a guard on the outer e.patient would be no
        // guard for the inner e.patient.name, which fails. Publish refuses it now, so the inner guard is blanked here.
        const string read = "e.patient.name";
        var (files, _, subject, _) = NullableFixture.At("prompt",
            $"{{{{ for e in input.visits.value }}}}{{{{ if e.patient }}}}{{{{ for e in input.stays.value }}}}{Guarded(read)}\n{{{{ end }}}}{{{{ end }}}}{{{{ end }}}}");
        var given = Inputs("patient given");
        given["visits"] = JsonNode.Parse("[{ \"patient\": { \"name\": \"Margaret Okafor\", \"value\": { \"name\": \"Margaret Okafor\" } } }]");
        given["stays"] = JsonNode.Parse("[{ \"patient\": null }]");

        var guarded = await Run(RunFixture.Plan(files), given);
        var result = await Run(Unguarded(files, read), given);

        Assert.True(guarded.State == RunState.Completed, guarded.Sentence);
        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal($"{subject} couldn't be rendered.", result.Sentence);
    }

    [Fact]
    public async Task AHeaderGuard_DoesNotHoldUnderAForThatBindsItsRoot()
    {
        // Fix round 1: draft runs only when the input patient is there, but under `for input in …` the read's input is the
        // loop's item (a visit the agent visit_list lists), whose patient is null here. Publish refuses the read without its
        // own guard, so that guard is blanked. The loop's source is a node, so the body reads no input of its own.
        const string read = "input.patient.value.name";
        var (files, _, subject, _) = NullableFixture.At("prompt",
            "{{ for input in node.visit_list.value }}{{ if input.patient }}{{ input.patient.value.name }}{{ end }}\n{{ end }}");
        files.WithJson("nodes/draft.md", h => h["if"] = JsonNode.Parse("{ \"!==\": [{ \"var\": \"input.patient.value.name\" }, null] }"))
            .Listed("formats/index.json", "visit_list")
            .With("formats/visit_list.json", Json("{ \"$schema\": \"urn:consultologist:spec:31:dialect\", " + NullableFixture.Visits[1..]))
            .WithAgent("visit_list", "List the visits in the draft for Dr A. Sample.\n\n{{ input.consult_draft.content }}\n", "\"visit_list\"");
        var outputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["node.visit_list"] = JsonNode.Parse("[{ \"patient\": null }]") };

        var guarded = await Run(RunFixture.Plan(files), Inputs("patient given"), outputs);
        var result = await Run(Unguarded(files, read, "input.patient"), Inputs("patient given"), outputs);

        Assert.True(guarded.State == RunState.Completed, guarded.Sentence);
        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal($"{subject} couldn't be rendered.", result.Sentence);
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

    private static string Guard(string read, string? through = null) => $"{{{{ if {through ?? Through(read)} }}}}";

    private const string End = "{{ end }}";

    /// <summary><paramref name="read"/> inside its guard, between brackets.</summary>
    private static string Guarded(string read) => $"[{Guard(read)}{{{{ {read} }}}}{End}]";

    /// <summary>
    /// The plan of <paramref name="files"/> (which publish), with the guard around <paramref name="read"/> (on
    /// <paramref name="through"/>, else the read without its last step) blanked in the compiled document: its two tags
    /// written as spaces, so every other tag keeps its place.
    /// </summary>
    private static RunPlan Unguarded(Dictionary<string, byte[]> files, string read, string? through = null)
    {
        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => r.Sentence)));
        var guarded = $"{Guard(read, through)}{{{{ {read} }}}}{End}";
        var blanked = new string(' ', Guard(read, through).Length) + $"{{{{ {read} }}}}" + new string(' ', End.Length);
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

    private static Task<OfflineResult> Run(RunPlan plan, Dictionary<string, JsonNode?> inputs, Dictionary<string, JsonNode?>? outputs = null) =>
        OfflineRun.RunAsync(plan, new OfflineRequest(
            inputs, new Dictionary<string, string?>(StringComparer.Ordinal), new JsonObject(),
            outputs ?? new Dictionary<string, JsonNode?>(StringComparer.Ordinal), new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Utc)));

    /// <summary>The text the body under test gave: <c>draft</c>'s prompt, or the deliverable <c>note</c>.</summary>
    private static string Text(OfflineResult result, string site) => site == "prompt"
        ? Assert.Single(result.Prompts, p => p.Key == "node.draft").Text
        : Assert.Single(result.Deliverables, d => d.Key == "deliverable.note").Text ?? "";
}
