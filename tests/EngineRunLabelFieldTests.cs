using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1344 B5 (package-format-v30-design.md § 5): items named by a field at 30 — an
/// array-of-objects input by its <c>labelField</c> (modelled in-repo on
/// example-conditional-sections's <c>sections</c>), a loop over a node's value by the
/// loop's; a blank field falls back to label plus position, and below 30 every name is
/// as it was. The name is the item's <c>name</c>, so the rail, the block names, History
/// and the composed headings follow with no other change.
/// </summary>
public class EngineRunLabelFieldTests
{
    // ---- an input's items -------------------------------------------------------------------

    private static JsonObject Sections(string? labelField) => new()
    {
        ["id"] = "sections", ["label"] = "Sections to produce", ["type"] = "array", ["items"] = "object",
        ["fields"] = new JsonArray(
            new JsonObject { ["id"] = "title", ["label"] = "Title", ["type"] = "text", ["required"] = true },
            new JsonObject { ["id"] = "kind", ["label"] = "Kind", ["type"] = "enum", ["required"] = true, ["values"] = new JsonArray("narrative", "table", "omit") },
            new JsonObject { ["id"] = "notes", ["label"] = "Notes for this section", ["type"] = "text", ["required"] = false }),
        ["labelField"] = labelField,
    };

    private static ConsultInputValue Section(string title, string kind) => ConsultInputValue.OfObject(new[]
    {
        new ConsultInputEntry("title", ConsultInputValue.OfText(title)),
        new ConsultInputEntry("kind", ConsultInputValue.OfText(kind)),
    });

    private static Dictionary<string, ConsultInputValue> NotesAnd(params ConsultInputValue[] sections) =>
        new(EngineRunNestedTests.Notes, StringComparer.Ordinal) { ["sections"] = ConsultInputValue.OfArray(sections) };

