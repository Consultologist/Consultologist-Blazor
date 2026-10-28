using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1071: the editor's preview scope — every run value a token naming its read,
/// the package's own texts rendered for real — and the one line the macro pane
/// shows: what the model is told where macro.&lt;id&gt;.prompt is read.
/// </summary>
public class TemplatePreviewScopeTests
{
    private static TemplateRenderScope Scope((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        TemplatePreviewScope.Tokens(bundle.Manifest, bundle.Files, V25Package.Catalog());

    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithInstruction(string instruction)
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with { Macros = manifest.Macros!.Select(m => m with { Instruction = instruction }).ToList() };
        return (manifest, files);
    }

    [Fact]
    public void TheMacroPrompt_IsTheInstruction_ThenTheMechanicsSentence()
    {
        var prompt = TemplatePreviewScope.MacroPrompt("closing", Scope(V25Package.Minimal()));

        Assert.Equal(WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed."), prompt);
    }

    [Fact]
    public void AReadTheRunWouldSupply_RendersAsItsToken()
    {
        var prompt = TemplatePreviewScope.MacroPrompt("closing", Scope(WithInstruction(
            "For {{ input.notes }} / {{ input.kind }} / {{ node.extract.content }} / {{ node.scope.value }} / {{ profile.name }} / {{ run.date }}.")));

        const string expected = "For ⟨input.notes⟩ / ⟨input.kind⟩ / ⟨node.extract.content⟩ / ⟨node.scope.value⟩ / ⟨profile.name⟩ / ⟨run.date⟩.";
        Assert.True(prompt.StartsWith(expected, StringComparison.Ordinal), prompt);
    }

    [Fact]
    public void AStructuredRead_RendersThePathsToken()
    {
        var prompt = TemplatePreviewScope.MacroPrompt("closing", Scope(WithInstruction(
            "{{ for c in node.extract.value.concepts }}[{{ c }}]{{ end }} {{ node.extract.content | concept_bullets }}")));

        Assert.StartsWith("[⟨node.extract.value.concepts[0]⟩] ⟨concept_bullets of node.extract.content⟩", prompt);
    }

    [Fact]
    public void ThePackagesOwnTexts_RenderForReal()
    {
        var prompt = TemplatePreviewScope.MacroPrompt("closing", Scope(WithInstruction("{{ snippet.standards.content }} {{ macro.closing.content }}")));

        // The self-read renders the macro's text — the trap #1067 warns about, visible.
        Assert.StartsWith("Follow the local standards. Closing: ⟨profile.name⟩.", prompt);
    }

    // ----- v26 (#1207): the preview is labelled at 26 — item.label, a folder item's .label, a
    // macro's .instruction, a standalone snippet's label from its header -----

    [Fact]
    public void At26_ThePreview_RendersTheLabelMembers_AndASnippetsHeaderLabel()
    {
        var (manifest, files) = WorkflowV26MacroFolderTests.WithFolder(
            "[{{ snippet.sections.hpi.label }}|{{ snippet.standards.label }}|{{ macro.section_notes.hpi.instruction }}|{{ for s in snippet.sections }}{{ s.label }},{{ end }}|{{ snippet.sections.plan.content }}]");
        var scope = TemplatePreviewScope.Tokens(manifest, files, V25Package.Catalog());

        var rendered = TemplateRenderer.Render("Node 'assessment' prompt", files["prompts/assessment.md"], scope, TemplatePreviewScope.Pipes);

        Assert.Equal("Assess. [HPI|Local standards|Note the history.|HPI,Plan,|Plan.]", rendered);
    }

    [Fact]
    public void Below26_ThePreview_IsNotLabelled()
    {
        var scope = Scope(V25Package.Minimal());

        Assert.False(scope.Labelled);
        Assert.Null(scope.SnippetLabels);
    }

    [Fact]
    public void AnInstructionThatDoesNotRender_ThrowsTheRenderersSentence()
    {
        var ex = Assert.ThrowsAny<Exception>(() => TemplatePreviewScope.MacroPrompt("closing", Scope(WithInstruction("{{ if }}"))));

        Assert.Contains("does not parse", ex.Message);
    }

    [Fact]
    public void EveryTemplateOfTheMinimalPackage_RendersOverTheScope()
    {
        var bundle = V25Package.Minimal();
        var scope = Scope(bundle);

        foreach (var (path, text) in bundle.Files.Where(f => f.Key.EndsWith(".md", StringComparison.Ordinal)))
        {
            var rendered = TemplateRenderer.Render($"'{path}'", text, scope, TemplatePreviewScope.Pipes);
            Assert.False(string.IsNullOrWhiteSpace(rendered), path);
        }
    }

    // ----- #1138: a picked sample renders for real -----

    [Fact]
    public void ARealOperand_RendersTheBytes_WithTheSourceMappedAsTheEngineMapsIt()
    {
        var concepts = ConceptOutputContract.Deserialize(ConceptSamples.ConceptListJson, "extract");

        // The token stays a token (its path keeps the raw id); real text renders through the spec.
        Assert.Equal("⟨concept_context of node.extract.content⟩",
            TemplatePreviewScope.Pipes[TemplatePipes.ConceptContext](new TemplateNodeOutput("⟨node.extract.content⟩", "object", "extract")));
        Assert.Equal(ConceptRenderers.Context(concepts),
            TemplatePreviewScope.Pipes[TemplatePipes.ConceptContext](new TemplateNodeOutput(ConceptSamples.ConceptListJson, "object", "extract")));
        Assert.Equal(ConceptRenderers.Bullets(concepts),
            TemplatePreviewScope.Pipes[TemplatePipes.ConceptBullets](new TemplateNodeOutput(ConceptSamples.ConceptListJson, "object", "extract")));

        // A well-known id renders with its legacy source, as a run does; the token names the id.
        Assert.Contains("source: patient", TemplatePreviewScope.Pipes[TemplatePipes.ConceptContext](new TemplateNodeOutput(ConceptSamples.ConceptListJson, "object", "extract_patient_concepts")), StringComparison.Ordinal);
        Assert.Equal("⟨concept_bullets of node.extract_patient_concepts.content⟩",
            TemplatePreviewScope.Pipes[TemplatePipes.ConceptBullets](new TemplateNodeOutput("⟨node.extract_patient_concepts.content⟩", "object", "extract_patient_concepts")));
    }

    [Fact]
    public void AScopeHoldingTheSample_RendersItsBytesWhereThePipeIs()
    {
        var scope = Scope(WithInstruction("Concepts:\n{{ node.extract.content | concept_bullets }}"));
        var nodes = new Dictionary<string, TemplateNodeOutput>(scope.Nodes, StringComparer.Ordinal);
        nodes["extract"] = nodes["extract"] with { Text = ConceptSamples.ConceptListJson };

        var prompt = TemplatePreviewScope.MacroPrompt("closing", scope with { Nodes = nodes });

        Assert.StartsWith("Concepts:\n- Diabetes mellitus (disorder) - 73211009 [not active SNOMED concept]", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("⟨concept_bullets", prompt, StringComparison.Ordinal);
    }
}
