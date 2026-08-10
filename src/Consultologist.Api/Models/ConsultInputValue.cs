using System.Text.Json;
using System.Text.Json.Serialization;

namespace Consultologist.Api.Models;

/// <summary>
/// One supplied input value, typed on the wire (package-format-v8-design.md
/// § 4). JSON has string, number, boolean, null, object and array — and no
/// date — so of v8's four declared types only <c>boolean</c> travels as
/// something other than a string: text, date and enum are all JSON strings,
/// a date by ISO convention.
///
/// A record rather than JsonElement on purpose. The request rides inside
/// ConsultGenerationOrchestrationInput, a durable payload replayed from JSON,
/// and a JsonElement there would break record equality and carry a document
/// lifetime into replay. This also makes a stored job self-describing: the
/// record says what type each value arrived as.
/// </summary>
[JsonConverter(typeof(ConsultInputValueConverter))]
public sealed record ConsultInputValue(string? Text, bool? Flag)
{
    public static ConsultInputValue OfText(string text) => new(text, null);

    public static ConsultInputValue OfBoolean(bool value) => new(null, value);

    /// <summary>
    /// A bare string means text. Compile-time strictness was never available
    /// here — the starter cannot know at the call site whether 'billable' is a
    /// boolean slot — so the type discipline lives where the declaration is
    /// known: the wire converter rejects a token JSON should not carry (400),
    /// and the starter rejects a value the declaration disagrees with (422).
    /// </summary>
    public static implicit operator ConsultInputValue(string text) => OfText(text);

    public bool IsBoolean => Flag.HasValue;

    /// <summary>A boolean is never blank — false is an answer, not an absence.</summary>
    public bool IsBlank => !Flag.HasValue && string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// The canonical string: what a prompt sees when the variable is not
    /// rendered as its own type, and what the v7 resolver map carries.
    /// </summary>
    public string Canonical => Flag.HasValue
        ? Flag.Value ? "true" : "false"
        : Text ?? string.Empty;
}

/// <summary>
/// Reads a JSON string as text and a JSON boolean as a flag. Anything else —
/// a number, an object, an array — is a **shape** error and throws, which the
/// HTTP door answers with 400: the request-shape rules are the 400s, and a
/// value disagreeing with the package's declaration is the 422 (that check
/// lives in the job starter, where the declaration is known and the slot can
/// be named).
/// </summary>
public sealed class ConsultInputValueConverter : JsonConverter<ConsultInputValue>
{
    /// <summary>
    /// Without this, System.Text.Json short-circuits a null token and never
    /// calls Read — putting a null into the map that every downstream check
    /// dereferences.
    /// </summary>
    public override bool HandleNull => true;

    public override ConsultInputValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => ConsultInputValue.OfText(reader.GetString() ?? string.Empty),
            JsonTokenType.True => ConsultInputValue.OfBoolean(true),
            JsonTokenType.False => ConsultInputValue.OfBoolean(false),
            // Blank text, not a C# null. A null value used to arrive as a
            // null string and read as blank — "required input missing" — and
            // handing back null instead would put a null into the map that
            // every downstream check dereferences.
            JsonTokenType.Null => ConsultInputValue.OfText(string.Empty),
            _ => throw new JsonException(
                $"An input value must be a JSON string or boolean; got {reader.TokenType}.")
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
