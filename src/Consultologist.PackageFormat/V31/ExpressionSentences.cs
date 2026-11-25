using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1501 (specVersion 31, area 04): every sentence the expression checker says, one method each, named after what
/// it says, as <see cref="FormatSentences"/> does for area 03. Each is the row of <c>v31/sentences.json</c> (part 03
/// § 11) or the area's own <c>A04-NNN</c> with its placeholders filled in, character for character. <c>&lt;site&gt;</c>
/// is <see cref="ExpressionSite.Subject"/> (D4), <c>&lt;member&gt;</c> its <see cref="ExpressionSite.Member"/>, and
/// every refusal names the site's file. No sentence carries a value from an input or a model: only ids, paths,
/// operation names, type strings, counts, limits and package text (a literal, a pattern). S03-036, S03-039 and
/// S03-044 are the flags' (<see cref="LineageFlags"/>).
/// </summary>
internal static class ExpressionSentences
{
    /// <summary>The sentence of one fault of the written form (<paramref name="rule"/> gives a rule too deep its place, S01-041).</summary>
    public static SourceRefusal Shape(ExpressionSite site, ShapeFault fault, JsonNode? rule = null) => fault.Kind switch
    {
        ShapeFaultKind.TooDeep => SourceSentences.JsonTooDeep(site.Path, DeepPointer(rule)),
        ShapeFaultKind.NotARule => NotARule(site, fault.Count),
        ShapeFaultKind.LooseEquality => LooseEquality(site),
        ShapeFaultKind.LooseInequality => LooseInequality(site),
        ShapeFaultKind.Substr => Substr(site),
        ShapeFaultKind.UnknownOperation => UnknownOperation(site, fault.Operation),
        ShapeFaultKind.BareOperand => BareOperand(site, fault.Operation),
        ShapeFaultKind.Arity => Arity(site, fault.Operation, fault.Count, fault.Text),
        ShapeFaultKind.IfWithoutElse => IfWithoutElse(site),
        ShapeFaultKind.VarDefault => VarDefault(site),
        ShapeFaultKind.VarNotText => VarNotText(site, fault.Text),
        ShapeFaultKind.PathNotAPath => NotAPath(site, fault.Text),
        ShapeFaultKind.PathPosition => Position(site, fault.Text),
        ShapeFaultKind.ObjectOperand => ObjectOperand(site, fault.Text),
        ShapeFaultKind.ComputedLiteral => ComputedLiteral(site, fault.Operation, fault.Text),
        ShapeFaultKind.LiteralNotText => LiteralNotText(site, fault.Operation, fault.Text, fault.Detail),
        ShapeFaultKind.CountPassMin => CountPassMin(site, fault.Text),
        ShapeFaultKind.BlankWord => BlankWord(site),
        ShapeFaultKind.PatternNotIRegexp => PatternNotIRegexp(site, fault.Text, fault.Detail),
        ShapeFaultKind.PatternTooLong => PatternTooLong(site, fault.Text, fault.Count),
        ShapeFaultKind.PatternRepeats => PatternRepeats(site, fault.Text, fault.Count),
        _ => PatternDoesNotCompile(site, fault.Text, fault.Detail),
    };

    /// <summary>
    /// The words a rule fails with at run time (part 03 § 11.3; D22): S03-040 for text over the limit, S03-041 for a
    /// condition that yields no boolean, A04-011 for a declined rule, a fault inside the evaluator, or a <c>plain</c> given
    /// Markdown nested too deeply to convert, and (#1503 R2) A06-001 for <c>matches</c> or <c>has_word</c> past the
    /// evaluator's time limit, the limit in seconds (at most three decimals, no trailing zeros).
    /// </summary>
    public static string RunTime(string subject, string member, JsonLogicFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Kind switch
        {
            JsonLogicFailureKind.MatchesTextTooLong =>
                $"{subject} {member} could not be decided: matches was given {failure.Detail} MiB of text; the most is 1 MiB.",
            JsonLogicFailureKind.NotABoolean => $"{subject} {member} yields {failure.Detail}; a condition yields a boolean.",
            JsonLogicFailureKind.MatchTooSlow =>
                $"{subject} {member} could not be decided: {failure.Detail} took longer than {Seconds(failure.Limit ?? TimeSpan.Zero)} seconds on this text. Use a simpler pattern.",
            // Declined, Fault and PlainNotConverted: A04-011, the detail as the failure gives it
            _ => $"{subject} {member} could not be evaluated: {failure.Detail}.",
        };
    }

