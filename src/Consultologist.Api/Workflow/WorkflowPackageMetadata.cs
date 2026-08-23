namespace Consultologist.Api.Workflow;

/// <summary>
/// The bounds on a package's title and description (v9 § 4, #432). Counted in
/// UTF-16 code units — string.Length — which is what the validator enforces;
/// the published JSON Schema's maxLength counts code points and is looser only
/// for astral characters. The engine is the authority. Mirrored by hand on the
/// client and pinned in SpecVersionMirrorTests.
/// </summary>
public static class WorkflowPackageMetadata
{
    public const int MaxTitleLength = 80;
    public const int MaxDescriptionLength = 500;
}