    /// <summary>The 30 minimal with a loop drafting each section the referral asks for.</summary>
    private static WorkflowPackage Drafting(string? labelField = "title") =>
        EngineRunNestedTests.Load(V30Package.With(
            root =>
            {
                var input = Sections(labelField);
                if (labelField is null)
                {
                    input.Remove("labelField");
                }

                root["inputs"]!.AsArray().Add(input);
                root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "draft_section", ["label"] = "Draft the section", ["format"] = "prose" });
                root["nodes"]!.AsArray().Add(new JsonObject
                {
                    ["id"] = "sections_draft", ["label"] = "Drafting the sections", ["for"] = "item in input.sections",
                    ["nodes"] = new JsonArray("draft_section"), ["ret"] = "node.draft_section",
                });
                root["deliverables"]![0]!["sources"]!.AsArray().Add("node.sections_draft");
            },
            files => files["prompts/draft_section.md"] = "Draft {{ item.value.title }} ({{ item.value.kind }}) from {{ input.notes }}"));

    [Fact]
    public async Task AnInputsItems_AreNamedByTheirField_EverywhereTheNameReaches()
    {
        var run = await EngineRun.StartAsync(
            Drafting(), NotesAnd(Section("History of present illness", "narrative"), Section("Plan", "table")), EngineRunNestedTests.Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(("History of present illness", "Plan"),
            (run.State.Blocks["consult:sections_draft:0"].Name, run.State.Blocks["consult:sections_draft:1"].Name));
        Assert.Contains(run.State.History, e => e.Label.Contains("History of present illness", StringComparison.Ordinal));
        Assert.Contains("History of present illness", Assert.Single(run.State.AssembledDocuments!, d => d.ResultId == "consult").Text);
        Matches("EngineRun30LabelField.txt", run.Trace());
    }

    [Fact]
    public async Task ABlankTitle_FallsBackToLabelAndPosition()
    {
        var run = await EngineRun.StartAsync(
            Drafting(), NotesAnd(Section("History of present illness", "narrative"), Section("   ", "table")), EngineRunNestedTests.Outputs);

        Assert.Equal(("History of present illness", "Sections to produce 2"),
            (run.State.Blocks["consult:sections_draft:0"].Name, run.State.Blocks["consult:sections_draft:1"].Name));
    }

    [Fact]
    public void AnInputWithoutALabelField_NamesItsItemsAsBefore()
    {
        var input = new WorkflowInputSpec("sections", "Sections to produce", true, WorkflowInputTypes.Array);
        var items = WorkflowInputFans.Items(input, ConsultInputValue.OfArray(new[] { Section("History", "narrative"), Section("Plan", "table") }));

        Assert.Equal(new[] { "Sections to produce 1", "Sections to produce 2" }, items.Select(item => item["name"]));
    }

    [Fact]
    public void AnInputsLabelField_TrimsTheText_AndFallsBackOnABlankOrMissingField()
    {
        var input = new WorkflowInputSpec("sections", "Sections to produce", true, WorkflowInputTypes.Array) with { LabelField = "title" };
        var items = WorkflowInputFans.Items(input, ConsultInputValue.OfArray(new[]
        {
            Section("  History  ", "narrative"),
            Section("", "table"),
            ConsultInputValue.OfObject(new[] { new ConsultInputEntry("kind", ConsultInputValue.OfText("omit")) }),
        }));

        Assert.Equal(new[] { "History", "Sections to produce 2", "Sections to produce 3" }, items.Select(item => item["name"]));
    }

    // ---- a node-value loop's items ----------------------------------------------------------

    [Fact]
    public void ANodeValueLoopsLabelField_NamesItsItems_AndFallsBack()
    {
        var output = new NodeRunResult("""{ "problems": [ { "name": " Anaemia " }, { "name": "" }, { "name": 3 }, { } ] }""", null, null, null, 6);

        var named = ConsultNodeValueLoops.ItemsOf(output, "node.find_problems.value.problems", "Problems", "name").Items;
        var plain = ConsultNodeValueLoops.ItemsOf(output, "node.find_problems.value.problems", "Problems").Items;

        Assert.Equal(new[] { "Anaemia", "Problems 2", "Problems 3", "Problems 4" }, named.Select(item => item["name"]));
        Assert.Equal(new[] { "Problems 1", "Problems 2", "Problems 3", "Problems 4" }, plain.Select(item => item["name"]));
    }

    /// <summary>The 30 minimal with the v30-label-field vector's loops: problems by name, parts by title.</summary>
    private static WorkflowPackage Named(Action<JsonObject>? change = null) =>
        EngineRunNestedTests.Load(EngineRunNestedTests.TwoLevels(root =>
        {
            V30Package.Node(root, "problems")["labelField"] = "name";
            V30Package.Node(root, "parts")["labelField"] = "title";
            change?.Invoke(root);
        }));

    [Fact]
    public async Task ANodeValueLoopsItems_AreNamedByTheirField_AndNestedItemsKeepTheirOwn()
    {
        var run = await EngineRun.StartAsync(Named(), EngineRunNestedTests.Notes, EngineRunNestedTests.Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal("Asthma", run.State.Blocks["consult:problems:1"].Name);
        var parts = Assert.Single(run.State.Nodes!, n => n.Id == "parts");
        Assert.Equal("title", parts.LabelField);
        Assert.Contains(run.State.History, e => e.Kind == "loop-resolved" && e.Label.StartsWith("Paragraphs (", StringComparison.Ordinal));
        // The inner loop declares no labelField: its items keep label plus position.
        Assert.Equal("Paragraphs 1", run.State.Blocks["consult:paragraphs:1/1"].Name);
    }

    [Fact]
    public async Task AnItemNotRun_IsNamedByItsField_InTheSkippedReason()
    {
        var run = await EngineRun.StartAsync(
            Named(root => V30Package.Node(root, "parts")["until"] = "true"), EngineRunNestedTests.Notes, EngineRunNestedTests.Outputs);

        var notRun = run.State.Blocks.Values.Where(b => b.Status == ConsultGenerationBlockStatuses.Skipped && b.Reason?.StartsWith("not run", StringComparison.Ordinal) == true).ToList();
        Assert.NotEmpty(notRun);
        Assert.All(notRun, block => Assert.Contains("(History)", block.Reason));
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
