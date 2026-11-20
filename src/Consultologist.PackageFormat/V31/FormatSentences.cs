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

    /// <summary>S02-017: a format name that is no built-in, catalogue or visible package format.</summary>
    public static SourceRefusal UnknownFormat(string name, string path) =>
        new("S02-017", $"Format '{name}' is not a built-in, catalogue or package format.", path);

    /// <summary>S02-021: a keyword outside the dialect, or <c>$schema</c>, <c>$id</c> or <c>$defs</c> anywhere but a format file's root.</summary>
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

    /// <summary>S02-022: a format file without <c>$schema</c>.</summary>
    public static SourceRefusal NoDialect(string id, string path) =>
        new("S02-022", $"Format '{id}' declares no $schema; every format file names the dialect ({FormatDialect.DialectId}).", path);

    /// <summary>S02-023: a <c>$schema</c> other than the dialect's id (D26: this reader reads 31 only).</summary>
    public static SourceRefusal OtherDialect(string id, string value, string path) =>
        new("S02-023", $"Format '{id}' declares $schema '{value}', but the package is specVersion 31, whose dialect is {FormatDialect.DialectId}.", path);

    /// <summary>S02-033: a package format that writes its own <c>$id</c>.</summary>
    public static SourceRefusal PackageFormatId(string id, string path) =>
        new("S02-033", $"Package format '{id}' declares $id; a package format's $id is written at publish.", path);

    /// <summary>S02-034 (D20): a package format whose root is a scalar; <paramref name="kind"/> is <c>string</c>, <c>number</c> or <c>boolean</c>.</summary>
    public static SourceRefusal ScalarPackageFormat(string id, string kind, string path) =>
        new("S02-034", $"Package format '{id}' is a {kind}; a package defines object and array formats, and refers to built-in and catalogue formats by name.", path);

    /// <summary>S02-035: a registry string format with <c>x-markdown</c> and no <c>contentMediaType</c>.</summary>
    public static SourceRefusal TextWithoutMediaType(string id, string path) =>
        new("S02-035", $"Format '{id}' is a text format and declares no contentMediaType.", path);

    /// <summary>S02-036: <c>contentMediaType: text/markdown</c> with no <c>x-markdown</c>.</summary>
    public static SourceRefusal MarkdownWithoutFlavour(string id, string path) =>
        new("S02-036", $"Format '{id}' is Markdown and declares no x-markdown (block or inline).", path);

    /// <summary>S02-037: <paramref name="keyword"/> (<c>contentMediaType</c> or <c>x-markdown</c>) where it doesn't apply.</summary>
    public static SourceRefusal AnnotationMisplaced(string format, string pointer, string keyword, string path) =>
        new("S02-037", $"{format} declares {keyword} at {Where(pointer)}, where it doesn't apply.", path);

    /// <summary>S02-038: a text annotation in an inline schema; <paramref name="owner"/> is the schema's owner (D11).</summary>
    public static SourceRefusal AnnotationInline(string owner, string keyword, string path) =>
        new("S02-038", $"{owner}'s inline format declares {keyword}; text inside an inline format is a $ref to a text format.", path);

    /// <summary>S02-047: fewer than two non-null values (said for none as for one).</summary>
    public static SourceRefusal OneValue(string format, string pointer, string path) =>
        new("S02-047", $"{Subject(format, pointer)} declares one value; an enum with one value is a constant, not a choice.", path);

    /// <summary>S02-048: a value written twice; <c>null</c> prints as <c>null</c>.</summary>
    public static SourceRefusal ValueTwice(string format, string pointer, string value, string path) =>
        new("S02-048", $"{Subject(format, pointer)} declares value '{value}' twice.", path);

    /// <summary>S02-049: a value that is not snake_case (D12).</summary>
    public static SourceRefusal ValueNotSnakeCase(string format, string pointer, string value, string path) =>
        new("S02-049", $"{Subject(format, pointer)} value '{value}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).", path);

    /// <summary>S02-050: two branches with one title.</summary>
    public static SourceRefusal TitleTwice(string format, string pointer, string title, string path) =>
        new("S02-050", $"{Subject(format, pointer)} gives two values the title '{title}'.", path);

    /// <summary>S02-051: a <c>oneOf</c> branch of another shape; <paramref name="n"/> from 1, <paramref name="shape"/> its compact JSON with every text hidden.</summary>
    public static SourceRefusal BranchShape(string format, string pointer, int n, string shape, string path) =>
        new("S02-051", $"{Subject(format, pointer)} writes oneOf branch {n} as {shape}; each branch is exactly {{ \"const\": <value>, \"title\": <label> }}.", path);

    /// <summary>S02-052: both <c>enum</c> and <c>oneOf</c>.</summary>
    public static SourceRefusal EnumAndOneOf(string format, string pointer, string path) =>
        new("S02-052", $"{Subject(format, pointer)} declares both enum and oneOf; write one.", path);

    /// <summary>S02-053: a nullable plain enum that doesn't list <c>null</c>.</summary>
    public static SourceRefusal NullableEnumWithoutNull(string format, string pointer, string path) =>
        new("S02-053", $"{Subject(format, pointer)} is nullable but its enum doesn't list null; a nullable enum lists null among its values.", path);

    /// <summary>S02-054: a nullable labelled enum with no <c>{ "const": null }</c> arm.</summary>
    public static SourceRefusal NullableOneOfWithoutNullArm(string format, string pointer, string path) =>
        new("S02-054", $"{Subject(format, pointer)} is nullable but its oneOf has no {{ \"const\": null }} arm; a nullable labelled enum adds one.", path);

    /// <summary>A03-001 (D20): a package format whose root is neither an object, a list nor a scalar.</summary>
    public static SourceRefusal PackageFormatNotObjectOrList(string id, string path) =>
        new("A03-001", $"Package format '{id}' is neither an object nor a list; a package defines object and array formats, and refers to built-in and catalogue formats by name.", path);

    /// <summary>A03-009: a text annotation's value outside <paramref name="accepted"/>.</summary>
    public static SourceRefusal AnnotationValue(string format, string keyword, string value, string accepted, string path) =>
        new("A03-009", $"{format} declares {keyword} '{value}' (accepted: {accepted}).", path);

    /// <summary>A03-010: <c>null</c> listed, or a <c>{ "const": null }</c> arm, when the type is plain <c>"string"</c>.</summary>
    public static SourceRefusal NullNotNullable(string format, string pointer, string path) =>
        new("A03-010", $"{Subject(format, pointer)} lists null but is not nullable; write \"type\": [\"string\", \"null\"], or remove null.", path);

    /// <summary>S02-018: an input read without <c>.value</c> or <c>.content</c>.</summary>
    public static SourceRefusal BareInput(string id, string path) =>
        new("S02-018", $"'input.{id}' is read bare; read input.{id}.value, or input.{id}.content for text.", path);

    /// <summary>S02-019: <c>.title</c> read on a plain <c>enum</c>; <paramref name="readPath"/> is what precedes <c>.title</c>.</summary>
    public static SourceRefusal TitleOfPlainEnum(string readPath, string path) =>
        new("S02-019", $"'{readPath}.title' reads the title of a plain enum, which has none; use a labelled oneOf, or read .value.", path);

    /// <summary>S02-020: a <c>.title</c> compared with a literal that is no title; <paramref name="titles"/> joined by <c>, </c>.</summary>
    public static SourceRefusal TitleLiteralNotATitle(string condition, string readPath, string literal, string titles, string path) =>
        new("S02-020", $"{condition} compares {readPath}.title with '{literal}', which is not one of its titles ({titles}).", path);

    /// <summary>S02-012: an agent node whose format has no binding for its producer.</summary>
    public static SourceRefusal NoBinding(string node, string format, string producer, string path) =>
        new("S02-012", $"Node '{node}' is kind agent and returns {format}, which has no binding for producer {producer} in agents/bindings.json.", path);

    /// <summary>S02-027: an agent node's format is an <c>anyOf</c> at its root; <paramref name="format"/> its type string.</summary>
    public static SourceRefusal AnyOfAtRoot(string node, string format, string path) =>
        new("S02-027", $"Node '{node}' returns {format}, an anyOf at its root, which strict mode can't send to a model; wrap it in an object.", path);

    /// <summary>S02-055: a format sent to a model with too many properties (D3).</summary>
    public static SourceRefusal TooManyProperties(string name, int n, int limit, string path) =>
        new("S02-055", $"Format '{name}' declares {n} properties; a format sent to a model declares at most {limit}.", path);

    /// <summary>S02-056: a format sent to a model nested too deep (D3); <paramref name="where"/> the first object at level 6.</summary>
    public static SourceRefusal TooDeep(string name, int n, string where, int limit, string path) =>
        new("S02-056", $"Format '{name}' nests {n} levels deep at {where}; a format sent to a model nests at most {limit}.", path);

    private static string Where(string pointer) => pointer.Length == 0 ? "the root" : pointer;
}
