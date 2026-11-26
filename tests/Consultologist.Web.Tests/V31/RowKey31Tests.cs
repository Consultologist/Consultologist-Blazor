using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>Task 13 (#1509; part 08 § 2.1, § 2.3): a row key read into its root, scopes, items and passes.</summary>
public class RowKey31Tests
{
    [Theory]
    [InlineData("node.plan", "node", "plan", new[] { "plan" }, new string[0], null)]
    [InlineData("node.sections.paragraphs.draft_paragraph", "node", "sections.paragraphs.draft_paragraph", new[] { "sections", "paragraphs", "draft_paragraph" }, new string[0], null)]
    [InlineData("node.hpi_section.standard_draft", "node", "hpi_section.standard_draft", new[] { "hpi_section", "standard_draft" }, new string[0], null)]
    [InlineData("node.standards.section.standard_draft", "node", "standards.section.standard_draft", new[] { "standards", "section", "standard_draft" }, new string[0], null)]
    [InlineData("check.part_coverage.coverage", "check", "part_coverage.coverage", new[] { "part_coverage", "coverage" }, new string[0], null)]
    [InlineData("deliverable.emr.note", "deliverable", "emr.note", new[] { "emr", "note" }, new string[0], null)]
    [InlineData("node.sections.paragraphs.draft_paragraph:2/1", "node", "sections.paragraphs.draft_paragraph:2/1", new[] { "sections", "paragraphs", "draft_paragraph" }, new[] { "2", "1" }, null)]
    [InlineData("node.refine.judge#3", "node", "refine.judge#3", new[] { "refine", "judge" }, new string[0], 3)]
    [InlineData("node.refine.sections#2:3", "node", "refine.sections#2:3", new[] { "refine", "sections" }, new[] { "3" }, 2)]
    [InlineData("node.standards.section.standard_draft:3", "node", "standards.section.standard_draft:3", new[] { "standards", "section", "standard_draft" }, new[] { "3" }, null)]
    [InlineData("check.part_coverage.coverage:2", "check", "part_coverage.coverage:2", new[] { "part_coverage", "coverage" }, new[] { "2" }, null)]
    [InlineData("node.standards.assessment:7", "node", "standards.assessment:7", new[] { "standards", "assessment" }, new[] { "7" }, null)]
    public void EachExampleOfPart08_ParsesToItsRootScopesItemsAndPasses(
        string key, string root, string path, string[] scopes, string[] items, int? pass)
    {
        var parsed = RowKey31.Parse(key);

        Assert.Equal(root, parsed.Root);
        Assert.Equal(path, parsed.Path);
        Assert.Equal(key, parsed.Raw);
        Assert.Equal(scopes, parsed.Steps.Select(s => s.Scope));
        Assert.Equal(items, parsed.Steps.SelectMany(s => s.Items));
        Assert.Equal(pass, parsed.Steps.Select(s => s.Pass).LastOrDefault(p => p != null));
    }

    [Fact]
    public void ADeliveryKey_HasItsRoot_AndTheDeliverableAndDestinationAsItsPath()
    {
        var parsed = RowKey31.Parse("delivery:deliverable.emr.note/email");

        Assert.Equal("delivery", parsed.Root);
        Assert.Equal("deliverable.emr.note/email", parsed.Path);
        Assert.Null(parsed.ParentInstance);
    }

    [Theory]
    [InlineData("node.identify_problem/render:1")]
    [InlineData("node.sections.paragraphs.draft_paragraph:2/1/render:1")]
    [InlineData("node.assessment/macro:letrozole_prescription_text")]
    [InlineData("node.standards.assessment:7/if:macro.letrozole_prescription_text")]
    [InlineData("node.assessment:7/spot:2")]
    public void ASubRowId_IsNotARowKey(string id)
    {
        var parsed = RowKey31.Parse(id);

        Assert.Equal("", parsed.Root);
        Assert.Equal(id, parsed.Path);
        Assert.Empty(parsed.Steps);
        Assert.Null(parsed.ParentInstance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plan")]
    [InlineData("node")]
    [InlineData("node.")]
    [InlineData("node..plan")]
    [InlineData("widget.plan")]
    [InlineData("node.plan:")]
    [InlineData("node.plan:1//2")]
    [InlineData("node.plan#x")]
    [InlineData("node.plan#")]
    public void AnUnparseableKey_HasNoRoot_AndItsPathIsTheKey_AndNeverThrows(string key)
    {
        var parsed = RowKey31.Parse(key);

        Assert.Equal("", parsed.Root);
        Assert.Equal(key, parsed.Path);
        Assert.Equal(key, parsed.Raw);
        Assert.Empty(parsed.Steps);
        Assert.Null(parsed.ParentInstance);
    }

    [Fact]
    public void ANullKey_IsUnparseable()
    {
        var parsed = RowKey31.Parse(null!);

        Assert.Equal("", parsed.Root);
        Assert.Equal("", parsed.Path);
    }

    [Theory]
    [InlineData("node.sections.paragraphs.draft_paragraph:2/1", "node.sections.paragraphs:2/1")]
    [InlineData("node.refine.sections#2:3", "node.refine#2:3")]
    [InlineData("node.refine.judge#3", "node.refine#3")]
    [InlineData("node.hpi_section.standard_draft", "node.hpi_section")]
    [InlineData("node.standards.section.standard_draft:3", "node.standards.section:3")]
    [InlineData("check.part_coverage.coverage:2", "check.part_coverage:2")]
    [InlineData("node.plan", null)]
    [InlineData("node.plan:2", null)]
    [InlineData("deliverable.emr.note", null)]
    public void ParentInstance_IsTheEnclosingItemPassOrCall(string key, string? parent)
    {
        Assert.Equal(parent, RowKey31.Parse(key).ParentInstance);
    }
}
