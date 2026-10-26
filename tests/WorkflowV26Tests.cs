using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// The 26 packages and the seam that lets them through while 26 is wired but
/// not yet accepted (#1150; the bump is #1149). <see cref="Minimal"/> is the
/// design's minimal 26 package (#1100: a loop over a snippet folder, its one
/// body node returned; the deliverable still naming a node and the prompts still
/// declared until #1101 and #1102 — <see cref="FlowErrors"/> sets those two
/// sentences aside). <see cref="FromV25"/> is the 25 minimal with its version
/// raised — invalid at 26 by design, every key 26 retires in it — for the
/// retirement facts.
/// </summary>
public static class V26Package
{
    public static readonly IReadOnlyCollection<int> Accepting26 = WorkflowPackageValidator.AcceptedSpecVersions.Append(26).ToList();

    /// <summary>
    /// The minimal 26 package: extract (concept_list), scope (classification),
    /// assessment (prose; the closing macro after it), coverage (terms_subset),
    /// section_notes (prose) run by the loop `sections` over snippet.sections
    /// and returned by it; one deliverable, one macro, one folder of two items.
    /// </summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Minimal()
    {
        var (manifest, files) = V25Package.Minimal();

        var at26 = manifest with
        {
            SpecVersion = 26,
            Nodes = new List<WorkflowNodeSpec>
            {
                new("extract", "Extract concepts", Prompt: "extract", Format: "concept_list", OnInvalid: WorkflowOnInvalid.Fail),
                new("scope", "Scope", Prompt: "scope", Format: "classification", OnInvalid: WorkflowOnInvalid.Fail,
                    With: new Dictionary<string, JsonNode?> { ["values"] = new JsonArray("in_scope", "out_of_scope") }),
                new("assessment", "Assessment", Prompt: "assessment", Format: "prose",
                    Macros: new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After) }),
                new("section_notes", "Section notes", Prompt: "section", Format: "prose"),
                new("coverage", "Coverage", Format: "terms_subset", OnInvalid: WorkflowOnInvalid.Fail,
                    With: new Dictionary<string, JsonNode?> { ["of"] = "node.extract", ["in"] = "node.extract", ["failWith"] = "The note names problems the source does not." }),
                new("sections", "Sections", For: "item in snippet.sections", Nodes: new List<string> { "section_notes" }, Ret: "node.section_notes"),
            },
            // #1101: sources: ["node.sections"]; until then the retired key, one sentence.
            Deliverables = new List<WorkflowDeliverableSpec> { new("consult", "node.sections", "Consultation note", Check: "node.coverage") },
        };

        var files26 = new Dictionary<string, string>(files, StringComparer.Ordinal)
        {
            // The item's gate is keyed if at 26 (#1094); until it is walked the item carries none.
            ["snippets/sections/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md" }, { "id": "plan", "name": "Plan", "file": "plan.md" } ] }""",
        };

        return (at26, files26);
    }

    /// <summary>The 25 minimal raised to 26 — every key 26 retires, for the retirement facts.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) FromV25()
    {
        var (manifest, files) = V25Package.Minimal();
        return (manifest with { SpecVersion = 26 }, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog(), Accepting26);

    public static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => Validate(bundle).Errors;

    public static List<string> Warnings((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => Validate(bundle).Warnings;

    /// <summary>
    /// The errors but for the two the minimal earns until its issues land: the
    /// deliverable's (#1101 — `node` retired, `sources` not yet read) and the
    /// prompts' declaration (#1102). #1101 replaces this with validity.
    /// </summary>
    public static List<string> FlowErrors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        Errors(bundle).Where(e => !e.StartsWith("Deliverable ", StringComparison.Ordinal)
            && !e.StartsWith("deliverables", StringComparison.Ordinal)
            && !e.StartsWith("prompts is retired", StringComparison.Ordinal)).ToList();

    public static List<string> FlowErrors(Func<WorkflowPackageManifest, WorkflowPackageManifest> change, Action<Dictionary<string, string>>? withFiles = null)
    {
        var (manifest, files) = Minimal();
        withFiles?.Invoke(files);
        return FlowErrors((change(manifest), files));
    }

    public static WorkflowNodeSpec Node(WorkflowPackageManifest manifest, string id) => manifest.Nodes!.First(n => n.Id == id);

    public static WorkflowPackageManifest WithNode(WorkflowPackageManifest manifest, string id, Func<WorkflowNodeSpec, WorkflowNodeSpec> change) =>
        manifest with { Nodes = manifest.Nodes!.Select(n => n.Id == id ? change(n) : n).ToList() };

    public static WorkflowPackageManifest WithNodes(WorkflowPackageManifest manifest, params WorkflowNodeSpec[] added) =>
        manifest with { Nodes = manifest.Nodes!.Concat(added).ToList() };
}

public class WorkflowV26Tests
{
    private static List<string> Errors(Func<WorkflowPackageManifest, WorkflowPackageManifest> change)
    {
        var (manifest, files) = V26Package.FromV25();
        return V26Package.Errors((change(manifest), files));
    }

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
        var errors = V26Package.Errors(V26Package.FromV25());

        Assert.DoesNotContain(errors, e => e.StartsWith("specVersion 26 is not supported", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("retired at specVersion 26", StringComparison.Ordinal));
    }

    [Fact]
    public void Prompts_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(V26Package.FromV25());

        Assert.Contains("prompts is retired at specVersion 26; a node names its prompt file (nodes[].prompt: \"<path>\").", errors);
    }

    [Fact]
    public void ANodeForEach_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(V26Package.FromV25());

        Assert.Contains("Node 'section_notes' declares forEach, which is retired at specVersion 26; a for node whose body holds the node (nodes[].for: \"item in snippet.<folder>\" | \"item in input.<id>\").", errors);
    }

    [Fact]
    public void ANodeWhen_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = Errors(m => V25Package.WithNode(m, "assessment", n => n with { When = "input.kind == \"new\"" }));

        Assert.Contains("Node 'assessment' declares when, which is retired at specVersion 26; an if node whose body holds the node (nodes[].if).", errors);
    }

    [Fact]
    public void ANodeAggregate_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(V26Package.FromV25());

        Assert.Contains("Node 'all' declares aggregate, which is retired at specVersion 26; a deliverable names its sources (deliverables[].sources).", errors);
    }

    [Fact]
    public void ANodeInstanceForItem_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = Errors(m => V25Package.WithNode(m, "section_notes", n => n with
        {
            Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, ForItem: "hpi") },
        }));

        Assert.Contains("Node 'section_notes' macro 'closing' declares forItem, which is retired at specVersion 26; the instance's if on item.id (if: \"item.id == \\\"<item>\\\"\").", errors);
    }

    [Fact]
    public void ANodeInstanceWhen_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = Errors(m => V25Package.WithNode(m, "assessment", n => n with
        {
            Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, When: "input.kind == \"new\"") },
        }));

        Assert.Contains("Node 'assessment' macro 'closing' declares when, which is retired at specVersion 26; a gate is keyed if; when is a case branch's label only.", errors);
    }

    [Fact]
    public void ADeliverableNode_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = V26Package.Errors(V26Package.FromV25());

        Assert.Contains("Deliverable 'consult' declares node, which is retired at specVersion 26; a deliverable names its sources (deliverables[].sources).", errors);
    }

    [Fact]
    public void ADeliverableWhen_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = Errors(m => m with
        {
            Deliverables = m.Deliverables!.Select(d => d with { When = "input.kind == \"new\"" }).ToList(),
        });

        Assert.Contains("Deliverable 'consult' declares when, which is retired at specVersion 26; a gate is keyed if; when is a case branch's label only.", errors);
    }

    [Fact]
    public void ADeliverableInstanceWhen_RetiredAt26_IsRefusedWithItsReplacement()
    {
        var errors = Errors(m => m with
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
        var errors = V26Package.Errors(V26Package.FromV25());

        Assert.Contains("Snippet folder 'sections' item 'plan' declares when, which is retired at specVersion 26; a gate is keyed if; when is a case branch's label only.", errors);
        Assert.DoesNotContain(errors, e => e.StartsWith("Snippet folder 'sections' item 'hpi' declares when", StringComparison.Ordinal));
    }

    [Fact]
    public void The25Retirements_StillHold_At26()
    {
        var errors = Errors(m => m with { Preludes = new Dictionary<string, string> { ["x"] = "prompts/_x.md" } });

        Assert.Contains(errors, e => e.StartsWith("preludes is retired at specVersion 25;", StringComparison.Ordinal));
    }

    [Fact]
    public void AKeyAbsent_MeetsNoRetirementSentence()
    {
        // The raised 25 minimal meets only the retirements its keys earn: the
        // prompts, forEach, aggregate, deliverable node and the one folder item when.
        var errors = V26Package.Errors(V26Package.FromV25()).Where(e => e.Contains("retired at specVersion 26", StringComparison.Ordinal)).ToList();

        Assert.Equal(5, errors.Count);
    }

    [Fact]
    public void TheV25Minimal_IsUntouched_ByThe26Path()
    {
        var result = V25Package.Validate(V25Package.Minimal(), V25Package.Catalog());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}