    /// <summary>A time limit in seconds, invariant, at most three decimals and no trailing zeros (<c>2</c>, <c>0.05</c>).</summary>
    private static string Seconds(TimeSpan limit) => limit.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>S03-002: text compared with a literal by <c>===</c> or <c>!==</c>.</summary>
    public static SourceRefusal ComparesTextWithLiteral(ExpressionSite site, string path, string format) =>
        Say(site, "S03-002", $"{site.Subject} compares {path} ({format}) with a literal; text is tested by whether it is blank, its size, or in.");

    /// <summary>S03-003: text compared with text.</summary>
    public static SourceRefusal ComparesTextWithText(ExpressionSite site, string path, string format, string other, string otherFormat) =>
        Say(site, "S03-003", $"{site.Subject} compares {path} ({format}) with {other} ({otherFormat}); text is tested by whether it is blank, its size, or in.");

    /// <summary>S03-004: <c>==</c>.</summary>
    public static SourceRefusal LooseEquality(ExpressionSite site) =>
        Say(site, "S03-004", $"{site.Subject} uses ==; equality is written === (shown as =).");

    /// <summary>S03-005: <c>!=</c>.</summary>
    public static SourceRefusal LooseInequality(ExpressionSite site) =>
        Say(site, "S03-005", $"{site.Subject} uses !=; inequality is written !== (shown as ≠).");

    /// <summary>S03-006: an operation outside part 03 § 5; the list is every allowed name (D8).</summary>
    public static SourceRefusal UnknownOperation(ExpressionSite site, string operation) =>
        Say(site, "S03-006", $"{site.Subject} uses {operation}, which is not a JSON Logic operation of this format ({JsonLogicOperations.AllowedList}).");

    /// <summary>S03-007: <c>substr</c>.</summary>
    public static SourceRefusal Substr(ExpressionSite site) =>
        Say(site, "S03-007", $"{site.Subject} uses substr; text is cut with trim or plain, and measured with size.");

    /// <summary>S03-008: an ordering of what is neither a number nor a date.</summary>
    public static SourceRefusal Orders(ExpressionSite site, string path, string format) =>
        Say(site, "S03-008", $"{site.Subject} orders {path} ({format}); <, >, ≤ and ≥ compare numbers or dates.");

    /// <summary>S03-009: <c>!</c> on text.</summary>
    public static SourceRefusal NotOnText(ExpressionSite site, string path, string format) =>
        Say(site, "S03-009", $"{site.Subject} tests {path} ({format}) with not; not takes a boolean — test text with blank.");

    /// <summary>S03-010: <paramref name="count"/> operands where the operation takes <paramref name="takes"/> (D8's words).</summary>
    public static SourceRefusal Arity(ExpressionSite site, string operation, int count, string takes) =>
        Say(site, "S03-010", $"{site.Subject} gives {operation} {count.ToString(CultureInfo.InvariantCulture)} operands; it takes {takes}.");

    /// <summary>S03-011: operands not written as an array.</summary>
    public static SourceRefusal BareOperand(ExpressionSite site, string operation) =>
        Say(site, "S03-011", $"{site.Subject} writes {operation} with a bare operand; operands are written as an array.");

    /// <summary>S03-012: <c>var</c> with an array (a default).</summary>
    public static SourceRefusal VarDefault(ExpressionSite site) =>
        Say(site, "S03-012", $"{site.Subject} reads var with a default; every read is one path.");

    /// <summary>S03-013: a path with a segment of digits only.</summary>
    public static SourceRefusal Position(ExpressionSite site, string path) =>
        Say(site, "S03-013", $"{site.Subject} reads {path}, a position in a list; lists are read whole, or by identity through .items.");

    /// <summary>S03-014: a read of nothing the site can see.</summary>
    public static SourceRefusal DoesNotExist(ExpressionSite site, string path) =>
        Say(site, "S03-014", $"{site.Subject} reads {path}, which doesn't exist here.");

    /// <summary>S03-015: an input read bare.</summary>
    public static SourceRefusal BareInput(ExpressionSite site, string id) =>
        Say(site, "S03-015", $"{site.Subject} reads input.{id}; an input is read as input.{id}.value, .content or .title.");

    /// <summary>S03-016: <c>.title</c> on a plain enum; <paramref name="path"/> is the read without <c>.title</c>.</summary>
    public static SourceRefusal TitleOfPlainEnum(ExpressionSite site, string path, string format) =>
        Say(site, "S03-016", $"{site.Subject} reads {path}.title, but {format} is a plain enum with no titles; read .value, or give the format oneOf with const and title.");

