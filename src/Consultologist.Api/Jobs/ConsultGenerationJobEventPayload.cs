using Azure.Data.Tables;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #1051: an SSE catch-up event's payload, split across Table properties.
/// Azure Table Storage caps one property at 64KB — 32K UTF-16 characters —
/// and a job's <c>snapshot</c>/<c>done</c> payload (the public job response,
/// nulls omitted, inputs emptied) runs past that on a package with many nodes:
/// a test/general job was ~46K characters when the write was refused on prod
/// (2026-10-16; the same refusal fired on 2026-10-03). The row keeps
/// <see cref="ChunkCountProperty"/> and the chunks in <see cref="PayloadProperty"/>,
/// <c>PayloadJson1</c> … <c>PayloadJson{N-1}</c>; a row written before #1051
/// carries no count and one <c>PayloadJson</c>. The cap is the entity's own
/// 1MB: sixteen chunks of thirty thousand characters, past which the store
/// refuses loudly (the SSE path then emits the events unpersisted).
/// </summary>
internal static class ConsultGenerationJobEventPayload
{
    /// <summary>Characters per property: under the 32K UTF-16 property limit with room for the row's other members.</summary>
    public const int ChunkChars = 30_000;

    /// <summary>Chunks per row: sixteen × thirty thousand UTF-16 characters ≈ 960KB, under the 1MB entity cap.</summary>
    public const int MaxChunks = 16;

    public const int MaxChars = ChunkChars * MaxChunks;

    public const string PayloadProperty = "PayloadJson";

    public const string ChunkCountProperty = "PayloadChunkCount";

    public static string PropertyName(int index) => index == 0 ? PayloadProperty : $"{PayloadProperty}{index}";

    /// <summary>The payload cut into chunks, never inside a surrogate pair; empty payload → one empty chunk.</summary>
    public static IReadOnlyList<string> Split(string payload)
    {
        if (payload.Length > MaxChars)
        {
            throw new InvalidOperationException(
                $"Consult generation SSE event payload is {payload.Length} characters; the store holds at most {MaxChars} ({MaxChunks} chunks of {ChunkChars}).");
        }

        if (payload.Length <= ChunkChars)
        {
            return new[] { payload };
        }

        var chunks = new List<string>();
        var start = 0;

        while (start < payload.Length)
        {
            var end = Math.Min(start + ChunkChars, payload.Length);

            // A boundary that lands between a surrogate pair moves back one, so
            // each chunk is well-formed on its own and the join is byte-identical.
            if (end < payload.Length && char.IsHighSurrogate(payload[end - 1]))
            {
                end--;
            }

            chunks.Add(payload[start..end]);
            start = end;
        }

        return chunks;
    }

    /// <summary>Writes the chunks and their count onto the row.</summary>
    public static void Write(TableEntity entity, string payload)
    {
        var chunks = Split(payload);
        entity[ChunkCountProperty] = chunks.Count;

        for (var index = 0; index < chunks.Count; index++)
        {
            entity[PropertyName(index)] = chunks[index];
        }
    }

    /// <summary>The payload joined back from the row — one property on a row written before #1051.</summary>
    public static string Read(TableEntity entity)
    {
        var count = entity.TryGetValue(ChunkCountProperty, out var stored) && stored is int declared ? declared : 1;

        if (count <= 1)
        {
            return entity.GetString(PayloadProperty) ?? string.Empty;
        }

        var joined = new System.Text.StringBuilder(count * ChunkChars);

        for (var index = 0; index < count; index++)
        {
            joined.Append(entity.GetString(PropertyName(index))
                ?? throw new InvalidOperationException(
                    $"Consult generation SSE event row {entity.PartitionKey}/{entity.RowKey} declares {count} payload chunks but has no {PropertyName(index)}."));
        }

        return joined.ToString();
    }
}
