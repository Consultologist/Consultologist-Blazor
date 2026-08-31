using Consultologist.Api.Models;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #557: fills a record's text from the outputs blob — the read half of the
/// migration. Pure over the payload; the caller decides WHETHER to hydrate
/// (pointer present, text not dropped). Pre-#557 records never reach here
/// and keep serving their entity fields.
/// </summary>
public static class JobOutputsHydration
{
    public static ConsultGenerationJobResponse Apply(ConsultGenerationJobResponse response, JobOutputsPayload payload)
    {
        var texts = payload.Documents?.ToDictionary(d => d.ResultId, d => d.Text, StringComparer.Ordinal);

        return response with
        {
            AssembledDocument = payload.AssembledDocument ?? response.AssembledDocument,
            AssembledDocuments = response.AssembledDocuments?
                .Select(d => texts != null && texts.TryGetValue(d.ResultId, out var text) ? d with { Text = text } : d)
                .ToList(),
            GeneratedBlocks = payload.BlockTexts is { Count: > 0 }
                ? response.GeneratedBlocks.ToDictionary(
                    b => b.Key,
                    b => payload.BlockTexts.TryGetValue(b.Key, out var text) ? text : b.Value,
                    StringComparer.Ordinal)
                : response.GeneratedBlocks
        };
    }

    /// <summary>The state-level overload — the starter's previous-run copy reads deliverable text off a source job's state.</summary>
    public static void Apply(ConsultGenerationJobState state, JobOutputsPayload payload)
    {
        state.AssembledDocument = payload.AssembledDocument ?? state.AssembledDocument;
        var texts = payload.Documents?.ToDictionary(d => d.ResultId, d => d.Text, StringComparer.Ordinal);

        foreach (var document in state.AssembledDocuments ?? Enumerable.Empty<ConsultGenerationResultDocumentState>())
        {
            if (texts != null && texts.TryGetValue(document.ResultId, out var text))
            {
                document.Text = text;
            }
        }

        if (payload.BlockTexts is { Count: > 0 })
        {
            foreach (var block in state.Blocks.Values)
            {
                if (payload.BlockTexts.TryGetValue(block.Id, out var text))
                {
                    block.GeneratedText = text;
                }
            }
        }
    }
}
