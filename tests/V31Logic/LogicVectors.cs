using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: one case of the suite's goldens and vectors (D29): a rule and the data it reads, as JSON texts
/// (<paramref name="Data"/> <c>"{}"</c> when the rule reads nothing), and what it pins, in a sentence.
/// </summary>
internal sealed record LogicCase(string About, string Rule, string Data);

/// <summary>#1501: one file of the suite, <c>&lt;Family&gt;/&lt;Name&gt;.json</c>: what its group pins and its cases.</summary>
internal sealed record LogicFile(string Family, string Name, string About, IReadOnlyList<LogicCase> Cases);

/// <summary>
/// #1501: the inputs of the six JSON Logic families of the conformance suite (part 10 § 5; D29), and
/// <see cref="Render"/>, which runs each case through <see cref="CorvusJsonLogicEvaluator"/> or
/// <see cref="JsonLogicPrinter"/> and writes the file's bytes. The expectations are never written here:
/// they are what the code gives, read before they were frozen.
/// The <c>about</c>s cite the normative document's numbering (chapter 3 is part 03).
/// </summary>
internal static class LogicVectors
{
    public const string OperationGoldens = "operation-goldens";
    public const string PrinterGoldens = "printer-goldens";
    public const string NullVectors = "null-vectors";
    public const string TextVectors = "text-vectors";
    public const string NumberVectors = "number-vectors";
    public const string DateVectors = "date-vectors";

    /// <summary>The six families, in the index's order.</summary>
    public static IReadOnlyList<string> Families { get; } =
        [OperationGoldens, PrinterGoldens, NullVectors, TextVectors, NumberVectors, DateVectors];

    private static readonly CorvusJsonLogicEvaluator Evaluator = new();

    // The conventions of expect.json (RefusalPackages.Expect): 2-space indentation, \n line ends.
    private static readonly JsonSerializerOptions Writing = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <summary>Every file of the six families.</summary>
    public static IReadOnlyList<LogicFile> All() =>
    [
        .. OperationGoldenFiles(),
        .. PrinterGoldenFiles(),
        .. NullVectorFiles(),
        .. TextVectorFiles(),
        .. NumberVectorFiles(),
        .. DateVectorFiles(),
    ];

    /// <summary>
    /// The file's bytes: <c>{ "about", "cases" }</c>, each case <c>{ "about", "rule", "data", "result" }</c> (a vector),
    /// <c>{ "about", "rule", "data", "output" }</c> (an operation golden: the result's canonical JSON, as a string) or
    /// <c>{ "about", "rule", "text" }</c> (a printer golden); 2-space indentation, <c>\n</c> line ends, a final newline,
    /// every character outside ASCII written as itself. A case whose rule fails is a fault of these inputs, and throws.
    /// </summary>
    public static string Render(LogicFile file)
    {
        var cases = new JsonArray();
        foreach (var logicCase in file.Cases)
        {
            var rule = JsonNode.Parse(logicCase.Rule);
            var written = new JsonObject { ["about"] = logicCase.About, ["rule"] = rule?.DeepClone() };
            if (file.Family == PrinterGoldens)
            {
                written["text"] = JsonLogicPrinter.Print(rule);
            }
            else
            {
                var data = JsonNode.Parse(logicCase.Data);
                var value = Evaluator.Compile(rule).Evaluate(data);
                if (value.Failure is not null)
                    throw new InvalidOperationException($"{file.Family}/{file.Name}: '{logicCase.About}' failed: {value.Failure.Detail}");
                var canonical = CanonicalJson.Write(value.Value);
                written["data"] = data?.DeepClone();
                if (file.Family == OperationGoldens)
                    written["output"] = canonical;
                else
                    written["result"] = JsonNode.Parse(canonical);
            }
            cases.Add(written);
        }

        var document = new JsonObject { ["about"] = file.About, ["cases"] = cases };
        return AsItself(document.ToJsonString(Writing)) + "\n";
    }

    /// <summary>
    /// <paramref name="json"/> with each <c>\uXXXX</c> escape of a character outside ASCII (a surrogate pair as one
    /// character) written as the character; ASCII controls and a lone surrogate stay escaped.
    /// </summary>
    private static string AsItself(string json)
    {
        var text = new StringBuilder(json.Length);
        for (var i = 0; i < json.Length; i++)
        {
            if (json[i] != '\\' || i + 1 >= json.Length)
            {
                text.Append(json[i]);
                continue;
            }
            if (json[i + 1] != 'u' || Escaped(json, i) is not { } unit || unit < 0x80)
            {
                text.Append(json, i, 2);
                i++;
                continue;
            }
            if (char.IsHighSurrogate(unit) && Escaped(json, i + 6) is { } low && char.IsLowSurrogate(low))
            {
                text.Append(unit).Append(low);
                i += 11;
            }
            else if (char.IsSurrogate(unit))
            {
                text.Append(json, i, 6);
                i += 5;
            }
            else
            {
                text.Append(unit);
                i += 5;
            }
        }
        return text.ToString();
    }

