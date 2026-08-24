using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// specVersion-6 fixtures: the v5 canonical package extended with an aggregator
/// result, and a multi-collection variant with a guidelines chain bridged
/// through an aggregator (package-format-v6-design.md).
/// </summary>
public static class V6Fixtures
{
    public const string GuidelinesDir = "data/guidelines/";

    /// <summary>The v5 pipeline with an assemble-note aggregator as the result.</summary>
    public static WorkflowPackageManifest SingleCollection()
    {
        var v5 = V5Fixtures.Manifest();
        var nodes = new List<WorkflowNodeSpec>(v5.Nodes!)
        {
            new("assemble-note", "Assemble note",
                Aggregate: new List<string> { "node:section-instructions" })
        };

        return v5 with
        {
            SpecVersion = 6,
            Nodes = nodes,
            Result = "node:assemble-note"
        };
    }

    /// <summary>
    /// Adds a second, independent chain: guidelines items summarized per item,
    /// aggregated, contextualized by a scalar prompt node, and joined into the
    /// result aggregator beside the standards chain.
    /// </summary>
    public static WorkflowPackageManifest MultiCollection()
    {
        var baseline = SingleCollection();
        var prompts = new List<WorkflowPromptSpec>(baseline.Prompts!)
        {
            new("summarize-guideline", "prompts/summarize-guideline.md",
                new List<string> { "guideline_text" }),
            new("contextualize", "prompts/contextualize.md",
                new List<string> { "guideline_summaries" })
        };

        var nodes = new List<WorkflowNodeSpec>(baseline.Nodes!);
        nodes.InsertRange(nodes.Count - 1, new[]
        {
            new WorkflowNodeSpec("summarize-guideline", "Summarizing guideline",
                Prompt: "summarize-guideline",
                Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
                {
                    ["guideline_text"] = new("item:content")
                },
                ForEach: "data:guidelines"),
            new WorkflowNodeSpec("agg-guidelines", "Collect guideline summaries",
                Aggregate: new List<string> { "node:summarize-guideline" }),
            new WorkflowNodeSpec("contextualize", "Contextualizing guidelines",
                Prompt: "contextualize",
                Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
                {
                    ["guideline_summaries"] = new("node:agg-guidelines")
                })
        });

        // The result aggregator joins both chains' products.
        var resultIndex = nodes.FindIndex(n => n.Id == "assemble-note");
        nodes[resultIndex] = nodes[resultIndex] with
        {
            Aggregate = new List<string> { "node:section-instructions", "node:contextualize" }
        };

        var data = new Dictionary<string, string>(baseline.Data!)
        {
            ["guidelines"] = GuidelinesDir
        };

        return baseline with { Prompts = prompts, Nodes = nodes, Data = data };
    }

    public static Dictionary<string, string> Files(WorkflowPackageManifest manifest)
    {
        var files = V5Fixtures.Files(manifest);
        files[GuidelinesDir + "index.json"] = """
            {
              "fields": ["id", "name", "content"],
              "items": [
                { "id": "htn", "name": "Hypertension guideline", "file": "htn.md" },
                { "id": "dm2", "name": "Diabetes guideline", "file": "dm2.md" }
              ]
            }
            """;
        files[GuidelinesDir + "htn.md"] = "Target blood pressure below 140/90.";
        files[GuidelinesDir + "dm2.md"] = "Screen annually for nephropathy.";
        return files;
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, Files(manifest), TestOutputContracts.CatalogSchemas);
}

