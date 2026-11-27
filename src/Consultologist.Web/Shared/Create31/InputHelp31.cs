using System.Text.Json;

namespace Consultologist.Web.Shared.Create31;

/// <summary>
/// #1509 (D4): each input's help text, read from <c>Resolve.inputs.&lt;id&gt;.description</c> of the content response the
/// Create page already loads for its rail; the lines are joined by line ends. An input with no description, or a
/// response that holds none, has no help.
/// </summary>
public static class InputHelp31
{
    public static IReadOnlyDictionary<string, string> From(JsonElement? resolve)
    {
        var help = new Dictionary<string, string>(StringComparer.Ordinal);
        if (resolve is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Object)
            return help;

        foreach (var input in inputs.EnumerateObject())
        {
            if (input.Value.ValueKind != JsonValueKind.Object
                || !input.Value.TryGetProperty("description", out var description) || description.ValueKind != JsonValueKind.Array)
                continue;
            var lines = description.EnumerateArray()
                .Where(line => line.ValueKind == JsonValueKind.String)
                .Select(line => line.GetString()!)
                .ToList();
            if (lines.Count > 0)
                help[input.Name] = string.Join("\n", lines);
        }

        return help;
    }
}

/// <summary>#1509: the Create page's area sentences for a 31 package's inputs (plan area 12, area sentences).</summary>
public static class Create31Sentences
{
    /// <summary>A12-006: an input whose format the page has no widget for; the run cannot be started here.</summary>
    public static string CannotBeFilledInHere(string label) =>
        $"This workflow can't be filled in here: this page has no field for '{label}'. Tell the operator.";
}
