using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v23 (#955) Layer C: authoring a node's macro attachments in the node pane —
/// attach a library macro, place it before/after, gate it with the node-level
/// condition. Gated to specVersion 23 and a prompt-bearing node; a bare
/// before/always attachment round-trips as the string form, a placed or gated
/// one as the object.
/// </summary>
public class TemplatesNodeMacrosTests : ClientRenderTestContext
{
    private static string Manifest(int specVersion) => $$"""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": {{specVersion}},
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [ { "id": "consult_draft", "label": "Consult draft", "required": true } ],
          "data": { "standards": "data/standards/" },
          "macros": [ { "id": "tool_guidance", "label": "Tool guidance", "file": "macros/tool_guidance.md" } ],
          "prompts": [
            { "id": "classify", "file": "prompts/classify.md", "variables": ["referral"] },
            { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "scope", "label": "Is the referral in scope?", "prompt": "classify", "kind": "classifier",
              "values": ["in_scope", "out_of_scope"], "bindings": { "referral": "input:consult_draft" } },
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section", "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """;

    private WorkflowPackageContentResponse Fixture(int specVersion) =>
        EditorFixtures.Package(Manifest(specVersion), specVersion,
            ("prompts/classify.md", "Is this in scope? {{ referral }}"),
            ("prompts/draft-section.md", "{{ section_name }}: {{ consult_draft }}"),
            ("macros/tool_guidance.md", "Use short SNOMED search terms."));

    private IRenderedComponent<Templates> RenderEditor(int specVersion)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(Fixture(specVersion));
        return Render<Templates>();
    }

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

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

    private static JsonElement NodeMacros(WorkflowPackagePublishRequest request, string nodeId)
    {
        var node = JsonDocument.Parse(request.Manifest.GetRawText()).RootElement
            .GetProperty("nodes").EnumerateArray().First(n => n.GetProperty("id").GetString() == nodeId);
        return node.GetProperty("macros").Clone();
    }

    // ----- gating -----------------------------------------------------------

    [Fact]
    public void At23_APromptNode_OffersTheMacrosSection()
    {
        var page = RenderEditor(23);
        ShowNode(page, "draft-section");

        Assert.Contains("Macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
        Assert.NotEmpty(page.FindAll("[aria-label='Attach a macro to draft-section']"));
    }

    [Fact]
    public void Below23_TheMacrosSection_IsAbsent()
    {
        var page = RenderEditor(22);
        ShowNode(page, "draft-section");

        Assert.Empty(page.FindAll("[aria-label='Attach a macro to draft-section']"));
    }

    [Fact]
    public void AnAggregatorNode_HasNoPromptToComposeInto_SoNoSection()
    {
        var page = RenderEditor(23);
        ShowNode(page, "assemble-note");

        Assert.Empty(page.FindAll("[aria-label='Attach a macro to assemble-note']"));
    }

    // ----- composition + round trip -----------------------------------------

    [Fact]
    public void AttachingAMacro_ComposesItBare_OntoTheNode()
    {
        var page = RenderEditor(23);
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find("[aria-label='Attach a macro to draft-section']").Change("tool_guidance");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var macros = NodeMacros(sent!, "draft-section");
        Assert.Equal(JsonValueKind.Array, macros.ValueKind);
        // The before/always form round-trips as the bare string.
        Assert.Equal("tool_guidance", macros[0].GetString());
    }

    [Fact]
    public void PlacingAMacroAfter_WritesThePlacementObject()
    {
        var page = RenderEditor(23);
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find("[aria-label='Attach a macro to draft-section']").Change("tool_guidance");
        page.Find("[aria-label='Placement for macro tool_guidance on draft-section']").Change("after");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var entry = NodeMacros(sent!, "draft-section")[0];
        Assert.Equal(JsonValueKind.Object, entry.ValueKind);
        Assert.Equal("tool_guidance", entry.GetProperty("id").GetString());
        Assert.Equal("after", entry.GetProperty("at").GetString());
    }

    [Fact]
    public void GatingAMacro_OnAClassifier_WritesItsWhen()
    {
        var page = RenderEditor(23);
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find("[aria-label='Attach a macro to draft-section']").Change("tool_guidance");
        // The macro's condition subject names the macro, so it never collides
        // with the node's own "Runs when".
        page.Find("[aria-label='Add a condition for draft-section macro tool_guidance']").Click();
        page.Find("[aria-label^='Condition operand for draft-section macro tool_guidance']").Change("node:scope");
        page.Find("[aria-label^='Condition value for draft-section macro tool_guidance']").Change("in_scope");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal("node:scope == in_scope", NodeMacros(sent!, "draft-section")[0].GetProperty("when").GetString());
    }
}
