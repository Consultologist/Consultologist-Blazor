using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1235's done-when: a 26 package's record and hashes are unchanged at 27 —
/// no migration exists (§ 7), so the twin is the 26 package with its version
/// raised: the same fire set, skeleton, served rows, composition bytes and
/// ladder; only the record's storage version moves (8 → 9).
/// </summary>
public class V27IdentityTests
{
    private static WorkflowPackage At(int specVersion, Func<WorkflowPackageManifest, WorkflowPackageManifest>? change = null)
    {
        var (manifest, files) = V26Package.Minimal();
        manifest = (change?.Invoke(manifest) ?? manifest) with { SpecVersion = specVersion };
        var accepting = specVersion == 27 ? V27Package.Accepting27 : V26Package.Accepting26;
        Assert.Empty(WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog(), accepting).Errors);

        var errors = new List<string>();
        var texts = WorkflowTextHeaders.Resolve(manifest, files, errors);
        var data = WorkflowDataResolver.ResolveContent(texts.Manifest, texts, errors);
        return new WorkflowPackage(manifest, Nodes: manifest.Nodes, Data: data, SourceFiles: files, Results: WorkflowPackageStore.ResolveResultSet(manifest), Formats: V25Package.Catalog(), FormatsRef: V25Package.Catalog().ResolvedRef, Texts: texts);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A26Package_At27_HasTheSameRows_FireSet_Skeleton_AndComposition(bool withTriage, bool withRoute)
    {
        Func<WorkflowPackageManifest, WorkflowPackageManifest> change = m =>
        {
            if (withTriage) m = V26JobStartTests.WithTriage(m);
            if (withRoute) m = V26JobStartTests.WithRoute(m);
            return m;
        };
        var at26 = At(26, change);
        var at27 = At(27, change);
        var supplied = V26JobStartTests.NewPatient;

        var fire26 = ConsultGenerationJobStarter.DecideFireSet(at26, supplied, null, V26JobStartTests.Scope(at26, supplied));
        var fire27 = ConsultGenerationJobStarter.DecideFireSet(at27, supplied, null, V26JobStartTests.Scope(at27, supplied));
        Assert.Equal(fire26.Firing.Select(r => r.Id), fire27.Firing.Select(r => r.Id));
        Assert.Equal(fire26.Branches ?? new Dictionary<string, string>(), fire27.Branches ?? new Dictionary<string, string>());
        Assert.Equal(fire26.Package.Nodes!.Select(n => n.Id), fire27.Package.Nodes!.Select(n => n.Id));

        var skeleton26 = ConsultGenerationJobStarter.ResolveSkeleton(at26, supplied, V26JobStartTests.Scope(at26, supplied));
        var skeleton27 = ConsultGenerationJobStarter.ResolveSkeleton(at27, supplied, V26JobStartTests.Scope(at27, supplied));
        Assert.Equal(skeleton26.Items.Select(i => i["id"]), skeleton27.Items.Select(i => i["id"]));

        var flows26 = ConsultGenerationJobStarter.FlowsOf(fire26.Package);
        var flows27 = ConsultGenerationJobStarter.FlowsOf(fire27.Package);
        var edges26 = ConsultGenerationJobStarter.DeriveEdges(fire26.Package)!;
        var edges27 = ConsultGenerationJobStarter.DeriveEdges(fire27.Package)!;
        foreach (var node in fire26.Package.Nodes!)
        {
            var row26 = ConsultGenerationJobStarter.DescribeNode(node, null, formats: at26.Formats, reads: edges26.GetValueOrDefault(node.Id), manifest: at26.Manifest, flows: flows26, branch: fire26.Branches?.GetValueOrDefault(node.Id)).AsRecordRowAt8();
            var row27 = ConsultGenerationJobStarter.DescribeNode(fire27.Package.Nodes!.First(n => n.Id == node.Id), null, formats: at27.Formats, reads: edges27.GetValueOrDefault(node.Id), manifest: at27.Manifest, flows: flows27, branch: fire27.Branches?.GetValueOrDefault(node.Id)).AsRecordRowAt8();
            // Records compare their lists by reference: the served bytes are the identity.
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(row26), System.Text.Json.JsonSerializer.Serialize(row27));
            Assert.Null(row27.Branches);
        }

        Assert.Equal(ConsultGenerationJobStarter.ChainSteps(at26).Select(s => s.Id), ConsultGenerationJobStarter.ChainSteps(at27).Select(s => s.Id));
    }

    [Fact]
    public void A26Loop_ComposesByteIdentically_At27_AndOnlyTheStorageVersionMoves()
    {
        // The pure halves take no version: the identity is the skipped set being empty.
        var at27 = At(27);
        var flows = ConsultGenerationJobStarter.FlowsOf(at27);
        var nodes = at27.Nodes!.Select(n => ConsultGenerationJobStarter.DescribeNode(n, null, formats: at27.Formats, manifest: at27.Manifest, flows: flows)).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var items = new[]
        {
            (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "hpi", ["label"] = "HPI" },
            new Dictionary<string, string> { ["id"] = "plan", ["label"] = "Plan" },
        };
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["section_notes:hpi"] = new("The history.", null, "i1", "o1", 6),
            ["section_notes:plan"] = new("The plan.", null, "i2", "o2", 6),
        };

        var without = ConsultFlowCompletion.TryCompose(nodes["sections"], nodes, outputs, _ => items, (_, _) => false)!.Result!;
        var at27Composed = ConsultFlowCompletion.TryCompose(nodes["sections"], nodes, outputs, _ => items, (_, _) => false, new HashSet<string>())!.Result!;
        Assert.Equal((without.RawOutput, without.InputHash, without.OutputHash, without.HashVersion), (at27Composed.RawOutput, at27Composed.InputHash, at27Composed.OutputHash, at27Composed.HashVersion));

        Assert.Equal(27, at27.Manifest.SpecVersion);
        Assert.Equal(9, StampOf(27));
        Assert.Equal(8, StampOf(26));

        static int StampOf(int specVersion)
        {
            var state = new ConsultGenerationJobState { PackageSpecVersion = specVersion };
            ConsultGenerationJobEntity.Stamp(state, 6);
            return state.SchemaVersion;
        }
    }
}
