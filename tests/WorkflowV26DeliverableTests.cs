using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1101 (package-format-v26-design.md § 3): the deliverable names what it
/// composes — sources, each a prose node, never a body node by name; the sixth
/// object deliverable.&lt;id&gt;.content, an edge on the sources; the gate keyed if;
/// reachability rooted at the sources.
/// </summary>
public class WorkflowV26DeliverableTests
{
    private static List<string> Errors(Func<WorkflowDeliverableSpec, WorkflowDeliverableSpec> change, Action<Dictionary<string, string>>? withFiles = null) =>
        V26Package.FlowErrors(m => m with { Deliverables = m.Deliverables!.Select(change).ToList() }, withFiles);


    [Fact]
    public void TheMinimal_ComposesItsSources()
    {
        Assert.Empty(V26Package.FlowErrors(V26Package.Minimal()));
    }

    [Fact]
    public void Sources_AreRequired_AndSpelledNodeDot()
    {
        Assert.Contains("Deliverable 'consult' names no sources; a deliverable composes one or more nodes (sources: [\"node.<id>\", …]).",
            Errors(d => d with { Sources = null }));
        Assert.Contains("Deliverable 'consult' names no sources; a deliverable composes one or more nodes (sources: [\"node.<id>\", …]).",
            Errors(d => d with { Sources = new List<string>() }));
        Assert.Contains("Deliverable 'consult' source 'sections' must be node.<id>.",
            Errors(d => d with { Sources = new List<string> { "sections" } }));
        Assert.Contains("Deliverable 'consult' source 'node:sections' must be node.<id>.",
            Errors(d => d with { Sources = new List<string> { "node:sections" } }));
    }

    [Fact]
    public void ASource_IsADeclaredNode_Once()
    {
        Assert.Contains("Deliverable 'consult' source names unknown node 'nope'.",
            Errors(d => d with { Sources = new List<string> { "node.sections", "node.nope" } }));
        Assert.Contains("Deliverable 'consult' source node.sections twice; a source composes once.",
            Errors(d => d with { Sources = new List<string> { "node.sections", "node.sections" } }));
    }

    [Fact]
    public void ASource_IsProse()
    {
        Assert.Contains("Deliverable 'consult' source node.extract is a concept_list; a deliverable composes prose — read its .value from a prose node.",
            Errors(d => d with { Sources = new List<string> { "node.extract", "node.sections" } }));
        Assert.Contains("Deliverable 'consult' source node.scope is a classification; a deliverable composes prose — read its .value from a prose node.",
            Errors(d => d with { Sources = new List<string> { "node.scope", "node.sections" } }));
        Assert.Contains("Deliverable 'consult' source node.coverage is a check; a check gates a deliverable (check: node.coverage), it is no text.",
            Errors(d => d with { Sources = new List<string> { "node.coverage", "node.sections" } }));
    }

    [Fact]
    public void ALoopSource_ReturnsProse()
    {
        var errors = V26Package.FlowErrors(
            m => V26Package.WithNode(V26Package.WithNodes(m,
                new WorkflowNodeSpec("per_item", "Per item", Prompt: "prompts/per_item.md", Format: "concept_list", OnInvalid: WorkflowOnInvalid.Fail)),
                "sections", n => n with { Nodes = new List<string> { "per_item", "section_notes" }, Ret = "node.per_item" }),
            files => files["prompts/per_item.md"] = "Concepts of {{ item.content }} and {{ node.section_notes.content }}.");

        Assert.Contains("Deliverable 'consult' source node.sections loops over 'per_item' of format 'concept_list'; a deliverable composes prose.", errors);
    }

