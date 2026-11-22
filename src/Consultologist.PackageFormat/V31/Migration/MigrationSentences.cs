using System.Globalization;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § Refusals): the migration's sentences, one method each, named for the rule
/// it says, as <see cref="CheckSentences"/> does for area 08. Each is the row of <c>v31/sentences.json</c>
/// (<c>S10-NNN</c>) or the area's own <c>A10-NNN</c> with its placeholders filled in, character for character. No
/// sentence carries a value of an input, a model or a profile: only package text (an expression as the author wrote
/// it), ids, paths and members.
/// </summary>
internal static class MigrationSentences
{
    /// <summary>
    /// S10-003 (part 10 § 2.3): the 30 condition or <c>let</c> <paramref name="expression"/>, as written, uses
    /// <paramref name="construct"/> (a builtin's name, <c>an assignment</c>, <c>a template</c>, …), which § 2.2's
    /// table has no row for.
    /// </summary>
    public static string NoJsonLogicForm(string expression, string construct) =>
        $"The condition '{expression}' uses {construct}, which has no JSON Logic form at 31: rewrite it by hand.";

    /// <summary>
    /// A10-008 (part 10 § 2.3, R1): the 30 <c>!&lt;written&gt;</c> in <paramref name="file"/>'s <paramref name="member"/>
    /// on a text, as the author wrote it, became <c>blank(&lt;printed&gt;)</c> (the 31 operand as the printer shows it:
    /// <see cref="BlankConversion"/>), which also holds for empty and white-space-only text, where 30's did not.
    /// </summary>
    public static string BecameBlank(string file, string member, BlankConversion blank)
    {
        ArgumentNullException.ThrowIfNull(blank);
        return $"{file}, {member}: '!{blank.Written}' became blank({blank.Printed}); it now also holds for empty and white-space-only text.";
    }

    /// <summary>
    /// A10-012 (part 10 § 1.4, D2): the package is at specVersion <paramref name="version"/>, below 5, where no rung
    /// starts (the script's exit 2). Pre-v5 packages are archived.
    /// </summary>
    public static string BelowFive(int version) =>
        string.Create(CultureInfo.InvariantCulture, $"The package is at specVersion {version}; migration starts at 5.");

    /// <summary>
    /// A10-015 (part 10 § 1.1, D2): the package is at specVersion <paramref name="version"/>, at or past the version the
    /// migration writes, so there is nothing to raise. The earlier rungs' sentence, word for word.
    /// </summary>
    public static string AlreadyAt(int version) =>
        string.Create(CultureInfo.InvariantCulture, $"The package is already at specVersion {version}.");

    /// <summary>
    /// A10-016 (part 10 § 1.4, D2): <c>manifest.json</c> is not JSON, not an object, or has no whole-number
    /// <c>specVersion</c> (the script's exit 2). No part of the manifest is quoted.
    /// </summary>
    public const string ManifestDoesNotRead =
        "The manifest does not read as a package: it is not a JSON object with a whole-number specVersion.";

    /// <summary>
    /// A10-017 (D2): an earlier rung's own <paramref name="refusal"/> (its sentence, unchanged), naming the rung
    /// <paramref name="rung"/> as the Editor's upgrade names it.
    /// </summary>
    public static string AtRung(int rung, string refusal) =>
        string.Create(CultureInfo.InvariantCulture, $"At specVersion {rung}: {refusal}");

    /// <summary>
    /// A10-018 (D2): the earlier rung <paramref name="rung"/> failed on the package's content (its code threw), so the
    /// run is refused rather than the migration throwing. No part of the failure is quoted.
    /// </summary>
    public static string RungFailed(int rung) =>
        string.Create(CultureInfo.InvariantCulture, $"At specVersion {rung}: the rung could not read this package; fix the manifest, then migrate.");

    /// <summary>
    /// S10-002 (part 10 § 1.4): what the rewrite could not decide in <paramref name="file"/>'s <paramref name="member"/>,
    /// written in a form the 31 checker refuses, with its <paramref name="reason"/> (an A10 reason; its final full stop is
    /// the sentence's own).
    /// </summary>
    public static string HandEdit(string file, string member, string reason) =>
        $"Hand edit needed in {file}, {member}: {reason.TrimEnd('.')}.";

