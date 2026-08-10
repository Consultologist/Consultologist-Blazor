using System.Text.Json;
using System.Text.Json.Serialization;

namespace Consultologist.Web.Services.AI;

/// <summary>
/// Mirrors Consultologist.Api.Models.ConsultInputValue — one supplied input
/// value, typed on the wire (package-format-v8.md § wire form).
///
/// The mirror exists because the client sends this: JSON has no date, so
/// text, date and enum all travel as strings and only <c>boolean</c> travels
/// as something else. Sending <c>"true"</c> for a boolean slot is a 422, which
/// is why the setup form cannot keep using a string map.
/// </summary>
[JsonConverter(typeof(ConsultInputValueConverter))]
public sealed record ConsultInputValue(string? Text, bool? Flag)
{
    public static ConsultInputValue OfText(string text) => new(text, null);

    public static ConsultInputValue OfBoolean(bool value) => new(null, value);

    public static implicit operator ConsultInputValue(string text) => OfText(text);

    public bool IsBoolean => Flag.HasValue;

    public string Canonical => Flag.HasValue
        ? Flag.Value ? "true" : "false"
        : Text ?? string.Empty;
}

/// <summary>
/// Writes a JSON string for text and a JSON boolean for a flag, which is the
/// shape the API's converter reads.
/// </summary>
public sealed class ConsultInputValueConverter : JsonConverter<ConsultInputValue>
{
    /// <summary>
    /// Without this, System.Text.Json short-circuits a null token and never
    /// calls Read — the same trap the API-side converter documents.
    /// </summary>
    public override bool HandleNull => true;

    public override ConsultInputValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => ConsultInputValue.OfText(reader.GetString() ?? string.Empty),
            JsonTokenType.True => ConsultInputValue.OfBoolean(true),
            JsonTokenType.False => ConsultInputValue.OfBoolean(false),
            JsonTokenType.Null => ConsultInputValue.OfText(string.Empty),
            _ => throw new JsonException($"An input value must be a JSON string or boolean; got {reader.TokenType}.")
        };

    public override void Write(Utf8JsonWriter writer, ConsultInputValue value, JsonSerializerOptions options)
    {
        if (value.Flag.HasValue)
        {
            writer.WriteBooleanValue(value.Flag.Value);
            return;
        }

        writer.WriteStringValue(value.Text ?? string.Empty);
    }
}
