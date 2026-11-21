namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1503 (specVersion 31, area 06): the <c>refusal-packages</c> cases of the rules area 06 adds at publish: one for every
/// sentence of <see cref="Consultologist.PackageFormat.V31.ResolveSentences"/> it adds that a package reaches through
/// <see cref="Consultologist.PackageFormat.V31.SourceCheck.Run"/>. As the earlier areas' cases are, each side is
/// <see cref="SourceFixture.Minimal"/> plus only what its rule needs, and each invalid side is its valid side with one
/// text replacement, so it gives exactly one refusal.
/// </summary>
internal static partial class RefusalPackages
{
    private static IEnumerable<RefusalCase> Engine()
    {
        yield return new RefusalCase("A06-002",
            "A while checked before each pass cannot read its own body, which has no value before the first pass (#1507, "
            + "item 4; chapter 4 § 4.3.1, § 4.3.3). The while refine drafts a summary while judge still has something to say. Invalid: "
            + "it declares checkFirst true, and its check reads node.judge. Valid: checkFirst false, the check run after each pass.",
            WithWhile(),
            WithWhile().Edit("nodes/refine/index.json", "\"checkFirst\": false", "\"checkFirst\": true"));
    }
}
