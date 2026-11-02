using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1284 (package-format-v28-design.md § 5, § 6): the store's read at 28 and the run
/// path over it. The 28 minimal and its 27 twin (the minimal it was spelled from)
/// load through <see cref="WorkflowPackageStore.Materialise"/> and
/// <see cref="WorkflowPackageStore.PackageOf"/> — the store's own construction — and
/// render the same bytes, derive the same edges, fire the same nodes and record the
/// same rows; the one difference is section_notes' prompt path, the file the
/// migration renamed to its id. Record storage stays 9.
/// </summary>
public class V28RunPathTests
{
    private static readonly OutputContractCatalog Catalog = LoadCatalog();

    private static OutputContractCatalog LoadCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return OutputContractCatalog.Load(Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents"));
    }

    private static readonly TemplateRunFacts Run = new("2026-11-02", "abcdef12", "general@v2026.11.1", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");
    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal) { ["notes"] = "Referral.", ["kind"] = "new" };

    private static WorkflowPackage Load((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle, IReadOnlyCollection<int> accepting)
    {
        var loaded = WorkflowPackageStore.Materialise($"{bundle.Manifest.Name}@{bundle.Manifest.Version}", bundle.Manifest,
            new Dictionary<string, string>(bundle.Files, StringComparer.Ordinal), null, Catalog, V25Package.Catalog(), accepting);
        return WorkflowPackageStore.PackageOf(loaded);
    }

    private static (WorkflowPackage At27, WorkflowPackage At28) Twins() =>
        (Load(V27Package.Minimal(), V27Package.Accepting27), Load(V28Package.Minimal(), V28Package.Accepting28));

    private static ConsultPromptNodeActivityInput Input(string nodeId, string promptId, IReadOnlyDictionary<string, string>? nodeTexts = null, IReadOnlyDictionary<string, string>? item = null, string? forEach = null) =>
        new(nodeId, promptId, Inputs, "general@v2026.11.1", Format: "prose", Producer: "attested",
            NodeTexts: nodeTexts ?? new Dictionary<string, string>(), Item: item, ForEach: forEach, Run: Run, Profile: Profile);

    private static string Render(WorkflowPackage package, string nodeId, IReadOnlyDictionary<string, string>? nodeTexts = null, IReadOnlyDictionary<string, string>? item = null, string? forEach = null)
    {
        var prompt = package.Nodes!.Single(n => n.Id == nodeId).Prompt!;
        return ConsultRenderPayload.RenderAt25(Input(nodeId, prompt, nodeTexts, item, forEach), package, package.Prompts![prompt].TemplateText);
    }

    [Fact]
    public void ALoaded28Package_CarriesItsImpliedPrompts_AndTheTemplatesByPath()
    {
        var (_, at28) = Twins();

        var prompts = at28.Nodes!.ToDictionary(n => n.Id, n => n.Prompt);
        Assert.Equal("prompts/extract.md", prompts["extract"]);
        Assert.Equal("prompts/section_notes.md", prompts["section_notes"]);
        Assert.Null(prompts["coverage"]);
        Assert.Null(prompts["sections"]);
        Assert.Same(at28.Manifest.Nodes, at28.Nodes);

        Assert.Equal(new[] { "prompts/assessment.md", "prompts/extract.md", "prompts/scope.md", "prompts/section_notes.md" }, at28.Prompts!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The28Minimal_RendersTheBytesIts27TwinDid()
    {
        var (at27, at28) = Twins();
        var concepts = new Dictionary<string, string> { ["extract"] = """{ "concepts": [ "Asthma", "Cough" ] }""" };

        foreach (var nodeId in new[] { "extract", "scope" })
        {
            Assert.Equal(Render(at27, nodeId), Render(at28, nodeId));
        }

        Assert.Equal(Render(at27, "assessment", concepts), Render(at28, "assessment", concepts));

        foreach (var (item27, item28) in at27.Data!.Collections["sections"].Items.Zip(at28.Data!.Collections["sections"].Items))
        {
            var rendered27 = Render(at27, "section_notes", item: Fields(item27), forEach: "snippet.sections");
            var rendered28 = Render(at28, "section_notes", item: Fields(item28), forEach: "snippet.sections");
            Assert.Equal(rendered27, rendered28);
            Assert.Equal(ConsultGenerationProvenance.Sha256Hex(rendered27), ConsultGenerationProvenance.Sha256Hex(rendered28));
        }

        static IReadOnlyDictionary<string, string> Fields(WorkflowDataItem item) => new Dictionary<string, string>(item.Fields, StringComparer.Ordinal) { ["id"] = item.Id };
    }

    [Fact]
    public void The28Minimal_FiresDerivesAndRecordsAsIts27Twin_ButTheRenamedPromptPath()
    {
        var (at27, at28) = Twins();
        var supplied = V26JobStartTests.NewPatient;

        var fire27 = ConsultGenerationJobStarter.DecideFireSet(at27, supplied, null, V26JobStartTests.Scope(at27, supplied));
        var fire28 = ConsultGenerationJobStarter.DecideFireSet(at28, supplied, null, V26JobStartTests.Scope(at28, supplied));
        Assert.Equal(fire27.Firing.Select(r => r.Id), fire28.Firing.Select(r => r.Id));

        var skeleton27 = ConsultGenerationJobStarter.ResolveSkeleton(at27, supplied, V26JobStartTests.Scope(at27, supplied));
        var skeleton28 = ConsultGenerationJobStarter.ResolveSkeleton(at28, supplied, V26JobStartTests.Scope(at28, supplied));
        Assert.Equal(skeleton27.Items.Select(i => i["id"]), skeleton28.Items.Select(i => i["id"]));

        var edges27 = ConsultGenerationJobStarter.DeriveEdges(fire27.Package)!;
        var edges28 = ConsultGenerationJobStarter.DeriveEdges(fire28.Package)!;
        Assert.Equal(edges27.Keys.Order(StringComparer.Ordinal), edges28.Keys.Order(StringComparer.Ordinal));
        Assert.NotEmpty(edges28["assessment"]);

        var flows27 = ConsultGenerationJobStarter.FlowsOf(fire27.Package);
        var flows28 = ConsultGenerationJobStarter.FlowsOf(fire28.Package);
        foreach (var node in fire27.Package.Nodes!)
        {
            Assert.Equal(JsonSerializer.Serialize(edges27.GetValueOrDefault(node.Id)), JsonSerializer.Serialize(edges28.GetValueOrDefault(node.Id)));

            var row27 = ConsultGenerationJobStarter.DescribeNode(node, null, formats: at27.Formats, reads: edges27.GetValueOrDefault(node.Id), manifest: at27.Manifest, flows: flows27).AsRecordRowAt8();
            var row28 = ConsultGenerationJobStarter.DescribeNode(fire28.Package.Nodes!.First(n => n.Id == node.Id), null, formats: at28.Formats, reads: edges28.GetValueOrDefault(node.Id), manifest: at28.Manifest, flows: flows28).AsRecordRowAt8();

            if (node.Id == "section_notes")
            {
                // The one file the migration renamed to its id: the row names the new path.
                Assert.Equal("prompts/section.md", row27.Prompt);
                Assert.Equal("prompts/section_notes.md", row28.Prompt);
                row28 = row28 with { Prompt = row27.Prompt };
            }

            Assert.Equal(JsonSerializer.Serialize(row27), JsonSerializer.Serialize(row28));
        }

        Assert.Equal(9, StampOf(28));

        static int StampOf(int specVersion)
        {
            var state = new ConsultGenerationJobState { PackageSpecVersion = specVersion };
            ConsultGenerationJobEntity.Stamp(state, 6);
            return state.SchemaVersion;
        }
    }

    [Fact]
    public void The28Diagram_DrawsTheReadsIts27TwinDrew()
    {
        var (at27, at28) = Twins();
        var walk27 = WorkflowDerivedEdges.WalkOf(at27.Manifest, at27.SourceFiles!, at27.Formats);
        var walk28 = WorkflowDerivedEdges.WalkOf(at28.Manifest, at28.SourceFiles!, at28.Formats);

        Assert.Equal(
            WorkflowDagDiagram.Generate(at27.Manifest, walk27).Replace("prompts/section.md", "prompts/section_notes.md"),
            WorkflowDagDiagram.Generate(at28.Manifest, walk28));
        Assert.Contains(walk28.Templates, t => t.Kind == TemplateKind.Prompt && t.Id == "assessment");
    }

    [Fact]
    public void TheContentResponse_SendsA28ManifestWithoutPromptKeys_AndItValidates()
    {
        var (at27, at28) = Twins();

        var response = WorkflowPackageContentResponse.For(at28.Manifest, at28.SourceFiles!);
        Assert.All(response.Manifest.GetProperty("nodes").EnumerateArray(), node => Assert.False(node.TryGetProperty("prompt", out _)));
        var roundTripped = V28Package.Read(response.Manifest.GetRawText());
        Assert.Empty(V28Package.Errors((roundTripped, new Dictionary<string, string>(response.Files, StringComparer.Ordinal))));

        // Below 28 the response is the manifest as it was.
        var response27 = WorkflowPackageContentResponse.For(at27.Manifest, at27.SourceFiles!);
        Assert.Equal(JsonSerializer.Serialize(at27.Manifest, WorkflowPackageManifestJson.WriteOptions), response27.Manifest.GetRawText().Replace("\r", ""));
    }

    [Fact]
    public void TheCandidatePrompts_AreEveryNonFlowNodesImpliedFile_At28Only()
    {
        Assert.Equal(new[] { "prompts/extract.md", "prompts/scope.md", "prompts/assessment.md", "prompts/section_notes.md", "prompts/coverage.md" },
            WorkflowImpliedPaths.CandidatePrompts(V28Package.Minimal().Manifest));
        Assert.Empty(WorkflowImpliedPaths.CandidatePrompts(V27Package.Minimal().Manifest));
    }
}
