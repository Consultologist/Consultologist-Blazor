namespace Consultologist.PackageFormat;

/// <summary>
/// The two byte-stable renderings of a concept list (package-format-v2.md "Concept
/// rendering"; #1138 moved them here from the Api): <see cref="Context"/> is what the
/// <c>concept_context</c> pipe produces (was AgentSectionGenerator.FormatConcepts), <see cref="Bullets"/>
/// what <c>concept_bullets</c> produces (was ConsultGenerationConceptFormatter.Format). The bytes
/// feed the model and are the deployed contract, pinned by ConceptFormatContractTests; the
/// engine and the editor render through these same functions, so the editor's sample is the
/// engine's bytes. <see cref="WorkflowConceptRenderers"/> names the ≤24 binding forms of the same two.
/// </summary>
public static class ConceptRenderers
{
    /// <summary>
    /// The context rendering: one line per concept —
    /// <c>- term (type) - id; active: True|False; source: source[ support: support]</c> — or <c>(none)</c>.
    /// </summary>
    public static string Context(IReadOnlyList<ClinicalConcept> concepts)
    {
        if (concepts.Count == 0)
        {
            return "(none)";
        }

        return string.Join(
            Environment.NewLine,
            concepts.Select(concept =>
            {
                var support = string.IsNullOrWhiteSpace(concept.Support) ? string.Empty : $" support: {concept.Support}";
                return $"- {concept.Term} ({concept.Type}) - {concept.Id}; active: {concept.IsActive}; source: {concept.Source}{support}";
            }));
    }

    /// <summary>
    /// The bullet rendering: <c>- term (type) - id</c>, suffixed <c> [not active SNOMED concept]</c>
    /// when inactive; a non-SNOMED concept is <c>- term [not SNOMED concept]</c>; <c>(none)</c> when empty.
    /// </summary>
    public static string Bullets(IReadOnlyList<ClinicalConcept> concepts)
    {
        return concepts.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, concepts.Select(BulletOf));
    }

    private static string BulletOf(ClinicalConcept concept)
    {
        if (!concept.IsSnomedConcept)
        {
            return $"- {concept.Term} [not SNOMED concept]";
        }

        var inactive = concept.IsActive ? string.Empty : " [not active SNOMED concept]";
        return $"- {concept.Term} ({concept.Type}) - {concept.Id}{inactive}";
    }
}

/// <summary>
/// #1138: the one concept-list fixture the editor's renderer pane and the tests render —
/// two fictional concepts so every mark appears once: an inactive SNOMED concept carrying a
/// support (<c>[not active SNOMED concept]</c>, <c> support: …</c>) and a non-SNOMED concept
/// (<c>[not SNOMED concept]</c>; its id is null, as the format allows).
/// </summary>
public static class ConceptSamples
{
    /// <summary>The source the tests render the sample with; the editor uses the package's own node's.</summary>
    public const string Source = "extract";

    /// <summary>How many concepts the sample holds — the preview pick's label says so.</summary>
    public const int Count = 2;

    /// <summary>The sample as a concept_list node's output text.</summary>
    public const string ConceptListJson = """
        {
          "concepts": [
            { "term": "Diabetes mellitus", "type": "disorder", "id": "73211009", "isSnomedConcept": true, "isActive": false, "support": "long-standing type 2 diabetes" },
            { "term": "Family support strong", "type": "finding", "id": null, "isSnomedConcept": false, "isActive": false, "support": null }
          ]
        }
        """;
}
