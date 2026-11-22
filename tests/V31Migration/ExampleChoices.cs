namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10): the choices part 10 makes by hand for the example packages, which
/// <see cref="MigrationFixture.Options"/> passes to the migration. <see cref="Renames"/> is § 1.9's (D6): the reserved
/// <c>parts</c> of <c>example-nested-note</c> becomes <c>sections</c>; every other reserved id takes its kind as a
/// suffix. <see cref="WhenEmpty"/> is § 1.10's (D7): <c>"hold"</c> for <c>test/general</c>'s deliverable,
/// <c>"fail"</c> for <c>example-nested-note</c>'s, and the default <c>"release"</c> for the rest. A package with no
/// entry gets <c>null</c>, the migration's default.
/// </summary>
internal static class ExampleChoices
{
    /// <summary>D6: the hand-chosen renames of <paramref name="package"/>'s reserved ids (30 id to 31 id), or <c>null</c>.</summary>
    public static IReadOnlyDictionary<string, string>? Renames(string package) => package switch
    {
        "example-nested-note" => new Dictionary<string, string>(StringComparer.Ordinal) { ["parts"] = "sections" },
        _ => null,
    };

    /// <summary>D7: <paramref name="package"/>'s deliverables' <c>whenEmpty</c> by deliverable id, or <c>null</c> (every one <c>"release"</c>).</summary>
    public static IReadOnlyDictionary<string, string>? WhenEmpty(string package) => package switch
    {
        "test/general" => new Dictionary<string, string>(StringComparer.Ordinal) { ["consult"] = "hold" },
        "example-nested-note" => new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = "fail" },
        _ => null,
    };
}
