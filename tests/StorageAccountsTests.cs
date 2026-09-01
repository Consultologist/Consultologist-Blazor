using Consultologist.Api;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Tests;

/// <summary>
/// #596: the naming rule in executable form — the derived URIs equal the
/// live accounts exactly (this file may name them: the tripwire scans
/// docs/scripts/src, and src carries only the rule's parts, never a full
/// name), and the section→role map can never quietly land a store on the
/// wrong account.
/// </summary>
public class StorageAccountsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build();

    [Theory]
    [InlineData("text", "blob", "https://consulttextcaeast.blob.core.windows.net")]
    [InlineData("text", "table", "https://consulttextcaeast.table.core.windows.net")]
    [InlineData("jobrecs", "table", "https://consultjobrecscaeast.table.core.windows.net")]
    [InlineData("jobrecs", "blob", "https://consultjobrecscaeast.blob.core.windows.net")]
    [InlineData("pub", "blob", "https://consultpubcaeast.blob.core.windows.net")]
    public void TheRule_NamesTheLiveAccounts_Exactly(string role, string service, string expected) =>
        Assert.Equal(expected, StorageAccounts.UriFor(role, service, "caeast"));

    [Fact]
    public void TheRule_FitsEveryDirection_TheExecutable24CharProof()
    {
        // The worst fictitious case from the naming decision: a digit'd
        // central region. The account part must fit Azure's 24-char cap.
        var uri = StorageAccounts.UriFor("jobrecs", "table", "uscentral2");
        var account = new Uri(uri).Host.Split('.')[0];

        Assert.Equal("consultjobrecsuscentral2", account);
        Assert.True(account.Length <= 24);
    }

    [Theory]
    [InlineData("AccountStorage", "jobrecs")]
    [InlineData("TextStorage", "text")]
    [InlineData("WorkflowPackages", "jobrecs")]
    public void TheSectionMap_ReadsTheRightAccount(string section, string role) =>
        Assert.Equal(role, StorageAccounts.RoleForSection(section));

    [Fact]
    public void AnUnknownSection_DerivesNothing()
    {
        Assert.Null(StorageAccounts.RoleForSection("LinkedInStateStorage"));
        Assert.Null(StorageAccounts.DerivedUriForSection(Config(("Storage:Region", "caeast")), "LinkedInStateStorage", "table"));
    }

    [Fact]
    public void NoRegion_DerivesNothing_TheChainFallsThroughUnchanged()
    {
        Assert.Null(StorageAccounts.DerivedUri(Config(), "text", "blob"));
        Assert.Null(StorageAccounts.DerivedUriForSection(Config(), "TextStorage", "table"));
    }

    [Fact]
    public void TheRegionSet_DerivesTheSectionsUri()
    {
        var config = Config(("Storage:Region", "caeast"));

        Assert.Equal("https://consultjobrecscaeast.table.core.windows.net",
            StorageAccounts.DerivedUriForSection(config, "AccountStorage", "table"));
        Assert.Equal("https://consulttextcaeast.table.core.windows.net",
            StorageAccounts.DerivedUriForSection(config, "TextStorage", "table"));
    }
}
