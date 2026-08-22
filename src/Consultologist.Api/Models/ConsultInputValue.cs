using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Consultologist.Api.Models;

/// <summary>
/// The JSON kind a supplied value arrived as. Closed: the converter admits
/// exactly these, and the engine dispatches on nothing else.
/// </summary>
public enum ConsultInputKind
{
    Text,
    Boolean,
    Number,
    Object,
    Array,

    /// <summary>
    /// A JSON null inside structure — an array element or an object field.
    /// Carried rather than refused at the wire, because whether a null
    /// element is acceptable is a question for the declaration, and the
    /// starter is where the declaration is known (v9 § 4: a 422 naming the
    /// slot). Never produced at the top of the map: a top-level null reads as
    /// blank text, as it has since v8.
    /// </summary>
    Null
}

/// <summary>One field of an object value, in the order the caller sent it.</summary>
public sealed record ConsultInputEntry(string Id, ConsultInputValue Value);

/// <summary>
/// One supplied input value, typed on the wire (package-format-v9-design.md
/// § 4). JSON has string, number, boolean, null, object and array — and no
/// date — so text, date and enum all travel as strings, a boolean as itself,
/// and from v9 a number as a number and structure as structure.
///
/// A record rather than JsonElement on purpose. The request rides inside
/// ConsultGenerationOrchestrationInput, a durable payload replayed from JSON,
/// and a JsonElement there would break record equality and carry a document
/// lifetime into replay. This also makes a stored job self-describing: the
/// record says what kind each value arrived as.
///
/// Equality is structural and over the <em>spelling</em> of a number, not its
/// decimal value: 1.5 and 1.50 serialise to different bytes and hash
/// differently, so they are different values here too.
/// </summary>
[JsonConverter(typeof(ConsultInputValueConverter))]
public sealed record ConsultInputValue
{
    private static readonly ConsultInputValue NullInstance = new(ConsultInputKind.Null);

    private ConsultInputValue(
        ConsultInputKind kind,
        string? text = null,
        bool? flag = null,
        string? number = null,
        decimal? numberValue = null,
        IReadOnlyList<ConsultInputEntry>? fields = null,
        IReadOnlyList<ConsultInputValue>? elements = null)
    {
        Kind = kind;
        Text = text;
        Flag = flag;
        Number = number;
        NumberValue = numberValue;
        Fields = fields;
        Elements = elements;
    }

    public ConsultInputKind Kind { get; }

    /// <summary>The text, when Kind is Text.</summary>
    public string? Text { get; }

    /// <summary>The flag, when Kind is Boolean.</summary>
    public bool? Flag { get; }

    /// <summary>
    /// The number's spelling exactly as the caller sent it — "1.50", not
    /// "1.5". Trimming would mean provenance records a value nobody sent
    /// (v9 § 4).
    /// </summary>
    public string? Number { get; }

    /// <summary>The decimal those digits denote, for comparison.</summary>
    public decimal? NumberValue { get; }

    /// <summary>The object's fields in supplied order, ids unique. Values are scalars or Null.</summary>
    public IReadOnlyList<ConsultInputEntry>? Fields { get; }

    /// <summary>The array's elements in supplied order: scalars, one-level objects, or Null.</summary>
    public IReadOnlyList<ConsultInputValue>? Elements { get; }

    public static ConsultInputValue OfText(string text) => new(ConsultInputKind.Text, text: text);

    public static ConsultInputValue OfBoolean(bool value) => new(ConsultInputKind.Boolean, flag: value);

    /// <summary>
    /// The one rule that makes "the digits as sent" true by construction: the
    /// spelling must parse as a decimal with no exponent, and that decimal
    /// must print back as exactly the spelling. One check refuses exponent
    /// form, a value outside decimal's range, and precision beyond what
    /// decimal keeps — anything the engine could not carry faithfully.
    /// </summary>
    public static bool TryParseNumber(string spelling, out ConsultInputValue value)
    {
        value = null!;

        if (string.IsNullOrEmpty(spelling))
        {
            return false;
        }

        if (!decimal.TryParse(
                spelling,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.ToString(CultureInfo.InvariantCulture), spelling, StringComparison.Ordinal))
        {
            return false;
        }

        value = new ConsultInputValue(ConsultInputKind.Number, number: spelling, numberValue: parsed);
        return true;
    }

