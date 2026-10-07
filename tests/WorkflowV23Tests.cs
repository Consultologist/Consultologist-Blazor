using System.Text.Json;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v23 step A (#955, node-macros-design.md): the validator accepts 23 and knows
/// the node's macro attachment — a library macro composed into THIS node's
/// prompt, before or after, gated by the node-level when grammar, the bare form
/// being what a prelude migrates to. The engine does not run it yet (Layer B
/// adds 23 to SupportedSpecVersions), so Accepted leads, as v8 did.
/// </summary>
public static class V23Fixtures
{
    public const string MacroId = "tool_guidance";

    /// <summary>A v23 manifest with a `scope` classifier (so a node macro's
    /// when can read node:scope) and a results list (so the base is valid).</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Base()
    {
        var (manifest, files) = V10Fixtures.WithClassifier();
        return (V11Fixtures.WithResultsList(manifest with { SpecVersion = 23 }),
            new Dictionary<string, string>(files, StringComparer.Ordinal));
    }

    public static WorkflowNodeSpec FirstPromptNode(WorkflowPackageManifest manifest) =>
        manifest.Nodes!.First(n => n.Prompt != null && n.Aggregate is null && !WorkflowNodeKinds.IsClassifier(n));

    public static WorkflowNodeSpec AggregatorNode(WorkflowPackageManifest manifest) =>
        manifest.Nodes!.First(n => n.Aggregate != null);

    /// <summary>Attach node macros to the first prompt node. The library macro is
    /// declared unless `declare` is false (the undeclared-reference case).</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithNodeMacros(
        List<WorkflowNodeMacroSpec> attach, bool declare = true, WorkflowNodeSpec? onNode = null)
    {
        var (manifest, files) = Base();
        var node = onNode ?? FirstPromptNode(manifest);
        manifest = manifest with
        {
            Macros = declare ? new List<WorkflowMacroSpec> { new(MacroId, "Tool guidance", $"macros/{MacroId}.md") } : null,
            Nodes = manifest.Nodes!.Select(n => n.Id == node.Id ? n with { Macros = attach } : n).ToList()
        };
        if (declare)
        {
            files[$"macros/{MacroId}.md"] = "Use short SNOMED search terms.";
        }
        return (manifest, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate(
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
        => WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);

    public static IEnumerable<string> Errors(
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
        => Validate(bundle).Errors;
}

public class WorkflowV23GateTests
{
    [Fact]
    public void TheValidatorAccepts23_ButTheStoreDoesNotRunItYet()
    {
        // Layer A makes it publishable; Layer B (#959) adds it to the run set.
        Assert.Contains(23, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.DoesNotContain(23, WorkflowPackageStore.SupportedSpecVersions);
    }

    [Fact]
    public void TheBaseManifest_IsValidAt23()
    {
        var result = V23Fixtures.Validate(V23Fixtures.Base());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}

public class WorkflowV23NodeMacroTests
{
    [Fact]
    public void ABareNodeMacro_IsAccepted()
    {
        // The before/always form a prelude migrates to.
        var result = V23Fixtures.Validate(V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { V23Fixtures.MacroId }));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void APlacedNodeMacro_AfterAndGated_IsAccepted()
    {
        var result = V23Fixtures.Validate(V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec>
        {
            new(V23Fixtures.MacroId, At: WorkflowNodeMacroSpec.After, When: "node:scope == in_scope")
        }));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AMacroAttachedOnlyToANode_IsNotAnOrphan()
    {
        // #955: the orphan rule counts node references too — a macro no result
        // names but a node composes is referenced, not dead weight.
        var result = V23Fixtures.Validate(V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { V23Fixtures.MacroId }));
        Assert.DoesNotContain(result.Errors, e => e.Contains("not referenced"));
    }

    [Fact]
    public void TheSameMacro_BeforeAndAfter_IsAccepted()
    {
        var result = V23Fixtures.Validate(V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec>
        {
            new(V23Fixtures.MacroId),
            new(V23Fixtures.MacroId, At: WorkflowNodeMacroSpec.After)
        }));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AnUndeclaredNodeMacro_IsRejected()
    {
        Assert.Contains(
            V23Fixtures.Errors(V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { "ghost" }, declare: false)),
            e => e.Contains("macro 'ghost' is not a declared macro"));
    }

    [Fact]
    public void AMacroListedTwiceInTheSamePlace_IsRejected()
    {
        Assert.Contains(
            V23Fixtures.Errors(V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec>
            {
                V23Fixtures.MacroId,
                V23Fixtures.MacroId
            })),
            e => e.Contains("is listed more than once before the prompt"));
    }

    [Fact]
    public void AnInvalidPlacement_IsRejected()
    {
        Assert.Contains(
            V23Fixtures.Errors(V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec>
            {
                new(V23Fixtures.MacroId, At: "sideways")
            })),
            e => e.Contains("is placed 'before' or 'after' the prompt"));
    }

    [Fact]
    public void AnInvalidWhen_IsRejected()
    {
        // The when speaks the node-level grammar; a value the classifier does
        // not declare is refused by the shared clause validator.
        Assert.Contains(
            V23Fixtures.Errors(V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec>
            {
                new(V23Fixtures.MacroId, When: "node:scope == nonsense")
            })),
            e => e.Contains("condition"));
    }

    [Fact]
    public void MacrosOnANodeWithNoPrompt_AreRejected()
    {
        var (manifest, _) = V23Fixtures.Base();
        var aggregator = V23Fixtures.AggregatorNode(manifest);
        Assert.Contains(
            V23Fixtures.Errors(V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { V23Fixtures.MacroId }, onNode: aggregator)),
            e => e.Contains("has no prompt to compose them into"));
    }

    [Fact]
    public void NodeMacros_AreRefusedByName_BelowTwentyThree()
    {
        var (manifest, files) = V23Fixtures.WithNodeMacros(new List<WorkflowNodeMacroSpec> { "ghost" }, declare: false);
        var atTwentyTwo = manifest with { SpecVersion = 22 };
        Assert.Contains(
            WorkflowPackageValidator.Validate(atTwentyTwo, files, TestOutputContracts.CatalogSchemas).Errors,
            e => e.Contains("declares macros, which requires specVersion 23"));
    }
}

public class WorkflowV23NodeMacroWireTests
{
    [Fact]
    public void TheWireForm_RoundTrips_BareAndPlaced()
    {
        var macros = new List<WorkflowNodeMacroSpec>
        {
            "tool_guidance",
            new("ap_guardrails", At: WorkflowNodeMacroSpec.After, When: "node:scope == in_scope", Optional: true)
        };

        var json = JsonSerializer.Serialize(macros, WorkflowPackageManifestJson.ReadOptions);

        using (var doc = JsonDocument.Parse(json))
        {
            // The bare entry is a string; the placed entry is an object.
            Assert.Equal(JsonValueKind.String, doc.RootElement[0].ValueKind);
            Assert.Equal(JsonValueKind.Object, doc.RootElement[1].ValueKind);
            Assert.Equal("after", doc.RootElement[1].GetProperty("at").GetString());
        }

        var back = JsonSerializer.Deserialize<List<WorkflowNodeMacroSpec>>(json, WorkflowPackageManifestJson.ReadOptions)!;

        Assert.True(back[0].IsBare);
        Assert.Equal("tool_guidance", back[0].Id);
        Assert.False(back[0].IsAfter);

        Assert.Equal("ap_guardrails", back[1].Id);
        Assert.True(back[1].IsAfter);
        Assert.Equal("node:scope == in_scope", back[1].When);
        Assert.True(back[1].Optional);
    }
}
