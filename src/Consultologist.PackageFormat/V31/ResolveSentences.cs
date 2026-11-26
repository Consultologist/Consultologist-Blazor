namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): every sentence the resolver says, one method each, named after what it says, as
/// <see cref="ExpressionSentences"/> does for area 04. Each is the row of <c>v31/sentences.json</c> (part 04 § 10.1,
/// part 07 § 10) or the area's own <c>A05-NNN</c> (#1503: or area 06's <c>A06-NNN</c>) with its placeholders filled in,
/// character for character.
/// <c>&lt;site&gt;</c> is the subject of the place that reads (<see cref="ExpressionSite.Subject"/>, or D13's
/// <c>Loop 'x'</c>, <c>Case 'x'</c>, <c>Call 'x'</c>), and every refusal names the file it is about. No sentence
/// carries a value from an input or a model: only ids, keys, references, member names and type strings.
/// </summary>
internal static class ResolveSentences
{
    /// <summary>A05-001 (D13): <paramref name="member"/> (<c>in</c>, <c>case</c>, <c>with &lt;param&gt;</c>) of <paramref name="kind"/> <paramref name="id"/> is text that is not a reference.</summary>
    public static SourceRefusal NotAReference(string kind, string id, string member, string text, string path) =>
        new("A05-001", $"{kind} '{id}' {member} '{text}' is not a reference; a reference is a root, then names joined by dots.", path);

    /// <summary>S03-014 (D13): a reference string whose head names nothing at <paramref name="site"/>, or whose member or field does not read.</summary>
    public static SourceRefusal DoesNotExist(string site, string reference, string path) =>
        new("S03-014", $"{site} reads {reference}, which doesn't exist here.", path);

    /// <summary>S04-017: <paramref name="member"/> (<c>in</c>, <c>case</c>, <c>with &lt;param&gt;</c>) written as an object or a list, where a reference is.</summary>
    public static SourceRefusal IsAnExpression(string kind, string id, string member, string path) =>
        new("S04-017", $"{kind} '{id}' {member} is an expression; it names one readable thing: compute the value in a computed node and name that.", path);

    /// <summary>S04-025: the loop <paramref name="id"/> iterates <paramref name="reference"/>, of the type string <paramref name="format"/>, which is no list.</summary>
    public static SourceRefusal IteratesNoList(string id, string reference, string format, string path) =>
        new("S04-025", $"Loop '{id}' iterates {reference}, which is {format}; only a list can be iterated.", path);

    /// <summary>S04-026: the loop <paramref name="id"/> iterates <c>item.value.&lt;field&gt;</c> with no loop around it.</summary>
    public static SourceRefusal IteratesItemOutsideLoop(string id, string field, string path) =>
        new("S04-026", $"Loop '{id}' iterates item.value.{field}, but it runs inside no loop; item.value names the enclosing loop's item.", path);

    /// <summary>S04-027: the loop <paramref name="id"/> iterates <c>node.&lt;node&gt;.value.&lt;field&gt;</c>, which runs inside the loop <paramref name="loop"/> it is not in.</summary>
    public static SourceRefusal IteratesNodeInsideLoop(string id, string node, string field, string loop, string path) =>
        new("S04-027", $"Loop '{id}' iterates node.{node}.value.{field}; {node} runs inside loop '{loop}' — a loop iterates a node outside it, or in an enclosing loop's body.", path);

    /// <summary>S04-028: <c>itemLabel</c> is no property of the element of <paramref name="reference"/>; <paramref name="fields"/> are its properties in written order (<c>none</c> for an element that is no object).</summary>
    public static SourceRefusal ItemLabelNotAField(string id, string field, string reference, string fields, string path) =>
        new("S04-028", $"Loop '{id}' itemLabel '{field}' is not a field of the elements of {reference} (fields: {fields}).", path);

    /// <summary>S04-029: <c>itemLabel</c> names a property the element does not require, or that may be null.</summary>
    public static SourceRefusal ItemLabelMayBeMissing(string id, string field, string path) =>
        new("S04-029", $"Loop '{id}' itemLabel '{field}' may be missing or null; itemLabel names a field every element has as a string.", path);