    [Fact]
    public void AnIfSource_ReturnsProse_InEveryBranch()
    {
        WorkflowPackageManifest WithTriage(WorkflowPackageManifest m, string thenFormat) =>
            V26Package.WithNodes(m,
                new WorkflowNodeSpec("full", "Full", Prompt: "prompts/full.md", Format: thenFormat, OnInvalid: thenFormat == "prose" ? null : WorkflowOnInvalid.Fail),
                new WorkflowNodeSpec("brief", "Brief", Prompt: "prompts/extract.md", Format: "prose"),
                new WorkflowNodeSpec("triage", "Triage", If: "input.kind == \"new\"",
                    Then: new WorkflowFlowBodySpec(new List<string> { "full" }, "node.full"),
                    Else: new WorkflowFlowBodySpec(new List<string> { "brief" }, "node.brief")));

        Assert.Empty(V26Package.FlowErrors(
            m => WithTriage(m, "prose") with { Deliverables = m.Deliverables!.Select(d => d with { Sources = new List<string> { "node.assessment", "node.triage", "node.sections" } }).ToList() },
            files => files["prompts/full.md"] = "Full {{ input.notes }}."));
        Assert.Contains("Deliverable 'consult' source node.triage returns 'object' and 'text'; a deliverable composes prose.",
            V26Package.FlowErrors(
                m => WithTriage(m, "concept_list") with { Deliverables = m.Deliverables!.Select(d => d with { Sources = new List<string> { "node.assessment", "node.triage", "node.sections" } }).ToList() },
                files => files["prompts/full.md"] = "Full {{ input.notes }}."));
    }

    [Fact]
    public void ABodyNodeSource_NamesTheLoop()
    {
        Assert.Contains("Deliverable 'consult' source node.section_notes runs inside loop 'sections'; name node.sections.",
            Errors(d => d with { Sources = new List<string> { "node.section_notes" } }));
    }

    [Fact]
    public void TwoDeliverables_MayShareASource()
    {
        var errors = V26Package.FlowErrors(m => m with
        {
            Deliverables = m.Deliverables!.Append(new WorkflowDeliverableSpec("letter", null, "Letter", Sources: new List<string> { "node.sections" })).ToList(),
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void ADeliverableMacro_ReadsNoBodyNode_ByName()
    {
        var errors = V26Package.FlowErrors(
            m => m with { Deliverables = m.Deliverables!.Select(d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: "append") } }).ToList() },
            files => files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Add a closing line.") + "Closing: {{ node.section_notes.content }}.");

        Assert.Contains("Deliverable 'consult' macro 'closing' reads node 'section_notes', which runs inside loop 'sections'; a deliverable macro reads scalar nodes, flow nodes and deliverable.<id>.content — place the macro on a body node to read its items.", errors);

        var loopRead = V26Package.FlowErrors(
            m => m with { Deliverables = m.Deliverables!.Select(d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: "append") } }).ToList() },
            files => files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Add a closing line.") + "Closing: {{ node.sections.content }}.");

