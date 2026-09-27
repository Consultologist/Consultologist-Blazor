using System.Text.Json;
using Bunit;
using Consultologist.PackageFormat;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.Workflow;
using Microsoft.AspNetCore.Components.Forms;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #859: Profile bulk import — the reverse of the multi-package export. An uploaded
/// export stages each package with an editable slug; importing publishes each as a
/// new package (FromUpload) and restores the consult pin so a restore never changes
/// what the account's consults run.
/// </summary>
public class ProfileBulkImportTests : ClientRenderTestContext
{
    private const string PinKey = "consult.workflowPackage";

    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithAccount() =>
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() },
            AccountKind: "organisation"));

    private static string Manifest(string name, string version) =>
        $$"""{"name":"{{name}}","version":"{{version}}","specVersion":7}""";

    private static byte[] TwoPackageExport() =>
        WorkflowPackageArchive.Zip(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acct-a1b2/triage/manifest.json"] = Manifest("acct-a1b2/triage", "v2026.09.1"),
            ["acct-a1b2/triage/prompts/draft.md"] = "triage",
            ["acct-a1b2/discharge/manifest.json"] = Manifest("acct-a1b2/discharge", "v2026.08.1"),
            ["acct-a1b2/discharge/prompts/draft.md"] = "discharge",
        });

    private static void Upload(IRenderedComponent<Profile> page, byte[] bytes, string name) =>
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, name));

    [Fact]
    public void UploadingAnExport_StagesEachPackage_WithASlugDerivedFromItsLeaf()
    {
        WithAccount();
        var page = Render<Profile>();

        Upload(page, TwoPackageExport(), "packages.zip");

        page.WaitForAssertion(() =>
        {
            var slugs = page.FindAll("input[aria-label^='Name for']");
            Assert.Equal(2, slugs.Count);
            Assert.Contains(slugs, s => s.GetAttribute("value") == "triage");
            Assert.Contains(slugs, s => s.GetAttribute("value") == "discharge");
        });
    }

    [Fact]
    public void Importing_PublishesEachFromUpload_PreservesThePin_AndShowsOutcomes()
    {
        WithAccount();
        AccountService.GetSettingAsync(PinKey).Returns(
            new AccountSettingResponse(PinKey, "acct-you/general@v2026.09.3", "text/plain", DateTimeOffset.UtcNow));

        var sent = new List<WorkflowPackagePublishRequest>();
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(r => sent.Add(r)))
            .Returns(ci =>
            {
                var req = ci.Arg<WorkflowPackagePublishRequest>();
                return req.NewPackageSlug == "discharge"
                    ? new WorkflowPublishOutcome(null, new[] { "Schema 'x' matches no contract." })
                    : new WorkflowPublishOutcome(
                        new WorkflowPackagePublishResponse("acct-you/triage", "v2026.09.1", "acct-you/triage@v2026.09.1"),
                        Array.Empty<string>());
            });

        var page = Render<Profile>();
        Upload(page, TwoPackageExport(), "packages.zip");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".import-packages-submit")));
        page.Find(".import-packages-submit").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(2, sent.Count);
            Assert.All(sent, r => Assert.True(r.FromUpload));
            Assert.Contains(sent, r => r.NewPackageSlug == "triage");
            Assert.Contains(sent, r => r.NewPackageSlug == "discharge");
            // The pin is snapshotted and restored — a restore never moves it.
            AccountService.Received().SaveSettingAsync(PinKey, "acct-you/general@v2026.09.3", "text/plain");
            // Per-package outcomes render (one success ref, one failure finding).
            Assert.Contains("acct-you/triage@v2026.09.1", page.Markup);
            Assert.Contains("matches no contract", page.Markup);
        });
    }

    [Fact]
    public void ABadZip_ShowsAnError_AndStagesNothing()
    {
        WithAccount();
        var page = Render<Profile>();

        Upload(page, new byte[] { 1, 2, 3, 4 }, "bad.zip");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("not a valid .zip", page.Find(".import-error").TextContent);
            Assert.Empty(page.FindAll("input[aria-label^='Name for']"));
        });
    }
}