    /// <summary>S03-017: a literal compared with a <c>.title</c> that is none of its titles (joined by <c>, </c>).</summary>
    public static SourceRefusal NotATitle(ExpressionSite site, string path, string text, string titles) =>
        Say(site, "S03-017", $"{site.Subject} compares {path}.title with \"{text}\", which is not one of its titles ({titles}).");

    /// <summary>S03-018: a read of the profile; <paramref name="field"/> is what follows <c>profile.</c>.</summary>
    public static SourceRefusal ReadsProfile(ExpressionSite site, string field) =>
        Say(site, "S03-018", $"{site.Subject} reads profile.{field}; profile fields are read only in deliverable templates and macro bodies, never in conditions or computed expressions.");

    /// <summary>S03-019: a read of <c>snippet</c>, <c>part</c>, <c>macro</c>, <c>renderer</c> or <c>deliverable</c>; <paramref name="path"/> as written.</summary>
    public static SourceRefusal NotAJsonLogicRoot(ExpressionSite site, string path) =>
        Say(site, "S03-019", $"{site.Subject} reads {path}; JSON Logic reads input, node, check, item, pass, param and run.");

    /// <summary>S03-020: a deliverable's <c>if</c> reads a node's text.</summary>
    public static SourceRefusal DeliverableReadsContent(ExpressionSite site, string node) =>
        Say(site, "S03-020", $"{site.Subject} {site.Member} reads node.{node}.content; a deliverable's if reads data (.value), not text.");

    /// <summary>S03-021: a deliverable's <c>if</c> reads a check.</summary>
    public static SourceRefusal DeliverableReadsCheck(ExpressionSite site, string check) =>
        Say(site, "S03-021", $"{site.Subject} {site.Member} reads check.{check}; a deliverable's existence can't depend on a check.");

    /// <summary>S03-022: a macro's <c>if</c> reads a parameter.</summary>
    public static SourceRefusal MacroReadsParam(ExpressionSite site, string name) =>
        Say(site, "S03-022", $"{site.Subject} {site.Member} reads param.{name}; a macro's if is shared, so it can't depend on one function's parameter.");

    /// <summary>S03-023: a literal that is none of an enum's values (joined by <c>, </c>).</summary>
    public static SourceRefusal NotAValue(ExpressionSite site, string path, string value, string values) =>
        Say(site, "S03-023", $"{site.Subject} compares {path} with \"{value}\", which is not one of its values ({values}).");

    /// <summary>S03-024: a field read through a union not every member of which declares it; <paramref name="path"/> is the read before <c>.value</c>.</summary>
    public static SourceRefusal NotEveryMember(ExpressionSite site, string path, string field, string format) =>
        Say(site, "S03-024", $"{site.Subject} reads {path}.value.{field}, which not every member of {format} declares.");

    /// <summary>S03-025: a condition of a known type that is no boolean.</summary>
    public static SourceRefusal NotABoolean(ExpressionSite site, string format) =>
        Say(site, "S03-025", $"{site.Subject} {site.Member} yields {format}; a condition yields a boolean.");

    /// <summary>S03-026: a computed node's result against its declared format.</summary>
    public static SourceRefusal YieldsNotDeclared(ExpressionSite site, string yields, string declares) =>
        Say(site, "S03-026", $"{site.Subject} yields {yields}; it declares {declares}.");

    /// <summary>S03-027: an <c>object</c> without a property of the format.</summary>
    public static SourceRefusal BuildsWithout(ExpressionSite site, string format, string field) =>
        Say(site, "S03-027", $"{site.Subject} builds {format} without {field}; object must give every property of the format.");

    /// <summary>S03-028: an <c>object</c> field the format does not declare.</summary>
    public static SourceRefusal BuildsUndeclared(ExpressionSite site, string format, string field) =>
        Say(site, "S03-028", $"{site.Subject} builds {format} with {field}, which the format doesn't declare.");

    /// <summary>S03-029: a literal operand written as an operation or an array (D9).</summary>
    public static SourceRefusal ComputedLiteral(ExpressionSite site, string operation, string operand) =>
        Say(site, "S03-029", $"{site.Subject} gives {operation} a computed {operand}; {operand} must be written as a literal.");

    /// <summary>S03-030: <c>count_pass</c>'s <c>min</c>, as its JSON text.</summary>
    public static SourceRefusal CountPassMin(ExpressionSite site, string n) =>
        Say(site, "S03-030", $"{site.Subject} gives count_pass a min of {n}; min is a whole number, 0 or more.");

    /// <summary>S03-031: a pattern outside RFC 9485's grammar; the pattern as written, not JSON-escaped.</summary>
    public static SourceRefusal PatternNotIRegexp(ExpressionSite site, string pattern, string reason) =>
        Say(site, "S03-031", $"{site.Subject} pattern \"{pattern}\" is not I-Regexp (RFC 9485): {reason}.");

