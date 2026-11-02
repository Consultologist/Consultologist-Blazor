using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1283 (package-format-v28-design.md § 10): the specVersion 28 conformance
/// vectors — a file's name is its id. The valid baseline is the 27 minimal
/// spelled at 28 (<see cref="V28Package.Minimal"/>, the migration's output);
/// each invalid case breaks one § 2, § 3 or § 6a sentence against it; the
/// recorded outcome is whatever the validator says. Held back by the export
/// while 28 is above the accepted ceiling; the bump (#1286) lifts them into
/// `conformance/v28/`. The below-28 gate is a 27 case, filed under v27/.
/// </summary>
public partial class ConformanceFixtureExport
{
    private static void AddV28Cases(List<Case> cases)
    {
        void Add(string id, string description, (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
            => cases.Add(new Case(id, 28, description, bundle.Manifest, bundle.Files));

        var m = V28Package.Minimal();
        (WorkflowPackageManifest, Dictionary<string, string>) Index(string json) =>
            V28Package.WithJson(_ => { }, files => files["snippets/sections/index.json"] = json);

        // ======================================================= valid
        Add("v28-minimal", "The 27 minimal at specVersion 28: no prompt keys (each prompt is prompts/<node id>.md), macros and snippets as lists of ids, the folder index { items } (§ 1–§ 3).", m);
        Add("v28-folder-label", "A snippet folder whose index carries a display label beside its items (§ 3).", Index("""{ "label": "Sections", "items": ["hpi", "plan"] }"""));
        Add("v28-macro-folder", "A macro folder listed as notes/ — its index the object, its items <id>.md beside it (§ 2, § 3).",
            V28Package.WithJson(
                root => root["macros"] = new JsonArray("closing", "notes/"),
                files =>
                {
                    files["macros/notes/index.json"] = """{ "items": ["red_flags"] }""";
                    files["macros/notes/red_flags.md"] = V26Package.Header("Red flags", instruction: "Add red flags if any.") + "Red flags: none.";
                    files["prompts/assessment.md"] += "\n{{ for m in macro.notes }}{{ m.label }}: {{ m.prompt }}{{ end }}";
                }));
        Add("v28-flow-node-description", "A loop with a display-only description beside its label (§ 6a).",
            V28Package.WithJson(root => V28Package.Node(root, "sections")["description"] = "Drafts one note per section."));

        // ======================================================= § 2: the manifest (schema-catchable)
        Add("invalid-prompt-key-at-28", "A node naming its prompt file — retired at 28; the file is prompts/<id>.md.",
            V28Package.WithJson(root => V28Package.Node(root, "extract")["prompt"] = "prompts/extract.md"));
        Add("invalid-macros-map-at-28", "macros as the 26 map — at 28 a list of ids.",
            V28Package.WithJson(root => root["macros"] = new JsonObject { ["closing"] = "macros/closing.md" }));
        Add("invalid-snippets-map-at-28", "snippets as the 26 map — at 28 a list of ids.",
            V28Package.WithJson(root => root["snippets"] = new JsonObject { ["standards"] = "snippets/standards.md", ["sections"] = "snippets/sections/" }));
        Add("invalid-macro-entry-not-snake-case-at-28", "A macros entry with a capital — an id is snake_case.",
            V28Package.WithJson(root => root["macros"] = new JsonArray("Closing"), files => files["macros/Closing.md"] = files["macros/closing.md"]));
        Add("invalid-description-on-a-plain-node-at-28", "A description on a node that runs a prompt — only a for, if or case node carries one.",
            V28Package.WithJson(root => V28Package.Node(root, "extract")["description"] = "Extracts."));
        Add("invalid-description-empty-at-28", "An empty description on a loop.",
            V28Package.WithJson(root => V28Package.Node(root, "sections")["description"] = ""));

        // ======================================================= § 2–§ 3: the file set (SHAPE_BLIND)
        Add("invalid-missing-implied-prompt-at-28", "A node whose prompts/<id>.md is not in the package.",
            V28Package.WithJson(_ => { }, files => files.Remove("prompts/scope.md")));
        Add("invalid-computed-node-with-a-prompt-file-at-28", "A computed node whose prompts/<id>.md is in the package — it reads no prompt.",
            V28Package.WithJson(_ => { }, files => files["prompts/coverage.md"] = "Compare."));
        Add("invalid-missing-folder-index-at-28", "A folder listed with no index.json.",
            V28Package.WithJson(_ => { }, files => files.Remove("snippets/sections/index.json")));
        Add("invalid-index-bare-list-at-28", "A folder index in the 26 list shape — at 28 the object { label?, items }.",
            Index("""[ { "id": "hpi", "file": "hpi.md" }, { "id": "plan", "file": "plan.md" } ]"""));
        Add("invalid-index-unknown-key-at-28", "A folder index with a key other than label and items.",
            Index("""{ "items": ["hpi", "plan"], "order": "run" }"""));
        Add("invalid-index-empty-items-at-28", "A folder index listing no items.", Index("""{ "items": [] }"""));
        Add("invalid-index-object-item-at-28", "A folder index item spelled { id, file } — at 28 an item is its id.",
            Index("""{ "items": [ { "id": "hpi", "file": "hpi.md" }, "plan" ] }"""));
        Add("invalid-index-duplicate-item-at-28", "A folder index listing an item twice.", Index("""{ "items": ["hpi", "plan", "hpi"] }"""));
        Add("invalid-folder-item-id-not-snake-case-at-28", "A snippet folder item whose id has a capital — the 26 gap closed.",
            V28Package.WithJson(_ => { }, files =>
            {
                files["snippets/sections/index.json"] = """{ "items": ["hpi", "Plan"] }""";
                files["snippets/sections/Plan.md"] = files["snippets/sections/plan.md"];
                files.Remove("snippets/sections/plan.md");
            }));

        // ======================================================= § 2: warnings (valid)
        Add("v28-stray-files-warn", "A file under prompts/ that names no node and an unlisted .md in a folder — warnings, the package still valid (§ 2).",
            V28Package.WithJson(_ => { }, files =>
            {
                files["prompts/old-draft.md"] = "An old draft.";
                files["snippets/sections/extra.md"] = "Extra.";
            }));

        // ======================================================= the gate below 28 (filed under v27/)
        var (json, files27) = V28Package.MinimalJson();
        var gate = JsonNode.Parse(json)!.AsObject();
        gate["specVersion"] = 27;
        foreach (var node in gate["nodes"]!.AsArray().Select(n => n!.AsObject()).Where(n => n["for"] is null && n["if"] is null && n["case"] is null && (string?)n["id"] != "coverage"))
        {
            node["prompt"] = $"prompts/{(string?)node["id"]}.md";
        }

        gate["snippets"] = new JsonObject { ["standards"] = "snippets/standards.md", ["sections"] = "snippets/sections/" };
        files27["snippets/sections/index.json"] = """[ { "id": "hpi", "file": "hpi.md" }, { "id": "plan", "file": "plan.md" } ]""";
        cases.Add(new Case("invalid-macros-id-list-at-27", 27, "macros as a list of ids at 27 — the spelling requires specVersion 28.",
            WorkflowPackageManifestJson.Read(gate.ToJsonString(), "gate@v27", V27Package.Accepting27.ToList()), files27));
    }
}
