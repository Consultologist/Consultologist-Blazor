using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1234 (package-format-v27-design.md § 9 A): the specVersion 27 conformance
/// vectors — a flow node inside a for. The valid baseline is the 26 minimal
/// raised (§ 7: no migration); each valid case is a shape 27 opens, each
/// invalid one breaks one § 3 sentence against it; the recorded outcome is
/// whatever the validator says. Held back by the export while 27 is above the
/// accepted ceiling (the bump, #1237, lifts them); the suite's facts pin the
/// sentences until then. The below-27 gate (`invalid-if-inside-a-for-at-26`)
/// is a 26 case and exports now, with its new sentence.
/// </summary>
public partial class ConformanceFixtureExport
{
    private static void AddV27Cases(List<Case> cases)
    {
        void Invalid(string id, string description, (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
            => cases.Add(new Case(id, 27, description, bundle.Manifest, bundle.Files));
        void Valid(string id, string description, (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
            => cases.Add(new Case(id, 27, description, bundle.Manifest, bundle.Files));

        var m = V27Package.Minimal();
        var triaged = V27Package.WithTriageInLoop(m);
        static WorkflowFlowBodySpec Body(string? ret, params string[] nodes) => new(nodes.ToList(), ret);

        // ======================================================= valid
        Valid("v27-minimal-is-the-v26-minimal-raised", "The 26 minimal at specVersion 27 — a version raise, nothing else (§ 7).", m);
        Valid("v27-loop-returns-an-if", "A loop whose body holds an if node reading the item, its then reading the loop's earlier body node one level up; the loop returns the flow node and the deliverable composes it.", triaged);
        Valid("v27-loop-returns-a-case-on-the-items-enum-field", "A loop over an array input of objects whose body is a case node switching on the item's enum field (item.value.kind), one branch per value; the loop returns it.", V27Package.WithRouteInLoop(m));
        Valid("v27-loop-returns-a-plain-node-beside-a-gated-branch", "A loop returning a plain body node that reads the inner if node by bare id — the flow node gates a side branch.",
            V27Package.With(triaged,
                x => V26Package.WithNode(V26Package.WithNodes(x, new WorkflowNodeSpec("summary", "Summary", Prompt: "prompts/summary.md", Format: "prose")),
                    "sections", n => n with { Nodes = new List<string> { "section_notes", "triage", "summary" }, Ret = "node.summary" }),
                files => files["prompts/summary.md"] = "Summary of {{ node.triage.content }} after {{ node.section_notes.content }}."));
        Valid("v27-condition-reads-an-earlier-body-node", "An if inside a loop whose condition reads an earlier body node of the loop, item-aligned.", V27Package.WithTriageInLoop(m, "node.section_notes.content != \"\""));
        Valid("v27-outside-iterates-a-loop-returning-an-if", "The outside iterates the loop: the element's item, the returned (branch) content, the inner flow node and a body node by id; the loop's text whole.",
            V27Package.With(triaged, files: files => files["prompts/assessment.md"] = "Assess.\n{{ for e in node.sections }}{{ e.item.label }}: {{ e.content }} / {{ e.triage.content }} / {{ e.section_notes.content }}{{ end }}\n{{ node.sections.content }}"));

        // ======================================================= § 3: the sentences
        Invalid("invalid-branch-node-attached-twice-at-27", "A branch node listed in the loop's nodes[] too — attached once, through its flow node.",
            V27Package.With(triaged, x => V26Package.WithNode(x, "sections", n => n with { Nodes = new List<string> { "section_notes", "triage", "patient_draft" } })));
        Invalid("invalid-if-inside-an-if-inside-a-for-at-27", "An if node in a branch of an if inside a loop — at 27 a branch holds plain nodes (one level).",
            V27Package.With(triaged, x => V26Package.WithNode(V26Package.WithNodes(x,
                    new WorkflowNodeSpec("detail", "Detail", If: "item.id == \"plan\"", Then: Body("node.table_draft", "table_draft"))),
                "triage", n => n with { Then = Body("node.patient_draft", "patient_draft", "detail") })));
        Invalid("invalid-condition-reads-its-loop-at-27", "An inner if whose condition reads the loop it runs in.", V27Package.WithTriageInLoop(m, "node.sections.content != \"\""));
        Invalid("invalid-condition-reads-its-own-branch-at-27", "An inner if whose condition reads a node of its own branch — decided after the condition.", V27Package.WithTriageInLoop(m, "node.patient_draft.content != \"\""));
        Invalid("invalid-condition-reads-another-loops-body-node-at-27", "An inner if whose condition reads a body node of another loop.",
            V27Package.With(V27Package.WithTriageInLoop(m, "node.other_notes.content != \"\""), x => V26Package.WithNodes(x,
                new WorkflowNodeSpec("other_notes", "Other notes", Prompt: "prompts/section.md", Format: "prose"),
                new WorkflowNodeSpec("other", "Other", For: "item in snippet.sections", Nodes: new List<string> { "other_notes" }, Ret: "node.other_notes"))));
        Invalid("invalid-case-on-a-folder-items-field-at-27", "A case inside a loop over a snippet folder switching on item.value.kind — a folder item's fields are text.",
            V27Package.With(m, x => V26Package.WithNode(V26Package.WithNodes(x,
                    new WorkflowNodeSpec("route", "Route", Case: "item.value.kind", Branches: new List<WorkflowCaseBranchSpec> { new(new List<string> { "narrative" }, new List<string> { "section_notes" }, "node.section_notes") })),
                "sections", n => n with { Nodes = new List<string> { "route" }, Ret = "node.route" })));
        Invalid("invalid-case-on-a-text-field-of-the-item-at-27", "A case switching on a text field of the item — a closed set only.", V27Package.WithRouteInLoop(m, "item.value.title"));
        Invalid("invalid-loop-value-over-disagreeing-branches-at-27", "The outside reads .value of a loop returning an if whose branches return different formats; the deliverable sourcing it names the non-prose branch.",
            V27Package.With(V27Package.WithTriageInLoop(m, then: Body("node.full_extract", "full_extract")),
                x => V26Package.WithNodes(x, new WorkflowNodeSpec("full_extract", "Full extract", Prompt: "prompts/full_extract.md", Format: "concept_list", OnInvalid: WorkflowOnInvalid.Fail)),
                files =>
                {
                    files["prompts/full_extract.md"] = "Extract from {{ item.content }}.";
                    files["prompts/assessment.md"] = "Assess. {{ node.sections.value }}";
                }));
        Invalid("invalid-inner-flow-neither-returned-nor-read-at-27", "An if inside a loop that the loop neither returns nor a body node reads.", V27Package.WithTriageInLoop(m, ret: "node.section_notes"));
        Invalid("invalid-outside-reads-the-inner-flow-at-27", "The outside reading the inner flow node by name — read the loop.", V27Package.With(triaged, files: files => files["prompts/assessment.md"] = "Assess. {{ node.triage.content }}"));
        Invalid("invalid-loop-sibling-reads-a-branch-node-at-27", "A loop's body node reading a node of the inner if's branch — the outside of a branch reads its flow node.",
            V27Package.With(triaged, files: files => files["prompts/section.md"] = "{{ item.content }} {{ node.patient_draft.content }}"));
        Invalid("invalid-branch-node-reads-its-loop-at-27", "A branch node reading the loop its flow node runs in.", V27Package.With(triaged, files: files => files["prompts/patient.md"] = "{{ node.sections.content }}"));
    }
}
