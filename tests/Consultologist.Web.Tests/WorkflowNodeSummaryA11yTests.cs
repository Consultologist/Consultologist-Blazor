using System;
using System.Linq;
using Bunit;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #688: the reorder/remove glyph buttons carry an accessible name, not just a
/// title. The aggregate-source ↑/↓/× is a representative of the shared
/// aggregate-row__button pattern across Consults, Templates and this component.
/// </summary>
public class WorkflowNodeSummaryA11yTests : ClientRenderTestContext
{
    [Fact]
    public void AggregateReorderButtons_HaveAccessibleNames()
    {
        var node = new WorkflowManifestReader.NodeView(
            "assemble", "Assembler", null, null,
            Array.Empty<WorkflowManifestReader.BindingView>(),
            IsResult: true, HasOutput: true,
            Aggregate: new[] { "draft", "hpi" });

        var page = Render<WorkflowNodeSummary>(parameters => parameters
            .Add(p => p.Nodes, new[] { node })
            .Add(p => p.Editable, true)
            .Add(p => p.OnAggregateChanged,
                EventCallback.Factory.Create<WorkflowNodeSummary.AggregateChange>(this, _ => { })));

        var labels = page.FindAll(".aggregate-row__button")
            .Select(button => button.GetAttribute("aria-label"))
            .ToList();

        Assert.Contains("Move source 1 up", labels);
        Assert.Contains("Remove source 1", labels);
    }
}
