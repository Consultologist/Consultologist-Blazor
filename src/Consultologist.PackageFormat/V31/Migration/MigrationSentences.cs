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
}
