using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #691: the per-binding source dropdown. Its source vocabulary is a pure
/// function (tested directly), and the control forwards a chosen source — and
/// only keeps a renderer for a node source — to the host.
/// </summary>
public class BindingSourceEditorTests : ClientRenderTestContext
{
    private static WorkflowManifestReader.NodeView Node(string id, bool hasOutput = true, string? forEach = null) =>
        new(id, id, null, forEach, Array.Empty<WorkflowManifestReader.BindingView>(),
            IsResult: false, HasOutput: hasOutput);

    // ----- the source-vocabulary helper -----

    [Fact]
    public void SourceOptions_CoversInputsNodesScalars_AndFallsBackToConsultDraft()
    {
        var nodes = new[] { Node("assemble"), Node("draft") };

        var noInputs = BindingSourceEditor.SourceOptions(nodes, new[] { "age" }, nodes[0], current: "");
        Assert.Contains("input:consult_draft", noInputs);      // frozen fallback when no inputs declared
        Assert.Contains("node:draft", noInputs);               // other nodes, not self
        Assert.DoesNotContain("node:assemble", noInputs);
        Assert.Contains("data:age", noInputs);                 // scalars

        var withInputs = BindingSourceEditor.SourceOptions(nodes, Array.Empty<string>(), nodes[0], current: "", inputs: new[] { "hpi", "ros" });
        Assert.Contains("input:hpi", withInputs);
        Assert.DoesNotContain("input:consult_draft", withInputs);
    }

    [Fact]
    public void SourceOptions_UnderADataForEach_OffersItemFields_AndKeepsAnUnknownCurrent()
    {
        var node = Node("fan", forEach: "data:standards");

        var options = BindingSourceEditor.SourceOptions(
            new[] { node }, Array.Empty<string>(), node, current: "input:was_removed").ToList();

        Assert.Contains("item:name", options);
        Assert.Contains("item:content", options);
        // An unrecognized current value is preserved (and first) so a stale binding still shows.
        Assert.Equal("input:was_removed", options[0]);
    }

    [Fact]
    public void TargetHasOutput_IsTrueOnlyForANodeThatProducesOutput()
    {
        var nodes = new[] { Node("draft", hasOutput: true), Node("gate", hasOutput: false) };

        Assert.True(BindingSourceEditor.TargetHasOutput(nodes, "node:draft"));
        Assert.False(BindingSourceEditor.TargetHasOutput(nodes, "node:gate"));
        Assert.False(BindingSourceEditor.TargetHasOutput(nodes, "input:hpi"));
    }

    // ----- the control -----

    private static WorkflowNodeSummary.BindingRow Row(string from, string? renderer = null) =>
        new("summary", from, renderer, from, renderer, IsNew: false);

    [Fact]
    public void ANodeSource_RevealsTheRendererSelect_WithLabelledControls()
    {
        var nodes = new[] { Node("assemble"), Node("draft", hasOutput: true) };
        var page = Render<BindingSourceEditor>(p => p
            .Add(x => x.Node, nodes[0])
            .Add(x => x.Row, Row("node:draft"))
            .Add(x => x.Nodes, nodes));

        var selects = page.FindAll(".binding-row__select");
        Assert.Equal("Source for summary", selects[0].GetAttribute("aria-label"));
        // A node source exposes the "render as" dropdown.
        Assert.Equal(2, selects.Count);
        Assert.Equal("Renderer for summary", selects[1].GetAttribute("aria-label"));
    }

    [Fact]
    public void ChangingToANonNodeSource_ForwardsTheChange_AndDropsTheRenderer()
    {
        WorkflowNodeSummary.BindingChange? change = null;
        var nodes = new[] { Node("assemble"), Node("draft", hasOutput: true) };
        var page = Render<BindingSourceEditor>(p => p
            .Add(x => x.Node, nodes[0])
            .Add(x => x.Row, Row("node:draft", renderer: "concept-bullets"))
            .Add(x => x.Nodes, nodes)
            .Add(x => x.Scalars, new[] { "age" })
            .Add(x => x.OnChanged, EventCallback.Factory.Create<WorkflowNodeSummary.BindingChange>(this, c => change = c)));

        page.FindAll(".binding-row__select")[0].Change("data:age");

        Assert.NotNull(change);
        Assert.Equal("data:age", change!.From);
        Assert.Null(change.As);   // a renderer only makes sense on a node source
    }
}
