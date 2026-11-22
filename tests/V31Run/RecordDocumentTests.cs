using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 1.1, 2, 3; D10, D12, D13): the canonical record document, assembled from rows,
/// pinned for each golden run in <c>Golden/&lt;name&gt;.record.json</c> (with <c>GOLDEN_UPDATE_V31=1</c> the test rewrites it
/// instead, to be read before it is committed).
/// </summary>
public class RecordDocumentTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly DateTime At = new(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc);

    private static RecordRow Decision(string id) => new("decision", id, $"{{\"id\":\"{id}\"}}");

    private static RecordRow Delivery(string deliverable, string kind) =>
        RecordDocument.Of(new DeliveryRow($"delivery:{deliverable}/{kind}", deliverable, kind, "sent", null, At, [new DeliveryAttempt(At, "sent")]));

    private static RecordRow Deliverable(string key) => new("deliverable", key, $"{{\"key\":\"{key}\",\"state\":\"released\"}}");

    private static List<RecordRow> Small() =>
    [
        Decision("node.b/if:self"),
        new("node", "node.a", "{\"key\":\"node.a\"}"),
        Delivery("deliverable.note", "pdf"),
        Deliverable("deliverable.note"),
        Decision("deliverable.note/if:self"),
        new("check", "check.c", "{\"key\":\"check.c\"}"),
        Delivery("deliverable.note", "email"),
        Deliverable("deliverable.letter"),
        new("node", "node.0", "{\"key\":\"node.0\"}"),
        Decision("check.c/if:case"),
    ];

    [Fact]
    public void Assemble_IsCanonicalAndInKeyOrder()
    {
        var rows = Small();
        var text = RecordDocument.Assemble(RecordRuns.Header(), rows);
        Assert.Equal(text, RecordDocument.Assemble(RecordRuns.Header(), Enumerable.Reverse(rows)));
        Assert.Equal(text, RecordDocument.Assemble(RecordRuns.Header(), rows.OrderBy(r => r.Json, StringComparer.Ordinal)));
        Assert.Equal(text, CanonicalJson.Write(JsonNode.Parse(text)));

        var document = JsonNode.Parse(text)!;
        Assert.Equal(12, (int)document["schemaVersion"]!);
        Assert.Equal(
            ["check.c/if:case", "deliverable.note/if:self", "node.b/if:self"],
            document["decisions"]!.AsArray().Select(d => (string)d!["id"]!));
        Assert.Equal(["node.0", "node.a"], document["nodeOutputs"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["check.c"], document["checks"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["deliverable.letter", "deliverable.note"], document["deliverables"]!.AsArray().Select(d => (string)d!["key"]!));
        var note = document["deliverables"]![1]!;
        Assert.Equal(["delivery:deliverable.note/email", "delivery:deliverable.note/pdf"], note["deliveries"]!.AsArray().Select(d => (string)d!["key"]!));
        Assert.Empty(document["deliverables"]![0]!["deliveries"]!.AsArray());
        Assert.StartsWith(RecordDocument.Hash(text)[..7], "sha256:");
        Assert.Equal(CanonicalJson.HashText(text), RecordDocument.Hash(text));
    }

    [Fact]
    public void Assemble_TakesARepeatedRowOnce()
    {
        var rows = Small();
        var once = RecordDocument.Assemble(RecordRuns.Header(), rows);
        Assert.Equal(once, RecordDocument.Assemble(RecordRuns.Header(), rows.Concat(rows.Take(4))));
    }

    [Fact]
    public void Assemble_RefusesADifferingRow()
    {
        var rows = Small().Append(new RecordRow("node", "node.a", "{\"key\":\"node.a\",\"x\":1}"));
        Assert.Throws<InvalidOperationException>(() => RecordDocument.Assemble(RecordRuns.Header(), rows));
    }

    [Fact]
    public void Assemble_RefusesADeliveryWithNoDeliverable()
    {
        Assert.Throws<InvalidOperationException>(() => RecordDocument.Assemble(RecordRuns.Header(), [Delivery("deliverable.gone", "pdf")]));
    }

    [Theory]
    [InlineData("recordHash")]
    [InlineData("textDroppedAtUtc")]
    [InlineData("history")]
    [InlineData("decisions")]
    [InlineData("nodeOutputs")]
    [InlineData("checks")]
    [InlineData("deliverables")]
    public void Assemble_RefusesWhatChangesAfterTheEnd(string member)
    {
        var header = RecordRuns.Header();
        header[member] = "x";
        Assert.Throws<ArgumentException>(() => RecordDocument.Assemble(header, Small()));
    }

    [Fact]
    public void DeliveryRow_IsItsPartEightShape()
    {
        var row = new DeliveryRow("delivery:deliverable.note/emr", "deliverable.note", "emr", "failed", "failed: timeout", null,
            [new DeliveryAttempt(At, "failed: timeout")]);
        Assert.Equal(
            "{\"atUtc\":null,\"attempts\":[{\"atUtc\":\"2026-11-16T15:00:00.0000000Z\",\"outcome\":\"failed: timeout\"}],"
            + "\"deliverable\":\"deliverable.note\",\"key\":\"delivery:deliverable.note/emr\",\"kind\":\"emr\","
            + "\"reason\":\"failed: timeout\",\"result\":\"failed\"}",
            CanonicalJson.Write(row.ToJson()));
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public async Task TheGoldenRuns_AssembleToTheirRecordGoldens(string name)
    {
        var ran = await RecordRuns.Run(name);
        var text = RecordRuns.Record(ran.Recorder);
        Assert.Equal(text, CanonicalJson.Write(JsonNode.Parse(text)));

        var path = Path.Combine(RunGoldenTests.GoldenFolder(), $"{name}.record.json");
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
            File.WriteAllText(path, text, Utf8NoBom);

        Assert.True(File.Exists(path), $"{name}.record.json is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan().SequenceEqual(Utf8NoBom.GetBytes(text)), $"{name}.record.json is not what the runner's rows assemble to.");
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void TheGoldenFolder_HoldsARecordPerRunGolden()
    {
        Assert.Equal(
            Directory.GetFiles(RunGoldenTests.GoldenFolder(), "*.run.txt").Select(f => Path.GetFileName(f)![..^".run.txt".Length] + ".record.json")
                .Order(StringComparer.Ordinal),
            Directory.GetFiles(RunGoldenTests.GoldenFolder(), "*.record.json").Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task NoRecord_HoldsAValue()
    {
        // Review Focus 4: the sentinel is in the inputs and in every stub reply that can carry it; it is in the runs'
        // values (so the search is not empty), and in no row of the record.
        var seen = 0;
        foreach (var name in RecordRuns.Names())
        {
            var ran = await RecordRuns.Run(name, RecordRuns.Sentinel);
            seen += ran.Result.Values.Values.Count(v => CanonicalJson.Write(v).Contains(RecordRuns.Sentinel, StringComparison.Ordinal));
            seen += ran.Result.Deliverables.Count(d => d.Text?.Contains(RecordRuns.Sentinel, StringComparison.Ordinal) == true);

            var text = RecordRuns.Record(ran.Recorder);
            Assert.DoesNotContain("SENTINEL", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OKAFOR", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(seen > 0, "the sentinel reached no value: the search proves nothing.");
    }

    [Fact]
    public async Task NoRecord_DependsOnTheValues()
    {
        // The record holds hashes of values, so the same run on other values differs only there; and it is not equal to the
        // run's without them (the sentinel changed the hashes), which shows the values did flow through.
        var plain = RecordRuns.Record((await RecordRuns.Run("nested-note")).Recorder);
        var carried = RecordRuns.Record((await RecordRuns.Run("nested-note", RecordRuns.Sentinel)).Recorder);
        Assert.NotEqual(plain, carried);
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public async Task TheRunner_FlushesEachRound(string name)
    {
        var ran = await RecordRuns.Run(name);
        var events = ran.Recorder.Events;
        Assert.True(ran.Recorder.Flushes >= 1);
        Assert.Equal("flush", events[^1]);
        for (var i = 1; i < events.Count; i++)
            Assert.False(events[i] == "flush" && events[i - 1] == "flush", "a flush with nothing new before it");
        // each delivery row is followed at once by a flush
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].StartsWith("delivery ", StringComparison.Ordinal))
                Assert.Equal("flush", events[i + 1]);
        }
    }

    [Fact]
    public async Task TheRunner_FlushesMoreThanOnce_WhenARunTakesRounds()
    {
        var ran = await RecordRuns.Run("checked-note");
        Assert.True(ran.Recorder.Flushes > 1, $"{ran.Recorder.Flushes} flush(es)");
        // the rows of the run's end (held gates) are flushed too: the last thing before the last flush is a row or a decision
        Assert.Equal("flush", ran.Recorder.Events[^1]);
    }

    public static IEnumerable<object[]> Goldens() => RecordRuns.Names().Select(n => new object[] { n });
}
