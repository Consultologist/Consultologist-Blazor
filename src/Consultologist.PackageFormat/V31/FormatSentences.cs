namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): every sentence the type system says, one method each, named
/// after what it says, as <see cref="SourceSentences"/> does for area 02. Each is the row of
/// <c>v31/sentences.json</c> (or the area's own <c>A03-NNN</c>) with its placeholders filled in,
/// character for character. No sentence carries a value from an input or a model: only ids,
/// member names, keywords, JSON pointers, counts and limits. The tasks that follow add theirs here.
/// </summary>
internal static class FormatSentences
{
    /// <summary>
    /// The <c>&lt;Subject&gt;</c> of a sentence about a place in a format (D11): the format as
    /// <paramref name="format"/> names it (<c>Format 'x'</c>, <c>Input 'y'</c>), then
    /// <c>at &lt;path&gt;</c>, with <c>the root</c> for the schema itself.
    /// </summary>
    public static string Subject(string format, string pointer) =>
        $"{format} at {Where(pointer)}";

    /// <summary>S02-021: a keyword outside the dialect, or one the root alone may write (Task 2's rule).</summary>
    public static SourceRefusal OutsideDialect(string format, string pointer, string keyword, string path) =>
        new("S02-021", $"{format} uses keyword '{keyword}' at {Where(pointer)}, which is outside the formats dialect.", path);

    /// <summary>S02-024: <c>additionalProperties</c> other than <c>false</c>; <paramref name="value"/> as compact JSON.</summary>
    public static SourceRefusal AdditionalPropertiesNotFalse(string format, string pointer, string value, string path) =>
        new("S02-024", $"{Subject(format, pointer)} declares additionalProperties {value}; every object declares additionalProperties: false.", path);

    /// <summary>S02-025: a <c>required</c> name that is no property.</summary>
    public static SourceRefusal RequiredNotAProperty(string format, string pointer, string property, string path) =>
        new("S02-025", $"{Subject(format, pointer)} lists '{property}' in required but declares no such property.", path);

    /// <summary>S02-026: a property not in <c>required</c>.</summary>
    public static SourceRefusal PropertyNotRequired(string format, string pointer, string property, string path) =>
        new("S02-026", $"{Subject(format, pointer)} leaves '{property}' out of required; every property is listed in required, and an optional one is written as nullable.", path);

    /// <summary>S02-028: a scalar made nullable with <c>anyOf</c>; <paramref name="type"/> is the scalar's.</summary>
    public static SourceRefusal NullableScalarWithAnyOf(string format, string pointer, string type, string path) =>
        new("S02-028", $"{Subject(format, pointer)} writes a nullable {type} with anyOf; write it as \"type\": [\"{type}\", \"null\"].", path);

    /// <summary>S02-029: a <c>type</c> that is not one of the seven, nor one of them plus <c>"null"</c>; <paramref name="list"/> as compact JSON.</summary>
    public static SourceRefusal TypeNotInDialect(string format, string pointer, string list, string path) =>
        new("S02-029", $"{Subject(format, pointer)} declares type {list}; a type array is one type plus \"null\".", path);

    /// <summary>S02-030: <c>format</c> other than <c>date</c>, or beside a type other than <c>string</c>.</summary>
    public static SourceRefusal FormatNotDate(string format, string pointer, string value, string path) =>
        new("S02-030", $"{Subject(format, pointer)} uses format '{value}'; the only format is date, on a string.", path);

    /// <summary>S02-031: a keyword beside <c>$ref</c> other than <c>title</c> and <c>description</c>.</summary>
    public static SourceRefusal KeywordBesideRef(string format, string pointer, string keyword, string path) =>
        new("S02-031", $"{Subject(format, pointer)} has '{keyword}' beside $ref; only title and description may sit beside a $ref.", path);

    /// <summary>S02-032: a definition (or format) whose references reach itself.</summary>
    public static SourceRefusal RefersToItself(string format, string pointer, string path) =>
        new("S02-032", $"{Subject(format, pointer)} refers to itself; recursive formats aren't supported.", path);

    /// <summary>A03-002: a <c>$ref</c> naming no definition of the file; <paramref name="name"/> as written when it has neither form.</summary>
    public static SourceRefusal DefinitionMissing(string format, string pointer, string name, string path) =>
        new("A03-002", $"{Subject(format, pointer)} refers to #/$defs/{name}, which the file doesn't define.", path);

    /// <summary>A03-003: a keyword (or a schema held by one) of the wrong JSON type, in A02-001's words.</summary>
    public static SourceRefusal KeywordWrongType(string format, string pointer, string keyword, string whatItIs, string whatItMustBe, string path) =>
        new("A03-003", $"{Subject(format, pointer)} writes '{keyword}' as {whatItIs}; it is {whatItMustBe}.", path);

    /// <summary>A03-004: a keyword on a schema whose type is not its own; <paramref name="belongsOn"/> is <c>an object</c>, <c>a list</c> or <c>a string</c>.</summary>
    public static SourceRefusal KeywordMisplaced(string format, string pointer, string keyword, string belongsOn, string path) =>
        new("A03-004", $"{Subject(format, pointer)} uses '{keyword}', which belongs on {belongsOn}.", path);

    /// <summary>A03-005: none of <c>type</c>, <c>$ref</c> and <c>anyOf</c>.</summary>
    public static SourceRefusal NoType(string format, string pointer, string path) =>
        new("A03-005", $"{Subject(format, pointer)} declares no type; write type, $ref or anyOf.", path);

    /// <summary>A03-006: an object without <paramref name="keyword"/> (<c>properties</c>, <c>required</c> or <c>additionalProperties</c>).</summary>
    public static SourceRefusal ObjectWithout(string format, string pointer, string keyword, string path) =>
        new("A03-006", $"{Subject(format, pointer)} is an object without '{keyword}'; every object writes properties, required and additionalProperties: false.", path);

    /// <summary>A03-007: an array without <c>items</c>.</summary>
    public static SourceRefusal ListWithoutItems(string format, string pointer, string path) =>
        new("A03-007", $"{Subject(format, pointer)} is a list without items; write the one schema its items follow.", path);

    /// <summary>A03-008: an <c>anyOf</c> of fewer than two members.</summary>
    public static SourceRefusal UnionTooSmall(string format, string pointer, int members, string path) =>
        new("A03-008", $"{Subject(format, pointer)} writes anyOf with {members} member; a union has two or more.", path);

    private static string Where(string pointer) => pointer.Length == 0 ? "the root" : pointer;
}
