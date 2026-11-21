using System.Globalization;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § Refusals): every publish sentence of the dialect, one method each, named
/// for the rule it says, as <see cref="ResolveSentences"/> does for area 05. Each is the row of <c>v31/sentences.json</c>
/// (<c>S05-NNN</c>) or the area's own <c>A07-NNN</c> with its placeholders filled in, character for character.
/// <c>subject</c> is the template's subject as the caller names it (<c>Prompt of node 'draft'</c>), the sentence's
/// <c>&lt;Subject&gt;</c>; <c>line</c> is the 1-based line in the body. No sentence carries a value of an input, a
/// model or a profile: only package text, names, paths and numbers.
/// </summary>
public static class DialectSentences
{
    /// <summary>S05-001 (R-D1): a refused statement or operator; <paramref name="construct"/> is the keyword or operator as written (<c>capture</c>, <c>=</c>, <c>+=</c>, <c>+</c>).</summary>
    public static SourceRefusal UsesConstruct(string subject, string construct, int line, string path) =>
        new("S05-001", $"{subject} uses {construct} at line {Line(line)}; templates present text and may not use assignment, capture, func, include, import, with, wrap, while, break, continue, case, tablerow or arithmetic.", path);

    /// <summary>S05-002 (R-D2): a built-in outside the seven, or a built-in named outside a pipe; <paramref name="builtIn"/> as written (<c>date.now</c>).</summary>
    public static SourceRefusal CallsBuiltIn(string subject, string builtIn, int line, string path) =>
        new("S05-002", $"{subject} calls {builtIn} at line {Line(line)}; the allowed built-ins are string.upcase, string.downcase, string.capitalize, string.strip, array.join, array.size and date.to_string.", path);

    /// <summary>S05-003 (R-D3): a pipe to <c>truncate</c>.</summary>
    public static SourceRefusal CallsTruncate(string subject, int line, string path) =>
        new("S05-003", $"{subject} calls truncate at line {Line(line)}; truncate cuts clinical text.", path);

    /// <summary>S05-004 (R-D4): a pipe to <c>object.default</c>.</summary>
    public static SourceRefusal CallsObjectDefault(string subject, int line, string path) =>
        new("S05-004", $"{subject} calls object.default at line {Line(line)}; choosing text is a decision: put it in a header condition.", path);

    /// <summary>S05-005 (R-D5): a pipe to <c>date.to_string</c> with no string literal for its pattern.</summary>
    public static SourceRefusal DateWithoutPattern(string subject, int line, string path) =>
        new("S05-005", $"{subject} calls date.to_string at line {Line(line)} without a pattern; write the pattern, for example \"%d %B %Y\".", path);

    /// <summary>S05-006 (R-D6): a <c>for</c> over a range or a literal; <paramref name="expression"/> as written (<c>1..3</c>).</summary>
    public static SourceRefusal ForOverNotAList(string subject, string expression, int line, string path) =>
        new("S05-006", $"{subject} runs a for over {expression} at line {Line(line)}; a for runs over a list.", path);

    /// <summary>S05-013 (R-L2): <c>if</c> and <c>for</c> nested <paramref name="depth"/> deep, past the limit of 15, first reached at <paramref name="line"/>.</summary>
    public static SourceRefusal NestsTooDeep(string subject, int depth, int line, string path) =>
        new("S05-013", $"{subject} nests if and for {depth.ToString(CultureInfo.InvariantCulture)} deep at line {Line(line)}; the limit is {DialectRules.NestingLimit.ToString(CultureInfo.InvariantCulture)}.", path);

    /// <summary>S05-019 (R-W5): <c>-</c> on a tag (part 05 § 2.6).</summary>
    public static SourceRefusal UsesDash(string subject, int line, string path) =>
        new("S05-019", $"{subject} uses - at line {Line(line)}; it removes blank lines between paragraphs. Use ~.", path);

    /// <summary>S05-030 (R-N4): the call form <c>renderer.&lt;id&gt;(value)</c>; <paramref name="value"/> is the argument as written.</summary>
    public static SourceRefusal CallsRenderer(string subject, string id, string value, int line, string path) =>
        new("S05-030", $"{subject} calls renderer.{id} at line {Line(line)}; pipe into it: {value} | renderer.{id}.", path);

    /// <summary>S05-063 (R-D11): an indexer; <paramref name="target"/> and <paramref name="index"/> as written (<c>node.plan.value.sections</c>, <c>2</c>).</summary>
    public static SourceRefusal ReadsPosition(string subject, string target, string index, int line, string path) =>
        new("S05-063", $"{subject} reads {target}[{index}] at line {Line(line)}, a position in a list; lists are read whole, with a for, or by identity through .items.", path);

    /// <summary>A07-004: a <c>for</c> option (<c>limit:</c>, <c>offset:</c>, <c>reversed</c>), a <c>for</c>'s <c>else</c>, or a loop variable outside the five (<c>for.rindex</c>).</summary>
    public static SourceRefusal ForForm(string subject, string form, int line, string path) =>
        new("A07-004", $"{subject} uses {form} at line {Line(line)}; a for runs over a whole list in order and reads only for.first, for.last, for.index, for.even and for.odd.", path);

