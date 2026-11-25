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

    /// <summary>
    /// A08-006 (#1505 ruling 6, R14): reading the body failed in a way no text explains, a fault of the engine and not of the
    /// package (<see cref="TemplateSyntax.Parse(string)"/>); no package produces it. #1508 (R8 item 4): its audience is the
    /// operator, not the author: the caller logs <see cref="SourceRefusal.EngineFault"/> (publish does, Task 16).
    /// </summary>
    public static SourceRefusal EngineFailedReading(string subject, string path) =>
        new("A08-006", $"{subject} couldn't be checked: the engine failed reading its template. This is a fault in the engine, not in the package; report it.", path);

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

    /// <summary>
    /// S05-026 (R-N1): a value piped into renderer <paramref name="id"/> that is not of its <c>input</c>;
    /// <paramref name="source"/> is what is piped as written (<c>node.plan.value</c>, or with the built-in steps before
    /// the renderer: <c>node.a.value.items | array.size</c>), <paramref name="format"/> and <paramref name="input"/> the type strings.
    /// </summary>
    public static SourceRefusal PipesWrongFormat(string subject, string source, string format, string id, string input, string path) =>
        new("S05-026", $"{subject} pipes {source} ({format}) into renderer.{id}, which renders {input}.", path);

    /// <summary>S05-028 (R-N2): a renderer whose <c>format</c> is not one of the three text formats; <paramref name="format"/> its type string.</summary>
    public static SourceRefusal RendererFormatNotText(string id, string format, string path) =>
        new("S05-028", $"Renderer '{id}' declares format {format}; a renderer returns plaintext, markdown_block or markdown_inline.", path);

    /// <summary>S05-029 (R-N3): a renderer's body reading <paramref name="root"/> (as part 05 § 3.1's table writes it: <c>node.*</c>, <c>item</c>), not its <c>value</c>.</summary>
    public static SourceRefusal RendererReadsRoot(string id, string root, string path) =>
        new("S05-029", $"Renderer '{id}' reads {root}; a renderer reads only the value piped into it.", path);

    /// <summary>S05-031 (R-N5): a pipe into renderer <paramref name="id"/>, which is neither the package's, the function's own nor a built-in.</summary>
    public static SourceRefusal PipesUndefinedRenderer(string subject, string id, string path) =>
        new("S05-031", $"{subject} pipes into renderer.{id}, which this package doesn't define.", path);

    /// <summary>S05-033 (R-N7): renderer <paramref name="id"/>'s body piping into renderer <paramref name="other"/>.</summary>
    public static SourceRefusal RendererPipesIntoRenderer(string id, string other, int line, string path) =>
        new("S05-033", $"Renderer '{id}' pipes into renderer.{other} at line {Line(line)}; chain renderers where they're used, not inside a renderer.", path);

    /// <summary>S05-034: a chain step whose renderer <paramref name="id"/> returns <paramref name="format"/>, not the <c>input</c> of the next renderer <paramref name="next"/>.</summary>
    public static SourceRefusal ChainWrongFormat(string subject, string id, string format, string next, string input, string path) =>
        new("S05-034", $"{subject} pipes renderer.{id} ({format}) into renderer.{next}, which renders {input}.", path);

    /// <summary>S05-061 (W-2), a warning: a <c>markdown_block</c> renderer <paramref name="id"/> piped in a tag with text before or after it on its line.</summary>
    public static SourceRefusal BlockRendererInsideALine(string subject, string id, int line, string path) =>
        new("S05-061", $"{subject} pipes renderer.{id}, a markdown_block, inside a line at line {Line(line)}.", path);

    /// <summary>S05-037 (R-S3): a bare <c>snippet.&lt;id&gt;</c>; <paramref name="id"/> as written (<c>tone</c>, <c>standards.hpi</c>).</summary>
    public static SourceRefusal ReadsBareSnippet(string subject, string id, int line, string path) =>
        new("S05-037", $"{subject} reads snippet.{id} at line {Line(line)}; read its text as snippet.{id}.content.", path);

    /// <summary>
    /// S05-022 (R-R3): snippet <paramref name="id"/> (<c>tone</c>, a folder item <c>standards/hpi</c>) reads
    /// <paramref name="readPath"/> (as written in its body), which <paramref name="subject"/>'s place does not give; the
    /// snippet is placed there at <paramref name="line"/> of the placer's text.
    /// </summary>
    public static SourceRefusal SnippetReadsUnavailable(string id, string readPath, string subject, int line, string path) =>
        new("S05-022", $"Snippet '{id}' reads {readPath}, which isn't available where {subject} places it at line {Line(line)}.", path);

    /// <summary>S05-023 (R-R4): <paramref name="kind"/> <paramref name="id"/> reaching itself; <paramref name="chain"/> the ids from it back to it.</summary>
    public static SourceRefusal ReachesItself(string kind, string id, IReadOnlyList<string> chain, string path) =>
        new("S05-023", $"{kind} '{id}' reaches itself: {string.Join(" → ", chain)}.", path);

    /// <summary>S05-035 (R-S1): a read of <c>snippet.&lt;id&gt;</c> the package (or the function) does not define; <paramref name="id"/> as written.</summary>
    public static SourceRefusal PlacesUndefinedSnippet(string subject, string id, string path) =>
        new("S05-035", $"{subject} places snippet.{id}, which this package doesn't define.", path);

    /// <summary>
    /// S05-036 (R-S2): the header of item <paramref name="id"/> of snippet folder <paramref name="folder"/> is not of the
    /// folder's <paramref name="format"/>; <paramref name="error"/> names the first fault's keyword and place, never a value.
    /// </summary>
    public static SourceRefusal SnippetHeaderNotFormat(string folder, string id, string format, string error, string path) =>
        new("S05-036", $"Snippet '{folder}/{id}' has a header that doesn't match format {format}: {error}.", path);

    /// <summary>S05-038 (R-P1): node <paramref name="id"/>'s body places part <paramref name="name"/>, which its <c>parts</c> does not declare.</summary>
    public static SourceRefusal PlacesUndeclaredPart(string id, string name, string path) =>
        new("S05-038", $"Node '{id}' places part.{name}, which its header doesn't declare.", path);

    /// <summary>S05-039 (R-P2): node <paramref name="id"/> declares part <paramref name="name"/> and its body never places it.</summary>
    public static SourceRefusal PartNeverPlaced(string id, string name, string path) =>
        new("S05-039", $"Node '{id}' declares part '{name}' but never places it.", path);

    /// <summary>S05-040 (R-P3): node <paramref name="id"/>'s body places part <paramref name="name"/> <paramref name="count"/> times.</summary>
    public static SourceRefusal PartPlacedTwice(string id, string name, int count, string path) =>
        new("S05-040", $"Node '{id}' places part.{name} {count.ToString(CultureInfo.InvariantCulture)} times; place a part once.", path);

    /// <summary>S05-041 (R-P4): node <paramref name="id"/>, not an agent, declares <c>parts</c>.</summary>
    public static SourceRefusal DeclaresPartsNotAgent(string id, string path) =>
        new("S05-041", $"Node '{id}' declares parts, and only agent nodes have parts.", path);

    /// <summary>
    /// A07-001 (R-S4, the guard): snippet <paramref name="id"/>, placed in <paramref name="where"/> (<c>a part's text</c>
    /// or <c>a macro's instruction</c>) by <paramref name="subject"/>, places <paramref name="unit"/> (<c>macro.m</c>,
    /// <c>part.p</c>), itself or through a snippet it places.
    /// </summary>
    public static SourceRefusal SnippetPlacesPartOrMacro(string id, string unit, string subject, string where, string path) =>
        new("A07-001", $"Snippet '{id}' places {unit}, and {subject} places it in {where}; a snippet placed there doesn't place parts or macros.", path);

    /// <summary>A07-002 (R-P5): part <paramref name="name"/> of node <paramref name="id"/> uses <paramref name="what"/> (<c>if</c>, or the <c>part.&lt;id&gt;</c> it places).</summary>
    public static SourceRefusal PartOnlyPresents(string name, string id, string what, string path) =>
        new("A07-002", $"Part '{name}' of node '{id}' uses {what}; a part's text only presents.", path);

    /// <summary>A07-010 (part 05 § 6.1): a single snippet whose <c>format</c> is not one of the three text formats; <paramref name="format"/> its type string.</summary>
    public static SourceRefusal SnippetFormatNotText(string id, string format, string path) =>
        new("A07-010", $"Snippet '{id}' declares format {format}; a snippet's text is plaintext, markdown_block or markdown_inline.", path);

    /// <summary>A07-011: a template whose placed units, counted with what its parts and snippets place in turn, pass <paramref name="limit"/>.</summary>
    public static SourceRefusal PlacesTooManyUnits(string subject, int limit, string path) =>
        new("A07-011", $"{subject} places more than {Count(limit)} parts, snippets and macros, counting what its parts and snippets place; place fewer.", path);

    /// <summary>
    /// A07-012 (part 05 § 6.2): the body of folder item <paramref name="id"/> (<c>&lt;folder&gt;/&lt;item&gt;</c>), which
    /// <paramref name="subject"/> reads as <paramref name="read"/> (<c>item.content</c>) at <paramref name="line"/>,
    /// <paramref name="does"/> what such a body may not: <c>places &lt;unit&gt;</c>, <c>pipes into renderer.&lt;id&gt;</c> or
    /// <c>reads &lt;path&gt;</c> (a run spot).
    /// </summary>
    public static SourceRefusal FolderItemDoes(string id, string does, string subject, string read, int line, string path) =>
        new("A07-012", $"Snippet '{id}' {does}, and {subject} reads it as {read} at line {Line(line)}; a folder item's body read in a loop "
            + "places no part, macro or snippet, pipes into no renderer and reads no run.* or run-dependent value.", path);

    /// <summary>
    /// A07-013 (part 05 § 6.2): the body of folder item <paramref name="id"/> (<c>&lt;folder&gt;/&lt;item&gt;</c>) holds a tag
    /// (or a raw block) first at <paramref name="line"/>, and <paramref name="subject"/> carries it out of its loop by
    /// <paramref name="route"/> (<c>passes it to function '&lt;f&gt;' as param.&lt;p&gt;</c>, <c>reads it in its expression</c>),
    /// where nothing renders it.
    /// </summary>
    public static SourceRefusal FolderItemCarried(string id, int line, string subject, string route, string path) =>
        new("A07-013", $"Snippet '{id}' holds a tag at line {Line(line)}, and {subject} {route}, where it isn't rendered; "
            + "keep a folder item's body plain text, or read it as item.content in its loop.", path);

    /// <summary>
    /// A07-014 (#1504 ruling 4, R6; part 07 § 3.1 <c>requires.agents</c>): the agent nodes <paramref name="first"/> and
    /// <paramref name="second"/> are bound to agent <paramref name="agent"/> at two versions, <paramref name="firstVersion"/>
    /// and <paramref name="secondVersion"/>; a package's <c>requires</c> names one version of each agent.
    /// </summary>
    public static SourceRefusal TwoVersionsOfOneAgent(string first, string second, string agent, int firstVersion, int secondVersion, string path) =>
        new("A07-014", $"Nodes '{first}' and '{second}' bind agent '{agent}' at versions {Line(firstVersion)} and {Line(secondVersion)}; "
            + "a package binds one version of an agent.", path);

    /// <summary>S05-042 (R-M1): macro <paramref name="id"/>'s body places <c>macro.<paramref name="other"/></c>, itself or through a snippet it places.</summary>
    public static SourceRefusal MacroBodyPlacesMacro(string id, string other, string path) =>
        new("S05-042", $"Macro '{id}' places macro.{other} in its body; {BodyIsOutput}", path);

    /// <summary>S05-043 (R-M1, for <c>part.*</c>): macro <paramref name="id"/>'s body places <c>part.<paramref name="name"/></c>; the rest is S05-042's.</summary>
    public static SourceRefusal MacroBodyPlacesPart(string id, string name, string path) =>
        new("S05-043", $"Macro '{id}' places part.{name} in its body; {BodyIsOutput}", path);

    /// <summary>S05-044 (R-M3): macro <paramref name="id"/>'s instruction holds its own marker <paramref name="count"/> times, not once.</summary>
    public static SourceRefusal MarkerCount(string id, int count, string path) =>
        new("S05-044", $"Macro '{id}' has {count.ToString(CultureInfo.InvariantCulture)} markers in its instruction; it must hold {{{{~ macro.{id}.content }}}} or {{{{ macro.{id}.content }}}} exactly once.", path);

    /// <summary>S05-045 (R-M4): macro <paramref name="id"/>'s instruction holds the marker of macro <paramref name="other"/>.</summary>
    public static SourceRefusal MarkerOfAnother(string id, string other, string path) =>
        new("S05-045", $"Macro '{id}' has the marker of macro '{other}' in its instruction.", path);

    /// <summary>
    /// S05-046 (R-M5): node <paramref name="id"/> (of the text format <paramref name="nodeFormat"/>) places macro
    /// <paramref name="macro"/>, whose <paramref name="format"/> it does not take; <paramref name="allowed"/> lists what it takes.
    /// </summary>
    public static SourceRefusal MacroFormatNotTaken(string id, string macro, string format, string nodeFormat, string allowed, string path) =>
        new("S05-046", $"Node '{id}' places macro.{macro}, whose format is {format}; an {nodeFormat} node takes {allowed}.", path);

    /// <summary>S05-047 (R-M6): <paramref name="subject"/>, no agent's prompt or part, places <c>macro.<paramref name="id"/></c>, itself or through a snippet it places.</summary>
    public static SourceRefusal OnlyAgentsPlaceMacros(string subject, string id, string path) =>
        new("S05-047", $"{subject} places macro.{id}; only agent nodes place macros, in their prompt or a part's text.", path);

    /// <summary>S05-048 (R-M7): <paramref name="subject"/> places <c>macro.<paramref name="id"/></c> more than once, counting its parts' text and the snippets it places.</summary>
    public static SourceRefusal MacroPlacedTwice(string subject, string id, string path) =>
        new("S05-048", $"{subject} places macro.{id} twice, counting its parts' text; place a macro once in a template.", path);

    /// <summary>S05-049 (R-M11): node <paramref name="id"/> places macro <paramref name="macro"/> and returns <paramref name="format"/> (its type string), no text format.</summary>
    public static SourceRefusal MacroInNonTextNode(string id, string macro, string format, string path) =>
        new("S05-049", $"Node '{id}' places macro.{macro}, and returns {format}; macros are placed only in agent nodes returning a text format.", path);

    /// <summary>S05-050 (R-M8): macro <paramref name="id"/> of <paramref name="format"/>, whose body <paramref name="reason"/> (<c>writes Markdown at line 2</c>).</summary>
    public static SourceRefusal MacroBodyNotFormat(string id, string format, string reason, string path) =>
        new("S05-050", $"Macro '{id}' has format {format}, and its body {reason}.", path);

    /// <summary>S05-051 (R-M9): macro <paramref name="id"/>'s body reads a marker (<c>macro.&lt;…&gt;.content</c>).</summary>
    public static SourceRefusal MarkerInBody(string id, string path) =>
        new("S05-051", $"Macro '{id}' has a marker in its body; a body is the text that replaces the marker.", path);

    /// <summary>
    /// S05-064 (R-R6): <paramref name="subject"/> (a prompt, a part's text, a macro's instruction) reads
    /// <paramref name="readPath"/>, the value of a node whose text carries profile text through macro <paramref name="macro"/>.
    /// </summary>
    public static SourceRefusal ReadsProfileCarrying(string subject, string readPath, int line, string macro, string path) =>
        new("S05-064", $"{subject} reads {readPath} at line {Line(line)}, which carries profile text through macro '{macro}'; a prompt or a part doesn't read profile text. Read it in a deliverable.", path);

    /// <summary>A07-003 (R-M2): macro <paramref name="id"/>'s instruction places <paramref name="unit"/> (<c>macro.other</c>, <c>part.p</c>, as written).</summary>
    public static SourceRefusal InstructionPlaces(string id, string unit, string path) =>
        new("A07-003", $"Macro '{id}' places {unit} in its instruction; combine instructions by placing each macro in the prompt.", path);

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
            "A08-006" => EngineFailedReading(subject, path) with { EngineFault = Fill(0) },
            _ => throw new ArgumentException($"{fault.Id} is no dialect sentence.", nameof(fault)),
        };
    }

    /// <summary>R-M1's reason, which S05-042 and S05-043 share.</summary>
    private const string BodyIsOutput = "a macro body is output text, written after the model replies. Put shared text in a snippet and place it in both.";

    private static string Line(int line) => line.ToString(CultureInfo.InvariantCulture);

    /// <summary>A count as the sentences write it, with a thousands comma (<c>1,000</c>).</summary>
    private static string Count(int count) => count.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary><c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
    private static string Listed(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
