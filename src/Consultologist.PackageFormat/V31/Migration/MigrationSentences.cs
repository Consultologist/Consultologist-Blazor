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
    /// A10-050 (part 05 § 7; #1507, Task 12 fix rounds 1 and 2; the difference is expected, #1508, comment 5, item 8): in <paramref name="file"/>'s <paramref name="member"/>,
    /// the comparison of a trimmed text with <c>""</c> (<see cref="BlankConversion.Written"/>, as the printer shows it)
    /// became <c>blank(x)</c> or <c>not blank(x)</c> (<see cref="BlankConversion.Printed"/>); 30 compared a null text (a node
    /// that did not run, a field of an object input left out) as not empty, so the rule now holds, or no longer holds, when <c>x</c> is null. The wording
    /// follows A10-008 and A10-027.
    /// </summary>
    public static string TrimBecameBlank(string file, string member, BlankConversion trim)
    {
        ArgumentNullException.ThrowIfNull(trim);
        // The printer writes the negation as "not blank(x)" (JsonLogicPrinter), the comparison with != as that.
        var holds = trim.Printed.StartsWith("not ", StringComparison.Ordinal) ? "no longer holds" : "also holds";
        return $"{file}, {member}: '{trim.Written}' became {trim.Printed}; it now {holds} when {string.Join(" or ", trim.Reads)} is null.";
    }

    /// <summary>
    /// A10-074 (#1508, comment 5, item 2; #1508, 2026-11-26, item 6; part 10 § 2.2): in <paramref name="file"/>'s
    /// <paramref name="member"/>, a text compared with a literal (<see cref="BlankConversion.Written"/>, as the printer
    /// shows it), which 31 refuses (S03-002), became membership in a list of that literal
    /// (<see cref="BlankConversion.Printed"/>), with <c>trim</c> written on each text read of an input (an item of an
    /// input's list too): 30 trimmed an input's text at its end before it compared, and 31's <c>trim</c> trims both ends,
    /// so an input's text that starts with white space now matches where 30's did not. The last clause, on the trim, is
    /// said only where a trim was written (<see cref="BlankConversion.Trimmed"/>): a node's text is compared as it is.
    /// </summary>
    public static string TextLiteralBecameIn(string file, string member, BlankConversion literal)
    {
        ArgumentNullException.ThrowIfNull(literal);
        var trimmed = literal.Trimmed ? ", and an input's text is trimmed at both ends first, where 30 trimmed its end" : "";
        return $"{file}, {member}: '{literal.Written}' became {literal.Printed}; text is compared with a literal by in at 31{trimmed}.";
    }

    /// <summary>
    /// A10-085 (#1508, 2026-11-26, item 6): <paramref name="comparison"/> (as the printer shows it) compares an input's
    /// text with a literal whose first character is white space: 30 kept an input's leading white space, so it could
    /// match, and the <c>trim</c> the migration writes on the input never leaves any, so no trim fits. A reason S10-002
    /// gives.
    /// </summary>
    public static string ComparedWithLeadingWhiteSpace(string comparison) =>
        $"'{comparison}' compares an input's text with a literal that starts with white space, which 30 could match and 31's trim never can; write the comparison so that it keeps 30's answer.";

    /// <summary>
    /// A10-075 (#1508, comment 5, item 2; Task 30 fix round 1): in <paramref name="file"/>'s <paramref name="member"/>, a
    /// text input read whole compared with <c>""</c> (<see cref="BlankConversion.Written"/>) became
    /// <c>x === null or x in [""]</c> or <c>x !== null and not (x in [""])</c> (<see cref="BlankConversion.Printed"/>):
    /// 30 read the input not given or blank as <c>""</c>, 31's start leaves it out and it reads null, which the null test
    /// counts as 30's <c>""</c>, so the rule answers as 30's did.
    /// </summary>
    public static string EmptyBecameNullTested(string file, string member, BlankConversion empty)
    {
        ArgumentNullException.ThrowIfNull(empty);
        return $"{file}, {member}: '{empty.Written}' became {empty.Printed}; 31 reads an input not given or blank as null, where 30 read \"\".";
    }

    /// <summary>
    /// A10-076 (#1508, comment 5, item 2; Task 30 fix round 1): the 30 condition <paramref name="expression"/>, as written,
    /// compares <paramref name="text"/> (printed at 31), which reads an input, with blank text: 30 read an input's text
    /// not given or blank as <c>""</c>, 31 reads it as null or as given, and no 31 form is written for it. A reason S10-002
    /// gives.
    /// </summary>
    public static string ComparedWithBlankText(string expression, string text) =>
        $"The condition '{expression}' compares {text}, read from an input, with blank text, which 30 and 31 read differently when the text is blank or not given; rewrite it by hand.";

    /// <summary>
    /// A10-073 (#1508, comment 5, item 2; part 10 § 2.2): the 30 condition <paramref name="expression"/>, as written, calls
    /// <c>string.contains</c> on <paramref name="list"/> (the 31 read, printed), a list: 30 wrote the list as text and
    /// searched it, where 31's <c>in</c> on a list is membership, so no rule is written. A reason S10-002 gives.
    /// </summary>
    public static string ContainsOnAList(string expression, string list) =>
        $"The condition '{expression}' uses string.contains on {list}, a list, which 30 tested as a substring of the list written as text; rewrite it by hand.";

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
    /// A10-013 (part 05 § 1.2, D16; #1508, comment 5, item 3): node <paramref name="id"/> declared <c>onInvalid: flag</c>,
    /// which kept a reply that did not fit its format; 31 has <c>fail</c> and <c>retry</c> only, and <c>retry</c> is
    /// written. Kept as a notice: #1447's before-and-after notification lists it.
    /// </summary>
    public static string FlagBecomesRetry(string id) =>
        $"Node '{id}' kept a reply that did not fit its format (onInvalid: flag); 31 makes one more attempt with the validation error, then the node fails.";

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
    /// A10-049 (D15): the header <paramref name="file"/> is made of a body the dialect refused (a hand edit's), so its
    /// <paramref name="flags"/> (each derived <c>false</c>) are not known to be false; the checker holds them once the edits
    /// are made.
    /// </summary>
    public static string FlagsNotKnown(string file, IReadOnlyList<string> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        var list = flags.Count == 1 ? flags[0] : string.Join(", ", flags.Take(flags.Count - 1)) + " and " + flags[^1];
        var (verb, pronoun) = flags.Count == 1 ? ("is", "it") : ("are", "them");
        return $"{file}: {list} {verb} written false without the bodies that need a hand edit; check {pronoun} once the edits are made.";
    }

    /// <summary>
    /// A10-030 (D16): node <paramref name="id"/> declared <paramref name="member"/> at 30, which 31 has no member for (a
    /// classifier's <c>with</c> values); it is not written.
    /// </summary>
    public static string MemberDropped(string id, string member) =>
        $"Node '{id}' declared {member} at 30, which 31 has no member for; it was dropped.";

    /// <summary>
    /// A10-064 (#1507, Task 15 fix round 1; D16; #1508 item 7): classifier <paramref name="id"/>'s <c>with</c> values have no 31
    /// member; they are the enum of its answer format, which 31 sends after its prompt (in place of A10-030 for a classifier's
    /// <c>with</c>); 30's "Answer with exactly one of" line is dropped.
    /// </summary>
    public static string ValuesInAnswerFormat(string id) =>
        $"Node '{id}' declared with at 30, which 31 has no member for; its values are the enum of its answer format, which 31 sends after its prompt.";

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
    /// A10-068 (part 05 § 2.6, part 10 § 1.2; #1507 final review A4): a notice: in <paramref name="file"/>, the white-space
    /// marks on <paramref name="lines"/> of the 30 body were rewritten as 31 has them (<c>-</c>, which removed every white
    /// space and line ending, is <c>~</c>, which removes spaces and tabs and one line ending on its right; a placement's
    /// mark is dropped but a <c>{{~</c> alone on its line), so the text renders with the line endings beside them kept.
    /// Never a hand edit.
    /// </summary>
    public static string MarksRewritten(string file, IReadOnlyList<int> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var listed = lines.Count == 1
            ? string.Create(CultureInfo.InvariantCulture, $"line {lines[0]}")
            : "lines " + string.Join(", ", lines.Take(lines.Count - 1).Select(l => l.ToString(CultureInfo.InvariantCulture)))
                + " and " + lines[^1].ToString(CultureInfo.InvariantCulture);
        return $"{file}: the white-space marks at {listed} of the 30 body became 31's (- is ~; a placement keeps only a {{{{~ alone on its line), so line endings beside them are now kept.";
    }

    /// <summary>
    /// A10-006 (part 10 § 1.9, part 01 § 6, D6): the <paramref name="kind"/> (<c>node</c>, <c>input</c>, <c>macro</c>,
    /// <c>snippet</c>, <c>format</c>, <c>deliverable</c>) <paramref name="id"/>, a word or a Windows name 31 reserves, was
    /// renamed <paramref name="newId"/>: the hand choice, or the id with its kind as a suffix.
    /// </summary>
    public static string RenamedReserved(string kind, string id, string newId) =>
        $"Renamed {kind} '{id}' to '{newId}': '{id}' is reserved at 31.";

    /// <summary>
    /// A10-065 (part 01 § 9.1, S01-034; part 10 § 1.9, D6; #1507 final review A1): the package format <paramref name="id"/>
    /// has the name of a 31 <paramref name="registry"/> format (<see cref="BuiltIn"/> or <see cref="Catalogue"/>), which
    /// 30 admitted and 31 refuses, and was renamed <paramref name="newId"/>: the hand choice, or the id with <c>_format</c>.
    /// </summary>
    public static string RenamedRegistryName(string id, string newId, string registry) =>
        $"Renamed format '{id}' to '{newId}': '{id}' is the name of a {registry} format at 31.";

    /// <summary>A10-065's word for a format of the registry's <c>v31/formats/</c>.</summary>
    public const string BuiltIn = "built-in";

    /// <summary>A10-065's word for a format of the agents catalogue.</summary>
    public const string Catalogue = "catalogue";

    /// <summary>
    /// S10-006 (part 10 § 1.9):<paramref name="id"/>'s suffixed form <paramref name="tried"/> (<c>&lt;id&gt;_&lt;kind&gt;</c>)
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
    /// A10-069 (part 01 § 5.3, § 9.1; part 10 § 1.9, A10-033's sibling; #1507 final review A6): the hand-chosen item
    /// format <paramref name="format"/> of snippet folder <paramref name="folder"/>
    /// (<see cref="Migration31Options.FolderFormats"/>) cannot be used: it is no 31 id, or is already a format of the
    /// package or the registry. A refusal: the operator's choice is never replaced by another.
    /// </summary>
    public static string ChosenFolderFormatRefused(string format, string folder, bool taken) =>
        $"The chosen format '{format}' for snippet folder '{folder}' cannot be used: '{format}' {(taken ? "is already taken" : "is not a 31 id")}; choose another.";

    /// <summary>
    /// A10-070 (#1508, R18; part 10 § 1.4): <c>options.json</c> (<see cref="Migration31Options.FromJson"/>) has the member
    /// <paramref name="member"/>, which is none of the three choices. A refusal: an option the migration does not read is
    /// never ignored.
    /// </summary>
    public static string UnknownOption(string member) =>
        $"options.json has the member '{member}', which is not an option of the migration; its members are renames, whenEmpty and folderFormats.";

    /// <summary>
    /// A10-071 (#1508, R18): <c>options.json</c>'s <paramref name="member"/> (<c>renames</c>, <c>whenEmpty</c> or
    /// <c>folderFormats</c>) is missing, or is not an object whose every value is text.
    /// </summary>
    public static string OptionNotAMap(string member) =>
        $"options.json must give {member} as an object whose values are text; write {{}} for none.";

    /// <summary>
    /// A10-072 (#1508, R18; part 10 § 1.10, part 06): <c>options.json</c>'s <c>whenEmpty</c> gives deliverable
    /// <paramref name="deliverable"/> a value that is not <c>release</c>, <c>hold</c> or <c>fail</c> (the value itself is
    /// not repeated).
    /// </summary>
    public static string WhenEmptyNotAValue(string deliverable) =>
        $"options.json gives deliverable '{deliverable}' a whenEmpty that is not release, hold or fail.";

    /// <summary>
    /// A10-082 (#1508, area 11 Task 36, carried from Task 29's review): <c>options.json</c>'s <paramref name="member"/> maps
    /// an empty id, or maps an id to an empty choice, which the script's flags refuse too (<c>&lt;key&gt;=&lt;value&gt;</c>).
    /// </summary>
    public static string OptionEmpty(string member) =>
        $"options.json gives {member} an empty id or an empty choice; name each id and its choice.";

    /// <summary>
    /// A10-083 (#1508, area 11 Task 36; part 10 §§ 1.9, 1.10, part 01 § 3): a package with no <c>options.json</c> is
    /// migrated with the defaults, and the bulk publish says so in its record.
    /// </summary>
    public const string DefaultOptions =
        "No options.json was given, so the migration's defaults were used: a reserved id takes its kind as a suffix, every deliverable's whenEmpty is release, and a snippet folder's items take <folder>_item.";

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

    /// <summary>
    /// A10-078 (#1508, comment 6, item 1; R13; Task 32 fix round 1): a reason: macro <paramref name="macro"/> is placed by
    /// more than one agent, so the migration wrote a classifier for each (<paramref name="nodes"/>, in order) and its
    /// <c>if</c> joins their answers by <c>and</c>, a condition 30 did not have: each of 30's placing nodes' models chose
    /// on its own.
    /// </summary>
    public static string SplitPerPlacer(string macro, IReadOnlyList<string> nodes) =>
        $"macro '{macro}' is placed by {nodes.Count.ToString(CultureInfo.InvariantCulture)} nodes, with a classifier each ({string.Join(", ", nodes)}), and its if needs every answer yes, where 30's model chose at each node on its own; split the macro into one per placing node, or choose its condition.";

    /// <summary>
    /// A10-079 (#1508, comment 7, Q3; R15; #1543, ruling 3, D11; part 10 § 1.2): the deliverable <paramref name="file"/> was
    /// given the <c>if</c> that skips it when one of its sources that may not run (<paramref name="reads"/>, the 31 reads, in
    /// <c>sources</c> order) did not run: 30 skipped a deliverable when any source was a flow that took no branch, where 31
    /// would compose it from the others and release it.
    /// </summary>
    public static string SkippedUnlessASourceRan(string file, IReadOnlyList<string> reads) =>
        $"{file}, if: written to skip the deliverable when a source did not run ({string.Join(", ", reads)}), as 30 skipped it.";

    /// <summary>
    /// A10-087 (#1508, 2026-11-26, item 1; F-D6; part 10 § 1.2): the leaf <paramref name="file"/> was given the <c>if</c> of
    /// <paramref name="deliverables"/> (in 30's order), the deliverables with a condition that need it: 30 ran only what an
    /// applicable deliverable needed, where 31 runs every node.
    /// </summary>
    public static string RunsOnlyWhen(string file, IReadOnlyList<string> deliverables) =>
        $"{file}, if: written so that the node runs only when deliverable {string.Join(" or ", deliverables)} applies, as 30 ran only what an applicable deliverable needed.";

    /// <summary>
    /// A10-088 (#1510; #1509 comment 7207709261, item 1; F-D1): the <c>for</c> <paramref name="loop"/>, whose <c>.content</c>
    /// is read, returns each item through the new <c>rendered</c> node <paramref name="node"/>, whose body strips the old
    /// <c>ret</c>'s text, as 30 trimmed each item of a loop it composed.
    /// </summary>
    public static string StripsLoopItems(string loop, string node) =>
        $"Loop '{loop}' returns each item through the new node '{node}', which strips the item's text as specVersion 30 did.";

    /// <summary>
    /// A10-089 (#1510; #1509 comment 7207709261, item 2; F-D4): the signature is written through the new renderer
    /// <paramref name="renderer"/>, whose body tests the signature trimmed, as 30 appended no empty signature block.
    /// </summary>
    public static string SignatureThroughRenderer(string renderer) =>
        $"The signature is written through renderer '{renderer}', which leaves it and the line before it out when the signature is blank, as specVersion 30 did.";

    /// <summary>
    /// A10-090 (#1510, F1 fix round 1): a reason: the <c>for</c> <paramref name="loop"/>, whose <c>.content</c> is read, returns
    /// items of more than one text format (an <c>if</c> or a <c>case</c> of a Markdown and a plain branch), so no one node can
    /// strip them and keep each as the engine writes it: the loop keeps its <c>ret</c>, and the operator strips each branch.
    /// </summary>
    public static string LoopItemsMixFormats(string loop) =>
        $"loop '{loop}' returns items of more than one text format, so its items are not stripped as specVersion 30 stripped them; strip each branch's text where it is written.";

    /// <summary>
    /// A10-091 (#1510; #1509 comment 7209167166, item 4; G-D8, G-D10): in the Markdown body <paramref name="file"/>, the
    /// migration ended <paramref name="count"/> line endings inside a paragraph with <c>\</c>, where 30's pipeline broke a
    /// single line ending and 31's does not. One per file; the count agrees with its noun (#1543, ruling 10). (A10-092, a body
    /// a prompt reads whose line endings were kept, is retired by #1543, ruling 1: such a body is broken like any other.)
    /// </summary>
    public static string ExplicitHardBreaks(string file, int count) => count == 1
        ? $"In '{file}', 1 line ending inside a paragraph ends with \\ (an explicit hard break), where specVersion 30 broke the line."
        : $"In '{file}', {count} line endings inside a paragraph end with \\ (an explicit hard break), where specVersion 30 broke the line.";

    /// <summary>
    /// A10-093 (#1543, ruling 2, D7, as the owner amended it on 2026-11-27): in the Markdown body <paramref name="file"/>, the
    /// migration separated each of <paramref name="reads"/> (a <c>markdown_block</c> read, as written without its pipes) from
    /// the text beside it by a blank line: text on the read's line, or a line of text next to the read's own. One per file;
    /// one read or several.
    /// </summary>
    public static string BlockReadsMoved(string file, IReadOnlyList<string> reads) => reads.Count == 1
        ? $"In '{file}', the read {reads[0]} now starts its own line, after a blank line: at specVersion 31 a markdown_block read stands alone on its line."
        : $"In '{file}', the reads {string.Join(", ", reads)} now start their own lines, after a blank line: at specVersion 31 a markdown_block read stands alone on its line.";

    /// <summary>
    /// A10-094 (#1543, ruling 2, D7, as the owner amended it on 2026-11-27, item 4, as the controller extended it in Task
    /// A5's review, for the owner): a reason: the <c>markdown_block</c> read <paramref name="read"/> at line
    /// <paramref name="line"/> of the body stands in a line that a blank line would break out of its Markdown block (the
    /// text before it starts a list item, a quote or a heading; the text after it would start one, or indented code), so the
    /// migration leaves the line as 30 wrote it. S10-002 gives it.
    /// </summary>
    public static string ReadBreaksItsBlock(string read, int line) =>
        string.Create(CultureInfo.InvariantCulture, $"the read {read} at line {line} stands in a line that a blank line would break out of its Markdown block (a list item, a quote, a heading or indented text), and at specVersion 31 a markdown_block read stands alone on its line; rewrite the line.");

    /// <summary>
    /// A10-084 (#1508, the final review of Part F; Q3, R15; #1543, ruling 3, D11): a reason: the deliverable's 30 condition is
    /// a hand edit (the converter refused it), and a source may not run, so A10-079's <paramref name="term"/> (the canonical
    /// JSON Logic that says each source that may not run ran: <c>&lt;source&gt;.state === "completed"</c>, <c>and</c> over
    /// those sources; node keys only) is not joined to it: the operator writes 30's condition <c>and</c> the term, or 31
    /// releases what 30 skipped.
    /// </summary>
    public static string JoinASourceRan(string term) =>
        $"a source may not run, and 30 skipped the deliverable when one did not; join {term} to 30's condition by and.";

    /// <summary>
    /// A10-086 (#1508, 2026-11-26, item 8; R15, F-D13; #1543, ruling 3, D11): a reason: a source of the deliverable may not
    /// run, and the migration cannot read whether the <paramref name="unread"/> ones ran (a node it does not know, or one
    /// whose run the deliverable's <c>if</c> cannot read), so it writes no <c>if</c> and the operator writes the one that skips
    /// the deliverable as 30 did; <paramref name="others"/>, when given, is the term (canonical JSON Logic) that says the
    /// other sources that may not run ran.
    /// </summary>
    public static string SourceRunNotRead(IReadOnlyList<string> unread, string? others) =>
        $"a source may not run, and the migration cannot read whether {string.Join(", ", unread)} ran; write the if that skips the deliverable when a source did not run, as 30 skipped it"
        + (others is null ? "." : $", joining {others} for the others.");

    /// <summary>
    /// A10-080 (#1508, comment 7, Q5; R17; part 10 § 1.4): a reason: the template reads <paramref name="path"/> at
    /// <paramref name="line"/> through <paramref name="through"/>, which can be null (A11-009 refuses it at publish), at a
    /// place the dialect holds no <c>if</c> (a part's text, A07-002; a test or an argument): the migration cannot write the
    /// guard there.
    /// </summary>
    public static string NoGuardHere(string path, string through, int line) =>
        $"the template reads {path} at line {line.ToString(CultureInfo.InvariantCulture)} through {through}, which can be null, where no {{{{ if {through} }}}} can enclose the read; move the read where that guard can stand.";

    /// <summary>
    /// A10-081 (#1508, comment 7, B2, and comment 8; R11; part 10 § 1.4): a reason: the migrated package's worst case opens
    /// <paramref name="count"/> instances through <paramref name="loop"/>, past <paramref name="limit"/>, the engine's
    /// default (<c>RunLimits.JobInstances</c>), so publish refuses it (A11-001); the migration guesses no <c>max</c>. The
    /// numbers are grouped as A11-001 groups them.
    /// </summary>
    public static string PastTheInstanceLimit(long count, string loop, int limit) =>
        $"up to {count.ToString("N0", CultureInfo.InvariantCulture)} steps could run in one job through {loop}, and this engine runs at most {limit.ToString("N0", CultureInfo.InvariantCulture)} for one job, so publish refuses the package; the migration guesses no max: lower a loop's max or split the workflow.";

    /// <summary>
    /// A10-077 (#1508, comment 6, item 1; R13; part 10 § 1.4): a reason: the <c>judgement</c> node <paramref name="node"/>
    /// the migration wrote decides whether macro <paramref name="macro"/> is offered, which 30's model chose from the
    /// instruction; its prompt ends with an empty question line, which publish refuses (A11-013), and the instruction's
    /// "otherwise" clause cannot be found reliably by code.
    /// </summary>
    public static string WriteTheQuestion(string node, string macro) =>
        $"node '{node}' decides whether macro '{macro}' is offered, which 30's model chose; write its yes-or-no question on the prompt's last line, and remove the \"otherwise\" clause from the macro's instruction.";

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
    /// A10-067 (R7, part 10 § 1.3, A10-044; #1507 final review A3): a notice: the optional macro <paramref name="macro"/> is
    /// offered by the input <paramref name="input"/>, but its <c>if</c> also reads another boolean input, so a 31 start
    /// refuses a <c>MacroChoices</c> entry keyed by the macro's id; the caller sends the input itself.
    /// </summary>
    public static string ChoiceRefusedAtStart(string macro, string input) =>
        $"Macro '{macro}' is chosen per run by the input '{input}', but its if reads another boolean input, so a MacroChoices entry for '{macro}' is refused at 31; send the input '{input}' in Inputs instead.";

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

    /// <summary>
    /// A10-051 (part 05 § 6.2, A07-012; R6): a reason: a snippet folder's item body places macro <paramref name="macro"/>
    /// at <paramref name="line"/> (its 30 <c>.prompt</c>, a hoisted body <c>if</c>'s condition already the macro's
    /// <c>if</c>), which a folder item's body read in a loop does not do at 31.
    /// </summary>
    public static string FolderItemPlacesMacro(string macro, int line) =>
        string.Create(CultureInfo.InvariantCulture, $"macro '{macro}' is placed at line {line}, but a snippet folder's item read in a loop places no macro at 31; place it in the prompt that loops over the folder.");

    /// <summary>
    /// A10-053 (R9, D20; part 06 § 6.4): a reason: the <paramref name="kind"/> node <paramref name="flow"/> a deliverable
    /// composes through no <c>for</c> returns 30 prose (<c>markdown_block</c>) from one branch and plain text from another,
    /// a union a <c>markdown_block</c> deliverable escapes as a string, the Markdown with it (under a <c>for</c> the
    /// deliverable reads the loop's <c>.content</c>, which is never escaped: no hand edit, D23).
    /// </summary>
    public static string ComposedBranchesMix(string kind, string flow) =>
        $"the {kind} '{flow}' that a deliverable composes returns Markdown from one branch and plain text from another, which the deliverable would escape as one string; give its branches one text format, then write that format here and on each flow that returns this one.";

    /// <summary>
    /// A10-066 (part 04 § 3.3, A06-002, the owner's ruling 4 on #1507; #1507 final review A2): a reason: a 30 while declares
    /// <c>checkFirst</c> true and its check reads the nodes <paramref name="nodes"/> of its own body, which have no value
    /// before the first pass. <c>checkFirst</c> is written as 30 had it, so the checker refuses it (A06-002).
    /// </summary>
    public static string CheckFirstReadsBody(IReadOnlyList<string> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return $"the check reads the loop's own nodes ({string.Join(", ", nodes)}), which have no value before the first pass; check after the pass, or read a value from outside the loop.";
    }

    /// <summary>
    /// S10-001 (part 10 § 1.4): the script's word when it wrote the migrated package and hand edits remain (its exit 1).
    /// </summary>
    public const string WrittenHandEditsRemain = "written; hand edits remain";

    /// <summary>A10-011 (part 10 § 4, D13): a notice: the package has hand edits, so no snapshot test was written.</summary>
    public const string NoSnapshotHandEditsRemain = "No snapshot test was written: hand edits remain.";

    /// <summary>
    /// A10-060 (part 07 § 8.4, part 01 § 7; D13): a notice: the snapshot test would take the package past
    /// <c>SourceBounds.&lt;bound&gt;</c> (<paramref name="bound"/>: <c>fileBytes</c>, <c>totalBytes</c> or <c>files</c>), so
    /// it was not written. Never a hand edit: the package publishes without it.
    /// </summary>
    public static string SnapshotPastBound(string bound) =>
        $"No snapshot test was written: tests/migration_snapshot.json would take the package past SourceBounds.{bound}.";

    /// <summary>
    /// A10-061 (part 10 § 4, part 07 § 8.3; D13): a notice: the migrated package's snapshot could not be made, <paramref name="how"/>
    /// being <see cref="DoesNotPublish"/>, <see cref="RefusedAtStart"/>, <see cref="FailsItsRun"/>, <see cref="TimesOut"/> or
    /// <see cref="CannotRun"/>, so no snapshot was written. Never a hand edit; it names no value (a checker sentence is package text).
    /// </summary>
    public static string SnapshotNotRun(string how) => $"No snapshot test was written: the migrated package {how}.";

    /// <summary>A10-061's form for a package that does not publish: the checker's <paramref name="sentences"/>, in order, after a colon.</summary>
    public static string DoesNotPublish(IEnumerable<string> sentences) => "does not publish: " + string.Join(" ", sentences);

    /// <summary>A10-061's form for a run whose start refused the fictional request.</summary>
    public const string RefusedAtStart = "is refused at its run's start";

    /// <summary>A10-061's form for a run that ended failed.</summary>
    public const string FailsItsRun = "fails its offline run";

    /// <summary>A10-061's form for a run that timed out (the offline clock never moves: not reached today).</summary>
    public const string TimesOut = "times out in its offline run";

    /// <summary>A10-061's form for a plan that does not build, or a fault on the way to the snapshot.</summary>
    public const string CannotRun = "cannot be run offline";

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
