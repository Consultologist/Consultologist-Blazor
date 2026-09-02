using Consultologist.Api.Models;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v12 #624 (design § 13): the check node's operations — pure set arithmetic
/// over two recorded concept lists, no model, no clock, no re-parsing;
/// deterministic on replay by construction, the fourth pure-over-snapshot
/// seam beside the renderer, the expander and the signature appender.
/// </summary>
internal static class ConsultCheckExecutor
{
    /// <summary>
    /// terms-subset: every codable input term is covered by the document's
    /// terms — comparison by active SNOMED concept id, insensitive to
    /// surface wording. A concept is codable when it is an active SNOMED
    /// concept WITH an id: ConceptOutputContract coalesces a null id to ""
    /// (never null), so the empty string is the uncoded spelling here.
    /// Uncodables are excluded from the subset test and named in Untested —
    /// never silently dropped.
    /// </summary>
    public static ConsultCheckOutcome TermsSubset(
        IReadOnlyList<ClinicalConcept>? of,
        IReadOnlyList<ClinicalConcept>? inDocument)
    {
        static bool Codable(ClinicalConcept concept) =>
            concept.IsSnomedConcept && concept.IsActive && !string.IsNullOrEmpty(concept.Id);

        var ofConcepts = of ?? Array.Empty<ClinicalConcept>();
        var documentConcepts = inDocument ?? Array.Empty<ClinicalConcept>();

        var documentIds = documentConcepts
            .Where(Codable)
            .Select(concept => concept.Id)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = ofConcepts
            .Where(Codable)
            .Where(concept => !documentIds.Contains(concept.Id))
            .GroupBy(concept => concept.Id, StringComparer.Ordinal)
            .Select(group => group.First().Term)
            .ToList();

        var untested = ofConcepts.Concat(documentConcepts)
            .Where(concept => !Codable(concept))
            .Select(concept => concept.Term)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ConsultCheckOutcome(
            uncovered.Count == 0,
            uncovered.Count > 0 ? uncovered : null,
            untested.Count > 0 ? untested : null);
    }
}
