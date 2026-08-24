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
    public void ForAccountWithASlug_IsTheRootPlusTheSlug()
    {
        Assert.Equal("acct-0123456789ab-breast-oncology", WorkflowPackageNaming.ForAccount(OwnerId, "breast-oncology"));
        Assert.Throws<ArgumentException>(() => WorkflowPackageNaming.ForAccount(OwnerId, "Breast"));
    }

    [Theory]
    [InlineData("acct-0123456789ab", "0123456789ab")]
    [InlineData("acct-0123456789ab-breast-oncology", "0123456789ab")]
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
    public async Task TheDerivedName_IsOwnedWithNoRecord_UntilTheFallbackRetires()
    {
        // Every account's first package predates records. The startup
        // backfill writes them; this clause covers the gap and is the
        // follow-up's to remove.
        var ownership = new FakeOwnership();

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
    public void NamesIn_DropsANestedPath_LoudlyHere_RatherThanSilentlyInAPicker()
    {
        // #448 widens the grammar; when it does, this is the assertion that
        // must change first, not the one that lets packages vanish.
        Assert.Empty(AccountPackageListing.NamesIn(new[] { "acct-0123456789ab/oncology/v2026.09.1/manifest.json" }));
    }
}

public class PackageOwnershipBackfillTests
{
    private const string OwnerId = "0123456789abcdef0123456789abcdef";
    private const string OtherId = "99999999999999999999999999999999";

    [Fact]
    public void Plan_MapsBareAndSluggedNamesToTheirAccount_ByRoot()
    {
        var plan = PackageOwnershipBackfill.Plan(
            new[] { "acct-0123456789ab", "acct-0123456789ab-breast-oncology", "acct-999999999999", "general", "acct-0123456789ab" },
            new[] { OwnerId, OtherId });

        Assert.Equal(
            new[]
            {
                (OwnerId, "acct-0123456789ab"),
                (OwnerId, "acct-0123456789ab-breast-oncology"),
                (OtherId, "acct-999999999999")
            },
            plan.Records);
        Assert.Empty(plan.Orphans);
        Assert.Equal(1, plan.RepoOwned);
    }

    [Fact]
    public void Plan_NamesAnOrphan_RatherThanGuessingAnOwner()
    {
        var plan = PackageOwnershipBackfill.Plan(
            new[] { "acct-abcdefabcdef", "acct-0123456789ab-" },
            new[] { OwnerId });

        Assert.Empty(plan.Records);
        Assert.Equal(new[] { "acct-0123456789ab-", "acct-abcdefabcdef" }, plan.Orphans);
    }
}