    /// <summary>
    /// A10-019 (part 10 § 1.4; part 01 § 4): <paramref name="file"/> (or, when <paramref name="header"/>, its header) is
    /// not strict JSON (RFC 8259, no member written twice, at most 32 levels deep), so the migration cannot read it. The
    /// fault names a member, never a value.
    /// </summary>
    public static string FileDoesNotRead(string file, bool header, StrictJsonFault fault, string? member)
    {
        var what = header ? "its header" : "it";
        var says = fault switch
        {
            StrictJsonFault.DuplicateMember => $"{what} names the member '{member}' twice",
            StrictJsonFault.TooDeep => string.Create(CultureInfo.InvariantCulture, $"{what} nests deeper than {SourceBounds.JsonDepth} levels"),
            _ => $"{what} is not JSON",
        };
        return $"The file '{file}' does not read: {says}; fix it, then migrate.";
    }

    /// <summary>
    /// A10-020 (part 10 § 1.4): the 30 reader (the manifest's read, its texts' headers, the validator or the walk) failed
    /// on the package's content (its code threw), so the run is refused rather than the migration throwing.
    /// </summary>
    public const string PackageDoesNotRead = "The package does not read at specVersion 30; fix it, then migrate.";

    /// <summary>
    /// A10-021 (part 02 § 11; part 10 § 1.2): a package format's <c>string</c> field <paramref name="field"/> (its path
    /// of property names, joined with <c>.</c>) holds free text; it stays data, and a text format is only suggested.
    /// </summary>
    public static string FreeTextField(string format, string field) =>
        $"Format '{format}', field '{field}' holds free text; consider a text format.";

    /// <summary>A10-022 (part 01 § 5.3, part 05 § 6.2): a reason: a 30 snippet folder item's <c>if</c> has no 31 place.</summary>
    public const string FolderItemIf =
        "a snippet folder's item has no if at 31; gate the item with an if node in the loop over the folder.";

    /// <summary>
    /// A10-023 (part 02 § 8.1, § 11): a reason: a 30 union input that has no 31 input format: one with no array arm, with
    /// more than one other arm, or whose array holds another type than its other arm.
    /// </summary>
    public const string UnionInput =
        "the input takes one of several types, which a 31 input cannot; choose one format.";

    /// <summary>A10-024 (part 02 § 3.1, § 3.2): a reason: a 30 type union of several types in a package format, which 31 has no type array for.</summary>
    public const string UnionField =
        "the field takes one of several types, which 31 writes as an anyOf; choose one.";

    /// <summary>A10-025 (part 02 § 3.2, § 3.3): a reason: a package format 30 made nullable at its root, which 31 writes as an anyOf a model's format may not have there.</summary>
    public const string NullableRoot =
        "the format is nullable at its root, which a format a model writes cannot be at 31; make the root an object.";

    /// <summary>
    /// A10-013 (part 05 § 1.2, D16): node <paramref name="id"/> declared <c>onInvalid: flag</c>, which kept a reply that did
    /// not fit its format; 31 has <c>fail</c> and <c>retry</c> only, and <c>fail</c> is written.
    /// </summary>
    public static string FlagBecomesFail(string id) =>
        $"Node '{id}' kept a reply that did not fit its format (onInvalid: flag); 31 fails it instead.";

    /// <summary>
    /// A10-027 (part 10 § 2.2, Review Focus 1, the Task 4 ruling): in <paramref name="file"/>'s <paramref name="member"/>,
    /// <c>size</c> of <paramref name="list"/> (printed), a list that can be null: 31's <c>size(null)</c> is null, where 30
    /// read an absent list as <c>[]</c> and counted 0, so the rule may answer differently when the list is absent.
    /// </summary>
    public static string SizeOfNullable(string file, string member, string list) =>
        $"{file}, {member}: size({list}) is null when {list} is null, where 30 counted 0.";

    /// <summary>
    /// A10-028 (part 04 § 5.2, part 10 § 1.2's <c>case</c> row): a reason: the <c>case</c> switches on
    /// <paramref name="subject"/>, which is no enumerated string, so 31 cannot list the values its branches miss.
    /// </summary>
    public static string CaseNotEnumerated(string subject) =>
        $"the case switches on {subject}, whose values 31 cannot list; switch on an enumerated string.";

    /// <summary>
    /// A10-032 (part 04 § 5.2, part 10 § 2.2): a reason: the <c>case</c> switches on the 30 classifier <paramref name="id"/>,
    /// whose answers (its <c>with</c> values) 31 does not type: <c>classification</c>'s <c>value</c> is a plain string, so
    /// the checker refuses the subject (S04-044) until the classifier's format lists its answers.
    /// </summary>
    public static string CaseOnClassifier(string id) =>
        $"the case switches on classifier '{id}', whose answers are not typed at 31; give it a package format whose value is an enum of its answers.";

