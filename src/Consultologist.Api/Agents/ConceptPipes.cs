using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Agents;

/// <summary>
/// v25 (B3 #1002, design § 1): the renderers bindings' <c>as:</c> named, as
/// template pipes over a node's text — <c>concept_context</c> is the attested,
/// byte-stable context rendering <c>as: concept-context</c> gave;
/// <c>concept_bullets</c> the bullet rendering <c>as: concept-bullets</c>
/// gave. Both parse the node's concept-list JSON with the node's concept
/// source, as the activity does for the node's own row, so the bytes are the
/// ones a binding produced.
/// </summary>
public static class ConceptPipes
{
    public static readonly IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>> All =
        new Dictionary<string, Func<TemplateNodeOutput, string>>(StringComparer.Ordinal)
        {
            [TemplatePipes.ConceptContext] = output => AgentSectionGenerator.FormatConcepts(Concepts(output)),
            [TemplatePipes.ConceptBullets] = output => ConsultGenerationConceptFormatter.Format(Concepts(output)),
        };

    private static IReadOnlyList<Models.ClinicalConcept> Concepts(TemplateNodeOutput output) =>
        ConceptOutputContract.Deserialize(output.Text, output.ConceptSource ?? "node");
}