    /// <summary>S04-030: <c>itemLabel</c> names a property of the type string <paramref name="format"/>, which is no string.</summary>
    public static SourceRefusal ItemLabelNotAString(string id, string field, string format, string path) =>
        new("S04-030", $"Loop '{id}' itemLabel '{field}' is {format}; itemLabel names a string field.", path);

    /// <summary>S04-035: the body node <paramref name="id"/> of the while <paramref name="loop"/> is not its <c>ret</c> and nothing in the while reads it.</summary>
    public static SourceRefusal WhileNodeUnused(string id, string loop, string path) =>
        new("S04-035", $"Node '{id}' in while '{loop}' is neither returned nor read; name it with ret, read it from a body node or from the check.", path);

    /// <summary>
    /// A06-002 (#1503; #1507, item 4): the while <paramref name="loop"/> declares <c>checkFirst</c> true, and its
    /// <c>while</c> rule reads the node <paramref name="node"/> of its own body, which has no value before the first pass.
    /// </summary>
    public static SourceRefusal CheckFirstReadsBody(string loop, string node, string path) =>
        new("A06-002", $"While '{loop}' declares checkFirst true, but its check reads node '{node}' of its own body, which has no "
            + "value before the first pass: declare \"checkFirst\": false, or read something that runs before the while.", path);

    /// <summary>S04-036: <c>pass.number</c> read where no <c>while</c> encloses the reader.</summary>
    public static SourceRefusal PassOutsideWhile(string site, string path) =>
        new("S04-036", $"{site} reads pass.number, but it runs inside no while; pass is a while's pass.", path);

    /// <summary>S04-037: <c>pass.parent</c> read in an outermost <c>while</c>.</summary>
    public static SourceRefusal PassParentOutsideWhile(string site, string path) =>
        new("S04-037", $"{site} reads pass.parent, but its while runs inside no while; pass.parent is the enclosing while's pass.", path);

    /// <summary>S04-038: <c>pass.previous.node.&lt;id&gt;</c> naming a node that is not in the <c>while</c> <paramref name="loop"/>.</summary>
    public static SourceRefusal PassPreviousNotInWhile(string site, string id, string loop, string path) =>
        new("S04-038", $"{site} reads pass.previous.node.{id}, which is not in while '{loop}'; a pass reads the while's own nodes.", path);

    /// <summary>S04-039: a node <paramref name="id"/> read from outside the loop <paramref name="loop"/> it runs inside.</summary>
    public static SourceRefusal NodeInsideLoop(string site, string id, string loop, string path) =>
        new("S04-039", $"{site} reads node '{id}', which runs inside loop '{loop}'; read node.{loop}.", path);

    /// <summary>S04-040: <c>item.parent</c> read in an outermost loop.</summary>
    public static SourceRefusal ItemParentOutsideLoop(string site, string path) =>
        new("S04-040", $"{site} reads item.parent, but its loop runs inside no loop; item.parent is the enclosing loop's item.", path);

    /// <summary>S04-044: the case <paramref name="id"/> switches on <paramref name="reference"/>, of the type string <paramref name="format"/>, which is no enumerated string.</summary>
    public static SourceRefusal SwitchesOnNoEnum(string id, string reference, string format, string path) =>
        new("S04-044", $"Case '{id}' switches on {reference}, which is {format}; a case switches on an enumerated string.", path);

    /// <summary>S04-045: branch <paramref name="n"/> lists <paramref name="value"/>, none of the subject's <paramref name="values"/> (each quoted, in the format's order).</summary>
    public static SourceRefusal ListsNoValue(string id, int n, string value, string reference, string values, string path) =>
        new("S04-045", $"Case '{id}' branch {n} lists \"{value}\", which is not one of {reference}'s values ({values}).", path);

    /// <summary>S04-046: <paramref name="value"/> is listed by the branches <paramref name="n"/> and <paramref name="m"/>.</summary>
    public static SourceRefusal ListsValueTwice(string id, string value, int n, int m, string path) =>
        new("S04-046", $"Case '{id}' lists \"{value}\" in branches {n} and {m}; a value belongs to one branch.", path);

