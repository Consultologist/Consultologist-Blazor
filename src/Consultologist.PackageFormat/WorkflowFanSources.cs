namespace Consultologist.PackageFormat;

/// <summary>
/// A fan's source, as <c>forEach</c> spells it: <c>data:&lt;collection&gt;</c>
/// (below 25), <c>snippet.&lt;folder&gt;</c> (25, the object spelling — #1037;
/// <c>snippet:&lt;folder&gt;</c> was the 25 spelling before it and the readers
/// here still strip it, the validator refuses it), <c>input:&lt;id&gt;</c> /
/// <c>input.&lt;id&gt;</c>. The engine keys a collection snapshot by the bare
/// collection or folder id and an input fan by its literal forEach.
/// </summary>
public static class WorkflowFanSources
{
    /// <summary>The 25 spelling: <c>snippet.&lt;folder&gt;</c>.</summary>
    public const string SnippetPrefix = TemplateObjectReference.SnippetPrefix;

    /// <summary>The colon spelling 25 carried before #1037; read, never written.</summary>
    public const string SnippetColonPrefix = TemplateObjects.Snippet + ":";

    /// <summary>True for a snippet folder fan (25), either spelling.</summary>
    public static bool IsSnippetFan(string? forEach) =>
        forEach is not null
        && (forEach.StartsWith(SnippetPrefix, StringComparison.Ordinal) || forEach.StartsWith(SnippetColonPrefix, StringComparison.Ordinal));

    /// <summary>The bare collection or folder id of a data or snippet fan; an input fan (and anything else) keeps its literal spelling.</summary>
    public static string CollectionIdOf(string forEach) =>
        forEach.StartsWith(WorkflowNodeBindingSources.DataPrefix, StringComparison.Ordinal)
            ? forEach[WorkflowNodeBindingSources.DataPrefix.Length..]
            : forEach.StartsWith(SnippetPrefix, StringComparison.Ordinal)
                ? forEach[SnippetPrefix.Length..]
                : forEach.StartsWith(SnippetColonPrefix, StringComparison.Ordinal)
                    ? forEach[SnippetColonPrefix.Length..]
                    : forEach;
}