    private static char? Escaped(string json, int at) =>
        at + 6 <= json.Length && json[at] == '\\' && json[at + 1] == 'u'
        && int.TryParse(json.AsSpan(at + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)
            ? (char)code
            : null;

    private static LogicCase C(string about, string rule, string data = "{}") => new(about, rule, data);

    private static LogicFile File(string family, string name, string about, params LogicCase[] cases) =>
        new(family, name, about, cases);

    // ---- data shared by the check operations ----

    private const string Pass = """{"pass":true,"message":null,"uncovered":null,"untested":null,"passed":null,"total":null}""";
    private const string Fail = """{"pass":false,"message":"The plan is missing.","uncovered":null,"untested":null,"passed":null,"total":null}""";

    private const string Flushes = """{"term":"hot flushes","type":"finding","id":"1001","isSnomedConcept":true,"isActive":true,"support":null}""";
    private const string JointPain = """{"term":"joint pain","type":"finding","id":"1002","isSnomedConcept":true,"isActive":true,"support":null}""";
    private const string Inactive = """{"term":"old term","type":"finding","id":"1003","isSnomedConcept":true,"isActive":false,"support":null}""";
    private const string NoId = """{"term":"fatigue after walking","type":"finding","id":null,"isSnomedConcept":true,"isActive":true,"support":null}""";
    private const string NotSnomed = """{"term":"feels unwell","type":"finding","id":"1004","isSnomedConcept":false,"isActive":true,"support":null}""";

    private const string Subset =
        """{"terms_subset":[{"var":"node.extract_input_terms.value"},{"var":"node.extract_note_terms.value"},"The note does not cover every clinical term found in the referral."]}""";

    private static string Terms(string referral, string note) =>
        """{"node":{"extract_input_terms":{"value":""" + referral + """},"extract_note_terms":{"value":""" + note + "}}}";

    private static string Concepts(params string[] concepts) => $$"""{"concepts":[{{string.Join(",", concepts)}}]}""";

    private const string AllPassValue = """{"all_pass":[{"var":"check.sections_checked.value"},"A section failed its check."]}""";
    private const string AllPassItems = """{"all_pass":[{"var":"check.sections_checked.items"},"A section failed its check."]}""";

    private static string CountPass(int min, string member = "value") =>
        $$"""{"count_pass":[{"var":"check.sections_checked.{{member}}"},{{min}},"Too few sections passed their check."]}""";

    private static string Checked(string member, params string[] elements) =>
        $$"""{"check":{"sections_checked":{"{{member}}":[""" + string.Join(",", elements) + "]}}}";

    private static string Item(string state, string value) => $$"""{"state":"{{state}}","value":{{value}}}""";

    // ---- operation-goldens: one file per custom operation (part 03 §§ 5.2 to 5.5) ----

    private static IEnumerable<LogicFile> OperationGoldenFiles()
    {
        const string F = OperationGoldens;
        yield return File(F, "size", "size: the code points of text-like operands, the elements of a list (§ 3.5.2).",
            C("§ 3.5.2 size: text counts its code points.", """{"size":["héllo"]}"""),
            C("§ 3.5.2 size: a character outside the Basic Multilingual Plane is one code point.", """{"size":["a😀b"]}"""),
            C("§ 3.5.2 size: an emoji, a zero-width joiner and a symbol are three code points.", """{"size":["\ud83d\udc69\u200d\u2695"]}"""),
            C("§ 3.5.2 size: with a variation selector after them, four.", """{"size":["\ud83d\udc69\u200d\u2695\ufe0f"]}"""),
            C("§ 3.5.2 size: a letter and a combining mark are two code points.", """{"size":["e\u0301"]}"""),
            C("§ 3.5.2 size: empty text is 0.", """{"size":[""]}"""),
            C("§ 3.5.2 size: a list counts its elements.", """{"size":[[1,2,3]]}"""),
            C("§ 3.5.2 size: an empty list is 0.", """{"size":[[]]}"""),
            C("§ 3.5.2 size: a list read from the data counts its elements.", """{"size":[{"var":"pass.history"}]}""", """{"pass":{"history":[{"number":1},{"number":2}]}}"""),
            C("§ 3.6.2 size: a number, data that escaped validation, reads as null.", """{"size":[5]}"""),
            C("§ 3.6.2 size: an object, data that escaped validation, reads as null.", """{"size":[{"var":"node.plan.value"}]}""", """{"node":{"plan":{"value":{"title":"Plan"}}}}"""));

        yield return File(F, "blank", "blank: true for empty text or text of whitespace only (§ 3.5.2).",
            C("§ 3.5.2 blank: empty text is blank.", """{"blank":[""]}"""),
            C("§ 3.5.2 blank: spaces and a tab are blank.", """{"blank":["  \t  "]}"""),
            C("§ 3.5.2 blank: line breaks are blank.", """{"blank":["\r\n\n"]}"""),
            C("§ 3.5.2 blank: U+00A0 and U+2003 are White_Space, so blank.", """{"blank":["\u00a0\u2003"]}"""),
            C("§ 3.5.2 blank: U+200B ZERO WIDTH SPACE is not White_Space, so not blank.", """{"blank":["\u200b"]}"""),
            C("§ 3.5.2 blank: a letter is not blank.", """{"blank":["a"]}"""),
            C("§ 3.5.2 blank: a letter between spaces is not blank.", """{"blank":[" a "]}"""),
            C("§ 3.6.2 blank: a number, data that escaped validation, reads as null, which is blank.", """{"blank":[5]}"""));

        yield return File(F, "words", "words: the count of maximal runs of non-whitespace characters (§ 3.5.2).",
            C("§ 3.5.2 words: runs of spaces and a line break separate three words.", """{"words":["  chest   pain\nworse "]}"""),
            C("§ 3.5.2 words: empty text has none.", """{"words":[""]}"""),
            C("§ 3.5.2 words: whitespace only has none.", """{"words":[" \t\n "]}"""),
            C("§ 3.5.2 words: punctuation is not whitespace, so chest-pain, and worse. are two words.", """{"words":["chest-pain, worse."]}"""),
            C("§ 3.5.2 words: U+00A0 is White_Space and separates words.", """{"words":["2.5\u00a0mg"]}"""),
            C("§ 3.5.2 words: a character outside the Basic Multilingual Plane is a word.", """{"words":["😀 😀"]}"""),
            C("§ 3.6.2 words: a number, data that escaped validation, reads as null.", """{"words":[5]}"""));

        yield return File(F, "lines", "lines: 0 for empty text, else 1 plus the line breaks (\\r\\n, \\n, \\r) not counting one at the very end (§ 3.5.2).",
            C("§ 3.5.2 lines: empty text has 0 lines.", """{"lines":[""]}"""),
            C("§ 3.5.2 lines: text with no line break is 1 line.", """{"lines":["a"]}"""),
            C("§ 3.5.2 lines: a line break at the very end is not counted.", """{"lines":["a\n"]}"""),
            C("§ 3.5.2 lines: one line break between two lines.", """{"lines":["a\nb"]}"""),
            C("§ 3.5.2 lines: \\r\\n is one line break, and a lone \\r is one.", """{"lines":["a\r\nb\rc"]}"""),
            C("§ 3.5.2 lines: \\r\\n at the very end is not counted.", """{"lines":["a\r\n"]}"""),
            C("§ 3.5.2 lines: a lone line break is 1 line.", """{"lines":["\n"]}"""),
            C("§ 3.5.2 lines: two line breaks are 2 lines, the last not counted.", """{"lines":["\n\n"]}"""),
            C("§ 3.5.2 lines: U+2028 LINE SEPARATOR is not one of the three line breaks.", """{"lines":["a\u2028b"]}"""),
            C("§ 3.6.2 lines: a number, data that escaped validation, reads as null.", """{"lines":[5]}"""));

        yield return File(F, "lower", "lower: lower-cased by the invariant culture's simple case mapping (§ 3.5.2).",
            C("§ 3.5.2 lower: ASCII letters are lowered, digits and punctuation unchanged.", """{"lower":["LETROZOLE 2.5 MG"]}"""),
            C("§ 3.5.2 lower: text already in lower case is unchanged.", """{"lower":["letrozole"]}"""),
            C("§ 3.5.2 lower: Greek capital omega lowers to omega.", """{"lower":["Ω"]}"""),
            C("§ 3.5.2 lower: a precomposed É lowers to é.", """{"lower":["É"]}"""),
            C("§ 3.5.2 lower: the simple mapping of final sigma ignores its place in the word.", """{"lower":["ΟΔΟΣ"]}"""),
            C("§ 3.5.2 lower: lowering Markdown lowers its source.", """{"lower":["**PLAN**"]}"""),
            C("§ 3.6.2 lower: a number, data that escaped validation, reads as null.", """{"lower":[5]}"""));

        yield return File(F, "trim", "trim: leading and trailing whitespace removed (§ 3.5.2).",
            C("§ 3.5.2 trim: spaces and a final line break are removed.", """{"trim":["  pain  \n"]}"""),
            C("§ 3.5.2 trim: whitespace inside the text is kept.", """{"trim":["  chest  pain "]}"""),
            C("§ 3.5.2 trim: whitespace only trims to empty text.", """{"trim":[" \t\r\n "]}"""),
            C("§ 3.5.2 trim: empty text stays empty.", """{"trim":[""]}"""),
            C("§ 3.5.2 trim: a character outside the Basic Multilingual Plane is kept whole.", """{"trim":[" 😀 "]}"""),
            C("§ 3.5.2 trim: U+200B ZERO WIDTH SPACE is not White_Space, so kept.", """{"trim":["\u200bpain\u200b"]}"""),
            C("§ 3.6.2 trim: a number, data that escaped validation, reads as null.", """{"trim":[5]}"""));

        yield return File(F, "starts_with", "starts_with: an ordinal, case-sensitive prefix test; on Markdown, of the source (§ 3.5.2).",
            C("§ 3.5.2 starts_with: true for a prefix.", """{"starts_with":["Dear Dr Sample","Dear"]}"""),
            C("§ 3.5.2 starts_with: case-sensitive, so false for another case.", """{"starts_with":["dear Dr Sample","Dear"]}"""),
            C("§ 3.5.2 starts_with: false when the prefix is elsewhere in the text.", """{"starts_with":["Dr Sample, dear","dear"]}"""),
            C("§ 3.5.2 starts_with: the empty prefix starts every text.", """{"starts_with":["Dear Dr Sample",""]}"""),
            C("§ 3.5.2 starts_with: false when the prefix is longer than the text.", """{"starts_with":["Dear","Dear Dr"]}"""),
            C("§ 3.5.2 starts_with: on Markdown it tests the source.", """{"starts_with":["**Plan**: continue","**Plan**"]}"""),
            C("§ 3.5.2 starts_with: ordinal, so a precomposed é does not start with e.", """{"starts_with":["épaule","e"]}"""),
            C("§ 3.6.2 starts_with: a number, data that escaped validation, reads as null, so false.", """{"starts_with":[5,"5"]}"""));

        yield return File(F, "ends_with", "ends_with: an ordinal, case-sensitive suffix test (§ 3.5.2).",
            C("§ 3.5.2 ends_with: true for a suffix.", """{"ends_with":["Continue letrozole.","."]}"""),
            C("§ 3.5.2 ends_with: case-sensitive, so false for another case.", """{"ends_with":["Continue Letrozole","letrozole"]}"""),
            C("§ 3.5.2 ends_with: false when the suffix is elsewhere in the text.", """{"ends_with":["letrozole daily","letrozole"]}"""),
            C("§ 3.5.2 ends_with: the empty suffix ends every text.", """{"ends_with":["letrozole",""]}"""),
            C("§ 3.5.2 ends_with: false when the suffix is longer than the text.", """{"ends_with":["mg","2.5 mg"]}"""),
            C("§ 3.5.2 ends_with: a character outside the Basic Multilingual Plane is matched whole.", """{"ends_with":["done 😀","😀"]}"""),
            C("§ 3.6.2 ends_with: a number, data that escaped validation, reads as null, so false.", """{"ends_with":[5,"5"]}"""));

        yield return File(F, "plain", "plain: Markdown converted to plain text by plain_conversion 1.0.0 (§ 3.5.2), every plain here converting (its operand is Markdown).",
            C("§ 3.5.2 plain: emphasis is removed.", """{"plain":["**Letrozole** 2.5 mg daily"]}"""),
            C("§ 3.5.2 plain: a heading and a paragraph are two lines, a link is its text.", """{"plain":["# Plan\n\nContinue [letrozole](https://example.org)."]}"""),
            C("§ 3.5.2 plain: a list's items are lines, without their markers.", """{"plain":["- hot flushes\n- joint pain"]}"""),
            C("§ 3.5.2 plain: a table's cells run together, separated by spaces.", """{"plain":["| Drug | Dose |\n|---|---|\n| Letrozole | 2.5 mg |"]}"""),
            C("§ 3.5.2 plain: empty text stays empty.", """{"plain":[""]}"""),
            C("§ 3.5.2 plain: text with no Markdown marks is unchanged.", """{"plain":["plain text, no marks"]}"""),
            C("§ 3.5.2 plain: \\r\\n becomes \\n, and line breaks and spaces at the very end are removed.", """{"plain":["Seen today.\r\n\r\nReview in 6 weeks.  \r\n"]}"""),
            C("§ 3.5.2 plain: inline code keeps its text without the backticks.", """{"plain":["Take `letrozole` daily"]}"""),
            C("§ 3.6.2 plain: a number, data that escaped validation, reads as null.", """{"plain":[5]}"""));

        yield return File(F, "object", "object: builds an object from its fields (§ 3.5.4); the output, canonical JSON, sorts the members.",
            C("§ 3.5.4 object: each field is its rule's value.", """{"object":{"title":{"var":"item.id"},"n":{"+":[1,2]}}}""", """{"item":{"id":"hpi"}}"""),
            C("§ 3.5.4 object: a field may be a nested object.", """{"object":{"inner":{"object":{"ok":true}}}}"""),
            C("§ 3.5.4 object: no field builds the empty object.", """{"object":{}}"""),
            C("§ 3.5.4 object: a field may hold a list.", """{"object":{"doses":[2.5,5]}}"""),
            C("§ 3.5.4 object: a field may yield null.", """{"object":{"feedback":{"var":"node.review.value"}}}""", """{"node":{"review":{"value":null,"content":null,"state":"skipped"}}}"""),
            C("§ 3.5.4 object: a field name that is not an identifier is kept as written.", """{"object":{"my field":"x","b":1}}"""));

        yield return File(F, "matches", "matches: I-Regexp (RFC 9485) with search semantics, translated to .NET and counted in code points (§ 3.5.3).",
            C("§ 3.5.3 matches: true when the pattern occurs anywhere in the text.", """{"matches":["started femara today","letrozole|femara"]}"""),
            C("§ 3.5.3 matches: always case-sensitive.", """{"matches":["started Femara","letrozole|femara"]}"""),
            C("§ 3.5.3 matches: false when the pattern does not occur.", """{"matches":["started tamoxifen","letrozole|femara"]}"""),
            C("§ 3.5.3 matches: . matches a character.", """{"matches":["abc","a.c"]}"""),
            C("§ 3.5.3 matches: . does not match \\n.", """{"matches":["a\nc","a.c"]}"""),
            C("§ 3.5.3 matches: . does not match \\r.", """{"matches":["a\rc","a.c"]}"""),
            C("§ 3.5.3 matches: . matches a character outside the Basic Multilingual Plane as one.", """{"matches":["a😀c","a.c"]}"""),
            C("§ 3.5.3 matches: a character outside the Basic Multilingual Plane is one, so not two.", """{"matches":["a😀c","a.{2}c"]}"""),
            C("§ 3.5.3 matches: a negated class matches a character outside the Basic Multilingual Plane as one.", """{"matches":["a😀c","a[^x]c"]}"""),
            C("§ 3.5.3 matches: a quantifier counts a character outside the Basic Multilingual Plane as one.", """{"matches":["😀😀","😀{2}"]}"""),
            C("§ 3.5.3 matches: a class range of characters outside the Basic Multilingual Plane.", """{"matches":["x😃x","[😀-😏]"]}"""),
            C("§ 3.5.3 matches: ^ is a literal character in I-Regexp, not an anchor.", """{"matches":["x^a","^a"]}"""),
            C("§ 3.5.3 matches: ^ is a literal, so it does not match at the start of the text.", """{"matches":["abc","^a"]}"""),
            C("§ 3.5.3 matches: $ is a literal character in I-Regexp, not an anchor.", """{"matches":["cost a$","a$"]}"""),
            C("§ 3.5.3 matches: a Unicode category matches a letter of the Basic Multilingual Plane.", """{"matches":["Élan","\\p{Lu}"]}"""),
            C("§ 3.5.3 matches: categories are judged on Basic Multilingual Plane code points only, so the Deseret letter U+10400 is in no \\p{L}.", """{"matches":["𐐀","\\p{L}"]}"""),
            C("§ 3.5.3 matches: and in every \\P{L}.", """{"matches":["𐐀","\\P{L}"]}"""),
            C("§ 3.5.3 matches: on Markdown it searches the source.", """{"matches":["**letrozole**","\\*\\*letrozole"]}"""),
            C("§ 3.6.2 matches: a number, data that escaped validation, reads as null, so false.", """{"matches":[5,"5"]}"""));

        yield return File(F, "has_word", "has_word: word occurs in x, ordinal and case-sensitive, with no word character on either side (§ 3.5.3).",
            C("§ 3.5.3 has_word: a letter after the word is a word character, so painful is not pain.", """{"has_word":["painful","pain"]}"""),
            C("§ 3.5.3 has_word: a letter before the word is a word character, so Spain is not pain.", """{"has_word":["Spain","pain"]}"""),
            C("§ 3.5.3 has_word: punctuation is not a word character.", """{"has_word":["pain.","pain"]}"""),
            C("§ 3.5.3 has_word: the word may contain a space.", """{"has_word":["chest pain today","chest pain"]}"""),
            C("§ 3.5.3 has_word: _ is connector punctuation, a word character.", """{"has_word":["pain_scale","pain"]}"""),
            C("§ 3.5.3 has_word: a decimal digit is a word character.", """{"has_word":["pain2","pain"]}"""),
            C("§ 3.5.3 has_word: a later occurrence with boundaries is found.", """{"has_word":["painpain pain","pain"]}"""),
            C("§ 3.5.3 has_word: case-sensitive.", """{"has_word":["Pain","pain"]}"""),
            C("§ 3.5.3 has_word: an accented letter is a word character, but the second occurrence stands alone.", """{"has_word":["paín pain","pain"]}"""),
            C("§ 3.5.3 has_word: the letter U+1D4B3 outside the Basic Multilingual Plane is a word character.", """{"has_word":["𝒳pain","pain"]}"""),
            C("§ 3.5.3 has_word: an emoji is not a word character.", """{"has_word":["😀pain😀","pain"]}"""),
            C("§ 3.6.2 has_word: a number, data that escaped validation, reads as null, so false.", """{"has_word":[5,"5"]}"""));

        yield return File(F, "terms_subset", "terms_subset: passes when every codable concept of the first list has its id in the second (§ 3.5.4); uncovered and untested are null when empty.",
            C("§ 3.5.4 terms_subset: an empty first list passes.", Subset, Terms(Concepts(), Concepts(Flushes))),
            C("§ 3.5.4 terms_subset: every codable concept covered passes.", Subset, Terms(Concepts(Flushes, JointPain), Concepts(JointPain, Flushes))),
            C("§ 3.5.4 terms_subset: a codable concept missing from the second list fails, with failWith, and is uncovered.", Subset, Terms(Concepts(Flushes, JointPain), Concepts(Flushes))),
            C("§ 3.5.4 terms_subset: an inactive concept, one with no id and one that is not SNOMED CT are untested, not uncovered.", Subset, Terms(Concepts(Flushes, Inactive, NoId, NotSnomed), Concepts(Flushes))),
            C("§ 3.5.4 terms_subset: untested terms come from both lists, each once.", Subset, Terms(Concepts(Flushes, NoId), Concepts(Flushes, NoId, Inactive))),
            C("§ 3.5.4 terms_subset: a concept repeated in the first list is uncovered once.", Subset, Terms(Concepts(JointPain, JointPain), Concepts(Flushes))),
            C("§ 3.5.4 terms_subset: concepts are matched by id, not by term.", Subset, Terms(Concepts(JointPain), Concepts(JointPain.Replace("joint pain", "arthralgia")))),
            C("§ 3.5.4 terms_subset: a value that is not a concept_list reads as an empty list (D21).", Subset, Terms("5", Concepts(Flushes))));

        yield return File(F, "all_pass", "all_pass: passes when every judged element passes; elements that did not complete are ignored; nothing judged fails (§ 3.5.4).",
            C("§ 3.5.4 all_pass: every element of a .value passing passes.", AllPassValue, Checked("value", Pass, Pass)),
            C("§ 3.5.4 all_pass: one element failing fails, with failWith.", AllPassValue, Checked("value", Pass, Fail)),
            C("§ 3.5.4 all_pass: an empty list judges nothing, and fails.", AllPassValue, Checked("value")),
            C("§ 3.5.4 all_pass: in .items, an element that is skipped or not_run is ignored.", AllPassItems, Checked("items", Item("completed", Pass), Item("skipped", "null"), Item("not_run", "null"))),
            C("§ 3.5.4 all_pass: in .items, a completed element that failed fails.", AllPassItems, Checked("items", Item("completed", Fail), Item("skipped", "null"))),
            C("§ 3.5.4 all_pass: in .items, no completed element judges nothing, and fails.", AllPassItems, Checked("items", Item("skipped", "null"), Item("not_run", "null"))),
            C("§ 3.5.4 all_pass: an element that is not an object, or has no boolean pass, is not judged (D21).", AllPassValue, Checked("value", Pass, "5", """{"pass":"yes"}""")));

        yield return File(F, "count_pass", "count_pass: passes when at least min of the judged elements pass; elements that did not complete are ignored (§ 3.5.4).",
            C("§ 3.5.4 count_pass: reaching min passes.", CountPass(1), Checked("value", Pass, Fail)),
            C("§ 3.5.4 count_pass: below min fails, with failWith.", CountPass(2), Checked("value", Pass, Fail)),
            C("§ 3.5.4 count_pass: a min of 0 passes an empty list.", CountPass(0), Checked("value")),
            C("§ 3.5.4 count_pass: a min of 1 fails an empty list.", CountPass(1), Checked("value")),
            C("§ 3.5.4 count_pass: in .items, only completed elements are counted.", CountPass(2, "items"), Checked("items", Item("completed", Pass), Item("skipped", "null"), Item("completed", Pass))),
            C("§ 3.5.4 count_pass: in .items, a skipped element does not count towards min.", CountPass(2, "items"), Checked("items", Item("completed", Pass), Item("skipped", "null"))));

        yield return File(F, "days_between", "days_between: whole days from a to b, negative when b is earlier (§ 3.5.5).",
            C("§ 3.5.5 days_between: whole days to a later date.", """{"days_between":["2026-01-01","2026-01-31"]}"""),
            C("§ 3.5.5 days_between: negative when b is earlier.", """{"days_between":["2026-01-31","2026-01-01"]}"""),
            C("§ 3.5.5 days_between: 0 for the same date.", """{"days_between":["2026-03-14","2026-03-14"]}"""),
            C("§ 3.5.5 days_between: across 29 February of a leap year.", """{"days_between":["2024-02-28","2024-03-01"]}"""),
            C("§ 3.5.5 days_between: across a year's end.", """{"days_between":["2025-12-31","2026-01-01"]}"""),
            C("§ 3.5.5 days_between: a malformed date gives null.", """{"days_between":["2026-2-3","2026-02-04"]}"""));

        yield return File(F, "years_between", "years_between: completed years from a to b, a year counting once its anniversary is reached; negative when b is earlier (§ 3.5.5).",
            C("§ 3.5.5 years_between: the day before the anniversary, the year is not completed.", """{"years_between":["1960-06-15","2026-06-14"]}"""),
            C("§ 3.5.5 years_between: on the anniversary, the year is completed.", """{"years_between":["1960-06-15","2026-06-15"]}"""),
            C("§ 3.5.5 years_between: negative when b is earlier, the same rule mirrored.", """{"years_between":["2026-06-15","1960-06-15"]}"""),
            C("§ 3.5.5 years_between: mirrored, a year short of the anniversary.", """{"years_between":["2026-06-14","1960-06-15"]}"""),
            C("§ 3.5.5 years_between: born on 29 February, 0 on 28 February of a year with no 29 February.", """{"years_between":["2008-02-29","2009-02-28"]}"""),
            C("§ 3.5.5 years_between: born on 29 February, 1 on 1 March of a year with no 29 February.", """{"years_between":["2008-02-29","2009-03-01"]}"""),
            C("§ 3.5.5 years_between: a malformed date gives null.", """{"years_between":["2026-02-30","2027-01-01"]}"""));

        yield return File(F, "add_days", "add_days: date plus n days; a non-integer n, or a result outside years 0001 to 9999, gives null (§ 3.5.5).",
            C("§ 3.5.5 add_days: one day past a year's end.", """{"add_days":["2026-12-31",1]}"""),
            C("§ 3.5.5 add_days: a negative n goes back.", """{"add_days":["2026-01-01",-1]}"""),
            C("§ 3.5.5 add_days: through 29 February of a leap year.", """{"add_days":["2024-02-28",2]}"""),
            C("§ 3.5.5 add_days: n of 0 gives the same date.", """{"add_days":["2026-03-14",0]}"""),
            C("§ 3.5.5 add_days: a non-integer n gives null.", """{"add_days":["2026-01-01",1.5]}"""),
            C("§ 3.5.5 add_days: a result after 9999-12-31 gives null.", """{"add_days":["9999-12-31",1]}"""),
            C("§ 3.5.5 add_days: a result before 0001-01-01 gives null.", """{"add_days":["0001-01-01",-1]}"""),
            C("§ 3.6.2 add_days: an n that is text, data that escaped validation, reads as null.", """{"add_days":["2026-01-01","1"]}"""));
    }

    // ---- printer-goldens (part 03 § 8, D23) ----

    private static IEnumerable<LogicFile> PrinterGoldenFiles()
    {
        const string F = PrinterGoldens;
        yield return File(F, "operations", "The printer's text for every operation of § 3.5, once each (§ 3.8).",
            C("§ 3.8 var: a path prints as written.", """{"var":"input.age.value"}"""),
            C("§ 3.8 missing: a function form over the list of paths.", """{"missing":["input.age.value"]}"""),
            C("§ 3.8 if: if c then a else b.", """{"if":[{"var":"input.urgent.value"},"today","this week"]}"""),
            C("§ 3.8 ===: strict equality prints as =.", """{"===":[{"var":"input.kind.value"},"table"]}"""),
            C("§ 3.8 !==: strict inequality prints as ≠.", """{"!==":[{"var":"item.id"},"billing"]}"""),
            C("§ 3.8 <: a < b.", """{"<":[{"var":"input.age.value"},18]}"""),
            C("§ 3.8 <=: prints as ≤.", """{"<=":[{"var":"input.age.value"},65]}"""),
            C("§ 3.8 >: a > b.", """{">":[{"var":"input.age.value"},65]}"""),
            C("§ 3.8 >=: prints as ≥.", """{">=":[{"years_between":[{"var":"input.date_of_birth.value"},{"var":"run.date"}]},65]}"""),
            C("§ 3.8 and: as a word.", """{"and":[{"var":"input.a.value"},{"var":"input.b.value"},{"var":"input.c.value"}]}"""),
            C("§ 3.8 or: as a word.", """{"or":[{"!==":[{"var":"item.id"},"billing"]},{"===":[{"var":"input.include_billing.value"},true]}]}"""),
            C("§ 3.8 !: prints as not.", """{"!":[{"var":"input.urgent.value"}]}"""),
            C("§ 3.8 in: a in b.", """{"in":["letrozole",{"lower":[{"var":"node.draft.content"}]}]}"""),
            C("§ 3.8 cat: a function form.", """{"cat":["Dr ",{"var":"input.name.value"}]}"""),
            C("§ 3.8 +: infix.", """{"+":[{"var":"input.a.value"},1]}"""),
            C("§ 3.8 -: infix with two operands.", """{"-":[{"var":"input.a.value"},1]}"""),
            C("§ 3.8 *: infix.", """{"*":[{"var":"input.a.value"},2]}"""),
            C("§ 3.8 /: infix.", """{"/":[{"var":"input.a.value"},2]}"""),
            C("§ 3.8 %: infix.", """{"%":[{"var":"input.a.value"},2]}"""),
            C("§ 3.8 min: a function form.", """{"min":[{"var":"input.a.value"},10]}"""),
            C("§ 3.8 max: a function form.", """{"max":[{"var":"input.a.value"},0]}"""),
            C("§ 3.8 size: a function form.", """{"size":[{"var":"input.prior_notes.value"}]}"""),
            C("§ 3.8 blank: a function form.", """{"blank":[{"var":"node.draft.content"}]}"""),
            C("§ 3.8 words: a function form.", """{"words":[{"var":"node.draft.content"}]}"""),
            C("§ 3.8 lines: a function form.", """{"lines":[{"var":"node.draft.content"}]}"""),
            C("§ 3.8 lower: a function form.", """{"lower":[{"var":"node.draft.content"}]}"""),
            C("§ 3.8 trim: a function form.", """{"trim":[{"var":"node.draft.content"}]}"""),
            C("§ 3.8 starts_with: a function form.", """{"starts_with":[{"var":"node.draft.content"},"Dear"]}"""),
            C("§ 3.8 ends_with: a function form.", """{"ends_with":[{"var":"node.draft.content"},"."]}"""),
            C("§ 3.8 plain: a function form.", """{"plain":[{"var":"input.consult_draft.content"}]}"""),
            C("§ 3.8 object: { field: rule, … }, fields in written order.", """{"object":{"title":{"var":"item.id"},"content":{"var":"node.draft.content"}}}"""),
            C("§ 3.8 matches: a function form, the pattern as a string literal.", """{"matches":[{"lower":[{"var":"node.draft.content"}]},"letrozole|femara"]}"""),
            C("§ 3.8 has_word: a function form.", """{"has_word":[{"var":"node.draft.content"},"chest pain"]}"""),
            C("§ 3.8 terms_subset: a function form.", """{"terms_subset":[{"var":"node.extract_input_terms.value"},{"var":"node.extract_note_terms.value"},"The note does not cover every clinical term found in the referral."]}"""),
            C("§ 3.8 all_pass: a function form.", """{"all_pass":[{"var":"check.sections_checked.items"},"A section failed its check."]}"""),
            C("§ 3.8 count_pass: a function form, min as a number.", """{"count_pass":[{"var":"check.sections_checked.value"},2,"Too few sections passed their check."]}"""),
            C("§ 3.8 days_between: a function form.", """{"days_between":[{"var":"input.referral_date.value"},{"var":"run.date"}]}"""),
            C("§ 3.8 years_between: a function form.", """{"years_between":[{"var":"input.date_of_birth.value"},{"var":"run.date"}]}"""),
            C("§ 3.8 add_days: a function form.", """{"add_days":[{"var":"run.date"},28]}"""));

        yield return File(F, "precedence", "Precedence and parentheses: a child is parenthesised when its precedence is lower than or equal to its parent's, so the text parses back to exactly the tree (§ 3.8).",
            C("§ 3.8 and in and: the inner and is parenthesised, a flat and of three being a different tree.", """{"and":[{"and":[{"var":"x.a"},{"var":"x.b"}]},{"var":"x.c"}]}"""),
            C("§ 3.8 a flat and of three prints with no parentheses.", """{"and":[{"var":"x.a"},{"var":"x.b"},{"var":"x.c"}]}"""),
            C("§ 3.8 and binds tighter than or: no parentheses.", """{"or":[{"and":[{"var":"x.a"},{"var":"x.b"}]},{"var":"x.c"}]}"""),
            C("§ 3.8 or inside and is parenthesised.", """{"and":[{"or":[{"var":"x.a"},{"var":"x.b"}]},{"var":"x.c"}]}"""),
            C("§ 3.8 not over and is parenthesised.", """{"!":[{"and":[{"var":"x.a"},{"var":"x.b"}]}]}"""),
            C("§ 3.8 not over not is parenthesised.", """{"!":[{"!":[{"var":"x.a"}]}]}"""),
            C("§ 3.8 not over a comparison is parenthesised.", """{"!":[{">":[{"var":"input.age.value"},65]}]}"""),
            C("§ 3.8 a comparison inside and needs no parentheses.", """{"and":[{"!==":[{"var":"input.age.value"},null]},{"<=":[{"var":"input.age.value"},65]}]}"""),
            C("§ 3.8 arithmetic inside a comparison needs no parentheses.", """{"<":[{"+":[{"var":"x.a"},1]},{"*":[{"var":"x.b"},2]}]}"""),
            C("§ 3.8 a flat + of three prints with no parentheses.", """{"+":[1,2,3]}"""),
            C("§ 3.8 + inside * is parenthesised.", """{"*":[{"+":[1,2]},3]}"""),
            C("§ 3.8 * inside + needs no parentheses.", """{"+":[{"*":[{"var":"x.a"},2]},1]}"""),
            C("§ 3.8 - on the right of - is parenthesised.", """{"-":[{"var":"x.a"},{"-":[{"var":"x.b"},{"var":"x.c"}]}]}"""),
            C("§ 3.8 - on the left of - is parenthesised too (equal precedence).", """{"-":[{"-":[{"var":"x.a"},{"var":"x.b"}]},{"var":"x.c"}]}"""),
            C("§ 3.8 / inside / is parenthesised.", """{"/":[{"/":[{"var":"x.a"},2]},3]}"""),
            C("§ 3.8 one-operand - over a read prints -a.", """{"-":[{"var":"x.a"}]}"""),
            C("§ 3.8 one-operand - over + is parenthesised.", """{"-":[{"+":[{"var":"x.a"},1]}]}"""),
            C("§ 3.8 a negative literal on the right of -.", """{"-":[{"var":"x.a"},-1]}"""),
            C("§ 3.8 more pairs of if print as else if.", """{"if":[{"var":"x.a"},1,{"var":"x.b"},2,3]}"""),
            C("§ 3.8 an if written in an else is parenthesised, a different tree from more pairs.", """{"if":[{"var":"x.a"},1,{"if":[{"var":"x.b"},2,3]}]}"""),
            C("§ 3.8 an if inside + is parenthesised.", """{"+":[1,{"if":[{"var":"x.a"},1,2]}]}"""),
            C("§ 3.8 an if in a condition is parenthesised.", """{"if":[{"if":[{"var":"x.a"},true,false]},1,2]}"""),
            C("§ 3.8 a child of a function form is never parenthesised.", """{"min":[1,{"if":[{"var":"x.a"},1,2]}]}"""),
            C("§ 3.8 a child in an array is never parenthesised.", """{"in":[{"var":"x.a"},[{"+":[1,2]},{"or":[true,false]}]]}"""),
            C("§ 3.8 a field of object is never parenthesised.", """{"object":{"total":{"+":[{"var":"x.a"},{"var":"x.b"}]},"ok":{"and":[{"var":"x.c"},true]}}}"""),
            C("§ 3.8 in inside and needs no parentheses; in inside a comparison is parenthesised.", """{"and":[{"in":["a",{"var":"x.a"}]},{"===":[{"in":["b",{"var":"x.b"}]},true]}]}"""));

        yield return File(F, "literals", "Every literal form, and the forms that keep the tree (§ 3.8; D23).",
            C("§ 3.8 a whole number in its RFC 8785 form.", "65"),
            C("§ 3.8 a fraction in its RFC 8785 form.", "0.5"),
            C("§ 3.8 a large number in its RFC 8785 form.", "1e21"),
            C("§ 3.8 a small number in its RFC 8785 form.", "0.000001"),
            C("§ 3.8 a smaller number in its RFC 8785 form.", "1e-7"),
            C("§ 3.8 a number written with a fraction of zero prints whole.", "1.0"),
            C("§ 3.8 a negative literal prints -1.", "-1"),
            C("§ 3.8 zero.", "0"),
            C("§ 3.8 true.", "true"),
            C("§ 3.8 false.", "false"),
            C("§ 3.8 null.", "null"),
            C("§ 3.8 a string literal is JSON text.", "\"table\""),
            C("§ 3.8 the empty string.", "\"\""),
            C("§ 3.8 a string with a quote and a line break is written with JSON escapes; ≠ as itself.", """ "a\"b\n≠" """),
            C("§ 3.8 a string holding the word and stays one literal.", "\" and \""),
            C("§ 3.8 a string holding a backslash and a tab.", """ "C:\\notes\t1" """),
            C("§ 3.8 a string with a character outside the Basic Multilingual Plane.", "\"😀\""),
            C("§ 3.8 an array.", """[1,{"var":"a.b"},"c"]"""),
            C("§ 3.8 the empty array.", "[]"),
            C("§ 3.8 an array in an array.", """[[1,2],[]]"""),
            C("D23: one-operand - over a number literal prints -(1), not -1.", """{"-":[1]}"""),
            C("D23: a path whose first segment is a printer word prints var(\"…\").", """{"var":"true"}"""),
            C("D23: in is a printer word too.", """{"var":"in.x"}"""),
            C("D23: a path that is not dot-joined identifiers prints var(\"…\").", """{"var":"a b"}"""),
            C("D23: a path with a word that is not first prints as written.", """{"var":"x.not"}"""),
            C("D23: an object field that is not an identifier prints as a JSON string.", """{"object":{"my field":1}}"""),
            C("D23: the empty object prints {}.", """{"object":{}}"""),
            C("§ 3.8 a .title read prints as written.", """{"===":[{"var":"input.specialty.title"},"Medical oncology"]}"""));
    }

    // ---- null-vectors (part 03 §§ 6.1 to 6.4) ----

    private static IEnumerable<LogicFile> NullVectorFiles()
    {
        const string F = NullVectors;
        yield return File(F, "standard", "A null operand in each position of every standard operation (§ 3.5.1, § 3.6.2).",
            C("§ 3.5.1 var: a path whose value is null reads as null.", """{"var":"input.age.value"}""", """{"input":{"age":{"value":null}}}"""),
            C("§ 3.5.1 var: an absent path reads as null.", """{"var":"input.age.value"}""", """{"input":{}}"""),
            C("§ 3.5.1 var: a path through a value that is null reads as null.", """{"var":"node.plan.value.title"}""", """{"node":{"plan":{"value":null}}}"""),
            C("§ 3.5.1 missing: a path whose value is null is missing.", """{"missing":["input.age.value"]}""", """{"input":{"age":{"value":null}}}"""),
            C("§ 3.5.1 missing: an absent path is missing.", """{"missing":["input.age.value"]}""", """{"input":{}}"""),
            C("§ 3.5.1 missing: a path whose value is \"\" is missing.", """{"missing":["input.name.value"]}""", """{"input":{"name":{"value":""}}}"""),
            C("§ 3.5.1 missing: a path with a value is not missing, and the missing ones keep their written order.", """{"missing":["input.name.value","input.kind.value","input.age.value"]}""", """{"input":{"name":{"value":null},"kind":{"value":"table"}}}"""),
            C("§ 3.5.1 if: a null condition is false.", """{"if":[null,"then","else"]}"""),
            C("§ 3.5.1 if: a null second condition is false.", """{"if":[false,"first",null,"second","else"]}"""),
            C("§ 3.5.1 if: a null branch taken gives null.", """{"if":[true,null,"else"]}"""),
            C("§ 3.5.1 and: null first is false.", """{"and":[null,true]}"""),
            C("§ 3.5.1 and: null second is false.", """{"and":[true,null]}"""),
            C("§ 3.5.1 or: null first is false.", """{"or":[null,false]}"""),
            C("§ 3.5.1 or: null second is false.", """{"or":[false,null]}"""),
            C("§ 3.5.1 or: null beside true is true.", """{"or":[null,true]}"""),
            C("§ 3.5.1 !: !null is true.", """{"!":[null]}"""),
            C("§ 3.5.1 ===: null === null is true.", """{"===":[null,null]}"""),
            C("§ 3.5.1 ===: null first, a value second, is false.", """{"===":[null,0]}"""),
            C("§ 3.5.1 ===: a value first, null second, is false.", """{"===":["",null]}"""),
            C("§ 3.5.1 ===: null is not false.", """{"===":[null,false]}"""),
            C("§ 3.5.1 !==: null !== null is false.", """{"!==":[null,null]}"""),
            C("§ 3.5.1 !==: null first, a value second, is true.", """{"!==":[null,0]}"""),
            C("§ 3.5.1 !==: a value first, null second, is true.", """{"!==":[1,null]}"""),
            C("§ 3.5.1 <: false when the first operand is null.", """{"<":[null,1]}"""),
            C("§ 3.5.1 <: false when the second operand is null.", """{"<":[-1,null]}"""),
            C("§ 3.5.1 <=: false when the first operand is null.", """{"<=":[null,1]}"""),
            C("§ 3.5.1 <=: false when the second operand is null.", """{"<=":[1,null]}"""),
            C("§ 3.5.1 <=: false when both are null.", """{"<=":[null,null]}"""),
            C("§ 3.5.1 >: false when the first operand is null.", """{">":[null,-1]}"""),
            C("§ 3.5.1 >: false when the second operand is null (null is not 0).", """{">":[1,null]}"""),
            C("§ 3.5.1 >=: false when the first operand is null.", """{">=":[null,1]}"""),
            C("§ 3.5.1 >=: false when the second operand is null.", """{">=":[1,null]}"""),
            C("§ 3.5.1 >=: false when both are null.", """{">=":[null,null]}"""),
            C("§ 3.5.1 +: null first gives null.", """{"+":[null,1]}"""),
            C("§ 3.5.1 +: null second gives null.", """{"+":[1,null]}"""),
            C("§ 3.5.1 +: null last of three gives null.", """{"+":[1,2,null]}"""),
            C("§ 3.5.1 *: null first gives null.", """{"*":[null,2]}"""),
            C("§ 3.5.1 *: null second gives null.", """{"*":[2,null]}"""),
            C("§ 3.5.1 -: the negation of null is null.", """{"-":[null]}"""),
            C("§ 3.5.1 -: null first gives null.", """{"-":[null,1]}"""),
            C("§ 3.5.1 -: null second gives null.", """{"-":[1,null]}"""),
            C("§ 3.5.1 /: null first gives null.", """{"/":[null,1]}"""),
            C("§ 3.5.1 /: null second gives null.", """{"/":[1,null]}"""),
            C("§ 3.5.1 %: null first gives null.", """{"%":[null,2]}"""),
            C("§ 3.5.1 %: null second gives null.", """{"%":[1,null]}"""),
            C("§ 3.5.1 min: null alone gives null.", """{"min":[null]}"""),
            C("§ 3.5.1 min: null first gives null.", """{"min":[null,1]}"""),
            C("§ 3.5.1 min: null second gives null.", """{"min":[1,null]}"""),
            C("§ 3.5.1 max: null alone gives null.", """{"max":[null]}"""),
            C("§ 3.5.1 max: null first gives null.", """{"max":[null,1]}"""),
            C("§ 3.5.1 max: null second gives null.", """{"max":[1,null]}"""),
            C("§ 3.5.1 in (list): a null needle is true when the list holds null.", """{"in":[null,[1,null]]}"""),
            C("§ 3.5.1 in (list): a null needle is false when the list does not hold null.", """{"in":[null,[1]]}"""),
            C("§ 3.5.1 in (list): a null list is false.", """{"in":[1,null]}"""),
            C("§ 3.5.1 in (list): a null list read from the data is false.", """{"in":["consult",{"var":"input.note_types.value"}]}""", """{"input":{"note_types":{"value":null}}}"""),
            C("§ 3.5.1 in (text): a null needle is false.", """{"in":[null,"chest pain"]}"""),
            C("§ 3.5.1 in (text): a null haystack is false.", """{"in":["pain",null]}"""),
            C("§ 3.5.1 cat: a null first operand contributes \"\".", """{"cat":[null,"b"]}"""),
            C("§ 3.5.1 cat: a null middle operand contributes \"\".", """{"cat":["a",null,"b"]}"""),
            C("§ 3.5.1 cat: a null last operand contributes \"\".", """{"cat":["a",null]}"""),
            C("§ 3.5.1 cat: null operands only give \"\".", """{"cat":[null,null]}"""),
            C("§ 3.5.1 cat: true, data that escaped validation, reads as null, so contributes \"\".", """{"cat":["a",true,"b"]}"""));

        yield return File(F, "text", "A null operand in each position of every custom text operation (§ 3.5.2, § 3.5.3, § 3.6.2).",
            C("§ 3.5.2 size: null gives null.", """{"size":[null]}"""),
            C("§ 3.5.2 size: an unsupplied input gives null.", """{"size":[{"var":"input.prior_notes.value"}]}""", """{"input":{"prior_notes":{"value":null,"content":null,"title":null}}}"""),
            C("§ 3.5.2 blank: null is blank.", """{"blank":[null]}"""),
            C("§ 3.5.2 blank: an unsupplied text input is blank.", """{"blank":[{"var":"input.consult_draft.content"}]}""", """{"input":{"consult_draft":{"value":null,"content":null,"title":null}}}"""),
            C("§ 3.5.2 words: null gives null.", """{"words":[null]}"""),
            C("§ 3.5.2 lines: null gives null.", """{"lines":[null]}"""),
            C("§ 3.5.2 lower: null gives null.", """{"lower":[null]}"""),
            C("§ 3.5.2 trim: null gives null.", """{"trim":[null]}"""),
            C("§ 3.5.2 plain: null gives null.", """{"plain":[null]}"""),
            C("§ 3.5.2 starts_with: a null text is false.", """{"starts_with":[null,"Dear"]}"""),
            C("§ 3.5.2 starts_with: a null prefix is false.", """{"starts_with":["Dear Dr Sample",null]}"""),
            C("§ 3.5.2 ends_with: a null text is false.", """{"ends_with":[null,"."]}"""),
            C("§ 3.5.2 ends_with: a null suffix is false.", """{"ends_with":["Continue letrozole.",null]}"""),
            C("§ 3.5.3 matches: a null text is false.", """{"matches":[null,"letrozole|femara"]}"""),
            C("§ 3.5.3 matches: a node that did not complete is false.", """{"matches":[{"var":"node.draft.content"},"letrozole"]}""", """{"node":{"draft":{"value":null,"content":null,"state":"skipped"}}}"""),
            C("§ 3.5.3 has_word: a null text is false.", """{"has_word":[null,"pain"]}"""));

        yield return File(F, "structural", "A null operand in each position of object and the check operations (§ 3.5.4, § 3.6.2); a check operation always gives a check_result.",
            C("§ 3.5.4 object: a field whose rule gives null holds null.", """{"object":{"feedback":null,"verdict":"ok"}}"""),
            C("§ 3.5.4 terms_subset: a null first list reads as empty, and passes.", Subset, Terms("null", Concepts(Flushes))),
            C("§ 3.5.4 terms_subset: a null second list reads as empty, so every codable concept is uncovered.", Subset, Terms(Concepts(Flushes, JointPain, NoId), "null")),
            C("§ 3.5.4 terms_subset: both lists null pass.", Subset, Terms("null", "null")),
            C("§ 3.5.4 all_pass: a null list judges nothing, and fails with failWith.", AllPassValue, """{"check":{"sections_checked":{"value":null}}}"""),
            C("§ 3.5.4 all_pass: an element whose value is null is not judged.", AllPassItems, Checked("items", Item("completed", Pass), Item("completed", "null"))),
            C("§ 3.5.4 count_pass: a null list with a min of 0 passes.", CountPass(0), """{"check":{"sections_checked":{"value":null}}}"""),
            C("§ 3.5.4 count_pass: a null list with a min of 1 fails.", CountPass(1), """{"check":{"sections_checked":{"value":null}}}"""));

        yield return File(F, "dates", "A null operand in each position of every date operation (§ 3.5.5, § 3.6.2).",
            C("§ 3.5.5 days_between: a null first date gives null.", """{"days_between":[null,"2026-01-01"]}"""),
            C("§ 3.5.5 days_between: a null second date gives null.", """{"days_between":["2026-01-01",null]}"""),
            C("§ 3.5.5 years_between: a null first date gives null.", """{"years_between":[null,"2026-01-01"]}"""),
            C("§ 3.5.5 years_between: a null second date gives null.", """{"years_between":["2026-01-01",null]}"""),
            C("§ 3.5.5 years_between: an unsupplied date of birth gives null.", """{"years_between":[{"var":"input.date_of_birth.value"},{"var":"run.date"}]}""", """{"input":{"date_of_birth":{"value":null,"content":null,"title":null}},"run":{"date":"2026-11-20"}}"""),
            C("§ 3.5.5 add_days: a null date gives null.", """{"add_days":[null,1]}"""),
            C("§ 3.5.5 add_days: a null n gives null.", """{"add_days":["2026-01-01",null]}"""),
            C("§ 3.5.5 comparing dates: < is false when a date is null.", """{"<":[null,"2026-01-01"]}"""),
            C("§ 3.5.5 comparing dates: >= is false when a date is null.", """{">=":["2026-01-01",null]}"""),
            C("§ 3.5.5 the screening example is false when the date of birth is unsupplied.", """{">=":[{"years_between":[{"var":"input.date_of_birth.value"},{"var":"run.date"}]},65]}""", """{"input":{"date_of_birth":{"value":null,"content":null,"title":null}},"run":{"date":"2026-11-20"}}"""));

        yield return File(F, "absence", "What a read gives (§ 3.6.1), the pitfall of § 3.6.2, and truthiness (§ 3.6.4). The data's members are the roots as an engine builds them.",
            C("§ 3.6.1 a supplied input's .value is its raw value.", """{"var":"input.age.value"}""", """{"input":{"age":{"value":70,"content":null,"title":null}}}"""),
            C("§ 3.6.1 a supplied text input's .content is its text.", """{"var":"input.consult_draft.content"}""", """{"input":{"consult_draft":{"value":"Margaret Okafor continues letrozole.","content":"Margaret Okafor continues letrozole.","title":null}}}"""),
            C("§ 3.6.1 a supplied input's .title is the chosen arm's title.", """{"var":"input.specialty.title"}""", """{"input":{"specialty":{"value":"medical_oncology","content":null,"title":"Medical oncology"}}}"""),
            C("§ 3.6.1 an unsupplied input's .value is null.", """{"var":"input.age.value"}""", """{"input":{"age":{"value":null,"content":null,"title":null}}}"""),
            C("§ 3.6.1 an unsupplied input's .content is null.", """{"var":"input.consult_draft.content"}""", """{"input":{"consult_draft":{"value":null,"content":null,"title":null}}}"""),
            C("§ 3.6.1 an unsupplied input's .title is null.", """{"var":"input.specialty.title"}""", """{"input":{"specialty":{"value":null,"content":null,"title":null}}}"""),
            C("§ 3.6.1 an unsupplied input's .value equals null.", """{"===":[{"var":"input.age.value"},null]}""", """{"input":{"age":{"value":null,"content":null,"title":null}}}"""),
            C("§ 3.6.1 an unsupplied input is missing.", """{"missing":["input.age.value"]}""", """{"input":{"age":{"value":null,"content":null,"title":null}}}"""),
            C("§ 3.6.1 a blank optional text input is missing (\"\" counts as missing).", """{"missing":["input.consult_draft.content"]}""", """{"input":{"consult_draft":{"value":"","content":"","title":null}}}"""),
            C("§ 3.6.1 a node that completed: its .value is its value.", """{"var":"node.draft.value"}""", """{"node":{"draft":{"value":"Plan: continue letrozole.","content":"Plan: continue letrozole.","state":"completed"}}}"""),
            C("§ 3.6.1 a node that completed: its .state is completed.", """{"var":"node.draft.state"}""", """{"node":{"draft":{"value":"Plan: continue letrozole.","content":"Plan: continue letrozole.","state":"completed"}}}"""),
            C("§ 3.6.1 a node that was skipped: its .value is null.", """{"var":"node.draft.value"}""", """{"node":{"draft":{"value":null,"content":null,"state":"skipped"}}}"""),
            C("§ 3.6.1 a node that was skipped: its .content is null.", """{"var":"node.draft.content"}""", """{"node":{"draft":{"value":null,"content":null,"state":"skipped"}}}"""),
            C("§ 3.6.1 a node that was skipped: its .state says skipped.", """{"var":"node.draft.state"}""", """{"node":{"draft":{"value":null,"content":null,"state":"skipped"}}}"""),
            C("§ 3.6.1 a node that was not run: its .value is null.", """{"var":"node.draft.value"}""", """{"node":{"draft":{"value":null,"content":null,"state":"not_run"}}}"""),
            C("§ 3.6.1 a node that was not run: its .state says not_run.", """{"var":"node.draft.state"}""", """{"node":{"draft":{"value":null,"content":null,"state":"not_run"}}}"""),
            C("§ 3.6.1 a node that did not complete is tested by its .state.", """{"===":[{"var":"node.draft.state"},"completed"]}""", """{"node":{"draft":{"value":null,"content":null,"state":"skipped"}}}"""),
            C("§ 3.6.1 a nullable field holding null reads as null.", """{"var":"node.review.value.feedback"}""", """{"node":{"review":{"value":{"verdict":"accept","feedback":null},"content":null,"state":"completed"}}}"""),
            C("§ 3.6.1 pass.previous on the first pass is null.", """{"var":"pass.previous"}""", """{"pass":{"number":1,"previous":null}}"""),
            C("§ 3.6.1 a read through pass.previous on the first pass is null.", """{"var":"pass.previous.node.judge.value"}""", """{"pass":{"number":1,"previous":null}}"""),
            C("§ 3.6.2 the pitfall: not over 65 is true when the age is unsupplied.", """{"!":[{">":[{"var":"input.age.value"},65]}]}""", """{"input":{"age":{"value":null,"content":null,"title":null}}}"""),
            C("§ 3.6.2 the pitfall's null test: known and not over 65 is false when the age is unsupplied.", """{"and":[{"!==":[{"var":"input.age.value"},null]},{"<=":[{"var":"input.age.value"},65]}]}""", """{"input":{"age":{"value":null,"content":null,"title":null}}}"""),
            C("§ 3.6.4 truthiness: true decides true.", """{"if":[true,"true","false"]}"""),
            C("§ 3.6.4 truthiness: false decides false.", """{"if":[false,"true","false"]}"""),
            C("§ 3.6.4 truthiness: null decides false.", """{"if":[null,"true","false"]}"""),
            C("§ 3.6.4 truthiness: missing's empty list decides false.", """{"if":[{"missing":["input.age.value"]},"true","false"]}""", """{"input":{"age":{"value":70,"content":null,"title":null}}}"""),
            C("§ 3.6.4 truthiness: missing's non-empty list decides true.", """{"if":[{"missing":["input.age.value"]},"true","false"]}""", """{"input":{"age":{"value":null,"content":null,"title":null}}}"""));
    }

    // ---- text-vectors (part 03 § 5.2, D20) ----

    /// <summary>Every character of Unicode's <c>White_Space</c> property, with its name.</summary>
    private static readonly (string Escape, string Name)[] WhiteSpace =
    [
        ("\\t", "U+0009 CHARACTER TABULATION"),
        ("\\n", "U+000A LINE FEED"),
        ("\\u000b", "U+000B LINE TABULATION"),
        ("\\f", "U+000C FORM FEED"),
        ("\\r", "U+000D CARRIAGE RETURN"),
        (" ", "U+0020 SPACE"),
        ("\\u0085", "U+0085 NEXT LINE"),
        ("\\u00a0", "U+00A0 NO-BREAK SPACE"),
        ("\\u1680", "U+1680 OGHAM SPACE MARK"),
        ("\\u2000", "U+2000 EN QUAD"),
        ("\\u2001", "U+2001 EM QUAD"),
        ("\\u2002", "U+2002 EN SPACE"),
        ("\\u2003", "U+2003 EM SPACE"),
        ("\\u2004", "U+2004 THREE-PER-EM SPACE"),
        ("\\u2005", "U+2005 FOUR-PER-EM SPACE"),
        ("\\u2006", "U+2006 SIX-PER-EM SPACE"),
        ("\\u2007", "U+2007 FIGURE SPACE"),
        ("\\u2008", "U+2008 PUNCTUATION SPACE"),
        ("\\u2009", "U+2009 THIN SPACE"),
        ("\\u200a", "U+200A HAIR SPACE"),
        ("\\u2028", "U+2028 LINE SEPARATOR"),
        ("\\u2029", "U+2029 PARAGRAPH SEPARATOR"),
        ("\\u202f", "U+202F NARROW NO-BREAK SPACE"),
        ("\\u205f", "U+205F MEDIUM MATHEMATICAL SPACE"),
        ("\\u3000", "U+3000 IDEOGRAPHIC SPACE"),
    ];

    /// <summary>Characters that look like space and are not <c>White_Space</c>.</summary>
    private static readonly (string Escape, string Name)[] NotWhiteSpace =
    [
        ("\\u200b", "U+200B ZERO WIDTH SPACE"),
        ("\\u180e", "U+180E MONGOLIAN VOWEL SEPARATOR"),
        ("\\ufeff", "U+FEFF ZERO WIDTH NO-BREAK SPACE"),
        ("\\u2060", "U+2060 WORD JOINER"),
    ];

    /// <summary>A word character of each category (§ 5.3) and its name.</summary>
    private static readonly (string Escape, string Category)[] WordCharacters =
    [
        ("A", "Lu (uppercase letter) A"),
        ("a", "Ll (lowercase letter) a"),
        ("\\u01c5", "Lt (titlecase letter) U+01C5"),
        ("\\u02b0", "Lm (modifier letter) U+02B0"),
        ("\\u05d0", "Lo (other letter) U+05D0"),
        ("\\u0301", "Mn (non-spacing mark) U+0301"),
        ("\\u0903", "Mc (spacing mark) U+0903"),
        ("\\u20dd", "Me (enclosing mark) U+20DD"),
        ("7", "Nd (decimal digit) 7"),
        ("\\u0663", "Nd (decimal digit) U+0663"),
        ("_", "Pc (connector punctuation) _"),
        ("\\u203f", "Pc (connector punctuation) U+203F"),
        ("\\ud801\\udc00", "Lu (uppercase letter) U+10400 (outside the Basic Multilingual Plane)"),
    ];

    /// <summary>Characters of other categories, which are not word characters.</summary>
    private static readonly (string Escape, string Category)[] NotWordCharacters =
    [
        ("\\u00b2", "No (other number) U+00B2"),
        ("\\u216b", "Nl (letter number) U+216B"),
        ("-", "Pd (dash punctuation) -"),
        ("(", "Ps (open punctuation) ("),
        (".", "Po (other punctuation) ."),
        ("+", "Sm (math symbol) +"),
        ("\\u00b0", "So (other symbol) U+00B0"),
        ("\\ud83d\\ude00", "So (other symbol) U+1F600 (outside the Basic Multilingual Plane)"),
        ("\\u200b", "Cf (format) U+200B"),
        ("\\u00a0", "Zs (space separator) U+00A0"),
    ];

    private static IEnumerable<LogicFile> TextVectorFiles()
    {
        const string F = TextVectors;
        yield return File(F, "lower", "lower: the invariant culture's simple case mapping, outside ASCII (§ 3.5.2), as the runtime lock.json records gives it.",
            C("§ 3.5.2 lower: U+03A9 GREEK CAPITAL LETTER OMEGA lowers to U+03C9.", """{"lower":["Ω"]}"""),
            C("§ 3.5.2 lower: U+2126 OHM SIGN lowers to U+03C9 GREEK SMALL LETTER OMEGA.", """{"lower":["\u2126"]}"""),
            C("§ 3.5.2 lower: U+0130 LATIN CAPITAL LETTER I WITH DOT ABOVE is unchanged: the invariant culture does not map it (Unicode's simple mapping would give i).", """{"lower":["İ"]}"""),
            C("§ 3.5.2 lower: U+0049 LATIN CAPITAL LETTER I lowers to i in the invariant culture, not to dotless ı.", """{"lower":["I"]}"""),
            C("§ 3.5.2 lower: U+1E9E LATIN CAPITAL LETTER SHARP S lowers to U+00DF.", """{"lower":["ẞ"]}"""),
            C("§ 3.5.2 lower: U+00DF LATIN SMALL LETTER SHARP S is already lower case.", """{"lower":["ß"]}"""),
            C("§ 3.5.2 lower: U+01C4 LATIN CAPITAL LETTER DZ WITH CARON lowers to U+01C6.", """{"lower":["Ǆ"]}"""),
            C("§ 3.5.2 lower: U+01C5, its titlecase form, lowers to U+01C6.", """{"lower":["ǅ"]}"""),
            C("§ 3.5.2 lower: U+13A0 CHEROKEE LETTER A lowers to U+AB70.", """{"lower":["Ꭰ"]}"""),
            C("§ 3.5.2 lower: U+13F0 CHEROKEE LETTER YE lowers to U+13F8.", """{"lower":["Ᏸ"]}"""),
            C("§ 3.5.2 lower: U+10400 DESERET CAPITAL LETTER LONG I, outside the Basic Multilingual Plane, lowers to U+10428.", """{"lower":["𐐀"]}"""),
            C("§ 3.5.2 lower: U+212A KELVIN SIGN lowers to k.", """{"lower":["\u212a"]}"""),
            C("§ 3.5.2 lower: U+FF21 FULLWIDTH LATIN CAPITAL LETTER A lowers to U+FF41.", """{"lower":["Ａ"]}"""),
            C("§ 3.5.2 lower: E and U+0301 COMBINING ACUTE ACCENT lower to e and the same mark.", """{"lower":["E\u0301"]}"""),
            C("§ 3.5.2 lower: a mixed sentence outside ASCII.", """{"lower":["ÉLAN ΟΔΟΣ Ꭰ 𐐀"]}"""));

        var trim = new List<LogicCase>();
        foreach (var (escape, name) in WhiteSpace)
            trim.Add(C($"§ 3.5.2 trim: {name} is White_Space, removed at either end.", $$"""{"trim":["{{escape}}pain{{escape}}"]}"""));
        foreach (var (escape, name) in NotWhiteSpace)
            trim.Add(C($"§ 3.5.2 trim: {name} is not White_Space, so kept.", $$"""{"trim":["{{escape}}pain{{escape}}"]}"""));
        trim.Add(C("§ 3.5.2 trim: every White_Space character at once trims to empty text.", $$"""{"trim":["{{string.Concat(WhiteSpace.Select(w => w.Escape))}}"]}"""));
        yield return File(F, "trim", "trim: every White_Space character removed at either end, and look-alikes kept (§ 3.5.2).", [.. trim]);

        var words = new List<LogicCase>();
        foreach (var (escape, name) in WhiteSpace)
            words.Add(C($"§ 3.5.2 words: {name} is White_Space, so it separates two words.", $$"""{"words":["chest{{escape}}pain"]}"""));
        foreach (var (escape, name) in NotWhiteSpace)
            words.Add(C($"§ 3.5.2 words: {name} is not White_Space, so the text is one word.", $$"""{"words":["chest{{escape}}pain"]}"""));
        words.Add(C("§ 3.5.2 words: every White_Space character at once is no word.", $$"""{"words":["{{string.Concat(WhiteSpace.Select(w => w.Escape))}}"]}"""));
        yield return File(F, "words", "words: every White_Space character separates words, and look-alikes do not (§ 3.5.2).", [.. words]);

        var hasWord = new List<LogicCase>();
        foreach (var (escape, category) in WordCharacters)
        {
            hasWord.Add(C($"§ 3.5.3 has_word: {category} before the word is a word character.", $$"""{"has_word":["{{escape}}pain","pain"]}"""));
            hasWord.Add(C($"§ 3.5.3 has_word: {category} after the word is a word character.", $$"""{"has_word":["pain{{escape}}","pain"]}"""));
        }
        foreach (var (escape, category) in NotWordCharacters)
            hasWord.Add(C($"§ 3.5.3 has_word: {category} on either side is not a word character.", $$"""{"has_word":["{{escape}}pain{{escape}}","pain"]}"""));
        hasWord.Add(C("§ 3.5.3 has_word: the whole text is the word.", """{"has_word":["pain","pain"]}"""));
        hasWord.Add(C("§ 3.5.3 has_word: a word that ends in a word character still needs a boundary after it.", """{"has_word":["chest pains","chest pain"]}"""));
        hasWord.Add(C("§ 3.5.3 has_word: a word outside ASCII.", """{"has_word":["la douleur thoracique","douleur"]}"""));
        hasWord.Add(C("§ 3.5.3 has_word: a word that begins with a non-word character is bounded by what is before it.", """{"has_word":["see (pain)","(pain"]}"""));
        yield return File(F, "has_word", "has_word: each category of word character beside the word, and other categories that are not (§ 3.5.3).", [.. hasWord]);
    }

    // ---- number-vectors (part 03 § 7, D19) ----

    private static IEnumerable<LogicFile> NumberVectorFiles()
    {
        const string F = NumberVectors;
        yield return File(F, "arithmetic", "Numbers are IEEE doubles; results are written in their RFC 8785 form (§ 3.7; D19).",
            C("§ 3.7 IEEE: 0.1 + 0.2 is 0.30000000000000004.", """{"+":[0.1,0.2]}"""),
            C("§ 3.7 IEEE: 0.1 + 0.2 is not 0.3.", """{"===":[{"+":[0.1,0.2]},0.3]}"""),
            C("§ 3.7 IEEE: 1 / 3.", """{"/":[1,3]}"""),
            C("§ 3.7 IEEE: 0.1 * 3.", """{"*":[0.1,3]}"""),
            C("§ 3.7 + of three.", """{"+":[1,2,3]}"""),
            C("§ 3.7 - of two.", """{"-":[5,3]}"""),
            C("§ 3.7 one-operand - negates.", """{"-":[5]}"""),
            C("§ 3.7 the negation of 0 is 0.", """{"-":[0]}"""),
            C("§ 3.7 a product that is negative zero is written 0.", """{"*":[-1,0]}"""),
            C("§ 3.7 /: a fraction.", """{"/":[1,4]}"""),
            C("D19 %: the remainder has the sign of the dividend.", """{"%":[-7,3]}"""),
            C("D19 %: a negative divisor.", """{"%":[7,-3]}"""),
            C("D19 %: of fractions.", """{"%":[5.5,2]}"""),
            C("§ 3.7 min of three.", """{"min":[3,1,2]}"""),
            C("§ 3.7 max of one.", """{"max":[3]}"""),
            C("§ 3.7 max with a negative.", """{"max":[-1,-2.5]}"""),
            C("§ 3.5.1 cat: a number in its RFC 8785 form.", """{"cat":["a",1.50,"b"]}"""),
            C("§ 3.5.1 cat: a large number in its RFC 8785 form.", """{"cat":["n=",1e21]}"""),
            C("§ 3.5.1 cat: a small number in its RFC 8785 form.", """{"cat":["n=",1e-7]}"""),
            C("§ 3.5.1 cat: a whole number written with a fraction is written whole.", """{"cat":["n=",2.0]}"""),
            C("§ 3.5.1 cat: a computed number.", """{"cat":[{"+":[0.1,0.2]}," mg"]}"""),
            C("§ 3.5.1 strings are never coerced: \"3\" + 1 is null.", """{"+":["3",1]}"""),
            C("§ 3.5.1 strings are never coerced: 5 < \"a\" is false.", """{"<":[5,"a"]}"""),
            C("§ 3.5.1 strings are never coerced: 1 === \"1\" is false.", """{"===":[1,"1"]}"""),
            C("§ 3.5.1 true is not a number: true + 1 is null.", """{"+":[true,1]}"""));

        yield return File(F, "non-finite", "An operation whose result is not finite (an overflow, NaN), or a division or remainder by zero, gives null (§ 3.7, § 3.5.1).",
            C("§ 3.7 an overflow of * gives null.", """{"*":[1e308,10]}"""),
            C("§ 3.7 a negative overflow of * gives null.", """{"*":[-1e308,10]}"""),
            C("§ 3.7 an overflow of + gives null.", """{"+":[1e308,1e308]}"""),
            C("§ 3.7 an overflow of - gives null.", """{"-":[-1e308,1e308]}"""),
            C("§ 3.7 an overflow of / gives null.", """{"/":[1e308,0.1]}"""),
            C("§ 3.5.1 division by zero gives null.", """{"/":[1,0]}"""),
            C("§ 3.5.1 0 / 0 gives null.", """{"/":[0,0]}"""),
            C("§ 3.5.1 remainder by zero gives null.", """{"%":[1,0]}"""),
            C("§ 3.7 an overflow inside a sum makes the sum null.", """{"+":[{"*":[1e308,10]},1]}"""),
            C("§ 3.7 an overflow inside min makes min null.", """{"min":[{"*":[1e308,10]},1]}"""),
            C("§ 3.7 an overflow compared is false.", """{"<":[{"*":[1e308,10]},1]}"""),
            C("§ 3.7 an overflow equals null.", """{"===":[{"*":[1e308,10]},null]}"""),
            C("§ 3.7 the largest double is finite.", """{"*":[1.7976931348623157e308,1]}"""));

        yield return File(F, "integers", "integer is a kind of number: a whole result is written without a fraction, and 1.0 is 1 (§ 3.7).",
            C("§ 3.7 1.0 === 1 is true.", """{"===":[1.0,1]}"""),
            C("§ 3.7 1.0 !== 1 is false.", """{"!==":[1.0,1]}"""),
            C("§ 3.7 a whole sum of fractions is written without a fraction.", """{"+":[0.5,0.5]}"""),
            C("§ 3.7 a whole product is written without a fraction.", """{"*":[2.5,2]}"""),
            C("§ 3.7 a whole quotient is written without a fraction.", """{"/":[6,3]}"""),
            C("§ 3.7 a number written with a fraction of zero is written whole.", """{"+":[2.0,0]}"""),
            C("§ 3.7 2 is in a list holding 2.0.", """{"in":[2,[1,2.0]]}"""),
            C("§ 3.7 a large whole number is written without an exponent below 1e21.", """{"*":[1e20,1]}"""),
            C("§ 3.7 a whole number of 1e21 or more is written with an exponent.", """{"*":[1e21,1]}"""),
            C("§ 3.7 2^53 + 1 is 2^53 in a double.", """{"+":[9007199254740992,1]}"""),
            C("§ 3.7 size gives a whole number.", """{"size":["abc"]}"""),
            C("§ 3.7 days_between gives a whole number.", """{"days_between":["2026-01-01","2026-03-01"]}"""),
            C("§ 3.7 a whole number compares equal to a computed whole number.", """{"===":[{"size":["abc"]},3.0]}"""));
    }

    // ---- date-vectors (part 03 § 5.5, D18) ----

    private static IEnumerable<LogicFile> DateVectorFiles()
    {
        const string F = DateVectors;
        yield return File(F, "days_between", "days_between: whole days in the proleptic Gregorian calendar, the bounds of years 0001 and 9999, and malformed dates giving null (§ 3.5.5; D18).",
            C("§ 3.5.5 days_between: across 29 February of a leap year.", """{"days_between":["2024-02-28","2024-03-01"]}"""),
            C("§ 3.5.5 days_between: 28 February to 1 March in a year with no 29 February.", """{"days_between":["2026-02-28","2026-03-01"]}"""),
            C("§ 3.5.5 days_between: 2000 is a leap year (divisible by 400).", """{"days_between":["2000-02-28","2000-03-01"]}"""),
            C("§ 3.5.5 days_between: 1900 is not a leap year (divisible by 100).", """{"days_between":["1900-02-28","1900-03-01"]}"""),
            C("§ 3.5.5 days_between: a leap year has 366 days.", """{"days_between":["2024-01-01","2025-01-01"]}"""),
            C("§ 3.5.5 days_between: from 0001-01-01 to 9999-12-31.", """{"days_between":["0001-01-01","9999-12-31"]}"""),
            C("§ 3.5.5 days_between: from 9999-12-31 back to 0001-01-01.", """{"days_between":["9999-12-31","0001-01-01"]}"""),
            C("D18 days_between: a month of one digit is malformed, so null.", """{"days_between":["2026-2-3","2026-02-04"]}"""),
            C("D18 days_between: 30 February is malformed, so null.", """{"days_between":["2026-02-01","2026-02-30"]}"""),
            C("D18 days_between: 29 February of a year with no 29 February is malformed, so null.", """{"days_between":["2026-02-01","2026-02-29"]}"""),
            C("D18 days_between: month 13 is malformed, so null.", """{"days_between":["2026-13-01","2026-01-01"]}"""),
            C("D18 days_between: year 0000 is outside 0001 to 9999, so null.", """{"days_between":["0000-12-31","0001-01-01"]}"""),
            C("D18 days_between: a time of day is malformed, so null.", """{"days_between":["2026-01-01T00:00:00","2026-01-02"]}"""),
            C("D18 days_between: a space before the date is malformed, so null.", """{"days_between":[" 2026-01-01","2026-01-02"]}"""),
            C("D18 days_between: slashes are malformed, so null.", """{"days_between":["2026/01/01","2026-01-02"]}"""),
            C("D18 days_between: digits other than 0 to 9 are malformed, so null.", """{"days_between":["２０２６-01-01","2026-01-02"]}"""),
            C("D18 days_between: a number is not a date, so null.", """{"days_between":[20260101,"2026-01-02"]}"""));

        yield return File(F, "years_between", "years_between: completed years, every 29 February case, the bounds of years 0001 and 9999, malformed dates (§ 3.5.5; D18).",
            C("§ 3.5.5 years_between: born 29 February, 0 on 28 February of a year with no 29 February.", """{"years_between":["2008-02-29","2009-02-28"]}"""),
            C("§ 3.5.5 years_between: born 29 February, 1 on 1 March of a year with no 29 February.", """{"years_between":["2008-02-29","2009-03-01"]}"""),
            C("§ 3.5.5 years_between: born 29 February, 3 on 28 February of the next leap year.", """{"years_between":["2008-02-29","2012-02-28"]}"""),
            C("§ 3.5.5 years_between: born 29 February, 4 on 29 February of the next leap year.", """{"years_between":["2008-02-29","2012-02-29"]}"""),
            C("§ 3.5.5 years_between: born 29 February, the anniversary in 1900 (no 29 February) is 1 March.", """{"years_between":["1896-02-29","1900-02-28"]}"""),
            C("§ 3.5.5 years_between: born 29 February, 4 on 1 March 1900.", """{"years_between":["1896-02-29","1900-03-01"]}"""),
            C("§ 3.5.5 years_between: born 28 February, 1 on 28 February of a leap year.", """{"years_between":["2007-02-28","2008-02-28"]}"""),
            C("§ 3.5.5 years_between: born 1 March, 0 on 29 February of a leap year.", """{"years_between":["2007-03-01","2008-02-29"]}"""),
            C("§ 3.5.5 years_between: mirrored, 29 February to 28 February of a year with no 29 February is 0.", """{"years_between":["2009-02-28","2008-02-29"]}"""),
            C("§ 3.5.5 years_between: mirrored, 1 March back to 29 February is -1.", """{"years_between":["2009-03-01","2008-02-29"]}"""),
            C("§ 3.5.5 years_between: 0 for the same date.", """{"years_between":["2026-11-20","2026-11-20"]}"""),
            C("§ 3.5.5 years_between: from 0001-01-01 to 9999-12-31.", """{"years_between":["0001-01-01","9999-12-31"]}"""),
            C("§ 3.5.5 years_between: from 9999-12-31 back to 0001-01-01.", """{"years_between":["9999-12-31","0001-01-01"]}"""),
            C("D18 years_between: 30 February is malformed, so null.", """{"years_between":["2026-02-30","2027-01-01"]}"""),
            C("D18 years_between: year 10000 is malformed, so null.", """{"years_between":["2026-01-01","10000-01-01"]}"""));

        yield return File(F, "add_days", "add_days: through 29 February, at the bounds of years 0001 and 9999, a non-integer n, malformed dates (§ 3.5.5; D18).",
            C("§ 3.5.5 add_days: 28 February plus 1 in a leap year is 29 February.", """{"add_days":["2024-02-28",1]}"""),
            C("§ 3.5.5 add_days: 28 February plus 1 in a year with no 29 February is 1 March.", """{"add_days":["2026-02-28",1]}"""),
            C("§ 3.5.5 add_days: 29 February plus 365 days.", """{"add_days":["2024-02-29",365]}"""),
            C("§ 3.5.5 add_days: 1 March minus 1 in 2000, a leap year.", """{"add_days":["2000-03-01",-1]}"""),
            C("§ 3.5.5 add_days: 1 March minus 1 in 1900, not a leap year.", """{"add_days":["1900-03-01",-1]}"""),
            C("§ 3.5.5 add_days: 0001-01-01 plus 3652058 is 9999-12-31.", """{"add_days":["0001-01-01",3652058]}"""),
            C("§ 3.5.5 add_days: 0001-01-01 plus 3652059 is past 9999, so null.", """{"add_days":["0001-01-01",3652059]}"""),
            C("§ 3.5.5 add_days: 9999-12-31 minus 3652058 is 0001-01-01.", """{"add_days":["9999-12-31",-3652058]}"""),
            C("§ 3.5.5 add_days: 9999-12-31 plus 1 is past 9999, so null.", """{"add_days":["9999-12-31",1]}"""),
            C("§ 3.5.5 add_days: 0001-01-01 minus 1 is before 0001, so null.", """{"add_days":["0001-01-01",-1]}"""),
            C("§ 3.5.5 add_days: 0001-01-01 plus 0 is itself.", """{"add_days":["0001-01-01",0]}"""),
            C("§ 3.5.5 add_days: a non-integer n gives null.", """{"add_days":["2026-01-01",0.5]}"""),
            C("§ 3.5.5 add_days: n written 2.0 is an integer.", """{"add_days":["2026-01-01",2.0]}"""),
            C("§ 3.5.5 add_days: a huge n gives null.", """{"add_days":["2026-01-01",1e300]}"""),
            C("D18 add_days: a malformed date gives null.", """{"add_days":["2026-02-30",1]}"""),
            C("D18 add_days: a date with a time of day gives null.", """{"add_days":["2026-01-01T09:00:00Z",1]}"""));

        yield return File(F, "comparisons", "Comparing dates with <, >, <=, >= and === (§ 3.5.5, § 3.5.1; D18).",
            C("§ 3.5.5 < on two dates compares them chronologically.", """{"<":["2026-01-02","2026-01-10"]}"""),
            C("§ 3.5.5 > across a year.", """{">":["2027-01-01","2026-12-31"]}"""),
            C("§ 3.5.5 >= on the same date is true.", """{">=":["2026-01-02","2026-01-02"]}"""),
            C("§ 3.5.5 <= on the same date is true.", """{"<=":["2026-01-02","2026-01-02"]}"""),
            C("§ 3.5.5 < on the same date is false.", """{"<":["2026-01-02","2026-01-02"]}"""),
            C("§ 3.5.5 the bounds: 0001-01-01 < 9999-12-31.", """{"<":["0001-01-01","9999-12-31"]}"""),
            C("§ 3.5.5 === on two equal dates is true.", """{"===":["2026-01-02","2026-01-02"]}"""),
            C("§ 3.5.5 === on two different dates is false.", """{"===":["2026-01-02","2026-01-03"]}"""),
            C("§ 3.5.5 a computed date compared with a date.", """{"<":[{"add_days":["2026-01-31",1]},"2026-02-02"]}"""),
            C("§ 3.5.5 a date read from run.date compared with a date.", """{">=":[{"var":"run.date"},"2026-11-20"]}""", """{"run":{"date":"2026-11-20"}}"""),
            C("D18 < with a malformed date is false.", """{"<":["2026-02-30","2026-03-01"]}"""),
            C("D18 > with a malformed date is false.", """{">":["2026-03-01","2026-2-3"]}"""),
            C("D18 < on two strings that are not dates is false.", """{"<":["a","b"]}"""),
            C("D18 < on a date and a number is false.", """{"<":["2026-01-01",20270101]}"""));
    }
}
