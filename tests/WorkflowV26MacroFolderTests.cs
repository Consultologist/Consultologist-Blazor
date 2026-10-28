using System.Text.Json;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1099 (package-format-v26-design.md § 5.3, § 5.4): macros as a map on the
/// wire, the list in memory; a macro folder declared as a snippet folder is —
/// the index the ordered list, the items macro files — read as
/// {{ for m in macro.&lt;folder&gt; }}, not placed.
/// </summary>
public class WorkflowV26MacroFolderTests
{
    private static string Write(WorkflowPackageManifest manifest) => JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions);

    /// <summary>The wire without its indentation, for a shape assertion.</summary>
    private static string Compact(string json) => System.Text.RegularExpressions.Regex.Replace(json, @"\s", string.Empty);

    /// <summary>The minimal with a macro folder of two items, read by the assessment prompt.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithFolder(string read = "{{ for m in macro.section_notes }}{{ m.label }}: {{ m.prompt }}{{ end }}")
    {
        var (manifest, files) = V26Package.Minimal();
        manifest = manifest with { Macros = manifest.Macros!.Append(new WorkflowMacroSpec("section_notes", null, "macros/section_notes/")).ToList() };
        files["macros/section_notes/index.json"] = """[ { "id": "hpi", "file": "hpi.md" }, { "id": "plan", "file": "plan.md" } ]""";
        files["macros/section_notes/hpi.md"] = V26Package.Header("HPI notes", instruction: "Note the history.") + "History noted: {{ profile.name }}.";
        files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes", instruction: "Note the plan.") + "Plan noted.";
        files["prompts/assessment.md"] = "Assess. " + read;
        return (manifest, files);
    }

    private static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => V26Package.Errors(bundle);

    // ----- the map on the wire -----------------------------------------------------

    [Fact]
    public void A26Manifest_WritesMacrosAsTheMap_AndReadsIt()
    {
        var json = Write(V26Package.Minimal().Manifest);
        Assert.Contains("\"macros\":{\"closing\":\"macros/closing.md\"}", Compact(json));

        var back = WorkflowPackageManifestJson.Read(json, "minimal", V26Package.Accepting26.ToList());
        var table = Assert.IsType<WorkflowMacroTable>(back.Macros);
        Assert.True(table.IsMap);
        Assert.Equal(("closing", "macros/closing.md"), (table[0].Id, table[0].File));
    }

    [Fact]
    public void A25Manifest_WritesTheListItAlwaysWrote_AndReadsIt()
    {
        var json = Write(V25Package.Minimal().Manifest);
        Assert.Contains("\"macros\":[{", Compact(json));

        var back = WorkflowPackageManifestJson.Read(json, "minimal", WorkflowPackageValidator.AcceptedSpecVersions);
        var table = Assert.IsType<WorkflowMacroTable>(back.Macros);
        Assert.False(table.IsMap);
        Assert.Equal(json, Write(back));
    }

    [Fact]
    public void TheList_IsRefusedAt26_AndTheMap_Below26()
    {
        var (manifest, files) = V26Package.Minimal();
        var listed = Write(manifest with { Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md") } });
        var atTwentySix = WorkflowPackageManifestJson.Read(listed, "listed", V26Package.Accepting26.ToList());
        Assert.Contains("macros is a list; at specVersion 26 macros is a map { \"<id>\": \"<file>\" | \"<dir>/\" } — the file's header carries label, instruction, optional, default and if.",
            V26Package.Errors((atTwentySix, files)));

        var (m25, f25) = V25Package.Minimal();
        var mapped = Write(m25 with { Macros = new List<WorkflowMacroSpec> { new("closing", null, "macros/closing.md") } });
        Assert.Contains("\"macros\":{", Compact(mapped));
        var atTwentyFive = WorkflowPackageManifestJson.Read(mapped, "mapped", WorkflowPackageValidator.AcceptedSpecVersions);
        var result = WorkflowPackageValidator.Validate(atTwentyFive, f25, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());
        Assert.Contains("macros is a map, which requires specVersion 26.", result.Errors);
    }

    // ----- the folder ----------------------------------------------------------------

    [Fact]
    public void AMacroFolder_IsReadAsAnIterable_WithTheMacroMembers()
    {
        Assert.Empty(Errors(WithFolder()));
        Assert.Empty(Errors(WithFolder("{{ for m in macro.section_notes }}{{ m.id }} {{ m.label }} {{ m.content }} {{ m.instruction }}{{ end }}")));
        Assert.Contains("Node 'assessment' prompt reads m.colour, which is not a member (a macro has .id, .label, .content, .prompt and .instruction).",
            Errors(WithFolder("{{ for m in macro.section_notes }}{{ m.colour }}{{ end }}")));
    }

    [Fact]
    public void TheWalk_ReachesTheItems_ThroughTheElementsReads()
    {
        var (manifest, files) = WithFolder();
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor, out var macroFolders);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);

        Assert.Equal(new[] { "section_notes/hpi", "section_notes/plan" }, walk.MacroFolderItemsOf("section_notes"));
        Assert.Contains(walk.Templates, t => t.Kind == TemplateKind.Macro && t.Id == "section_notes/hpi" && t.Subject == "Macro 'section_notes/hpi'");
        Assert.Contains(walk.Templates, t => t.Kind == TemplateKind.Instruction && t.Id == "section_notes/plan");
        Assert.Superset(new HashSet<string> { "section_notes", "section_notes/hpi", "section_notes/plan" }, walk.MacrosReached(Array.Empty<string>()).ToHashSet());
        Assert.Superset(new HashSet<string> { "section_notes/hpi", "section_notes/plan" }, walk.PromptReadsOf("assessment").ToHashSet());
    }

    // #1224: the pair judged on a folder item's header too, keyed <folder>/<item>.

    [Fact]
    public void AFolderItem_OptionalWithoutDefault_IsRefusedAt26()
    {
        var (manifest, files) = WithFolder();
        files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes", instruction: "Note the plan.", optional: true) + "Plan noted.";

        Assert.Contains("Macro 'section_notes/plan' is optional and declares no default; an optional macro must say what a run that makes no choice does.", Errors((manifest, files)));
    }

    [Fact]
    public void AFolderItem_DefaultWithoutOptional_IsRefusedAt26_AndThePairPublishes()
    {
        var (manifest, files) = WithFolder();
        files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes", instruction: "Note the plan.", @default: true) + "Plan noted.";
        Assert.Contains("Macro 'section_notes/plan' declares default but is not optional; only optional: true takes a per-run choice.", Errors((manifest, files)));

        files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes", instruction: "Note the plan.", optional: true, @default: true) + "Plan noted.";
        Assert.Empty(Errors((manifest, files)));
    }

    [Fact]
    public void AnItemReadInline_NeedsItsInstruction_AndAContentReadDoesNot()
    {
        var (manifest, files) = WithFolder();
        files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes") + "Plan noted.";

        Assert.Contains("Macro 'section_notes/plan' is read inline by node 'assessment' and declares no instruction; the model is told nothing (macros/section_notes/plan.md).", Errors((manifest, files)));

        var (contentOnly, contentFiles) = WithFolder("{{ for m in macro.section_notes }}{{ m.content }}{{ end }}");
        contentFiles["macros/section_notes/plan.md"] = V26Package.Header("Plan notes") + "Plan noted.";
        Assert.Empty(Errors((contentOnly, contentFiles)));
    }

    [Fact]
    public void AnItemsOwnIf_IsWalked_AndTheReaderWaitsOnWhatItReads()
    {
        var (manifest, files) = WithFolder();
        files["macros/section_notes/hpi.md"] = V26Package.Header("HPI notes", instruction: "Note the history.", @if: "node.scope.value == \"maybe\"") + "History noted.";
        Assert.Contains("Macro 'section_notes/hpi' if compares node.scope.value to 'maybe', which it does not declare (values: in_scope, out_of_scope).", Errors((manifest, files)));

        files["macros/section_notes/hpi.md"] = V26Package.Header("HPI notes", instruction: "Note the history.", @if: "node.scope.value == \"in_scope\"") + "History noted.";
        Assert.Empty(Errors((manifest, files)));
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor, out var macroFolders);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);
        Assert.Single(walk.Conditions, c => c.Site == WorkflowDerivedEdges.ConditionSite.Macro && c.OwnerId == "section_notes/hpi");
        Assert.Contains("scope", walk.EdgesOf("assessment"));
    }

    [Fact]
    public void TheFolder_IsResolvedAsASnippetFolderIs()
    {
        var (manifest, files) = WithFolder();

        files.Remove("macros/section_notes/index.json");
        Assert.Contains("Macro collection 'section_notes' is missing 'macros/section_notes/index.json'.", Errors((manifest, files)));

        (manifest, files) = WithFolder();
        files["macros/section_notes/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md" } ] }""";
        Assert.Contains("Macro folder 'section_notes' index.json at specVersion 26 is an ordered list [{ id, file }]; name and when live in the item file's header.", Errors((manifest, files)));

        (manifest, files) = WithFolder();
        files["macros/section_notes/index.json"] = """[ { "id": "hpi", "file": "hpi.md" }, { "id": "hpi", "file": "plan.md" } ]""";
        Assert.Contains("Macro collection 'section_notes' has duplicate item id 'hpi'.", Errors((manifest, files)));

        (manifest, files) = WithFolder();
        files["macros/section_notes/index.json"] = """[ { "id": "hpi", "file": "hpi.md" }, { "id": "plan", "file": "nope.md" } ]""";
        Assert.Contains("Macro collection 'section_notes' item 'plan' file 'macros/section_notes/nope.md' is missing from the package.", Errors((manifest, files)));

        (manifest, files) = WithFolder();
        files["macros/section_notes/plan.md"] = "Plan noted.";
        Assert.Contains("macros/section_notes/plan.md has no header; a macro file opens with { \"label\": … }.", Errors((manifest, files)));

        (manifest, files) = WithFolder();
        files["macros/section_notes/index.json"] = """[ { "id": "Hpi", "file": "hpi.md" } ]""";
        Assert.Contains("Macro folder 'section_notes' item id 'Hpi' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).", Errors((manifest, files)));
    }

    [Fact]
    public void AFolder_IsRead_NotPlaced()
    {
        var (manifest, files) = WithFolder();
        var onNode = V26Package.WithNode(manifest, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After), new("section_notes", At: WorkflowNodeMacroSpec.After) } });
        Assert.Contains("Node 'assessment' macro 'section_notes' names macro folder 'section_notes'; a folder is read ({{ for m in macro.section_notes }}), not placed, at specVersion 26.", Errors((onNode, files)));

        var item = V26Package.WithNode(manifest, "assessment", n => n with { Macros = new List<WorkflowNodeMacroSpec> { new("section_notes.hpi", At: WorkflowNodeMacroSpec.After) } });
        Assert.Contains("Node 'assessment' macro 'section_notes.hpi' names folder item 'section_notes.hpi'; placing a folder item waits for a package that needs it — read the folder ({{ for m in macro.section_notes }}).", Errors((item, files)));

        var onDeliverable = manifest with { Deliverables = manifest.Deliverables!.Select(d => d with { Macros = new List<WorkflowDeliverableMacroSpec> { new("section_notes", At: "append") } }).ToList() };
        Assert.Contains("Deliverable 'consult' macro 'section_notes' names macro folder 'section_notes'; a folder is read ({{ for m in macro.section_notes }}), not placed, at specVersion 26.", Errors((onDeliverable, files)));
    }

    [Fact]
    public void AFolder_AsAValue_IsRefused()
    {
        // #1116: an item by path reads directly (WorkflowV26DirectReadTests); the folder as a value stays refused.
        Assert.Contains(Errors(WithFolder("{{ macro.section_notes }}")), e => e.StartsWith("Node 'assessment' prompt reads macro.section_notes", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRenderer_IteratesAFolder_InTheIndexsOrder()
    {
        var run = new TemplateRunFacts("2026-10-26", "abcdef12", "test/general@v2026.10.8", "api.example.test");
        var profile = new TemplateProfileFacts("Dr Example", "Dr Example, FRCPC");
        var folders = new Dictionary<string, IReadOnlyList<TemplateMacroSource>>
        {
            ["section_notes"] = new List<TemplateMacroSource>
            {
                new("hpi", "HPI notes", "History noted.", "Note the history."),
                new("plan", "Plan notes", "Plan noted.", "Note the plan."),
            },
        };
        var scope = new TemplateRenderScope(
            new Dictionary<string, string>(), null, null, new Dictionary<string, TemplateNodeOutput>(), new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<TemplateFolderItem>>(), null, new Dictionary<string, TemplateMacroSource>(), run, profile,
            MacroFolders: folders, InertMacros: new HashSet<string>(StringComparer.Ordinal) { "section_notes/plan" });
        var trace = new TemplateRenderer.TemplateRenderTrace();

        var rendered = TemplateRenderer.Render("Prompt 'p'", "{{ for m in macro.section_notes }}[{{ m.id }}|{{ m.content }}|{{ m.prompt }}]{{ end }}", scope, null, trace);

        Assert.Equal("[hpi|History noted.|" + WorkflowMacroMechanics.Prompt("hpi", "Note the history.") + "][plan||]", rendered);
        Assert.Equal(new[] { "section_notes/hpi" }, trace.PromptReads);
        Assert.Equal(new[] { "section_notes/hpi" }, trace.ContentReads);
    }
}