    /// <summary>
    /// A10-029 (part 04 § 5.2): a reason: the <c>case</c>'s subject <paramref name="subject"/> can be <c>null</c>, which 31
    /// sends to an <c>else</c>, and the 30 case had none.
    /// </summary>
    public static string CaseSubjectNullable(string subject) =>
        $"{subject} can be null, which a 31 case sends to its else; add an else body.";

    /// <summary>
    /// A10-010 (part 02 § 11's last bullet): node <paramref name="id"/> declared <c>reproducible</c>
    /// <paramref name="claimed"/> at 30, and the derived <paramref name="written"/> (part 02 § 1.1) is what 31 holds.
    /// </summary>
    public static string ReproducibleDiffered(string id, bool claimed, bool written) =>
        $"Node '{id}' declared reproducible {Word(claimed)} at 30; {Word(written)} is written, as its reads give it.";

    private static string Word(bool value) => value ? "true" : "false";

    /// <summary>
    /// A10-030 (D16): node <paramref name="id"/> declared <paramref name="member"/> at 30, which 31 has no member for (a
    /// classifier's <c>with</c> values); it is not written.
    /// </summary>
    public static string MemberDropped(string id, string member) =>
        $"Node '{id}' declared {member} at 30, which 31 has no member for; it was dropped.";

    /// <summary>
    /// A10-031 (part 02 § 3.3): a reason: a package format whose root is a scalar or <c>null</c>, where a 31 package format
    /// is an object or a list.
    /// </summary>
    public const string PackageFormatRoot =
        "a package format's root is an object or a list at 31; make it one, or use a registry format.";

    /// <summary>
    /// A10-026 (part 01 § 2.4): <paramref name="file"/> held a carriage return that did not end a line, which 31 refuses;
    /// it was made a line end, so the file's bytes changed there.
    /// </summary>
    public static string LoneCarriageReturn(string file) =>
        $"{file}: a carriage return that didn't end a line became a line end.";

    /// <summary>
    /// A10-006 (part 10 § 1.9, part 01 § 6, D6): the <paramref name="kind"/> (<c>node</c>, <c>input</c>, <c>macro</c>,
    /// <c>snippet</c>, <c>format</c>, <c>deliverable</c>) <paramref name="id"/>, a word or a Windows name 31 reserves, was
    /// renamed <paramref name="newId"/>: the hand choice, or the id with its kind as a suffix.
    /// </summary>
    public static string RenamedReserved(string kind, string id, string newId) =>
        $"Renamed {kind} '{id}' to '{newId}': '{id}' is reserved at 31.";

    /// <summary>
    /// S10-006 (part 10 § 1.9): <paramref name="id"/>'s suffixed form <paramref name="tried"/> (<c>&lt;id&gt;_&lt;kind&gt;</c>)
    /// was already an id, so the rename took the first free numeric suffix, <paramref name="newId"/>.
    /// </summary>
    public static string RenameTaken(string id, string newId, string tried) =>
        $"Renamed '{id}' to '{newId}'; '{tried}' was already taken.";

    /// <summary>
    /// A10-033 (part 10 § 1.9, D6): the hand-chosen rename of <paramref name="id"/> to <paramref name="newId"/>
    /// (<see cref="Migration31Options.Renames"/>) cannot be made: <paramref name="newId"/> is not a good 31 id (part 01
    /// § 6) or is already one. A refusal: the operator's choice is never replaced by another.
    /// </summary>
    public static string ChosenRenameRefused(string id, string newId, bool taken) =>
        $"The chosen rename of '{id}' to '{newId}' cannot be made: '{newId}' {(taken ? "is already taken" : "is not a 31 id")}; choose another.";

    /// <summary>
    /// A10-034 (part 01 § 6, § 7 <c>idLength</c>; part 10 § 1.9): the <paramref name="kind"/> <paramref name="id"/>, longer
    /// than 31's 64 characters (30 had no bound), was shortened to <paramref name="newId"/>.
    /// </summary>
    public static string RenamedTooLong(string kind, string id, string newId) =>
        $"Renamed {kind} '{id}' to '{newId}': an id is at most 64 characters at 31.";

    /// <summary>
    /// A10-001 (part 10 § 1.4, part 05 § 2.5): a reason: the body <c>if</c> at <paramref name="line"/> of the 31 file tests
    /// a value, so 31 refuses it in a body, and it is too complex to lift into parts (an <c>else if</c>, an <c>if</c> inside
    /// another <c>if</c> or a <c>for</c>, an <c>if</c> inside a line of text, a test mixing a recorded fact with a value).
    /// </summary>
    public static string IfTooComplex(int line) =>
        string.Create(CultureInfo.InvariantCulture, $"the body's if at line {line} does more than choose text to show; write it as parts with their own if.");

