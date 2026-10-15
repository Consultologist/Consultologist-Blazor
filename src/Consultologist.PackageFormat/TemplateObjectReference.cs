using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>
/// An object reference as a manifest VALUE spells it at 25 (#1037, design § 16):
/// <c>node.&lt;id&gt;</c>, <c>snippet.&lt;folder&gt;</c>, <c>input.&lt;id&gt;</c> — the
/// template objects' spelling, so a <c>with</c> parameter, a <c>forEach</c>, an
/// <c>aggregate[]</c> source, a deliverable's <c>node</c> and its <c>check</c> all
/// read as a template would read them. Below 25 the same keys spell the binding
/// grammar (<c>node:&lt;id&gt;</c>, <c>data:&lt;id&gt;</c>, <c>input:&lt;id&gt;</c>);
/// the readers that serve both versions strip either through <see cref="NodeIdOf"/>.
/// </summary>
public static class TemplateObjectReference
{
    // The id in the manifest's own spelling (a node id may carry a hyphen below
    // the 25 id rule; the rule, not the reference grammar, refuses it).
    private static readonly Regex Pattern = new("^(?<object>[a-z]+)\\.(?<id>[a-z0-9][a-z0-9_-]*)(?<rest>(\\.[a-zA-Z0-9_]+)*)$", RegexOptions.Compiled);

    public const string NodePrefix = TemplateObjects.Node + ".";
    public const string SnippetPrefix = TemplateObjects.Snippet + ".";
    public const string InputPrefix = TemplateObjects.Input + ".";

    public static string Node(string id) => NodePrefix + id;

    public static string Snippet(string id) => SnippetPrefix + id;

    public static string Input(string id) => InputPrefix + id;

    /// <summary>Splits <c>object.id[.rest]</c>; false for anything else (a literal, a colon reference, blank).</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out string obj, out string id, out string rest)
    {
        var match = text is null ? null : Pattern.Match(text);

        if (match is not { Success: true })
        {
            obj = id = rest = string.Empty;
            return false;
        }

        obj = match.Groups["object"].Value;
        id = match.Groups["id"].Value;
        rest = match.Groups["rest"].Value;
        return true;
    }

    /// <summary>True for a bare <c>&lt;object&gt;.&lt;id&gt;</c> of the given object (no further member).</summary>
    public static bool IsBare(string? text, string obj, out string id)
    {
        if (TryParse(text, out var found, out id, out var rest) && found == obj && rest.Length == 0)
        {
            return true;
        }

        id = string.Empty;
        return false;
    }

    /// <summary>True when the text spells a reference in the ≤24 binding grammar (<c>node:</c>, <c>data:</c>, <c>input:</c>, <c>item:</c>, <c>snippet:</c>).</summary>
    public static bool IsColonSpelling(string? text) =>
        text is not null && Regex.IsMatch(text, "^(node|data|input|item|snippet):");

    /// <summary>
    /// The node id a reference names whichever way it is spelled — <c>node:x</c>
    /// below 25, <c>node.x</c> at 25 — or null when it is neither.
    /// </summary>
    public static string? NodeIdOf(string? reference)
    {
        if (reference is null)
        {
            return null;
        }

        if (reference.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal))
        {
            return reference.Length > WorkflowNodeBindingSources.NodePrefix.Length ? reference[WorkflowNodeBindingSources.NodePrefix.Length..] : null;
        }

        return IsBare(reference, TemplateObjects.Node, out var id) ? id : null;
    }
}
