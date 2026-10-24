namespace Consultologist.PackageFormat;

/// <summary>
/// One concept of a concept list as the engine holds it: the term, its SNOMED CT
/// type and id (empty when the finding maps to no concept), whether it is a SNOMED
/// concept and active, the concept source stamped at parse time (the extracting
/// node's id or a well-known source — <see cref="WorkflowNodeDefaults.WellKnownConceptSources"/>),
/// and the supporting phrase when the agent gave one. #1138: moved from the Api
/// (Consultologist.Api.Models) with the parser and the renderers; the JSON it
/// serializes to is unchanged — property names only, no type names.
/// </summary>
public sealed record ClinicalConcept(
    string Term,
    string Type,
    string Id,
    bool IsSnomedConcept,
    bool IsActive,
    string Source,
    string? Support = null);
