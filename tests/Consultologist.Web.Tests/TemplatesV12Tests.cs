using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v12 rung (e) (#621): the editor authors twelve. This file starts with the
/// carriage: the results writer must carry the entry objects (placement and
/// when) and the deliverable's check, or the first Documents-pane edit
/// erases what a v12 manifest declares — the exact loss class v11 fixed for
/// its own keys.
/// </summary>
public class TemplatesV12Tests : ClientRenderTestContext
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

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    /// <summary>The composed manifest against the REAL validator and the real catalog — the round-trip proof.</summary>
    private Consultologist.PackageFormat.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            EditorCatalogSchemas.CatalogSchemas.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private static JsonElement Result(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("results")[0];

    private void WithDraft(WorkflowPackageContentResponse package, string payloadJson) =>
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult(payloadJson);

    // ----- the carriage: nothing the editor touches erases v12 shapes -----

    [Fact]
    public void AResultsEdit_KeepsThePlacedGatedEntry_AndTheCheck()
    {
        // A relabel forces the wholesale results rewrite — the exact trigger
        // that used to flatten entries to strings and drop the check.
        var package = EditorFixtures.V12Full();
        WithDraft(package, """
            {
              "Version": 14,
              "Results": [ { "Id": "consult_note", "Node": "node:assemble-note", "Label": "Renamed note", "When": "node:scope == in_scope",
                             "Macros": ["disclaimer", "closing"], "Signature": true,
                             "MacroEntries": [ { "Id": "disclaimer", "After": "node:draft-section", "When": "node:scope == in_scope" }, { "Id": "closing" } ],
                             "Check": "node:coverage" } ]
            }
            """);
        CapturePublish();
        var page = RenderEditor(package);

        Publish(page);

        Assert.NotNull(sent);
        var result = Result(sent!);
        Assert.Equal("Renamed note", result.GetProperty("label").GetString());
        Assert.Equal("node:coverage", result.GetProperty("check").GetString());

        var entries = result.GetProperty("macros").EnumerateArray().ToList();
        // The adorned entry stays an OBJECT with its placement and when …
        Assert.Equal(JsonValueKind.Object, entries[0].ValueKind);
        Assert.Equal("disclaimer", entries[0].GetProperty("id").GetString());
        Assert.Equal("node:draft-section", entries[0].GetProperty("after").GetString());
        Assert.Equal("node:scope == in_scope", entries[0].GetProperty("when").GetString());
        // … and the bare entry stays the v11 STRING — byte parity.
        Assert.Equal(JsonValueKind.String, entries[1].ValueKind);
        Assert.Equal("closing", entries[1].GetString());

        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void AnEditOnAnEleven_WritesTheBareStrings_ItAlwaysWrote()
    {
        var package = EditorFixtures.V11Macro();
        WithDraft(package, """
            {
              "Version": 14,
              "Results": [ { "Id": "consult_note", "Node": "node:assemble-note", "Label": "Renamed note", "When": "node:scope == in_scope", "Macros": ["disclaimer"], "Signature": true } ]
            }
            """);
        CapturePublish();
        var page = RenderEditor(package);

        Publish(page);

        var entry = Result(sent!).GetProperty("macros").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.String, entry.ValueKind);
        Assert.Equal("disclaimer", entry.GetString());
    }

    [Fact]
    public void TheReader_SeesEveryTwelveShape()
    {
        var manifest = EditorFixtures.V12Full().Manifest;

        var result = WorkflowManifestReader.ReadResults(manifest).Single();
        Assert.Equal("node:coverage", result.Check);
        var placed = result.Macros![0];
        Assert.Equal(("disclaimer", "node:draft-section", "node:scope == in_scope"), (placed.Id, placed.After, placed.When));
        Assert.False(placed.IsBare);
        Assert.True(result.Macros[1].IsBare);

        var closing = WorkflowManifestReader.ReadMacros(manifest).Single(m => m.Id == "closing");
        Assert.Equal((true, true), (closing.Optional, closing.Default));

        var coverage = WorkflowManifestReader.ReadNodes(manifest).Single(n => n.Id == "coverage");
        Assert.True(coverage.IsCheck);
        Assert.Equal(("terms-subset", "node:extract-input-terms", "node:extract-note-terms"), (coverage.Op, coverage.Of, coverage.In));
        Assert.StartsWith("The note does not cover", coverage.FailWith!);
        Assert.True(WorkflowManifestReader.ReadNodes(manifest).Single(n => n.Id == "patient-header").IsTemplate);
    }
}

/// <summary>
/// v12 § 5 (#621): the desk speaks the placeholder set the staged version
/// speaks — a legal v12 signature token publishes; below 12 it earns the
/// version sentence, never a false "does not resolve"; and the help panel
/// lists the token exactly when the version can spell it.
/// </summary>
public class TemplatesV12SignatureTokenDeskTests : ClientRenderTestContext
{
    /// <summary>The token arrives as a pending file edit — publish needs a
    /// pending change, and the desk judges effective text either way.</summary>
    private void WithTokenEdit(WorkflowPackageContentResponse package) =>
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult("{ \"Version\": 14, \"Edits\": { \"macros/disclaimer.md\": \"Sincerely, {{profile:signature}}\" } }");

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        return Render<Templates>();
    }

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    [Fact]
    public void AtTwelve_TheToken_Publishes()
    {
        // The signed result would double-sign with a token-carrying macro
        // (signed once), so the fixture's signature flag comes off.
        var package = EditorFixtures.V12();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["results"]![0]!["signature"] = false;
        package = package with { Manifest = System.Text.Json.JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
        WithTokenEdit(package);

        WorkflowPackagePublishRequest? sent = null;
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));
        var page = RenderEditor(package);

        Publish(page);

        Assert.NotNull(sent);
        Assert.Empty(Refusals(page));
    }

    [Fact]
    public void AtEleven_TheToken_EarnsTheVersionSentence_NotASpellingOne()
    {
        var package = EditorFixtures.V11Macro();
        WithTokenEdit(package);
        var page = RenderEditor(package);

        Publish(page);

        Assert.Contains(
            "Macro 'disclaimer' placeholder '{{profile:signature}}' requires specVersion 12. Use \"Upgrade to specVersion 12\" and publish.",
            Refusals(page));
        Assert.DoesNotContain(Refusals(page), refusal => refusal.Contains("does not resolve"));
    }

    [Fact]
    public void TheHelpPanel_ListsTheToken_ExactlyWhenTheVersionCanSpellIt()
    {
        var twelve = RenderEditor(EditorFixtures.V12());
        twelve.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == "disclaimer").Click();
        Assert.Contains("{{profile:signature}}", twelve.Find(".macro-placeholders").TextContent);

        var eleven = RenderEditor(EditorFixtures.V11Macro());
        eleven.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == "disclaimer").Click();
        Assert.DoesNotContain("{{profile:signature}}", eleven.Find(".macro-placeholders").TextContent);
    }
}
