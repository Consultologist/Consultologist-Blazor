using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): the dialect checker's rules for enums (part 02 § 3.2, § 9), the
/// text annotations (§ 5.1) and the root of each kind of format file (§ 3.1, § 4, § 6.2), by
/// <see cref="SchemaSite"/>.
/// </summary>
public static partial class FormatDialect
{
    private static readonly string[] MediaTypes = ["text/plain", "text/markdown"];
    private static readonly string[] MarkdownKinds = ["block", "inline"];

    private sealed partial class Walk
    {
        /// <summary>The annotations an inline schema has been refused for already: S02-038 names no place, so it is said once a keyword.</summary>
        private readonly HashSet<string> inlineAnnotations = new(StringComparer.Ordinal);

        /// <summary>
        /// The rules of the root as a whole: a format file names the dialect (S02-022); a package's
        /// is an object or a list (S02-034, A03-001, D20); a registry text format carries both
        /// annotations (S02-035, S02-036). An inline schema has none of these.
        /// </summary>
        private void Root(JsonObject root, Declared declared)
        {
            if (site == SchemaSite.Inline)
                return;

            if (!root.ContainsKey("$schema"))
                Refusals.Add(FormatSentences.NoDialect(id, path));

            if (site == SchemaSite.PackageFormat && declared.Valid)
            {
                if (declared.Type is { } scalar && Scalars.Contains(scalar))
                    Refusals.Add(FormatSentences.ScalarPackageFormat(id, scalar == "integer" ? "number" : scalar, path));
                else if (declared.Type == "null" || (declared.Type is null && (root.ContainsKey("$ref") || root.ContainsKey("anyOf"))))
                    Refusals.Add(FormatSentences.PackageFormatNotObjectOrList(id, path));
            }

            if (site == SchemaSite.RegistryFormat && declared.Type == "string")
            {
                if (root.ContainsKey("x-markdown") && !root.ContainsKey("contentMediaType"))
                    Refusals.Add(FormatSentences.TextWithoutMediaType(id, path));
                if (Text(root["contentMediaType"]) == "text/markdown" && !root.ContainsKey("x-markdown"))
                    Refusals.Add(FormatSentences.MarkdownWithoutFlavour(id, path));
            }
        }

        /// <summary><c>$schema</c>, <c>$id</c> or <c>$defs</c> at the root: a format file's own; outside the dialect in an inline schema (S02-021).</summary>
        private void RootKeyword(string keyword, JsonNode? value)
        {
            if (site == SchemaSite.Inline)
            {
                Refusals.Add(FormatSentences.OutsideDialect(format, "", keyword, path));
                return;
            }

            switch (keyword)
            {
                case "$schema":
                    if (Text(value) is not { } dialect)
                        WrongType("", keyword, value, "text");
                    else if (dialect != DialectId)
                        Refusals.Add(FormatSentences.OtherDialect(id, dialect, path));
                    break;

                case "$id":
                    if (site == SchemaSite.PackageFormat)
                        Refusals.Add(FormatSentences.PackageFormatId(id, path));
                    break;

                default: // $defs
                    Definitions(value);
                    break;
            }
        }

        /// <summary>
        /// <c>contentMediaType</c> or <c>x-markdown</c>: in an inline schema, S02-038; in a package
        /// format, or below a registry format's root, S02-037; at a registry format's root, its value
        /// (A03-009) and whether it applies there (S02-037).
        /// </summary>
        private void Annotation(JsonObject schema, string keyword, JsonNode? value, string pointer, bool isRoot, Declared declared)
        {
            if (site == SchemaSite.Inline)
            {
                if (inlineAnnotations.Add(keyword))
                    Refusals.Add(FormatSentences.AnnotationInline(format, keyword, path));
                return;
            }

            if (!isRoot || site == SchemaSite.PackageFormat)
            {
                Refusals.Add(FormatSentences.AnnotationMisplaced(format, pointer, keyword, path));
                return;
            }

            var accepted = keyword == "contentMediaType" ? MediaTypes : MarkdownKinds;
            if (Text(value) is not { } written)
                WrongType("", keyword, value, "text");
            else if (!accepted.Contains(written, StringComparer.Ordinal))
                Refusals.Add(FormatSentences.AnnotationValue(format, keyword, written, string.Join(", ", accepted), path));

            if (declared.Type != "string"
                || (keyword == "x-markdown" && Text(schema["contentMediaType"]) == "text/plain"))
                Refusals.Add(FormatSentences.AnnotationMisplaced(format, "", keyword, path));
        }

