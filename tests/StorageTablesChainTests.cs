using Azure.Core;
using Consultologist.Api;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #596: the construction chain, pinned — explicit URI → derived from the
/// geography → connection string. Construction never calls storage, so the
/// chain itself is pure enough to test.
/// </summary>
public class StorageTablesChainTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build();

    private static readonly TokenCredential Credential = Substitute.For<TokenCredential>();

    [Fact]
    public void AnExplicitUri_BeatsTheDerivedOne()
    {
        var config = Config(
            ("AccountStorage:TableServiceUri", "https://explicitoverride.table.core.windows.net"),
            ("Storage:Region", "caeast"));

        var client = StorageTables.CreateClient(config, Credential, "AppUsers", "AccountStorage");

        Assert.Equal("explicitoverride", client.AccountName);
    }

    [Fact]
    public void TheRegionAlone_NamesTheAccount()
    {
        var config = Config(("Storage:Region", "caeast"));

        var client = StorageTables.CreateClient(config, Credential, "AppUsers", "AccountStorage");

        Assert.Equal("consultjobrecscaeast", client.AccountName);
    }

    [Fact]
    public void TheChain_TriesEverySectionsExplicitUri_BeforeAnyDerivation()
    {
        // The usage store's real chain: its own section first, then the
        // records fallback — an explicit URI on the later section still
        // beats derivation.
        var config = Config(
            ("AccountStorage:TableServiceUri", "https://explicitfallback.table.core.windows.net"),
            ("Storage:Region", "caeast"));

        var client = StorageTables.CreateClient(config, Credential, "AccountUsage", "AccountUsageStorage", "AccountStorage");

        Assert.Equal("explicitfallback", client.AccountName);
    }

    [Fact]
    public void AnUnknownSection_DerivesThroughItsFallbackSection()
    {
        var config = Config(("Storage:Region", "caeast"));

        var client = StorageTables.CreateClient(config, Credential, "LinkedInLinkStates", "LinkedInStateStorage", "AccountStorage");

        Assert.Equal("consultjobrecscaeast", client.AccountName);
    }

    [Fact]
    public void TheTextSection_DerivesTheTextAccount_NeverTheRecords()
    {
        var config = Config(("Storage:Region", "caeast"));

        var client = StorageTables.CreateClient(config, Credential, "ConsultGenerationJobEvents", "TextStorage");

        Assert.Equal("consulttextcaeast", client.AccountName);
    }

    [Fact]
    public void NoRegionAndNoUri_FallsThroughToTheConnectionString_TheLocalDevRung()
    {
        var config = Config(("AzureWebJobsStorage", "UseDevelopmentStorage=true"));

        var client = StorageTables.CreateClient(config, Credential, "AppUsers", "AccountStorage");

        Assert.Equal("devstoreaccount1", client.AccountName);
    }
}