    /// <summary>S03-032: a pattern of more than 200 code points.</summary>
    public static SourceRefusal PatternTooLong(ExpressionSite site, string pattern, int length) =>
        Say(site, "S03-032", $"{site.Subject} pattern \"{pattern}\" is {length.ToString(CultureInfo.InvariantCulture)} characters long; the most is 200.");

    /// <summary>S03-033: <c>has_word</c> with a blank word.</summary>
    public static SourceRefusal BlankWord(ExpressionSite site) =>
        Say(site, "S03-033", $"{site.Subject} gives has_word a blank word; has_word finds one word or phrase.");

    /// <summary>S03-034: an <c>if</c> with no else.</summary>
    public static SourceRefusal IfWithoutElse(ExpressionSite site) =>
        Say(site, "S03-034", $"{site.Subject} uses if without an else; write the else.");

    /// <summary>S03-035: Markdown given to <c>cat</c>.</summary>
    public static SourceRefusal CatsMarkdown(ExpressionSite site, string path, string format) =>
        Say(site, "S03-035", $"{site.Subject} cats {path} ({format}); convert Markdown with plain first.");

    /// <summary>
    /// S03-036: node <paramref name="id"/> declares <paramref name="member"/> as <paramref name="declared"/>, but its reads make
    /// it <paramref name="derived"/>'s value; the read is <see cref="LineageValue.Because"/>, or <c>none</c>. <paramref name="path"/> is the node's file.
    /// </summary>
    public static SourceRefusal FlagDisagrees(string id, string member, bool declared, LineageValue derived, string path) =>
        new("S03-036", $"Node '{id}' declares {member} {Json(declared)}, but its reads make it {Json(derived.Value)}: {derived.Because ?? "none"}.", path);

    /// <summary>S03-039 (a note, § 11.2): the words shown beside a node whose text is computed from model text.</summary>
    public static string ModelDerivedNote(string nodeId) => $"computed from model text: node.{nodeId}";

    /// <summary>S03-044: a prompt, part or condition that reads <paramref name="readPath"/>, which carries profile text.</summary>
    public static SourceRefusal ReadsProfileText(ExpressionSite site, string readPath) =>
        Say(site, "S03-044", $"{site.Subject} reads {readPath}, which carries profile text; prompts, parts and conditions don't read profile text.");

    private static string Json(bool value) => value ? "true" : "false";

    /// <summary>S03-037 (a warning, § 11.2): a condition that reads the run fact <paramref name="fact"/> itself.</summary>
    public static SourceRefusal RunDependentDecision(ExpressionSite site, string fact) =>
        Say(site, "S03-037", $"{site.Subject} {site.Member} reads run.{fact}; its decision can differ between runs with the same inputs.");

    /// <summary>
    /// S03-038 (a warning, § 11.2): computed node <paramref name="id"/> reads <paramref name="read"/>, a run fact
    /// (<c>run.id</c>) or a run-dependent value's path; <paramref name="path"/> is the node's file.
    /// </summary>
    public static SourceRefusal RunDependentValue(string id, string read, string path) =>
        new("S03-038", $"Computed node '{id}' reads {read}, which is run-dependent; its value can differ between runs with the same inputs.", path);

    /// <summary>S03-042: a counted repetition over 100.</summary>
    public static SourceRefusal PatternRepeats(ExpressionSite site, string pattern, int times) =>
        Say(site, "S03-042", $"{site.Subject} pattern \"{pattern}\" repeats {times.ToString(CultureInfo.InvariantCulture)} times; a counted repetition is at most 100.");

    /// <summary>S03-043: a pattern .NET does not compile (D12's reasons).</summary>
    public static SourceRefusal PatternDoesNotCompile(ExpressionSite site, string pattern, string reason) =>
        Say(site, "S03-043", $"{site.Subject} pattern \"{pattern}\" doesn't compile with the engine's options: {reason}.");

    /// <summary>A04-001: an operand of a type its operation does not take; <paramref name="expected"/> is one of the plan's words.</summary>
    public static SourceRefusal OperandNotTaken(ExpressionSite site, string operation, string operand, string format, string expected) =>
        Say(site, "A04-001", $"{site.Subject} gives {operation} {operand} ({format}); {operation} takes {expected}.");

    /// <summary>A04-002: <c>===</c> or <c>!==</c> across two types.</summary>
    public static SourceRefusal ComparesTwoTypes(ExpressionSite site, string a, string aFormat, string b, string bFormat) =>
        Say(site, "A04-002", $"{site.Subject} compares {a} ({aFormat}) with {b} ({bFormat}); = and ≠ compare two values of one type.");

