using System.Text.Json;

namespace Consultologist.Api.Tests;

/// <summary>v25 (B6 #1005): held values as the payload v2 carries them — JSON elements; a string answer is a string element.</summary>
internal static class FormResponseJson
{
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static JsonElement Text(string value) => Parse(JsonSerializer.Serialize(value));

    public static Dictionary<string, JsonElement> Strings(IReadOnlyDictionary<string, string> values) =>
        values.ToDictionary(pair => pair.Key, pair => Text(pair.Value), StringComparer.Ordinal);
}
