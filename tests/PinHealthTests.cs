using Azure;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>#384: every pin against the loaded catalog, grouped by what it resolved to.</summary>
public class PinHealthTests
{
    private const string Catalog = "output-contracts@v2026.07.2";
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Assemble_GroupsAccountsByRef_AndNamesThemOnlyWhereARemedyIsNeeded()
    {
        var pins = new List<(string, string)>
        {
            ("user-b", "general@v2026.08.1"),
            ("user-a", "general@v2026.08.1"),
            ("user-c", "acct-1234567890ab@v2026.08.3"),
            ("user-d", "acct-1234567890ab/x@v2026.08.1")
        };
        var outcomes = new Dictionary<string, (string, string?)>(StringComparer.Ordinal)
        {
            ["general@v2026.08.1"] = (PinHealthStatuses.Healthy, null),
            ["acct-1234567890ab@v2026.08.3"] = (PinHealthStatuses.Stranded, "the catalog moved"),
            ["acct-1234567890ab/x@v2026.08.1"] = (PinHealthStatuses.Unreadable, "RequestFailedException")
        };

        var report = PinHealth.Assemble(Catalog, "abc", pins, outcomes, Now);

        Assert.Equal(Catalog, report.Catalog);
        Assert.Equal("abc", report.Engine);
        Assert.Equal(4, report.Accounts);
        Assert.Equal(
            new[] { "acct-1234567890ab/x@v2026.08.1", "acct-1234567890ab@v2026.08.3", "general@v2026.08.1" },
            report.Pins.Select(pin => pin.Ref));

        var healthy = report.Pins.Single(pin => pin.Ref == "general@v2026.08.1");
        Assert.Equal(PinHealthStatuses.Healthy, healthy.Status);
        Assert.Equal(2, healthy.Accounts);
        Assert.Null(healthy.Reason);
        Assert.Null(healthy.AppUserIds);

        var stranded = report.Pins.Single(pin => pin.Ref == "acct-1234567890ab@v2026.08.3");
        Assert.Equal("the catalog moved", stranded.Reason);
        Assert.Equal(new[] { "user-c" }, stranded.AppUserIds);

        var unreadable = report.Pins.Single(pin => pin.Status == PinHealthStatuses.Unreadable);
        Assert.Equal(new[] { "user-d" }, unreadable.AppUserIds);
    }

    [Fact]
    public void Assemble_ARefWithNoOutcome_IsUnreadableNotHealthy()
    {
        var report = PinHealth.Assemble(Catalog, null, new List<(string, string)> { ("u", "general@latest") },
            new Dictionary<string, (string, string?)>(), Now);

        Assert.Equal(PinHealthStatuses.Unreadable, report.Pins.Single().Status);
        Assert.Equal(new[] { "u" }, report.Pins.Single().AppUserIds);
    }

    [Fact]
    public void Classify_AContentRefusal_IsStrandedWithTheStoresOwnSentence()
    {
        var exception = WorkflowPackageContentException.SchemaUnmatched("acct-1234567890ab@v2026.08.3", "concept-list", Catalog);
        var (status, reason) = PinHealth.Classify(exception);

        Assert.Equal(PinHealthStatuses.Stranded, status);
        Assert.Equal(exception.Message, reason);
        Assert.Contains("the catalog moved", reason);

        var spec = new WorkflowPackageSpecVersionException("general@v2026.01.1", 2, new[] { 5, 6 });
        Assert.Equal((PinHealthStatuses.Stranded, spec.Message), PinHealth.Classify(spec));
    }

    [Fact]
    public void Classify_AnythingElse_IsUnreadableNamedByTypeOnly()
    {
        var exception = new RequestFailedException(403, "https://consultjobrecscaeast.blob.core.windows.net/secret-path");
        var (status, reason) = PinHealth.Classify(exception);

        Assert.Equal(PinHealthStatuses.Unreadable, status);
        Assert.Equal("RequestFailedException", reason);
        Assert.DoesNotContain("blob.core", reason);
    }

    [Fact]
    public async Task Reporter_ResolvesEachDistinctRefOnce_ThroughTheStore()
    {
        var accounts = Substitute.For<IAccountStore>();
        accounts.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<AccountSummary>
        {
            new("user-a", AccountStatuses.Active),
            new("user-b", AccountStatuses.Active),
            new("user-c", AccountStatuses.Pending)
        });
        var settings = new FakeSettingsStore();
        await settings.SaveAsync("user-a", WorkflowPackagePinResolver.PackagePinSettingKey, "acct-1234567890ab@v2026.08.3", "text/plain", CancellationToken.None);
        await settings.SaveAsync("user-b", WorkflowPackagePinResolver.PackagePinSettingKey, "acct-1234567890ab@v2026.08.3", "text/plain", CancellationToken.None);
        var ownership = new FakeOwnership();
        ownership.Records.Add(("user-a", "acct-1234567890ab"));
        ownership.Records.Add(("user-b", "acct-1234567890ab"));
        var resolver = new WorkflowPackagePinResolver(settings, ownership, NullLogger<WorkflowPackagePinResolver>.Instance);

        var store = Substitute.For<IWorkflowPackageStore>();
        store.ResolveAsync(Arg.Is<WorkflowPackageRef>(r => r.ToString() == "acct-1234567890ab@v2026.08.3"), Arg.Any<CancellationToken>())
            .Returns<WorkflowPackage>(_ => throw WorkflowPackageContentException.StampedContractUnknown(
                "acct-1234567890ab@v2026.08.3", "concept-list", "concept-list-v2", "output-contracts@v2026.07.1", Catalog));
        var catalog = OutputContractCatalog.Load(Path.Combine(RepoRoot(), "external", "consultologist-agents", "agents"));

        var report = await new PinHealthReporter(accounts, resolver, store, catalog).RunAsync(CancellationToken.None);

        Assert.Equal(3, report.Accounts);
        Assert.Equal(catalog.ResolvedRef, report.Catalog);
        var stranded = report.Pins.Single(pin => pin.Ref == "acct-1234567890ab@v2026.08.3");
        Assert.Equal(PinHealthStatuses.Stranded, stranded.Status);
        Assert.Contains("which " + Catalog + " no longer carries", stranded.Reason);
        Assert.Equal(new[] { "user-a", "user-b" }, stranded.AppUserIds);
        // user-c stored no pin: it lands on the resolver's default, which the store accepted.
        var healthy = report.Pins.Single(pin => pin.Ref != "acct-1234567890ab@v2026.08.3");
        Assert.Equal("general@latest", healthy.Ref);
        Assert.Equal(PinHealthStatuses.Healthy, healthy.Status);
        Assert.Null(healthy.AppUserIds);
        await store.Received(1).ResolveAsync(Arg.Is<WorkflowPackageRef>(r => r.ToString() == "acct-1234567890ab@v2026.08.3"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheResponse_CarriesExactlyTheReportFields()
    {
        Assert.Equal(
            new[] { "Accounts", "Catalog", "Engine", "GeneratedAtUtc", "Pins" },
            typeof(PinHealthResponse).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "Accounts", "AppUserIds", "Reason", "Ref", "Status" },
            typeof(PinHealthEntry).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