    /// <summary>A07-005: a body Scriban cannot parse, or one whose tag is never closed; never Scriban's own message.</summary>
    public static SourceRefusal CannotBeRead(string subject, int line, string path) =>
        new("A07-005", $"{subject} can't be read as a template at line {Line(line)}: close every tag and every if and for.", path);

    /// <summary>S05-007 (R-D7): a body <c>if</c> testing something other than a loop position, a state or whether a check passed; <paramref name="expression"/> is the test as written.</summary>
    public static SourceRefusal TestsInBodyIf(string subject, string expression, int line, string path) =>
        new("S05-007", $"{subject} tests {expression} in a body if at line {Line(line)}; a body if tests only loop position, state and whether a check passed. Decide it in a header: a part, a snippet's if or a macro's if.", path);

    /// <summary>
    /// S05-009 (R-D8): an <c>if</c> chain with no <c>else</c> that tests <paramref name="expression"/>'s state and misses
    /// some; <paramref name="handled"/> and <paramref name="missing"/> are states in the closed set's order.
    /// </summary>
    public static SourceRefusal BranchesWithoutEveryState(string subject, string expression, IReadOnlyList<string> handled, IReadOnlyList<string> missing, string path) =>
        new("S05-009", $"{subject} branches on {expression} and handles {Listed(handled)}, but not {Listed(missing)}: add it or an else.", path);

    /// <summary>S05-010 (R-D9): a state compared with <paramref name="value"/> (without its quotes), which is not in the closed set.</summary>
    public static SourceRefusal ComparesStateWith(string subject, string expression, string value, int line, string path) =>
        new("S05-010", $"{subject} compares {expression} with \"{value}\" at line {Line(line)}; a state is completed, skipped or not_run.", path);

    /// <summary>
    /// S05-011 (R-D10): a read of a member that is not there (strict variables, part 05 § 2.3); <paramref name="what"/>
    /// is the format read (its type string) or the root, or the site's words for a root no template reads.
    /// </summary>
    public static SourceRefusal ReadsWhatIsNotThere(string subject, string readPath, int line, string what, string path) =>
        new("S05-011", $"{subject} reads {readPath} at line {Line(line)}, which {what} doesn't have.", path);

    /// <summary>S05-015 (R-W1): <c>~}}</c> on a placement of <paramref name="unit"/> (as written: <c>snippet.tone.content</c>, <c>part.x</c>, <c>macro.y</c>).</summary>
    public static SourceRefusal PlacesWithTildeRight(string subject, string unit, int line, string path) =>
        new("S05-015", $"{subject} places {unit} with ~}}}} at line {Line(line)}; it removes the line's newline, joining two lines. Use {{{{~ {unit} }}}} alone on a line.", path);

    /// <summary>S05-016 (R-W2): <c>-</c> on a placement of <paramref name="unit"/>.</summary>
    public static SourceRefusal PlacesWithDash(string subject, string unit, int line, string path) =>
        new("S05-016", $"{subject} places {unit} with - at line {Line(line)}; it removes blank lines between paragraphs.", path);

    /// <summary>S05-017 (R-W3): <c>{{~ unit }}</c> with text before it on its line.</summary>
    public static SourceRefusal PlacesBlockInsideALine(string subject, string unit, int line, string path) =>
        new("S05-017", $"{subject} places {{{{~ {unit} }}}} inside a line at line {Line(line)}; it removes the space before it. Use {{{{ {unit} }}}}.", path);

    /// <summary>S05-018 (R-W4): <c>{{~ unit }}</c> with text after it on its line.</summary>
    public static SourceRefusal TextAfterBlock(string subject, string unit, int line, string path) =>
        new("S05-018", $"{subject} has text after {{{{~ {unit} }}}} at line {Line(line)}; a block placement stands alone on its line.", path);

    /// <summary>
    /// S05-020 (R-R1): a root the site never reads (part 05 § 3.1); <paramref name="root"/> as the table writes it
    /// (<c>deliverable.*</c>, <c>item</c>), <paramref name="site"/> the table's words for the site (<c>prompt</c>) and
    /// <paramref name="allowed"/> the table's list of what it reads.
    /// </summary>
    public static SourceRefusal ReadsRoot(string subject, string root, int line, string site, string allowed, string path) =>
        new("S05-020", $"{subject} reads {root} at line {Line(line)}; {site} reads {allowed}.", path);

    /// <summary>S05-021 (R-R2): <c>profile.&lt;field&gt;</c> in a prompt, a part or a <c>rendered</c> node of node <paramref name="id"/>.</summary>
    public static SourceRefusal ReadsProfileInPrompt(string id, string field, string path) =>
        new("S05-021", $"Node '{id}' reads profile.{field}; the clinician's profile is never sent to a model. Read it in a deliverable or a macro body.", path);

