using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1151 (#1149): the specVersion 26 conformance vectors — the program manifest
/// (package-format-v26-design.md). The valid baseline is the design's minimal
/// package (V26Package.Minimal, grown by #1100–#1102); the migrated-25 baseline
/// (`v26-minimal-is-the-migrated-v25-minimal`) joins with #1152's rewriter. Each
/// invalid case breaks one rule the 26 path built — one per sentence — against
/// the minimal or, where the key must be present, the 25 minimal raised; the
/// recorded outcome is whatever the validator says. Held back by the export
/// while 26 is above the accepted ceiling (the bump lifts it); the suite's
/// facts pin the sentences until then. The "requires specVersion 26" gates
/// (a 26 key on a 25 manifest, #1165) ride the bump with the rest — exported
/// early they would change the registry's supported v25 set.
/// </summary>
public partial class ConformanceFixtureExport
{
    private static void AddV26Cases(List<Case> cases)
    {
        void Invalid(string id, string description, (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
            => cases.Add(new Case(id, 26, description, bundle.Manifest, bundle.Files));
        void Valid(string id, string description, (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
            => cases.Add(new Case(id, 26, description, bundle.Manifest, bundle.Files));

        // ----- bases -----
        var m = V26Package.Minimal();
        var raised = V26Package.FromV25();

        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Manifest((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, Func<WorkflowPackageManifest, WorkflowPackageManifest> change)
            => (change(b.Manifest), b.Files);
        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Files((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, Action<Dictionary<string, string>> change)
        {
            var files = new Dictionary<string, string>(b.Files, StringComparer.Ordinal);
            change(files);
            return (b.Manifest, files);
        }
        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Node((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, string id, Func<WorkflowNodeSpec, WorkflowNodeSpec> change)
            => Manifest(b, x => V26Package.WithNode(x, id, change));
        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Deliverable((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, Func<WorkflowDeliverableSpec, WorkflowDeliverableSpec> change)
            => Manifest(b, x => x with { Deliverables = x.Deliverables!.Select(change).ToList() });
        static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Sourcing((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) b, string source)
            => Deliverable(b, d => d with { Sources = (d.Sources ?? new List<string>()).Append(source).ToList() });
        static WorkflowFlowBodySpec Body(string? ret, params string[] nodes) => new(nodes.ToList(), ret);
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) P(string text) => Files(m, fs => fs["prompts/assessment.md"] = text);
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Loop(Func<WorkflowNodeSpec, WorkflowNodeSpec> change) => Node(m, "sections", change);

        // An if node over two prose nodes, sourced; a case node over input.kind, sourced.
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Triage(string condition = "input.kind == \"new\"", WorkflowFlowBodySpec? then = null, WorkflowFlowBodySpec? @else = null) =>
            Sourcing(Manifest(m, x => V26Package.WithNodes(x,
                new WorkflowNodeSpec("full_workup", "Full work-up", Prompt: "prompts/extract.md", Format: "prose"),
                new WorkflowNodeSpec("brief_update", "Brief update", Prompt: "prompts/extract.md", Format: "prose"),
                new WorkflowNodeSpec("triage", "Triage", If: condition, Then: then ?? Body("node.full_workup", "full_workup"), Else: @else ?? Body("node.brief_update", "brief_update")))), "node.triage");
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Route(string subject = "input.kind", params WorkflowCaseBranchSpec[] branches) =>
            Sourcing(Manifest(m, x => V26Package.WithNodes(x,
                new WorkflowNodeSpec("plan", "Plan", Prompt: "prompts/extract.md", Format: "prose"),
                new WorkflowNodeSpec("letter", "Letter", Prompt: "prompts/extract.md", Format: "prose"),
                new WorkflowNodeSpec("route", "Route", Case: subject, Branches: branches.Length == 0
                    ? new List<WorkflowCaseBranchSpec>
                    {
                        new(new List<string> { "new" }, new List<string> { "plan" }, "node.plan"),
                        new(new List<string> { "follow_up" }, new List<string> { "letter" }, "node.letter"),
                    }
                    : branches.ToList()))), "node.route");

        // ======================================================= valid
        Valid("v26-minimal",
            "The design's minimal 26 package: a prose node reading an input and a snippet, a concept_list node, a classification taking its values through with, a terms_subset check, a loop over a snippet folder returning its one body node, a macro placed after the prose node, one deliverable composing the prose node and the loop, checked.",
            m);
        Valid("v26-if-node", "An if node over an input, each branch returning a prose node, sourced by the deliverable.", Triage());
        Valid("v26-case-node", "A case node over an enum input, one branch per value returning a prose node, sourced by the deliverable.", Route());
        Valid("v26-loop-read-outside", "The outside iterates the loop: the element's item, the returned node's content and a body node by id; the loop's text whole.",
            P("Assess.\n{{ for e in node.sections }}{{ e.item.name }}: {{ e.content }} / {{ e.section_notes.content }}{{ end }}\n{{ node.sections.content }}"));
        Valid("v26-two-deliverables-share-a-source", "Two deliverables compose the same loop.",
            Manifest(m, x => x with { Deliverables = x.Deliverables!.Append(new WorkflowDeliverableSpec("letter", null, "Letter", Sources: new List<string> { "node.sections" })).ToList() }));
        Valid("v26-deliverable-content-read", "A node reads a deliverable's composed text (deliverable.<id>.content) and is a second deliverable's source.",
            Manifest(Files(m, fs => fs["prompts/letter.md"] = "Dear doctor,\n{{ deliverable.consult.content }}"), x => V26Package.WithNodes(x, new WorkflowNodeSpec("letter", "Letter", Prompt: "prompts/letter.md", Format: "prose"))
                with { Deliverables = x.Deliverables!.Append(new WorkflowDeliverableSpec("patient_letter", null, "Patient letter", Sources: new List<string> { "node.letter", "node.sections" })).ToList() }));

        // ======================================================= § 8: retired by name (#1150)
        Invalid("invalid-prompts-at-26", "prompts[] at 26 — retired; a node names its prompt file.", Manifest(m, x => x with { Prompts = new List<WorkflowPromptSpec> { new("assessment", "prompts/assessment.md", new List<string>()) } }));
        Invalid("invalid-node-foreach-at-26", "forEach on a node at 26 — retired; a for node whose body holds the node.", Node(m, "section_notes", n => n with { ForEach = "snippet.sections" }));
        Invalid("invalid-node-when-at-26", "when on a node at 26 — retired; an if node whose body holds the node.", Node(m, "assessment", n => n with { When = "input.kind == \"new\"" }));
        Invalid("invalid-node-aggregate-at-26", "aggregate on a node at 26 — retired; a deliverable names its sources.", Manifest(m, x => V26Package.WithNodes(x, new WorkflowNodeSpec("all", "Everything", Aggregate: new List<string> { "node.assessment" }))));
        Invalid("invalid-instance-foritem-at-26", "forItem on a node instance at 26 — retired; the instance's if on item.id.", Node(m, "section_notes", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, ForItem: "hpi") } }));
        Invalid("invalid-instance-when-at-26", "when on a node instance at 26 — retired; a gate is keyed if.", Node(m, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, When: "input.kind == \"new\"") } }));
        Invalid("invalid-deliverable-node-at-26", "node on a deliverable at 26 — retired; a deliverable names its sources.", Deliverable(m, d => d with { Node = "node.sections" }));
        Invalid("invalid-deliverable-when-at-26", "when on a deliverable at 26 — retired; a gate is keyed if.", Deliverable(m, d => d with { When = "input.kind == \"new\"" }));
        Invalid("invalid-deliverable-instance-when-at-26", "when on a deliverable instance at 26 — retired; a gate is keyed if.", Deliverable(m, d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: "append", When: "input.kind == \"new\"") } }));
        Invalid("invalid-folder-item-when-at-26", "when on a snippet folder item at 26 — retired; a gate is keyed if.", raised);

        // ======================================================= § 1: the flow nodes (#1100)
        Invalid("invalid-two-flow-keys-at-26", "A node carrying for and if — one flow node.", Loop(n => n with { If = "input.kind == \"new\"" }));
        Invalid("invalid-flow-node-with-format-at-26", "A for node declaring a format — a flow node is structural.", Loop(n => n with { Format = "prose" }));
        Invalid("invalid-body-key-without-flow-at-26", "ret on a plain node — a body key belongs to a flow node.", Node(m, "assessment", n => n with { Ret = "node.extract" }));
        Invalid("invalid-then-on-a-for-at-26", "then on a for node — a key the kind does not carry.", Loop(n => n with { Then = Body("node.section_notes", "section_notes") }));
        Invalid("invalid-loop-grammar-at-26", "A for that is not \"item in <collection>\".", Loop(n => n with { For = "snippet.sections" }));
        Invalid("invalid-loop-over-a-file-snippet-at-26", "A loop over a snippet that is a file, not a folder.", Loop(n => n with { For = "item in snippet.standards" }));
        Invalid("invalid-loop-over-a-node-value-at-26", "A loop over a node's value — waits for the scheduler redesign.", Loop(n => n with { For = "item in node.extract.value" }));
        Invalid("invalid-loop-without-body-at-26", "A for node with no nodes.", Loop(n => n with { Nodes = null }));
        Invalid("invalid-ret-missing-at-26", "A for node without ret — required.", Loop(n => n with { Ret = null }));
        Invalid("invalid-ret-not-in-body-at-26", "A ret naming a node outside the body.", Loop(n => n with { Ret = "node.assessment" }));
        Invalid("invalid-ret-not-node-dot-at-26", "A ret that is not node.<id>.", Loop(n => n with { Ret = "section_notes" }));
        Invalid("invalid-node-in-two-bodies-at-26", "A node attached to a loop and to an if's then.", Triage(then: Body("node.section_notes", "section_notes")));
        Invalid("invalid-undeclared-body-node-at-26", "A body naming a node the package does not declare.", Loop(n => n with { Nodes = new List<string> { "section_notes", "nope" } }));
        Invalid("invalid-for-inside-a-for-at-26", "A loop inside a loop — nesting waits for the scheduler redesign.",
            Manifest(m, x => V26Package.WithNodes(x,
                new WorkflowNodeSpec("paragraphs", "Paragraphs", For: "item in snippet.sections", Nodes: new List<string> { "assessment" }, Ret: "node.assessment"),
                new WorkflowNodeSpec("outer", "Outer", For: "item in snippet.sections", Nodes: new List<string> { "paragraphs" }, Ret: "node.paragraphs"))));
        Invalid("invalid-if-inside-a-for-at-26", "An if node inside a loop's body — at 26 a body holds plain nodes.", Node(Triage(), "sections", n => n with { Nodes = new List<string> { "section_notes", "triage" } }));
        Invalid("invalid-if-without-then-at-26", "An if node without then.", Node(Triage(), "triage", n => n with { Then = null }));
        Invalid("invalid-case-without-branches-at-26", "A case node without branches.", Node(Route(), "route", n => n with { Branches = new List<WorkflowCaseBranchSpec>() }));
        Invalid("invalid-case-over-a-text-at-26", "A case switching on a text input — a closed set only.", Route("input.notes"));
        Invalid("invalid-branch-value-outside-the-set-at-26", "A branch value the enum does not declare.",
            Route("input.kind", new WorkflowCaseBranchSpec(new List<string> { "new" }, new List<string> { "plan" }, "node.plan"), new WorkflowCaseBranchSpec(new List<string> { "maybe" }, new List<string> { "letter" }, "node.letter")));
        Invalid("invalid-value-in-two-branches-at-26", "A value taken by two branches.",
            Route("input.kind", new WorkflowCaseBranchSpec(new List<string> { "new" }, new List<string> { "plan" }, "node.plan"), new WorkflowCaseBranchSpec(new List<string> { "new", "follow_up" }, new List<string> { "letter" }, "node.letter")));
        Invalid("invalid-empty-when-at-26", "A branch whose when lists no value.",
            Route("input.kind", new WorkflowCaseBranchSpec(new List<string>(), new List<string> { "plan" }, "node.plan")));
        Invalid("invalid-outside-reads-a-body-node-at-26", "The outside reading a body node by bare id — the loop is what it reads.", P("Assess. {{ node.section_notes.content }}"));
        Invalid("invalid-bodies-of-two-loops-align-at-26", "A body node of one loop reading a body node of another — through a snippet, judged at the reader.",
            Files(Manifest(m, x => V26Package.WithNodes(x,
                new WorkflowNodeSpec("per_section", "Per section", Prompt: "prompts/per_section.md", Format: "prose"),
                new WorkflowNodeSpec("other", "Other", For: "item in snippet.sections", Nodes: new List<string> { "per_section" }, Ret: "node.per_section"))),
                fs =>
                {
                    fs["prompts/per_section.md"] = "{{ item.content }} {{ snippet.standards.content }}";
                    fs["snippets/standards.md"] = "Standards. {{ node.section_notes.content }}";
                }));
        Invalid("invalid-body-node-reads-its-loop-at-26", "A body node reading the loop it runs in.", Files(m, fs => fs["prompts/section.md"] = "{{ item.content }} {{ node.sections.content }}"));
        Invalid("invalid-if-expression-reads-a-loop-at-26", "An if node's expression reading a loop — a condition decides before the loop runs.", Triage("node.sections.content != \"\""));
        Invalid("invalid-body-node-neither-returned-nor-read-at-26", "A body node nobody returns or reads.",
            Files(Node(Manifest(m, x => V26Package.WithNodes(x, new WorkflowNodeSpec("draft", "Draft", Prompt: "prompts/draft.md", Format: "prose"))), "sections", n => n with { Nodes = new List<string> { "draft", "section_notes" } }),
                fs => fs["prompts/draft.md"] = "Draft {{ item.name }}."));

        // ======================================================= § 3: the deliverable (#1101)
        Invalid("invalid-deliverable-without-sources-at-26", "A deliverable naming no sources.", Deliverable(m, d => d with { Sources = null }));
        Invalid("invalid-source-not-node-dot-at-26", "A source that is not node.<id>.", Deliverable(m, d => d with { Sources = new List<string> { "sections" } }));
        Invalid("invalid-source-unknown-at-26", "A source naming an undeclared node.", Deliverable(m, d => d with { Sources = new List<string> { "node.sections", "node.nope" } }));
        Invalid("invalid-source-twice-at-26", "A source listed twice.", Deliverable(m, d => d with { Sources = new List<string> { "node.sections", "node.sections" } }));
        Invalid("invalid-source-a-concept-list-at-26", "A concept_list node as a source — a deliverable composes prose.", Deliverable(m, d => d with { Sources = new List<string> { "node.extract", "node.sections" } }));
        Invalid("invalid-source-a-check-at-26", "A check as a source — it gates, it is no text.", Deliverable(m, d => d with { Sources = new List<string> { "node.coverage", "node.sections" } }));
        Invalid("invalid-source-a-body-node-at-26", "A body node as a source — name the loop.", Deliverable(m, d => d with { Sources = new List<string> { "node.section_notes" } }));
        Invalid("invalid-deliverable-if-reads-a-loop-at-26", "A deliverable's if reading a loop.", Deliverable(m, d => d with { If = "node.sections.content != \"\"" }));
        Invalid("invalid-deliverable-content-in-a-condition-at-26", "A deliverable's if reading deliverable.<id>.content — a condition decides before the sections compose.", Deliverable(m, d => d with { If = "deliverable.consult.content != \"\"" }));
        Invalid("invalid-deliverable-content-value-at-26", "deliverable.<id>.value — not a member.", P("{{ deliverable.consult.value }}"));
        Invalid("invalid-deliverable-content-piped-at-26", "A renderer pipe on a deliverable's composed text.", P("{{ deliverable.consult.content | concept_bullets }}"));
        Invalid("invalid-source-reads-its-deliverable-at-26", "A source reading its own deliverable's text — a cycle.", P("Assess. {{ deliverable.consult.content }}"));
        Invalid("invalid-deliverable-without-a-loop-at-26", "A deliverable whose closure holds no loop — no consult.", Deliverable(m, d => d with { Sources = new List<string> { "node.assessment" } }));
        Invalid("invalid-node-feeds-no-deliverable-at-26", "A node that reaches no deliverable's closure.",
            Files(Manifest(m, x => V26Package.WithNodes(x, new WorkflowNodeSpec("aside", "Aside", Prompt: "prompts/aside.md", Format: "prose"))), fs => fs["prompts/aside.md"] = "Aside {{ input.notes }}."));

        // ======================================================= § 4: the prompt on the node (#1102)
        Invalid("invalid-prompt-is-an-id-at-26", "A node naming a prompt by id — at 26 prompt is the file.", Node(m, "assessment", n => n with { Prompt = "assessment" }));
        Invalid("invalid-prompt-file-missing-at-26", "A node naming a prompt file the package lacks.", Node(m, "assessment", n => n with { Prompt = "prompts/nope.md" }));
    }
}
