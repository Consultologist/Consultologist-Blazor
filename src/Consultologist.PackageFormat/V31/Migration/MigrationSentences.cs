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
}
