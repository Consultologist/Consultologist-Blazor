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
}