    /// <summary>S04-047: the subject's <paramref name="values"/> (each quoted, in the format's order) no branch lists, with no <c>else</c>.</summary>
    public static SourceRefusal ListsNotEveryValue(string id, string values, string path) =>
        new("S04-047", $"Case '{id}' doesn't list {values} and has no else; a case has a branch for every value: add a branch, an empty branch, or an else.", path);

    /// <summary>S04-075: the subject <paramref name="reference"/> can be null, and the case has no <c>else</c>.</summary>
    public static SourceRefusal NullSubjectWithNoElse(string id, string reference, string path) =>
        new("S04-075", $"Case '{id}' switches on {reference}, which can be null, and has no else; a null subject goes to the else: add one.", path);

    /// <summary>S04-050: the node <paramref name="id"/> of the function <paramref name="function"/> reads the input <paramref name="input"/>.</summary>
    public static SourceRefusal FunctionReadsInput(string id, string function, string input, string path) =>
        new("S04-050", $"Node '{id}' in function '{function}' reads input.{input}; a function reads only its parameters: add a parameter and pass it in with.", path);

    /// <summary>S04-051: the node <paramref name="id"/> of the function <paramref name="function"/> reads <paramref name="reference"/>, from outside it.</summary>
    public static SourceRefusal FunctionReadsOutside(string id, string function, string reference, string path) =>
        new("S04-051", $"Node '{id}' in function '{function}' reads {reference}; a function reads only its parameters: add a parameter and pass it in with.", path);

    /// <summary>S04-052: the function <paramref name="function"/> reads the package's <paramref name="kind"/> (<c>snippet</c>, <c>macro</c>, <c>format</c>, <c>renderer</c>) <paramref name="id"/>, which its <c>uses</c> does not list.</summary>
    public static SourceRefusal UsesUnlisted(string function, string kind, string id, string path) =>
        new("S04-052", $"Function '{function}' uses {kind} '{id}' but doesn't list it in uses.", path);

    /// <summary>S04-053: the function <paramref name="function"/> holds its own <paramref name="kind"/> <paramref name="id"/> and lists the package's in <c>uses</c>.</summary>
    public static SourceRefusal OwnAndUsed(string function, string kind, string id, string path) =>
        new("S04-053", $"Function '{function}' has its own {kind} '{id}' and also uses the package's: rename one.", path);

    /// <summary>S04-054: the function <paramref name="a"/> calls itself through the others; <paramref name="chain"/> is the ring after <paramref name="a"/>, each id quoted and joined by <c>', which calls '</c> (part 04 § 10.1: a longer ring is named in full).</summary>
    public static SourceRefusal CallsInARing(string a, string chain, string path) =>
        new("S04-054", $"Function '{a}' calls '{chain}', which calls '{a}': functions may not call themselves.", path);

    /// <summary>S04-055: the function <paramref name="a"/> calls itself directly.</summary>
    public static SourceRefusal CallsItself(string a, string path) =>
        new("S04-055", $"Function '{a}' calls '{a}': functions may not call themselves.", path);

    /// <summary>S04-056: the call <paramref name="id"/>, outside every function, starts a chain of nested calls <paramref name="depth"/> deep, past <see cref="FormatBounds.Depth"/>.</summary>
    public static SourceRefusal CallsTooDeep(string id, int depth, string path) =>
        new("S04-056", $"Call '{id}' reaches depth {depth}; calls nest at most FormatBounds.depth ({FormatBounds.Depth}) deep.", path);

    /// <summary>S04-057: the function <paramref name="function"/> returns the <c>if</c> <paramref name="id"/>, which has no <c>else</c>.</summary>
    public static SourceRefusal ReturnsAnIfWithNoElse(string function, string id, string path) =>
        new("S04-057", $"Function '{function}' returns node.{id}, an if with no else: add an else, or return a node that always runs.", path);

    /// <summary>S04-058: what the function <paramref name="function"/> returns is <paramref name="id"/> (its full name), which may be skipped inside the <c>if</c> (or <c>case</c>) <paramref name="branch"/>.</summary>
    public static SourceRefusal ReturnsASkippableNode(string function, string id, string branch, string path) =>
        new("S04-058", $"Function '{function}' returns node.{id}, which runs inside if '{branch}' and may be skipped: return a node that always runs.", path);

