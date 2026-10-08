using System.Text.Json;
using Consultologist.PackageFormat;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>
/// v24 (#957): the per-item layer of a node macro on a node that fans a
/// data: collection — `forItem` anchors the attachment to one item, a when
/// reads `item:id` against the collection's items (a closed set at publish,
/// compared like a classifier's value), and `{{item:<field>}}` tokens fill
/// per item. An input fan's items are the caller's, so none of the three
/// reads them; a deliverable composes no fan item, so a macro carrying an item
/// token is attached only to a fan node.
/// </summary>
public static class V24Fixtures
{
    public const string MacroId = V23Fixtures.MacroId;
    public const string FanNodeId = "section-instructions";

    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Base()
    {
        var (manifest, files) = V23Fixtures.Base();
        return (manifest with { SpecVersion = 24 }, files);
    }

    public static WorkflowNodeSpec FanNode(WorkflowPackageManifest manifest) =>
        manifest.Nodes!.First(n => n.Id == FanNodeId);

    /// <summary>Attach node macros at 24 — to the standards fan unless `onNode` says otherwise — with the macro text given.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithNodeMacros(
        List<WorkflowNodeMacroSpec> attach,
        string text = "Use short SNOMED search terms.",
        WorkflowNodeSpec? onNode = null,
        int specVersion = 24)
    {
        var (manifest, files) = V23Fixtures.WithNodeMacros(attach, onNode: onNode ?? FanNode(V23Fixtures.Base().Manifest));
        files[$"macros/{MacroId}.md"] = text;
        return (manifest with { SpecVersion = specVersion }, files);
    }

    public static IEnumerable<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        V23Fixtures.Errors(bundle);

    public static void AssertValid((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
    {
        var result = V23Fixtures.Validate(bundle);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}

public class WorkflowV24GateTests
{
    [Fact]
    public void TheValidatorAccepts24_ButTheStoreDoesNotRunItYet()
    {
        // Layer A (#970) makes it publishable; Layer B (#971) runs it — the
        // staged gap every rung has opened and closed.
        Assert.Contains(24, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.DoesNotContain(24, WorkflowPackageStore.SupportedSpecVersions);
    }

    [Fact]
    public void TheBaseManifest_IsValidAt24()
    {
        V24Fixtures.AssertValid(V24Fixtures.Base());
    }

    [Fact]
    public void AV23NodeMacro_IsStillValidAt24()
    {
        V24Fixtures.AssertValid(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec>
        {
            V24Fixtures.MacroId,
            new(V24Fixtures.MacroId, At: WorkflowNodeMacroSpec.After, When: "node:scope == in_scope")
        }));
    }
}

public class WorkflowV24ForItemTests
{
    [Fact]
    public void ForItem_OnAnItemTheCollectionHolds_IsAccepted()
    {
        V24Fixtures.AssertValid(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec>
        {
            new(V24Fixtures.MacroId, At: WorkflowNodeMacroSpec.After, ForItem: "hpi")
        }));
    }

    [Fact]
    public void ForItem_Below24_IsRefusedByName()
    {
        Assert.Contains(
            V24Fixtures.Errors(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { new(V24Fixtures.MacroId, ForItem: "hpi") }, specVersion: 23)),
            e => e.Contains("macro 'tool_guidance' declares forItem, which requires specVersion 24"));
    }

    [Fact]
    public void ForItem_OnANodeThatDoesNotFan_IsRefused()
    {
        var scalar = V23Fixtures.FirstPromptNode(V23Fixtures.Base().Manifest);
        Assert.Contains(
            V24Fixtures.Errors(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { new(V24Fixtures.MacroId, ForItem: "hpi") }, onNode: scalar)),
            e => e.Contains($"anchors to fan item 'hpi', but '{scalar.Id}' is not a forEach fan"));
    }

    [Fact]
    public void ForItem_OnAnItemTheCollectionLacks_IsRefused()
    {
        Assert.Contains(
            V24Fixtures.Errors(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { new(V24Fixtures.MacroId, ForItem: "ros") })),
            e => e.Contains("anchors to fan item 'ros', which the collection fanned by 'section-instructions' does not contain (items: hpi, pmh)"));
    }

    [Fact]
    public void ForItem_RoundTripsOnTheWire_AndIsNotBare()
    {
        var spec = new WorkflowNodeMacroSpec(V24Fixtures.MacroId, ForItem: "hpi");
        Assert.False(spec.IsBare);

        // Read back the way the engine does (case-insensitive), as the v20 slot test.
        var json = JsonSerializer.Serialize(spec);
        Assert.Contains("\"forItem\":\"hpi\"", json);
        Assert.Equal(spec, JsonSerializer.Deserialize<WorkflowNodeMacroSpec>(json, WorkflowPackageManifestJson.ReadOptions));
    }
}

