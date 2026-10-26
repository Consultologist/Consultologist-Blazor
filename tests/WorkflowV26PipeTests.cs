using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1059 (package-format-v26-design.md § 7.1): a renderer pipe takes the node —
/// {{ node.extract | concept_bullets }}; the .content and .value spellings are
/// refused with the replacement; an if/case node passes when its returned
/// node's format is the pipe's; a loop is a list — pipe a body node inside it.
/// </summary>
public class WorkflowV26PipeTests
{
    private static List<string> Reading(string text) => V26Package.FlowErrors(m => m, files => files["prompts/assessment.md"] = "Assess. " + text);

    /// <summary>The minimal with a concept_list body node in the loop beside section_notes, returned by it.</summary>
    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithConceptBody(string sectionText)
    {
        var (manifest, files) = V26Package.Minimal();
        manifest = V26Package.WithNode(V26Package.WithNodes(manifest,
            new WorkflowNodeSpec("per_item", "Per item", Prompt: "prompts/per_item.md", Format: "concept_list", OnInvalid: WorkflowOnInvalid.Fail)),
            "sections", n => n with { Nodes = new List<string> { "per_item", "section_notes" }, Ret = "node.section_notes" });
        files["prompts/per_item.md"] = "Concepts of {{ item.content }}.";
        files["prompts/section.md"] = sectionText;
        return (manifest, files);
    }