    public static ConsultInputValue OfNumber(string spelling) =>
        TryParseNumber(spelling, out var value)
            ? value
            : throw new ArgumentException($"'{spelling}' is not a plain decimal the format carries.", nameof(spelling));

    /// <summary>An object of scalar-or-null fields. Ids must be unique; structure may not nest.</summary>
    public static ConsultInputValue OfObject(IEnumerable<ConsultInputEntry> fields)
    {
        var list = fields.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in list)
        {
            if (!seen.Add(field.Id))
            {
                throw new ArgumentException($"The object repeats the field '{field.Id}'.", nameof(fields));
            }

            if (field.Value.IsStructured)
            {
                throw new ArgumentException($"The field '{field.Id}' holds structure; a field holds a scalar or null.", nameof(fields));
            }
        }

        return new ConsultInputValue(ConsultInputKind.Object, fields: list);
    }

    /// <summary>An array of scalars, one-level objects, or nulls. An array may not hold an array.</summary>
    public static ConsultInputValue OfArray(IEnumerable<ConsultInputValue> elements)
    {
        var list = elements.ToList();

        if (list.Any(element => element.IsArray))
        {
            throw new ArgumentException("An array may not hold an array.", nameof(elements));
        }

        return new ConsultInputValue(ConsultInputKind.Array, elements: list);
    }

    /// <summary>A JSON null inside structure. Never legal at the top of the map.</summary>
    public static ConsultInputValue NullElement => NullInstance;

    /// <summary>
    /// The carrier form: this value as its wire JSON, for the road between the
    /// job starter and the renderer, which is a string map at every durable
    /// hop (v9 § 10). A scalar travels as its canonical string; structure has
    /// none, so it travels as this — field order as supplied — and the
    /// renderer reconstructs it with <see cref="FromJson"/>, told to by the
    /// variable's declared type. The same mechanism v8 used for a date and a
    /// boolean, now that structure has a string that round-trips.
    /// </summary>
    public string AsJson() => JsonSerializer.Serialize(this);

    /// <summary>
    /// The carrier read back. This is the wire reader, so a malformed carrier
    /// is refused exactly as a malformed request would be.
    /// </summary>
    public static ConsultInputValue FromJson(string json) =>
        JsonSerializer.Deserialize<ConsultInputValue>(json)
        ?? throw new InvalidOperationException("A carrier string read back as nothing.");

    /// <summary>
    /// A bare string means text. Compile-time strictness was never available
    /// here — the starter cannot know at the call site whether 'billable' is a
    /// boolean slot — so the type discipline lives where the declaration is
    /// known: the wire converter rejects a shape JSON should not carry (400),
    /// and the starter rejects a value the declaration disagrees with (422).
    /// </summary>
    public static implicit operator ConsultInputValue(string text) => OfText(text);

    public bool IsBoolean => Kind == ConsultInputKind.Boolean;

    public bool IsNumber => Kind == ConsultInputKind.Number;

    public bool IsObject => Kind == ConsultInputKind.Object;

    public bool IsArray => Kind == ConsultInputKind.Array;

    public bool IsNull => Kind == ConsultInputKind.Null;

    public bool IsStructured => Kind is ConsultInputKind.Object or ConsultInputKind.Array;

    /// <summary>
    /// Only text can be blank. A boolean is an answer, a number is an answer,
    /// and an empty array is present and empty (v9 § 4) — not absent, so a
    /// required slot holding one is refused by the starter naming the slot,
    /// never waved through as "supplied".
    /// </summary>
    public bool IsBlank => Kind == ConsultInputKind.Text && string.IsNullOrWhiteSpace(Text);

    /// <summary>Whether <see cref="Canonical"/> has an answer.</summary>
    public bool HasCanonical => Kind is ConsultInputKind.Text or ConsultInputKind.Boolean or ConsultInputKind.Number;

    /// <summary>
    /// The canonical string: what a prompt sees when the variable is not
    /// rendered as its own type, and what the v7 resolver map carries. Text,
    /// "true"/"false", or a number's spelling.
    ///
    /// Unrepresentable for an object, an array or a null element — deliberately
    /// a throw rather than an empty string, because an empty string here would
    /// let structure reach a string-only renderer silently (v9 § 10). Every
    /// caller is guarded by a refusal that runs first.
    /// </summary>
    public string Canonical => Kind switch
    {
        ConsultInputKind.Text => Text ?? string.Empty,
        ConsultInputKind.Boolean => Flag!.Value ? "true" : "false",
        ConsultInputKind.Number => Number!,
        _ => throw new InvalidOperationException($"A {Described} input value has no canonical string.")
    };

    /// <summary>
    /// The kind, as a refusal message names it: "a number", "an object". Never
    /// the value, which may be patient data.
    /// </summary>
    public string Described => Kind switch
    {
        ConsultInputKind.Text => "text",
        ConsultInputKind.Boolean => "a boolean",
        ConsultInputKind.Number => "a number",
        ConsultInputKind.Object => "an object",
        ConsultInputKind.Array => "an array",
        _ => "a null"
    };

    /// <summary>
    /// Characters of caller text inside the value, whatever its shape — for a
    /// log line, never for the cap, which is applied per text scalar.
    /// </summary>
    public int TextLength => Kind switch
    {
        ConsultInputKind.Text => Text?.Length ?? 0,
        ConsultInputKind.Boolean => Canonical.Length,
        ConsultInputKind.Number => Number!.Length,
        ConsultInputKind.Object => Fields!.Sum(entry => entry.Value.TextLength),
        ConsultInputKind.Array => Elements!.Sum(element => element.TextLength),
        _ => 0
    };

    public bool Equals(ConsultInputValue? other) =>
        other is not null
        && Kind == other.Kind
        && string.Equals(Text, other.Text, StringComparison.Ordinal)
        && Flag == other.Flag
        && string.Equals(Number, other.Number, StringComparison.Ordinal)
        && SameSequence(Fields, other.Fields)
        && SameSequence(Elements, other.Elements);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Text, StringComparer.Ordinal);
        hash.Add(Flag);
        hash.Add(Number, StringComparer.Ordinal);

        if (Fields is not null)
        {
            foreach (var field in Fields)
            {
                hash.Add(field);
            }
        }

        if (Elements is not null)
        {
            foreach (var element in Elements)
            {
                hash.Add(element);
            }
        }

        return hash.ToHashCode();
    }

    private static bool SameSequence<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);
}

