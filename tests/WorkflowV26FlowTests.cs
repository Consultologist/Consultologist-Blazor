using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1100 (package-format-v26-design.md § 1): the flow nodes' structure on the
/// 26 path — one flow key, structural, the keys its kind carries; a loop's
/// collection; bodies of declared plain nodes attached once, no nesting; ret
/// required and a member; a case over a closed set; the bodies' scope on the
/// edges; a condition reads no loop; a body node returned or read. The reads a
/// template makes of a flow node are the second PR's.
/// </summary>
public class WorkflowV26FlowTests
{
    private static WorkflowNodeSpec Loop(string id = "sections", string over = "item in snippet.sections", string? ret = "node.section_notes", params string[] body) =>
        new(id, "Sections", For: over, Nodes: body.Length == 0 ? new List<string> { "section_notes" } : body.ToList(), Ret: ret);

    private static WorkflowFlowBodySpec Body(string? ret, params string[] nodes) => new(nodes.ToList(), ret);


    /// <summary>The minimal with an if node `triage` over two prose nodes added beside the loop.</summary>
    private static WorkflowPackageManifest WithTriage(WorkflowPackageManifest m, string condition = "input.kind == \"new\"", WorkflowFlowBodySpec? then = null, WorkflowFlowBodySpec? @else = null) =>
        Sourcing(V26Package.WithNodes(m,
            new("full_workup", "Full work-up", Prompt: "prompts/extract.md", Format: "prose"),
            new("brief_update", "Brief update", Prompt: "prompts/extract.md", Format: "prose"),
            new("triage", "Triage", If: condition, Then: then ?? Body("node.full_workup", "full_workup"), Else: @else ?? Body("node.brief_update", "brief_update"))), "node.triage");

    /// <summary>#1101: a flow node a test adds feeds the deliverable as one more source.</summary>
    private static WorkflowPackageManifest Sourcing(WorkflowPackageManifest m, string source) =>
        m with { Deliverables = m.Deliverables!.Select(d => d with { Sources = (d.Sources ?? new List<string>()).Append(source).ToList() }).ToList() };

    /// <summary>The minimal with a case node `route` over input.kind, two branches of one prose node each.</summary>
    private static WorkflowPackageManifest WithRoute(WorkflowPackageManifest m, string subject = "input.kind", params WorkflowCaseBranchSpec[] branches) =>
        Sourcing(V26Package.WithNodes(m,
            new("plan", "Plan", Prompt: "prompts/extract.md", Format: "prose"),
            new("letter", "Letter", Prompt: "prompts/extract.md", Format: "prose"),
            new("route", "Route", Case: subject, Branches: branches.Length == 0
                ? new List<WorkflowCaseBranchSpec>
                {
                    new(new List<string> { "new" }, new List<string> { "plan" }, "node.plan"),
                    new(new List<string> { "follow_up" }, new List<string> { "letter" }, "node.letter"),
                }
                : branches.ToList())), "node.route");

    // ----- the minimal, and what a flow node declares ---------------------------

    [Fact]
    public void TheMinimal_MeetsNoFlowSentence()
    {
        var errors = V26Package.FlowErrors(V26Package.Minimal());

        Assert.Empty(errors);
    }

    [Fact]
    public void AnIfNode_AndACaseNode_MeetNoFlowSentence()
    {
        Assert.Empty(V26Package.FlowErrors(m => WithTriage(m)));
        Assert.Empty(V26Package.FlowErrors(m => WithRoute(m)));
    }

