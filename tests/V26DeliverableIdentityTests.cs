using System.Text.Json;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1155's done-when, offline (package-format-v26-design.md § 3, § 3.1): a
/// migrated package whose deliverable is over a scalar and a fan composes from
/// its sources the parts and the ladder its 25 aggregator composed, and a reader
/// of that aggregator's text reads the composed text through
/// deliverable.&lt;id&gt;.content. The migrated example-v12-constructs proved it at
/// the bump (2026-10-27); #1229 retires that read of a sibling checkout and
/// keeps the proof over the 25 minimal with a letter reading node.all.content.
/// </summary>
public class V26DeliverableIdentityTests
{
    private static NodeRunResult Output(string text) => new(text, null, "in-" + text.Length, "out-" + text.Length, 6);

    /// <summary>The 25 minimal plus a letter node reading the aggregator's text, assembled into its own deliverable.</summary>
    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithLetter()
    {
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
        files["prompts/letter.md"] = "Letter from:\n{{ node.all.content }}";
        return (manifest, files);
    }

    [Fact]
    public void TheMigratedTwin_ComposesItsDeliverable_AsThe25AggregatorDid_AndTheReaderReadsIt()
    {
        var (at25, files) = WithLetter();
        var migrated = WorkflowPackageMigration25To26.Migrate(JsonSerializer.Serialize(at25, WorkflowPackageManifestJson.WriteOptions), files);
        Assert.True(migrated.Succeeded, string.Join(" | ", migrated.Refusals));
        var manifest = WorkflowPackageManifestJson.Read(migrated.ManifestJson, "twin", V26Package.Accepting26.ToList());

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
        Assert.Equal(new[] { "consult" }, deliverableReads["letter"]);
        var nodesById = package.Nodes!
            .Select(node => ConsultGenerationJobStarter.DescribeNode(node, null, formats: package.Formats, reads: derived[node.Id], flows: flows, deliverableReads: deliverableReads[node.Id]))
            .ToDictionary(n => n.Id, StringComparer.Ordinal);
        var results = package.Results!
            .Select(result => ConsultGenerationJobStarter.DescribeResult(result, result.Macros, result.MacroPlacements, null, null))
            .ToList();
        var consult = ConsultDeliverables.Resolve(results, null, nodesById).First(d => d.ResultId == "consult");
        // The 25 aggregator `all` dissolved into the deliverable's sources — the scalar, then the loop named after its collection.
        Assert.Equal(new[] { "assessment", "sections" }, consult.Sources);
        Assert.Equal(new[] { "assessment", "section_notes" }, consult.SourceIds);

        // The run: the scalar done, the fan's items done, the loop settled as the engine settles it.
        var sections = data.Collections["sections"].Items
            .Select(item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(item.Fields, StringComparer.Ordinal) { ["id"] = item.Id })
            .ToList();
        Assert.Equal(new[] { "hpi", "plan" }, sections.Select(item => item["id"]));
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["assessment"] = Output("The assessment."),
            ["section_notes:hpi"] = Output("Write the history."),
            ["section_notes:plan"] = Output("Write the plan."),
        };
        var loop = ConsultFlowCompletion.TryCompose(nodesById["sections"], nodesById, outputs, _ => sections, (_, _) => false)!;
        outputs["sections"] = loop.Result!;

        var composed = ConsultDeliverableComposition.TryCompose(consult, nodesById, outputs, _ => sections, (_, _) => false)!;

        // The 25 aggregator's parts, in its order, through its renderer and ladder.
        var expected = ConsultAggregateRenderer.Render(new ConsultAggregateRenderer.Part[]
        {
            new ConsultAggregateRenderer.ScalarPart("The assessment."),
            new ConsultAggregateRenderer.ForEachPart(new[]
            {
                ("hpi", "HPI", "Write the history."),
                ("plan", "Plan", "Write the plan."),
            }),
        });
        Assert.Null(composed.Failure);
        Assert.Equal(expected, composed.Rendered);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeAggregateInputHash(new[] { outputs["assessment"].OutputHash, outputs["section_notes:hpi"].OutputHash, outputs["section_notes:plan"].OutputHash }),
            composed.InputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(expected), composed.ComposedHash);
        // The loop's own text is the fan part alone — found whole inside the deliverable.
        Assert.Contains(loop.Result!.RawOutput, composed.Rendered, StringComparison.Ordinal);

        // The reader: its migrated prompt reads the composed text.
        var prompt = migrated.Files["prompts/letter.md"];
        Assert.Contains("{{ deliverable.consult.content }}", prompt, StringComparison.Ordinal);
        var input = new ConsultPromptNodeActivityInput("letter", "prompts/letter.md",
            new Dictionary<string, string>(manifest.Inputs!.ToDictionary(i => i.Id, _ => "x", StringComparer.Ordinal)), package.Ref,
            Format: "prose", Producer: "attested", NodeTexts: new Dictionary<string, string>(),
            Run: new TemplateRunFacts("2026-01-02", "job", package.Ref, "host"), Profile: new TemplateProfileFacts("Dr Example", string.Empty),
            Deliverables: ConsultRenderPayload.DeliverableTextsFor(nodesById["letter"].DeliverableReads,
                new Dictionary<string, ConsultDeliverableComposition.Composed> { ["consult"] = composed }, new HashSet<string> { "consult", "letter" }));

        Assert.Equal("Letter from:\n" + expected, ConsultRenderPayload.RenderAt25(input, package, prompt));
    }
}
