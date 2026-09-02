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

/// <summary>
/// v12 (#621): the desk mirrors every v12 sentence — carried shapes below 12
/// are refused by name with the upgrade pointer (NewestSpecVersion, never
/// the runnable rung), and at 12 the rule mirrors speak the validator's
/// sentences verbatim. One fresh page per fixture.
/// </summary>
public class TemplatesV12DeskMirrorTests : ClientRenderTestContext
{
    private static WorkflowPackageContentResponse Modify(
        WorkflowPackageContentResponse package,
        Action<System.Text.Json.Nodes.JsonObject> mutate)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        mutate(root);
        return package with { Manifest = System.Text.Json.JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    /// <summary>Publish needs a pending change; a whitespace nudge on the standing prompt file is the least one.</summary>
    private void Nudge(WorkflowPackageContentResponse package) =>
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult("{ \"Version\": 14, \"Edits\": { \"prompts/draft-section.md\": \"Draft {{ section_name }} from {{ consult_draft }}. \" } }");

    private IReadOnlyList<string> PublishAndRead(WorkflowPackageContentResponse package)
    {
        Nudge(package);
        WorkflowService.GetCurrentPackageContentAsync().Returns(package);
        var page = Render<Templates>();
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();
        return page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();
    }

    [Fact]
    public void BelowTwelve_EveryCarriedShape_IsRefusedByName_WithThePointer()
    {
        // One v11 manifest carrying the twelve: each key answers with its
        // version requirement and the upgrade pointer — NewestSpecVersion,
        // never the runnable rung.
        var package = Modify(EditorFixtures.V11Macro(), root =>
        {
            root["macros"]![0]!["optional"] = true;
            root["macros"]![0]!["default"] = true;
            root["results"]![0]!["check"] = "node:coverage";
            root["results"]![0]!["macros"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject { ["id"] = "disclaimer", ["after"] = "node:draft-section", ["when"] = "node:scope == in_scope" });
            root["nodes"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = "coverage", ["label"] = "Coverage", ["kind"] = "check", ["op"] = "terms-subset",
                ["of"] = "node:scope", ["in"] = "node:scope", ["failWith"] = "No."
            });
        });

        var refusals = PublishAndRead(package);

        const string pointer = " Use \"Upgrade to specVersion 12\" and publish.";
        Assert.Contains($"Macro 'disclaimer' declares optional, which requires specVersion 12.{pointer}", refusals);
        Assert.Contains($"Macro 'disclaimer' declares default, which requires specVersion 12.{pointer}", refusals);
        Assert.Contains($"Result 'consult_note' places macro 'disclaimer', which requires specVersion 12.{pointer}", refusals);
        Assert.Contains($"Result 'consult_note' gates macro 'disclaimer' with when, which requires specVersion 12.{pointer}", refusals);
        Assert.Contains($"Result 'consult_note' declares check, which requires specVersion 12.{pointer}", refusals);
        Assert.Contains($"Node 'coverage' declares kind 'check', which requires specVersion 12.{pointer}", refusals);
        Assert.Contains($"Node 'coverage' declares op, which requires specVersion 12.{pointer}", refusals);
    }

    [Fact]
    public void AtTwelve_TheOptionalPair_AndThePlacement_SpeakTheValidatorsSentences()
    {
        var noDefault = Modify(EditorFixtures.V12Full(), root =>
        {
            root["macros"]![1]!.AsObject().Remove("default");
        });
        Assert.Contains(
            "Macro 'closing' is optional and declares no default; an optional macro must say what a run that makes no choice does.",
            PublishAndRead(noDefault));

        var bothAnchors = Modify(EditorFixtures.V12Full(), root =>
        {
            root["results"]![0]!["macros"]![0]!["before"] = "node:draft-section";
        });
        Assert.Contains(
            "Result 'consult_note' places macro 'disclaimer' with both before and after; a placement names exactly one.",
            PublishAndRead(bothAnchors));

        var badAnchor = Modify(EditorFixtures.V12Full(), root =>
        {
            root["results"]![0]!["macros"]![0]!["after"] = "node:scope";
        });
        Assert.Contains(
            "Result 'consult_note' places macro 'disclaimer' after 'node:scope', which its aggregator 'assemble-note' does not aggregate.",
            PublishAndRead(badAnchor));
    }

