using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 1.1, 11; part 09 § 3.7; D10, D11, D12, R14): a 31 job's rows are stored as they
/// finish. <see cref="StoredRunRecorder"/> buffers a round's rows and decisions and writes them at its flush in one
/// <c>write-record-rows-v31</c> call (their settled text first, then the rows), then counts them on the entity in one
/// <c>RecordV31Rows</c>, with the round's tokens. A replay's second write is a no-op; a row that differs fails the job with
/// A06-010 and leaves the stored row. Content is fictional (Margaret Okafor).
/// </summary>
public class StoredRunRecorderTests
{
    private static readonly string Hash = "sha256:" + new string('a', 64);

    private static RunRow Row(string key, JsonObject? tokens = null, string? content = null)
    {
        var detail = new JsonObject();
        if (tokens != null)
            detail["tokens"] = tokens;
        return new RunRow(key, key, PlanKind.Computed, RowState.Completed, null, Hash, detail,
            content == null ? null : new JsonObject { ["content"] = content });
    }

    private static RunDecision Decision(string owner) =>
        new(owner, "self", "not blank(input.consult_draft.content)", Hash, true, null, [], false, false, V31Job.Now);

    private static JsonObject Tokens(int input, int output) => new() { ["input"] = input, ["output"] = output, ["total"] = input + output };

    [Fact]
    public async Task ARoundsRows_GoInOneActivity_ThenOneEntityCall()
    {
        var job = V31Job.Of("nested-note");
        var context = job.Context();
        var recorder = new StoredRunRecorder(context, job.Input());

        await recorder.RowAsync(Row("node.a", Tokens(10, 4), "Seen Margaret Okafor today (fictional)."));
        await recorder.RowAsync(Row("node.b", Tokens(5, 1)));
        await recorder.RowAsync(Row("node.c"));
        await recorder.DecisionAsync(Decision("node.a"));
        await recorder.DecisionAsync(Decision("node.b"));
        Assert.Empty(context.Calls); // buffered: nothing is written before the flush

        await recorder.FlushAsync();

        Assert.Equal(
            [("activity", ConsultGenerationActivityNames.WriteRecordRowsV31), ("entity", nameof(ConsultGenerationJobEntity.RecordV31Rows))],
            context.Calls.Select(c => (c.Kind, c.Name)));
        var written = JsonSerializer.Deserialize<WriteRecordRowsV31Input>(context.Calls[0].InputJson)!;
        Assert.Equal((V31Job.JobId, "user-1", V31Job.AccountKind), (written.JobId, written.AppUserId, written.AccountKind));
        Assert.Equal(["node.a", "node.b", "node.c", "node.a/if:self", "node.b/if:self"], written.Rows.Select(r => r.Key));
        Assert.Equal(["node.a"], written.Settled.Keys);
        Assert.Equal(5, job.Rows.Table.All().Count);

        // the settled text is in its blob, never in the entity's call
        var text = await job.Blobs.ReadTextAsync(V31Job.AccountKind, "user-1", V31Job.JobId, "node.a", CancellationToken.None);
        Assert.Equal("Seen Margaret Okafor today (fictional).", (string?)text!["content"]);
        Assert.DoesNotContain("Okafor", context.Calls[1].InputJson, StringComparison.Ordinal);

        // the entity counts them and adds the round's tokens
        var progress = job.State.V31Progress!;
        Assert.Equal((3, 2, "node.c"), (progress.Rows, progress.Decisions, progress.CurrentKey));
        Assert.Equal(3, progress.States["completed"]);
        Assert.Equal(new ConsultGenerationV31Tokens(15, 5, 20), progress.Tokens);

        // a flush with nothing buffered calls nothing
        await recorder.FlushAsync();
        Assert.Equal(2, context.Calls.Count);
    }

