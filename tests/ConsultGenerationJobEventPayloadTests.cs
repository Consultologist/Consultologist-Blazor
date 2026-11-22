using Azure.Data.Tables;
using Consultologist.Api.Jobs;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1051: an SSE catch-up event's payload split across Table properties — one
/// property holds 32K UTF-16 characters at most, a job's snapshot holds more.
/// The split never cuts a surrogate pair, the join is byte-identical, a row
/// written before #1051 (one property, no count) still reads, and the entity's
/// own cap is refused by name.
/// </summary>
public class ConsultGenerationJobEventPayloadTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(ConsultGenerationJobEventPayload.ChunkChars, 1)]
    [InlineData(ConsultGenerationJobEventPayload.ChunkChars + 1, 2)]
    [InlineData(46_117, 2)]
    [InlineData(ConsultGenerationJobEventPayload.MaxChars, ConsultGenerationJobEventPayload.MaxChunks)]
    public void Split_CutsIntoChunks_AndJoinsBack(int length, int expectedChunks)
    {
        var payload = string.Concat(Enumerable.Range(0, length).Select(i => (char)('a' + i % 26)));

        var chunks = ConsultGenerationJobEventPayload.Split(payload);

        Assert.Equal(expectedChunks, chunks.Count);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= ConsultGenerationJobEventPayload.ChunkChars));
        Assert.Equal(payload, string.Concat(chunks));

        var entity = new TableEntity("job", "000000000001");
        ConsultGenerationJobEventPayload.Write(entity, payload);

        Assert.Equal(expectedChunks, entity.GetInt32(ConsultGenerationJobEventPayload.ChunkCountProperty));
        Assert.Equal(payload, ConsultGenerationJobEventPayload.Read(entity));
    }

    [Fact]
    public void Split_NeverCutsASurrogatePair()
    {
        // The boundary of the first chunk lands on the high half of U+1F600; the
        // cut moves back one so each chunk is well-formed and the join is identical.
        var payload = new string('x', ConsultGenerationJobEventPayload.ChunkChars - 1) + "\U0001F600" + new string('y', 100);

        var chunks = ConsultGenerationJobEventPayload.Split(payload);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(ConsultGenerationJobEventPayload.ChunkChars - 1, chunks[0].Length);
        Assert.True(char.IsHighSurrogate(chunks[1][0]));
        Assert.Equal(payload, string.Concat(chunks));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(payload), System.Text.Encoding.UTF8.GetBytes(string.Concat(chunks)));
    }

    [Fact]
    public void Split_RefusesPastTheEntityCap_ByName()
    {
        var payload = new string('x', ConsultGenerationJobEventPayload.MaxChars + 1);

        var ex = Assert.Throws<InvalidOperationException>(() => ConsultGenerationJobEventPayload.Split(payload));

        Assert.Equal("Consult generation SSE event payload is 480001 characters; the store holds at most 480000 (16 chunks of 30000).", ex.Message);
    }

    [Fact]
    public void ARowWrittenBefore1051_ReadsItsOneProperty()
    {
        var legacy = new TableEntity("job", "000000000007")
        {
            ["AppUserId"] = "acct",
            ["Sequence"] = 7L,
            ["EventType"] = "block-completed",
            ["EventKey"] = "block-completed:hpi",
            ["PayloadJson"] = "{\"BlockId\":\"hpi\"}",
            ["CreatedAtUtc"] = new DateTimeOffset(2026, 10, 3, 19, 25, 0, TimeSpan.Zero),
        };

        var stored = TableConsultGenerationJobEventStore.ToStoredEvent(legacy);

        Assert.Equal("{\"BlockId\":\"hpi\"}", stored.PayloadJson);
        Assert.Equal(7, stored.Sequence);
        Assert.Equal("block-completed", stored.EventType);
        Assert.Equal("job:000000000007", stored.SseId);
    }

    [Fact]
    public void AChunkedRow_ReadsItsChunksInOrder_AndAMissingChunkIsLoud()
    {
        var entity = new TableEntity("job", "000000000002")
        {
            ["AppUserId"] = "acct",
            ["Sequence"] = 2L,
            ["EventType"] = "done",
            ["EventKey"] = "done",
            ["CreatedAtUtc"] = DateTimeOffset.UtcNow,
            [ConsultGenerationJobEventPayload.ChunkCountProperty] = 3,
            ["PayloadJson"] = "one,",
            ["PayloadJson1"] = "two,",
            ["PayloadJson2"] = "three",
        };

        Assert.Equal("one,two,three", TableConsultGenerationJobEventStore.ToStoredEvent(entity).PayloadJson);

        entity.Remove("PayloadJson1");
        var ex = Assert.Throws<InvalidOperationException>(() => TableConsultGenerationJobEventStore.ToStoredEvent(entity));
        Assert.Equal("Consult generation SSE event row job/000000000002 declares 3 payload chunks but has no PayloadJson1.", ex.Message);
    }

    [Fact]
    public void ARowFinishedPayload_IsItsKeySectionAndStatus_Only()
    {
        // #1506 (specVersion 31, area 09; D16): one candidate per stored row, decisions left out, keyed row:<key>, in key
        // order and before done; the payload names the row and its state word, never its contents.
        var response = new Consultologist.Api.Models.ConsultGenerationJobResponse(
            "job-31", "user-1", ConsultGenerationJobStatuses.Completed, 0, 0, 0,
            new Dictionary<string, string>(), new Dictionary<string, string>(), true, SchemaVersion: 12);
        var rows = new[]
        {
            new Consultologist.PackageFormat.V31.Run.RecordRow("node", "node.b#2", "{\"status\":\"Completed\",\"outputHash\":\"sha256:aa\"}"),
            new Consultologist.PackageFormat.V31.Run.RecordRow("decision", "node.b/if:pass.1", "{\"result\":\"true\"}"),
            new Consultologist.PackageFormat.V31.Run.RecordRow("delivery", "delivery:deliverable.note/email", "{\"result\":\"sent\"}"),
            new Consultologist.PackageFormat.V31.Run.RecordRow("deliverable", "deliverable.note", "{\"state\":\"blocked\",\"composedHash\":\"sha256:bb\"}"),
            new Consultologist.PackageFormat.V31.Run.RecordRow("check", "check.a", "{\"status\":\"Completed\"}"),
        };

        var candidates = ConsultGenerationJobs.CreateSemanticEventCandidates(response, rows);
        var finished = candidates.Where(c => c.EventType == ConsultGenerationRowEvents.EventName).ToList();

        Assert.Equal(["row:check.a", "row:deliverable.note", "row:delivery:deliverable.note/email", "row:node.b#2"], finished.Select(c => c.EventKey));
        Assert.Equal(
            [
                """{"key":"check.a","section":"check","status":"Completed"}""",
                """{"key":"deliverable.note","section":"deliverable","status":"blocked"}""",
                """{"key":"delivery:deliverable.note/email","section":"delivery","status":"sent"}""",
                """{"key":"node.b#2","section":"node","status":"Completed"}""",
            ],
            finished.Select(c => c.PayloadJson));
        Assert.Equal("done", candidates[^1].EventType);

        // without rows (every 5–30 job): no row event
        Assert.DoesNotContain(ConsultGenerationJobs.CreateSemanticEventCandidates(response), c => c.EventType == ConsultGenerationRowEvents.EventName);
    }
}
