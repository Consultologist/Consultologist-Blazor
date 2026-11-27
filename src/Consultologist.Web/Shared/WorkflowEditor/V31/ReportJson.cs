using System.Text.Json;

namespace Consultologist.Web.Shared.WorkflowEditor.V31;

/// <summary>
/// #1509 (Task 24; D10, D11): the lenient reads of a stored report. A member that is missing or of another kind reads as
/// nothing; no read throws, so a report written by another version still shows what it has.
/// </summary>
internal static class ReportJson
{
    /// <summary>A string member's text, or null when the member is missing, null or not a string.</summary>
    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A number member as written, or null.</summary>
    public static string? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : null;

    /// <summary>A boolean member, or null.</summary>
    public static bool? Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>An object member, or null.</summary>
    public static JsonElement? Object(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    /// <summary>An array member's items; empty when the member is missing or not an array.</summary>
    public static IReadOnlyList<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : Array.Empty<JsonElement>();

    /// <summary>An array member's strings (items of another kind are left out).</summary>
    public static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        Items(element, name).Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList();
}
