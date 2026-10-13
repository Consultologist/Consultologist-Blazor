namespace Consultologist.PackageFormat;

/// <summary>
/// A fan's source, as <c>forEach</c> spells it: <c>data:&lt;collection&gt;</c>
/// (below 25), <c>snippet:&lt;folder&gt;</c> (25), <c>input:&lt;id&gt;</c>. The
/// engine keys a collection snapshot by the bare collection or folder id and an
/// input fan by its literal forEach.
/// </summary>
public static class WorkflowFanSources
{
    public const string SnippetPrefix = TemplateObjects.Snippet + ":";

    /// <summary>True for a snippet folder fan (25).</summary>
    public static bool IsSnippetFan(string? forEach) =>
        forEach is not null && forEach.StartsWith(SnippetPrefix, StringComparison.Ordinal);

    /// <summary>The bare collection or folder id of a data or snippet fan; an input fan (and anything else) keeps its literal spelling.</summary>
    public static string CollectionIdOf(string forEach) =>
        forEach.StartsWith(WorkflowNodeBindingSources.DataPrefix, StringComparison.Ordinal)
            ? forEach[WorkflowNodeBindingSources.DataPrefix.Length..]
            : forEach.StartsWith(SnippetPrefix, StringComparison.Ordinal)
                ? forEach[SnippetPrefix.Length..]
                : forEach;
}