    [Fact]
    public void ARendererPipe_TakesTheNode()
    {
        Assert.Empty(Reading("{{ node.extract | concept_bullets }}"));
        Assert.Empty(Reading("{{ node.extract | concept_context }}"));

        var (manifest, files) = V26Package.Minimal();
        files["prompts/assessment.md"] = "Assess. {{ if input.kind == \"new\" }}{{ node.extract | concept_bullets }}{{ end }}";
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);
        var prompt = Assert.Single(walk.Templates, t => t.Kind == TemplateKind.Prompt && t.Id == "assessment");
        var rendered = Assert.Single(prompt.References.Rendered);
        Assert.Equal(("extract", "concept_bullets", "input.kind == \"new\""), (rendered.Node, rendered.Pipe, rendered.Guard));
        Assert.Contains("extract", walk.EdgesOf("assessment"));
    }

    [Fact]
    public void TheContentAndValueSpellings_AreRefusedWithTheReplacement()
    {
        Assert.Contains("Node 'assessment' prompt pipes node.extract.content through concept_bullets; at specVersion 26 a renderer pipe takes the node — node.extract | concept_bullets.",
            Reading("{{ node.extract.content | concept_bullets }}"));
        Assert.Contains("Node 'assessment' prompt pipes node.extract.value through concept_bullets; a renderer pipe takes the node — node.extract | concept_bullets.",
            Reading("{{ node.extract.value | concept_bullets }}"));
    }

    [Fact]
    public void ANodeOfAnotherFormat_IsRefused_NamingTheFormat()
    {
        Assert.Contains("Node 'assessment' prompt pipes node.scope through concept_bullets, but format 'classification' (enum) is not concept_list; concept_bullets renders a concept_list node (node.<id>).",
            Reading("{{ node.scope | concept_bullets }}"));

        var (manifest, files) = WithConceptBody("{{ item.label }}: {{ item.content }} {{ node.per_item | concept_bullets }}");
        files["prompts/assessment.md"] = "Assess. {{ node.per_item | concept_bullets }}";
        // per_item is a body node — read from outside, the loop is what one reads; the pipe does not change that.
        Assert.Contains(V26Package.FlowErrors((manifest, files)), e => e.Contains("per_item", StringComparison.Ordinal) && e.Contains("runs inside loop", StringComparison.Ordinal));

        var (manifest2, files2) = V26Package.Minimal();
        files2["prompts/assessment.md"] = "Assess. {{ node.section_notes | concept_bullets }}";
        Assert.Contains(V26Package.FlowErrors((manifest2, files2)), e => e.Contains("section_notes", StringComparison.Ordinal) && e.Contains("runs inside loop", StringComparison.Ordinal));
    }

    [Fact]
    public void ATextNode_IsRefused()
    {
        // assessment reads itself? no — scope the read in section_notes: a prose node piped.
        var (manifest, files) = V26Package.Minimal();
        files["prompts/section.md"] = "{{ item.label }}: {{ item.content }} {{ node.assessment | concept_bullets }}";

        Assert.Contains("Node 'section_notes' prompt pipes node.assessment through concept_bullets, but format 'prose' is text; concept_bullets renders a concept_list node (node.<id>).", V26Package.FlowErrors((manifest, files)));
    }

    [Fact]
    public void ALoop_IsAList_PipeABodyNodeInsideIt()
    {
        Assert.Contains("Node 'assessment' prompt pipes node.sections through concept_bullets; a loop is a list — pipe a body node inside it ({{ for e in node.sections }}{{ e.section_notes | concept_bullets }}{{ end }}), or read node.sections.content.",
            Reading("{{ node.sections | concept_bullets }}"));
        Assert.Contains("Node 'assessment' prompt pipes node.sections.content through concept_bullets; at specVersion 26 a renderer pipe takes the node — node.sections | concept_bullets.",
            Reading("{{ node.sections.content | concept_bullets }}"));

        // Inside the loop: the body node by id (decided 2026-10-26), never the element's content.
        // The returned node reads its sibling by bare id inside the body, piped — the in-body spelling.
        var (manifest, files) = WithConceptBody("{{ item.label }}: {{ item.content }} {{ node.per_item | concept_bullets }}");
        files["prompts/assessment.md"] = "Assess. {{ for e in node.sections }}{{ e.per_item | concept_bullets }}{{ end }}";
        Assert.Empty(V26Package.FlowErrors((manifest, files)));

        files["prompts/assessment.md"] = "Assess. {{ for e in node.sections }}{{ e.content | concept_bullets }}{{ end }}";
        Assert.Contains("Node 'assessment' prompt pipes e.content through concept_bullets; at specVersion 26 a renderer pipe takes the node — e.section_notes | concept_bullets.", V26Package.FlowErrors((manifest, files)));

        files["prompts/assessment.md"] = "Assess. {{ for e in node.sections }}{{ e | concept_bullets }}{{ end }}";
        Assert.Contains("Node 'assessment' prompt pipes e through concept_bullets; a renderer pipe takes a node (node.<id>).", V26Package.FlowErrors((manifest, files)));

        files["prompts/assessment.md"] = "Assess. {{ for e in node.sections }}{{ e.section_notes | concept_bullets }}{{ end }}";
        Assert.Contains("Node 'assessment' prompt pipes e.section_notes through concept_bullets, but format 'prose' is text; concept_bullets renders a concept_list node (node.<id>).", V26Package.FlowErrors((manifest, files)));
    }

    [Fact]
    public void AnIfNode_PipesWhenItsBranchesAgree()
    {
        WorkflowPackageManifest Triage(WorkflowPackageManifest m, string elseFormat) => V26Package.WithNodes(m,
            new WorkflowNodeSpec("full", "Full", Prompt: "prompts/full.md", Format: "concept_list", OnInvalid: WorkflowOnInvalid.Fail),
            new WorkflowNodeSpec("brief", "Brief", Prompt: "prompts/brief.md", Format: elseFormat, OnInvalid: elseFormat == "prose" ? null : WorkflowOnInvalid.Fail),
            new WorkflowNodeSpec("triage", "Triage", If: "input.kind == \"new\"",
                Then: new WorkflowFlowBodySpec(new List<string> { "full" }, "node.full"),
                Else: new WorkflowFlowBodySpec(new List<string> { "brief" }, "node.brief")));
        void Files(Dictionary<string, string> files)
        {
            files["prompts/full.md"] = "Full {{ input.notes }}";
            files["prompts/brief.md"] = "Brief {{ input.notes }}";
            files["prompts/assessment.md"] = "Assess. {{ node.triage | concept_bullets }}";
        }

        Assert.Empty(V26Package.FlowErrors(m => Triage(m, "concept_list"), Files));
        Assert.Contains(V26Package.FlowErrors(m => Triage(m, "prose"), Files),
            e => e.StartsWith("Node 'assessment' prompt pipes node.triage through concept_bullets, but branches of 'triage' return", StringComparison.Ordinal) && e.EndsWith("concept_bullets renders a concept_list node (node.<id>).", StringComparison.Ordinal));
    }

    [Fact]
    public void ADeliverable_ASnippet_AnInput_AreNotNodes()
    {
        Assert.Contains("Node 'assessment' prompt pipes deliverable.consult.content through concept_bullets, but deliverable.consult composes sections; concept_bullets renders a concept_list node (node.<id>).",
            Reading("{{ deliverable.consult.content | concept_bullets }}"));
        Assert.Contains("Node 'assessment' prompt pipes snippet.standards.content through concept_bullets; a renderer pipe takes a node (node.<id>).",
            Reading("{{ snippet.standards.content | concept_bullets }}"));
        Assert.Contains("Node 'assessment' prompt pipes input.notes through concept_bullets; a renderer pipe takes a node (node.<id>).",
            Reading("{{ input.notes | concept_bullets }}"));
    }

    [Fact]
    public void Below26_TheGrammarIsFrozen()
    {
        var (manifest, files) = V25Package.Minimal();
        files["prompts/assessment.md"] = "Assess. {{ node.extract | concept_bullets }}";
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        var prompt = Assert.Single(walk.Templates, t => t.Kind == TemplateKind.Prompt && t.Id == "assessment");
        Assert.Contains(prompt.References.Errors, e => e.Contains("reads node 'extract' bare", StringComparison.Ordinal));
    }
}
