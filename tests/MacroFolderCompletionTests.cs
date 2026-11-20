using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// A prompt that reads a macro folder's items inline (<c>{{ for m in macro.&lt;folder&gt; }}{{ m.prompt }}</c>,
/// #1099) completes: the cross-check at completion walks the package with its macro folders,
/// as the validators do, so the items the prompt rendered are reads the walk reaches.
/// Found by the byte-equivalence captures of area 01 (PR #1512): example-macros failed at
/// node summary with "the walk and the render disagree".
/// </summary>
public class MacroFolderCompletionTests
{
    private static readonly TemplateRunFacts Run = new("2026-11-20", "abcdef12", "general@v2026.11.1", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");
    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal) { ["notes"] = "Referral.", ["kind"] = "new" };
    private static readonly Dictionary<string, string> Supplied = new(StringComparer.Ordinal) { ["notes"] = "\"Referral.\"", ["kind"] = "\"new\"" };

    private static WorkflowPackageManifest WithFolder(WorkflowPackageManifest m) =>
        m with { Macros = m.Macros!.Append(new WorkflowMacroSpec("section_notes", null, "macros/section_notes/")).ToList() };

    private static void FolderFiles(Dictionary<string, string> files)
    {
        files["macros/section_notes/index.json"] = """[ { "id": "hpi", "file": "hpi.md" }, { "id": "plan", "file": "plan.md" } ]""";
        files["macros/section_notes/hpi.md"] = V26Package.Header("HPI notes", instruction: "Note the history.") + "History noted: {{ profile.name }}.";
        files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes", instruction: "Note the plan.") + "Plan noted.";
        files["prompts/assessment.md"] = "Assess.\n{{ for m in macro.section_notes }}- {{ m.label }}: {{ m.prompt }}\n{{ end }}";
    }

    [Fact]
    public void APromptReadingAMacroFoldersItems_Completes()
    {
        var built = V26JobStartTests.Package(WithFolder, FolderFiles);
        var errors = new List<string>();
        var package = built with { MacroFolders = WorkflowTextHeaders.ResolveMacroFolders(built.Manifest, built.Texts!, errors) };
        Assert.Empty(errors);
        var node = package.Nodes!.First(n => n.Id == "assessment");
        var descriptor = ConsultGenerationJobStarter.DescribeNode(
            node, null, macroFiles: package.TextFiles, macros: package.EffectiveManifest.Macros, formats: package.Formats, effectiveInputs: Inputs, manifest: package.EffectiveManifest,
            flows: ConsultGenerationJobStarter.FlowsOf(package));
        var input = new ConsultPromptNodeActivityInput(
            ConsultNodeScheduler.InstanceKey("assessment", null), node.Prompt!, Inputs, package.Ref,
            Format: descriptor.Format, Producer: descriptor.Producer,
            NodeTexts: new Dictionary<string, string> { ["extract"] = """{ "concepts": [ "Asthma" ] }""", ["scope"] = "in_scope" },
            Run: Run, Profile: Profile, Supplied: Supplied, Classifications: new Dictionary<string, string> { ["scope"] = "in_scope" },
            FailIfEmpty: descriptor.FailIfEmpty, Instances: descriptor.Macros);
        var trace = new TemplateRenderer.TemplateRenderTrace();
        ConsultRenderPayload.RenderAt25(input, package, package.TextOf(node.Prompt!), trace);
        var reads = trace.PromptReads.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new[] { "section_notes/hpi", "section_notes/plan" }, reads.Order(StringComparer.Ordinal));

        const string reply = "Plan.\n\n{{ macro.section_notes.hpi.content }}";
        var settled = new NodeRunResult(reply, null, "in", ConsultGenerationProvenance.Sha256Hex(reply), ConsultGenerationProvenance.NodeHashVersionAt25,
            AgentOutputHash: ConsultGenerationProvenance.Sha256Hex(reply));

        var result = ConsultNodeCompletion.Complete(settled, input, package, reads);

        Assert.StartsWith("Plan.", result.RawOutput);
    }
}
