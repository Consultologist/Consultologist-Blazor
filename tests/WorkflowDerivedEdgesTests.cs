using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// B3 #1002: the derived edges of a 25 package come from ONE walk —
/// <see cref="WorkflowDerivedEdges"/> — that the validator calls at publish
/// and load and the job starter calls at job start, so the scheduler can never
/// see a graph the validator did not.
/// </summary>
public class WorkflowDerivedEdgesTests
{
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Derive((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        WorkflowDerivedEdges.Derive(bundle.Manifest, bundle.Files, V25Package.Catalog());

    [Fact]
    public void TheMinimalPackage_ReadsThroughPromptsAndWith()
    {
        var edges = Derive(V25Package.Minimal());

        Assert.Equal(new[] { "all", "assessment", "coverage", "extract", "scope", "section_notes" }, edges.Keys.Order());
        Assert.Equal(new[] { "extract" }, edges["assessment"].Order());   // {{ node.extract.value.concepts }}
        Assert.Equal(new[] { "extract" }, edges["coverage"].Order());     // with: of/in = node.extract
        Assert.Empty(edges["extract"]);
        Assert.Empty(edges["scope"]);
        Assert.Empty(edges["section_notes"]);                             // reads item.* only
        Assert.Empty(edges["all"]);                                       // aggregate joins in WorkflowNodeClosure.Edges, not here
    }

    [Fact]
    public void ARead_TheValidatorRefuses_IsTheReadTheStarterWouldSee()
    {
        // extract reads assessment, assessment reads extract: the validator
        // refuses the cycle; Derive reports exactly that edge — one walk.
        var bundle = V25Package.WithPrompt(V25Package.Minimal(), "extract", "{{ node.assessment.content }}\n{{ input.notes }}");

        Assert.Contains(V25Package.Errors(bundle), e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "assessment" }, Derive(bundle)["extract"].Order());
    }

    [Fact]
    public void AFannedPrompt_ReadsItsFanAlignedUpstream()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Prompts = manifest.Prompts!.Append(new WorkflowPromptSpec("per_section", "prompts/per_section.md", new List<string>())).ToList(),
            Nodes = manifest.Nodes!
                .Select(n => n.Id == "all" ? n with { Aggregate = new List<string> { "node.assessment", "node.per_section" } } : n)
                .Append(new WorkflowNodeSpec("per_section", "Per section", Prompt: "per_section", Format: "prose", ForEach: "snippet.sections"))
                .ToList(),
        };
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["prompts/per_section.md"] = "{{ item.name }}\n{{ node.section_notes.content }}\n{{ node.assessment.content }}",
        };

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));
        Assert.Equal(new[] { "assessment", "section_notes" }, Derive((manifest, files))["per_section"].Order());
    }

    [Fact]
    public void ASnippetThatReadsANode_IsAnEdgeOfEveryPromptThatReadsTheSnippet()
    {
        // assessment reads snippet.standards; standards reads node.scope.content:
        // assessment waits on scope (design § 7, "through a snippet it reads").
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/standards.md"] = "Follow the local standards for {{ node.scope.content }}.",
        };

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));
        Assert.Equal(new[] { "extract", "scope" }, Derive((manifest, files))["assessment"].Order());
    }

    [Fact]
    public void AFolderItemThatReadsANode_IsAnEdgeOfTheFanThatReadsItemText()
    {
        // section_notes fans snippet:sections and reads item.content; plan.md
        // reads node.scope.content: every instance waits on scope.
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/sections/plan.md"] = "Plan for {{ node.scope.content }}.",
        };

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));
        Assert.Equal(new[] { "scope" }, Derive((manifest, files))["section_notes"].Order());
    }

    [Fact]
    public void AMacroTextThatReadsANode_IsAnEdgeOfThePromptThatReadsIt()
    {
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["macros/closing.md"] = "Closing: {{ profile.name }} on {{ node.scope.content }}.",
        };
        var bundle = V25Package.WithPrompt((manifest, files), "extract", "{{ macro.closing.content }}\n{{ input.notes }}");

        Assert.Equal(new[] { "scope" }, Derive(bundle)["extract"].Order());
    }

    [Fact]
    public void AnInstanceTextThatReadsANode_IsAnEdgeOfTheNodeItIsPlacedOn()
    {
        // B5 #1004: assessment places closing after its output; closing.md reads
        // node.scope.content — the text composes at assessment's completion, over
        // scope's output, so assessment waits on scope.
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["macros/closing.md"] = "Closing: {{ profile.name }} on {{ node.scope.content }}.",
        };

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));
        Assert.Equal(new[] { "extract", "scope" }, Derive((manifest, files))["assessment"].Order());
    }

    [Fact]
    public void AnInstructionThatReadsANode_IsAnEdgeOfTheNodeWhosePromptReadsItInline()
    {
        // #1044: the instruction renders where macro.closing.prompt is read (the
        // prompt, through its snippet), over that node's scope — so what it
        // reads, the node waits on.
        var (manifest, files) = V25Package.Minimal();
        manifest = V25Package.WithNode(manifest, "assessment", n => n with { Macros = null });
        manifest = manifest with { Macros = manifest.Macros!.Select(m => m with { Instruction = "Close for {{ node.scope.content }}." }).ToList() };
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/standards.md"] = "Standards. {{ macro.closing.prompt }}",
        };

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));
        Assert.Equal(new[] { "extract", "scope" }, Derive((manifest, files))["assessment"].Order());
    }

    [Fact]
    public void AnInlineMacroThatReadsANode_IsAnEdgeOfTheNodeWhosePromptInstructsIt()
    {
        // The prompt reads macro.closing.prompt (through its snippet); the
        // fill renders at this node, so its reads are this node's edges — and
        // the instance is gone, so the slot alone carries them.
        var (manifest, files) = V25Package.Minimal();
        manifest = V25Package.WithNode(manifest, "assessment", n => n with { Macros = null });
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/standards.md"] = "Standards. {{ macro.closing.prompt }}",
            ["macros/closing.md"] = "Closing: {{ node.scope.content }}.",
        };

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));
        Assert.Equal(new[] { "extract", "scope" }, Derive((manifest, files))["assessment"].Order());
    }

    [Fact]
    public void ACycleThroughAnInstanceText_IsRefused()
    {
        // closing reads node.assessment.content and is placed on assessment: the
        // node would wait on itself.
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["macros/closing.md"] = "Closing: {{ node.assessment.content }}.",
        };

        Assert.Contains(V25Package.Errors((manifest, files)), e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("assessment", Derive((manifest, files))["assessment"]);
    }

    [Fact]
    public void AnIfNodesExpression_IsAFlowCondition_AndItsReadsAreTheFlowNodesEdges()
    {
        // #1100: an if node's expression is walked at its own site; what it reads
        // the flow node waits on, and the engine's stage one reads it as gating.
        var (manifest, files) = V26Package.Minimal();
        manifest = V26Package.WithNodes(manifest,
            new WorkflowNodeSpec("full_workup", "Full work-up", Prompt: "prompts/assessment.md", Format: "prose"),
            new WorkflowNodeSpec("triage", "Triage", If: "node.scope.value == \"in_scope\"", Then: new WorkflowFlowBodySpec(new List<string> { "full_workup" }, "node.full_workup")));
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        var condition = Assert.Single(walk.Conditions, c => c.Site == WorkflowDerivedEdges.ConditionSite.Flow);
        Assert.Equal("triage", condition.OwnerId);
        Assert.Equal("Node 'triage' if", condition.Subject);
        Assert.True(condition.Walk.IsValid, string.Join(" | ", condition.Walk.References.Errors));
        Assert.Contains("scope", walk.EdgesOf("triage"));
        Assert.Contains("scope", walk.GatingReads());
    }

    [Fact]
    public void At26_TheSharedWalk_ShapesTheLoop_AndFansItsBody()
    {
        // #1100 PR 2: the engine's and the editor's entry resolves the flow nodes as
        // the validator does — the loop's shape, its body node's fan and scope.
        var (manifest, files) = V26Package.Minimal();
        files["prompts/assessment.md"] = "Assess. {{ for e in node.sections }}{{ e.item.name }} {{ e.content }}{{ end }}";
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);

        var loop = Assert.Single(nodes, n => n.Node.Id == "sections");
        Assert.Equal(TemplateFlowKind.For, loop.Shape!.Flow!.Kind);
        Assert.IsType<SnippetFolderFan>(loop.Fan);
        var body = Assert.Single(nodes, n => n.Node.Id == "section_notes");
        Assert.IsType<SnippetFolderFan>(body.Fan);
        Assert.Equal("sections", body.Scope);
        Assert.Equal("sections", contextFor(null).Bodies!["section_notes"].Owner);

        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);
        var prompt = Assert.Single(walk.Templates, t => t.Id == "assessment");
        Assert.True(prompt.References.IsValid, string.Join(" | ", prompt.References.Errors));
        Assert.Contains("sections", walk.EdgesOf("assessment"));

        var derived = WorkflowDerivedEdges.Derive(manifest, files, V25Package.Catalog());
        Assert.Contains("sections", derived["assessment"]);
    }

    [Fact]
    public void At26_TheWalk_ReadsANodesPromptByPath_UnderTheNodesSubject()
    {
        // #1102: no prompts[] at 26 — the node names its file; the walked template
        // is the node's, in the family of "Node 'x' condition".
        var (manifest, files) = V26Package.Minimal();
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        var prompt = Assert.Single(walk.Templates, t => t.Kind == TemplateKind.Prompt && t.Id == "assessment");
        Assert.Equal("Node 'assessment' prompt", prompt.Subject);
        Assert.True(walk.HasPrompt("assessment"));
        Assert.False(walk.HasPrompt("coverage"));
        Assert.Contains("extract", walk.EdgesOf("assessment"));
    }

    [Fact]
    public void At26_ADeliverableRead_IsAnEdgeOnItsSources_AtTheSharedWalk()
    {
        // #1101: deliverable.consult.content read by a node makes that node wait on
        // consult's sources — on the validator's walk and on the engine's alike.
        var (manifest, files) = V26Package.Minimal();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Append(new WorkflowNodeSpec("letter", "Letter", Prompt: "prompts/letter.md", Format: "prose")).ToList(),
        };
        files["prompts/letter.md"] = "Dear doctor,\n{{ deliverable.consult.content }}";

        var derived = Derive((manifest, files));

        Assert.Equal(new[] { "assessment", "sections" }, derived["letter"].Order());
        Assert.Contains("consult", contextOf((manifest, files)).Deliverables!);

        static TemplateReferenceContext contextOf((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
        {
            WorkflowDerivedEdges.PrepareNodes(bundle.Manifest, bundle.Files, V25Package.Catalog(), out _, out var contextFor);
            return contextFor(null);
        }
    }

    [Fact]
    public void PromptReadsOf_ClosesOverSnippets_AndTheFanItemThatRan()
    {
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/standards.md"] = "Standards. {{ macro.closing.prompt }}",
            ["snippets/sections/hpi.md"] = "History. {{ macro.closing.prompt }}",
        };
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        Assert.Equal(new[] { "closing" }, walk.PromptReadsOf("assessment"));            // through snippet.standards
        Assert.Equal(new[] { "closing" }, walk.PromptReadsOf("section_notes"));         // at least one item (hpi)
        Assert.Equal(new[] { "closing" }, walk.PromptReadsOf("section_notes", "hpi"));  // the instance that ran for hpi
        Assert.Empty(walk.PromptReadsOf("section_notes", "plan"));                      // plan.md carries no read
        Assert.Empty(walk.PromptReadsOf("extract"));
        Assert.Empty(walk.PromptReadsOf("coverage"));                                   // no prompt at all
        Assert.True(walk.HasPrompt("assessment"));
        Assert.False(walk.HasPrompt("coverage"));
    }

    [Fact]
    public void PromptReadSitesOf_NamesTheHolder_AndTheFanItemItCameThrough()
    {
        // #1087: the same closure as PromptReadsOf, with where each read sits.
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/standards.md"] = "Standards. {{ macro.closing.prompt }}",
            ["snippets/sections/hpi.md"] = "History. {{ macro.closing.prompt }}",
        };
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        // assessment reads the snippet that holds the read: the holder is the snippet, no fan item.
        Assert.Equal(
            new[] { new WorkflowDerivedEdges.PromptReadSite("closing", TemplateKind.Snippet, "standards", "Snippet 'standards'", null) },
            walk.PromptReadSitesOf("assessment"));

        // section_notes reaches it only through the hpi item's own text: credited to hpi.
        Assert.Equal(
            new[] { new WorkflowDerivedEdges.PromptReadSite("closing", TemplateKind.Snippet, "sections/hpi", "Snippet 'sections/hpi'", "hpi") },
            walk.PromptReadSitesOf("section_notes"));

        Assert.Empty(walk.PromptReadSitesOf("extract"));
        Assert.Empty(walk.PromptReadSitesOf("coverage"));
    }

    [Fact]
    public void PromptReadSitesOf_CreditsAPromptHeldRead_Once_AndAnItemHeldOne_ToItsItem()
    {
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["prompts/section.md"] = "{{ item.name }}: {{ item.content }} {{ macro.closing.prompt }}",
            ["snippets/sections/hpi.md"] = "History. {{ macro.closing.prompt }}",
        };
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        Assert.Equal(
            new[]
            {
                new WorkflowDerivedEdges.PromptReadSite("closing", TemplateKind.Prompt, "section_notes", "Prompt 'section'", null),
                new WorkflowDerivedEdges.PromptReadSite("closing", TemplateKind.Snippet, "sections/hpi", "Snippet 'sections/hpi'", "hpi"),
            },
            walk.PromptReadSitesOf("section_notes"));
        Assert.Equal(new[] { "closing" }, walk.PromptReadsOf("section_notes", "plan"));   // the prompt-held read reaches every item
    }

    [Fact]
    public void ANodeWithoutAFormat_ContributesNoReads()
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = V25Package.WithNode(manifest, "assessment", n => n with { Format = null });

        Assert.Empty(Derive((manifest, files))["assessment"]);
    }

    [Fact]
    public void WithoutTheCatalogue_OnlyLocalFormatsResolve()
    {
        var (manifest, files) = V25Package.Minimal();

        var edges = WorkflowDerivedEdges.Derive(manifest, files, formats: null);

        Assert.Empty(edges["assessment"]);
        Assert.Empty(edges["coverage"]);
    }


    [Fact]
    public void MacrosReached_RootsAtPromptsFanItemsInstancesAndConditions_NotAtUnplacedTexts()
    {
        // #1067: closing is placed (assessment, after); inline is read through the
        // hpi fan item; viaplaced is read by closing's text; viacondition by a
        // when; unreached is read only by its own instruction and by orphan's
        // text, and orphan by nobody.
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Macros = manifest.Macros!.Concat(new[]
            {
                new WorkflowMacroSpec("inline", "Inline", "macros/inline.md", Instruction: "Write it after the history."),
                new WorkflowMacroSpec("viaplaced", "Via placed", "macros/viaplaced.md", Instruction: "x"),
                new WorkflowMacroSpec("viacondition", "Via condition", "macros/viacondition.md", Instruction: "x"),
                new WorkflowMacroSpec("unreached", "Unreached", "macros/unreached.md", Instruction: "Write {{ macro.unreached.content }} last."),
                new WorkflowMacroSpec("orphan", "Orphan", "macros/orphan.md", Instruction: "x"),
            }).ToList(),
        };
        manifest = V25Package.WithNode(manifest, "scope", n => n with { When = "(macro.viacondition.content | string.size) > 0" });
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/sections/hpi.md"] = "History. {{ macro.inline.prompt }}",
            ["macros/closing.md"] = "Closing: {{ macro.viaplaced.content }}",
            ["macros/inline.md"] = "Inline.",
            ["macros/viaplaced.md"] = "Via placed.",
            ["macros/viacondition.md"] = "Via condition.",
            ["macros/unreached.md"] = "Unreached.",
            ["macros/orphan.md"] = "Orphan reads {{ macro.unreached.content }}",
        };
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        Assert.Equal(new[] { "closing", "inline", "viacondition", "viaplaced" }, walk.MacrosReached(Array.Empty<string>()).Order());
        Assert.Contains("orphan", walk.MacrosReached(new[] { "orphan" }));   // a deliverable instance is a root too
    }

    [Fact]
    public void AnAggregatorRead_IsAnEdge_ForTheStarter()
    {
        // #1065: the letter waits on all; all's own sources join in WorkflowNodeClosure, not here.
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Prompts = manifest.Prompts!.Append(new WorkflowPromptSpec("letter", "prompts/letter.md", new List<string>())).ToList(),
            Nodes = manifest.Nodes!
                .Append(new WorkflowNodeSpec("letter", "Letter", Prompt: "letter", Format: "prose"))
                .Append(new WorkflowNodeSpec("letter_all", "Letter, assembled", Aggregate: new List<string> { "node.letter" }))
                .ToList(),
            Deliverables = manifest.Deliverables!.Append(new WorkflowDeliverableSpec("letter", "node.letter_all", "Letter")).ToList(),
        };
        files = new Dictionary<string, string>(files, StringComparer.Ordinal) { ["prompts/letter.md"] = "Letter from:\n{{ node.all.content }}" };

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));
        Assert.Equal(new[] { "all" }, Derive((manifest, files))["letter"].Order());
        Assert.Empty(Derive((manifest, files))["all"]);
    }
}

