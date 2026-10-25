using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1165 — the manifest model at 26 (the 26 counterpart of A3 #998): the
/// records carry the 26 members ahead of the bump, so they read, round-trip
/// and write their camelCase keys; a ≤25 manifest writes the bytes it always
/// wrote; and below 26 every member is refused by name. Validity at 26 is not
/// asserted anywhere — #1150's path is inert until #1100 and #1101.
/// </summary>
public static class V26Fixtures
{
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Base()
    {
        var (manifest, files) = V25Fixtures.Base();
        return (manifest with { SpecVersion = 26 }, files);
    }

    public static WorkflowNodeSpec FirstNode(WorkflowPackageManifest manifest) => manifest.Nodes!.First();

    public static WorkflowPackageManifest WithFirstNode(WorkflowPackageManifest manifest, Func<WorkflowNodeSpec, WorkflowNodeSpec> change)
    {
        var node = FirstNode(manifest);
        return manifest with { Nodes = manifest.Nodes!.Select(n => n.Id == node.Id ? change(n) : n).ToList() };
    }
}

public class WorkflowV26ModelTests
{
    [Fact]
    public void AForNode_RoundTrips()
    {
        var (manifest, _) = V26Fixtures.Base();
        var at26 = V26Fixtures.WithFirstNode(manifest, n => n with
        {
            For = "item in snippet.standards",
            Nodes = new List<string> { "draft", "instructions" },
            Ret = "node.instructions",
        });

        var read = V25Fixtures.RoundTrip(at26, out var json);
        var readNode = V26Fixtures.FirstNode(read);
        var wire = V25Fixtures.Keys(json)["nodes"]![0]!;

        Assert.Equal("item in snippet.standards", readNode.For);
        Assert.Equal(new[] { "draft", "instructions" }, readNode.Nodes);
        Assert.Equal("node.instructions", readNode.Ret);
        Assert.Null(readNode.Then);
        Assert.Null(readNode.Branches);
        Assert.Equal("item in snippet.standards", wire["for"]!.GetValue<string>());
        Assert.Equal(2, wire["nodes"]!.AsArray().Count);
        Assert.Equal("node.instructions", wire["ret"]!.GetValue<string>());
        Assert.Null(wire["then"]);
        Assert.Null(wire["else"]);
        Assert.Null(wire["branches"]);
    }

    [Fact]
    public void AnIfNode_RoundTrips_WithThenAndElse()
    {
        var (manifest, _) = V26Fixtures.Base();
        var at26 = V26Fixtures.WithFirstNode(manifest, n => n with
        {
            If = "input.kind == \"new\"",
            Then = new WorkflowFlowBodySpec(new List<string> { "full_workup" }, "node.full_workup"),
            Else = new WorkflowFlowBodySpec(new List<string> { "brief_update" }, "node.brief_update"),
        });

        var read = V25Fixtures.RoundTrip(at26, out var json);
        var readNode = V26Fixtures.FirstNode(read);
        var wire = V25Fixtures.Keys(json)["nodes"]![0]!;

        Assert.Equal("input.kind == \"new\"", readNode.If);
        Assert.Equal("node.full_workup", readNode.Then!.Ret);
        Assert.Equal(new[] { "brief_update" }, readNode.Else!.Nodes);
        Assert.Equal("node.full_workup", wire["then"]!["ret"]!.GetValue<string>());
        Assert.Equal("brief_update", wire["else"]!["nodes"]![0]!.GetValue<string>());
    }

    [Fact]
    public void ACaseNode_RoundTrips_WithBranches()
    {
        var (manifest, _) = V26Fixtures.Base();
        var at26 = V26Fixtures.WithFirstNode(manifest, n => n with
        {
            Case = "node.scope.value",
            Branches = new List<WorkflowCaseBranchSpec>
            {
                new(new List<string> { "in_scope" }, new List<string> { "plan" }, "node.plan"),
                new(new List<string> { "out_of_scope", "needs_information" }, new List<string> { "letter" }, "node.letter"),
            },
            Else = new WorkflowFlowBodySpec(new List<string> { "hold" }, "node.hold"),
        });

        var read = V25Fixtures.RoundTrip(at26, out var json);
        var readNode = V26Fixtures.FirstNode(read);
        var wire = V25Fixtures.Keys(json)["nodes"]![0]!;

        Assert.Equal("node.scope.value", readNode.Case);
        Assert.Equal(2, readNode.Branches!.Count);
        Assert.Equal(new[] { "out_of_scope", "needs_information" }, readNode.Branches[1].When);
        Assert.Equal("node.letter", readNode.Branches[1].Ret);
        Assert.Equal("node.hold", readNode.Else!.Ret);
        // Each branch is labelled by when — Scriban's word (decided 2026-10-25); the
        // node's own when is the ≤25 string, untouched by the branches.
        Assert.Equal("in_scope", wire["branches"]![0]!["when"]![0]!.GetValue<string>());
        Assert.Null(wire["when"]);
        Assert.Null(readNode.When);
    }

    [Fact]
    public void ADeliverable_RoundTrips_SourcesAndIf()
    {
        var (manifest, _) = V26Fixtures.Base();
        var deliverable = manifest.Results!.First() with
        {
            Sources = new List<string> { "node.sections", "node.patient_header" },
            If = "input.kind == \"new\"",
        };
        var at26 = manifest with { Results = null, Deliverables = new List<WorkflowDeliverableSpec> { deliverable } };

        var read = V25Fixtures.RoundTrip(at26, out var json);
        var wire = V25Fixtures.Keys(json)["deliverables"]![0]!;

        Assert.Equal(new[] { "node.sections", "node.patient_header" }, read.Deliverables!.Single().Sources);
        Assert.Equal("input.kind == \"new\"", read.Deliverables.Single().If);
        Assert.Equal(2, wire["sources"]!.AsArray().Count);
        Assert.Equal("input.kind == \"new\"", wire["if"]!.GetValue<string>());
    }

    [Fact]
    public void ANodeInstanceIf_RoundTrips_AndIsNotBare()
    {
        // The IsBare trap: an adornment the writer does not key on drops on republish.
        var instance = new WorkflowNodeMacroSpec("closing", At: WorkflowNodeMacroSpec.After, If: "item.id == \"hpi\"");

        var once = JsonSerializer.Serialize(instance, V25Fixtures.Wire);
        var read = JsonSerializer.Deserialize<WorkflowNodeMacroSpec>(once, WorkflowPackageManifestJson.ReadOptions)!;
        var twice = JsonSerializer.Serialize(read, V25Fixtures.Wire);

        Assert.False(instance.IsBare);
        Assert.False(new WorkflowNodeMacroSpec("closing", If: "input.kind == \"new\"").IsBare);
        Assert.Equal("item.id == \"hpi\"", read.If);
        Assert.Equal(once, twice);
        Assert.Equal(new[] { "id", "at", "if" }, JsonNode.Parse(once)!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void ADeliverableInstanceIf_RoundTrips_AndIsNotBare()
    {
        var instance = new WorkflowDeliverableMacroSpec("closing", At: WorkflowDeliverableMacroSpec.Append, If: "input.kind == \"new\"");

        var once = JsonSerializer.Serialize(instance, V25Fixtures.Wire);
        var read = JsonSerializer.Deserialize<WorkflowDeliverableMacroSpec>(once, WorkflowPackageManifestJson.ReadOptions)!;
        var twice = JsonSerializer.Serialize(read, V25Fixtures.Wire);

        Assert.False(instance.IsBare);
        Assert.False(new WorkflowDeliverableMacroSpec("closing", If: "input.kind == \"new\"").IsBare);
        Assert.Equal("input.kind == \"new\"", read.If);
        Assert.True(read.IsAppend);
        Assert.Equal(once, twice);
        Assert.Contains("\"if\"", twice);
    }

    [Fact]
    public void AFolderItemIf_IsRead_ByTheIndexAndTheResolver()
    {
        const string index = """{ "fields": ["id", "name", "content"], "items": [ { "id": "plan", "name": "Plan", "file": "plan.md", "if": "input.kind == \"new\"" } ] }""";

        var parsed = JsonSerializer.Deserialize<WorkflowDataIndexFile>(index, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("input.kind == \"new\"", parsed.Items!.Single().If);
        Assert.Null(parsed.Items.Single().When);

        var errors = new List<string>();
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["snippets/sections/index.json"] = index,
            ["snippets/sections/plan.md"] = "Plan.",
        };
        var resolved = WorkflowDataResolver.Resolve(new Dictionary<string, string> { ["sections"] = "snippets/sections/" }, "Snippet", files, errors);

        Assert.Empty(errors);
        Assert.Equal("input.kind == \"new\"", resolved.Collections["sections"].Items.Single().If);
    }

    [Fact]
    public void AV25Node_WritesTheBytesItAlwaysWrote()
    {
        var (manifest, _) = V25Package.Minimal();

        var wire = V25Fixtures.Keys(JsonSerializer.Serialize(manifest, V25Fixtures.Wire));
        var node = wire["nodes"]![0]!;
        var deliverable = wire["deliverables"]![0]!;

        foreach (var flow in new[] { "for", "if", "case", "then", "else", "branches", "nodes", "ret" })
        {
            Assert.Null(node[flow]);
        }

        Assert.Null(deliverable["sources"]);
        Assert.Null(deliverable["if"]);
        // The placed instance in the minimal (closing, after) stays an object without if.
        var placed = wire["nodes"]!.AsArray().First(n => n!["macros"] != null)!["macros"]![0]!;
        Assert.Null(placed["if"]);
    }
}

/// <summary>
/// Below 26 every new member is refused by name — the v25 posture carried —
/// so a 25 (or 24) manifest spelling one does not parse and pass where the
/// strict reader used to refuse it as unmapped.
/// </summary>
public class WorkflowV26BelowGateTests
{
    private static List<string> ErrorsAt25(Func<WorkflowPackageManifest, WorkflowPackageManifest> change, Action<Dictionary<string, string>>? withFiles = null)
    {
        var (manifest, files) = V25Package.Minimal();
        withFiles?.Invoke(files);
        return V25Package.Errors((change(manifest), files));
    }

    private static List<string> ErrorsAt24(Func<WorkflowPackageManifest, WorkflowPackageManifest> change, Action<Dictionary<string, string>>? withFiles = null)
    {
        var (manifest, files) = V24Fixtures.Base();
        withFiles?.Invoke(files);
        return V25Fixtures.Errors((change(manifest), files));
    }

    [Fact]
    public void TheV25Minimal_IsValid_AsBefore()
    {
        Assert.Empty(ErrorsAt25(m => m));
    }

    [Theory]
    [InlineData("for")]
    [InlineData("if")]
    [InlineData("case")]
    [InlineData("then")]
    [InlineData("else")]
    [InlineData("branches")]
    [InlineData("nodes")]
    [InlineData("ret")]
    public void ANodeMember_Below26_IsRefusedByName(string member)
    {
        var errors = ErrorsAt25(m => V26Fixtures.WithFirstNode(m, node => member switch
        {
            "for" => node with { For = "item in snippet.sections" },
            "if" => node with { If = "input.kind == \"new\"" },
            "case" => node with { Case = "input.kind" },
            "then" => node with { Then = new WorkflowFlowBodySpec(new List<string> { "x" }, "node.x") },
            "else" => node with { Else = new WorkflowFlowBodySpec(new List<string> { "x" }, "node.x") },
            "branches" => node with { Branches = new List<WorkflowCaseBranchSpec> { new(new List<string> { "new" }, new List<string> { "x" }, "node.x") } },
            "nodes" => node with { Nodes = new List<string> { "x" } },
            _ => node with { Ret = "node.x" },
        }));

        Assert.Contains($"Node 'extract' declares {member}, which requires specVersion 26.", errors);
    }

    [Theory]
    [InlineData("sources")]
    [InlineData("if")]
    public void ADeliverableMember_Below26_IsRefusedByName(string member)
    {
        var errors = ErrorsAt25(m => m with
        {
            Deliverables = m.Deliverables!.Select(d => member == "sources"
                ? d with { Sources = new List<string> { "node.all" } }
                : d with { If = "input.kind == \"new\"" }).ToList(),
        });

        Assert.Contains($"Deliverable 'consult' declares {member}, which requires specVersion 26.", errors);
    }

    [Fact]
    public void ANodeInstanceIf_Below26_IsRefusedByName()
    {
        var errors = ErrorsAt25(m => V25Package.WithNode(m, "assessment", n => n with
        {
            Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, If: "input.kind == \"new\"") },
        }));

        Assert.Contains("Node 'assessment' macro 'closing' declares if, which requires specVersion 26.", errors);
    }

    [Fact]
    public void ADeliverableInstanceIf_Below26_IsRefusedByName()
    {
        var errors = ErrorsAt25(m => m with
        {
            Deliverables = m.Deliverables!.Select(d => d with
            {
                Macros = new List<WorkflowDeliverableMacroSpec> { new("closing", At: WorkflowDeliverableMacroSpec.Append, If: "input.kind == \"new\"") },
            }).ToList(),
        });

        Assert.Contains("Deliverable 'consult' macro 'closing' declares if, which requires specVersion 26.", errors);
    }

    [Fact]
    public void AFolderItemIf_Below26_IsRefusedByName()
    {
        var errors = ErrorsAt25(m => m, files =>
            files["snippets/sections/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md", "if": "input.kind == \"new\"" }, { "id": "plan", "name": "Plan", "file": "plan.md" } ] }""");

        Assert.Contains("Snippet folder 'sections' item 'hpi' declares if, which requires specVersion 26.", errors);
        Assert.DoesNotContain(errors, e => e.StartsWith("Snippet folder 'sections' item 'plan' declares if", StringComparison.Ordinal));
    }

    [Fact]
    public void ADeliverableMember_Below25_SaysResult()
    {
        var errors = ErrorsAt24(m => m with
        {
            Results = m.Results!.Select(d => d with { Sources = new List<string> { "node.x" } }).ToList(),
        });

        var id = V24Fixtures.Base().Manifest.Results!.First().Id;
        Assert.Contains($"Result '{id}' declares sources, which requires specVersion 26.", errors);
    }

    [Fact]
    public void AFolderItemIf_Below25_SaysDataCollection()
    {
        var errors = ErrorsAt24(
            m => m with { Data = new Dictionary<string, string>(m.Data ?? new Dictionary<string, string>()) { ["notes"] = "data/notes/" } },
            files =>
            {
                files["data/notes/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "a", "name": "A", "file": "a.md", "if": "input.kind == \"new\"" } ] }""";
                files["data/notes/a.md"] = "A.";
            });

        Assert.Contains("Data collection 'notes' item 'a' declares if, which requires specVersion 26.", errors);
    }

    [Fact]
    public void At26_TheGateIsSilent()
    {
        var (manifest, files) = V26Package.Minimal();
        var at26 = V26Fixtures.WithFirstNode(manifest, n => n with { Ret = "node.extract" });

        var errors = V26Package.Errors((at26, files));

        Assert.DoesNotContain(errors, e => e.Contains("requires specVersion 26", StringComparison.Ordinal));
    }
}