    /// <summary>
    /// A10-005 (part 05 § 2, <c>scriban-dialect.md</c> § 10): a reason: the body uses <paramref name="construct"/> (a
    /// statement, an operator or a built-in as written, <c>item.label</c>, a 30 run fact) at <paramref name="line"/> of
    /// the 31 file, which the dialect has no form for.
    /// </summary>
    public static string NotInDialect(string construct, int line) =>
        string.Create(CultureInfo.InvariantCulture, $"the template uses {construct} at line {line}, which the 31 dialect doesn't have; rewrite it.");

    /// <summary>S10-004 (part 10 § 1.4, part 05 § 3.1): a reason: a 30 prompt reads <c>profile.&lt;field&gt;</c>, which a 31 prompt never reads.</summary>
    public static string ProfileInPrompt(string field) =>
        $"the prompt reads profile.{field}, which a 31 prompt doesn't read; move it to a deliverable or a macro body.";

    /// <summary>
    /// A10-035 (part 05 §§ 2.5, 6.3, 7): a reason: the body <c>if</c> at <paramref name="line"/> tests a value in a body
    /// that has no parts (<paramref name="site"/>: <c>snippet</c>, <c>macro</c> or <c>rendered node</c>), so it cannot be
    /// lifted; a snippet with its own <c>if</c> may be placed in each of those bodies.
    /// </summary>
    public static string IfWithoutParts(int line, string site) =>
        string.Create(CultureInfo.InvariantCulture, $"the body's if at line {line} decides what a {site} body shows, which has no parts; write each text as a snippet with its own if, placed where the text was.");

    /// <summary>
    /// A10-036 (part 10 § 1.2's hoist row, A10-002's sibling): a reason: <paramref name="macro"/>'s placements in
    /// <paramref name="files"/> (one or several) carry different conditions (a hoisted body <c>if</c> on one, another or
    /// none on another), and a macro has one <c>if</c>.
    /// </summary>
    public static string PlacedUnderConditions(string macro, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var listed = files.Count < 2 ? string.Concat(files) : string.Join(", ", files.Take(files.Count - 1)) + " and " + files[^1];
        return $"macro '{macro}' is placed under different conditions in {listed}; a macro has one if, so place it under one condition, or split it into one macro per condition.";
    }

    /// <summary>
    /// A10-037 (part 10 § 1.2's bare-read row, A07-007): a reason: a 30 bare read at <paramref name="line"/>, written whole,
    /// is <paramref name="path"/> at 31, an object or a list (<paramref name="list"/>), which no output writes.
    /// </summary>
    public static string WritesWhole(string path, int line, bool list) =>
        string.Create(CultureInfo.InvariantCulture, $"the template writes {path} at line {line}, which is {(list ? "a list" : "an object")} at 31; read a member, loop over it or pipe it into a renderer.");

    /// <summary>A10-038 (part 10 § 1.4, A07-005): a reason: the body cannot be read as a template from <paramref name="line"/> on (a tag, an <c>if</c> or a <c>for</c> never closed).</summary>
    public static string Unreadable(int line) =>
        string.Create(CultureInfo.InvariantCulture, $"the template can't be read at line {line}; close every tag, if and for.");

    /// <summary>
    /// A10-039 (part 05 § 3.1, part 06 § 6.3): a reason: the body reads <paramref name="path"/> at <paramref name="line"/>, a
    /// root its <paramref name="site"/> (<c>prompt</c>, <c>rendered node</c>, <c>macro body</c>, <c>deliverable</c> for a
    /// snippet a deliverable places) never reads at 31 (<c>deliverable.*</c>, a <c>rendered</c> node's <c>profile.*</c>).
    /// </summary>
    public static string ReadNotAllowed(string path, int line, string site) =>
        string.Create(CultureInfo.InvariantCulture, $"the template reads {path} at line {line}, which a 31 {site} doesn't read; rewrite it.");

    /// <summary>
    /// A10-003 (part 10 § 1.4, D3; part 05 § 10): a reason: a 30 node instance placed macro <paramref name="macro"/>'s text
    /// <paramref name="where"/> (<c>before</c>, <c>after</c>, or <c>before and after</c> when instances place it on both sides) the node's reply; at 31 the model places its marker, so the
    /// instruction must say where.
    /// </summary>
    public static string PlacedByTheEngine(string macro, string where) =>
        $"macro '{macro}' was placed {where} the reply by the engine; say in its instruction where the model writes its marker.";

