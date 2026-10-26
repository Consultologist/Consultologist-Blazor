using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1152 (package-format-v26-design.md § 13): the 25 → 26 rewriter — mechanical,
/// byte-identical in what the model sees, one notice per rewrite, refusals that
/// apply nothing; the 25 minimal migrates into a valid 26 package; the seven
/// public packages are the dry run.
/// </summary>
public class WorkflowPackageMigration25To26Tests
{
    private static string Json(WorkflowPackageManifest manifest) => JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions);

    private static Migration26Result Migrated(Action<JsonObject>? reshape = null, Action<Dictionary<string, string>>? files = null)
    {
        var (manifest, manifestFiles) = V25Package.Minimal();
        var root = JsonNode.Parse(Json(manifest))!.AsObject();
        reshape?.Invoke(root);
        files?.Invoke(manifestFiles);
        return WorkflowPackageMigration25To26.Migrate(root.ToJsonString(), manifestFiles);
    }

    private static WorkflowPackageManifest Manifest(string json) => WorkflowPackageManifestJson.Read(json, "migrated", V26Package.Accepting26.ToList());

    private static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Bundle(Migration26Result result) => (Manifest(result.ManifestJson), result.Files);

    private static int VersionOf(string manifestJson) => JsonNode.Parse(manifestJson)!["specVersion"]!.GetValue<int>();

    // ----- the minimal, migrated ------------------------------------------------------

    [Fact]
    public void TheMinimal_Migrates_AndIsValidAt26()
    {
        var result = Migrated();

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        var errors = V26Package.Errors(Bundle(result));
        Assert.Empty(errors);
        Assert.Equal(26, Manifest(result.ManifestJson).SpecVersion);
    }

    [Fact]
    public void TheRetiredKeys_AreGone()
    {
        var result = Migrated();
        var root = JsonNode.Parse(result.ManifestJson)!.AsObject();

        Assert.Null(root["prompts"]);
        foreach (var node in root["nodes"]!.AsArray().OfType<JsonObject>())
        {
            Assert.Null(node["forEach"]);
            Assert.Null(node["aggregate"]);
            Assert.Null(node["when"]);
        }

        var deliverable = root["deliverables"]!.AsArray()[0]!.AsObject();
        Assert.Null(deliverable["node"]);
        Assert.Null(deliverable["when"]);
        Assert.Equal(JsonValueKind.Object, root["macros"]!.GetValueKind());
        Assert.Equal("macros/closing.md", root["macros"]!["closing"]!.GetValue<string>());
        Assert.DoesNotContain("\"fields\"", result.Files["snippets/sections/index.json"]);
        Assert.DoesNotContain("\"name\"", result.Files["snippets/sections/index.json"]);
        Assert.DoesNotContain("\"when\"", result.Files["snippets/sections/index.json"]);
    }

    [Fact]
    public void TheFan_IsALoop_AndTheAggregatorIsTheDeliverablesSources()
    {
        var manifest = Manifest(Migrated().ManifestJson);

        var loop = manifest.Nodes!.Single(n => n.Id == "sections");
        Assert.Equal(("item in snippet.sections", "node.section_notes"), (loop.For, loop.Ret));
        Assert.Equal(new[] { "section_notes" }, loop.Nodes);
        Assert.Null(manifest.Nodes!.FirstOrDefault(n => n.Id == "all"));
        Assert.Equal(new[] { "node.assessment", "node.sections" }, manifest.Deliverables![0].Sources);
        Assert.Null(manifest.Deliverables[0].Node);
        // The loop sits where its first body node sat.
        Assert.True(manifest.Nodes.FindIndex(n => n.Id == "sections") < manifest.Nodes.FindIndex(n => n.Id == "section_notes"));
    }

    [Fact]
    public void TheTexts_CarryTheirHeaders_AndTheirReads()
    {
        var result = Migrated();

        Assert.StartsWith("{\n  \"label\": \"Standards\"\n}\nFollow the local standards.", result.Files["snippets/standards.md"], StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"label\": \"HPI\"\n}\nHistory: {{ input.notes }}", result.Files["snippets/sections/hpi.md"], StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"label\": \"Plan\",\n  \"if\": \"input.kind == \\\"new\\\"\"\n}\nPlan.", result.Files["snippets/sections/plan.md"], StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"label\": \"Closing\",\n  \"instruction\": \"Add a closing line if the plan changed.\"\n}\nClosing: {{ profile.name }}.", result.Files["macros/closing.md"], StringComparison.Ordinal);
        Assert.Equal("{{ item.label }}: {{ item.content }}", result.Files["prompts/section.md"]);
        Assert.Contains("\"id\": \"hpi\"", result.Files["snippets/sections/index.json"]);
        Assert.Equal("prompts/section.md", Manifest(result.ManifestJson).Nodes!.Single(n => n.Id == "section_notes").Prompt);
    }

    [Fact]
    public void TheNotices_NameEveryRewrite()
    {
        var notices = Migrated().Notices;

        Assert.Contains(notices, n => n.StartsWith("Loop 'sections' over sections returns 'section_notes' with body [section_notes].", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.StartsWith("Deliverable 'consult' composes sources [node.assessment, node.sections] (aggregator 'all' dissolved).", StringComparison.Ordinal));
        Assert.Contains("4 nodes now name the prompt file; prompts[] removed.", notices);
        Assert.Contains("Snippet 'standards' labelled 'Standards' — invented from the id; review it.", notices);
        Assert.Contains("Snippet folder 'sections' index is the ordered list; each item file opens with its header (label, if).", notices);
        Assert.Contains("macros is the map of id to file; each macro file opens with its header (label, instruction, optional, default).", notices);
        Assert.Contains("1 read of .name now reads .label.", notices);
    }

    // ----- the rows the minimal does not carry -----------------------------------------

    [Fact]
    public void AnAggregatorRead_BecomesTheDeliverables_OrTheLoops()
    {
        // Named by a deliverable: deliverable.<id>.content.
        var named = Migrated(files: f => f["prompts/scope.md"] = "Is this in scope? {{ node.all.content }}");
        Assert.True(named.Succeeded, string.Join(" | ", named.Refusals));
        Assert.Equal("Is this in scope? {{ deliverable.consult.content }}", named.Files["prompts/scope.md"]);
        Assert.Contains("node.all.content now reads deliverable.consult.content in prompts/scope.md.", named.Notices);

        // Named by none, one fanned source: the loop.
        var unnamed = Migrated(
            root =>
            {
                root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "agg", ["label"] = "Agg", ["aggregate"] = new JsonArray("node.section_notes") });
            },
            f => f["prompts/scope.md"] = "Is this in scope? {{ node.agg.content }}");
        Assert.True(unnamed.Succeeded, string.Join(" | ", unnamed.Refusals));
        Assert.Equal("Is this in scope? {{ node.sections.content }}", unnamed.Files["prompts/scope.md"]);

        // Named by none, two sources: refused, nothing applied.
        var refused = Migrated(
            root => root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "agg", ["label"] = "Agg", ["aggregate"] = new JsonArray("node.assessment", "node.section_notes") }),
            f => f["prompts/scope.md"] = "Is this in scope? {{ node.agg.content }}");
        Assert.Equal(new[] { "'prompts/scope.md' reads node.agg.content, which no deliverable names and which composes 2 sources; read a deliverable or one loop, then upgrade." }, refused.Refusals);
        Assert.Equal(25, VersionOf(refused.ManifestJson));
    }

    [Fact]
    public void TwoAggregatedChainsOnOneCollection_AreTwoLoops_NamedAfterTheirReturnedNodes()
    {
        var result = Migrated(root =>
        {
            var nodes = root["nodes"]!.AsArray();
            nodes.Add(new JsonObject { ["id"] = "section_terms", ["label"] = "Section terms", ["prompt"] = "section_terms", ["format"] = "prose", ["forEach"] = "snippet.sections" });
            nodes.Add(new JsonObject { ["id"] = "terms", ["label"] = "Terms", ["aggregate"] = new JsonArray("node.section_terms") });
            root["prompts"]!.AsArray().Add(new JsonObject { ["id"] = "section_terms", ["file"] = "prompts/section_terms.md", ["variables"] = new JsonArray() });
            root["deliverables"]!.AsArray().Add(new JsonObject { ["id"] = "terms_note", ["node"] = "node.terms", ["label"] = "Terms note" });
        }, f => f["prompts/section_terms.md"] = "Terms of {{ item.content }}.");

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        var manifest = Manifest(result.ManifestJson);
        Assert.Equal("node.section_notes", manifest.Nodes!.Single(n => n.Id == "sections_section_notes").Ret);
        Assert.Equal(new[] { "section_notes" }, manifest.Nodes!.Single(n => n.Id == "sections_section_notes").Nodes);
        Assert.Equal("node.section_terms", manifest.Nodes!.Single(n => n.Id == "sections_section_terms").Ret);
        Assert.Equal(new[] { "node.assessment", "node.sections_section_notes" }, manifest.Deliverables!.Single(d => d.Id == "consult").Sources);
        Assert.Equal(new[] { "node.sections_section_terms" }, manifest.Deliverables!.Single(d => d.Id == "terms_note").Sources);
        Assert.Contains(result.Notices, n => n.Contains("named after its returned node", StringComparison.Ordinal));
        Assert.Empty(V26Package.Errors(Bundle(result)));
    }

    [Fact]
    public void TwoAggregatorsOnOneChain_ShareTheLoop()
    {
        var result = Migrated(root =>
        {
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "agg", ["label"] = "Agg", ["aggregate"] = new JsonArray("node.section_notes") });
            root["deliverables"]!.AsArray().Add(new JsonObject { ["id"] = "digest", ["node"] = "node.agg", ["label"] = "Digest" });
        });

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        var manifest = Manifest(result.ManifestJson);
        Assert.Single(manifest.Nodes!, n => n.For is not null);
        Assert.Equal(new[] { "node.sections" }, manifest.Deliverables!.Single(d => d.Id == "digest").Sources);
        Assert.Empty(V26Package.Errors(Bundle(result)));
    }

    [Fact]
    public void ANodeWhen_WrapsInAnIfNode()
    {
        var result = Migrated(
            root =>
            {
                var scope = root["nodes"]!.AsArray().OfType<JsonObject>().First(n => n["id"]!.GetValue<string>() == "extract");
                scope["when"] = "input.kind == \"new\"";
            },
            f => f["prompts/scope.md"] = "Is this in scope? {{ node.extract.content }}");

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        var manifest = Manifest(result.ManifestJson);
        var wrapper = manifest.Nodes!.Single(n => n.Id == "extract_if");
        Assert.Equal(("input.kind == \"new\"", "node.extract"), (wrapper.If, wrapper.Then!.Ret));
        Assert.Equal(new[] { "extract" }, wrapper.Then.Nodes);
        Assert.Null(manifest.Nodes!.Single(n => n.Id == "extract").When);
        Assert.Equal("Is this in scope? {{ node.extract_if.content }}", result.Files["prompts/scope.md"]);
    }

    [Fact]
    public void Gates_AreKeyedIf()
    {
        var result = Migrated(root =>
        {
            var assessment = root["nodes"]!.AsArray().OfType<JsonObject>().First(n => n["id"]!.GetValue<string>() == "assessment");
            assessment["macros"] = new JsonArray(new JsonObject { ["id"] = "closing", ["at"] = "after", ["when"] = "input.kind == \"new\"" });
            var sections = root["nodes"]!.AsArray().OfType<JsonObject>().First(n => n["id"]!.GetValue<string>() == "section_notes");
            sections["macros"] = new JsonArray(new JsonObject { ["id"] = "closing", ["at"] = "after", ["forItem"] = "hpi", ["when"] = "input.kind == \"new\"" });
            var deliverable = root["deliverables"]!.AsArray()[0]!.AsObject();
            deliverable["when"] = "input.kind == \"new\"";
            deliverable["macros"] = new JsonArray(new JsonObject { ["id"] = "closing", ["at"] = "append", ["when"] = "input.kind == \"follow_up\"" });
        });

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        var manifest = Manifest(result.ManifestJson);
        Assert.Equal("input.kind == \"new\"", manifest.Nodes!.Single(n => n.Id == "assessment").Macros![0].If);
        Assert.Equal("(input.kind == \"new\") && item.id == \"hpi\"", manifest.Nodes!.Single(n => n.Id == "section_notes").Macros![0].If);
        Assert.Null(manifest.Nodes!.Single(n => n.Id == "section_notes").Macros![0].ForItem);
        Assert.Equal("input.kind == \"new\"", manifest.Deliverables![0].If);
        Assert.Equal("input.kind == \"follow_up\"", manifest.Deliverables![0].Macros![0].If);
        Assert.Contains("5 gates now keyed if (when, and forItem as item.id == …).", result.Notices);
    }

    [Fact]
    public void APipe_TakesTheNode_AndRendersTheSameBytes()
    {
        var result = Migrated(files: f => f["prompts/scope.md"] = "Concepts: {{ node.extract.content | concept_bullets }} and {{ node.extract.content | concept_context }}.");

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        Assert.Equal("Concepts: {{ node.extract | concept_bullets }} and {{ node.extract | concept_context }}.", result.Files["prompts/scope.md"]);
        Assert.Contains("2 renderer pipes now take the node (node.<id> | pipe).", result.Notices);

        var scope = new TemplateRenderScope(
            new Dictionary<string, string>(), null, null,
            new Dictionary<string, TemplateNodeOutput> { ["extract"] = new(ConceptSamples.ConceptListJson, "object", ConceptSource: "extract") },
            new Dictionary<string, string>(), new Dictionary<string, IReadOnlyList<TemplateFolderItem>>(), null, new Dictionary<string, TemplateMacroSource>(),
            new TemplateRunFacts("2026-10-26", "abcdef12", "test/general@v2026.10.8", "api.example.test"), new TemplateProfileFacts("Dr Example", "Dr Example, FRCPC"));
        var at25 = TemplateRenderer.Render("Prompt 'scope'", "Concepts: {{ node.extract.content | concept_bullets }} and {{ node.extract.content | concept_context }}.", scope, TemplatePipes.Engine);
        var at26 = TemplateRenderer.Render("Prompt 'scope'", result.Files["prompts/scope.md"], scope, TemplatePipes.Engine);
        Assert.Equal(at25, at26);
    }

    // ----- refusals apply nothing --------------------------------------------------------

    [Theory]
    [InlineData(24, "Migrate to specVersion 25 first (the package is at 24; scripts/migrate-workflow-package.cs).")]
    [InlineData(26, "The package is already at specVersion 26.")]
    public void TheWrongVersion_IsRefused(int version, string sentence)
    {
        var result = Migrated(root => root["specVersion"] = version);

        Assert.Equal(new[] { sentence }, result.Refusals);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void AnOrphanFan_ATextWithAHeader_APromptBoundTwice_AreRefused_AndNothingApplies()
    {
        var orphan = Migrated(root => root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "aside", ["label"] = "Aside", ["prompt"] = "section", ["format"] = "prose", ["forEach"] = "snippet.sections" }));
        Assert.Contains("Node 'aside' fans 'sections' but no aggregator sources it or a node that reads it; aggregate it or remove it, then upgrade.", orphan.Refusals);
        Assert.Equal(25, VersionOf(orphan.ManifestJson));
        Assert.Equal("Follow the local standards.", orphan.Files["snippets/standards.md"]);

        var header = Migrated(files: f => f["snippets/standards.md"] = "{\n  \"label\": \"X\"\n}\nFollow.");
        Assert.Equal(new[] { "'snippets/standards.md' opens with a header already; a 25 text has none — remove it, then upgrade." }, header.Refusals);

        var twice = Migrated(root => root["nodes"]!.AsArray().OfType<JsonObject>().First(n => n["id"]!.GetValue<string>() == "scope")["prompt"] = "extract");
        Assert.Contains("Prompt 'extract' is bound by nodes 'extract', 'scope'; at 26 a node names its own file — copy the file per node, then upgrade.", twice.Refusals);
        Assert.Equal(25, VersionOf(twice.ManifestJson));
    }

    // ----- the public packages, when checked out beside the engine ------------------------

    [Fact]
    public void ThePublicPackages_MigrateWithoutARefusal_AndValidateAt26()
    {
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "Consultologist", "consultologist-workflows", "packages");
        if (!Directory.Exists(packages))
        {
            return;
        }

        var expectedLoops = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["general"] = new[] { "standards" },
            ["test/general"] = new[] { "standards" },
            ["example-two-documents"] = new[] { "standards" },
            ["example-v12-constructs"] = new[] { "standards_section_instructions", "standards_letter_body" },
            ["example-classifier-scope"] = new[] { "plan_sections", "letter_decline_letter", "letter_information_request" },
            ["example-conditional-documents"] = new[] { "sections_consult_note_section", "sections_progress_note_section" },
            ["example-structured-intake"] = new[] { "sections_note_section", "sections_senior_review_section", "sections_long_stay_section", "prior_notes_summarise_note", "prior_notes_index_note" },
        };

        foreach (var (name, loops) in expectedLoops)
        {
            var dir = Path.Combine(packages, name.Replace('/', Path.DirectorySeparatorChar));
            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith("manifest.json", StringComparison.Ordinal) && !p.EndsWith("dag.mmd", StringComparison.Ordinal) && !p.EndsWith(".stamp.json", StringComparison.Ordinal))
                .ToDictionary(p => Path.GetRelativePath(dir, p).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
            var result = WorkflowPackageMigration25To26.Migrate(File.ReadAllText(Path.Combine(dir, "manifest.json")), files);

            Assert.True(result.Succeeded, $"{name}: {string.Join(" | ", result.Refusals)}");
            var manifest = Manifest(result.ManifestJson);
            Assert.Equal(loops.Order(StringComparer.Ordinal), manifest.Nodes!.Where(n => n.For is not null).Select(n => n.Id).Order(StringComparer.Ordinal));
            var errors = V26Package.Errors(Bundle(result));
            Assert.True(errors.Count == 0, $"{name}: {string.Join(" | ", errors)}");
        }
    }
}