    [Fact]
    public void AtTwelve_TheCheckShape_AndTheOrphan_SpeakTheValidatorsSentences()
    {
        var broken = Modify(EditorFixtures.V12Full(), root =>
        {
            var coverage = root["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "coverage")!.AsObject();
            coverage.Remove("op");
            coverage["failWith"] = " ";
            coverage["of"] = "extract-input-terms";
            coverage["in"] = "node:missing-node";
        });
        var refusals = PublishAndRead(broken);
        Assert.Contains("Check 'coverage' declares no op; the operations are terms-subset.", refusals);
        Assert.Contains("Check 'coverage' declares no failWith; a failed check must speak the package's own sentence.", refusals);
        Assert.Contains("Check 'coverage' of 'extract-input-terms' must be a node:<id> reference.", refusals);
        Assert.Contains("Check 'coverage' in references undeclared node 'missing-node'.", refusals);

        var orphan = Modify(EditorFixtures.V12Full(), root =>
        {
            root["results"]![0]!.AsObject().Remove("check");
        });
        Assert.Contains(
            "Check 'coverage' is not named by any result; a check gates a deliverable, or it is dead weight.",
            PublishAndRead(orphan));

        var notACheck = Modify(EditorFixtures.V12Full(), root =>
        {
            root["results"]![0]!["check"] = "node:scope";
        });
        Assert.Contains(
            "Result 'consult_note' check names 'scope', which is not a check node.",
            PublishAndRead(notACheck));
    }

    [Fact]
    public void AtTwelve_TheEntrysWhen_IsJudgedByTheOneGrammar_UnderTheLongerPrefix()
    {
        var badValue = Modify(EditorFixtures.V12Full(), root =>
        {
            root["results"]![0]!["macros"]![0]!["when"] = "node:scope == maybe";
        });
        Assert.Contains(PublishAndRead(badValue), refusal =>
            refusal.StartsWith("Result 'consult_note' macro 'disclaimer' condition compares 'node:scope' to 'maybe'", StringComparison.Ordinal));

        var undeclared = Modify(EditorFixtures.V12Full(), root =>
        {
            root["results"]![0]!["macros"]![0]!["when"] = "include_counseling";
        });
        Assert.Contains(
            "Result 'consult_note' macro 'disclaimer' condition reads undeclared input 'include_counseling'.",
            PublishAndRead(undeclared));
    }

    [Fact]
    public void AtTwelve_TheSignatureInterplay_IsRefusedAtTheDesk()
    {
        // The gated entry's macro carries the token → the conditional-
        // signature sentence; and the signed result carrying a token macro →
        // signed once. The fixture's disclaimer entry is already gated.
        var gatedToken = Modify(EditorFixtures.V12Full(), root =>
        {
            root["results"]![0]!["signature"] = false;
        });
        gatedToken.Files["macros/disclaimer.md"] = "Sincerely, {{profile:signature}}";
        var refusals = PublishAndRead(gatedToken);
        Assert.Contains(
            "Result 'consult_note' gates macro 'disclaimer' with when, and the macro carries {{profile:signature}}; a conditional signature was rejected (#516) and stays rejected.",
            refusals);

        var signedToken = EditorFixtures.V12Full();
        signedToken.Files["macros/closing.md"] = "Yours, {{profile:signature}}";
        // closing is optional in the fixture — the optional-token sentence
        // fires too; the signed-once one is the assertion here.
        Assert.Contains(
            "Result 'consult_note' declares signature and references macro 'closing', which contains {{profile:signature}}; a deliverable is signed once.",
            PublishAndRead(signedToken));

        var templateClaim = Modify(EditorFixtures.V12Full(), root =>
        {
            root["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "patient-header")!["reproducible"] = true;
        });
        Assert.Contains(
            "Template 'patient-header' declares reproducible; a template is deterministic by construction, and the claim is not its to make.",
            PublishAndRead(templateClaim));
    }
}
