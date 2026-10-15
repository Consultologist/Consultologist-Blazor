using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// B3 #1002: a 25 package at job start — the derived edges on the descriptor
/// (the validator's own walk), the fire set's cascade and prune over them,
/// every snippet folder snapshotted with its items settled by their input-only
/// <c>when</c>, a snippet fan over that snapshot.
/// </summary>
public class V25JobStartTests
{
    private static readonly Dictionary<string, ConsultInputValue> NewPatient = new(StringComparer.Ordinal)
    {
        ["notes"] = "Referral.",
        ["kind"] = "new",
    };

    private static readonly Dictionary<string, ConsultInputValue> FollowUp = new(StringComparer.Ordinal)
    {
        ["notes"] = "Referral.",
        ["kind"] = "follow_up",
    };

    /// <summary>The design's minimal package with a second folder nobody fans, as the store would hand it to the starter.</summary>
    internal static WorkflowPackage Package(
        Func<WorkflowPackageManifest, WorkflowPackageManifest>? change = null,
        Func<Dictionary<string, string>, Dictionary<string, string>>? changeFiles = null)
    {
        var (manifest, files) = V25Package.Minimal();
        manifest = manifest with
        {
            Snippets = new Dictionary<string, string>(manifest.Snippets!) { ["extras"] = "snippets/extras/" },
        };
        files = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            ["snippets/extras/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "billing", "name": "Billing", "file": "billing.md", "when": "kind == follow_up" } ] }""",
            ["snippets/extras/billing.md"] = "Bill as a follow-up.",
        };
        manifest = change?.Invoke(manifest) ?? manifest;
        files = changeFiles?.Invoke(files) ?? files;

        Assert.Equal(new List<string>(), V25Package.Errors((manifest, files)));

        var errors = new List<string>();
        var data = WorkflowDataResolver.ResolveContent(manifest, files, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            Data: data,
            SourceFiles: files,
            Results: new List<WorkflowResolvedResult> { new("consult", "all", "Consultation note") },
            Formats: V25Package.Catalog(),
            FormatsRef: V25Package.Catalog().ResolvedRef);
    }

    [Fact]
    public void TheDerivedEdges_AreTheValidatorsWalk_AndRideTheDescriptorSorted()
    {
        var package = Package();

        Assert.True(ConsultGenerationJobStarter.IsAt25(package));
        var derived = ConsultGenerationJobStarter.DeriveEdges(package)!;

        Assert.Equal(WorkflowDerivedEdges.Derive(package.Manifest, package.SourceFiles!, package.Formats), derived);
        Assert.Equal(new[] { "extract" }, derived["assessment"].Order());

        var assessment = ConsultGenerationJobStarter.DescribeNode(
            package.Nodes!.First(n => n.Id == "assessment"), null, formats: package.Formats, reads: derived["assessment"]);
        var extract = ConsultGenerationJobStarter.DescribeNode(
            package.Nodes!.First(n => n.Id == "extract"), null, formats: package.Formats, reads: derived["extract"]);

        Assert.Equal(new[] { "extract" }, assessment.Reads);
        Assert.Null(extract.Reads);
        Assert.Null(assessment.Bindings);
    }

    [Fact]
    public void Below25_NoEdgesAreDerived()
    {
        var (manifest, files) = V24Fixtures.Base();
        var package = new WorkflowPackage(manifest, Nodes: manifest.Nodes, SourceFiles: files);

        Assert.False(ConsultGenerationJobStarter.IsAt25(package));
        Assert.Null(ConsultGenerationJobStarter.DeriveEdges(package));
    }

    [Fact]
    public void TheFireSet_Cascades_ThroughATemplateRead()
    {
        // consult → all (aggregates assessment) → assessment reads
        // node.extract.value in its PROMPT — no binding, no aggregate names
        // extract. Gate extract: the deliverable is skipped through the
        // derived edge, naming the node.
        var package = Package(m => V25Package.WithNode(m, "extract", n => n with { When = "kind == follow_up" }));

        var decision = ConsultGenerationJobStarter.DecideFireSet(package, NewPatient, null);

        Assert.Empty(decision.Firing);
        var skipped = Assert.Single(decision.Skipped);
        Assert.Equal("consult", skipped.ResultId);
        Assert.Contains("needs node 'extract'", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePrune_KeepsWhatATemplateReads()
    {
        // Two deliverables; the letter is skipped by its condition. The note
        // reaches extract only through assessment's template read: without
        // the derived edge the prune would drop extract and the render would
        // find no output for node.extract.
        var package = Package(m => m with
        {
            Nodes = m.Nodes!.Append(new WorkflowNodeSpec("letter_all", "Letter", Aggregate: new List<string> { "node.assessment", "node.section_notes" })).ToList(),
            Deliverables = m.Deliverables!.Append(new WorkflowDeliverableSpec("letter", "node.letter_all", "Letter", When: "kind == follow_up")).ToList(),
        });
        package = package with
        {
            Results = new List<WorkflowResolvedResult>
            {
                new("consult", "all", "Consultation note"),
                new("letter", "letter_all", "Letter", WorkflowResultConditions.TryParseExpression("kind == follow_up", out var when, out _) ? when : null),
            },
        };

        var decision = ConsultGenerationJobStarter.DecideFireSet(package, NewPatient, null);

        Assert.Equal("consult", Assert.Single(decision.Firing).Id);
        Assert.Contains(decision.Package.Nodes!, n => n.Id == "extract");
        Assert.Contains(decision.Package.Nodes!, n => n.Id == "assessment");
        Assert.Contains(decision.Package.Nodes!, n => n.Id == "section_notes");
        Assert.DoesNotContain(decision.Package.Nodes!, n => n.Id == "letter_all");
    }

    [Fact]
    public void EveryFolder_IsSnapshotted_WithItsItemsSettledByWhen()
    {
        var package = Package();

        var newPatient = ConsultGenerationJobStarter.ResolveSkeleton(package, NewPatient);
        var followUp = ConsultGenerationJobStarter.ResolveSkeleton(package, FollowUp);

        // sections: hpi always, plan when kind == new; extras: billing when follow_up — nobody fans extras.
        Assert.Equal(new[] { "extras", "sections" }, newPatient.CollectionSets.Keys.Order());
        Assert.Equal(new[] { "hpi", "plan" }, newPatient.CollectionSets["sections"].Select(i => i["id"]));
        Assert.Empty(newPatient.CollectionSets["extras"]);
        Assert.Equal(new[] { "hpi" }, followUp.CollectionSets["sections"].Select(i => i["id"]));
        Assert.Equal(new[] { "billing" }, followUp.CollectionSets["extras"].Select(i => i["id"]));
        Assert.Equal("Bill as a follow-up.", followUp.CollectionSets["extras"][0]["content"]);

        // The rosters follow the settled set.
        Assert.Equal(new[] { "hpi" }, followUp.CollectionRosters!.Single(r => r.CollectionId == "sections").Items.Select(i => i.Id));
    }

    [Fact]
    public void ASnippetFan_RunsOverTheSettledFolder()
    {
        // section_notes fans snippet.sections; the orchestrator keys the
        // snapshot by the folder id, as a data: fan did.
        var skeleton = ConsultGenerationJobStarter.ResolveSkeleton(Package(), FollowUp);
        var descriptor = ConsultGenerationJobStarter.DescribeNode(
            Package().Nodes!.First(n => n.Id == "section_notes"), null, formats: V25Package.Catalog());

        Assert.Equal("snippet.sections", descriptor.ForEach);
        Assert.Equal(new[] { "hpi" }, skeleton.CollectionSets["sections"].Select(i => i["id"]));
    }
}
