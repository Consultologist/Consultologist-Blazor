using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>
/// #863 (v20): the inline slot marker a section prompt's MODEL output may contain,
/// <c>[[slot:&lt;macro-id&gt;]]</c>. The engine replaces each marker at document assembly
/// with the named macro's verbatim expanded text (see the runtime slot filler).
///
/// A distinct <c>[[…]]</c> delimiter, deliberately NOT <c>{{…}}</c>: the marker is
/// instructed from a Scriban prompt template (whose strict <c>{{ }}</c> would choke
/// on a brace token) and must never collide with the macro placeholder grammar
/// (<see cref="WorkflowMacroPlaceholders"/>). The marker lives only in model output —
/// never in a macro file or a rendered prompt — so neither Scriban nor the macro-file
/// validator ever sees it.
/// </summary>
public static class WorkflowSlotMarker
{
    /// <summary>Matches <c>[[slot:&lt;id&gt;]]</c>; group 1 is the macro id (snake_case ids per WorkflowDeclaredIds).</summary>
    public static readonly Regex Pattern = new(@"\[\[slot:([A-Za-z0-9_]+)\]\]", RegexOptions.Compiled);
}
