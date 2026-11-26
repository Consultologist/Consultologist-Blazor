using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10); #1508 (area 11, Task 29; R18, ruled by the owner, comment 5 item 6): the choices part
/// 10 makes by hand for the example packages, which <see cref="MigrationFixture.Options"/> passes to the migration. They
/// are held in no code: each package's <c>hand-edits/&lt;package&gt;/options.json</c> (<see cref="MigrationOptionsFile"/>)
/// states them, read with <see cref="Migration31Options.FromJson"/>, the file the operator's run reads too. § 1.9's
/// (D6): <c>example-nested-note</c>'s reserved <c>parts</c> becomes <c>sections</c>; § 1.10's (D7): <c>"hold"</c> for
/// <c>test/general</c>'s deliverable, <c>"fail"</c> for <c>example-nested-note</c>'s; part 01 § 3's: <c>test/general</c>'s
/// snippet folder <c>standards</c> types its items with <c>standard_section</c>; <c>{}</c> elsewhere.
/// </summary>
internal static class ExampleChoices
{
    /// <summary>The path of <paramref name="package"/>'s <c>options.json</c>, beside its hand edits.</summary>
    public static string FileOf(string package) => Path.Combine(MigrationFixture.HandEditsOf(package), MigrationOptionsFile.FileName);

    /// <summary><paramref name="package"/>'s <c>options.json</c> as written; fails when the package has none.</summary>
    public static JsonObject Json(string package)
    {
        var path = FileOf(package);
        Assert.True(File.Exists(path), $"{MigrationFixture.HandEditsFolder}/{package}/{MigrationOptionsFile.FileName} is missing: every package states its migration's choices.");
        return JsonNode.Parse(ByteEquivalenceFixtures.ReadText(path))!.AsObject();
    }
}
