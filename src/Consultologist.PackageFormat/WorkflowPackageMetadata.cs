namespace Consultologist.PackageFormat;

/// <summary>
/// The bounds on a package's title, description (v9 § 4, #432) and tags
/// (#453). Counted in
/// UTF-16 code units — string.Length — which is what the validator enforces;
/// the published JSON Schema's maxLength counts code points and is looser only
/// for astral characters. The engine is the authority. Mirrored by hand on the
/// client and pinned in SpecVersionMirrorTests.
/// </summary>
public static class WorkflowPackageMetadata
{
    public const int MaxTitleLength = 80;
    public const int MaxDescriptionLength = 500;

    // #453: per tag, and per package. Judgment calls recorded on the issue.
    public const int MaxTagLength = 32;
    public const int MaxTags = 20;
}
