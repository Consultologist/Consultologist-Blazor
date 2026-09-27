using System.Text.Json;
using Bunit;
using Consultologist.PackageFormat;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
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

    // #852: with the real catalog fetched, an imported package whose schema matches
    // the catalog validates cleanly — no false-positive "must canonically match" warning.
    [Fact]
    public void ImportingASchemaPackage_WithTheCatalog_ShowsNoSchemaWarning()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        WorkflowService.GetCatalogSchemasAsync().Returns(
            (IReadOnlyDictionary<string, string>?)new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["concept-list"] = EditorCatalogSchemas.ConceptListSchema
            });

        Upload(page, PackageZip(EditorFixtures.V12Full()), "schema-pkg.zip");

        Assert.Contains("Imported", page.Markup);
        Assert.DoesNotContain("must canonically match a catalog output contract", page.Markup);
    }

    // #852: if the catalog can't be fetched, the schema check can't be judged, so the
    // false-positive warning is suppressed (not shown) — the server publish is the gate.
    [Fact]
    public void ImportingASchemaPackage_WithoutTheCatalog_SuppressesTheSchemaWarning()
    {
        var page = RenderEditor(EditorFixtures.V12Full());
        WorkflowService.GetCatalogSchemasAsync().Returns((IReadOnlyDictionary<string, string>?)null);

        Upload(page, PackageZip(EditorFixtures.V12Full()), "schema-pkg.zip");

        Assert.Contains("Imported", page.Markup);
        Assert.DoesNotContain("must canonically match a catalog output contract", page.Markup);
    }

    // #855: the Workflow graph is drawn from the IMPORTED manifest (via the server
    // generator), not fetched by ref — an import has no registry ref, so a by-ref
    // fetch would return the pinned/default package's stale diagram.
    [Fact]
    public void ImportingAPackage_DrawsTheGraphFromTheImportedManifest_NotByRef()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        WorkflowService.GetDiagramForManifestAsync(Arg.Any<JsonElement>()).Returns("graph TD; imported");
        WorkflowService.GetCurrentDiagramAsync(Arg.Any<string?>()).Returns("graph TD; pinned");

        Upload(page, PackageZip(EditorFixtures.V11Macro()), "imported.zip");

        // LoadDiagramAsync is fired as `_ = …` on import, so wait for it to settle.
        page.WaitForAssertion(() =>
            Assert.Equal("graph TD; imported", page.FindComponent<WorkflowDagView>().Instance.Diagram));
    }

    // #855: Refresh on an unedited import also redraws from the manifest (no by-ref
    // fetch, no pending decoration — the import IS the content).
    [Fact]
    public void RefreshingAnImportedPackage_KeepsTheManifestDiagram()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        WorkflowService.GetDiagramForManifestAsync(Arg.Any<JsonElement>()).Returns("graph TD; imported");
        WorkflowService.GetCurrentDiagramAsync(Arg.Any<string?>()).Returns("graph TD; pinned");

        Upload(page, PackageZip(EditorFixtures.V11Macro()), "imported.zip");
        page.WaitForAssertion(() =>
            Assert.Equal("graph TD; imported", page.FindComponent<WorkflowDagView>().Instance.Diagram));

        page.FindAll("button").First(b => b.TextContent.Contains("Refresh")).Click();

        page.WaitForAssertion(() =>
            Assert.Equal("graph TD; imported", page.FindComponent<WorkflowDagView>().Instance.Diagram));
    }

    // #857: importing persists the uploaded .zip to localStorage so it survives a
    // reload (keyed by a dedicated import key, since an import has no registry ref).
    [Fact]
    public void ImportingAPackage_PersistsItToLocalStorage()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());

        Upload(page, PackageZip(EditorFixtures.V11Macro()), "imported.zip");

        var setItems = JSInterop.Invocations["localStorage.setItem"];
        Assert.Contains(setItems, i => (i.Arguments[0] as string) == "workflow-editor-import");
        var importSet = setItems.Last(i => (i.Arguments[0] as string) == "workflow-editor-import");
        Assert.Contains("ZipBase64", importSet.Arguments[1] as string ?? string.Empty);
    }

    // #857: on a cold load, a held import is restored in place of the registry
    // package — the reason the feature exists.
    [Fact]
    public void AStoredImport_IsRestoredOnLoad_NotTheRegistryPackage()
    {
        // The registry would serve V11Macro; the held import is the same package
        // but carrying a marker only the uploaded copy has.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V11Macro());
        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            new PublicPackageView("acct-1234567890ab-existing", "v2026.08.1", new List<string> { "v2026.08.1" })
        });

        var zip = PackageZip(EditorFixtures.V11Macro(),
            ("prompts/draft-section.md", "IMPORTED-MARKER {{ consult_draft }}"));
        var draftJson = JsonSerializer.Serialize(new
        {
            Version = 1,
            ZipBase64 = Convert.ToBase64String(zip),
            NewPackageSlug = string.Empty
        });
        JSInterop.Setup<string?>("localStorage.getItem", "workflow-editor-import").SetResult(draftJson);

        var page = Render<Templates>();

        Assert.Contains("held locally in this browser", page.Markup);
        // The name field is open (an import is ready to be named and published).
        Assert.NotNull(page.Find("input[aria-label='New package path']"));
        // The editor is reading the imported files, not the registry copy.
        Navigate(page, "draft-section");
        Assert.Contains("IMPORTED-MARKER", page.Markup);
    }

    // #857: a corrupt stored import must never wedge the editor — it falls back to
    // the registry package.
    [Fact]
    public void ACorruptStoredImport_FallsBackToTheRegistry()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V11Macro());
        WorkflowService.GetMyPackagesAsync().Returns(new[]
        {
            new PublicPackageView("acct-1234567890ab-existing", "v2026.08.1", new List<string> { "v2026.08.1" })
        });
        JSInterop.Setup<string?>("localStorage.getItem", "workflow-editor-import").SetResult("not json at all");

        var page = Render<Templates>();

        Assert.DoesNotContain("held locally in this browser", page.Markup);
        Assert.DoesNotContain("Imported", page.Markup);
    }

    // #857: publishing a held import turns it into a package — the held copy is dropped.
    [Fact]
    public void PublishingAnImport_ClearsTheStoredImport()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        CapturePublish();

        Upload(page, PackageZip(EditorFixtures.V11Macro()), "imported.zip");
        page.Find("input[aria-label='New package path']").Input("my/imported-copy");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish as new package")).Click();

        page.WaitForAssertion(() => Assert.Contains(
            JSInterop.Invocations["localStorage.removeItem"],
            i => (i.Arguments[0] as string) == "workflow-editor-import"));
    }

    // #857: Discard is enabled for an unedited import (which has nothing "pending")
    // and drops the held import, returning to the registry package.
    [Fact]
    public void DiscardingAnUneditedImport_ClearsTheStoredImport_AndReturnsToRegistry()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());

        Upload(page, PackageZip(EditorFixtures.V11Macro(),
            ("prompts/draft-section.md", "IMPORTED-MARKER {{ consult_draft }}")), "imported.zip");
        Navigate(page, "draft-section");
        Assert.Contains("IMPORTED-MARKER", page.Markup);

        // #770: Discard arms on the first click, acts on the second.
        page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Discard").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Confirm discard").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                JSInterop.Invocations["localStorage.removeItem"],
                i => (i.Arguments[0] as string) == "workflow-editor-import");
            // Back on the registry package: the imported marker is gone.
            Assert.DoesNotContain("IMPORTED-MARKER", page.Markup);
        });
    }

    // #858: when the server ingest endpoint returns content, the editor loads that
    // server-normalized content and shows the server's (full, catalog-backed) findings.
    [Fact]
    public void ImportingAPackage_LoadsServerContent_AndShowsServerFindings()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        WorkflowService.IngestPackageAsync(Arg.Any<byte[]>())
            .Returns(new WorkflowIngestOutcome(EditorFixtures.V11Macro(), new List<string> { "server-finding-xyz" }, null));

        Upload(page, PackageZip(EditorFixtures.V11Macro()), "imported.zip");

        Assert.Contains("but it has issues to resolve", page.Markup);
        Assert.Contains("server-finding-xyz", page.Markup);
    }

    // #858: when the endpoint is unavailable (null), the editor falls back to the
    // local unzip — the import still works, reading the uploaded file's own content.
    [Fact]
    public void ImportingAPackage_FallsBackToLocal_WhenIngestUnavailable()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        WorkflowService.IngestPackageAsync(Arg.Any<byte[]>()).Returns((WorkflowIngestOutcome?)null);

        Upload(page, PackageZip(EditorFixtures.V11Macro(),
            ("prompts/draft-section.md", "LOCAL-FALLBACK-MARKER {{ consult_draft }}")), "imported.zip");

        Assert.Contains("Imported", page.Markup);
        Navigate(page, "draft-section");
        Assert.Contains("LOCAL-FALLBACK-MARKER", page.Markup);
    }

    // #858: a "not a package" verdict (400) from the server is shown, and the editor
    // is left unchanged — no local re-parse that would only fail the same way.
    [Fact]
    public void ImportingAPackage_ShowsServerError_AndLeavesEditorUnchanged()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        WorkflowService.IngestPackageAsync(Arg.Any<byte[]>())
            .Returns(new WorkflowIngestOutcome(null, new List<string>(), "That .zip has no manifest.json — it is not a package."));

        Upload(page, PackageZip(EditorFixtures.V11Macro()), "imported.zip");

        Assert.Contains("no manifest.json", page.Markup);
        Assert.DoesNotContain("Imported ", page.Markup);
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
