namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 2.4, § 3.2): the differences from 30 that a ruling or a spec section
/// declares, so a comparison may mark them expected. Anything not named here is unexpected (the plan's "when in doubt,
/// a difference is unexpected"). Task 4 holds the reasons a converted condition may answer otherwise than 30's
/// evaluator; Task 15 adds the rules for the byte-equivalence comparison.
/// </summary>
public static class ExpectedDifferences
{
    /// <summary>
    /// The reasons a converted condition, <c>case</c> or <c>let</c> may answer otherwise than 30 did on the same case,
    /// each id beside the ruling or spec section it rests on.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ConditionReasons { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["blank"] = "R1 (#1507, 2026-11-20); part 10 § 2.3: '!x' on text is blank(x), which also holds for empty and white-space-only text and for an input not given.",
        ["trim-blank"] = "Part 05 § 7 (the migrated test/general's part if uses blank); #1507, Task 12 fix round 1's ruling: '(x | string.strip) == \"\"' on text is blank(x) (and '!=' is !blank(x)), which also holds for a text that is null at 31 (a node that did not run), where 30 compared it as not empty (A10-050).",
    };
}
