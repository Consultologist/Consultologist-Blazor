using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1506 (specVersion 31, area 09): the golden runs again, for the record: each run's rows, decisions and (the delivery
/// runner on its outcomes) deliveries kept in one <see cref="ListRecorder"/>. With a <c>sentinel</c>, every input and every
/// stub reply that can hold it (and still be a value of its format) carries it.
/// </summary>
internal static class RecordRuns
{
    public const string Sentinel = "ZZ-SENTINEL-OKAFOR";

    /// <summary>The fixed fictional header every record golden is assembled under.</summary>
    public static JsonObject Header() => new()
    {
        ["schemaVersion"] = 12,
        ["packageSpecVersion"] = 31,
        ["jobId"] = "job-fixture",
        ["status"] = "Completed",
    };

    /// <summary>The names of the run goldens, as <c>Golden/&lt;name&gt;.run.txt</c> has them.</summary>
    public static IEnumerable<string> Names() =>
        CompiledGoldenTests.Cases().Select(c => c.Name).Append("checked-note-outcomes");

    public sealed record Ran(ListRecorder Recorder, RunResult Result);

    public static async Task<Ran> Run(string name, string? sentinel = null)
    {
        var inputs = Inputs(sentinel);
        switch (name)
        {
            case "macros":
            {
                var plan = RunFixture.Plan(RunGoldenTests.Macros());
                var host = new ComposingRunHost(plan, (call, prompt) =>
                {
                    var reply = call.EntryKey == "node.plan" ? PlacementDecisionsTests.Sections
                        : prompt.TextReply ? ComposingRunHost.EchoMarkers(call, prompt)
                        : ComposingRunHost.Judgement(true);
                    return prompt.TextReply && sentinel != null && call.EntryKey != "node.plan" ? reply + " " + sentinel : reply;
                });
                var recorder = new ListRecorder(() => host.Calls.Count);
                var macroInputs = inputs with { RunFacts = new JsonObject { ["date"] = "2026-11-21" } };
                var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(macroInputs, FakeRunHost.Start);
                return await Delivered(recorder, result);
            }

            case "checked-note-outcomes":
            {
                var plan = RunFixture.Plan("checked-note");
                var host = Stubbed(plan, sentinel)
                    .Reply("node.sections.section_terms", "{\"concepts\":[]}")
                    .Render("deliverable.patient.patient_letter", new RenderOutcome(" \n\n", false, null));
                var recorder = new ListRecorder(() => host.Calls.Count);
                var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(inputs, FakeRunHost.Start);
                return await Delivered(recorder, result);
            }

            default:
            {
                var plan = RunFixture.Plan(name);
                var host = Stubbed(plan, sentinel);
                var recorder = new ListRecorder(() => host.Calls.Count);
                var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(inputs, FakeRunHost.Start);
                return await Delivered(recorder, result);
            }
        }
    }

    /// <summary>The record of a run: its rows, decisions and deliveries, under <see cref="Header"/>.</summary>
    public static string Record(ListRecorder recorder) => RecordDocument.Assemble(
        Header(),
        recorder.Rows.Select(RecordDocument.Of)
            .Concat(recorder.Decisions.Select(RecordDocument.Of))
            .Concat(recorder.Deliveries.Select(RecordDocument.Of)));

    private static async Task<Ran> Delivered(ListRecorder recorder, RunResult result)
    {
        Assert.True(result.State == RunState.Completed, $"{result.State}: {result.FailedKey} {result.Sentence}");
        var host = new FakeDeliveryHost();
        await FakeDeliveryHost.Drive(() => new DeliveryRunner(new RunLimits(), host, recorder).RunAsync(result.Deliverables));
        return new Ran(recorder, result);
    }

    private static RunInputs Inputs(string? sentinel)
    {
        var inputs = RunFixture.GoldenInputs();
        if (sentinel == null)
            return inputs;
        var carried = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal);
        // the draft's text, and the sections' titles (their kinds are enumerations, which the rules read)
        foreach (var (id, value) in inputs.Inputs)
            carried[id] = id switch
            {
                "consult_draft" => Carry(value, sentinel, _ => true),
                "sections" => Carry(value, sentinel, name => name == "title"),
                _ => value,
            };
        return inputs with { Inputs = carried };
    }

    private static FakeRunHost Stubbed(RunPlan plan, string? sentinel) =>
        new FakeRunHost(true, plan).Stub(call => Reply(plan, call, sentinel));

    /// <summary>The stub reply of <paramref name="call"/>, carrying <paramref name="sentinel"/> where it is still a value of its format.</summary>
    private static string Reply(RunPlan plan, AgentCall call, string? sentinel)
    {
        var reply = RunFixture.StubReply(plan, call);
        if (sentinel == null)
            return reply;
        var format = (call.Function == null ? plan.Entries : plan.Functions[call.Function].Entries)[call.EntryKey].Format;
        if (plan.Formats.IsText(format))
            return reply + " " + sentinel;
        var (value, parsed) = plan.Formats.Read(reply, format);
        if (!parsed)
            return reply;
        var carried = Carry(value, sentinel, _ => true);
        var text = CanonicalJson.Write(carried);
        var (again, ok) = plan.Formats.Read(text, format);
        return ok && plan.Formats.Validate(again, format).Valid ? text : reply;
    }

    /// <summary>
    /// A copy of <paramref name="node"/> with the sentinel appended to each string value held under a member
    /// <paramref name="named"/> accepts (a string in an array, or at the root, takes the name of the member above it).
    /// </summary>
    private static JsonNode? Carry(JsonNode? node, string sentinel, Func<string, bool> named, string member = "") => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, Carry(p.Value, sentinel, named, p.Key)))),
        JsonArray a => new JsonArray(a.Select(n => Carry(n, sentinel, named, member)).ToArray()),
        JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(named(member) ? s + " " + sentinel : s),
        _ => node?.DeepClone(),
    };
}
