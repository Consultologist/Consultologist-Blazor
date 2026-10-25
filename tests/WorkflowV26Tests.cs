using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// A 26-flagged package and the seam that lets it through while 26 is wired
/// but not yet accepted (#1150, the 26 counterpart of A4 #999; the bump is
/// #1149). The fixture is the 25 minimal with its version raised — invalid at
/// 26 by design, since every key 26 retires is in it — so every fact here
/// asserts a sentence's presence, never validity.
/// </summary>
public static class V26Package
{
    public static readonly IReadOnlyCollection<int> Accepting26 = WorkflowPackageValidator.AcceptedSpecVersions.Append(26).ToList();

    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Minimal()
    {
        var (manifest, files) = V25Package.Minimal();
        return (manifest with { SpecVersion = 26 }, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog(), Accepting26);

    public static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => Validate(bundle).Errors;

    public static List<string> Errors(Func<WorkflowPackageManifest, WorkflowPackageManifest> change)
    {
        var (manifest, files) = Minimal();
        return Errors((change(manifest), files));
    }
}

public class WorkflowV26Tests
{
    [Fact]
    public void ThePublicEntry_StillRefuses26_UntilTheBump()
    {
        // The accepted set does not move here (#1149 moves it with the store's
        // and the editor's mirror); the seam is the only door.
        Assert.DoesNotContain(26, WorkflowPackageValidator.AcceptedSpecVersions);

        var (manifest, files) = V26Package.Minimal();
        var result = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());

        Assert.Contains(result.Errors, e => e.StartsWith("specVersion 26 is not supported", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, e => e.Contains("retired at specVersion 26", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSeam_Routes26_ToThe26Path()
    {
        var errors = V26Package.Errors(V26Package.Minimal());

        Assert.DoesNotContain(errors, e => e.StartsWith("specVersion 26 is not supported", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("retired at specVersion 26", StringComparison.Ordinal));
    }

    [Fact]
    public void Prompts_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(V26Package.Minimal());

        Assert.Contains("prompts is retired at specVersion 26; a node names its prompt file (nodes[].prompt: \"<path>\").", errors);
    }

    [Fact]
    public void ANodeForEach_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(V26Package.Minimal());

        Assert.Contains("Node 'section_notes' declares forEach, which is retired at specVersion 26; a for node whose body holds the node (nodes[].for: \"item in snippet.<folder>\" | \"item in input.<id>\").", errors);
    }

    [Fact]
    public void ANodeWhen_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(m => V25Package.WithNode(m, "assessment", n => n with { When = "input.kind == \"new\"" }));

        Assert.Contains("Node 'assessment' declares when, which is retired at specVersion 26; an if node whose body holds the node (nodes[].if).", errors);
    }

    [Fact]
    public void ANodeAggregate_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(V26Package.Minimal());

        Assert.Contains("Node 'all' declares aggregate, which is retired at specVersion 26; a deliverable names its sources (deliverables[].sources).", errors);
    }

    [Fact]
    public void ANodeInstanceForItem_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(m => V25Package.WithNode(m, "section_notes", n => n with
        {
            Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, ForItem: "hpi") },
        }));

        Assert.Contains("Node 'section_notes' macro 'closing' declares forItem, which is retired at specVersion 26; the instance's if on item.id (if: \"item.id == \\\"<item>\\\"\").", errors);
    }

    [Fact]
    public void ANodeInstanceWhen_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(m => V25Package.WithNode(m, "assessment", n => n with
        {
            Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, When: "input.kind == \"new\"") },
        }));

        Assert.Contains("Node 'assessment' macro 'closing' declares when, which is retired at specVersion 26; a gate is keyed if; when is a case branch's label only.", errors);
    }

    [Fact]
    public void ADeliverableNode_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(V26Package.Minimal());

        Assert.Contains("Deliverable 'consult' declares node, which is retired at specVersion 26; a deliverable names its sources (deliverables[].sources).", errors);
    }

    [Fact]
    public void ADeliverableWhen_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(m => m with
        {
            Deliverables = m.Deliverables!.Select(d => d with { When = "input.kind == \"new\"" }).ToList(),
        });

        Assert.Contains("Deliverable 'consult' declares when, which is retired at specVersion 26; a gate is keyed if; when is a case branch's label only.", errors);
    }

    [Fact]
    public void ADeliverableInstanceWhen_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(m => m with
        {
            Deliverables = m.Deliverables!.Select(d => d with
            {
                Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: "append", When: "input.kind == \"new\"") },
            }).ToList(),
        });

        Assert.Contains("Deliverable 'consult' macro 'closing' declares when, which is retired at specVersion 26; a gate is keyed if; when is a case branch's label only.", errors);
    }

    [Fact]
    public void AFolderItemWhen_RetiredAt26_IsRefusedWithItsReplacement()
    {
        // The 25 minimal's 'plan' item carries a when in snippets/sections/index.json.
        var errors = V26Package.Errors(V26Package.Minimal());

        Assert.Contains("Snippet folder 'sections' item 'plan' declares when, which is retired at specVersion 26; a gate is keyed if; when is a case branch's label only.", errors);
        Assert.DoesNotContain(errors, e => e.StartsWith("Snippet folder 'sections' item 'hpi' declares when", StringComparison.Ordinal));
    }

    [Fact]
    public void The25Retirements_StillHold_At26()
    {
        var errors = V26Package.Errors(m => m with { Preludes = new Dictionary<string, string> { ["x"] = "prompts/_x.md" } });

        Assert.Contains(errors, e => e.StartsWith("preludes is retired at specVersion 25;", StringComparison.Ordinal));
    }

    [Fact]
    public void AKeyAbsent_MeetsNoRetirementSentence()
    {
        // A 26 manifest without instance gates, deliverable gates or node whens
        // meets only the retirements its keys earn: the fixture's prompts,
        // forEach, aggregate, deliverable node and the one folder item when.
        var errors = V26Package.Errors(V26Package.Minimal()).Where(e => e.Contains("retired at specVersion 26", StringComparison.Ordinal)).ToList();

        Assert.Equal(5, errors.Count);
    }

    [Fact]
    public void TheV25Minimal_IsUntouched_ByThe26Path()
    {
        var result = V25Package.Validate(V25Package.Minimal(), V25Package.Catalog());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}