    /// <summary>S05-024 (R-R5): an output reading <c>run.&lt;fact&gt;</c> and also <paramref name="other"/>.</summary>
    public static SourceRefusal ReadsRunWithMore(string subject, string other, string fact, int line, string path) =>
        new("S05-024", $"{subject} reads {other} and run.{fact} in one output at line {Line(line)}; an output that reads a run fact reads nothing else.", path);

    /// <summary>S05-025 (R-R5, for a run-dependent value): an output reading the run-dependent <paramref name="value"/> and also <paramref name="other"/>; the rest is S05-024's.</summary>
    public static SourceRefusal ReadsRunDependentWithMore(string subject, string other, string value, int line, string path) =>
        new("S05-025", $"{subject} reads {other} and the run-dependent {value} in one output at line {Line(line)}; an output that reads a run fact reads nothing else.", path);

    /// <summary>S05-037 (R-S3): a bare <c>snippet.&lt;id&gt;</c>; <paramref name="id"/> as written (<c>tone</c>, <c>standards.hpi</c>).</summary>
    public static SourceRefusal ReadsBareSnippet(string subject, string id, int line, string path) =>
        new("S05-037", $"{subject} reads snippet.{id} at line {Line(line)}; read its text as snippet.{id}.content.", path);

    /// <summary>S06-028 (part 06 R-F1): <c>profile.&lt;field&gt;</c> outside part 06 § 10.1's closed list.</summary>
    public static SourceRefusal ReadsNoProfileField(string subject, string field, string path) =>
        new("S06-028", $"{subject} reads profile.{field}, which isn't a profile field; the fields are name, credentials, organisation, signature and closing.", path);

    /// <summary>A07-007 (D13): an object or a list written directly; <paramref name="what"/> is <c>an object</c> or <c>a list</c>.</summary>
    public static SourceRefusal WritesAnObjectOrList(string subject, string readPath, int line, string what, string path) =>
        new("A07-007", $"{subject} writes {readPath} at line {Line(line)}, which is {what}; read a member, loop over it or pipe it into a renderer.", path);

    /// <summary>A07-008: a bare <c>node.x</c>, <c>input.x</c>, <c>check.x</c>, <c>param.x</c> (or <c>item</c>); <paramref name="member"/> is <c>.value</c>, <c>.content</c> or <c>.title</c>.</summary>
    public static SourceRefusal ReadsBare(string subject, string readPath, int line, string member, string path) =>
        new("A07-008", $"{subject} reads {readPath} at line {Line(line)}; read {member} of it.", path);

    /// <summary>A07-009: a placement of <paramref name="unit"/> inside <paramref name="where"/> (<c>an if</c> or <c>a for</c>).</summary>
    public static SourceRefusal PlacesInsideAnIfOrFor(string subject, string unit, int line, string where, string path) =>
        new("A07-009", $"{subject} places {unit} at line {Line(line)} inside {where}; a part, a snippet or a macro is placed at the top level of its template, and its own if decides it.", path);

    /// <summary>S05-060 (W-1), a warning: a run spot reading <paramref name="reads"/> (<c>run.date</c>, or a run-dependent value as written).</summary>
    public static SourceRefusal ReadsRunFact(string subject, string reads, int line, string path) =>
        new("S05-060", $"{subject} reads {reads} at line {Line(line)}. Its main hash differs between runs; runs are compared on the run-neutral hash.", path);

    /// <summary>The sentence of <paramref name="fault"/>, said of <paramref name="subject"/> in the file <paramref name="path"/>.</summary>
    /// <exception cref="ArgumentException">The fault's id is no sentence of this class (a fault this library did not make).</exception>
    public static SourceRefusal Of(DialectFault fault, string subject, string path)
    {
        ArgumentNullException.ThrowIfNull(fault);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(path);

        string Fill(int i) => i < fault.Fill.Count ? fault.Fill[i] : string.Empty;

        return fault.Id switch
        {
            "S05-001" => UsesConstruct(subject, Fill(0), fault.Line, path),
            "S05-002" => CallsBuiltIn(subject, Fill(0), fault.Line, path),
            "S05-003" => CallsTruncate(subject, fault.Line, path),
            "S05-004" => CallsObjectDefault(subject, fault.Line, path),
            "S05-005" => DateWithoutPattern(subject, fault.Line, path),
            "S05-006" => ForOverNotAList(subject, Fill(0), fault.Line, path),
            "S05-013" => NestsTooDeep(subject, int.TryParse(Fill(0), NumberStyles.None, CultureInfo.InvariantCulture, out var depth) ? depth : DialectRules.NestingLimit + 1, fault.Line, path),
            "S05-019" => UsesDash(subject, fault.Line, path),
            "S05-030" => CallsRenderer(subject, Fill(0), Fill(1), fault.Line, path),
            "S05-063" => ReadsPosition(subject, Fill(0), Fill(1), fault.Line, path),
            "A07-004" => ForForm(subject, Fill(0), fault.Line, path),
            "A07-005" => CannotBeRead(subject, fault.Line, path),
            _ => throw new ArgumentException($"{fault.Id} is no dialect sentence.", nameof(fault)),
        };
    }

    private static string Line(int line) => line.ToString(CultureInfo.InvariantCulture);

    /// <summary><c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
    private static string Listed(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
