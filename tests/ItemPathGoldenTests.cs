using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1340 (package-format-v30-design.md § 1.2): the byte-for-byte pin behind B1.
/// A 29 job with item-keyed state of every kind — a loop over a node's value, its
/// body node's item rows, an inner if's branches and skipped item, a skipped and a
/// completed block, a folder fan's item progress — served and turned into events,
/// recorded in <c>Golden/ItemPaths29.txt</c> before the path helper existed. The
/// helper must leave every key, row and event exactly as they were (timestamps
/// masked; nothing else).
/// </summary>
public class ItemPathGoldenTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly DateTimeOffset At = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyDictionary<string, string> Item(string id, string name) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = id, ["name"] = name, ["content"] = "std" };

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State) Job()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        var state = ConsultGenerationJobState.Create("job-1", "user-1", new[] { Item("hpi", "History of Present Illness"), Item("plan", "Plan") });
        StateProperty.SetValue(entity, state);
        entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", new[] { Item("hpi", "History of Present Illness"), Item("plan", "Plan") }, PackageSpecVersion: 29)).GetAwaiter().GetResult();
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!);
    }

    private static string Snapshot()
    {
        var (entity, state) = Job();
        state().Nodes = new List<ConsultNodeDescriptor>
        {
            new("find_problems", "Find problems", "prompts/find_problems.md", Format: "problem_list", Prompt: "prompts/find_problems.md"),
            new("problems", "Problems", Kind: "for", Collection: "node.find_problems.value.problems", Body: new[] { "draft_problem", "triage" }, Ret: "draft_problem"),
            new("draft_problem", "Draft problem", "prompts/draft_problem.md", ForEach: "node.find_problems.value.problems", In: "problems", Format: "prose", Prompt: "prompts/draft_problem.md"),
            new("triage", "Triage", ForEach: "node.find_problems.value.problems", Kind: "if", Expression: "item.value.severity == \"high\"", Body: new[] { "escalate" }, In: "problems"),
            new("section_notes", "Section notes", "prompts/section.md", ForEach: "snippet.sections", In: "sections", Format: "prose", Prompt: "prompts/section.md"),
            new("sections", "Sections", Kind: "for", Collection: "snippet.sections", Body: new[] { "section_notes" }, Ret: "section_notes", Count: 2),
        };
        state().UnresolvedLoops = new List<string> { "problems" };
        state().ItemSteps = new List<ConsultItemStepDescriptor> { new("draft_problem", "Drafting problem"), new("section_notes", "Writing section notes") };
        const string reason = "needs `item.value.severity == \"high\"`; it does not hold";

        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate("find_problems", "Find problems", null, "in-1", "out-1", 1, 6));
        entity.ResolveLoop(new ConsultGenerationLoopResolution(
            "problems", "node.find_problems.value.problems",
            new List<ConsultCollectionItem> { new("1", "Problems 1"), new("2", "Problems 2") },
            new List<ConsultCollectionItem> { new("consult:problems:1", "Problems 1"), new("consult:problems:2", "Problems 2") },
            4, At)).GetAwaiter().GetResult();
        entity.MarkNodeItemCompleted(new ConsultGenerationNodeItemUpdate("draft_problem", "Draft problem", "1", "Problems 1", null, "in-2", "out-2", 1, 2));
        entity.RecordFlowBranch(new ConsultGenerationFlowBranch("triage", "1", "then"));
        entity.RecordFlowBranch(new ConsultGenerationFlowBranch("triage", "2", "none"));
        entity.MarkNodeItemSkipped(new ConsultGenerationNodeItemSkip("triage", "2", "Triage", reason));
        entity.SkipBlock(new ConsultGenerationBlockSkip("consult:problems:2", "Problems 2", reason)).GetAwaiter().GetResult();
        entity.CompleteBlock(new BlockGenerationResult("consult:problems:1", "Problems 1", true, "Drafted.", null)).GetAwaiter().GetResult();
        entity.MarkNodeItemCompleted(new ConsultGenerationNodeItemUpdate("section_notes", "Section notes", "hpi", "History of Present Illness", null, "in-3", "out-3", 1, 1));
        entity.MarkNodeItemCompleted(new ConsultGenerationNodeItemUpdate("section_notes", "Section notes", "plan", "Plan", null, "in-4", "out-4", 1, 1));
        entity.CompleteBlock(new BlockGenerationResult("consult:sections:hpi", "History of Present Illness", true, "HPI.", null)).GetAwaiter().GetResult();

        var served = state().ToResponse();
        var text = new StringBuilder();
        text.AppendLine("# the served record");
        text.AppendLine(JsonSerializer.Serialize(served, new JsonSerializerOptions { WriteIndented = true }));
        text.AppendLine("# the event candidates");

        foreach (var candidate in ConsultGenerationJobs.CreateSemanticEventCandidates(served))
        {
            text.AppendLine($"{candidate.EventType} | {candidate.EventKey} | {candidate.PayloadJson}");
        }

        text.AppendLine("# instance keys");
        text.AppendLine(ConsultNodeScheduler.InstanceKey("find_problems", null));
        text.AppendLine(ConsultNodeScheduler.InstanceKey("draft_problem", "2"));
        text.AppendLine(ConsultNodeScheduler.InstanceKey("section_notes", "hpi"));

        // Clock readings are the only thing that moves between runs.
        return Regex.Replace(text.ToString(), @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?", "<time>").Replace("\r\n", "\n");
    }

    [Fact]
    public void A29JobsKeysRowsAndEvents_AreByteForByteAsBeforeThePathHelper()
    {
        var golden = Path.Combine(Path.GetDirectoryName(SourceFile())!, "Golden", "ItemPaths29.txt");
        var actual = Snapshot();

        if (!File.Exists(golden))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, actual);
            Assert.Fail($"Wrote {golden}; review it and commit it before the refactor.");
        }

        Assert.Equal(File.ReadAllText(golden).Replace("\r\n", "\n"), actual);
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
