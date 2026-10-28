using System.Text.Json;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1157 (package-format-v26-design.md § 4, § 5): the store's read at 26 —
/// the nodes' prompt files in the download set, a folder's index in its
/// version's shape, the header split done once after validation and carried
/// on the package beside the raw form, the prompts keyed by path, the data
/// through the headers; and the done-when: a 26 package with headers renders
/// the bytes its 25 twin did.
/// </summary>
public class WorkflowPackageStoreMaterialiseTests
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

    private static readonly TemplateRunFacts Run = new("2026-10-27", "abcdef12", "general@v2026.10.1", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");
    private static readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal) { ["notes"] = "Referral.", ["kind"] = "new" };

    /// <summary>The store's load without the blobs: materialise, then the package as ResolveAsync builds it.</summary>
    internal static WorkflowPackage Load(WorkflowPackageManifest manifest, Dictionary<string, string> files)
    {
        var loaded = WorkflowPackageStore.Materialise($"{manifest.Name}@{manifest.Version}", manifest, files, null, Catalog, V25Package.Catalog(), V26Package.Accepting26);
        return new WorkflowPackage(manifest, loaded.Prompts, manifest.Nodes, loaded.SchemaContracts, loaded.Data, null, loaded.Files,
            WorkflowPackageStore.ResolveResultSet(manifest), loaded.Stamp, loaded.ContractAgents, loaded.CatalogRef, loaded.Formats, loaded.FormatsRef, Texts: loaded.Texts, MacroFolders: loaded.MacroFolders);
    }

    /// <summary>The 25 minimal and its migrated 26 twin, each loaded the store's way.</summary>
    private static (WorkflowPackage At25, WorkflowPackage At26) Twins()
    {
        var (manifest, files) = V25Package.Minimal();
        var at25 = Load(manifest, new Dictionary<string, string>(files, StringComparer.Ordinal));

        var migrated = WorkflowPackageMigration25To26.Migrate(JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions), files);
        Assert.True(migrated.Succeeded, string.Join(" | ", migrated.Refusals));
        var twin = WorkflowPackageManifestJson.Read(migrated.ManifestJson, "twin", V26Package.Accepting26.ToList());
        var at26 = Load(twin, new Dictionary<string, string>(migrated.Files, StringComparer.Ordinal));
        return (at25, at26);
    }

    private static ConsultPromptNodeActivityInput Input(string nodeId, string promptId, IReadOnlyDictionary<string, string>? nodeTexts = null, IReadOnlyDictionary<string, string>? item = null, string? forEach = null) =>
        new(nodeId, promptId, Inputs, "general@v2026.10.1", Format: "prose", Producer: "attested",
            NodeTexts: nodeTexts ?? new Dictionary<string, string>(), Item: item, ForEach: forEach, Run: Run, Profile: Profile);

    [Fact]
    public void TheMinimal25_AndItsMigratedTwin_RenderTheSameBytes_LoadedTheStoresWay()
    {
        var (at25, at26) = Twins();
        var concepts = new Dictionary<string, string> { ["extract"] = """{ "concepts": [ "Asthma", "Cough" ] }""" };

        // The prompt, found by id at 25 and by path at 26, renders the same bytes.
        var assessment25 = ConsultRenderPayload.RenderAt25(Input("assessment", "assessment", concepts), at25, at25.Prompts!["assessment"].TemplateText);
        var assessment26 = ConsultRenderPayload.RenderAt25(Input("assessment", "prompts/assessment.md", concepts), at26, at26.Prompts!["prompts/assessment.md"].TemplateText);
        Assert.Equal(assessment25, assessment26);
        Assert.Equal("Assess.\nFollow the local standards.\nReferral.\n- Asthma\n- Cough\n", assessment26);

        // A fanned item: the 25 item carries name, the 26 item its header's label; the body is the body.
        var item25 = at25.Data!.Collections["sections"].Items[0];
        var item26 = at26.Data!.Collections["sections"].Items[0];
        var section25 = ConsultRenderPayload.RenderAt25(Input("section_notes:hpi", "section", item: Fields(item25), forEach: "snippet.sections"), at25, at25.Prompts!["section"].TemplateText);
        var section26 = ConsultRenderPayload.RenderAt25(Input("section_notes:hpi", "prompts/section.md", item: Fields(item26), forEach: "snippet.sections"), at26, at26.Prompts!["prompts/section.md"].TemplateText);
        Assert.Equal(section25, section26);
        Assert.Equal("HPI: History: Referral.", section26);

        // The macro: the instruction is the manifest's at 25 and the header's at 26; the text is the body.
        const string macroRead = "{{ macro.closing.prompt }}|{{ macro.closing.content }}";
        var macro25 = ConsultRenderPayload.RenderAt25(Input("assessment", "assessment"), at25, macroRead);
        var macro26 = ConsultRenderPayload.RenderAt25(Input("assessment", "prompts/assessment.md"), at26, macroRead);
        Assert.Equal(macro25, macro26);
        Assert.Equal(WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed.") + "|Closing: Dr Example.", macro26);

        static IReadOnlyDictionary<string, string> Fields(WorkflowDataItem item) => new Dictionary<string, string>(item.Fields, StringComparer.Ordinal) { ["id"] = item.Id };
    }

    [Fact]
    public void A26Package_PromptsAreKeyedByPath_OnePerDistinctFile()
    {
        var (manifest, files) = V26Package.Minimal();
        // Two nodes on one file: one template.
        manifest = V26Package.WithNode(manifest, "scope", n => n with { Prompt = "prompts/extract.md" });
        var package = Load(manifest, files);

        Assert.True(package.HasPrompts);
        Assert.Equal(new[] { "prompts/assessment.md", "prompts/extract.md", "prompts/section.md" }, package.Prompts!.Keys.Order());
        var extract = package.Prompts["prompts/extract.md"];
        Assert.Equal(("prompts/extract.md", files["prompts/extract.md"], 0, null, false), (extract.Id, extract.TemplateText, extract.Variables.Count, extract.PreludeText, extract.Raw));
    }

    [Fact]
    public void A26Package_CarriesItsTextsBesideTheRawForm()
    {
        var (manifest, files) = V26Package.Minimal();
        files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Add a closing line if the plan changed.", @if: "input.kind == \"new\"", optional: true, @default: false) + "Closing: {{ profile.name }}.";
        var package = Load(manifest, files);

        // Raw stays raw: the file opens with its header, the manifest's macro carries nothing.
        Assert.StartsWith("{", package.SourceFiles!["macros/closing.md"], StringComparison.Ordinal);
        Assert.Null(package.Manifest.Macros![0].Label);
        // The texts: the body, the header's facts, the gate by path.
        Assert.Equal("Closing: {{ profile.name }}.", package.TextOf("macros/closing.md"));
        var closing = package.EffectiveManifest.Macros![0];
        Assert.Equal(("Closing", "Add a closing line if the plan changed.", true, false), (closing.Label, closing.Instruction, closing.Optional, closing.Default));
        Assert.Equal("input.kind == \"new\"", package.Texts!.Headers["macros/closing.md"].If);
        // A prompt file is in both maps unchanged.
        Assert.Equal(package.SourceFiles["prompts/assessment.md"], package.TextOf("prompts/assessment.md"));
    }

    [Fact]
    public void AFolderItemsLabelAndIf_ReachTheData()
    {
        var (manifest, files) = V26Package.Minimal();
        files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: "input.kind == \"new\"") + "Plan.";
        var package = Load(manifest, files);

        var plan = package.Data!.Collections["sections"].Items[1];
        Assert.Equal(("plan", "Plan", "Plan.", "input.kind == \"new\""), (plan.Id, plan.Fields["label"], plan.Fields["content"], plan.If));
        Assert.Equal("Follow the local standards.", package.Data.Scalars["standards"]);
    }

    [Fact]
    public void Below26_TextsIsNull_AndPromptsAreKeyedById()
    {
        var (manifest, files) = V25Package.Minimal();
        var package = Load(manifest, files);

        Assert.Null(package.Texts);
        Assert.Same(package.Manifest, package.EffectiveManifest);
        Assert.Same(package.SourceFiles, package.TextFiles);
        Assert.Equal(new[] { "assessment", "extract", "scope", "section" }, package.Prompts!.Keys.Order());
        Assert.Equal("Closing", package.EffectiveManifest.Macros![0].Label);
    }

    [Fact]
    public void TheRootFiles_At26_AreTheNodesPromptFiles_AndAFolderEntryIsSkipped()
    {
        var (manifest, _) = V26Package.Minimal();
        manifest = manifest with { Macros = manifest.Macros!.Append(new WorkflowMacroSpec("notes", null, "macros/notes/")).ToList() };

        var roots = WorkflowPackageStore.RootFilesOf(manifest).Order().ToList();

        Assert.Equal(new[] { "macros/closing.md", "prompts/assessment.md", "prompts/extract.md", "prompts/scope.md", "prompts/section.md" }, roots);

        var (at25, _) = V25Package.Minimal();
        Assert.Equal(new[] { "macros/closing.md", "prompts/assessment.md", "prompts/extract.md", "prompts/scope.md", "prompts/section.md" }, WorkflowPackageStore.RootFilesOf(at25).Order());
    }

    [Fact]
    public void TheIndex_IsReadInItsVersionsShape()
    {
        var (_, files25) = V25Package.Minimal();
        var (_, files26) = V26Package.Minimal();

        Assert.Equal(new[] { "hpi.md", "plan.md" }, WorkflowPackageStore.ItemFilesOf(files25["snippets/sections/index.json"], "sections", 25));
        Assert.Equal(new[] { "hpi.md", "plan.md" }, WorkflowPackageStore.ItemFilesOf(files26["snippets/sections/index.json"], "sections", 26));
        // The other shape names nothing — the validator says why.
        Assert.Empty(WorkflowPackageStore.ItemFilesOf(files25["snippets/sections/index.json"], "sections", 26));
        Assert.Empty(WorkflowPackageStore.ItemFilesOf(files26["snippets/sections/index.json"], "sections", 25));
        Assert.Empty(WorkflowPackageStore.ItemFilesOf("not json", "sections", 25));
    }

    [Fact]
    public void A26PackageWithAHeaderFault_IsRefusedAtLoad_NamingTheFile()
    {
        var (manifest, files) = V26Package.Minimal();
        files["snippets/standards.md"] = "Follow the local standards.";

        var ex = Assert.Throws<WorkflowPackageContentException>(() => Load(manifest, files));

        Assert.Contains("snippets/standards.md has no header", ex.Message, StringComparison.Ordinal);
    }

    // ----- the publisher's relabel warning, through the headers ----------------------------

    [Fact]
    public void TheRelabelWarning_At26_ComparesBodies_UnderTheHeaders()
    {
        var (manifest, files) = V26Package.Minimal();
        var parent = Load(manifest, new Dictionary<string, string>(files, StringComparer.Ordinal));

        // The same package: no warning.
        Assert.Empty(WorkflowPackagePublisher.RelabelledWithoutContentChange(parent, manifest, files));

        // The standalone snippet's body changed under its header while the folder did not: the warning.
        var relabelled = new Dictionary<string, string>(files, StringComparer.Ordinal) { ["snippets/standards.md"] = V26Package.Header("Local standards") + "Follow the regional standards." };
        var warning = Assert.Single(WorkflowPackagePublisher.RelabelledWithoutContentChange(parent, manifest, relabelled));
        Assert.StartsWith("Value 'standards' changed but no data collection did", warning, StringComparison.Ordinal);

        // Only the header's label changed: the text the model sees did not — no warning.
        var labelled = new Dictionary<string, string>(files, StringComparer.Ordinal) { ["snippets/standards.md"] = V26Package.Header("Regional standards") + "Follow the local standards." };
        Assert.Empty(WorkflowPackagePublisher.RelabelledWithoutContentChange(parent, manifest, labelled));
    }

    // ----- #1182: a macro folder ---------------------------------------------------------------

    [Fact]
    public void TheMacroFolderTable_NamesTheFolders_AndSkipsTheFlatEntries()
    {
        var (manifest, files) = WorkflowV26MacroFolderTests.WithFolder();

        Assert.Equal(new Dictionary<string, string> { ["section_notes"] = "macros/section_notes/" }, WorkflowPackageStore.MacroFolderTableOf(manifest));
        Assert.Null(WorkflowPackageStore.MacroFolderTableOf(V26Package.Minimal().Manifest));
        Assert.Equal(new[] { "hpi.md", "plan.md" }, WorkflowPackageStore.ItemFilesOf(files["macros/section_notes/index.json"], "section_notes", 26, "Macro"));
    }

    [Fact]
    public void A26PackageWithAMacroFolder_MaterialisesItsFolder_BesideTheFlatMacros()
    {
        var (manifest, files) = WorkflowV26MacroFolderTests.WithFolder();
        // #1224: an optional item says its default — the pair is judged over the header now.
        files["macros/section_notes/plan.md"] = V26Package.Header("Plan notes", instruction: "Note the plan.", @if: "input.kind == \"follow_up\"", optional: true, @default: false) + "Plan noted.";
        var package = Load(manifest, files);

        var folder = Assert.Single(package.MacroFolders!.Collections).Value;
        Assert.Equal(new[] { "hpi", "plan" }, folder.Items.Select(item => item.Id));
        var plan = folder.Items[1];
        Assert.Equal(("Plan notes", "Note the plan.", "input.kind == \"follow_up\"", true, "Plan noted."), (plan.Header!.Label, plan.Header.Instruction, plan.If, plan.Header.Optional, plan.Fields["content"]));
        Assert.Equal("Closing", package.EffectiveManifest.Macros!.First(m => m.Id == "closing").Label);
        // The folder's files are bodies in the texts and raw in the source files.
        Assert.Equal("Plan noted.", package.TextOf("macros/section_notes/plan.md"));
        Assert.StartsWith("{", package.SourceFiles!["macros/section_notes/plan.md"], StringComparison.Ordinal);
        Assert.Null(Load(V26Package.Minimal().Manifest, V26Package.Minimal().Files).MacroFolders);
    }
}
