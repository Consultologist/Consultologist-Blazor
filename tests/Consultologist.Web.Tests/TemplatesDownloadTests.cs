using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #808: the Editor downloads the package being edited as a zip of the real
/// directory tree — the same artifact it would publish. The download rides the
/// `consultologistDownload` JS shim as base64; these decode that back into a zip
/// and assert its entries. (JSInterop runs Loose, so the shim call is recorded,
/// not executed.)
/// </summary>
public class TemplatesDownloadTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        return Render<Templates>();
    }

    private (string FileName, Dictionary<string, string> Files) LastDownload()
    {
        var invocation = JSInterop.Invocations["consultologistDownload"].Last();
        var fileName = (string)invocation.Arguments[0]!;
        var bytes = Convert.FromBase64String((string)invocation.Arguments[2]!);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var files = archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            },
            StringComparer.Ordinal);
        return (fileName, files);
    }

    private static void SetEdit(IRenderedComponent<Templates> page, string path, string text)
    {
        var edits = (System.Collections.Generic.IDictionary<string, string>)typeof(Templates)
            .GetField("edits", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(page.Instance)!;
        edits[path] = text;
        page.Render();
    }

    [Fact]
    public async Task DownloadPackage_ZipsTheDirectoryTree()
    {
        var page = RenderEditor(EditorFixtures.V7());

        await page.Find(".download-package-button").ClickAsync(new());

        var (fileName, files) = LastDownload();
        Assert.Equal("acct-1234567890ab@v2026.07.1.zip", fileName);
        Assert.Contains("manifest.json", files.Keys);
        Assert.Contains("prompts/draft-section.md", files.Keys);
        Assert.Contains("data/standards/index.json", files.Keys);
        Assert.Contains("data/standards/hpi.md", files.Keys);

        using var manifest = JsonDocument.Parse(files["manifest.json"]);
        Assert.Equal(7, manifest.RootElement.GetProperty("specVersion").GetInt32());
    }

    [Fact]
    public async Task DownloadPackage_ReflectsPendingEdits()
    {
        // What you download is what you'd publish: a pending edit is in the zip.
        var page = RenderEditor(EditorFixtures.V7());
        SetEdit(page, "prompts/draft-section.md", "Edited prompt body.");

        await page.Find(".download-package-button").ClickAsync(new());

        Assert.Equal("Edited prompt body.", LastDownload().Files["prompts/draft-section.md"]);
    }
}