    /// <summary>S04-059: the function <paramref name="function"/> returns the <c>case</c> <paramref name="id"/>, whose branches for <paramref name="values"/> (each quoted) are empty.</summary>
    public static SourceRefusal ReturnsACaseWithAnEmptyBranch(string function, string id, string values, string path) =>
        new("S04-059", $"Function '{function}' returns node.{id}, a case with an empty branch for {values}: return a node that always runs.", path);

    /// <summary>S04-060: the function <paramref name="function"/> returns the <c>if</c> or <c>case</c> <paramref name="id"/>, two of whose bodies return the type strings <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static SourceRefusal ReturnsTwoFormats(string function, string id, string a, string b, string path) =>
        new("S04-060", $"Function '{function}' returns node.{id}, whose branches return {a} and {b}: a function returns one format. Wrap them in an object format.", path);

    /// <summary>S04-061: the function <paramref name="function"/> declares <paramref name="declared"/>, and its <c>ret</c> <paramref name="id"/> returns <paramref name="returned"/> (type strings).</summary>
    public static SourceRefusal FunctionFormatDisagrees(string function, string declared, string id, string returned, string path) =>
        new("S04-061", $"Function '{function}' declares format {declared}; its ret '{id}' returns {returned}.", path);

    /// <summary>S04-064: the call <paramref name="id"/> names <paramref name="function"/>, which no function of the package is.</summary>
    public static SourceRefusal UnknownFunction(string id, string function, string path) =>
        new("S04-064", $"Call '{id}' names function '{function}', which the package doesn't define.", path);

    /// <summary>S04-065: the call <paramref name="id"/> leaves out <paramref name="param"/>; <paramref name="parameters"/> are the function's, in declared order.</summary>
    public static SourceRefusal ParameterNotPassed(string id, string param, string function, string parameters, string path) =>
        new("S04-065", $"Call '{id}' passes no {param}; function '{function}' takes {parameters}.", path);

    /// <summary>S04-066: the call <paramref name="id"/> passes <paramref name="param"/>, which the function does not declare; <paramref name="parameters"/> are its own, in declared order (<c>none</c> for none).</summary>
    public static SourceRefusal ParameterNotTaken(string id, string param, string function, string parameters, string path) =>
        new("S04-066", $"Call '{id}' passes {param}, which function '{function}' doesn't take ({parameters}).", path);

    /// <summary>S04-068: the argument <paramref name="reference"/>, of the type string <paramref name="format"/>, does not fit <paramref name="param"/>'s <paramref name="expected"/>.</summary>
    public static SourceRefusal ArgumentDoesNotFit(string id, string reference, string format, string param, string function, string expected, string path) =>
        new("S04-068", $"Call '{id}' passes {reference} ({format}) as {param}; function '{function}' takes {expected}.", path);

    /// <summary>S04-069: a condition of the function <paramref name="function"/> reads <c>param.&lt;name&gt;</c>, bound to <c>profile.&lt;field&gt;</c>; <paramref name="where"/> is <c>a prompt</c>, <c>a part</c> or <c>a condition</c>.</summary>
    public static SourceRefusal ReadsProfileParameter(string function, string name, string field, string where, string path) =>
        new("S04-069", $"Function '{function}' reads param.{name}, which carries profile.{field}, in {where}; the profile is read only in deliverables and macro bodies.", path);

    /// <summary>S04-070: the call <paramref name="id"/> declares <paramref name="declared"/>, and the function returns <paramref name="returned"/> (type strings).</summary>
    public static SourceRefusal CallFormatDisagrees(string id, string declared, string function, string returned, string path) =>
        new("S04-070", $"Call '{id}' declares format {declared}; function '{function}' returns {returned}.", path);

    /// <summary>S04-074 (a warning): the function <paramref name="function"/> lists <paramref name="kind"/> <paramref name="id"/> in <c>uses</c> and never names it.</summary>
    public static SourceRefusal UsedNever(string function, string kind, string id, string path) =>
        new("S04-074", $"Function '{function}' lists {kind} '{id}' in uses but never uses it.", path);

