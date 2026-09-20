using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #806: Profile exports every package the account owns as one zip, a subfolder
/// per package (its latest version). Fully client-side — list then fetch each —
/// so these stub the two service calls and decode the recorded download.
/// </summary>
public class RegistryDownloadProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithAccount() =>
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() },
            AccountKind: "organisation"));

    private static WorkflowPackageContentResponse Package(string name, string version, params (string Path, string Text)[] files) =>
        new(name, version, 7,
            JsonDocument.Parse($$"""{ "name": "{{name}}", "version": "{{version}}", "specVersion": 7 }""").RootElement.Clone(),
            files.ToDictionary(f => f.Path, f => f.Text, StringComparer.Ordinal));

    private static PublicPackageView Listed(string name, string latest) =>
        new(name, latest, new List<string> { latest });

    private Dictionary<string, string> LastDownloadedZip()
    {
        var bytes = Convert.FromBase64String((string)JSInterop.Invocations["consultologistDownload"].Last().Arguments[2]!);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            },
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task DownloadMyPackages_ZipsASubfolderPerPackage()
    {
        WithAccount();
        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            Listed("acct-1234567890ab-triage", "v2026.09.1"),
            Listed("acct-1234567890ab-discharge", "v2026.08.1")
        });
        WorkflowService.GetCurrentPackageContentAsync("acct-1234567890ab-triage@v2026.09.1")
            .Returns(Package("acct-1234567890ab-triage", "v2026.09.1", ("prompts/draft.md", "Triage draft.")));
        WorkflowService.GetCurrentPackageContentAsync("acct-1234567890ab-discharge@v2026.08.1")
            .Returns(Package("acct-1234567890ab-discharge", "v2026.08.1", ("data/standards/hpi.md", "History.")));

        var page = Render<Profile>();
        await page.Find(".download-packages-button").ClickAsync(new());

        var zip = LastDownloadedZip();
        Assert.Contains("acct-1234567890ab-triage/manifest.json", zip.Keys);
        Assert.Equal("Triage draft.", zip["acct-1234567890ab-triage/prompts/draft.md"]);
        Assert.Contains("acct-1234567890ab-discharge/manifest.json", zip.Keys);
        Assert.Equal("History.", zip["acct-1234567890ab-discharge/data/standards/hpi.md"]);

        using var manifest = JsonDocument.Parse(zip["acct-1234567890ab-triage/manifest.json"]);
        Assert.Equal("acct-1234567890ab-triage", manifest.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnUnreadablePackage_IsNamedAndSkipped_NotFatal()
    {
        WithAccount();
        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            Listed("acct-1234567890ab-good", "v2026.09.1"),
            Listed("acct-1234567890ab-gone", "v2026.09.1")
        });
        WorkflowService.GetCurrentPackageContentAsync("acct-1234567890ab-good@v2026.09.1")
            .Returns(Package("acct-1234567890ab-good", "v2026.09.1", ("prompts/draft.md", "Good.")));
        WorkflowService.GetCurrentPackageContentAsync("acct-1234567890ab-gone@v2026.09.1")
            .Returns<WorkflowPackageContentResponse>(_ => throw new HttpRequestException("404"));

        var page = Render<Profile>();
        await page.Find(".download-packages-button").ClickAsync(new());

        // The good one still shipped; the failure is named, not thrown.
        Assert.Contains("acct-1234567890ab-good/manifest.json", LastDownloadedZip().Keys);
        Assert.Contains("could not be read", page.Find(".packages-message").TextContent);
        Assert.Contains("acct-1234567890ab-gone", page.Find(".packages-message").TextContent);
    }

    [Fact]
    public void WithNoPackages_TheButtonIsDisabled()
    {
        WithAccount();
        // GetMyPackagesAsync defaults to null in the harness -> no packages.

        var page = Render<Profile>();

        Assert.True(page.Find(".download-packages-button").HasAttribute("disabled"));
    }
}
