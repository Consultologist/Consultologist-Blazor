using System.Globalization;

namespace Consultologist.PackageFormat;

/// <summary>
/// A fan over a caller-supplied array (package-format-v9-design.md § 5,
/// #426). One item shape for every element — <c>{ id, name, value }</c>:
///
/// - <c>id</c> is the element's zero-based index. Engine-minted, always:
///   array order is the caller's, is significant and is hashed (§ 8), so an
///   item's identity is stable across a replay for the same reason the hash
///   is. Never a declared field, because two sources of truth for identity
///   is how cross-node alignment starts disagreeing with per-item failure
///   keys.
/// - <c>name</c> is the input's label and the element's ordinal — "Prior
///   notes 2" — and <b>never the element's text</b>. A block name reaches the
///   job's history events, the run-rail roster and the SSE payload; an
///   element is patient data.
/// - <c>value</c> is the element: a scalar's canonical string, or an object
///   element's carrier (#423), which the renderer materialises (#425). A node
///   binds <c>item:value</c> for either kind, so no field name is reserved.
/// </summary>
public static class WorkflowInputFans
{
    public static bool IsInputFan(string? forEach) =>
        forEach?.StartsWith(WorkflowNodeBindingSources.InputPrefix, StringComparison.Ordinal) == true;

    public static string InputIdOf(string forEach) => forEach[WorkflowNodeBindingSources.InputPrefix.Length..];

    /// <summary>The snapshot key: the literal forEach string, which cannot collide with a data collection id.</summary>
    public static string Key(string inputId) => WorkflowNodeBindingSources.InputPrefix + inputId;

    /// <summary>The three keys an input fan's item carries.</summary>
    public static readonly IReadOnlyList<string> ItemFields = new[] { "id", "name", "value" };

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Items(WorkflowInputSpec input, ConsultInputValue? value)
    {
        if (value is null || !value.IsArray)
        {
            return Array.Empty<IReadOnlyDictionary<string, string>>();
        }

        return value.Elements!
            .Select((element, index) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["id"] = index.ToString(CultureInfo.InvariantCulture),
                ["name"] = $"{input.Label} {(index + 1).ToString(CultureInfo.InvariantCulture)}",
                // A null element never reaches a fan — the starter refuses it
                // by index — but the map is total rather than assuming so.
                ["value"] = element.IsNull ? string.Empty : element.HasCanonical ? element.Canonical : element.AsJson()
            })
            .ToList();
    }
}