/// <summary>
/// A shape the wire converter refuses: thrown only by
/// <see cref="ConsultInputValueConverter"/>, so the HTTP door can surface this
/// message as the 400 while every other JsonException — truncated bodies,
/// grammar errors, .NET's own wording — stays behind the generic one. The
/// message names the token kind and where it sat, never the value.
/// </summary>
public sealed class ConsultInputShapeException : JsonException
{
    public ConsultInputShapeException(string message) : base(message)
    {
    }
}

/// <summary>
/// Reads a JSON string as text, a boolean as a flag, a plain-decimal number as
/// a number, an object of scalars as an object and an array of scalars or
/// one-level objects as an array. Anything else — an exponent, structure
/// nested past one level, a repeated key — is a <b>shape</b> error and throws
/// <see cref="ConsultInputShapeException"/>, which the HTTP door answers with
/// 400. A value disagreeing with the package's declaration is the 422, and
/// that check lives in the job starter, where the declaration is known and the
/// slot can be named.
/// </summary>
public sealed class ConsultInputValueConverter : JsonConverter<ConsultInputValue>
{
    private enum Site
    {
        Top,
        Element,
        Field
    }

    /// <summary>
    /// Without this, System.Text.Json short-circuits a null token and never
    /// calls Read — putting a null into the map that every downstream check
    /// dereferences.
    /// </summary>
    public override bool HandleNull => true;

    public override ConsultInputValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadValue(ref reader, Site.Top, where: null);

