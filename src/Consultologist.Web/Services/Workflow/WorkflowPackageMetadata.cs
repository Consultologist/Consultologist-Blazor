namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// Mirrors Consultologist.Api.Workflow.WorkflowPackageMetadata (v9 § 4, #432,
/// #453): the bounds on a package's title, description and tags, in UTF-16
/// code units.
/// Pinned against the server's in SpecVersionMirrorTests.
/// </summary>
public static class WorkflowPackageMetadata
{
    public const int MaxTitleLength = 80;
    public const int MaxDescriptionLength = 500;
    public const int MaxTagLength = 32;
    public const int MaxTags = 20;
}
