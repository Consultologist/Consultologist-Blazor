using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// specVersion-7 fixtures: the v6 single-collection package with a declared
/// input (the general-minimal proving shape) and a two-deliverable variant
/// splitting the standards and guidelines chains into separate results
/// (package-format-v7.md).
/// </summary>
public static class V7Fixtures
{
    public static WorkflowPackageManifest Minimal()
    {
        return V6Fixtures.SingleCollection() with
        {
            SpecVersion = 7,
            Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft") }
        };
    }

    /// <summary>
    /// The multi-collection fixture reshaped for two deliverables: assemble-note
    /// keeps the standards chain, a second aggregator takes the guidelines chain
    /// as its own result.
    /// </summary>
    public static WorkflowPackageManifest MultiDeliverable()
    {
        var baseline = V6Fixtures.MultiCollection() with
        {
            SpecVersion = 7,
            Inputs = new List<WorkflowInputSpec>
            {
                new("consult_draft", "Consult draft"),
                new("prior_notes", "Prior notes", Required: false)
            }
        };

        var nodes = new List<WorkflowNodeSpec>(baseline.Nodes!);
        var resultIndex = nodes.FindIndex(n => n.Id == "assemble-note");
        nodes[resultIndex] = nodes[resultIndex] with
        {
            Aggregate = new List<string> { "node:section-instructions" }
        };
        nodes.Add(new WorkflowNodeSpec("assemble-letter", "Assemble letter",
            Aggregate: new List<string> { "node:contextualize" }));

        return baseline with
        {
            Nodes = nodes,
            Result = null,
            Results = new List<WorkflowResultSpec>
            {
                new("consult_note", "node:assemble-note", "Consultation note"),
                new("patient_letter", "node:assemble-letter", "Patient letter")
            }
        };
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);
}

public class WorkflowV7ValidationTests
{
    [Fact]
    public void Minimal_IsValid()
    {
        var result = V7Fixtures.Validate(V7Fixtures.Minimal());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void MultiDeliverable_IsValid()
    {
        var result = V7Fixtures.Validate(V7Fixtures.MultiDeliverable());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void MissingInputs_IsRejected()
    {
        var manifest = V7Fixtures.Minimal() with { Inputs = null };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("inputs is required in specVersion 7"));
    }

    [Fact]
    public void EmptyInputs_IsRejected()
    {
        var manifest = V7Fixtures.Minimal() with { Inputs = new List<WorkflowInputSpec>() };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("inputs must declare at least one input slot"));
    }