    /// <summary>A05-005 (D13): the call <paramref name="id"/>'s argument <paramref name="param"/> is <paramref name="whatItIs"/> (a number, true or false): neither a reference nor <c>null</c>.</summary>
    public static SourceRefusal ArgumentNotAReference(string id, string param, string whatItIs, string path) =>
        new("A05-005", $"Call '{id}' with {param} is {whatItIs}; an argument is a reference, written as text, or null.", path);

    /// <summary>S04-015: the flow node or call <paramref name="id"/> (<paramref name="kind"/> <c>Loop</c>, <c>While</c>, <c>If</c>, <c>Case</c>, <c>Call</c>) declares <c>reproducible</c> <paramref name="declared"/>, and its body makes it the other.</summary>
    public static SourceRefusal ReproducibleNotTheBodys(string kind, string id, bool declared, string path) =>
        new("S04-015", $"{kind} '{id}' declares reproducible {Bool(declared)}; its body makes it {Bool(!declared)}.", path);

    /// <summary>S04-016: the flow node or call <paramref name="id"/> declares the flag <paramref name="member"/> (<c>modelDerived</c>, <c>runDependent</c>, <c>profileCarrying</c>) <paramref name="declared"/>, and its body makes it the other.</summary>
    public static SourceRefusal FlagNotTheBodys(string kind, string id, string member, bool declared, string path) =>
        new("S04-016", $"{kind} '{id}' declares {member} {Bool(declared)}; its body makes it {Bool(!declared)}.", path);

    /// <summary>S04-063: the macro <paramref name="macro"/>, placed in the function <paramref name="function"/>, has an <c>if</c> that reads <c>node.&lt;id&gt;</c>, which the function has no node of.</summary>
    public static SourceRefusal PlacedMacroReadsNothing(string macro, string function, string id, string path) =>
        new("S04-063", $"Macro '{macro}' is placed in function '{function}', where its if reads node.{id}, which doesn't exist there: add a classifier with that id (pattern A), or pass the decision in and hold it in a computed node (pattern B).", path);

    /// <summary>
    /// A05-003: nodes that read each other in a circle; <paramref name="a"/> is the circle's first node and <paramref name="chain"/>
    /// the nodes after it, each id quoted and joined by <c>', which reads node '</c> (a longer circle is named in full, as
    /// S04-054 is; a node reading itself is its own <paramref name="chain"/>).
    /// </summary>
    public static SourceRefusal ReadInACircle(string a, string chain, string path) =>
        new("A05-003", $"Node '{a}' reads node '{chain}', which reads node '{a}': nodes can't read each other in a circle. Break the circle.", path);

    /// <summary>
    /// A05-004 (D5): the function <paramref name="function"/>'s own format <paramref name="id"/> is bundled as
    /// <c>&lt;function&gt;__&lt;id&gt;</c>, a key another format the package uses already has.
    /// </summary>
    public static SourceRefusal BundleKeyTaken(string function, string id, string path) =>
        new("A05-004", $"Function '{function}' format '{id}' is compiled as '{function}__{id}', which the package already uses: rename one.", path);

    /// <summary>S04-072 (a note, part 04 § 10.2): up to <paramref name="n"/> agent calls could run at once, past the engine's per-job <paramref name="ceiling"/>.</summary>
    public static SourceRefusal CallsAtOnce(long n, int ceiling) =>
        new("S04-072", $"Up to {Number(n)} agent calls could run at once; this engine runs at most {Number(ceiling)} per job, so the rest wait their turn.", "");

    /// <summary>S04-073 (a note): the loop <paramref name="id"/> has no cap over a list known only at run time, so the agent calls under it are unbounded.</summary>
    public static SourceRefusal CallsAtOnceUnbounded(string id, int ceiling) =>
        new("S04-073", $"An unbounded number of agent calls could run at once (loop '{id}' has no cap over a list known only at run time); this engine runs at most {Number(ceiling)} per job, so the rest wait their turn.", "");

