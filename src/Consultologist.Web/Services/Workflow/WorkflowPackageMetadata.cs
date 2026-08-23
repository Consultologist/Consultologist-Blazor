namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// Mirrors Consultologist.Api.Workflow.WorkflowPackageMetadata (v9 § 4, #432):
/// the bounds on a package's title and description, in UTF-16 code units.
/// Pinned against the server's in SpecVersionMirrorTests.
/// </summary>
public static class WorkflowPackageMetadata
{
    public const int MaxTitleLength = 80;
    public const int MaxDescriptionLength = 500;
}
