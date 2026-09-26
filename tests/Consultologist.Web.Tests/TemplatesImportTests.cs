using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.AspNetCore.Components.Forms;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #847: importing a package .zip (the download's reverse) into the editor — it
/// loads for the session and its files flow to Publish; a bad zip is refused and
/// leaves the editor unchanged.
/// </summary>
public class TemplatesImportTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        // The account already owns a package, so an import offers "Publish as a
        // new package…" (name it) rather than the first-package path (#851).
        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            new PublicPackageView("acct-1234567890ab-existing", "v2026.08.1", new List<string> { "v2026.08.1" })
        });
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static void Upload(IRenderedComponent<Templates> page, byte[] bytes, string name) =>
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, name));

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    // A package .zip in the shape "Download package" emits: content files + manifest.json.
    private static byte[] PackageZip(WorkflowPackageContentResponse fixture, params (string Path, string Text)[] overrides)
    {
        var files = new Dictionary<string, string>(fixture.Files, StringComparer.Ordinal)
        {
            ["manifest.json"] = fixture.Manifest.GetRawText()
        };
        foreach (var (path, text) in overrides)
        {
            files[path] = text;
        }

        return WorkflowPackageArchive.Zip(files);
    }

    [Fact]
    public void ImportingAPackageZip_LoadsItForTheSession()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());

        var zip = PackageZip(EditorFixtures.V11Macro(),
            ("prompts/draft-section.md", "IMPORTED-MARKER {{ consult_draft }}"));

        Upload(page, zip, "acct-1234567890ab@v2026.08.1.zip");

        Assert.Contains("Imported", page.Markup);

        // The editor is reading the imported file, not the originally-loaded one.
        Navigate(page, "draft-section");
        Assert.Contains("IMPORTED-MARKER", page.Markup);
    }

    [Fact]
    public void ImportingANonZip_IsRefused_AndLeavesTheEditorUnchanged()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());

        Upload(page, new byte[] { 1, 2, 3, 4 }, "not-a-package.zip");

        Assert.Contains("not a valid .zip", page.Markup);
        Assert.DoesNotContain("Imported", page.Markup);
    }

    [Fact]
    public void ImportingAZipWithoutAManifest_IsRefused()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());

        var zip = WorkflowPackageArchive.Zip(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prompts/draft.md"] = "No manifest here."
        });

        Upload(page, zip, "no-manifest.zip");

        Assert.Contains("no manifest.json", page.Markup);
        Assert.DoesNotContain("Imported", page.Markup);
    }

    // #849: an imported package is saveable as a new package with no further edit.
    [Fact]
    public void AnImportedPackage_CanBeNamedAndPublishedAsANewPackage_WithoutAnEdit()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        CapturePublish();

        var zip = PackageZip(EditorFixtures.V11Macro(),
            ("prompts/draft-section.md", "IMPORTED-MARKER {{ consult_draft }}"));
        Upload(page, zip, "imported.zip");

        // The name field is open and ready right after import.
        var slug = page.Find("input[aria-label='New package path']");
        slug.Input("my/imported-copy");

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish as new package")).Click();

        Assert.NotNull(sent);
        Assert.Equal("my/imported-copy", sent!.NewPackageSlug);
        Assert.Equal("IMPORTED-MARKER {{ consult_draft }}", sent.Files["prompts/draft-section.md"]);
    }

    // #851: an imported package is never treated as "your existing package" — no
    // "Publish new version" (which would fail the server's target-ownership check);
    // publishing sends FromUpload so the server skips the fork-source gate.
    [Fact]
    public void AnImportedPackage_HidesPublishNewVersion_AndSendsFromUpload()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        CapturePublish();

        Upload(page, PackageZip(EditorFixtures.V11Macro()), "imported.zip");

        Assert.DoesNotContain(page.FindAll("fluent-button"), b => b.TextContent.Contains("Publish new version"));

        page.Find("input[aria-label='New package path']").Input("my/imported-copy");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish as new package")).Click();

        Assert.NotNull(sent);
        Assert.True(sent!.FromUpload);
        Assert.Equal("my/imported-copy", sent.NewPackageSlug);
    }

    // Guard: a normal (non-imported) edit-free package keeps publish disabled.
    [Fact]
    public void ANormalEditFreePackage_KeepsPublishDisabled()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());

        var publish = page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish new version"));
        Assert.True(publish.HasAttribute("disabled"));
    }
}
