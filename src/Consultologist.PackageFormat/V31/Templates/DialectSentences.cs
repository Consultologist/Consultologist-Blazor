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
}