    [Fact]
    public async Task ADeliveryRow_IsCountedByItsResult()
    {
        var job = V31Job.Of("nested-note");
        var context = job.Context();
        var recorder = new StoredRunRecorder(context, job.Input());

        await recorder.DeliveryAsync(new DeliveryRow("delivery:deliverable.note/emr", "deliverable.note", "emr", "not sent", "x", null, []));
        await recorder.FlushAsync();

        Assert.Equal(1, job.State.V31Progress!.States["delivery:not sent"]);
        var stored = Assert.Single(job.Rows.Table.All());
        Assert.Equal("delivery", stored.GetString("section"));
    }

    [Fact]
    public async Task ATextThatCannotBeStored_FailsTheWrite_BeforeAnyRow()
    {
        // the settled text goes first, so a stored row always has its text; a blob that cannot be written fails the activity
        var rows = new FakeRecordRowStore();
        var blobs = Substitute.For<IRecordBlobStore>();
        blobs.WriteTextAsync(default, default!, default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("The blob service is down.")));
        var activity = new WriteRecordRowsV31Activity(rows, blobs, new CapturingLogger<WriteRecordRowsV31Activity>());
        var input = new WriteRecordRowsV31Input(V31Job.JobId, "user-1", V31Job.AccountKind,
            [RecordDocument.Of(Row("node.a", content: "text"))],
            new Dictionary<string, JsonObject>(StringComparer.Ordinal) { ["node.a"] = new() { ["content"] = "text" } });

        await Assert.ThrowsAsync<InvalidOperationException>(() => activity.RunAsync(input, CancellationToken.None));