public class WorkflowV24ItemOperandTests
{
    [Theory]
    [InlineData("item:id == hpi")]
    [InlineData("item:id != pmh")]
    [InlineData("item:id == hpi and node:scope == in_scope")]
    [InlineData("not (item:id == pmh)")]
    public void AnItemIdClause_OnADataFanMacro_IsAccepted(string when)
    {
        V24Fixtures.AssertValid(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { new(V24Fixtures.MacroId, When: when) }));
    }

    [Fact]
    public void AnItemClause_Below24_IsRefusedByName()
    {
        Assert.Contains(
            V24Fixtures.Errors(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { new(V24Fixtures.MacroId, When: "item:id == hpi") }, specVersion: 23)),
            e => e.Contains("condition reads 'item:', which requires specVersion 24"));
    }

    [Fact]
    public void AnItemClause_OnANodeThatDoesNotFan_IsRefused()
    {
        var scalar = V23Fixtures.FirstPromptNode(V23Fixtures.Base().Manifest);
        Assert.Contains(
            V24Fixtures.Errors(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { new(V24Fixtures.MacroId, When: "item:id == hpi") }, onNode: scalar)),
            e => e.Contains($"reads 'item:id', but '{scalar.Id}' is not a forEach fan"));
    }

    [Theory]
    [InlineData("item:name == hpi", "a condition reads item:id only")]
    [InlineData("item:id", "tests an item for truth")]
    [InlineData("item:id > hpi", "an item id is compared with == or != only")]
    [InlineData("item:id == ros", "which the collection does not contain (items: hpi, pmh)")]
    [InlineData("item:id + 1 > 2", "in arithmetic; an item's field is a symbol, not a number")]
    public void AnItemClause_OutsideTheClosedForm_IsRefused(string when, string sentence)
    {
        Assert.Contains(
            V24Fixtures.Errors(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { new(V24Fixtures.MacroId, When: when) })),
            e => e.Contains(sentence));
    }

    [Fact]
    public void AnItemClause_OnADeliverable_IsRefused()
    {
        // A deliverable's when is judged once per run; there is no item to read.
        var (manifest, files) = V24Fixtures.Base();
        manifest = manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0 ? r with { When = "item:id == hpi" } : r).ToList()
        };
        Assert.Contains(
            V24Fixtures.Errors((manifest, files)),
            e => e.Contains("reads 'item:id', but only a node macro on a node that fans a data: collection reads an item"));
    }

    [Fact]
    public void TheParser_ReadsAnItemOperand_AndWritesItBack()
    {
        Assert.True(WorkflowResultConditions.TryParseExpression("item:id == hpi and node:scope == in_scope", out var expression, out var error), error);
        var item = Assert.Single(expression!.Leaves, leaf => leaf.IsItemValue);
        Assert.Equal("id", item.ItemField);
        Assert.Equal("hpi", item.Literal);
        Assert.False(item.IsNodeValue);
        Assert.Equal("item:id == hpi and node:scope == in_scope", expression.Text);
    }

    [Fact]
    public void AnItemLeaf_IsJudgedAgainstTheItemItIsGiven_AndAbsentWithoutOne()
    {
        Assert.True(WorkflowResultConditions.TryParseExpression("item:id == hpi", out var expression, out _));
        var hpi = new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = "hpi", ["name"] = "History of Present Illness" };
        var pmh = new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = "pmh", ["name"] = "Past Medical History" };

        Assert.True(WorkflowResultConditions.Holds(expression, null, null, hpi));
        Assert.False(WorkflowResultConditions.Holds(expression, null, null, pmh));
        // No item (a scalar node, a deliverable): absent, never held — and not
        // turned into held by negation either.
        Assert.False(WorkflowResultConditions.Holds(expression, null, null));
        Assert.True(WorkflowResultConditions.TryParseExpression("not (item:id == hpi)", out var negated, out _));
        Assert.False(WorkflowResultConditions.Holds(negated, null, null));
        Assert.True(WorkflowResultConditions.Holds(negated, null, null, pmh));
    }
}

public class WorkflowV24ItemTokenTests
{
    [Theory]
    [InlineData("Draft the {{item:name}} section.")]
    [InlineData("Section {{item:id}}: follow {{item:content}}.")]
    public void AnItemToken_OnADataFanMacro_IsAccepted(string text)
    {
        V24Fixtures.AssertValid(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { V24Fixtures.MacroId }, text));
    }

    [Fact]
    public void AnItemToken_Below24_IsAVersionRequirement()
    {
        Assert.Contains(
            V24Fixtures.Errors(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { V24Fixtures.MacroId }, "Draft {{item:name}}.", specVersion: 23)),
            e => e.Contains("Macro 'tool_guidance' placeholder '{{item:name}}' requires specVersion 24"));
    }

    [Fact]
    public void AnItemToken_TheCollectionDoesNotDeclare_IsRefused()
    {
        Assert.Contains(
            V24Fixtures.Errors(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { V24Fixtures.MacroId }, "{{item:value}}")),
            e => e.Contains("Node 'section-instructions' macro 'tool_guidance' carries '{{item:value}}', an item field the collection does not declare (it declares: id, name, content)"));
    }

    [Fact]
    public void AnItemToken_OnANodeThatDoesNotFan_IsRefused()
    {
        var scalar = V23Fixtures.FirstPromptNode(V23Fixtures.Base().Manifest);
        Assert.Contains(
            V24Fixtures.Errors(V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { V24Fixtures.MacroId }, "Draft {{item:name}}.", onNode: scalar)),
            e => e.Contains($"Node '{scalar.Id}' macro 'tool_guidance' carries '{{{{item:name}}}}' but the node declares no forEach"));
    }

    [Fact]
    public void AnItemToken_OnADeliverableAttachment_IsRefused()
    {
        // Attached to the fan node too, so the orphan rule stays quiet and the
        // refusal is the deliverable's alone.
        var (manifest, files) = V24Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { V24Fixtures.MacroId }, "Draft {{item:name}}.");
        manifest = manifest with
        {
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with { Macros = new List<WorkflowResultMacroSpec> { new(V24Fixtures.MacroId) } }
                : r).ToList()
        };
        Assert.Contains(
            V24Fixtures.Errors((manifest, files)),
            e => e.Contains("macro 'tool_guidance' carries '{{item:name}}', which only a node that fans a collection fills; a deliverable composes no fan item"));
    }
}
