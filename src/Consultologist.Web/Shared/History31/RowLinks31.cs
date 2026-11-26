namespace Consultologist.Web.Shared.History31;

/// <summary>
/// #1509 (Task 17 fix; Task 18): the one way a link to a row (<c>#row-&lt;key&gt;</c>) is followed inside History's record
/// view. With <c>&lt;base href="/"&gt;</c> a bare fragment link navigates to the home page, so a click is handled here:
/// <see cref="Reveal"/> opens the rows the target sits in (Nodes' own mechanism) and answers whether it did; when it did
/// not (a row of a section that does not open), the fragment is followed on this page instead.
/// </summary>
public sealed class RowLinks31
{
    /// <summary>Opens the target's rows in the section that holds it; true when it was found there.</summary>
    public required Func<string, bool> Reveal { get; init; }
}
