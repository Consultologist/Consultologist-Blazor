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
}
