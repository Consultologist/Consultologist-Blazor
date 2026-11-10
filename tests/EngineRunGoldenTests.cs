using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1341 B2a: whole jobs, end to end, through the real starter, entity and
/// orchestrator (<see cref="EngineRun"/>) — and their every activity payload,
/// entity operation, rendered prompt, served record and event pinned in
/// <c>Golden/EngineRun*.txt</c>. Nesting (B2b–B2d) reworks the orchestrator's
/// keys and lists; a 26–29 job must run exactly as these did.
/// </summary>
public class EngineRunGoldenTests
{
    private const string Problems = """
        { "summary": "Two problems.", "problems": [ { "name": "Asthma", "severity": "low" }, { "name": "Sepsis", "severity": "high" } ] }
        """;

    private static readonly Dictionary<string, ConsultInputValue> Notes = new(StringComparer.Ordinal)
    {
        ["notes"] = ConsultInputValue.OfText("A fictional referral: a 40-year-old with wheeze and a fever."),
        ["kind"] = ConsultInputValue.OfText("new"),
    };

    /// <summary>The scripted replies: each node's text in its format; anything else a stand-in.</summary>
    private static string? Outputs(string key) => WorkflowItemPath.NodeOf(key) switch
    {
        "find_problems" => Problems,
        "extract" => """{ "concepts": [ { "term": "Asthma", "id": "195967001", "type": "disorder", "isActive": true } ] }""",
        "scope" => "in_scope",
        _ => null,
    };

    private static WorkflowPackage Load((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle, string cacheKey) =>
        WorkflowPackageStore.PackageOf(WorkflowPackageStore.Materialise(cacheKey, bundle.Manifest,
            new Dictionary<string, string>(bundle.Files, StringComparer.Ordinal), null, V29Catalog.Catalog, V25Package.Catalog(), V29Package.Accepting29));

    [Fact]
    public async Task A29Job_RunsAsItDidBeforeNesting()
    {
        var run = await EngineRun.StartAsync(Load(V29Package.Minimal(), "test@v29"), Notes,
            Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Matches("EngineRun29.txt", run.Trace());
    }

    [Fact]
    public async Task A29Job_WithASkippedGatedItem_AndAnInnerIf_RunsAsItDidBeforeNesting()
    {
        // The plan section is gated on the scope classifier and skipped (it answers in_scope); the
        // problems loop returns an inner if — a severe problem's draft is escalated, a mild one
        // takes no branch and composes as a skipped element.
        var bundle = V29Package.With(root =>
        {
            var nodes = root["nodes"]!.AsArray();
            nodes.Add(new JsonObject { ["id"] = "escalate", ["label"] = "Escalate", ["format"] = "prose" });
            nodes.Add(new JsonObject
            {
                ["id"] = "triage", ["label"] = "Triage", ["if"] = "item.value.severity == \"high\"",
                ["then"] = new JsonObject { ["nodes"] = new JsonArray("escalate"), ["ret"] = "node.escalate" },
            });
            var problems = V29Package.Node(root, "problems");
            problems["nodes"]!.AsArray().Add("triage");
            problems["ret"] = "node.triage";
        }, files =>
        {
            files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: "node.scope.value == \"out_of_scope\"") + "Plan.";
            files["prompts/escalate.md"] = "Escalate {{ item.value.name }}: {{ node.draft_problem.content }}";
        });

        var run = await EngineRun.StartAsync(Load(bundle, "test@v29"), Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Matches("EngineRun29Gated.txt", run.Trace());
    }

    [Fact]
    public async Task A26Job_RunsAsItDidBeforeNesting()
    {
        var bundle = V26Package.Minimal();
        var package = WorkflowPackageStore.PackageOf(WorkflowPackageStore.Materialise("test@v26", bundle.Manifest,
            new Dictionary<string, string>(bundle.Files, StringComparer.Ordinal), null, V29Catalog.Catalog, V25Package.Catalog(), V26Package.Accepting26));

        var run = await EngineRun.StartAsync(package, Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Matches("EngineRun26.txt", run.Trace());
    }

    private static void Matches(string name, string actual)
    {
        var golden = Path.Combine(Path.GetDirectoryName(SourceFile())!, "Golden", name);

        if (!File.Exists(golden))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, actual);
            Assert.Fail($"Wrote {golden}; review it and commit it.");
        }

        Assert.Equal(File.ReadAllText(golden).Replace("\r\n", "\n"), actual);
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
