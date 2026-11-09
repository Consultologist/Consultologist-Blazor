using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1339 (package-format-v30-design.md § 13): the specVersion 30 conformance
/// vectors — loops that nest (A1), then while/until (A2) and let/labelField (A3).
/// The valid baseline is <see cref="V30Package.Minimal"/>; each invalid case
/// breaks one sentence against it; the recorded outcome is whatever the
/// validator says. Held back by the export while 30 is above the accepted
/// ceiling; the bump (#1347) lifts them into `conformance/v30/`, re-exports the
/// below-30 cases whose sentence it changes, and adds the v29/ gates. Every
/// invalid nesting case is SHAPE_BLIND: a body is a list of ids and the for text
/// a string to the schema, so only the validator sees what they name.
/// </summary>
public partial class ConformanceFixtureExport
{
    private static void AddV30Cases(List<Case> cases)
    {
        void Add(string id, string description, (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
            => cases.Add(new Case(id, 30, description, bundle.Manifest, bundle.Files));

        (WorkflowPackageManifest, Dictionary<string, string>) Inner(string source, Action<Dictionary<string, string>>? files = null) =>
            V30Package.With(root => V30Package.Node(root, "paragraphs")["for"] = "item in " + source, files);

        // ======================================================= valid: § 2 nesting
        Add("v30-minimal", "A loop inside a loop: parts over node.plan.value.sections, its body outlining each part, then paragraphs over that outline's points, each reading its part as item.parent (§ 2).", V30Package.Minimal());
        Add("v30-three-levels", "Three levels: a loop over an array input inside paragraphs, reading item.parent.parent (§ 2.1, § 2.4).",
            V30Package.With(
                root =>
                {
                    root["inputs"]!.AsArray().Add(new JsonObject { ["id"] = "questions", ["label"] = "Questions", ["type"] = "array", ["items"] = "text" });
                    var paragraphs = V30Package.Node(root, "paragraphs");
                    paragraphs["nodes"] = new JsonArray("draft_paragraph", "sentences");
                    paragraphs["ret"] = "node.sentences";
                    root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "draft_sentence", ["label"] = "Draft sentence", ["format"] = "prose" });
                    root["nodes"]!.AsArray().Add(new JsonObject
                    {
                        ["id"] = "sentences", ["label"] = "Sentences", ["for"] = "item in input.questions",
                        ["nodes"] = new JsonArray("draft_sentence"), ["ret"] = "node.draft_sentence"
                    });
                },
                files => files["prompts/draft_sentence.md"] =
                    "Part {{ item.parent.parent.value.title }}, point {{ item.parent.value.point }}, question {{ item.id }}: {{ item.value }}. {{ node.draft_paragraph.content }}"));
        Add("v30-inner-over-the-outer-item-field", "An inner loop over a list field of the outer item: item in item.value.topics (§ 2.2).",
            Inner("item.value.topics", files => files["prompts/draft_paragraph.md"] = "Part {{ item.parent.value.title }}, topic {{ item.value.topic }}, of {{ node.outline.value.points | array.size }} points."));
        Add("v30-loop-in-a-branch-in-a-loop", "A loop inside an if inside a loop: paragraphs run only for a history part (§ 2.1).",
            V30Package.With(root =>
            {
                var parts = V30Package.Node(root, "parts");
                parts["nodes"] = new JsonArray("outline", "gate");
                parts["ret"] = "node.gate";
                root["nodes"]!.AsArray().Add(new JsonObject
                {
                    ["id"] = "gate", ["label"] = "History only", ["if"] = "item.value.kind == \"history\"",
                    ["then"] = new JsonObject { ["nodes"] = new JsonArray("paragraphs"), ["ret"] = "node.paragraphs" },
                });
            }));
        Add("v30-nested-list-in-a-template", "A template iterating a nested list through the outer element: for p in s.paragraphs (§ 6).",
            V30Package.With(root =>
            {
                root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "assemble", ["label"] = "Assemble", ["format"] = "prose" });
                root["deliverables"]![0]!["sources"]!.AsArray().Add("node.assemble");
            }, files => files["prompts/assemble.md"] =
                "{{ for s in node.parts }}## {{ s.item.value.title }}\n{{ for p in s.paragraphs }}{{ p.content }}\n{{ end }}{{ end }}"));

        // ======================================================= § 2: nesting refusals (SHAPE_BLIND)
        Add("invalid-body-holds-its-holder-at-30", "A loop's body listing the loop that holds it.",
            V30Package.With(root => V30Package.Node(root, "paragraphs")["nodes"] = new JsonArray("draft_paragraph", "parts")));
        Add("invalid-inner-over-a-text-field-at-30", "An inner loop over a field the outer item types as text.", Inner("item.value.title"));
        Add("invalid-inner-over-an-undeclared-field-at-30", "An inner loop over a field the outer item does not declare.", Inner("item.value.missing"));
        Add("invalid-inner-over-the-item-id-at-30", "An inner loop over item.id — it iterates a list field of the outer item.", Inner("item.id"));
        Add("invalid-top-level-over-item-at-30", "A top-level loop over item.value — it runs inside no loop.",
            V30Package.With(root => V30Package.Node(root, "parts")["for"] = "item in item.value.sections"));
        Add("invalid-inner-over-an-unrelated-loop-node-at-30", "An inner loop over a node of a loop that does not enclose it.", Inner("node.draft_problem.value.points"));
        Add("invalid-item-parent-in-a-top-level-loop-at-30", "item.parent read in a loop that runs inside no loop.",
            V30Package.With(_ => { }, files => files["prompts/outline.md"] = "Outline {{ item.parent.value.title }}."));
        Add("invalid-inner-body-read-from-outside-at-30", "An outer body node reading an inner body node by id — read the inner loop.",
            V30Package.With(_ => { }, files => files["prompts/outline.md"] = "Outline {{ item.value.title }} after {{ node.draft_paragraph.content }}."));
        Add("invalid-inner-source-reads-the-inner-loop-at-30", "An outer body node feeding an inner loop that reads that loop — the source runs before it.",
            V30Package.With(_ => { }, files => files["prompts/outline.md"] = "Outline {{ item.value.title }} after {{ node.paragraphs.content }}."));
    }
}
