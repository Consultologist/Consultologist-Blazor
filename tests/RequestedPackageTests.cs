using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #411: the editor asks the content and diagram endpoints for a package by
/// <c>ref</c>, because editing something stopped meaning pinning it. This is
/// the rule that decides which package a read serves, and whether the caller
/// may have it.
///
/// Tested as a static over a Uri because there is no HttpRequestData harness in
/// this repo — every WorkflowPackages function is untested at the function
/// layer, so a rule left inside one would have no coverage at all.
/// </summary>
public class RequestedPackageTests
{
    private const string OwnerId = "0123456789abcdef0123456789abcdef";
    private const string OwnedPackage = "acct-0123456789ab";

    private static WorkflowPackages.RequestedPackage Parse(string query) =>
        WorkflowPackages.ParseRequestedPackage(new Uri($"https://example.test/api/x{query}"), OwnerId);

    [Theory]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("?ref=")]
    [InlineData("?ref=%20")]
    [InlineData("?other=general@v2026.08.1")]
    public void AskingForNothing_MeansThePin(string query)
    {
        // Every client that predates #411 sends no ref, and must keep getting
        // the pin. An empty ref is the same request, not a malformed one.
        var requested = Parse(query);

        Assert.Equal(WorkflowPackages.RequestedPackageKind.Pin, requested.Kind);
        Assert.Null(requested.Ref);
    }

    [Fact]
    public void ARepoOwnedPackage_IsServedToAnyone()
    {
        // This is the fork-from-general path, and it is what the editor loads
        // when somebody selects general to look at it.
        var requested = Parse("?ref=general@v2026.08.1");

        Assert.Equal(WorkflowPackages.RequestedPackageKind.Resolved, requested.Kind);
        Assert.Equal("general", requested.Ref!.Name);
        Assert.Equal("v2026.08.1", requested.Ref.Version);
    }

    [Fact]
    public void YourOwnFork_IsServedToYou()
    {
        var requested = Parse($"?ref={OwnedPackage}@v2026.08.16");

        Assert.Equal(WorkflowPackages.RequestedPackageKind.Resolved, requested.Kind);
        Assert.Equal(OwnedPackage, requested.Ref!.Name);
    }

    [Fact]
    public void SomebodyElsesFork_IsRefused()
    {
        // The whole reason this gate exists. The store performs no access check
        // of its own — it would resolve a foreign fork perfectly happily — so
        // refusing here is the only thing between two accounts.
        var requested = Parse("?ref=acct-999999999999@v2026.07.1");

        Assert.Equal(WorkflowPackages.RequestedPackageKind.Forbidden, requested.Kind);
        Assert.Null(requested.Ref);
    }

    [Fact]
    public void Latest_IsAccepted_UnlikeLineage()
    {
        // Deliberately different from the lineage endpoint, which requires a
        // concrete version. The picker offers `general@latest`, the pin permits
        // it, and the response reports the RESOLVED name and version — so what
        // the client ends up holding is concrete either way.
        var requested = Parse("?ref=general@latest");

        Assert.Equal(WorkflowPackages.RequestedPackageKind.Resolved, requested.Kind);
        Assert.True(requested.Ref!.IsLatest);
    }

    [Theory]
    [InlineData("?ref=general")]
    [InlineData("?ref=general@")]
    [InlineData("?ref=general@nonsense")]
    [InlineData("?ref=@v2026.08.1")]
    [InlineData("?ref=General@v2026.08.1")]
    [InlineData("?ref=general@v2026.08.1@extra")]
    public void AnythingUnparseable_IsMalformedRatherThanTheFallback(string query)
    {
        // Falling back to the pin on a bad ref would be worse than refusing:
        // the caller asked for one package and would silently edit another.
        var requested = Parse(query);

        Assert.Equal(WorkflowPackages.RequestedPackageKind.Malformed, requested.Kind);
        Assert.Null(requested.Ref);
    }
}