    [Fact]
    public void TwoFlowKeys_AreOneTooMany()
    {
        var errors = V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => n with { If = "input.kind == \"new\"" }));

        Assert.Contains("Node 'sections' declares for, if; a node is one flow node — for, if or case.", errors);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("prompt")]
    [InlineData("with")]
    [InlineData("onInvalid")]
    [InlineData("macros")]
    public void AFlowNodeWithANodesKey_IsStructural(string key)
    {
        var errors = V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => key switch
        {
            "format" => n with { Format = "prose" },
            "prompt" => n with { Prompt = "section" },
            "with" => n with { With = new Dictionary<string, JsonNode?> { ["of"] = "node.extract" } },
            "onInvalid" => n with { OnInvalid = WorkflowOnInvalid.Fail },
            _ => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing") } },
        }));

        Assert.Contains($"Node 'sections' declares for and {key}; a flow node is structural.", errors);
    }

    [Theory]
    [InlineData("nodes")]
    [InlineData("ret")]
    [InlineData("then")]
    [InlineData("branches")]
    public void ABodyKeyWithoutAFlowKey_IsRefused(string key)
    {
        var errors = V26Package.FlowErrors(m => V26Package.WithNode(m, "assessment", n => key switch
        {
            "nodes" => n with { Nodes = new List<string> { "extract" } },
            "ret" => n with { Ret = "node.extract" },
            "then" => n with { Then = Body("node.extract", "extract") },
            _ => n with { Branches = new List<WorkflowCaseBranchSpec> { new(new List<string> { "new" }, new List<string> { "extract" }, "node.extract") } },
        }));

        Assert.Contains($"Node 'assessment' declares {key}, which belongs to a flow node; declare for, if or case.", errors);
    }

    [Fact]
    public void AKeyTheKindDoesNotCarry_IsRefused()
    {
        Assert.Contains("Node 'sections' declares then, which a for node does not carry.",
            V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => n with { Then = Body("node.section_notes", "section_notes") })));
        Assert.Contains("Node 'triage' declares nodes, which an if node does not carry.",
            V26Package.FlowErrors(m => V26Package.WithNode(WithTriage(m), "triage", n => n with { Nodes = new List<string> { "extract" } })));
        Assert.Contains("Node 'route' declares then, which a case node does not carry.",
            V26Package.FlowErrors(m => V26Package.WithNode(WithRoute(m), "route", n => n with { Then = Body("node.plan", "plan") })));
    }

    // ----- the loop's collection -------------------------------------------------

    [Theory]
    [InlineData("snippet.sections", "Loop 'sections' iterates 'snippet.sections'; a loop iterates \"item in snippet.<folder>\" or \"item in input.<id>\".")]
    [InlineData("x in snippet.sections", "Loop 'sections' iterates 'x in snippet.sections'; a loop iterates \"item in snippet.<folder>\" or \"item in input.<id>\".")]
    [InlineData("item in data:sections", "Loop 'sections' iterates 'item in data:sections'; a loop iterates \"item in snippet.<folder>\" or \"item in input.<id>\".")]
    [InlineData("item in snippet.standards", "Loop 'sections' iterates snippet 'standards', which is a file, not a folder.")]
    [InlineData("item in snippet.nope", "Loop 'sections' iterates snippet folder 'nope', which the package does not declare.")]
    [InlineData("item in input.nope", "Loop 'sections' iterates undeclared input 'nope'.")]
    [InlineData("item in input.notes", "Loop 'sections' iterates input 'notes', which is a text; only an array can be iterated.")]
    [InlineData("item in node.extract.value", "Loop 'sections' iterates node.extract.value; a loop over a node's value waits for the scheduler redesign — iterate an input or a snippet folder.")]
    public void ALoop_IteratesASnippetFolderOrAnArrayInput(string over, string sentence)
    {
        var errors = V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => n with { For = over }));

        Assert.Contains(sentence, errors);
    }

    [Fact]
    public void ALoopOverAnArrayInput_TypesTheItem()
    {
        var errors = V26Package.FlowErrors(
            m => V26Package.WithNode(m with { Inputs = m.Inputs!.Append(new WorkflowInputSpec("history", "History", false, "array", Items: new WorkflowElementSpec("text"))).ToList() },
                "sections", n => n with { For = "item in input.history" }),
            files => files["prompts/section.md"] = "{{ item.value }} {{ item.content }}");

        Assert.Contains(errors, e => e.Contains("reads item.content, which an input fan item does not carry", StringComparison.Ordinal));
    }

    // ----- the bodies and ret ----------------------------------------------------

    [Fact]
    public void ALoopWithoutABody_IsRefused()
    {
        var errors = V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => n with { Nodes = null }));

        Assert.Contains("Loop 'sections' has no body; name the nodes that run per item in nodes.", errors);
    }

    [Fact]
    public void Ret_IsRequired_OnALoop_AThen_AnElse_AndABranch()
    {
        Assert.Contains("Loop 'sections' returns nothing; name the body node the outside reads with ret.",
            V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => n with { Ret = null })));
        Assert.Contains("Node 'triage' then returns nothing; name the body node the outside reads with ret.",
            V26Package.FlowErrors(m => WithTriage(m, then: Body(null, "full_workup"))));
        Assert.Contains("Node 'triage' else returns nothing; name the body node the outside reads with ret.",
            V26Package.FlowErrors(m => WithTriage(m, @else: Body(null, "brief_update"))));
        Assert.Contains("Node 'route' branch 2 returns nothing; name the body node the outside reads with ret.",
            V26Package.FlowErrors(m => WithRoute(m, "input.kind",
                new WorkflowCaseBranchSpec(new List<string> { "new" }, new List<string> { "plan" }, "node.plan"),
                new WorkflowCaseBranchSpec(new List<string> { "follow_up" }, new List<string> { "letter" }, null))));
    }

    [Fact]
    public void Ret_IsSpelledNodeDot_AndNamesABodyNode()
    {
        Assert.Contains("Loop 'sections' ret 'section_notes' must be node.<id>.",
            V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => n with { Ret = "section_notes" })));
        Assert.Contains("Loop 'sections' returns node.assessment, which is not in its body (nodes: section_notes).",
            V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => n with { Ret = "node.assessment" })));
    }

    [Fact]
    public void ABodyNode_IsDeclared_AndListedOnce()
    {
        Assert.Contains("Loop 'sections' names node 'nope', which the package does not declare.",
            V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => n with { Nodes = new List<string> { "section_notes", "nope" } })));
        Assert.Contains("Loop 'sections' lists node 'section_notes' twice; a body is a set.",
            V26Package.FlowErrors(m => V26Package.WithNode(m, "sections", n => n with { Nodes = new List<string> { "section_notes", "section_notes" } })));
    }

    [Fact]
    public void ANodeInTwoBodies_RunsInOne()
    {
        var errors = V26Package.FlowErrors(m => WithTriage(m, then: Body("node.section_notes", "section_notes")));

        Assert.Contains("Node 'section_notes' runs inside loop 'sections' and inside node 'triage'; a node runs in one body.", errors);
    }

    [Fact]
    public void AForInsideAFor_WaitsForTheSchedulerRedesign()
    {
        var errors = V26Package.FlowErrors(m => V26Package.WithNodes(m,
            new("paragraphs", "Paragraphs", For: "item in snippet.sections", Nodes: new List<string> { "assessment" }, Ret: "node.assessment"),
            new("outer", "Outer", For: "item in snippet.sections", Nodes: new List<string> { "paragraphs" }, Ret: "node.paragraphs")));

        Assert.Contains("Loop 'paragraphs' runs inside loop 'outer'; nesting waits for the scheduler redesign.", errors);
    }

    [Fact]
    public void AnIfInsideAFor_AndAForInsideAnIf_AreLaterIssues()
    {
        Assert.Contains("Node 'triage' runs inside loop 'sections'; at specVersion 26 a body holds plain nodes — a flow node inside a body is a later issue.",
            V26Package.FlowErrors(m => V26Package.WithNode(WithTriage(m), "sections", n => n with { Nodes = new List<string> { "section_notes", "triage" } })));
        Assert.Contains("Loop 'sections' runs inside node 'triage'; nesting waits for the scheduler redesign.",
            V26Package.FlowErrors(m => WithTriage(m, then: Body("node.sections", "sections"))));
    }

    [Fact]
    public void AnIfWithoutThen_AndACaseWithoutBranches_AreRefused()
    {
        Assert.Contains("Node 'triage' declares if without then; an if node runs a then body.",
            V26Package.FlowErrors(m => V26Package.WithNode(WithTriage(m), "triage", n => n with { Then = null })));
        Assert.Contains("Node 'route' declares case without branches; a case node runs one branch per set of values.",
            V26Package.FlowErrors(m => V26Package.WithNode(WithRoute(m), "route", n => n with { Branches = new List<WorkflowCaseBranchSpec>() })));
    }

    // ----- a case over a closed set ----------------------------------------------

    [Fact]
    public void ACase_SwitchesOnAnEnumInput_OrAnEnumFormatNodesValue()
    {
        const string rule = "a case switches on an enum input (input.<id>) or an enum-format node's value (node.<id>.value)";

        Assert.Empty(V26Package.FlowErrors(m => WithRoute(m, "node.scope.value",
            new WorkflowCaseBranchSpec(new List<string> { "in_scope" }, new List<string> { "plan" }, "node.plan"),
            new WorkflowCaseBranchSpec(new List<string> { "out_of_scope" }, new List<string> { "letter" }, "node.letter"))));
        Assert.Contains($"Node 'route' switches on input.notes, which is a text; {rule}.",
            V26Package.FlowErrors(m => WithRoute(m, "input.notes")));
        Assert.Contains($"Node 'route' switches on node.assessment.value, but format 'prose' is text; {rule}.",
            V26Package.FlowErrors(m => WithRoute(m, "node.assessment.value")));
        Assert.Contains($"Node 'route' switches on node.sections.value, which is a flow node; {rule}.",
            V26Package.FlowErrors(m => WithRoute(m, "node.sections.value")));
        Assert.Contains($"Node 'route' switches on 'input.kind == \"new\"'; {rule}.",
            V26Package.FlowErrors(m => WithRoute(m, "input.kind == \"new\"")));
    }

    [Fact]
    public void ABranchValue_IsOneOfTheSet_AndTakesOneBranch()
    {
        Assert.Contains("Node 'route' branch 2 when 'maybe' is a value input.kind does not declare (values: new, follow_up).",
            V26Package.FlowErrors(m => WithRoute(m, "input.kind",
                new WorkflowCaseBranchSpec(new List<string> { "new" }, new List<string> { "plan" }, "node.plan"),
                new WorkflowCaseBranchSpec(new List<string> { "maybe" }, new List<string> { "letter" }, "node.letter"))));
        Assert.Contains("Node 'route' lists 'new' twice (branches 1 and 2); each value takes one branch.",
            V26Package.FlowErrors(m => WithRoute(m, "input.kind",
                new WorkflowCaseBranchSpec(new List<string> { "new" }, new List<string> { "plan" }, "node.plan"),
                new WorkflowCaseBranchSpec(new List<string> { "new", "follow_up" }, new List<string> { "letter" }, "node.letter"))));
        Assert.Contains("Node 'route' branch 1 lists no value; a branch's when names one or more values.",
            V26Package.FlowErrors(m => WithRoute(m, "input.kind",
                new WorkflowCaseBranchSpec(new List<string>(), new List<string> { "plan" }, "node.plan"))));
    }

    // ----- the bodies' scope on the edges ----------------------------------------

    [Fact]
    public void TheOutside_ReadingABodyNode_ReadsTheLoopInstead()
    {
        // PR 2: the walker names it where the read is written, so it never becomes an edge.
        var errors = V26Package.FlowErrors(m => m, files => files["prompts/assessment.md"] = "Assess. {{ node.section_notes.content }}");

        Assert.Contains("Node 'assessment' prompt reads node 'section_notes', which runs inside loop 'sections'; read node.sections.", errors);
        Assert.DoesNotContain("Node 'assessment' reads node 'section_notes', which runs inside loop 'sections'; read node.sections.", errors);
    }

    [Fact]
    public void ASnippet_ReadingABodyNode_IsJudgedAtItsReader()
    {
        // A snippet is walked once with no scope of its own; the outside node that
        // reads it earns the sentence on the edge (PR 1's rule), as item reads do.
        var errors = V26Package.FlowErrors(m => m, files => files["snippets/standards.md"] = "Standards. {{ node.section_notes.content }}");

        Assert.Contains("Node 'assessment' reads node 'section_notes', which runs inside loop 'sections'; read node.sections.", errors);
    }

    [Fact]
    public void TheOutside_ReadingTheLoop_IsAnEdge_AndNoSentence()
    {
        var (manifest, files) = V26Package.Minimal();
        files["prompts/assessment.md"] = "Assess. {{ node.sections.content }}";

        Assert.Empty(V26Package.FlowErrors((manifest, files)));
    }

    [Fact]
    public void BodyNodes_OfDifferentLoops_DoNotAlign()
    {
        var errors = V26Package.FlowErrors(
            m => V26Package.WithNodes(m,
                new("per_section", "Per section", Prompt: "prompts/per_section.md", Format: "prose"),
                new("other", "Other", For: "item in snippet.sections", Nodes: new List<string> { "per_section" }, Ret: "node.per_section")),
            files => files["prompts/per_section.md"] = "{{ item.content }} {{ node.section_notes.content }}");

        // The walker names the read where it is written (PR 2); a deferred text's
        // read is judged at its reader, where the two loops are named (PR 1).
        Assert.Contains("Node 'per_section' prompt reads node 'section_notes', which runs inside loop 'sections'; read node.sections.", errors);

        var deferred = V26Package.FlowErrors(
            m => V26Package.WithNodes(m,
                new("per_section", "Per section", Prompt: "prompts/per_section.md", Format: "prose"),
                new("other", "Other", For: "item in snippet.sections", Nodes: new List<string> { "per_section" }, Ret: "node.per_section")),
            files =>
            {
                files["prompts/per_section.md"] = "{{ item.content }} {{ snippet.standards.content }}";
                files["snippets/standards.md"] = "Standards. {{ node.section_notes.content }}";
            });

        Assert.Contains("Node 'per_section' reads node 'section_notes' across loops ('other' vs 'sections'); body nodes of one loop read one another.", deferred);
    }

    [Fact]
    public void ABodyNode_ReadingItsOwnLoop_IsNamed()
    {
        var errors = V26Package.FlowErrors(m => m, files => files["prompts/section.md"] = "{{ item.content }} {{ node.sections.content }}");

        Assert.Contains("Node 'section_notes' reads node.sections, the loop it runs in; a body node reads its siblings, the outside reads the loop.", errors);
        Assert.Contains(errors, e => e.StartsWith("nodes contain a cycle involving:", StringComparison.Ordinal));
    }

    [Fact]
    public void Siblings_ReadOneAnother_ItemAligned()
    {
        var errors = V26Package.FlowErrors(
            m => V26Package.WithNode(V26Package.WithNodes(m, new WorkflowNodeSpec("draft", "Draft", Prompt: "prompts/draft.md", Format: "prose")),
                "sections", n => n with { Nodes = new List<string> { "draft", "section_notes" } }),
            files =>
            {
                files["prompts/draft.md"] = "Draft {{ item.name }}.";
                files["prompts/section.md"] = "{{ item.content }} {{ node.draft.content }}";
            });

        Assert.Empty(errors);
    }

    // ----- conditions and reachability -------------------------------------------

    [Fact]
    public void AnIfExpression_IsWalkedAsACondition()
    {
        Assert.Contains(V26Package.FlowErrors(m => WithTriage(m, "{{ input.kind }}")),
            e => e.StartsWith("Node 'triage' if is a template, not a condition;", StringComparison.Ordinal));
        Assert.Contains("Node 'triage' if compares input.kind to 'maybe', which it does not declare (values: new, follow_up).",
            V26Package.FlowErrors(m => WithTriage(m, "input.kind == \"maybe\"")));
    }

    [Fact]
    public void AnIfExpression_ReadingALoop_IsRefused()
    {
        var errors = V26Package.FlowErrors(m => WithTriage(m, "node.sections.content != \"\""));

        Assert.Contains("Node 'triage' if reads node.sections, which loops; a condition decides before the loop runs — read a scalar node.", errors);
    }

    [Fact]
    public void AnIfExpression_ReadingABodyNode_IsScoped()
    {
        var errors = V26Package.FlowErrors(m => WithTriage(m, "node.section_notes.content != \"\""));

        Assert.Contains("Node 'triage' if reads node 'section_notes', which runs inside loop 'sections'; read node.sections.", errors);
    }

    [Fact]
    public void ABodyNode_NeitherReturnedNorRead_IsUnreachable()
    {
        var errors = V26Package.FlowErrors(
            m => V26Package.WithNode(V26Package.WithNodes(m, new WorkflowNodeSpec("draft", "Draft", Prompt: "prompts/draft.md", Format: "prose")),
                "sections", n => n with { Nodes = new List<string> { "draft", "section_notes" } }),
            files => files["prompts/draft.md"] = "Draft {{ item.name }}.");

        Assert.Contains("Node 'draft' in loop 'sections' is neither returned nor read; name it with ret or read it from a body node.", errors);
    }

    [Fact]
    public void AnIfOverTwoProseNodes_ReadsAsProse_AndAnIfOverTwoFormats_ReadsAsText()
    {
        // The returned nodes share a format: .value is the format's (prose has none).
        var errors = V26Package.FlowErrors(m => WithTriage(m), files => files["prompts/assessment.md"] = "Assess. {{ node.triage.content }}");
        Assert.Empty(errors);

        var valueRead = V26Package.FlowErrors(m => WithTriage(m), files => files["prompts/assessment.md"] = "Assess. {{ node.triage.value }}");
        Assert.Contains(valueRead, e => e.Contains("reads node.triage.value, but format 'prose' is text and has no value", StringComparison.Ordinal));
    }

    // ----- the reads (PR 2) --------------------------------------------------------

    [Fact]
    public void TheOutside_IteratesTheLoop_AndTheProbeRenders()
    {
        var errors = V26Package.FlowErrors(m => m, files =>
            files["prompts/assessment.md"] = "Assess.\n{{ for e in node.sections }}{{ e.item.name }}: {{ e.content }} / {{ e.section_notes.content }}{{ end }}\n{{ node.sections.content }} {{ node.sections[0].item.id }} {{ node.sections | array.size }}");

        Assert.Empty(errors);
    }

    [Fact]
    public void TheLoopsValue_OverAConceptListReturnedNode()
    {
        // A loop whose returned node is a concept_list: .value is the array of the
        // concept lists, the element's content pipes through a renderer, the loop's
        // own text does not.
        WorkflowPackageManifest WithExtractLoop(WorkflowPackageManifest m) =>
            V26Package.WithNode(V26Package.WithNodes(m,
                new WorkflowNodeSpec("per_item", "Per item", Prompt: "prompts/per_item.md", Format: "concept_list", OnInvalid: WorkflowOnInvalid.Fail)),
                "sections", n => n with { Nodes = new List<string> { "per_item", "section_notes" }, Ret = "node.per_item" })
            // #1101: a concept_list loop is no prose source — the assessment, which reads it, is the deliverable's.
            with { Deliverables = m.Deliverables!.Select(d => d with { Sources = new List<string> { "node.assessment" } }).ToList() };

        void Files(Dictionary<string, string> files, string assessment)
        {
            files["prompts/per_item.md"] = "Concepts of {{ item.content }} and {{ node.section_notes.content }}.";
            files["prompts/assessment.md"] = assessment;
        }

        Assert.Empty(V26Package.FlowErrors(WithExtractLoop, files => Files(files, "{{ for v in node.sections.value }}{{ v.concepts | array.size }}{{ end }} {{ for e in node.sections }}{{ e.content | concept_bullets }}{{ end }}")));
        Assert.Contains(V26Package.FlowErrors(WithExtractLoop, files => Files(files, "{{ node.sections.content | concept_bullets }}")),
            e => e.Contains("pipes node.sections.content through concept_bullets, but node.sections loops — a list has no one text", StringComparison.Ordinal));
        Assert.Contains(V26Package.FlowErrors(WithExtractLoop, files => Files(files, "{{ node.sections.value.concepts }}")),
            e => e.Contains("reads field 'concepts' of 'node.sections.value', which is an array of object, not an object", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIfOverTwoFormats_RefusesValueByName()
    {
        var errors = V26Package.FlowErrors(
            m => V26Package.WithNodes(m,
                new WorkflowNodeSpec("full_extract", "Full extract", Prompt: "prompts/full_extract.md", Format: "concept_list", OnInvalid: WorkflowOnInvalid.Fail),
                new WorkflowNodeSpec("brief_update", "Brief update", Prompt: "prompts/extract.md", Format: "prose"),
                new WorkflowNodeSpec("triage", "Triage", If: "input.kind == \"new\"", Then: Body("node.full_extract", "full_extract"), Else: Body("node.brief_update", "brief_update"))),
            files =>
            {
                files["prompts/full_extract.md"] = "Extract from {{ input.notes }}.";
                files["prompts/assessment.md"] = "Assess. {{ node.triage.content }} {{ node.triage.value }}";
            });

        Assert.Contains("Node 'assessment' prompt reads node.triage.value, but branches of 'triage' return 'object' and 'text'; read node.triage.content.", errors);
    }

    // ----- #1102: a node names its prompt file -------------------------------------

    [Fact]
    public void ANodesPrompt_IsItsFile_WalkedUnderTheNodesSubject()
    {
        var errors = V26Package.FlowErrors(m => m, files => files["prompts/assessment.md"] = "Assess. {{ node.nope.content }}");

        Assert.Contains("Node 'assessment' prompt reads node 'nope', which the package does not declare.", errors);
    }

    [Fact]
    public void APromptThatIsAnId_IsRefusedNamingTheFile()
    {
        var errors = V26Package.FlowErrors(m => V26Package.WithNode(m, "assessment", n => n with { Prompt = "assessment" }));

        Assert.Contains("Node 'assessment' names prompt 'assessment'; at specVersion 26 prompt is the file (prompts/assessment.md).", errors);
    }

    [Fact]
    public void APromptFileMissing_IsRefused()
    {
        Assert.Contains("Node 'assessment' prompt 'prompts/nope.md' is missing from the package.",
            V26Package.FlowErrors(m => V26Package.WithNode(m, "assessment", n => n with { Prompt = "prompts/nope.md" })));
        Assert.Contains("Node 'assessment' prompt 'nope' is missing from the package.",
            V26Package.FlowErrors(m => V26Package.WithNode(m, "assessment", n => n with { Prompt = "nope" })));
    }

    [Fact]
    public void TwoNodes_MayShareOneFile()
    {
        // Reuse is a snippet by the design's word; two nodes naming one file are
        // still two prompts, each walked as its node's.
        var errors = V26Package.FlowErrors(m => Sourcing(V26Package.WithNodes(m,
            new WorkflowNodeSpec("second_opinion", "Second opinion", Prompt: "prompts/assessment.md", Format: "prose")), "node.second_opinion"));

        Assert.Empty(errors);
    }
}
