namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// Mirrors Consultologist.Api.Workflow.WorkflowInputFans (v9 § 5): a node may
/// fan over a caller-supplied array, and each item carries exactly these
/// fields. Pinned against the server's in SpecVersionMirrorTests.
/// </summary>
public static class WorkflowInputFans
{
    public const string Prefix = "input:";

    public static readonly IReadOnlyList<string> ItemFields = new[] { "id", "name", "value" };

    public static bool IsInputFan(string? forEach) =>
        forEach?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string InputIdOf(string forEach) => forEach[Prefix.Length..];
}