    private static ConsultInputValue ReadValue(ref Utf8JsonReader reader, Site site, string? where)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return ConsultInputValue.OfText(reader.GetString() ?? string.Empty);

            case JsonTokenType.True:
                return ConsultInputValue.OfBoolean(true);

            case JsonTokenType.False:
                return ConsultInputValue.OfBoolean(false);

            case JsonTokenType.Null:
                // At the top, blank text and not a C# null: a null value used to
                // arrive as a null string and read as blank — "required input
                // missing" — and handing back null would put a null into the map
                // that every downstream check dereferences. Inside structure it
                // is carried as itself, for the starter to refuse with the slot
                // named (v9 § 4).
                return site == Site.Top ? ConsultInputValue.OfText(string.Empty) : ConsultInputValue.NullElement;

            case JsonTokenType.Number:
                return ReadNumber(ref reader, where);

            case JsonTokenType.StartObject:
                if (site == Site.Field)
                {
                    throw Shape($"{where} is an object; a field holds text, a number, a boolean or null, never structure.");
                }

                return ReadObject(ref reader, where);

            case JsonTokenType.StartArray:
                if (site != Site.Top)
                {
                    throw Shape(site == Site.Element
                        ? $"{where} is an array; an array may hold text, numbers, booleans, nulls and objects, never another array."
                        : $"{where} is an array; a field holds text, a number, a boolean or null, never structure.");
                }

                return ReadArray(ref reader);

            default:
                throw Shape($"An input value must be a JSON string, number, boolean, null, object or array; got {reader.TokenType}.");
        }
    }

    private static ConsultInputValue ReadNumber(ref Utf8JsonReader reader, string? where)
    {
        var raw = reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan.ToArray();
        var spelling = Encoding.UTF8.GetString(raw);

        return ConsultInputValue.TryParseNumber(spelling, out var value)
            ? value
            : throw Shape($"{where ?? "An input value"} is not a plain decimal: no exponent form, and within decimal's range and precision.");
    }

    private static ConsultInputValue ReadObject(ref Utf8JsonReader reader, string? where)
    {
        var fields = new List<ConsultInputEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var owner = where ?? "An input value";

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return ConsultInputValue.OfObject(fields);
            }

            // The serializer hands a converter the whole value, so after a
            // property name the value token is always there to read.
            var id = reader.GetString() ?? string.Empty;
            reader.Read();

            if (!ids.Add(id))
            {
                throw Shape($"{owner} repeats the field '{id}'.");
            }

            fields.Add(new ConsultInputEntry(id, ReadValue(ref reader, Site.Field, $"{owner}'s field '{id}'")));
        }

        throw Shape($"{owner} is an object that does not end.");
    }

    private static ConsultInputValue ReadArray(ref Utf8JsonReader reader)
    {
        var elements = new List<ConsultInputValue>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return ConsultInputValue.OfArray(elements);
            }

            elements.Add(ReadValue(ref reader, Site.Element, $"An input value's element {elements.Count}"));
        }

        throw Shape("An input value is an array that does not end.");
    }

    private static ConsultInputShapeException Shape(string message) => new(message);

    public override void Write(Utf8JsonWriter writer, ConsultInputValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case ConsultInputKind.Boolean:
                writer.WriteBooleanValue(value.Flag!.Value);
                break;

            case ConsultInputKind.Number:
                // The spelling, verbatim: what was read is what is written, so
                // the durable payload replays to the same value and the hash
                // sees the digits the caller sent.
                writer.WriteRawValue(value.Number!);
                break;

            case ConsultInputKind.Object:
                writer.WriteStartObject();
                foreach (var field in value.Fields!)
                {
                    writer.WritePropertyName(field.Id);
                    Write(writer, field.Value, options);
                }

                writer.WriteEndObject();
                break;

            case ConsultInputKind.Array:
                writer.WriteStartArray();
                foreach (var element in value.Elements!)
                {
                    Write(writer, element, options);
                }

                writer.WriteEndArray();
                break;

            case ConsultInputKind.Null:
                writer.WriteNullValue();
                break;

            default:
                writer.WriteStringValue(value.Text ?? string.Empty);
                break;
        }
    }
}