    /// <summary>A10-004 (D4): a reason: macro <paramref name="macro"/> is read both as <c>.content</c> (fixed text) and as <c>.prompt</c> (an instruction) at 30.</summary>
    public static string ReadBothWays(string macro) =>
        $"macro '{macro}' is read both as fixed text and as an instruction; split it into a snippet and a macro.";

    /// <summary>
    /// A10-007 (part 10 § 1.3, part 02 § 10): the optional macro <paramref name="macro"/> is now offered by the optional
    /// boolean input <paramref name="input"/> its <c>if</c> reads (the macro's id, or with <c>_macro</c> when an input had it).
    /// </summary>
    public static string OfferedByInput(string macro, string input) =>
        $"Macro '{macro}' is now offered by the input '{input}'.";

    /// <summary>
    /// A10-040 (D4): a reason: a 30 body reads macro <paramref name="macro"/>'s <c>.content</c> (fixed text) at
    /// <paramref name="line"/>, but the macro does not become a snippet (it has an <c>if</c>, is optional, is a folder's, or is
    /// placed by a node instance or on a deliverable with a condition), and at 31 <c>macro.&lt;id&gt;.content</c> is a marker.
    /// </summary>
    public static string ReadAsFixedText(string macro, int line) =>
        string.Create(CultureInfo.InvariantCulture, $"macro '{macro}' is read as fixed text at line {line}, but it is offered under a condition or placed by the engine; place its text as a snippet with an if.");

    /// <summary>
    /// A10-045 (D4; part 05 § 8.1): a reason: a 30 body reads the folder macro <paramref name="macro"/>'s <c>.content</c>
    /// (<c>&lt;folder&gt;/&lt;id&gt;</c>, in a <c>for</c> over the folder) at <paramref name="line"/>; a folder's item is
    /// never a snippet, so the read has no 31 form.
    /// </summary>
    public static string FolderReadAsFixedText(string macro, int line) =>
        string.Create(CultureInfo.InvariantCulture, $"macro '{macro}' is read as fixed text at line {line}, but a macro folder's item is never fixed text at 31; place its text as a snippet.");

    /// <summary>A10-041 (part 05 § 8.3, § 8.7): a reason: a 30 macro's body places macro <paramref name="placed"/> (its <c>.prompt</c>), which no 31 macro body does.</summary>
    public static string BodyPlacesMacro(string placed) =>
        $"the body places macro '{placed}', which a 31 macro body cannot; place it in the prompt instead.";

    /// <summary>
    /// A10-042 (D5; part 05 § 8.4): a reason: the 30 instruction is itself a template (it renders a tag before the model
    /// sees it), and a 31 instruction is plain lines holding the macro's own marker once.
    /// </summary>
    public const string InstructionIsATemplate =
        "the instruction is a template at 30, which a 31 instruction cannot be; write it as lines that say where the model writes the marker.";

    /// <summary>
    /// A10-002 (part 10 § 1.4, D3, D4; part 06 § 6.3): a reason: macro <paramref name="macro"/> is placed on the deliverable
    /// with a condition (the placement's <c>if</c>, its own, or as an optional macro); a 31 deliverable places no macro, and
    /// only fixed text becomes a snippet read.
    /// </summary>
    public static string DeliverableMacroWithCondition(string macro) =>
        $"macro '{macro}' is placed on this deliverable with a condition; place its text as a snippet with an if, or write the condition in the template.";

    /// <summary>
    /// A10-046 (part 06 §§ 1, 3): a reason: the 30 check <paramref name="check"/> ran inside the <paramref name="kind"/>
    /// node <paramref name="flow"/> (decided with its branch, or once per item), and a 31 check is never inside a node flow.
    /// </summary>
    public static string CheckInsideAFlow(string check, string kind, string flow) =>
        $"check '{check}' ran inside the {kind} node '{flow}' at 30, but a 31 check is never inside a node flow; take it out of this list and gate checks/{check}.md with an if, or make it a check loop over the loop's .items.";

    /// <summary>A10-047 (part 06 § 6.3; D4): a reason: a deliverable places <paramref name="macro"/>, which is no single macro of the package (30 refuses it first).</summary>
    public static string NotASingleMacro(string macro) =>
        $"'{macro}' is placed on this deliverable but is not a single macro of the package; place a snippet here instead.";

    /// <summary>
    /// A10-048 (D4): a notice: the deliverable macro <paramref name="macro"/>, kept as a macro, is read on deliverables as the
    /// snippet <paramref name="snippet"/>, since a snippet already has its id.
    /// </summary>
    public static string SnippetCopyRenamed(string macro, string snippet) =>
        $"Macro '{macro}' is read on deliverables as the snippet '{snippet}': a snippet '{macro}' already exists.";
}
