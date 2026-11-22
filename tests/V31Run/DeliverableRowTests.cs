using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 10; R1, D6): a deliverable's row is part 08 § 10's. <see cref="RunRow.ToJson"/>
/// writes its members in order and no others: its template by hash, its format, its state word and reason, its composed
/// hash (hash version 4), the delivery rows of a released one, the gate's row key, its template's sites and its
/// substitutions, and its renders, run spots, units, profile reads and flags. A blocked deliverable keeps its composed hash,
/// sites and substitutions, and its composed text in the retained text, and is never delivered (R1). Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
public class DeliverableRowTests
{
    /// <summary>Part 08 § 10's members of a deliverable row, in order.</summary>
    private static readonly string[] DeliverableMembers =
    [
        "key", "label", "templateHash", "format", "state", "stateReason", "composedHash", "hashVersion", "delivery", "check",
        "sites", "substitutions", "renders", "runNeutralHash", "runSpots", "units", "profileReads", "modelDerived", "runDependent",
        "profileCarrying", "startedAtUtc", "completedAtUtc", "durationMs",
    ];

    [Fact]
    public async Task ADeliverableRow_HasPart08sMembers()
    {
        var plan = RunFixture.Plan("checked-note");
        var run = await RunGoldenTests.Run(plan, new RunLimits(), seed: null);

        const string key = "deliverable.patient.patient_letter";
        var row = run.Recorder.Row(key).ToJson();

        Assert.Equal(DeliverableMembers, row.Select(m => m.Key));
        Assert.Equal(key, (string?)row["key"]);
        Assert.Equal("Letter to the patient", (string?)row["label"]);
        Assert.Equal((string?)plan.Entries[key].Json["bodyHash"], (string?)row["templateHash"]);
        Assert.StartsWith("sha256:", (string?)row["templateHash"], StringComparison.Ordinal);
        Assert.Equal("plaintext", (string?)row["format"]);
        Assert.Equal("released", (string?)row["state"]);
        Assert.Null(row["stateReason"]);
        Assert.Equal(run.Recorder.Row(key).ValueHash, (string?)row["composedHash"]);
        Assert.Equal(4, (int)row["hashVersion"]!);
        Assert.Equal(
            "[\"delivery:deliverable.patient.patient_letter/email\",\"delivery:deliverable.patient.patient_letter/pdf\"]",
            CanonicalJson.Write(row["delivery"]));
        Assert.Null(row["check"]);
        Assert.NotEmpty(row["sites"]!.AsArray());
        Assert.All(row["sites"]!.AsArray(), s => Assert.Equal(["site", "at", "expr", "loop"], s!.AsObject().Select(m => m.Key)));
        Assert.IsType<JsonArray>(row["substitutions"]);
        Assert.False(row.ContainsKey("kind"));
        Assert.False(row.ContainsKey("status"));
    }

    [Fact]
    public async Task ABlockedDeliverable_KeepsItsHashAndIsNeverDelivered()
    {
        // R1: checked-note's proofread does not pass on the stubs, so the note (delivered to emr) is blocked
        var run = await RunGoldenTests.Run(RunFixture.Plan("checked-note"), new RunLimits(), seed: null);

        var note = run.Recorder.Row("deliverable.note");
        var row = note.ToJson();

        Assert.Equal(DeliverableMembers, row.Select(m => m.Key));
        Assert.Equal("blocked", (string?)row["state"]);
        Assert.Equal("blocked: check 'proofread' failed (it did not pass)", (string?)row["stateReason"]);
        Assert.Equal(CanonicalJson.HashText("Rendered deliverable.note."), (string?)row["composedHash"]);
        Assert.Equal("[]", CanonicalJson.Write(row["delivery"]));
        Assert.Equal("check.proofread", (string?)row["check"]);
        Assert.NotEmpty(row["sites"]!.AsArray());

        // its composed text is kept in the retained text, beside the row
        Assert.Equal("Rendered deliverable.note.", (string?)note.Text!["content"]);
        Assert.DoesNotContain("Rendered deliverable.note.", CanonicalJson.Write(row), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleasedDeliverable_NamesItsDeliveryRows()
    {
        var plan = RunFixture.Plan(Minimal().WithJson("deliverables/index.json", i => i["delivery"] = new JsonArray("email")));
        var host = new ComposingRunHost(plan, (call, _) => RunFixture.StubReply(plan, call));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var note = recorder.Row("deliverable.note");
        var row = note.ToJson();
        Assert.Equal("released", (string?)row["state"]);
        Assert.Equal("[\"delivery:deliverable.note/email\"]", CanonicalJson.Write(row["delivery"]));
        Assert.Equal(
            "[{\"at\":\"1:1\",\"expr\":\"node.plan.content\",\"loop\":null,\"site\":1}]",
            CanonicalJson.Write(row["sites"]));

        // its substitution replays against its composed text, kept beside the row
        var substitution = Assert.Single(row["substitutions"]!.AsArray())!;
        Assert.Equal("node.plan.content", (string?)substitution["reads"]);
        var content = (string)note.Text!["content"]!;
        Assert.Equal(CanonicalJson.HashText(content), (string?)row["composedHash"]);
        Assert.True(RecordReplay.Verify(content, row["substitutions"]!.AsArray(), out var fault), fault);
    }

    [Fact]
    public async Task ASkippedDeliverable_HasNoComposedHash()
    {
        var files = Minimal()
            .WithJson("manifest.json", m => m["inputs"]!["include_billing"] = JsonNode.Parse(
                "{ \"label\": \"Include the billing section\", \"description\": null, \"format\": \"boolean\", \"required\": false, \"channel\": null }"))
            .WithJson("deliverables/note.md", h => h["if"] = JsonNode.Parse("{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }"));
        var plan = RunFixture.Plan(files);
        var recorder = new ListRecorder();

        await new PlanRunner(plan, new RunLimits(), new FakeRunHost(auto: true), recorder)
            .RunAsync(PlanRunnerLeavesTests.Inputs(includeBilling: false), FakeRunHost.Start);

        var note = recorder.Row("deliverable.note");
        var row = note.ToJson();
        Assert.Equal(DeliverableMembers, row.Select(m => m.Key));
        Assert.Equal("skipped", (string?)row["state"]);
        Assert.Equal("skipped: input.include_billing.value = true is false", (string?)row["stateReason"]);
        Assert.Null(row["composedHash"]);
        Assert.Equal("[]", CanonicalJson.Write(row["delivery"]));
        Assert.Equal("[]", CanonicalJson.Write(row["substitutions"]));
        Assert.Single(row["sites"]!.AsArray()); // package text: its template's sites stand
        Assert.Null(note.Text?["content"]);
    }
}
