using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Logging.Abstractions;

namespace Consultologist.Api.Tests;

/// <summary>#447: ownership is a record, and a second package is the account's root plus a slug.</summary>
public class PackageNamingWithSlugsTests
{
    private const string OwnerId = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("breast-oncology", true)]
    [InlineData("a", true)]
    [InlineData("v2", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Breast", false)]
    [InlineData("breast_oncology", false)]
    [InlineData("-breast", false)]
    [InlineData("breast-", false)]
    [InlineData("breast oncology", false)]
    public void ASlug_IsTheNameGrammar_ShortAndNotEndingInAHyphen(string? slug, bool valid)
    {
        Assert.Equal(valid, WorkflowPackageNaming.IsValidSlug(slug));
    }

    [Fact]
    public void ASlug_IsAtMostFortyCharacters()
    {
        Assert.True(WorkflowPackageNaming.IsValidSlug(new string('a', WorkflowPackageNaming.MaxSlugLength)));
        Assert.False(WorkflowPackageNaming.IsValidSlug(new string('a', WorkflowPackageNaming.MaxSlugLength + 1)));
    }

    [Fact]
    public void ForAccountWithAPath_IsTheRootThenThePath()
    {
        // #448: the root is the drive; folders follow. A slug is a one-segment path.
        Assert.Equal("acct-0123456789ab/breast-oncology", WorkflowPackageNaming.ForAccount(OwnerId, "breast-oncology"));
        Assert.Equal("acct-0123456789ab/oncology/breast", WorkflowPackageNaming.ForAccount(OwnerId, "oncology/breast"));
        Assert.Throws<ArgumentException>(() => WorkflowPackageNaming.ForAccount(OwnerId, "Breast"));
        Assert.Throws<ArgumentException>(() => WorkflowPackageNaming.ForAccount(OwnerId, "a/b/c/d"));
    }

    [Theory]
    [InlineData("breast", true)]
    [InlineData("oncology/breast", true)]
    [InlineData("a/b/c", true)]
    [InlineData("a/b/c/d", false)]
    [InlineData("oncology//breast", false)]
    [InlineData("/breast", false)]
    [InlineData("breast/", false)]
    [InlineData("Oncology/breast", false)]
    [InlineData("", false)]
    public void APath_IsOneToThreeSlugs(string path, bool valid)
    {
        Assert.Equal(valid, WorkflowPackageNaming.IsValidPath(path));
    }

    [Theory]
    [InlineData("acct-0123456789ab", "0123456789ab")]
    [InlineData("acct-0123456789ab-breast-oncology", "0123456789ab")]
    [InlineData("acct-0123456789ab/breast", "0123456789ab")]
    [InlineData("acct-0123456789ab/oncology/breast", "0123456789ab")]
    [InlineData("acct-0123456789ab/", null)]
    [InlineData("acct-0123456789ab/a/b/c/d", null)]
    [InlineData("acct-0123456789abcd", null)]
    [InlineData("acct-0123456789ab-", null)]
    [InlineData("acct-0123456789AB", null)]
    [InlineData("acct-012345", null)]
    [InlineData("general", null)]
    public void TheAccountRoot_IsReadOffBareAndSluggedNames_AndNothingElse(string name, string? root)
    {
        Assert.Equal(root, WorkflowPackageNaming.AccountRootOf(name));
    }

    [Fact]
    public void AnAccountsPackagesShareItsRoot_SoAListingUnderItFindsThemAll()
    {
        // The MinePackages listing lists the private registry under the root.
        Assert.StartsWith(WorkflowPackageNaming.ForAccount(OwnerId), WorkflowPackageNaming.ForAccount(OwnerId, "x"), StringComparison.Ordinal);
    }
}

public class WorkflowPackageAccessTests
{
    private const string OwnerId = "0123456789abcdef0123456789abcdef";
    private const string OtherId = "99999999999999999999999999999999";

    [Fact]
    public async Task ARepoOwnedName_IsOpenToEveryone()
    {
        var ownership = new FakeOwnership();
        Assert.True(await ownership.CanAccessAsync("general", OtherId, CancellationToken.None));
    }

    [Fact]
    public async Task ARecordedPackage_IsItsOwners_AndNobodyElses()
    {
        var ownership = new FakeOwnership();
        ownership.Records.Add((OwnerId, "acct-0123456789ab-breast-oncology"));

        Assert.True(await ownership.CanAccessAsync("acct-0123456789ab-breast-oncology", OwnerId, CancellationToken.None));
        Assert.False(await ownership.CanAccessAsync("acct-0123456789ab-breast-oncology", OtherId, CancellationToken.None));
    }

    [Fact]
    public async Task TheDerivedName_IsNobodys_WithoutARecord()
    {
        // #462: the fallback that owned an account's first package by its
        // name retired once every existing package was recorded. The name
        // says nothing now; only the record does.
        var ownership = new FakeOwnership();

        Assert.False(await ownership.CanAccessAsync("acct-0123456789ab", OwnerId, CancellationToken.None));

        ownership.Records.Add((OwnerId, "acct-0123456789ab"));
        Assert.True(await ownership.CanAccessAsync("acct-0123456789ab", OwnerId, CancellationToken.None));
        Assert.False(await ownership.CanAccessAsync("acct-0123456789ab", OtherId, CancellationToken.None));
    }

    [Fact]
    public async Task ASluggedName_WithNoRecord_IsNobodys()
    {
        // The fallback covers the derived name only: a slugged package exists
        // only through a publish, which records it. Fail closed otherwise.
        var ownership = new FakeOwnership();

        Assert.False(await ownership.CanAccessAsync("acct-0123456789ab-breast-oncology", OwnerId, CancellationToken.None));
    }

    [Fact]
    public async Task ThePinResolver_ResolvesARecordedPackage()
    {
        var settings = new FakeSettingsStore();
        var ownership = new FakeOwnership();
        ownership.Records.Add((OwnerId, "acct-0123456789ab-breast-oncology"));
        await settings.SaveAsync(OwnerId, WorkflowPackagePinResolver.PackagePinSettingKey, "acct-0123456789ab-breast-oncology@v2026.09.1", "text/plain", CancellationToken.None);
        var resolver = new WorkflowPackagePinResolver(settings, ownership, NullLogger<WorkflowPackagePinResolver>.Instance);

        Assert.Equal("acct-0123456789ab-breast-oncology@v2026.09.1", (await resolver.ResolvePinAsync(OwnerId, CancellationToken.None)).ToString());

        // The same pin on an account with no record degrades as a foreign pin does.
        Assert.Equal("general", (await resolver.ResolvePinAsync(OtherId, CancellationToken.None)).Name);
    }
}

public class AccountPackageListingNamesTests
{
    [Fact]
    public void NamesIn_IsEveryNameWithAManifest_Ordinal_Once()
    {
        var names = AccountPackageListing.NamesIn(new[]
        {
            "acct-0123456789ab/v2026.08.1/manifest.json",
            "acct-0123456789ab/v2026.08.1/prompts/x.md",
            "acct-0123456789ab/v2026.08.2/manifest.json",
            "acct-0123456789ab/latest.json",
            "acct-0123456789ab-breast-oncology/v2026.09.1/manifest.json",
            "acct-0123456789ab-breast-oncology/v2026.09.1/publish.json",
        });

        Assert.Equal(new[] { "acct-0123456789ab", "acct-0123456789ab-breast-oncology" }, names);
    }

    [Fact]
    public void NamesIn_ReadsANestedPath_AsItself()
    {
        // #447 planted the inverse of this as a tripwire; #448 turned it.
        Assert.Equal(
            new[] { "acct-0123456789ab", "acct-0123456789ab/oncology/breast" },
            AccountPackageListing.NamesIn(new[]
            {
                "acct-0123456789ab/oncology/breast/v2026.09.1/manifest.json",
                "acct-0123456789ab/oncology/breast/latest.json",
                "acct-0123456789ab/v2026.08.1/manifest.json"
            }));
    }

    [Fact]
    public void ANestedName_IsAnOwnerRowKey_WithoutTheSlash()
    {
        // #448: a table key may not hold '/'; '|' may, and no name holds it.
        Assert.Equal("acct-0123456789ab|oncology|breast", PackageOwnerEntity.KeyFor("acct-0123456789ab/oncology/breast"));
        Assert.Equal("acct-0123456789ab/oncology/breast", PackageOwnerEntity.NameOf("acct-0123456789ab|oncology|breast"));
        Assert.Equal("acct-0123456789ab", PackageOwnerEntity.KeyFor("acct-0123456789ab"));
    }
}
