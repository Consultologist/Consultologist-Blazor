using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>A 24 package carrying every row of the § 14 migration table — the fixture the 24→25 migration is tested on.</summary>
public static class V24Package
{
    public const string ConceptsFile = "schemas/concepts.json";
    public const string PlanShapeFile = "schemas/plan-shape.json";

    public const string PlanShapeSchema = """
        { "type": "object", "description": "The plan as steps.", "additionalProperties": false, "required": ["steps"], "properties": { "steps": { "type": "array", "items": { "type": "string" } } } }
        """;

    public const string UndescribedSchema = """
        { "type": "object", "additionalProperties": false, "required": ["steps"], "properties": { "steps": { "type": "array", "items": { "type": "string" } } } }
        """;

    public static string ManifestJson(int specVersion = 24) => $$"""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.10.1",
          "specVersion": {{specVersion}},
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "notes", "label": "Referral notes", "required": true },
            { "id": "kind", "label": "Kind", "required": true, "type": "enum", "values": ["new", "follow_up"] },
            { "id": "history", "label": "History", "required": false, "type": "array", "items": "text" }
          ],
          "data": { "standards": "data/standards.txt", "sections": "data/sections/" },
          "schemas": { "concepts": "{{ConceptsFile}}" },
          "schemaRefs": { "problems": "concept-list" },
          "customSchemas": { "plan-shape": "{{PlanShapeFile}}" },
          "macros": [
            { "id": "tool_guidance", "label": "Tool guidance", "file": "macros/tool_guidance.md" },
            { "id": "closing", "label": "Closing", "file": "macros/closing.md" },
            { "id": "disclaimer", "label": "Disclaimer", "file": "macros/disclaimer.md" },
            { "id": "handoff", "label": "Handoff", "file": "macros/handoff.md" },
            { "id": "footer", "label": "Footer", "file": "macros/footer.md" }
          ],
          "prompts": [
            { "id": "extract", "file": "prompts/extract.md", "variables": ["notes"] },
            { "id": "problem", "file": "prompts/problem.md", "variables": ["notes", "concepts"] },
            { "id": "scope", "file": "prompts/scope.md", "variables": ["notes"] },
            { "id": "plan", "file": "prompts/plan.md", "variables": ["notes"] },
            { "id": "assessment", "file": "prompts/assessment.md", "variables": ["notes", "problems", "scope_value", "standard", "plan"] },
            { "id": "section", "file": "prompts/section.md", "variables": ["section_name", "section_text"] },
            { "id": "summary", "file": "prompts/summary.md", "variables": ["notes"] },
            { "id": "raw_note", "file": "prompts/raw_note.md", "variables": [], "raw": true }
          ],
          "results": [
            { "id": "consult", "node": "node:assemble", "label": "Consultation note", "when": "kind == new", "check": "node:coverage",
              "macros": [ "closing", { "id": "footer", "after": "node:section-notes", "when": "kind == new" }, { "id": "handoff", "slot": true } ] }
          ],
          "nodes": [
            { "id": "extract-patient-concepts", "label": "Extract concepts", "prompt": "extract", "bindings": { "notes": "input:notes" }, "output": { "schema": "concepts" } },
            { "id": "identify-problem", "label": "Identify problem", "prompt": "problem",
              "bindings": { "notes": "input:notes", "concepts": "node:extract-patient-concepts" }, "output": { "schema": "problems" } },
            { "id": "scope", "label": "Scope", "prompt": "scope", "kind": "classifier", "values": ["in_scope", "out_of_scope"], "bindings": { "notes": "input:notes" } },
            { "id": "plan", "label": "Plan", "prompt": "plan", "bindings": { "notes": "input:notes" }, "output": { "schema": "plan-shape", "failIfEmpty": "No plan." } },
            { "id": "assessment", "label": "Assessment", "prompt": "assessment",
              "bindings": { "notes": "input:notes", "problems": { "from": "node:identify-problem", "as": "concept-context" }, "scope_value": "node:scope", "standard": "data:standards", "plan": "node:plan" },
              "macros": [ "tool_guidance", { "id": "closing", "at": "after", "when": "node:scope == in_scope" } ] },
            { "id": "section-notes", "label": "Section notes", "prompt": "section", "forEach": "data:sections",
              "bindings": { "section_name": "item:name", "section_text": "item:content" },
              "macros": [ { "id": "disclaimer", "forItem": "hpi" } ] },
            { "id": "summary", "label": "Summary", "prompt": "summary", "kind": "template", "bindings": { "notes": "input:notes" } },
            { "id": "raw_node", "label": "Raw", "prompt": "raw_note" },
            { "id": "coverage", "label": "Coverage", "kind": "check", "op": "terms-subset", "of": "node:identify-problem", "in": "node:extract-patient-concepts", "failWith": "Problems the notes do not name." },
            { "id": "assemble", "label": "Assemble", "aggregate": ["node:assessment", "node:section-notes", "node:summary", "node:raw_node"] }
          ]
        }
        """;

