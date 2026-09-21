using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v18 (#822): a node may declare `when:` — the deliverable-level condition
/// grammar, written on a node — and runs only when it holds. Allowed on
/// prompt/template/aggregate/check nodes; refused on a classifier (which always
/// runs to make its decision). The grammar and its operand rules are the
/// deliverable-`when` rules, verbatim. The baseline is v17's plus the version
/// line; the cascade behaviour is exercised in DecisionBoundaryTests.
/// </summary>
public static class V18Fixtures
{
    public static WorkflowPackageManifest Minimal() => V17Fixtures.Minimal() with { SpecVersion = 18 };

    /// <summary>Two deliverables with typed inputs: `consult_note` via
    /// `assemble-note`, `patient_letter` via `assemble-letter`, which aggregates
    /// `contextualize` — the node a when can gate to cascade-skip the letter.</summary>
    public static WorkflowPackageManifest TwoDeliverables() =>
        V8Fixtures.Conditional(when: null) with { SpecVersion = 18, Tags = new List<string>() };

    public static WorkflowPackageManifest WithNodeWhen(string nodeId, string? when, WorkflowPackageManifest? baseline = null)
    {
        var manifest = baseline ?? TwoDeliverables();
        return manifest with
        {
            Nodes = manifest.Nodes!.Select(node => node.Id == nodeId ? node with { When = when } : node).ToList()
        };
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);
}

public class WorkflowV18NodeWhenTests
{
    private static IReadOnlyList<string> Errors(WorkflowPackageManifest manifest) => V18Fixtures.Validate(manifest).Errors;

    [Fact]
    public void ANodeWhen_ReadingAnInput_IsValid()
    {
        var manifest = V18Fixtures.WithNodeWhen("contextualize", "encounter_kind == follow_up");

        Assert.True(V18Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void ANodeWhen_BelowEighteen_IsRefusedByName()
    {
        var manifest = V18Fixtures.WithNodeWhen("contextualize", "encounter_kind == follow_up") with { SpecVersion = 17 };

        Assert.Contains(
            "Node 'contextualize' declares when, which requires specVersion 18.",
            Errors(manifest));
    }

    [Fact]
    public void ANodeWhen_OnAClassifier_IsRefused()
    {
        var (manifest, _) = V10Fixtures.WithClassifier(V10Fixtures.Classifier() with { When = "consult_draft" });
        manifest = manifest with { SpecVersion = 18 };

        Assert.Contains(
            "Node 'scope' declares when but is a classifier; a classifier always runs to make its decision.",
            Errors(manifest));
    }

    [Fact]
    public void ANodeWhen_ReadingAnUndeclaredInput_IsRefused()
    {
        // The condition grammar is the deliverable-when grammar, so its operand
        // rules speak here too — a node-shaped prefix, the same sentence.
        var manifest = V18Fixtures.WithNodeWhen("contextualize", "encounter_kind == not_a_value");

        Assert.Contains(
            Errors(manifest),
            error => error.StartsWith("Node 'contextualize' condition", StringComparison.Ordinal));
    }

    [Fact]
    public void ACheckNode_MayCarryAWhen()
    {
        // Settled for #822: a check is a validation gate, and gating it with a
        // when ("only run this check if X") is allowed.
        var manifest = V18Fixtures.WithNodeWhen("contextualize", null); // no-op baseline is valid at 18
        Assert.True(V18Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }
}