        Assert.Empty(loopRead);
    }

    [Fact]
    public void ADeliverablesIf_IsWalked_AtItsSite()
    {
        Assert.Contains("Deliverable 'consult' if compares input.kind to 'maybe', which it does not declare (values: new, follow_up).",
            Errors(d => d with { If = "input.kind == \"maybe\"" }));
        Assert.Contains("Deliverable 'consult' if reads node.sections, which loops; a condition decides before the loop runs — read a scalar node.",
            Errors(d => d with { If = "node.sections.content != \"\"" }));
        Assert.Contains("Deliverable 'consult' if reads deliverable.consult; a condition decides before the sections compose — read a scalar node.",
            Errors(d => d with { If = "deliverable.consult.content != \"\"" }));
        Assert.Empty(Errors(d => d with { If = "node.scope.value == \"in_scope\"" }));
    }

    [Fact]
    public void AnInstanceIf_IsWalked_AtItsSite()
    {
        var errors = Errors(d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: "append", If: "input.kind == \"maybe\"") } });

        Assert.Contains("Deliverable 'consult' macro 'closing' if compares input.kind to 'maybe', which it does not declare (values: new, follow_up).", errors);
    }

    [Fact]
    public void DeliverableContent_IsReadable_AsAnEdgeOnTheSources()
    {
        var (manifest, files) = V26Package.Minimal();
        manifest = V26Package.WithNodes(manifest, new WorkflowNodeSpec("letter", "Letter", Prompt: "prompts/letter.md", Format: "prose"));
        manifest = manifest with
        {
            Deliverables = manifest.Deliverables!.Append(new WorkflowDeliverableSpec("patient_letter", null, "Patient letter", Sources: new List<string> { "node.letter", "node.sections" })).ToList(),
        };
        files["prompts/letter.md"] = "Dear doctor,\n{{ deliverable.consult.content }}";

        Assert.Empty(V26Package.FlowErrors((manifest, files)));

        var edges = WorkflowDerivedEdges.Derive(manifest, files, V25Package.Catalog());
        Assert.Equal(new[] { "assessment", "sections" }, edges["letter"].Order());
    }

    [Fact]
    public void DeliverableContent_HasNoValue_NoPipe_AndIsDeclared()
    {
        List<string> Reading(string text) => V26Package.FlowErrors(m => m, files => files["prompts/assessment.md"] = text);

        Assert.Contains("Node 'assessment' prompt reads deliverable.consult.value, which is not a member (deliverable.<id> has .content).", Reading("{{ deliverable.consult.value }}"));
        Assert.Contains("Node 'assessment' prompt pipes deliverable.consult.content through concept_bullets, but deliverable.consult composes sections; concept_bullets renders a concept_list node (node.<id>).", Reading("{{ deliverable.consult.content | concept_bullets }}"));
        Assert.Contains("Node 'assessment' prompt reads deliverable 'nope', which the package does not declare.", Reading("{{ deliverable.nope.content }}"));
        Assert.Contains("Node 'assessment' prompt reads deliverable 'consult' bare; read deliverable.consult.content.", Reading("{{ deliverable.consult }}"));
    }

    [Fact]
    public void ASourceReadingItsDeliverable_IsACycle()
    {
        var errors = V26Package.FlowErrors(m => m, files => files["prompts/assessment.md"] = "Assess. {{ deliverable.consult.content }}");

        Assert.Contains(errors, e => e.StartsWith("nodes contain a cycle involving:", StringComparison.Ordinal) && e.Contains("assessment", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryDeliverable_IncludesALoop()
    {
        var errors = Errors(d => d with { Sources = new List<string> { "node.assessment" } });

        Assert.Contains("Deliverable 'consult' must transitively include at least one loop (a for node): a deliverable with no loop has no consult. Name a for node among its sources or read one from a source (loops in this package: sections).", errors);
    }

    [Fact]
    public void EveryNode_FeedsADeliverable()
    {
        var errors = V26Package.FlowErrors(
            m => V26Package.WithNodes(m, new WorkflowNodeSpec("aside", "Aside", Prompt: "prompts/aside.md", Format: "prose")),
            files => files["prompts/aside.md"] = "Aside {{ input.notes }}.");

        Assert.Contains("Node 'aside' does not feed the deliverable: every node must transitively reach 'consult's sources. Read it from a node that does, or name it among the deliverable's sources.", errors);
    }

    [Fact]
    public void TheProbe_RendersADeliverableRead()
    {
        var (manifest, files) = V26Package.Minimal();
        manifest = V26Package.WithNodes(manifest, new WorkflowNodeSpec("letter", "Letter", Prompt: "prompts/letter.md", Format: "prose"));
        manifest = manifest with
        {
            Deliverables = manifest.Deliverables!.Append(new WorkflowDeliverableSpec("patient_letter", null, "Patient letter", Sources: new List<string> { "node.letter", "node.sections" })).ToList(),
        };
        files["prompts/letter.md"] = "{{ deliverable.consult.content | string.upcase }} {{ for e in node.sections }}{{ e.content }}{{ end }}";

        var errors = V26Package.FlowErrors((manifest, files));

        Assert.DoesNotContain(errors, e => e.Contains("failed strict rendering", StringComparison.Ordinal));
        Assert.Empty(errors);
    }
}
