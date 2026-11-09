using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1339 (package-format-v30-design.md § 13): the specVersion 30 conformance
/// vectors — loops that nest (A1), while/until (A2, A2b), let and labelField (A3).
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

        // ======================================================= valid: § 3 while and until (A2)
        var refining = WorkflowV30WhileTests.Refining;

        void InParts(JsonObject root)
        {
            V30Package.Node(root, "parts")["nodes"]!.AsArray().Add("refine");
            var sources = root["deliverables"]![0]!["sources"]!.AsArray();
            sources.Remove(sources.First(s => s!.GetValue<string>() == "node.refine"));
        }

        void Review(JsonObject root)
        {
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "critic", ["label"] = "Critic", ["format"] = "verdict", ["onInvalid"] = "fail" });
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "review", ["label"] = "Review", ["while"] = "node.critic.value.verdict != \"good\"", ["max"] = 2,
                ["nodes"] = new JsonArray("refine", "critic"), ["ret"] = "node.refine"
            });
            var sources = root["deliverables"]![0]!["sources"]!.AsArray();
            sources.Remove(sources.First(s => s!.GetValue<string>() == "node.refine"));
            sources.Add("node.review");
        }

        Add("v30-while", "A while redrafting a summary until a judge calls it good, at most 3 passes; the draft reads the judge's last feedback as pass.previous.node.judge (§ 3.1, § 3.2).", refining(null, null));
        Add("v30-while-in-a-loop", "A while inside a loop: each part's summary is redrafted per item, reading item and pass together (§ 2.1, § 3.1).",
            refining(InParts, files =>
            {
                files["prompts/draft.md"] = "Section {{ item.value.title }}, pass {{ pass.number }}: {{ pass.previous.node.judge.value.feedback }}";
                files["prompts/judge.md"] = "Judge {{ node.draft.content }} for {{ item.value.title }}";
                files["prompts/draft_paragraph.md"] = "Point {{ item.value.point }} of {{ item.parent.value.title }}, after {{ node.refine.content }}";
            }));
        Add("v30-while-in-a-while", "A while inside a while: the inner draft reads the outer pass as pass.parent and its critic one pass back (§ 3.2).",
            refining(Review, files =>
            {
                files["prompts/critic.md"] = "Critique {{ node.refine.content }} after {{ node.refine.passes }} passes ({{ node.refine.stopped }}).";
                files["prompts/draft.md"] = "Round {{ pass.parent.number }}, pass {{ pass.number }}. {{ pass.parent.previous.node.critic.value.feedback }} {{ pass.previous.node.judge.value.feedback }}";
            }));
        Add("v30-while-passes-and-stopped", "The outside reads a while's last pass and how it ended: node.refine.passes and .stopped (§ 3.2).",
            refining(root =>
            {
                root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "report", ["label"] = "Report", ["format"] = "prose" });
                root["deliverables"]![0]!["sources"]!.AsArray().Add("node.report");
            }, files => files["prompts/report.md"] = "After {{ node.refine.passes }} passes{{ if node.refine.stopped == \"bound\" }} (at its bound){{ end }}: {{ node.refine.content }}"));
        Add("v30-for-until", "A for that stops early: its parts run in order and it stops after the first plan part (§ 3.4).",
            V30Package.With(root => V30Package.Node(root, "parts")["until"] = "item.value.kind == \"plan\" || (node.outline.value.points | array.size) > 4"));

        // ======================================================= § 3: while and until refusals
        // SHAPE_BLIND: a missing max (the schema cannot tie it to while), until on a
        // while, and every pass read; max 0 and max 11 the schema refuses too.
        Add("invalid-while-without-max-at-30", "A while that declares no bound.", refining(root => V30Package.Node(root, "refine").Remove("max"), null));
        Add("invalid-while-max-above-the-cap-at-30", "A while whose bound is above 10.", refining(root => V30Package.Node(root, "refine")["max"] = 11, null));
        Add("invalid-while-max-zero-at-30", "A while whose bound is 0.", refining(root => V30Package.Node(root, "refine")["max"] = 0, null));
        Add("invalid-until-on-a-plain-node-at-30", "until on a plain node — it belongs to a for or a while.", refining(root => V30Package.Node(root, "plan")["until"] = "input.notes == \"\"", null));
        Add("invalid-pass-outside-a-while-at-30", "pass.number read by a node that runs inside no while.",
            refining(null, files => files["prompts/plan.md"] = "Plan {{ pass.number }}: {{ input.notes }}"));
        Add("invalid-pass-previous-of-an-outside-node-at-30", "pass.previous naming a node outside the while.",
            refining(null, files => files["prompts/draft.md"] = "{{ pass.previous.node.plan.content }}"));
        Add("invalid-body-reads-its-while-at-30", "A body node reading the while it runs in — read the previous pass instead.",
            refining(null, files => files["prompts/judge.md"] = "Judge {{ node.refine.content }}"));
        Add("invalid-pass-parent-with-no-outer-while-at-30", "pass.parent read in a while that runs inside no while.",
            refining(null, files => files["prompts/draft.md"] = "{{ pass.parent.number }}"));

        // ======================================================= valid: § 3 the pass as an object, more ways to stop (A2b)
        void Trail(JsonObject root)
        {
            root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "trail", ["label"] = "Revision trail", ["format"] = "prose" });
            root["deliverables"]![0]!["sources"]!.AsArray().Add("node.trail");
        }

        Add("v30-pass-history", "A draft reading every earlier pass (pass.history, oldest first) and the one two back (pass.previous.previous); a trail reading node.refine.history from outside (§ 3.2).",
            refining(Trail, files =>
            {
                files["prompts/draft.md"] = "{{ for p in pass.history }}Pass {{ p.number }}: {{ p.node.judge.value.feedback }}{{ end }} {{ pass.previous.previous.node.judge.value.feedback }} {{ input.notes }}";
                files["prompts/trail.md"] = "{{ for p in node.refine.history }}Pass {{ p.number }}: {{ p.node.draft.content }}{{ end }} Last: {{ node.refine.history[-1].node.judge.value.verdict }}";
            }));
        Add("v30-while-check-first", "A while that checks before each pass, so it may run none (§ 3.1).",
            refining(root => V30Package.Node(root, "refine")["checkFirst"] = true, null));
        Add("v30-while-until", "A while with an until beside its check: it stops when either says so (§ 3.1, § 3.4).",
            refining(root => V30Package.Node(root, "refine")["until"] = "node.judge.value.verdict == \"good\" || pass.number >= 2", null));
        Add("v30-for-max", "A for that runs at most its first 2 parts, and a template reading why it stopped (§ 3.4).",
            V30Package.With(root =>
            {
                V30Package.Node(root, "parts")["max"] = 2;
                root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "assemble", ["label"] = "Assemble", ["format"] = "prose" });
                root["deliverables"]![0]!["sources"]!.AsArray().Add("node.assemble");
            }, files => files["prompts/assemble.md"] = "{{ node.parts.content }}{{ if node.parts.stopped == \"bound\" }} (first 2 parts){{ end }}"));

        // ======================================================= § 3 refusals (A2b)
        Add("invalid-check-first-on-a-for-at-30", "checkFirst on a for — it belongs to a while.", V30Package.With(root => V30Package.Node(root, "parts")["checkFirst"] = true));
        Add("invalid-for-max-zero-at-30", "A for whose max is 0.", V30Package.With(root => V30Package.Node(root, "parts")["max"] = 0));
        Add("invalid-outside-history-previous-at-30", "An element of a while's history read from outside has no previous — the list is in pass order.",
            refining(Trail, files => files["prompts/trail.md"] = "{{ node.refine.history[-1].previous.number }}"));
        Add("invalid-pass-history-as-a-value-at-30", "pass.history read as a value — it is iterated or indexed.",
            refining(null, files => files["prompts/draft.md"] = "{{ pass.history }}"));

        // ======================================================= valid: § 4 let, § 5 labelField (A3)
        var counting = WorkflowV30LetTests.Counting;

        Add("v30-let", "A let counting the problems a node names, with no model call; each drafted problem reads the count (§ 4.1).", counting(null, null));
        Add("v30-let-in-a-loop", "A let inside a loop, computed once per item from the item (§ 4.1).",
            counting(root =>
            {
                (V30Package.Node(root, "problem_count")["let"], V30Package.Node(root, "problem_count")["type"]) = ("item.value.name | string.size", "number");
                V30Package.Node(root, "problems")["nodes"]!.AsArray().Add("problem_count");
            }, null));
        Add("v30-let-of-a-format", "A let typed by an object format, iterated by a loop (§ 4.1).",
            counting(root =>
            {
                (V30Package.Node(root, "problem_count")["let"], V30Package.Node(root, "problem_count")["type"]) = ("node.find_problems.value", "problem_list");
                V30Package.Node(root, "problems")["for"] = "item in node.problem_count.value.problems";
            }, files => files["prompts/draft_problem.md"] = "{{ item.value.name }}: {{ node.problem_count.value.summary }}"));
        Add("v30-label-field", "Items named by a field: a loop over a node's value by each problem's name, and parts by their title (§ 5).",
            V30Package.With(root =>
            {
                V30Package.Node(root, "problems")["labelField"] = "name";
                V30Package.Node(root, "parts")["labelField"] = "title";
            }));

        // ======================================================= § 4, § 5 refusals (A3)
        Add("invalid-let-without-type-at-30", "A let that names no type.", counting(root => V30Package.Node(root, "problem_count").Remove("type"), null));
        Add("invalid-let-of-a-text-format-at-30", "A let typed by a text format.", counting(root => V30Package.Node(root, "problem_count")["type"] = "prose", null));
        Add("invalid-let-retry-at-30", "A let that would retry — there is no model to repair.", counting(root => V30Package.Node(root, "problem_count")["onInvalid"] = "retry", null));
        Add("invalid-let-with-a-format-at-30", "A let that also declares a format.", counting(root => V30Package.Node(root, "problem_count")["format"] = "prose", null));
        Add("invalid-let-reads-the-clock-at-30", "A let calling date.now — not deterministic on replay.", counting(root => V30Package.Node(root, "problem_count")["let"] = "date.now", null));
        Add("invalid-let-type-mismatch-at-30", "A let whose expression is a number, declared text.", counting(root => V30Package.Node(root, "problem_count")["type"] = "text", null));
        Add("invalid-label-field-not-a-field-at-30", "A loop naming its items by a field its elements do not have.",
            V30Package.With(root => V30Package.Node(root, "problems")["labelField"] = "title"));
        Add("invalid-label-field-enum-at-30", "A loop naming its items by an enum field.",
            V30Package.With(root => V30Package.Node(root, "problems")["labelField"] = "severity"));
    }
}