    public static Dictionary<string, string> Files() => new(StringComparer.Ordinal)
    {
        ["prompts/extract.md"] = "Extract the concepts from:\n{{ notes }}",
        ["prompts/problem.md"] = "Problems in {{ notes }} given:\n{{ concepts }}",
        ["prompts/scope.md"] = "Is this in scope? {{ notes }}",
        ["prompts/plan.md"] = "Plan steps for {{ notes }}.",
        ["prompts/assessment.md"] = "Assess {{ notes }}.\nProblems:\n{{ problems }}\nScope: {{ scope_value }}\nStandard: {{ standard }}\nPlan: {{ plan }}\nHandoff: [[slot:handoff]]\n{{ if scope_value == \"in_scope\" }}In scope.{{ end }}",
        ["prompts/section.md"] = "{{ section_name }}: {{ section_text }}",
        ["prompts/summary.md"] = "Summary of {{ notes }}",
        ["prompts/raw_note.md"] = "Literal {{ braces }} stay.",
        ["data/standards.txt"] = "Follow the local standards. A literal {{ brace }}.",
        ["data/sections/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md" }, { "id": "plan", "name": "Plan", "file": "plan.md" } ] }""",
        ["data/sections/hpi.md"] = "History of presenting illness.",
        ["data/sections/plan.md"] = "The plan {{ in braces }}.",
        ["macros/tool_guidance.md"] = "Use short terms for {{input:notes}} on {{run:date}}.",
        ["macros/closing.md"] = "Closing: {{profile:name}}, {{classification:scope}}.",
        ["macros/disclaimer.md"] = "Disclaimer for {{item:name}} ({{item:content}}).",
        ["macros/handoff.md"] = "Handoff from {{data:standards}}.",
        ["macros/footer.md"] = "Footer on {{run:date}}.",
        [ConceptsFile] = TestOutputContracts.ConceptListSchema,
        [PlanShapeFile] = PlanShapeSchema
    };

    public static WorkflowPackageManifest Manifest(string json) =>
        JsonSerializer.Deserialize<WorkflowPackageManifest>(json, WorkflowPackageManifestJson.ReadOptions)!;

    /// <summary>The catalogue's canonical concept-list body resolves to its contract; anything else is custom.</summary>
    public static string? ResolveContract(JsonNode schema) =>
        schema.ToJsonString() == JsonNode.Parse(TestOutputContracts.ConceptListSchema)!.ToJsonString() ? WorkflowNodeDefaults.ConceptListSchemaId : null;
}

/// <summary>
/// C6 (#1012, design § 14): the 24→25 migration — mechanical, every row of the table,
/// byte-identical in what the model sees — and its refusals.
/// </summary>
public class WorkflowPackageMigration24To25Tests
{
    private static Migration25Result Migrated() =>
        WorkflowPackageMigration24To25.Migrate(V24Package.ManifestJson(), V24Package.Files(), V24Package.ResolveContract);

    private static JsonObject Root(Migration25Result result) => JsonNode.Parse(result.ManifestJson)!.AsObject();

    private static JsonObject Node(JsonObject root, string id) =>
        root["nodes"]!.AsArray().OfType<JsonObject>().Single(n => n["id"]!.GetValue<string>() == id);

    [Fact]
    public void TheFixture_IsAValid24Package()
    {
        var result = WorkflowPackageValidator.Validate(V24Package.Manifest(V24Package.ManifestJson()), V24Package.Files(), TestOutputContracts.CatalogSchemas);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Migrate_Succeeds_AndTheRetiredKeysAreGone()
    {
        var result = Migrated();
        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        var root = Root(result);

        Assert.Equal(25, root["specVersion"]!.GetValue<int>());
        foreach (var key in new[] { "data", "results", "schemas", "schemaRefs", "customSchemas" })
        {
            Assert.False(root.ContainsKey(key), key);
        }

        foreach (var prompt in root["prompts"]!.AsArray().OfType<JsonObject>())
        {
            Assert.False(prompt.ContainsKey("variables"), prompt["id"]!.GetValue<string>());
            Assert.False(prompt.ContainsKey("raw"), prompt["id"]!.GetValue<string>());
        }

        foreach (var node in root["nodes"]!.AsArray().OfType<JsonObject>())
        {
            foreach (var key in new[] { "bindings", "kind", "values", "op", "of", "in", "failWith", "output" })
            {
                Assert.False(node.ContainsKey(key), $"{node["id"]} {key}");
            }
        }
    }

    [Fact]
    public void HyphenatedIds_AreRenamed_InEveryReference()
    {
        var root = Root(Migrated());

        Assert.NotNull(Node(root, "extract_patient_concepts"));
        Assert.NotNull(Node(root, "identify_problem"));
        Assert.NotNull(Node(root, "section_notes"));
        Assert.Equal(new[] { "node:assessment", "node:section_notes", "node:summary", "node:raw_node" }, Node(root, "assemble")["aggregate"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Equal("node.identify_problem", Node(root, "coverage")["with"]!["of"]!.GetValue<string>());
        Assert.Equal("node.extract_patient_concepts", Node(root, "coverage")["with"]!["in"]!.GetValue<string>());
    }

    [Fact]
    public void Templates_ReadObjects_WhereVariablesWereBound()
    {
        var result = Migrated();

        Assert.Equal("Extract the concepts from:\n{{ input.notes }}", result.Files["prompts/extract.md"]);
        // A concept node bound without a renderer rendered as bullets at 24 — the pipe keeps the bytes.
        Assert.Equal("Problems in {{ input.notes }} given:\n{{ node.extract_patient_concepts.text | concept_bullets }}", result.Files["prompts/problem.md"]);
        Assert.EndsWith("{{ item.name }}: {{ item.text }}", result.Files["prompts/section.md"]);

        var assessment = result.Files["prompts/assessment.md"];
        Assert.Contains("Problems:\n{{ node.identify_problem.text | concept_context }}", assessment);
        Assert.Contains("Scope: {{ node.scope.value }}", assessment);
        Assert.Contains("Standard: {{ snippet.standards.text }}", assessment);
        Assert.Contains("Plan: {{ node.plan.text }}", assessment);
        // Inside a larger expression the read keeps its meaning without a pipe to parenthesise.
        Assert.Contains("{{ if node.scope.value == \"in_scope\" }}In scope.{{ end }}", assessment);
        // The slot marker is the slot read.
        Assert.Contains("Handoff: {{ macro.handoff.prompt }}", assessment);
        Assert.DoesNotContain("[[slot:", assessment);
    }

    [Fact]
    public void ARawPrompt_IsEscaped()
    {
        var result = Migrated();
        Assert.Equal("Literal {{ \"{{\" }} braces }} stay.", result.Files["prompts/raw_note.md"]);
    }

    [Fact]
    public void Nodes_NameTheirFormats()
    {
        var root = Root(Migrated());

        Assert.Equal("concept-list", Node(root, "extract_patient_concepts")["format"]!.GetValue<string>());
        Assert.Equal("fail", Node(root, "extract_patient_concepts")["onInvalid"]!.GetValue<string>());
        Assert.Equal("concept-list", Node(root, "identify_problem")["format"]!.GetValue<string>());

        var scope = Node(root, "scope");
        Assert.Equal("classification", scope["format"]!.GetValue<string>());
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, scope["with"]!["values"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal("fail", scope["onInvalid"]!.GetValue<string>());

        var plan = Node(root, "plan");
        Assert.Equal("plan_shape", plan["format"]!.GetValue<string>());
        Assert.Equal("fail", plan["onInvalid"]!.GetValue<string>());
        Assert.Equal("No plan.", plan["failIfEmpty"]!.GetValue<string>());

        Assert.Equal("prose", Node(root, "assessment")["format"]!.GetValue<string>());
        Assert.Equal("prose", Node(root, "raw_node")["format"]!.GetValue<string>());
        Assert.Equal("rendered", Node(root, "summary")["format"]!.GetValue<string>());

        var coverage = Node(root, "coverage");
        Assert.Equal("terms-subset", coverage["format"]!.GetValue<string>());
        Assert.Equal("Problems the notes do not name.", coverage["with"]!["failWith"]!.GetValue<string>());
        Assert.Equal("fail", coverage["onInvalid"]!.GetValue<string>());
        Assert.False(coverage.ContainsKey("prompt"));

        Assert.Equal("snippet:sections", Node(root, "section_notes")["forEach"]!.GetValue<string>());
    }

    [Fact]
    public void ACustomSchema_IsAPackageFormat_WithABlankDescription()
    {
        var result = Migrated();
        var root = Root(result);

        var formats = root["formats"]!.AsArray().OfType<JsonObject>().ToList();
        var planShape = Assert.Single(formats);
        Assert.Equal("plan_shape", planShape["id"]!.GetValue<string>());
        Assert.Equal("formats/plan_shape.json", planShape["file"]!.GetValue<string>());

        var spec = JsonNode.Parse(result.Files["formats/plan_shape.json"])!.AsObject();
        Assert.Equal("object", spec["type"]!.GetValue<string>());
        Assert.Equal("custom", spec["producer"]!.GetValue<string>());
        // The schema's own description is the format's.
        Assert.Equal("The plan as steps.", spec["description"]!.GetValue<string>());
        Assert.NotNull(spec["schema"]!["properties"]!["steps"]);
        Assert.False(result.Files.ContainsKey(V24Package.PlanShapeFile));
        Assert.False(result.Files.ContainsKey(V24Package.ConceptsFile));
    }

    [Fact]
    public void Deliverables_ReplaceResults_AndMacrosMoveOrSayAt()
    {
        var root = Root(Migrated());

        var deliverable = Assert.Single(root["deliverables"]!.AsArray().OfType<JsonObject>());
        Assert.Equal("node:assemble", deliverable["node"]!.GetValue<string>());
        Assert.Equal("node.coverage", deliverable["check"]!.GetValue<string>());
        Assert.Equal("kind == new", deliverable["when"]!.GetValue<string>());
        var appended = Assert.Single(deliverable["macros"]!.AsArray().OfType<JsonObject>());
        Assert.Equal("closing", appended["id"]!.GetValue<string>());
        Assert.Equal("append", appended["at"]!.GetValue<string>());

        // The node-anchored macro is a node instance now; the prompt-side one left the list.
        var sectionMacros = Node(root, "section_notes")["macros"]!.AsArray().OfType<JsonObject>().ToList();
        var instance = Assert.Single(sectionMacros);
        Assert.Equal("footer", instance["id"]!.GetValue<string>());
        Assert.Equal("after", instance["at"]!.GetValue<string>());
        Assert.Equal("kind == new", instance["when"]!.GetValue<string>());
        Assert.False(Node(root, "assessment").ContainsKey("macros"));
    }

    [Fact]
    public void PromptSideMacros_BecomeInlineReads_UnderTheirGates_AndANeverPlacedOneBecomesASnippet()
    {
        var result = Migrated();
        var root = Root(result);

        var assessment = result.Files["prompts/assessment.md"];
        Assert.StartsWith("{{ snippet.tool_guidance.text }}\n\nAssess {{ input.notes }}.", assessment);
        Assert.EndsWith("\n\n{{ if node.scope.value == \"in_scope\" }}{{ macro.closing.text }}{{ end }}", assessment);

        var section = result.Files["prompts/section.md"];
        Assert.Equal("{{ if item.id == \"hpi\" }}{{ snippet.disclaimer.text }}{{ end }}\n\n{{ item.name }}: {{ item.text }}", section);

        // tool_guidance and disclaimer: read prompt-side only → snippets, their placeholders now objects
        // (disclaimer's item reads run under section_notes' fan — the reading node's business).
        foreach (var id in new[] { "tool_guidance", "disclaimer" })
        {
            Assert.DoesNotContain(root["macros"]!.AsArray().OfType<JsonObject>(), m => m["id"]!.GetValue<string>() == id);
            Assert.Equal($"snippets/{id}.md", root["snippets"]![id]!.GetValue<string>());
            Assert.False(result.Files.ContainsKey($"macros/{id}.md"));
        }

        Assert.Equal("Use short terms for {{ input.notes }} on {{ run.date }}.", result.Files["snippets/tool_guidance.md"]);
        Assert.Equal("Disclaimer for {{ item.name }} ({{ item.text }}).", result.Files["snippets/disclaimer.md"]);

        // The remaining macros read objects where placeholders stood.
        Assert.Equal("Closing: {{ profile.name }}, {{ node.scope.value }}.", result.Files["macros/closing.md"]);
        Assert.Equal("Handoff from {{ snippet.standards.text }}.", result.Files["macros/handoff.md"]);
        Assert.Equal("Footer on {{ run.date }}.", result.Files["macros/footer.md"]);
    }

    [Fact]
    public void Data_BecomesSnippets_FilesMoved_AndLiteralBracesEscaped()
    {
        var result = Migrated();
        var root = Root(result);

        Assert.Equal("snippets/standards.md", root["snippets"]!["standards"]!.GetValue<string>());
        Assert.Equal("snippets/sections/", root["snippets"]!["sections"]!.GetValue<string>());
        Assert.Equal("Follow the local standards. A literal {{ \"{{\" }} brace }}.", result.Files["snippets/standards.md"]);
        Assert.Equal("The plan {{ \"{{\" }} in braces }}.", result.Files["snippets/sections/plan.md"]);
        Assert.Equal("History of presenting illness.", result.Files["snippets/sections/hpi.md"]);
        Assert.Contains("\"hpi\"", result.Files["snippets/sections/index.json"]);
        Assert.DoesNotContain(result.Files.Keys, f => f.StartsWith("data/", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMigratedPackage_ValidatesAt25_ExceptTheExplicitInitTheNoticesName()
    {
        var result = Migrated();
        var manifest = V24Package.Manifest(result.ManifestJson);

        var errors = V25Package.Errors((manifest, result.Files));

        var expected = new[]
        {
            "Macro 'closing' must declare an instruction at specVersion 25; placed text tells the model when to emit its marker.",
            "Macro 'footer' must declare an instruction at specVersion 25; placed text tells the model when to emit its marker.",
            "Macro 'handoff' must declare an instruction at specVersion 25; placed text tells the model when to emit its marker."
        };
        Assert.True(expected.Order(StringComparer.Ordinal).SequenceEqual(errors.Order(StringComparer.Ordinal)), string.Join("\n", errors));

        // Each of those is named by a notice.
        foreach (var macro in new[] { "closing", "footer", "handoff" })
        {
            Assert.Contains(result.Notices, n => n == $"Macro '{macro}' needs an instruction before publishing (specVersion 25): what the model is told where its slot is read.");
        }
    }

    [Fact]
    public void AnUndescribedCustomSchema_LeavesTheDescriptionBlank_AndTheNoticeSaysSo()
    {
        var result = MigrateReshaped(_ => { }, files => files[V24Package.PlanShapeFile] = V24Package.UndescribedSchema);
        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));

        var spec = JsonNode.Parse(result.Files["formats/plan_shape.json"])!.AsObject();
        Assert.Equal("", spec["description"]!.GetValue<string>());
        Assert.Contains(result.Notices, n => n.Contains("'plan_shape'") && n.Contains("write its description before publishing"));
        // Explicit init: the registry refuses the blank description until it is written.
        Assert.Contains("Format 'plan_shape' description must not be blank.", V25Package.Errors((V24Package.Manifest(result.ManifestJson), result.Files)));
    }

    [Fact]
    public void WrittenInstructionsAndDescription_MakeThePackagePublishable()
    {
        var result = Migrated();
        var root = Root(result);
        foreach (var macro in root["macros"]!.AsArray().OfType<JsonObject>())
        {
            macro["instruction"] = "Say when it applies.";
        }

        var files = new Dictionary<string, string>(result.Files, StringComparer.Ordinal);
        var spec = JsonNode.Parse(files["formats/plan_shape.json"])!.AsObject();
        spec["description"] = "The plan as steps.";
        files["formats/plan_shape.json"] = spec.ToJsonString();

        var errors = V25Package.Errors((V24Package.Manifest(root.ToJsonString()), files));
        Assert.Empty(errors);
    }

    [Fact]
    public void TheNotices_NameEveryRewrite()
    {
        var notices = Migrated().Notices;

        Assert.Contains(notices, n => n.StartsWith("Node 'identify-problem' renamed 'identify_problem'", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.StartsWith("Schema 'plan-shape' renamed 'plan_shape'", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.Contains("prompts now read objects where variables were bound"));
        Assert.Contains(notices, n => n.StartsWith("Prompt 'raw_note' was raw", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.StartsWith("Nodes now name their format:", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.StartsWith("Schema 'plan_shape' is now the package format 'plan_shape'", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.StartsWith("Macro 'footer' moved from deliverable 'consult' onto node 'section_notes' (after)", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.Contains("'disclaimer'") && n.Contains("now the snippet"));
        Assert.Contains(notices, n => n.StartsWith("Macro 'handoff' was a slot", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.StartsWith("results is now deliverables", StringComparison.Ordinal));
        Assert.Contains(notices, n => n.Contains("tool_guidance") && n.Contains("now the snippet"));
        Assert.Contains(notices, n => n.StartsWith("data is now snippets", StringComparison.Ordinal) && n.Contains("escaped in 2 files"));
    }

    // ----- refusals: nothing applied -----

    private static Migration25Result MigrateReshaped(Action<JsonObject> reshape, Action<Dictionary<string, string>>? files = null)
    {
        var root = JsonNode.Parse(V24Package.ManifestJson())!.AsObject();
        reshape(root);
        var bundle = V24Package.Files();
        files?.Invoke(bundle);
        return WorkflowPackageMigration24To25.Migrate(root.ToJsonString(), bundle, V24Package.ResolveContract);
    }

    [Fact]
    public void APromptBoundDifferently_ByTwoNodes_IsRefused()
    {
        var result = MigrateReshaped(root =>
        {
            // raw_node re-pointed at the extract prompt with notes bound elsewhere.
            var node = root["nodes"]!.AsArray().OfType<JsonObject>().Single(n => n["id"]!.GetValue<string>() == "raw_node");
            node["prompt"] = "extract";
            node["bindings"] = new JsonObject { ["notes"] = "input:kind" };
        });

        Assert.False(result.Succeeded);
        // Ids are renamed before the templates are read, so the sentence names the 25 ids; nothing is applied.
        Assert.Contains("Prompt 'extract' binds 'notes' differently on nodes 'extract_patient_concepts' and 'raw_node'; a per-node alias map is deferred — split the prompt first, then upgrade.", result.Refusals);
        Assert.Equal(24, V24Package.Manifest(result.ManifestJson).SpecVersion);
    }

    [Fact]
    public void AnIdRenameThatCollides_IsRefused()
    {
        var result = MigrateReshaped(root =>
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "identify_problem", ["label"] = "Twin", ["prompt"] = "summary", ["bindings"] = new JsonObject { ["notes"] = "input:notes" } }));

        Assert.Contains(result.Refusals, r => r.StartsWith("Node 'identify-problem' renamed 'identify_problem' collides", StringComparison.Ordinal));
    }

    [Fact]
    public void ARemainingPrelude_IsRefused()
    {
        var result = MigrateReshaped(root => root["preludes"] = new JsonObject { ["guidance"] = "prompts/_guidance.md" });

        Assert.Contains("Prelude 'guidance' remains; migrate preludes to node macros first (specVersion 23).", result.Refusals);
    }

    [Fact]
    public void ATemplateThatDoesNotParse_IsRefused_NamingTheLine()
    {
        var result = MigrateReshaped(_ => { }, files => files["prompts/extract.md"] = "Extract\n{{ notes ");

        Assert.Contains(result.Refusals, r => r.StartsWith("Prompt 'extract' does not parse at line", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(22, "Migrate to specVersion 24 first (the package is at 22).")]
    [InlineData(25, "The package is already at specVersion 25.")]
    public void TheWrongVersion_IsRefused(int specVersion, string sentence)
    {
        var result = WorkflowPackageMigration24To25.Migrate(V24Package.ManifestJson(specVersion), V24Package.Files());

        Assert.Equal(new[] { sentence }, result.Refusals);
    }

    // ----- byte identity: the two renderers agree on the binding rows -----

    [Fact]
    public void AMigratedPrompt_RendersTheBytesTheBoundOneDid()
    {
        var result = Migrated();
        const string bullets = "- Chest pain (finding) - 29857009";
        const string notes = "The notes.";

        var at24 = PromptTemplateRenderer.Render(
            new WorkflowPromptTemplate("problem", V24Package.Files()["prompts/problem.md"], new[] { "notes", "concepts" }, null),
            new Dictionary<string, string> { ["notes"] = notes, ["concepts"] = bullets });

        var scope = new TemplateRenderScope(
            new Dictionary<string, string> { ["notes"] = notes, ["kind"] = "new", ["history"] = "[]" },
            null,
            null,
            new Dictionary<string, TemplateNodeOutput> { ["extract_patient_concepts"] = new("{\"concepts\":[]}", "object") },
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<TemplateFolderItem>>(),
            null,
            new Dictionary<string, TemplateMacroSource>(),
            new TemplateRunFacts("2026-10-15", "job", "pkg", "host"),
            new TemplateProfileFacts("Dr", ""));
        var at25 = TemplateRenderer.Render(
            "Prompt 'problem'",
            result.Files["prompts/problem.md"],
            scope,
            new Dictionary<string, Func<TemplateNodeOutput, string>> { [TemplatePipes.ConceptBullets] = _ => bullets, [TemplatePipes.ConceptContext] = _ => "ctx" });

        Assert.Equal(at24, at25);
    }
}