    [Theory]
    [InlineData("Consult_Draft")]
    [InlineData("consult-draft")]
    [InlineData("1draft")]
    [InlineData("")]
    public void MalformedInputId_IsRejected(string id)
    {
        var manifest = V7Fixtures.Minimal() with
        {
            Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft"), new(id, "Label") }
        };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains($"Input id '{id}' must be snake_case"));
    }

    [Fact]
    public void DuplicateInputId_IsRejected()
    {
        var manifest = V7Fixtures.Minimal() with
        {
            Inputs = new List<WorkflowInputSpec>
            {
                new("consult_draft", "Consult draft"),
                new("consult_draft", "Again")
            }
        };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("Duplicate input id 'consult_draft'"));
    }

    [Fact]
    public void BlankInputLabel_IsRejected()
    {
        var manifest = V7Fixtures.Minimal() with
        {
            Inputs = new List<WorkflowInputSpec> { new("consult_draft", " ") }
        };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("Input 'consult_draft' has no label"));
    }

    [Fact]
    public void UndeclaredInputBinding_IsRejected_AndListsTheDeclaration()
    {
        // The package binds input:consult_draft but declares only referral_letter:
        // the vocabulary is the declaration, conventions grant no exemption.
        var manifest = V7Fixtures.Minimal() with
        {
            Inputs = new List<WorkflowInputSpec> { new("referral_letter", "Referral letter") }
        };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("undeclared input 'consult_draft' (declared: referral_letter)"));
    }

    [Fact]
    public void InputsOnV6_IsRejected()
    {
        var manifest = V6Fixtures.SingleCollection() with
        {
            Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft") }
        };

        Assert.Contains(V6Fixtures.Validate(manifest).Errors,
            e => e.Contains("inputs requires specVersion 7"));
    }

    [Fact]
    public void ResultsOnV6_IsRejected()
    {
        var manifest = V6Fixtures.SingleCollection() with
        {
            Results = new List<WorkflowResultSpec> { new("consult", "node:assemble-note", "Note") }
        };

        Assert.Contains(V6Fixtures.Validate(manifest).Errors,
            e => e.Contains("results requires specVersion 7"));
    }

    [Fact]
    public void BothResultAndResults_IsRejected()
    {
        var manifest = V7Fixtures.MultiDeliverable() with { Result = "node:assemble-note" };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("Declare result or results, not both"));
    }

    [Fact]
    public void NeitherResultNorResults_IsRejected()
    {
        var manifest = V7Fixtures.MultiDeliverable() with { Results = null };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("A result or results declaration is required in specVersion 7"));
    }

    [Fact]
    public void EmptyResults_IsRejected()
    {
        var manifest = V7Fixtures.MultiDeliverable() with { Results = new List<WorkflowResultSpec>() };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("results must declare at least one deliverable"));
    }

    [Fact]
    public void MalformedResultId_IsRejected()
    {
        var manifest = V7Fixtures.MultiDeliverable();
        manifest.Results![0] = manifest.Results[0] with { Id = "consult-note" };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("Result id 'consult-note' must be snake_case"));
    }

    [Fact]
    public void NonAggregatorResultNode_IsRejected()
    {
        var manifest = V7Fixtures.MultiDeliverable();
        manifest.Results![1] = manifest.Results[1] with { Node = "node:contextualize" };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("Result 'patient_letter' must reference an aggregator node ('contextualize' is not one)"));
    }

    [Fact]
    public void SharedResultNode_IsRejected()
    {
        var manifest = V7Fixtures.MultiDeliverable();
        manifest.Results![1] = manifest.Results[1] with { Node = "node:assemble-note" };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("Results 'consult_note' and 'patient_letter' share node 'assemble-note'"));
    }

    [Fact]
    public void UnknownResultNode_IsRejected()
    {
        var manifest = V7Fixtures.MultiDeliverable();
        manifest.Results![1] = manifest.Results[1] with { Node = "node:missing" };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("Result 'patient_letter' references unknown node 'missing'"));
    }

    [Fact]
    public void NodeFeedingNoResult_IsRejected_MultiRootWording()
    {
        // Drop the second deliverable: the guidelines chain feeds nothing.
        var manifest = V7Fixtures.MultiDeliverable();
        manifest.Results!.RemoveAt(1);
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!);
        nodes.RemoveAll(n => n.Id == "assemble-letter");
        manifest = manifest with { Nodes = nodes };

        var errors = V7Fixtures.Validate(manifest).Errors;

        Assert.Contains(errors, e => e.Contains("Node 'contextualize' does not feed the result"));
        Assert.Contains(errors, e => e.Contains("Node 'summarize-guideline' does not feed the result"));
    }

    [Fact]
    public void OrphanNodeWithTwoResults_UsesMultiRootWording()
    {
        var manifest = V7Fixtures.MultiDeliverable();
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!)
        {
            new("stray", "Stray", Prompt: "contextualize",
                Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
                {
                    ["guideline_summaries"] = new("node:agg-guidelines")
                })
        };
        manifest = manifest with { Nodes = nodes };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("Node 'stray' does not feed any result: every node must transitively reach a result node in specVersion 7"));
    }

    [Fact]
    public void ResultWithoutForEachSource_IsRejectedPerResult()
    {
        // A deliverable over the scalar analysis chain only: no fan, no consult.
        var manifest = V7Fixtures.MultiDeliverable();
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!)
        {
            new("assemble-summary", "Assemble summary",
                Aggregate: new List<string> { "node:extract-patient-concepts" })
        };
        manifest.Results!.Add(new WorkflowResultSpec("summary", "node:assemble-summary", "Summary"));
        manifest = manifest with { Nodes = nodes };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("Result 'summary' must transitively include at least one forEach source"));
    }

    // #227: these assert the whole string rather than a substring. The change
    // is a sentence an author reads once and acts on, so the wording is the
    // behaviour — a Contains check would pass on advice that trailed off.

    [Fact]
    public void ResultWithoutForEachSource_NamesTheFixAndThePackagesForEachNodes()
    {
        // The author's problem here is routing, not authoring: the package
        // fans in four places and this deliverable reaches none of them. So
        // the message says which nodes are available to aggregate over.
        var manifest = V7Fixtures.MultiDeliverable();
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!)
        {
            new("assemble-summary", "Assemble summary",
                Aggregate: new List<string> { "node:extract-patient-concepts" })
        };
        manifest.Results!.Add(new WorkflowResultSpec("summary", "node:assemble-summary", "Summary"));
        manifest = manifest with { Nodes = nodes };

        Assert.Contains(
            "Result 'summary' must transitively include at least one forEach source: "
            + "a deliverable with no fan has no consult. "
            + "Add an aggregator whose sources include a forEach node and bind it into this result "
            + "(forEach nodes in this package: patient-section-draft, section-instructions, "
            + "standard-section-draft, summarize-guideline).",
            V7Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void ResultWithoutForEachSource_SaysSoWhenThePackageFansNowhere()
    {
        // Listing an empty set, or telling this author to aggregate over a
        // forEach node, would be advice they cannot act on — nothing in the
        // package fans at all, which is a different problem.
        var manifest = V7Fixtures.MultiDeliverable();
        manifest = manifest with
        {
            Nodes = manifest.Nodes!.Select(node => node with { ForEach = null }).ToList()
        };

        Assert.Contains(
            V7Fixtures.Validate(manifest).Errors,
            e => e.EndsWith(
                "a deliverable with no fan has no consult. "
                + "This package declares no forEach node — add one over a data collection, "
                + "then aggregate it into this result.",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OrphanNodeError_NamesTheFix()
    {
        var manifest = V7Fixtures.MultiDeliverable();
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!)
        {
            new("stray", "Stray", Prompt: "contextualize",
                Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
                {
                    ["guideline_summaries"] = new("node:agg-guidelines")
                })
        };
        manifest = manifest with { Nodes = nodes };

        Assert.Contains(
            "Node 'stray' does not feed any result: every node must transitively reach "
            + "a result node in specVersion 7. "
            + "Bind it into a node that does, or add it to a result's aggregator.",
            V7Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void SingleRootOrphanNodeError_NamesTheFix()
    {
        // The one-result wording, which names the node everything must reach.
        var manifest = V7Fixtures.MultiDeliverable();
        manifest.Results!.RemoveAt(1);
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!);
        nodes.RemoveAll(n => n.Id == "assemble-letter");
        manifest = manifest with { Nodes = nodes };

        Assert.Contains(
            "Node 'contextualize' does not feed the result: every node must transitively reach "
            + "'assemble-note' in specVersion 7. "
            + "Bind it into a node that does, or add it to the result's aggregator.",
            V7Fixtures.Validate(manifest).Errors);
    }

    [Fact]
    public void AggregationExplicitError_NamesSpecVersion7()
    {
        // Binding a forEach node from a scalar node stays closed in v7, with the
        // interpolated version in the message.
        var manifest = V7Fixtures.Minimal();
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!)
        {
            new("peek", "Peek", Prompt: "contextualize",
                Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
                {
                    ["guideline_summaries"] = new("node:section-instructions")
                })
        };
        var prompts = new List<WorkflowPromptSpec>(manifest.Prompts!)
        {
            new("contextualize", "prompts/contextualize.md", new List<string> { "guideline_summaries" })
        };
        manifest = manifest with { Nodes = nodes, Prompts = prompts };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("aggregation is explicit in specVersion 7"));
    }
}

public class WorkflowPackageDescribeTests
{
    private static WorkflowPackage Package(
        WorkflowPackageManifest manifest,
        IReadOnlyList<WorkflowResolvedResult>? results)
    {
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            Data: data,
            ResultNodeId: results is { Count: 1 } ? results[0].NodeId : results is null ? "assemble-note" : null,
            Results: results);
    }

    [Fact]
    public void V6Package_DeclaresNoInputsOrResults()
    {
        // The frozen path: the client renders its single consult_draft field.
        var response = WorkflowPackages.Describe(Package(V6Fixtures.SingleCollection(), null));

        Assert.Null(response.Inputs);
        Assert.Null(response.Results);
        Assert.NotEmpty(response.Blocks!);
    }

    [Fact]
    public void ATitledPackage_CarriesItsTitle_AndAnUntitledOneNull()
    {
        // #432: the pinned-package response carries the title; the client
        // shows the ref when it is null.
        var titled = WorkflowPackages.Describe(Package(
            V7Fixtures.Minimal() with { SpecVersion = 9, Title = "Breast oncology consults", Tags = new List<string>() },
            new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));
        var untitled = WorkflowPackages.Describe(Package(
            V7Fixtures.Minimal(),
            new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        Assert.Equal("Breast oncology consults", titled.Title);
        Assert.Null(untitled.Title);
    }

    [Fact]
    public void V7MinimalPackage_DeclaresItsInputAndTheSugarResult()
    {
        var response = WorkflowPackages.Describe(Package(
            V7Fixtures.Minimal(),
            new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        Assert.Equal(
            new[] { new WorkflowPackageInputResponse("consult_draft", "Consult draft", true) },
            response.Inputs!.ToArray());
        Assert.Equal(
            new[] { new WorkflowPackageResultResponse("consult", "Assemble note") },
            response.Results!.ToArray());
    }

    [Fact]
    public void V7MultiDeliverablePackage_DeclaresEveryInputAndDeliverable()
    {
        var response = WorkflowPackages.Describe(Package(
            V7Fixtures.MultiDeliverable(),
            new List<WorkflowResolvedResult>
            {
                new("consult_note", "assemble-note", "Consultation note"),
                new("patient_letter", "assemble-letter", "Patient letter")
            }));

        Assert.Equal(
            new[]
            {
                new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
                new WorkflowPackageInputResponse("prior_notes", "Prior notes", false)
            },
            response.Inputs!.ToArray());
        Assert.Equal(
            new[]
            {
                new WorkflowPackageResultResponse("consult_note", "Consultation note"),
                new WorkflowPackageResultResponse("patient_letter", "Patient letter")
            },
            response.Results!.ToArray());
        // Blocks carry the deliverable dimension the client groups by.
        Assert.All(response.Blocks!, block =>
            Assert.True(block.Id.StartsWith("consult_note:", StringComparison.Ordinal)
                || block.Id.StartsWith("patient_letter:", StringComparison.Ordinal)));
    }
}

public class WorkflowV7BlocksTests
{
    private static WorkflowPackage Package(WorkflowPackageManifest manifest, IReadOnlyList<WorkflowResolvedResult> results)
    {
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            Data: data,
            ResultNodeId: results.Count == 1 ? results[0].NodeId : null,
            Results: results);
    }

    [Fact]
    public void MultiDeliverableBlocks_CarryTheResultPrefix()
    {
        var manifest = V7Fixtures.MultiDeliverable();
        var package = Package(manifest, new List<WorkflowResolvedResult>
        {
            new("consult_note", "assemble-note", "Consultation note"),
            new("patient_letter", "assemble-letter", "Patient letter")
        });

        var blocks = WorkflowPackageBlocks.Resolve(package);

        Assert.Equal(
            new[] { "consult_note:section-instructions:hpi", "consult_note:section-instructions:pmh", "patient_letter:contextualize" },
            blocks.Select(b => b.Id).ToArray());
    }

    [Fact]
    public void SugarBlocks_UseTheConsultPrefix()
    {
        var manifest = V7Fixtures.Minimal();
        var package = Package(manifest, new List<WorkflowResolvedResult>
        {
            new("consult", "assemble-note", "Assemble note")
        });

        var blocks = WorkflowPackageBlocks.Resolve(package);

        Assert.Equal(
            new[] { "consult:section-instructions:hpi", "consult:section-instructions:pmh" },
            blocks.Select(b => b.Id).ToArray());
    }

    [Fact]
    public void V6Blocks_KeepTodaysIds()
    {
        var manifest = V6Fixtures.SingleCollection();
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        var package = new WorkflowPackage(manifest, Nodes: manifest.Nodes, Data: data, ResultNodeId: "assemble-note");

        Assert.Equal(
            new[] { "section-instructions:hpi", "section-instructions:pmh" },
            WorkflowPackageBlocks.Resolve(package).Select(b => b.Id).ToArray());
    }
}
