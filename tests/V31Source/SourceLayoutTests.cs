using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the folder layout of part 01 § 2 — every folder lists its
/// members once in its <c>index.json</c>, the nine top-level folders and a function's four are
/// always present, and every other file is a stray.
/// </summary>
public class SourceLayoutTests
{
    private static SourceRefusal Only(SourceParseResult result)
    {
        var refusal = Assert.Single(result.Refusals);
        Assert.Null(result.Package);
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    private static void Items(Dictionary<string, byte[]> files, string index, params string[] items) =>
        files.WithJson(index, json => json["items"] = new JsonArray(items.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()));

    [Fact]
    public void AStrayFile_IsNamed()
    {
        var refusal = Only(SourceFixture.Minimal().With("nodes/notes.txt", "notes").Parse());

        Assert.Equal("S01-006", refusal.Id);
        Assert.Equal(
            "File 'nodes/notes.txt' is not listed: every file in nodes is named once in its index.json, or is index.json or README.md.",
            refusal.Sentence);
        Assert.Equal("nodes/notes.txt", refusal.Path);
    }

    [Fact]
    public void ADotFile_IsAStrayFile()
    {
        var result = SourceFixture.Minimal().With(".DS_Store", "x").With("snippets/.gitkeep", "").Parse();

        Assert.Equal(2, result.Refusals.Count);
        Assert.All(result.Refusals, r => Assert.Equal("S01-006", r.Id));
        Assert.Equal(
            "File '.DS_Store' is not listed: every file in the package root is named once in its index.json, or is index.json or README.md.",
            result.Refusals[0].Sentence);
        Assert.Equal(
            "File 'snippets/.gitkeep' is not listed: every file in snippets is named once in its index.json, or is index.json or README.md.",
            result.Refusals[1].Sentence);
    }

    [Fact]
    public void AReadme_IsAllowedInAnyFolder()
    {
        var result = SourceFixture.Full()
            .With("nodes/README.md", "# Nodes\n")
            .With("nodes/sections/README.md", "# Sections\n")
            .With("functions/draft_section/README.md", "# Draft a section\n")
            .With("snippets/standards/README.md", "# Standards\n")
            .Parse();

        Assert.Empty(result.Refusals);
        Assert.Equal(
            ["README.md", "functions/draft_section/README.md", "nodes/README.md", "nodes/sections/README.md", "snippets/standards/README.md"],
            result.Package!.Readmes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("# Sections\n", result.Package.Readmes["nodes/sections/README.md"]);
    }

    [Fact]
    public void DagMmd_IsAllowedAtTheRootOnly()
    {
        var refusal = Only(SourceFixture.Minimal()
            .With("dag.mmd", "flowchart TD\n")
            .With("nodes/dag.mmd", "flowchart TD\n")
            .Parse());

        Assert.Equal("S01-006", refusal.Id);
        Assert.Equal("nodes/dag.mmd", refusal.Path);
    }

    [Theory]
    [InlineData("nodes\\x.md", "the package root")]
    [InlineData("./nodes/x.md", "the package root")]
    [InlineData("/x.md", "the package root")]
    [InlineData("../x.md", "the package root")]
    [InlineData("nodes/Plan.md", "nodes")]
    [InlineData("nodes/sub/", "nodes")]
    [InlineData("nodes//x.md", "nodes")]
    public void OddPaths_AreStrayFiles(string path, string folder)
    {
        var refusal = Only(SourceFixture.Minimal().With(path, "x").Parse());

        Assert.Equal("S01-006", refusal.Id);
        Assert.Equal(
            $"File '{path}' is not listed: every file in {folder} is named once in its index.json, or is index.json or README.md.",
            refusal.Sentence);
    }

    [Fact]
    public void AStrayFolder_ReportsEachOfItsFilesOnce()
    {
        var result = SourceFixture.Full()
            .With("nodes/old/a.md", "a")
            .With("nodes/old/deeper/b.md", "b")
            .With("nodes/sections/old/c.md", "c")
            .Parse();

        Assert.Equal(
            [("nodes/old/a.md", "nodes"), ("nodes/old/deeper/b.md", "nodes"), ("nodes/sections/old/c.md", "nodes/sections")],
            result.Refusals.Select(r => (r.Path, r.Sentence.Split(" in ")[1].Split(" is named")[0])));
        Assert.All(result.Refusals, r => Assert.Equal("S01-006", r.Id));
    }

    [Fact]
    public void StraysInFlowAndFunctionFolders_AreNamedAgainstThem()
    {
        var result = SourceFixture.Full()
            .With("nodes/sections/paragraphs/x.md", "x")
            .With("nodes/sections_draft/route/x.md", "x")
            .With("nodes/billing_gate/x.md", "x")
            .With("functions/draft_section/x.md", "x")
            .With("functions/draft_section/snippets/x.md", "x")
            .With("snippets/standards/x.md", "x")
            .With("deliverables/emr/x.md", "x")
            .Parse();

        Assert.Equal(
            [
                "deliverables/emr", "functions/draft_section/snippets", "functions/draft_section", "nodes/billing_gate",
                "nodes/sections/paragraphs", "nodes/sections_draft/route", "snippets/standards",
            ],
            result.Refusals.Select(r => r.Sentence.Split(" in ")[1].Split(" is named")[0]));
        Assert.All(result.Refusals, r => Assert.Equal("S01-006", r.Id));
    }

    [Fact]
    public void AFunctionListedAsAFile_IsMissing()
    {
        var files = SourceFixture.Minimal().With("functions/draft.md", "x");
        Items(files, "functions/index.json", "draft");

        var result = files.Parse();

        Assert.Equal(["S01-006", "S01-009"], result.Refusals.Select(r => r.Id));
        Assert.Equal("functions/draft.md", result.Refusals[0].Path);
        Assert.Equal("functions/index.json lists 'draft', but functions has no draft.md or draft/.", result.Refusals[1].Sentence);
    }

    [Fact]
    public void AMissingIndex_IsNamed()
    {
        // snippets/ still holds its other files, so the folder is there without its index; they are not strays.
        var refusal = Only(SourceFixture.Full().Without("snippets/index.json").Parse());

        Assert.Equal("S01-007", refusal.Id);
        Assert.Equal(
            "snippets has no index.json; every folder lists its members, even when it has none (\"items\": []).",
            refusal.Sentence);
        Assert.Equal("snippets/index.json", refusal.Path);
    }

    [Fact]
    public void AMissingTopFolder_IsNamed()
    {
        var refusal = Only(SourceFixture.Minimal().Without("tests/index.json").Parse());

        Assert.Equal("S01-008", refusal.Id);
        Assert.Equal("The package has no tests/ folder; all nine top-level folders are always present.", refusal.Sentence);
    }

    [Fact]
    public void AMissingFunctionDefinitionFolder_IsNamed()
    {
        var refusal = Only(SourceFixture.Full().Without("functions/draft_section/macros/index.json").Parse());

        Assert.Equal("S01-007", refusal.Id);
        Assert.Equal(
            "functions/draft_section/macros has no index.json; every folder lists its members, even when it has none (\"items\": []).",
            refusal.Sentence);
    }

    [Fact]
    public void AListedMemberWithNoFile_IsNamed()
    {
        var files = SourceFixture.Minimal();
        Items(files, "nodes/index.json", "plan", "ghost");

        var refusal = Only(files.Parse());

        Assert.Equal("S01-009", refusal.Id);
        Assert.Equal("nodes/index.json lists 'ghost', but nodes has no ghost.md or ghost/.", refusal.Sentence);
        Assert.Equal("nodes/index.json", refusal.Path);
    }

    [Fact]
    public void AListedMemberWithNoFile_InAFlowFolder_NamesItsList()
    {
        var files = SourceFixture.Full();
        files.WithJson("nodes/refine/index.json", json => json["nodes"] = new JsonArray("draft_summary", "judge", "ghost"));

        var refusal = Only(files.Parse());

        Assert.Equal("nodes/refine/index.json nodes lists 'ghost', but nodes/refine has no ghost.md or ghost/.", refusal.Sentence);
    }

    [Fact]
    public void AListedMemberOfTheWrongKind_IsMissing_AndItsFilesAreStrays()
    {
        var files = SourceFixture.Minimal().With("renderers/fancy/index.json", "{}");
        Items(files, "renderers/index.json", "fancy");

        var result = files.Parse();

        // Ordered by path: renderers/fancy/index.json sorts before renderers/index.json.
        Assert.Equal(["S01-006", "S01-009"], result.Refusals.Select(r => r.Id));
        Assert.Equal("renderers/fancy/index.json", result.Refusals[0].Path);
        Assert.Equal("renderers/index.json lists 'fancy', but renderers has no fancy.md or fancy/.", result.Refusals[1].Sentence);
    }

    [Fact]
    public void AMemberListedTwice_IsNamed()
    {
        var files = SourceFixture.Minimal();
        Items(files, "nodes/index.json", "plan", "plan", "plan");

        var refusal = Only(files.Parse());

        Assert.Equal("S01-010", refusal.Id);
        Assert.Equal("nodes/index.json lists 'plan' twice.", refusal.Sentence);
    }

    [Fact]
    public void ATrailingSlash_IsNamed()
    {
        var files = SourceFixture.Full();
        Items(files, "snippets/index.json", "closing_line", "standards/");

        var refusal = Only(files.Parse());

        Assert.Equal("S01-011", refusal.Id);
        Assert.Equal("snippets/index.json lists 'standards/'; list a folder by its bare id.", refusal.Sentence);
    }

    [Fact]
    public void AFileAndAFolderOfOneId_AreRefused()
    {
        var refusal = Only(SourceFixture.Minimal().With("nodes/plan/index.json", "{}").Parse());

        Assert.Equal("S01-012", refusal.Id);
        Assert.Equal("nodes holds both plan.md and plan/: an id names one file or one folder.", refusal.Sentence);
    }

    [Fact]
    public void AListedIdIsHeldToTheIdRules()
    {
        var files = SourceFixture.Minimal();
        files.With("nodes/Plan.md", SourceFixture.Text(files, "nodes/plan.md")).Without("nodes/plan.md");
        Items(files, "nodes/index.json", "Plan");

        // The refused entry accounts for no file, so nodes/Plan.md is a stray as well.
        var result = files.Parse();
        Assert.Equal([("S01-006", "nodes/Plan.md"), ("S01-030", "nodes/index.json")], result.Refusals.Select(r => (r.Id, r.Path)));

        var full = SourceFixture.Full();
        full.With("macros/phrases/value.md", SourceFixture.Text(full, "macros/phrases/thanks.md")).Without("macros/phrases/thanks.md");
        Items(full, "macros/phrases/index.json", "value");

        var refusal = full.Parse().Refusals[0];
        Assert.Equal("S01-033", refusal.Id);
        Assert.Equal("Id 'value' is reserved: member 'value' is read in references, so nothing may be called 'value'.", refusal.Sentence);
    }

    [Fact]
    public void AListedIdThatIsRefused_AccountsForNoFile()
    {
        var files = SourceFixture.Minimal().With("nodes/sections/junk.md", SourceFixture.Text(SourceFixture.Minimal(), "nodes/plan.md"));
        Items(files, "nodes/index.json", "plan", "sections/junk");

        var result = files.Parse();

        Assert.All(result.Refusals, V31Contract.AssertSentence);
        Assert.Equal(
            [
                ("S01-030", "nodes/index.json"),
                ("S01-006", "nodes/sections/junk.md"),
            ],
            result.Refusals.Select(r => (r.Id, r.Path)));
        Assert.Equal(
            "File 'nodes/sections/junk.md' is not listed: every file in nodes is named once in its index.json, or is index.json or README.md.",
            result.Refusals[1].Sentence);
    }

    [Fact]
    public void AListedIdThatIsRefused_LeavesItsFolderToBeJudgedOnItsOwn()
    {
        var files = SourceFixture.Full();
        files["nodes/Refine/index.json"] = files["nodes/refine/index.json"];
        Items(files, "nodes/index.json",
            "plan", "section_count", "sections", "sections_draft", "refine", "billing_gate",
            "standards", "extract_input_terms", "extract_note_terms", "Refine");

        var result = files.Parse();

        Assert.Equal(
            [("S01-006", "nodes/Refine/index.json"), ("S01-030", "nodes/index.json")],
            result.Refusals.Select(r => (r.Id, r.Path)));
    }

    [Fact]
    public void ANestedDeliveryFolder_IsRefused()
    {
        var refusal = Only(SourceFixture.Full()
            .With("deliverables/emr/inner/index.json", "{}")
            .With("deliverables/emr/inner/x.md", "x")
            .Parse());

        Assert.Equal("S01-013", refusal.Id);
        Assert.Equal(
            "Delivery folder 'deliverables/emr/inner' is inside another delivery folder; delivery folders are one level deep.",
            refusal.Sentence);
        Assert.Equal("deliverables/emr/inner", refusal.Path);
    }

    [Fact]
    public void AnUnreadableIndex_DoesNotMakeStraysOfItsFolder()
    {
        var refusal = Only(SourceFixture.Full().With("nodes/sections/index.json", "{ nope").Parse());

        Assert.Equal("S01-017", refusal.Id);
        Assert.Equal("nodes/sections/index.json", refusal.Path);
    }

    [Fact]
    public void NoManifest_IsNamed()
    {
        var refusal = Only(SourceFixture.Minimal().Without("manifest.json").Parse());

        Assert.Equal("A02-003", refusal.Id);
        Assert.Equal("The package has no manifest.json; it holds the package's name, version and inputs.", refusal.Sentence);
        Assert.Equal("manifest.json", refusal.Path);
    }

    [Fact]
    public void AFileAndAFolderOfOneId_InAFlowFolder_AreRefused()
    {
        var files = SourceFixture.Full().With("nodes/sections/outline/index.json", "{}");

        var refusal = Only(files.Parse());

        Assert.Equal("S01-012", refusal.Id);
        Assert.Equal("nodes/sections holds both outline.md and outline/: an id names one file or one folder.", refusal.Sentence);
        Assert.Equal("nodes/sections/outline.md", refusal.Path);
    }

    [Fact]
    public void AnIntakeRefusal_StillReturnsTheNormalisedFiles()
    {
        var files = SourceFixture.Minimal().With("nodes/plan.md", SourceFixture.Text(SourceFixture.Minimal(), "nodes/plan.md").Replace("\n", "\r\n"));
        files["deliverables/note.md"] = [0xEF, 0xBB, 0xBF, (byte)'x'];

        var result = files.Parse();

        Assert.Equal("S01-014", Assert.Single(result.Refusals).Id);
        Assert.Equal(["nodes/plan.md"], result.Normalised);
    }

    [Fact]
    public void AnIntakeRefusal_EndsTheParse()
    {
        var files = SourceFixture.Minimal().With("nodes/notes.txt", "x");
        files["nodes/plan.md"] = [0xEF, 0xBB, 0xBF, (byte)'x'];

        var refusal = Only(files.Parse());

        Assert.Equal("S01-014", refusal.Id);
    }

    [Fact]
    public void Refusals_AreOrderedByPath_AndSaidOnce()
    {
        var files = SourceFixture.Full().With("tests/x.txt", "x").With("checks/x.txt", "x").With("a.txt", "x");
        Items(files, "macros/index.json", "follow_up", "phrases", "ghost", "ghost");

        var result = files.Parse();

        Assert.Equal(
            ["a.txt", "checks/x.txt", "macros/index.json", "macros/index.json", "tests/x.txt"],
            result.Refusals.Select(r => r.Path));
        Assert.Equal(["S01-009", "S01-010"], result.Refusals.Where(r => r.Path == "macros/index.json").Select(r => r.Id));
    }
}
