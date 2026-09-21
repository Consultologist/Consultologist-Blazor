using System.Collections;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #822 PR 2 (v18): authoring a node's own `when` in the node pane. The editor
/// reuses the deliverable condition builder; a completed condition rides its own
/// nodeWhenEdits dict and composes onto the node's `when`. The affordance is
/// gated to specVersion 18 and hidden on a classifier (which the format refuses
/// a when on).
/// </summary>
public class TemplatesNodeWhenTests : ClientRenderTestContext
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    // The V10Classifier shape at a chosen specVersion, optionally with a `when`
    // already on the draft-section node (a carried/forked manifest).
    private static string Manifest(int specVersion, string? draftSectionWhen = null)
    {
        var when = draftSectionWhen == null ? string.Empty : $""", "when": "{draftSectionWhen}" """;
        return $$"""
            {
              "name": "acct-1234567890ab",
              "version": "v2026.08.1",
              "specVersion": {{specVersion}},
              "tags": [],
              "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
              "inputs": [
                { "id": "consult_draft", "label": "Consult draft", "required": true },
                { "id": "encounter_kind", "label": "Encounter kind", "required": false, "type": "enum", "values": ["new_patient", "follow_up"] }
              ],
              "data": { "standards": "data/standards/" },
              "prompts": [
                { "id": "classify", "file": "prompts/classify.md", "variables": ["referral"] },
                { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["section_name", "consult_draft"] }
              ],
              "results": [
                { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note", "when": "node:scope == in_scope" }
              ],
              "nodes": [
                { "id": "scope", "label": "Is the referral in scope?", "prompt": "classify", "kind": "classifier",
                  "values": ["in_scope", "out_of_scope"], "bindings": { "referral": "input:consult_draft" } },
                { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section", "prompt": "draft-section"{{when}},
                  "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
                { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
              ]
            }
            """;
    }

    private WorkflowPackageContentResponse Fixture(int specVersion, string? draftSectionWhen = null) =>
        EditorFixtures.Package(Manifest(specVersion, draftSectionWhen), specVersion,
            ("prompts/classify.md", "Is this in scope? {{ referral }}"),
            ("prompts/draft-section.md", "{{ section_name }}: {{ consult_draft }}"));

    private IRenderedComponent<Templates> RenderEditor(int specVersion, string? draftSectionWhen = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(Fixture(specVersion, draftSectionWhen));
        return Render<Templates>();
    }

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private static IDictionary NodeWhenEdits(IRenderedComponent<Templates> page) =>
        (IDictionary)typeof(Templates).GetField("nodeWhenEdits", Members)!.GetValue(page.Instance)!;

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private static string? NodeWhenOf(WorkflowPackagePublishRequest request, string nodeId)
    {
        var node = JsonDocument.Parse(request.Manifest.GetRawText()).RootElement
            .GetProperty("nodes").EnumerateArray().First(n => n.GetProperty("id").GetString() == nodeId);
        return node.TryGetProperty("when", out var when) ? when.GetString() : null;
    }

    // ----- gating ----------------------------------------------------------

    [Fact]
    public void At18_ANonClassifierNode_OffersTheRunsWhenEditor()
    {
        var page = RenderEditor(18);
        ShowNode(page, "draft-section");

        Assert.NotEmpty(page.FindAll(".node-when-editor"));
        Assert.NotEmpty(page.FindAll("[aria-label='Condition operand for draft-section']"));
    }

    [Fact]
    public void At18_AClassifierNode_DoesNotOfferIt()
    {
        // The format refuses a when on a classifier; the editor never offers one.
        var page = RenderEditor(18);
        ShowNode(page, "scope");

        Assert.Empty(page.FindAll(".node-when-editor"));
    }

    [Fact]
    public void BelowEighteen_TheEditorIsAbsent()
    {
        var page = RenderEditor(10);
        ShowNode(page, "draft-section");

        Assert.Empty(page.FindAll(".node-when-editor"));
    }

    // ----- composition + round trip ----------------------------------------

    [Fact]
    public void AuthoringANodeWhen_ComposesItOntoTheNode()
    {
        var page = RenderEditor(18);
        CapturePublish();
        ShowNode(page, "draft-section");

        // The same condition builder the deliverable when uses; the node's id
        // is the subject in every control's aria-label.
        page.Find("[aria-label='Condition operand for draft-section']").Change("encounter_kind");
        page.Find("[aria-label='Condition operator for draft-section']").Change("==");
        page.Find("[aria-label='Condition value for draft-section']").Change("follow_up");

        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal("encounter_kind == follow_up", NodeWhenOf(sent!, "draft-section"));
        var manifest = JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var validated = Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
            manifest, sent.Files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public async Task ANodeWhenBelow18_IsRefusedAtTheDesk_NamingTheUpgrade()
    {
        // A pending node-when on a v17 package (the editor hides the control
        // below 18, but a carried draft can hold one): the desk refuses it by
        // name and holds the publish until the package is upgraded.
        var fixture = Fixture(17);
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{fixture.Ref}")
            .SetResult("""
                { "Version": 15, "NodeWhenEdits": [ { "NodeId": "draft-section", "When": "encounter_kind == follow_up" } ] }
                """);
        CapturePublish();
        var page = Render<Templates>();

        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(
            "Node 'draft-section' declares when, which requires specVersion 18. Use \"Upgrade to specVersion 18\" and publish.",
            Refusals(page));
    }

    [Fact]
    public void ANodeWhenEdit_RehydratesFromADraft()
    {
        // A persisted draft carrying a node-when restores it on load (ApplyDraft).
        var fixture = Fixture(18);
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{fixture.Ref}")
            .SetResult("""
                { "Version": 15, "NodeWhenEdits": [ { "NodeId": "draft-section", "When": "encounter_kind == follow_up" } ] }
                """);

        var page = Render<Templates>();

        Assert.Equal("encounter_kind == follow_up", NodeWhenEdits(page)["draft-section"]);
    }
}
