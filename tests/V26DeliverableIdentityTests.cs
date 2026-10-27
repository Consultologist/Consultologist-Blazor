using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1155's done-when, offline (package-format-v26-design.md § 3, § 3.1): the
/// migrated example-v12-constructs — a deliverable over a fan and a scalar —
/// composes from its sources the parts and the ladder its 25 aggregator
/// composed, and extract_note_terms reads them through
/// deliverable.consult.content. Skips when the packages repo is not checked out.
/// </summary>
public class V26DeliverableIdentityTests
{
    private static readonly string Package = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "Consultologist", "consultologist-workflows", "packages", "example-v12-constructs");

    private static NodeRunResult Output(string text) => new(text, null, "in-" + text.Length, "out-" + text.Length, 6);

    [Fact]
    public void TheMigratedV12Constructs_ComposesItsDeliverable_AsThe25AggregatorDid_AndTheReaderReadsIt()
    {
        if (!Directory.Exists(Package))
        {
            return;
        }

        var files = Directory.EnumerateFiles(Package, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("manifest.json", StringComparison.Ordinal) && !p.EndsWith("dag.mmd", StringComparison.Ordinal) && !p.EndsWith(".stamp.json", StringComparison.Ordinal))
            .ToDictionary(p => Path.GetRelativePath(Package, p).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
        var migrated = WorkflowPackageMigration25To26.Migrate(File.ReadAllText(Path.Combine(Package, "manifest.json")), files);
        Assert.True(migrated.Succeeded, string.Join(" | ", migrated.Refusals));
        var manifest = WorkflowPackageManifestJson.Read(migrated.ManifestJson, "example-v12-constructs", V26Package.Accepting26.ToList());

        var errors = new List<string>();
        var texts = WorkflowTextHeaders.Resolve(manifest, migrated.Files, errors);
        var data = WorkflowDataResolver.ResolveContent(manifest, texts, errors);
        Assert.Empty(errors);
        var package = new WorkflowPackage(manifest, Nodes: manifest.Nodes, Data: data, SourceFiles: migrated.Files,
            Results: WorkflowPackageStore.ResolveResultSet(manifest), Formats: TestFormats.Catalog, FormatsRef: TestFormats.Catalog.ResolvedRef);

        // The snapshot the starter takes: the rows, with their reads and their deliverable reads.
        var flows = ConsultGenerationJobStarter.FlowsOf(package);
        var derived = ConsultGenerationJobStarter.DeriveEdges(package)!;
        var deliverableReads = ConsultGenerationJobStarter.NodeDeliverableReads(package)!;
        Assert.Equal(new[] { "consult" }, deliverableReads["extract_note_terms"]);
        Assert.Equal(new[] { "ack_letter" }, deliverableReads["extract_letter_terms"]);
        var nodesById = package.Nodes!
            .Select(node => ConsultGenerationJobStarter.DescribeNode(node, null, formats: package.Formats, reads: derived[node.Id], flows: flows, deliverableReads: deliverableReads[node.Id]))
            .ToDictionary(n => n.Id, StringComparer.Ordinal);
        var results = package.Results!
            .Select(result => ConsultGenerationJobStarter.DescribeResult(result, result.Macros, result.MacroPlacements, null, null))
            .ToList();
        var consult = ConsultDeliverables.Resolve(results, null, nodesById).First(d => d.ResultId == "consult");
        Assert.Equal(new[] { "standards_section_instructions", "patient_header" }, consult.Sources);
        Assert.Equal(new[] { "section_instructions", "patient_header" }, consult.SourceIds);

        // The run: the fan's items done, the scalar done, the loop settled as the engine settles it.
        var standards = data.Collections["standards"].Items
            .Select(item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(item.Fields, StringComparer.Ordinal) { ["id"] = item.Id })
            .ToList();
        Assert.Equal(new[] { "hpi", "pmh" }, standards.Select(item => item["id"]));
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["section_instructions:hpi"] = Output("Write the history."),
            ["section_instructions:pmh"] = Output("Write the past history."),
            ["patient_header"] = Output("Prepared regarding the consultation of 2026-01-02."),
        };
        var loop = ConsultFlowCompletion.TryCompose(nodesById["standards_section_instructions"], nodesById, outputs, _ => standards, (_, _) => false)!;
        outputs["standards_section_instructions"] = loop.Result!;

        var composed = ConsultDeliverableComposition.TryCompose(consult, nodesById, outputs, _ => standards, (_, _) => false)!;

        // The 25 aggregator's parts, in its order, through its renderer and ladder.
        var expected = ConsultAggregateRenderer.Render(new ConsultAggregateRenderer.Part[]
        {
            new ConsultAggregateRenderer.ForEachPart(new[]
            {
                ("hpi", "History of Present Illness", "Write the history."),
                ("pmh", "Past Medical History", "Write the past history."),
            }),
            new ConsultAggregateRenderer.ScalarPart("Prepared regarding the consultation of 2026-01-02."),
        });
        Assert.Null(composed.Failure);
        Assert.Equal(expected, composed.Rendered);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeAggregateInputHash(new[] { outputs["section_instructions:hpi"].OutputHash, outputs["section_instructions:pmh"].OutputHash, outputs["patient_header"].OutputHash }),
            composed.InputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(expected), composed.ComposedHash);
        // The loop's own text is the fan part alone — the deliverable's prefix.
        Assert.StartsWith(loop.Result!.RawOutput, composed.Rendered, StringComparison.Ordinal);

        // The reader: its migrated prompt reads the composed text.
        var prompt = migrated.Files["prompts/extract-note-terms.md"];
        Assert.Contains("{{ deliverable.consult.content }}", prompt, StringComparison.Ordinal);
        var input = new ConsultPromptNodeActivityInput("extract_note_terms", "prompts/extract-note-terms.md",
            new Dictionary<string, string>(manifest.Inputs!.ToDictionary(i => i.Id, _ => "x", StringComparer.Ordinal)), package.Ref,
            Format: "concept_list", Producer: "attested", NodeTexts: new Dictionary<string, string>(),
            Run: new TemplateRunFacts("2026-01-02", "job", package.Ref, "host"), Profile: new TemplateProfileFacts("Dr Example", string.Empty),
            Deliverables: ConsultRenderPayload.DeliverableTextsFor(nodesById["extract_note_terms"].DeliverableReads,
                new Dictionary<string, ConsultDeliverableComposition.Composed> { ["consult"] = composed }, new HashSet<string> { "consult", "ack_letter" }));

        Assert.Equal("Extract the clinical terms as concept-list JSON: " + expected, ConsultRenderPayload.RenderAt25(input, package, prompt));
    }
}
