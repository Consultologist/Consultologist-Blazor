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
        var bundle = V25Package.WithPrompt(V25Package.Minimal(), "extract", "{{ node.assessment.text }}\n{{ input.notes }}");

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
                .Select(n => n.Id == "all" ? n with { Aggregate = new List<string> { "node:assessment", "node:per_section" } } : n)
                .Append(new WorkflowNodeSpec("per_section", "Per section", Prompt: "per_section", Format: "prose", ForEach: "snippet:sections"))
                .ToList(),
        };
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["prompts/per_section.md"] = "{{ item.name }}\n{{ node.section_notes.text }}\n{{ node.assessment.text }}",
        };

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));
        Assert.Equal(new[] { "assessment", "section_notes" }, Derive((manifest, files))["per_section"].Order());
    }

    [Fact]
    public void ASnippetThatReadsANode_IsAnEdgeOfEveryPromptThatReadsTheSnippet()
    {
        // assessment reads snippet.standards; standards reads node.scope.text:
        // assessment waits on scope (design § 7, "through a snippet it reads").
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/standards.md"] = "Follow the local standards for {{ node.scope.text }}.",
        };

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));
        Assert.Equal(new[] { "extract", "scope" }, Derive((manifest, files))["assessment"].Order());
    }

    [Fact]
    public void AFolderItemThatReadsANode_IsAnEdgeOfTheFanThatReadsItemText()
    {
        // section_notes fans snippet:sections and reads item.text; plan.md
        // reads node.scope.text: every instance waits on scope.
        var (manifest, files) = V25Package.Minimal();
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/sections/plan.md"] = "Plan for {{ node.scope.text }}.",
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
            ["macros/closing.md"] = "Closing: {{ profile.name }} on {{ node.scope.text }}.",
        };
        var bundle = V25Package.WithPrompt((manifest, files), "extract", "{{ macro.closing.text }}\n{{ input.notes }}");

        Assert.Equal(new[] { "scope" }, Derive(bundle)["extract"].Order());
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
        var start = Array.FindIndex(lines, l => l.StartsWith("> If so, emit", StringComparison.Ordinal));
        Assert.True(start >= 0, "the registry document no longer carries the mechanics blockquote");

        var quoted = lines.Skip(start).TakeWhile(l => l.StartsWith("> ", StringComparison.Ordinal)).Select(l => l[2..].Trim());
        var published = string.Join(' ', quoted).Replace("`", string.Empty, StringComparison.Ordinal);

        Assert.Equal(WorkflowMacroMechanics.Sentence, published);
    }

    [Fact]
    public void ThePrompt_IsTheInstruction_ABlankLine_AndTheSentenceNamingTheMarker()
    {
        var prompt = WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed.\n");

        Assert.Equal(
            "Add a closing line if the plan changed.\n\nIf so, emit [[slot:closing]] verbatim and nothing else at that place; do not write that content yourself — it is filled in afterward, and only when it applies.",
            prompt);
        Assert.DoesNotContain("<id>", prompt);
    }
}