    /// <summary>A04-003: two types where one is expected; <paramref name="where"/> is <c>the branches of an if</c> or <c>a list</c>.</summary>
    public static SourceRefusal Mixes(ExpressionSite site, string format, string other, string where) =>
        Say(site, "A04-003", $"{site.Subject} mixes {format} and {other} in {where}; they have one type.");

    /// <summary>A04-004: an object of <paramref name="members"/> members where a rule is expected (D7).</summary>
    public static SourceRefusal NotARule(ExpressionSite site, int members) =>
        Say(site, "A04-004", $"{site.Subject} writes an object with {members.ToString(CultureInfo.InvariantCulture)} members where a rule is expected; an operation is an object with one member, and object builds an object.");

    /// <summary>A04-005: <c>var</c> with what is not text (A02-001's words).</summary>
    public static SourceRefusal VarNotText(ExpressionSite site, string whatItIs) =>
        Say(site, "A04-005", $"{site.Subject} writes var with {whatItIs}; var takes one path, written as text.");

    /// <summary>A04-006: a path that is not non-empty segments joined by dots (D6).</summary>
    public static SourceRefusal NotAPath(ExpressionSite site, string path) =>
        Say(site, "A04-006", $"{site.Subject} reads '{path}', which is not a path; a path is a root, then names joined by dots.");

    /// <summary>A04-007: <c>object</c> with what is not an object.</summary>
    public static SourceRefusal ObjectOperand(ExpressionSite site, string whatItIs) =>
        Say(site, "A04-007", $"{site.Subject} writes object with {whatItIs}; object takes one object, from field names to rules.");

    /// <summary>A04-008: <c>object</c> where no format is expected (D27).</summary>
    public static SourceRefusal ObjectWithoutFormat(ExpressionSite site) =>
        Say(site, "A04-008", $"{site.Subject} builds an object where no format gives its fields; object builds the value of a computed node.");

    /// <summary>A04-009: an <c>object</c> field whose type does not fit its property.</summary>
    public static SourceRefusal BuildsFieldAs(ExpressionSite site, string format, string field, string fieldFormat, string declared) =>
        Say(site, "A04-009", $"{site.Subject} builds {format} with {field} as {fieldFormat}; the format declares it {declared}.");

    /// <summary>A04-010: a literal operand of the wrong JSON kind (D9).</summary>
    public static SourceRefusal LiteralNotText(ExpressionSite site, string operation, string operand, string whatItIs) =>
        Say(site, "A04-010", $"{site.Subject} gives {operation} a {operand} that is {whatItIs}; {operand} is written as text.");

    private static SourceRefusal Say(ExpressionSite site, string id, string sentence) => new(id, sentence, site.Path);

    /// <summary>The JSON pointer, inside <paramref name="rule"/>, of its first container past <see cref="SourceBounds.JsonDepth"/> levels (the root is level 1), as S01-041 names one.</summary>
    private static string DeepPointer(JsonNode? rule)
    {
        var pointer = new StringBuilder();
        var node = rule;
        for (var level = 1; level <= SourceBounds.JsonDepth; level++)
        {
            var next = node switch
            {
                JsonObject obj => obj.Where(p => Depth(p.Value) > SourceBounds.JsonDepth - level)
                    .Select(p => (Key: p.Key, p.Value)).FirstOrDefault(),
                JsonArray array => array.Select((v, i) => (Key: i.ToString(CultureInfo.InvariantCulture), Value: v))
                    .FirstOrDefault(p => Depth(p.Value) > SourceBounds.JsonDepth - level),
                _ => default,
            };
            if (next.Key is null)
                return pointer.Length == 0 ? "/" : pointer.ToString();
            pointer.Append('/').Append(next.Key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
            node = next.Value;
        }

        return pointer.ToString();
    }

    /// <summary>
    /// How many container levels <paramref name="node"/> holds, itself included, counted no further than one past
    /// <see cref="SourceBounds.JsonDepth"/>. #1507 (area 10): the converter measures its written rule with it.
    /// </summary>
    internal static int Depth(JsonNode? node, int bound = SourceBounds.JsonDepth + 1)
    {
        if (bound <= 0 || node is not JsonObject and not JsonArray)
            return 0;
        var deepest = 0;
        var children = node is JsonObject obj ? obj.Select(p => p.Value) : ((JsonArray)node);
        foreach (var child in children)
            deepest = Math.Max(deepest, Depth(child, bound - 1));
        return deepest + 1;
    }
}
