using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// C6 (#1012): a snippet or macro text is walked with no fan of its own, so its
/// fan-item reads are recorded, not refused — and refused at the READING site when
/// that node does not fan: a node instance, what a prompt reaches through a snippet
/// or macro text, a slot's fill, a deliverable instance. Restores what v24 (#957)
/// allowed as {{item:field}} in a macro composed into a fan node.
/// </summary>
public class WorkflowV25ItemReadsTests
{
    private static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        V25Package.Validate(bundle, V25Package.Catalog());

    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithClosingText(string text)
    {
        var (manifest, files) = V25Package.Minimal();
        files["macros/closing.md"] = text;
        return (manifest, files);
    }

    [Fact]
    public void ATextWalkedWithNoFan_RecordsItemReads_WhenDeferred()
    {
        var deferred = TemplateReferenceContext.Empty with { ItemDeferred = true };

        var refs = TemplateReferenceWalker.Walk("Macro 'm'", "{{ item.name }} {{ item.text }} {{ item.value.x }}", deferred);
        Assert.True(refs.IsValid, string.Join(" | ", refs.Errors));
        Assert.Equal(new[] { "name", "text", "value" }, refs.ItemMembers.Order());

        var stray = TemplateReferenceWalker.Walk("Macro 'm'", "{{ item.content }}", deferred);
        Assert.Contains("Macro 'm' reads item.content, which a fan item does not carry (it carries: id, name, text or value).", stray.Errors);

        var prompt = TemplateReferenceWalker.Walk("Prompt 'p'", "{{ item.name }}", TemplateReferenceContext.Empty);
        Assert.Contains("Prompt 'p' reads item.name, but the node does not fan.", prompt.Errors);
    }

    [Fact]
    public void ANodeInstance_OnANodeThatDoesNotFan_IsRefused_WhenTheMacroReadsTheItem()
    {
        var result = Validate(WithClosingText("Closing for {{ item.name }}."));

        Assert.Contains("Node 'assessment' macro 'closing' reads item.name, but 'assessment' does not fan.", result.Errors);
    }

    [Fact]
    public void ANodeInstance_OnAFanNode_MayReadTheItem()
    {
        var (manifest, files) = WithClosingText("Closing for {{ item.name }}: {{ item.text }}.");
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id switch
            {
                "assessment" => n with { Macros = null },
                "section_notes" => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After) } },
                _ => n
            }).ToList()
        };

        var result = Validate((manifest, files));

        Assert.DoesNotContain(result.Errors, e => e.Contains("does not fan"));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ADeliverableInstance_OfAMacroReadingTheItem_IsRefused()
    {
        var (manifest, files) = WithClosingText("Closing for {{ item.name }}.");
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(n => n.Id == "assessment" ? n with { Macros = null } : n).ToList(),
            Deliverables = new List<WorkflowDeliverableSpec>
            {
                new("consult", "node:all", "Consultation note", Check: "node.coverage",
                    Macros: new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append) })
            }
        };

        var result = Validate((manifest, files));

        Assert.Contains("Deliverable 'consult' macro 'closing' reads item.name; a deliverable composes no fan item — place the macro on the node to read its items.", result.Errors);
    }

    [Fact]
    public void APromptReachingTheItem_ThroughASnippet_IsRefused_WhereTheNodeDoesNotFan()
    {
        var (manifest, files) = V25Package.Minimal();
        files["snippets/standards.md"] = "Standards for {{ item.name }}.";

        var result = Validate((manifest, files));

        Assert.Contains("Node 'assessment' reaches item.name through Snippet 'standards', but 'assessment' does not fan.", result.Errors);
    }

    [Fact]
    public void ASlotFill_ReadingTheItem_IsRefused_WhereTheSanctioningNodeDoesNotFan()
    {
        var (manifest, files) = WithClosingText("Closing for {{ item.name }}.");
        manifest = manifest with { Nodes = manifest.Nodes!.Select(n => n.Id == "assessment" ? n with { Macros = null } : n).ToList() };
        files["prompts/assessment.md"] = "Assess {{ input.notes }}. {{ macro.closing.prompt }}";

        var result = Validate((manifest, files));

        Assert.Contains("Node 'assessment' reads macro.closing.prompt (a slot), but its fill reads item.name and 'assessment' does not fan.", result.Errors);
    }
}