/// <summary>Design § 5.3: the mechanics sentence is a constant of the format, pinned to the registry's published text.</summary>
public class WorkflowMacroMechanicsTests
{
    private static string RegistryDocument()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "external", "consultologist-package-format", "package-format-v25.md");
    }

    [Fact]
    public void TheSentence_IsTheRegistrysBlockquote_WordForWord()
    {
        var lines = File.ReadAllLines(RegistryDocument());
        var start = Array.FindIndex(lines, l => l.StartsWith("> If so, write", StringComparison.Ordinal));
        Assert.True(start >= 0, "the registry document no longer carries the mechanics blockquote");

        var quoted = lines.Skip(start).TakeWhile(l => l.StartsWith("> ", StringComparison.Ordinal)).Select(l => l[2..].Trim());
        var published = string.Join(' ', quoted).Replace("`", string.Empty, StringComparison.Ordinal);

        Assert.Equal(WorkflowMacroMechanics.Sentence, published);
    }

    [Fact]
    public void ThePrompt_IsTheInstruction_ABlankLine_AndTheSentenceNamingTheRead()
    {
        var prompt = WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed.\n");

        Assert.Equal(
            "Add a closing line if the plan changed.\n\nIf so, write {{ macro.closing.content }} verbatim and nothing else at that place; do not write that content yourself — it is filled in afterward.",
            prompt);
        Assert.DoesNotContain("<id>", prompt);
        Assert.DoesNotContain("[[slot:", prompt);
    }
}