public class WorkflowV6ValidationTests
{
    [Fact]
    public void SingleCollectionWithResultAggregator_IsValid()
    {
        var result = V6Fixtures.Validate(V6Fixtures.SingleCollection());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void MultiCollection_IsValid()
    {
        var result = V6Fixtures.Validate(V6Fixtures.MultiCollection());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void SharedPrompt_IsValid_InV6()
    {
        // v6 relaxes v5's one-node-per-prompt rule (#170): a second forEach
        // node reuses summarize-guideline with its own full bindings, joined
        // into the result so reachability holds.
        var manifest = V6Fixtures.MultiCollection();
        var nodes = manifest.Nodes!.ToList();
        nodes.Insert(nodes.Count - 1, new WorkflowNodeSpec("summarize-standard", "Summarizing standard",
            Prompt: "summarize-guideline",
            Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
            {
                ["guideline_text"] = new("item:content")
            },
            ForEach: "data:standards"));
        var resultIndex = nodes.FindIndex(n => n.Id == "assemble-note");
        nodes[resultIndex] = nodes[resultIndex] with
        {
            Aggregate = new List<string>(nodes[resultIndex].Aggregate!) { "node:summarize-standard" }
        };
        manifest = manifest with { Nodes = nodes };

        var result = V6Fixtures.Validate(manifest);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void DisconnectedChain_FailsReachability()
    {
        var manifest = V6Fixtures.MultiCollection();
        var nodes = manifest.Nodes!.Select(n => n.Id == "assemble-note"
            ? n with { Aggregate = new List<string> { "node:section-instructions" } }
            : n).ToList();
        var errors = V6Fixtures.Validate(manifest with { Nodes = nodes }).Errors;

        Assert.Contains(errors, e => e.Contains("Node 'contextualize' does not feed the result"));
        Assert.Contains(errors, e => e.Contains("Node 'agg-guidelines' does not feed the result"));
        Assert.Contains(errors, e => e.Contains("Node 'summarize-guideline' does not feed the result"));
    }

    [Fact]
    public void ResultOnForEachNode_Errors()
    {
        var manifest = V6Fixtures.SingleCollection() with { Result = "node:section-instructions" };

        Assert.Contains(
            V6Fixtures.Validate(manifest).Errors,
            e => e.Contains("result must reference an aggregator node in specVersion 6"));
    }

    [Fact]
    public void AggregatorWithPromptFields_Errors()
    {
        var manifest = V6Fixtures.SingleCollection();
        var nodes = manifest.Nodes!.Select(n => n.Id == "assemble-note"
            ? n with { Prompt = "section-instructions" }
            : n).ToList();

        Assert.Contains(
            V6Fixtures.Validate(manifest with { Nodes = nodes }).Errors,
            e => e.Contains("must declare only aggregate"));
    }

    [Fact]
    public void EmptyAggregateList_Errors()
    {
        var manifest = V6Fixtures.SingleCollection();
        var nodes = manifest.Nodes!.Select(n => n.Id == "assemble-note"
            ? n with { Aggregate = new List<string>() }
            : n).ToList();

        Assert.Contains(
            V6Fixtures.Validate(manifest with { Nodes = nodes }).Errors,
            e => e.Contains("declares an empty aggregate list"));
    }

    [Fact]
    public void UnknownAggregateSource_Errors()
    {
        var manifest = V6Fixtures.SingleCollection();
        var nodes = manifest.Nodes!.Select(n => n.Id == "assemble-note"
            ? n with { Aggregate = new List<string> { "node:missing" } }
            : n).ToList();

        Assert.Contains(
            V6Fixtures.Validate(manifest with { Nodes = nodes }).Errors,
            e => e.Contains("references unknown node 'missing'"));
    }

    [Fact]
    public void ScalarPromptBindingForEach_IsStillClosed()
    {
        // Aggregation is never implicit in a binding: a scalar prompt node may not
        // bind a forEach node in v6 either — that is what aggregators are for.
        var manifest = V6Fixtures.SingleCollection();
        var nodes = manifest.Nodes!.Select(n => n.Id == "identify-problem"
            ? n with
            {
                Bindings = new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
                {
                    ["patient_concepts"] = new("node:section-instructions")
                }
            }
            : n).ToList();

        Assert.Contains(
            V6Fixtures.Validate(manifest with { Nodes = nodes }).Errors,
            e => e.Contains("aggregation is explicit in specVersion 6"));
    }

    [Fact]
    public void AggregateCycle_Errors()
    {
        // assemble-note aggregates cycle-prompt, which binds assemble-note.
        var manifest = V6Fixtures.SingleCollection();
        var prompts = new List<WorkflowPromptSpec>(manifest.Prompts!)
        {
            new("cycle-prompt", "prompts/cycle-prompt.md", new List<string> { "doc" })
        };
        var nodes = manifest.Nodes!.Select(n => n.Id == "assemble-note"
            ? n with { Aggregate = new List<string> { "node:section-instructions", "node:cycle-prompt" } }
            : n).ToList();
        nodes.Add(new WorkflowNodeSpec("cycle-prompt", "Cycling",
            Prompt: "cycle-prompt",
            Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
            {
                ["doc"] = new("node:assemble-note")
            }));

        Assert.Contains(
            V6Fixtures.Validate(manifest with { Prompts = prompts, Nodes = nodes }).Errors,
            e => e.Contains("cycle"));
    }

    [Fact]
    public void V5_AggregateProperty_Errors()
    {
        var manifest = V5Fixtures.Manifest();
        var nodes = new List<WorkflowNodeSpec>(manifest.Nodes!)
        {
            new("assemble-note", "Assemble note",
                Aggregate: new List<string> { "node:section-instructions" })
        };

        Assert.Contains(
            V5Fixtures.Validate(manifest with { Nodes = nodes }, V5Fixtures.Files(manifest)).Errors,
            e => e.Contains("declares aggregate, which requires specVersion 6"));
    }

    [Fact]
    public void Diagram_RendersAggregatorBoxAndEdges()
    {
        var diagram = WorkflowDagDiagram.Generate(V6Fixtures.SingleCollection());

        Assert.Contains("assemble_note[\"assemble-note<br/>Assemble note<br/>aggregate\"]", diagram);
        Assert.Contains("section_instructions -->|\"aggregate\"| assemble_note", diagram);
    }
}