    /// <summary>S04-079 (a note): the loop <paramref name="id"/> iterates <paramref name="reference"/>, which could hold more than the engine's <paramref name="limit"/> items per loop.</summary>
    public static SourceRefusal ItemsPastLimit(string id, string reference, int limit, string path) =>
        new("S04-079", $"Loop '{id}' iterates {reference}, which could hold more than {Number(limit)} items; this engine runs at most {Number(limit)} per loop, and fails the loop beyond that.", path);

    /// <summary>S04-080 (a note): up to <paramref name="n"/> agent calls could run in one job, past the engine's <paramref name="limit"/>.</summary>
    public static SourceRefusal CallsPastLimit(long n, int limit) =>
        new("S04-080", $"Up to {Number(n)} agent calls could run in one job; this engine runs at most {Number(limit)} per job, and fails the job beyond that.", "");

    /// <summary>
    /// S09-016 (a note, part 09 § 3.5; #1508 D16): <paramref name="package"/>'s worst case could run for about
    /// <paramref name="minutes"/> minutes, past the engine's job limit of <paramref name="limit"/>; area 10's A10-014 has
    /// the same words.
    /// </summary>
    public static SourceRefusal WorstCaseMinutes(string package, long minutes, int limit) =>
        new("S09-016", $"{package} could run for about {Number(minutes)} minutes in the worst case; this engine stops a job after {Number(limit)} minutes.", "");

    /// <summary>
    /// A11-001 (#1508 R1, ruled by the owner): up to <paramref name="n"/> instances could open in one job, the heaviest
    /// path through the loop or call <paramref name="key"/>, past the engine's <paramref name="limit"/> (A09-001's
    /// <c>jobInstances</c>). Counts are written as A09-001 writes its limit, <c>10,000</c>.
    /// </summary>
    public static SourceRefusal TooManySteps(long n, string key, int limit, string path) =>
        new("A11-001", $"Up to {Grouped(n)} steps could run in one job, through {key}; this engine runs at most {Grouped(limit)} for one job. Lower a loop's max or split the workflow.", path);

    /// <summary>
    /// A11-002 (#1508 R4, ruled by the owner): the format <paramref name="id"/> names the format <paramref name="name"/> and
    /// also defines <paramref name="name"/> in its own <c>$defs</c>.
    /// </summary>
    public static SourceRefusal NameAlsoDefined(string id, string name, string path) =>
        new("A11-002", $"Format '{id}' names format '{name}' and also defines '{name}' in its own $defs. Rename the definition or remove it.", path);

    /// <summary>
    /// A11-003 (#1508 R5, ruled by the owner): the format <paramref name="id"/> expands to more than
    /// <paramref name="limit"/> nodes (<see cref="Run.PlanFormats.NodeBudget"/>), which the run would hold as <c>false</c>.
    /// </summary>
    public static SourceRefusal ExpandsTooFar(string id, int limit, string path) =>
        new("A11-003", $"Format '{id}' expands to more than {Grouped(limit)} parts, more than this engine can run. Make it smaller.", path);

    /// <summary>
    /// A11-010 (#1508, ruled by the owner, Q3 of 2026-11-26; R15), a publish warning: the deliverable <paramref name="id"/> has
    /// no <c>if</c> and <c>whenEmpty: release</c>, and every node or check it reads may not run.
    /// </summary>
    public static SourceRefusal MayBeReleasedEmpty(string id, string path) =>
        new("A11-010", $"Deliverable '{id}' could be released with nothing in it: everything it reads may not run. Give it an if, or whenEmpty: hold.", path);

    /// <summary>
    /// A11-011 (#1508, ruled by the owner, Q4 of 2026-11-26; R16), a publish warning: the deliverable <paramref name="id"/>'s
    /// <c>for</c> at <paramref name="line"/> over <c>&lt;loop key&gt;.items</c> (<paramref name="loop"/>) writes fixed text outside
    /// every <c>if</c> on its item's <c>state</c>.
    /// </summary>
    public static SourceRefusal FixedTextForEveryItem(string id, string loop, int line, string path) =>
        new("A11-011", $"Deliverable '{id}' writes fixed text for every item of {loop} at line {Number(line)}, also for items that did not run. Loop over .value, or guard on state.", path);

