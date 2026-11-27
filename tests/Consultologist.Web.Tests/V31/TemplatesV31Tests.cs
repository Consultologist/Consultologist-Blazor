using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Bunit;
using Consultologist.PackageFormat;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Services.Workflow.V31;
using Consultologist.Web.Shared.WorkflowEditor;
using Consultologist.Web.Shared.WorkflowEditor.V31;
using Microsoft.AspNetCore.Components.Forms;
using NSubstitute;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 23 (#1509; part 09 § 8.1, § 8.2; D14): the Editor opens a specVersion 31 package read-only, through
/// <see cref="V31PackageView"/>: the bar above the panes, its files listed and shown as stored, <c>resolve.json</c> and
/// <c>lock.json</c> for a version the account owns, the diagram when the content holds one, a download carrying the
/// evidence, and a 31 import opened the same way with its notices. A 30 package still edits.
/// </summary>
public class TemplatesV31Tests : ClientRenderTestContext
{
    private const string Bar = "This package uses the new format. You can view and download it; editing returns in a later release.";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync(Arg.Any<string?>()).Returns(fixture);
        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            new PublicPackageView("acct-1234567890ab", "v2026.12.1", new List<string> { "v2026.12.1" })
        });
        return Render<Templates>();
    }

    private static IReadOnlyList<string> Listed(IRenderedComponent<Templates> page) =>
        page.FindAll(".v31-package__file").Select(button => button.TextContent.Trim()).ToList();

    private static void Open(IRenderedComponent<Templates> page, string entry) =>
        page.FindAll(".v31-package__file").Single(button => button.TextContent.Trim() == entry).Click();

    private static string Shown(IRenderedComponent<Templates> page) => page.Find("pre.v31-package__text").TextContent;

    private (string FileName, Dictionary<string, string> Files) LastDownload()
    {
        var invocation = JSInterop.Invocations["consultologistDownload"].Last();
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
        return ((string)invocation.Arguments[0]!, files);
    }

    private static byte[] Zip31(WorkflowPackageContentResponse fixture)
    {
        var files = new Dictionary<string, string>(fixture.Files, StringComparer.Ordinal)
        {
            ["manifest.json"] = fixture.Manifest.GetRawText(),
            ["resolve.json"] = "{}",
        };
        return WorkflowPackageArchive.Zip(files);
    }

    private static void Upload(IRenderedComponent<Templates> page, byte[] bytes, string name) =>
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, name));

    [Fact]
    public void A31Package_ShowsTheBar()
    {
        var page = RenderEditor(EditorFixtures.V31());

        Assert.Equal(Bar, page.Find(".v31-package__bar").TextContent.Trim());
        Assert.Single(page.FindComponents<V31PackageView>());
        Assert.Empty(page.FindAll(".editor-layout"));
    }

    [Fact]
    public void ItHasNoPublish_Upgrade_Undo_Discard_OrNewPackageControls()
    {
        var page = RenderEditor(EditorFixtures.V31());

        var buttons = page.FindAll("fluent-button, button").Select(button => button.TextContent.Trim()).ToList();
        Assert.DoesNotContain(buttons, text => text.Contains("Publish", StringComparison.Ordinal));
        Assert.DoesNotContain(buttons, text => text.Contains("Upgrade", StringComparison.Ordinal));
        Assert.DoesNotContain(buttons, text => text is "Undo" or "Redo" or "Discard" or "Revert to default");
        Assert.Empty(page.FindAll("input[aria-label='New package path']"));

        // The toolbar keeps the picker, Download and Import only.
        Assert.Single(page.FindComponents<WorkflowPackagePicker>());
        Assert.Single(page.FindAll(".download-package-button"));
        Assert.Contains(buttons, text => text.Contains("Import package", StringComparison.Ordinal));
    }

    [Fact]
    public void EverySourceFile_IsListed_AndOpensReadOnly()
    {
        var fixture = EditorFixtures.V31();
        var page = RenderEditor(fixture);

        var sources = fixture.Files.Keys.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["manifest.json", .. sources, "resolve.json", "lock.json"], Listed(page));

        foreach (var path in sources)
        {
            Open(page, path);
            Assert.Equal(fixture.Files[path], Shown(page));
        }

        Assert.Empty(page.FindAll("textarea"));
        Assert.Empty(page.FindAll("[contenteditable]"));
        Assert.All(page.FindAll("input"), input =>
            Assert.True(input.GetAttribute("type") == "file" || input.Closest(".package-picker") != null,
                $"An input outside the picker and the import: {input.OuterHtml}"));
    }

    [Fact]
    public void TheManifest_IsShownAsPublished_NullsKept()
    {
        var fixture = EditorFixtures.V31();
        var page = RenderEditor(fixture);

        // manifest.json is first and open on load.
        Assert.Equal("manifest.json", Listed(page)[0]);
        Assert.Equal(fixture.Manifest.GetRawText(), Shown(page));
        Assert.Contains("\"derivedFrom\": null", Shown(page));
        Assert.Contains("\"channel\": null", Shown(page));
    }

    [Fact]
    public void ResolveAndLock_AreViewable_ForAVersionTheAccountOwns_AndNotOtherwise()
    {
        var owned = EditorFixtures.V31();
        var page = RenderEditor(owned);

        Open(page, "resolve.json");
        Assert.Equal(JsonSerializer.Serialize(owned.Resolve!.Value, Indented), Shown(page));
        Open(page, "lock.json");
        Assert.Equal(JsonSerializer.Serialize(owned.Lock!.Value, Indented), Shown(page));

        var notMine = RenderEditor(EditorFixtures.V31(name: "example-nested-note"));
        Assert.DoesNotContain("resolve.json", Listed(notMine));
        Assert.DoesNotContain("lock.json", Listed(notMine));
        Assert.Contains("nodes/summary.md", Listed(notMine));
    }

    [Fact]
    public void TheDiagramEntry_AppearsOnlyWithADagFile()
    {
        var without = RenderEditor(EditorFixtures.V31());
        Assert.DoesNotContain("Diagram", Listed(without));

        var fixture = EditorFixtures.V31(withDiagram: true);
        var with = RenderEditor(fixture);
        var listed = Listed(with);
        Assert.Equal("Diagram", listed[^1]);
        // dag.mmd is the diagram, not a source file: it is listed once, as the diagram.
        Assert.DoesNotContain("dag.mmd", listed);

        Open(with, "Diagram");
        Assert.Equal(fixture.Files["dag.mmd"], with.FindComponent<WorkflowDagView>().Instance.Diagram);
    }

    [Fact]
    public async Task Download_Includes_ResolveLockAndThePublishReport()
    {
        var fixture = EditorFixtures.V31();
        var page = RenderEditor(fixture);

        await page.Find(".download-package-button").ClickAsync(new());

        var (fileName, files) = LastDownload();
        Assert.Equal("acct-1234567890ab@v2026.12.1.zip", fileName);
        Assert.Equal(
            new[] { "deliverables/index.json", "deliverables/note.md", "lock.json", "manifest.json", "nodes/index.json", "nodes/summary.md", "publish-report.json", "resolve.json" },
            files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(fixture.Manifest.GetRawText(), files["manifest.json"]);
        Assert.Equal(fixture.Resolve!.Value.GetRawText(), files["resolve.json"]);
        Assert.Equal(fixture.Lock!.Value.GetRawText(), files["lock.json"]);
        Assert.Equal(fixture.PublishReport!.Value.GetRawText(), files["publish-report.json"]);
        Assert.Equal(fixture.Files["nodes/summary.md"], files["nodes/summary.md"]);
    }

    [Fact]
    public async Task Download_OfAMigratedVersion_CarriesNoMigrationReport()
    {
        var page = RenderEditor(EditorFixtures.V31Migrated());

        await page.Find(".download-package-button").ClickAsync(new());

        Assert.DoesNotContain("migration-report.json", LastDownload().Files.Keys);
    }

    [Fact]
    public void PackageDownload31_WritesEachEntryAsTheResponseHoldsIt_AndLeavesOutWhatIsAbsent()
    {
        var imported = EditorFixtures.V31Imported();

        var entries = PackageDownload31.Entries(imported);

        Assert.Equal(["manifest.json", .. imported.Files.Keys.Order(StringComparer.Ordinal)], entries.Select(entry => entry.Path));
        Assert.Equal(imported.Manifest.GetRawText(), System.Text.Encoding.UTF8.GetString(entries[0].Bytes));
    }

    [Fact]
    public void ImportingA31Zip_OpensItReadOnly_WithItsNotices()
    {
        var page = RenderEditor(EditorFixtures.V7());
        var imported = EditorFixtures.V31Imported();
        WorkflowService.IngestPackageAsync(Arg.Any<byte[]>()).Returns(new WorkflowIngestOutcome(imported, new List<string>(), null));

        Upload(page, Zip31(imported), "nested-note.zip");

        Assert.Equal(Bar, page.Find(".v31-package__bar").TextContent.Trim());
        Assert.Contains(
            "This package's resolve.json and lock.json were ignored; publishing writes new ones.",
            page.FindAll(".v31-package__notice").Select(notice => notice.TextContent.Trim()));
        Assert.Empty(page.FindAll(".editor-layout"));
        Assert.Empty(page.FindAll("input[aria-label='New package path']"));
        Assert.DoesNotContain(page.FindAll("fluent-button"), button => button.TextContent.Contains("Publish", StringComparison.Ordinal));
        // An import carries no evidence, and is not a version the account owns.
        Assert.DoesNotContain("resolve.json", Listed(page));
        Assert.Contains("nodes/summary.md", Listed(page));
    }

    [Fact]
    public void AnImportThe31SourceCheckRefuses_ShowsEveryError()
    {
        var page = RenderEditor(EditorFixtures.V7());
        string[] errors = ["The first refusal of the source check.", "The second refusal of the source check."];
        WorkflowService.IngestPackageAsync(Arg.Any<byte[]>())
            .Returns(new WorkflowIngestOutcome(null, new List<string>(), errors[0], errors));

        Upload(page, Zip31(EditorFixtures.V31Imported()), "nested-note.zip");

        Assert.Equal(errors, page.FindAll(".import-errors li").Select(item => item.TextContent.Trim()));
        // The editor is unchanged: the 30 package is still open for editing.
        Assert.Empty(page.FindAll(".v31-package__bar"));
        Assert.Single(page.FindAll(".editor-layout"));
    }

    [Fact]
    public void A30Package_StillEdits()
    {
        var page = RenderEditor(EditorFixtures.V30());

        Assert.Empty(page.FindComponents<V31PackageView>());
        Assert.DoesNotContain(Bar, page.Markup);
        Assert.Single(page.FindAll(".editor-layout"));
        Assert.Contains(page.FindAll("fluent-button"), button => button.TextContent.Contains("Publish new version", StringComparison.Ordinal));
        Assert.Contains(page.FindAll("fluent-button"), button => button.TextContent.Trim() == "Undo");
    }

    [Fact]
    public void ANewPackage_IsCreatedAt30() => Assert.Equal(30, Templates.NewestSpecVersion);
}
