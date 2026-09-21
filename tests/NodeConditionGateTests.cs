using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #822 (package-format v18): a node's own `when` gates it, and the drop
/// cascades. A gated-out CONTENT node holes every deliverable whose aggregator
/// reaches it, so that deliverable is skipped (its private nodes prune with it);
/// deliverables that do not consume the gated node still fire. Decided by the
/// starter's own fire-set path, so it holds at start and at the boundary.
/// </summary>
public class NodeConditionGateTests
{
    private static readonly Dictionary<string, ConsultInputValue> FollowUp = new(StringComparer.Ordinal)
    {
        ["consult_draft"] = "65M, adenocarcinoma of the lung, for chemoradiation.",
        ["encounter_kind"] = "follow_up"
    };

    private static readonly Dictionary<string, ConsultInputValue> NewPatient = new(StringComparer.Ordinal)
    {
        ["consult_draft"] = "65M, adenocarcinoma of the lung, for chemoradiation.",
        ["encounter_kind"] = "new_patient"
    };

    // The two-deliverable v18 package (consult_note ← assemble-note; patient_letter
    // ← assemble-letter ← contextualize), with a when on `contextualize` — the
    // node only the letter consumes.
    private static WorkflowPackage Package(string contextualizeWhen)
    {
        var manifest = V18Fixtures.WithNodeWhen("contextualize", contextualizeWhen);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, V6Fixtures.Files(manifest), errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            SchemaContracts: TestOutputContracts.CatalogSchemas,
            Data: data,
            Results: new List<WorkflowResolvedResult>
            {
                new("consult_note", "assemble-note", "Consultation note"),
                new("patient_letter", "assemble-letter", "Patient letter")
            });
    }

    [Fact]
    public void AGatedContentNode_SkipsOnlyTheDeliverableThatConsumesIt()
    {
        // encounter_kind is new_patient, so `contextualize` (when: follow_up)
        // does not hold: the letter that aggregates it is skipped, the note is not.
        var decision = ConsultGenerationJobStarter.DecideFireSet(
            Package("encounter_kind == follow_up"), NewPatient, null);

        Assert.Equal("consult_note", Assert.Single(decision.Firing).Id);
        var skipped = Assert.Single(decision.Skipped);
        Assert.Equal("patient_letter", skipped.ResultId);
        Assert.Contains("contextualize", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AGatedContentNode_AndItsDeliverable_ArePrunedFromTheRun()
    {
        var decision = ConsultGenerationJobStarter.DecideFireSet(
            Package("encounter_kind == follow_up"), NewPatient, null);

        Assert.DoesNotContain(decision.Package.Nodes!, node => node.Id == "contextualize");
        Assert.DoesNotContain(decision.Package.Nodes!, node => node.Id == "assemble-letter");
        Assert.Contains(decision.Package.Nodes!, node => node.Id == "assemble-note");
    }

    [Fact]
    public void WhenTheGateHolds_BothDeliverablesFire()
    {
        // encounter_kind is follow_up, so `contextualize` holds and nothing is gated.
        var decision = ConsultGenerationJobStarter.DecideFireSet(
            Package("encounter_kind == follow_up"), FollowUp, null);

        Assert.Equal(new[] { "consult_note", "patient_letter" }, decision.Firing.Select(r => r.Id));
        Assert.Empty(decision.Skipped);
        Assert.Contains(decision.Package.Nodes!, node => node.Id == "contextualize");
    }
}