    /// <summary>
    /// S07-003 (at job start, part 07 § 10): the package needs <paramref name="component"/> (<c>Scriban</c>, <c>the
    /// evaluator</c> or an operation's name) in <paramref name="range"/>, and the engine runs
    /// <paramref name="engineVersion"/>; also said of a package locked before one of the engine's break points (§ 4.4).
    /// </summary>
    public static SourceRefusal ComponentOutOfRange(string package, string version, string component, string range, string engineVersion, string path) =>
        new("S07-003", $"This workflow ({package}@{version}) needs {component} {range}; this engine runs {component} {engineVersion}. Publish a new version of the workflow to run it on this engine.", path);

    /// <summary>S07-004 (at job start): the package needs the <paramref name="kindOrFeature"/> (<c>kind</c> or <c>feature</c>) <paramref name="name"/>, which the engine does not support.</summary>
    public static SourceRefusal NotSupported(string package, string version, string kindOrFeature, string name, string path) =>
        new("S07-004", $"This workflow ({package}@{version}) needs {kindOrFeature} '{name}', which this engine doesn't support. Publish a new version of the workflow to run it on this engine.", path);

    /// <summary>S07-005 (at job start): the package needs the attested agent <paramref name="agent"/> at <paramref name="agentVersion"/>, which the engine does not have.</summary>
    public static SourceRefusal AgentMissing(string package, string version, string agent, string agentVersion, string path) =>
        new("S07-005", $"This workflow ({package}@{version}) needs agent {agent} version {agentVersion}, which this engine doesn't have. Publish a new version of the workflow to run it on this engine.", path);

    /// <summary>S07-006 (at load, part 07 § 10): the compiled document's bytes are not canonical, or do not hash to the stamp's <c>resolveHash</c>; #1504 (R6): or a member publish writes in one form holds another (<c>requires.agents</c>' versions, <see cref="RequiresCheck.Engine"/>).</summary>
    public static SourceRefusal ResolveDoesNotMatch(string package, string version) =>
        new("S07-006", $"Workflow {package}@{version} can't run: its compiled document doesn't match the hash published for it.", CompiledPackage.ResolveFile);

    /// <summary>S07-017 (at load): the lock's bytes are not canonical or do not hash to the stamp's <c>lockHash</c>, or the lock and the compiled document do not name each other.</summary>
    public static SourceRefusal LockDoesNotMatch(string package, string version) =>
        new("S07-017", $"Workflow {package}@{version} can't run: its lock doesn't match the hash published for it, or doesn't belong to its compiled document.", ResolveCompiler.LockFile);

    /// <summary>S07-015 (a warning CI gives in the examples repository, § 7): the compiled copy at <paramref name="path"/> is committed.</summary>
    public static SourceRefusal EvidenceCommitted(string path) =>
        new("S07-015", $"{path} is evidence written only by publish; it is ignored and shouldn't be committed.", path);

    /// <summary>
    /// S07-016 (the Editor, on import, § 7): said once when the compiled copies intake <paramref name="dropped"/>
    /// (<see cref="SourceParseResult.Dropped"/>) hold <c>resolve.json</c> or <c>lock.json</c>; null when they hold neither.
    /// </summary>
    public static SourceRefusal? ImportIgnored(IReadOnlyList<string> dropped)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        return dropped.Any(d => d is "resolve.json" or ResolveCompiler.LockFile)
            ? new("S07-016", "This package's resolve.json and lock.json were ignored; publishing writes new ones.", "")
            : null;
    }

    /// <summary>S06-031 (at start, part 07 § 10; part 06): the package reads <c>profile.signature</c> and the account has none set.</summary>
    public static SourceRefusal SignatureNotSet() =>
        new("S06-031", "This workflow needs your signature. Choose one in Profile.", "");

    /// <summary>S06-032 (at start): the package reads a profile field other than the signature, labelled <paramref name="label"/> (<c>v31/profile.json</c>), and the account has none set.</summary>
    public static SourceRefusal ProfileFieldNotSet(string label) =>
        new("S06-032", $"This workflow needs your {label}. Set it in Profile, under Letter details.", "");

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Number(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Grouped(long value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