        Assert.Empty(rows.Table.All());
    }

    [Fact]
    public async Task AReplay_WritesNothingNew()
    {
        // Review Focus 1: the same job run again over the same stores (as a Durable replay writes every row again), its
        // calls answered in another order, so its rounds can split differently: every row is there once, and none conflicts
        var first = V31Job.Of("checked-note");
        await V31Job.RunAsync(first.Context());
        Assert.Equal(ConsultGenerationJobStatuses.Completed, first.State.Status);
        var stored = first.Rows.Table.All().Select(e => (e.RowKey, e.GetString("hash"))).ToList();
        Assert.NotEmpty(stored);

        var again = V31Job.Of("checked-note");
        again.Rows = first.Rows;
        again.Blobs = first.Blobs;
        await V31Job.DriveAsync(again.Context());

        Assert.Equal(ConsultGenerationJobStatuses.Completed, again.State.Status);
        Assert.Equal(stored, again.Rows.Table.All().Select(e => (e.RowKey, e.GetString("hash"))).ToList());
        Assert.DoesNotContain(again.RecordLog.Entries, e => e.Message.Contains("conflict", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADifferingRow_FailsTheJobAsAnEngineFault()
    {
        // a row stored for node.plan that is not the one this run writes: the write is refused, the job fails with A06-010
        var job = V31Job.Of("nested-note");
        const string before = "{\"stored\":\"before, for Margaret Okafor\"}";
        await job.Rows.WriteAsync(V31Job.JobId, [new RecordRow("node", "node.plan", before)], CancellationToken.None);
        var context = job.Context();

        await Assert.ThrowsAsync<RecordConflictException>(() => V31Job.RunAsync(context));

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        Assert.Equal(ConsultGenerationOrchestratorV31.EngineFault, job.State.FailureError);
        Assert.Equal(before, (await job.Rows.ReadAsync(V31Job.JobId, "node.plan", CancellationToken.None))!.Json);

        var conflict = Assert.Single(job.RecordLog.Entries, e => e.Message.StartsWith("V31 record conflict.", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, conflict.Level);
        Assert.Contains("node.plan", conflict.Message, StringComparison.Ordinal);
        Assert.Contains(V31Job.JobId, conflict.Message, StringComparison.Ordinal);
        var written = JsonSerializer.Deserialize<WriteRecordRowsV31Input>(
            context.Calls.First(c => c.Name == ConsultGenerationActivityNames.WriteRecordRowsV31 && c.InputJson.Contains("\"node.plan\"", StringComparison.Ordinal)).InputJson)!;
        var logged = job.RecordLog.Everything;
        Assert.DoesNotContain("Okafor", logged, StringComparison.Ordinal);
        Assert.DoesNotContain(written.Rows.Single(r => r.Key == "node.plan").Json, logged, StringComparison.Ordinal);
        Assert.DoesNotContain("stored", logged, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nested-note")]
    [InlineData("conditional")]
    [InlineData("functions")]
    [InlineData("checked-note")]
    public async Task A31Job_StoresEveryRow(string golden)
    {
        // the golden run through the orchestration, its renders the golden host's: the stored rows assemble to its record golden
        var job = V31Job.Of(golden);
        var context = job.Context()
            .On(ConsultGenerationActivityNames.RenderNodeV31, payload => new RenderOutcome($"Rendered {((RenderNodeV31Input)payload!).Call.Key}.", false, null));

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        var rows = await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None);
        // the golden's delivery host keeps its own clock, an hour after the run's (FakeDeliveryHost.Start); here both are the
        // orchestration's, so a delivery's times are the run's: nothing else in the golden is at the delivery host's hour
        var expected = File.ReadAllText(Path.Combine(RunGoldenTests.GoldenFolder(), $"{golden}.record.json")).Replace(
            JsonSerializer.Serialize(FakeDeliveryHost.Start.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            JsonSerializer.Serialize(V31Job.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            StringComparison.Ordinal);
        Assert.Equal(expected, RecordDocument.Assemble(RecordRuns.Header(), rows));
        Assert.Equal(rows.Count, job.State.V31Progress!.Rows + job.State.V31Progress.Decisions);
    }

    [Fact]
    public async Task TheSettledText_HoldsTheContent()
    {
        // checked-note on the default stubs: its note is blocked by proofread (R1), and kept with its composed text
        var job = V31Job.Of("checked-note");

        await V31Job.RunAsync(job.Context());

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        var rows = await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None);
        var agents = 0;
        foreach (var row in rows.Where(r => r.Section == "node"))
        {
            var json = JsonNode.Parse(row.Json)!;
            if ((string?)json["kind"] != "agent" || (string?)json["status"] != "Completed")
                continue;
            var text = await job.Blobs.ReadTextAsync(V31Job.AccountKind, "user-1", V31Job.JobId, row.Key, CancellationToken.None);
            if (SourceLayout.Text(text?["content"]) is not { } content)
                continue;
            Assert.Equal((string?)json["outputHash"], CanonicalJson.HashText(content));
            agents++;
        }

        Assert.True(agents > 0, "no completed agent row had settled content");

        var note = JsonNode.Parse(rows.Single(r => r.Key == "deliverable.note").Json)!;
        Assert.Equal("blocked", (string?)note["state"]);
        var composed = await job.Blobs.ReadTextAsync(V31Job.AccountKind, "user-1", V31Job.JobId, "deliverable.note", CancellationToken.None);
        Assert.Equal((string?)note["composedHash"], CanonicalJson.HashText(SourceLayout.Text(composed!["content"])!));
    }

    [Fact]
    public async Task Tokens_ReachTheUsageRow()
    {
        // R14: each attempt's tokens ride on its row; the rounds sum them on the entity, and the usage row has the job's sums
        var job = V31Job.Of("nested-note");
        var stub = job.Agent;
        job.Agent = call => stub(call) with { Detail = new JsonObject { ["tokens"] = Tokens(100, 7) } };
        var context = job.Context();

        await V31Job.RunAsync(context);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        var calls = V31Job.AgentCalls(context).Count;
        Assert.True(calls > 1);
        Assert.Equal(new ConsultGenerationV31Tokens(100 * calls, 7 * calls, 107 * calls), job.State.V31Progress!.Tokens);
        await job.Usage.Received(1).AddAsync("user-1", Arg.Any<string>(), 1, new ConsultTokenUsage(100 * calls, 7 * calls), Arg.Any<CancellationToken>());

        // the tokens summed from the stored rows are the same
        var rows = await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None);
        var summed = rows.Select(r => JsonNode.Parse(r.Json)!["tokens"]).OfType<JsonObject>().Sum(t => (int)t["input"]!);
        Assert.Equal(100 * calls, summed);
    }
}
