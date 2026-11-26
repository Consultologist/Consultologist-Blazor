using System.Text.Json;
using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Shared.History31;

/// <summary>
/// #1509 (Task 16): the rows of one job fetched by key (<c>Rows?key=</c>), for one view of it. Each key is fetched once;
/// a later ask is answered from the same fetch. A fetch that failed is forgotten, so the next ask tries again. A row's
/// text is shown only while the route still gives it (<see cref="RecordRowText.TextDeleted"/> otherwise).
/// </summary>
public sealed class RowText31
{
    private readonly IAIEndpointService _ai;
    private readonly string _jobId;
    private readonly Dictionary<string, Task<RecordRowText?>> _fetches = new(StringComparer.Ordinal);

    public RowText31(IAIEndpointService ai, string jobId)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _jobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
    }

    /// <summary>The row under <paramref name="key"/> with its retained text; null when the job has no such row (404).</summary>
    public Task<RecordRowText?> Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_fetches.TryGetValue(key, out var held) && !held.IsFaulted && !held.IsCanceled)
        {
            return held;
        }

        var fetch = _ai.GetJobRowAsync(_jobId, key);
        _fetches[key] = fetch;
        return fetch;
    }

    /// <summary>A fetched row's produced or composed text (<c>text.content</c>); null when none is offered or it was dropped.</summary>
    public static string? Content(RecordRowText? row) =>
        row is { TextDeleted: false, Text: { ValueKind: JsonValueKind.Object } text }
        && text.TryGetProperty("content", out var content)
        && content.ValueKind == JsonValueKind.String
            ? content.GetString()
            : null;
}
