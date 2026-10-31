using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1243 (decided 2026-10-30): a pane whose text iterates a loop previews the loop's elements —
/// one per folder item at 26, each element's content and body nodes as tokens on the element's
/// path; at 27 the loop's inner if/case is judged per element from the picks and the pane says
/// how the elements were decided.
/// </summary>
public class TemplatesPreviewLoopTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> Show(string key, WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
        return page;
    }

    private static string Rendered(IRenderedComponent<Templates> page) =>
        page.Find(".pane-preview pre.rendered-preview__text").TextContent;

    private static IReadOnlyList<(string Kind, string Key, string Text, string? Branch)> Gates(IRenderedComponent<Templates> page) =>
        page.FindAll(".rendered-preview__gate")
            .Select(li => (li.GetAttribute("data-gate-kind")!, li.GetAttribute("data-gate-key")!, li.TextContent.Trim(), li.GetAttribute("data-branch")))
            .ToList();

    [Fact]
    public void At26_AReaderIteratingTheLoop_PreviewsOneElementPerFolderItem()
    {
        var page = Show("node:assessment", EditorFixtures.ReadingTheLoop(EditorFixtures.V26(), "{{ for e in node.sections }}[{{ e.item.label }}: {{ e.content }}]{{ end }}"));

        var rendered = Rendered(page);
        Assert.Contains("[HPI: ⟨node.sections[hpi].section_notes.content⟩]", rendered);
        Assert.Contains("[Plan: ⟨node.sections[plan].section_notes.content⟩]", rendered);
        Assert.DoesNotContain(Gates(page), g => g.Branch == "per element");
    }

    [Fact]
    public void At27_TheInnerIf_IsJudgedPerElement_AndThePaneSaysHow()
    {
        var page = Show("node:assessment", EditorFixtures.ReadingTheLoop(EditorFixtures.V27Flow(), "{{ for e in node.sections }}[{{ e.item.id }}:{{ if e.skipped }}skipped{{ else }}{{ e.content }}{{ end }}]{{ end }}"));

        var rendered = Rendered(page);
        // triage: if item.id == "hpi" then patient_draft else table_draft — both items take a branch.
        Assert.Contains("[hpi:⟨node.sections[hpi].patient_draft.content⟩]", rendered);
        Assert.Contains("[plan:⟨node.sections[plan].table_draft.content⟩]", rendered);
        var line = Assert.Single(Gates(page), g => g.Branch == "per element");
        Assert.Equal(("flow", "triage"), (line.Kind, line.Key));
        Assert.StartsWith("node 'triage' decided per element of loop 'sections': then 1 · else 1", line.Text);
    }
}
