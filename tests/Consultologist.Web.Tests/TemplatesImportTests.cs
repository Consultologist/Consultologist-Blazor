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
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static void Upload(IRenderedComponent<Templates> page, byte[] bytes, string name) =>
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, name));

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
}