        /// <summary>
        /// The values of an <c>enum</c> or a labelled <c>oneOf</c> on a string. A malformed entry or
        /// branch is refused itself, and leaves the count and the null rules unjudged; the values that
        /// are well formed are still held to uniqueness and snake_case, and the titles to uniqueness.
        /// </summary>
        private void Values(string keyword, JsonArray entries, string pointer, Declared declared)
        {
            var values = new List<string>();
            var titles = new List<string>();
            var nulls = 0;
            var wellFormed = true;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (keyword == "enum")
                {
                    if (entry is null)
                        nulls++;
                    else if (Text(entry) is { } value)
                        values.Add(value);
                    else
                        wellFormed = false;
                }
                else if (entry is JsonObject { Count: 1 } arm && arm.ContainsKey("const") && arm["const"] is null)
                {
                    nulls++;
                }
                else if (entry is JsonObject { Count: 2 } branch && Text(branch["const"]) is { } value
                    && Text(branch["title"]) is { Length: > 0 } title)
                {
                    values.Add(value);
                    titles.Add(title);
                }
                else
                {
                    wellFormed = false;
                    Refusals.Add(FormatSentences.BranchShape(format, pointer, i + 1, Json(entry, hideText: true), path));
                }
            }

            if (!wellFormed && keyword == "enum")
            {
                WrongType(pointer, keyword, entries, "a list of text");
                return;
            }

            foreach (var value in Repeated(values))
                Refusals.Add(FormatSentences.ValueTwice(format, pointer, value, path));
            if (nulls > 1)
                Refusals.Add(FormatSentences.ValueTwice(format, pointer, "null", path));
            if (site != SchemaSite.RegistryFormat)
            {
                foreach (var value in values.Distinct(StringComparer.Ordinal).Where(v => !SourceIds.IsPattern(v)))
                    Refusals.Add(FormatSentences.ValueNotSnakeCase(format, pointer, value, path));
            }

            foreach (var title in Repeated(titles))
                Refusals.Add(FormatSentences.TitleTwice(format, pointer, title, path));

            if (!wellFormed)
                return;

            if (values.Count < 2)
                Refusals.Add(FormatSentences.OneValue(format, pointer, path));

            if (declared.Valid && declared.Type == "string")
            {
                if (declared.Nullable && nulls == 0)
                {
                    Refusals.Add(keyword == "enum"
                        ? FormatSentences.NullableEnumWithoutNull(format, pointer, path)
                        : FormatSentences.NullableOneOfWithoutNullArm(format, pointer, path));
                }
                else if (!declared.Nullable && nulls > 0)
                {
                    Refusals.Add(FormatSentences.NullNotNullable(format, pointer, path));
                }
            }
        }
    }

    /// <summary>
    /// #1503 (specVersion 31, area 06; R4, #1507 item 5): an <c>enum</c> on a <c>number</c> or an <c>integer</c> (and not
    /// beside a <c>oneOf</c>) whose entries are numbers, with <c>null</c> among them or not, at least one a number. Any
    /// other enum off a string is placed as before (A03-004), so one that mixes text and numbers is said as it was.
    /// </summary>
    private static bool IsNumericEnum(JsonObject schema, string keyword, JsonArray entries, Declared declared) =>
        keyword == "enum" && declared.Valid && declared.Type is "number" or "integer" && !schema.ContainsKey("oneOf")
        && entries.Any(IsNumber) && entries.All(e => e is null || IsNumber(e));

    private static bool IsNumber(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.Number;

    private sealed partial class Walk
    {
        /// <summary>
        /// The values of a numeric enum (<see cref="IsNumericEnum"/>), held to the enum rules that are not about text:
        /// each a whole number on an <c>integer</c> and <c>null</c> only on a nullable type (else A03-003, and no more is
        /// judged); no value twice, a number by its value (<c>1</c> and <c>1.0</c> are one, S02-048); two values at least
        /// (S02-047); a nullable one lists <c>null</c> (S02-053). A value is named by its canonical JSON.
        /// </summary>
        private void NumericValues(JsonArray entries, string pointer, Declared declared)
        {
            var nulls = entries.Count(e => e is null);
            if (nulls > 0 && !declared.Nullable)
            {
                WrongType(pointer, "enum", entries, "a list of numbers");
                return;
            }

            if (declared.Type == "integer" && entries.OfType<JsonValue>().Any(v => !IsWhole(v)))
            {
                WrongType(pointer, "enum", entries, "a list of whole numbers");
                return;
            }

            var values = entries.OfType<JsonValue>().Select(v => CanonicalJson.Write(v)).ToList();
            foreach (var value in Repeated(values))
                Refusals.Add(FormatSentences.ValueTwice(format, pointer, value, path));
            if (nulls > 1)
                Refusals.Add(FormatSentences.ValueTwice(format, pointer, "null", path));
            if (values.Count < 2)
                Refusals.Add(FormatSentences.OneValue(format, pointer, path));
            if (declared.Nullable && nulls == 0)
                Refusals.Add(FormatSentences.NullableEnumWithoutNull(format, pointer, path));
        }

        private static bool IsWhole(JsonValue value) =>
            double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && double.IsFinite(d) && d == Math.Floor(d);
    }

    /// <summary>Each text written more than once, once, in the order of its second appearance.</summary>
    private static IEnumerable<string> Repeated(List<string> texts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            if (!seen.Add(text) && said.Add(text))
                yield return text;
        }
    }
}
