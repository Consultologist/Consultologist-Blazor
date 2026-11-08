using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1301 (package-format-v29-design.md § 10): the specVersion 29 conformance
/// vectors — a loop over a node's value and a folder item gated on a node. The
/// valid baseline is <see cref="V29Package.Minimal"/>; each invalid case breaks
/// one § 2.1, § 3.1 or § 6 sentence against it; the recorded outcome is whatever
/// the validator says. Held back by the export while 29 is above the accepted
/// ceiling; the bump (#1307) lifts them into `conformance/v29/`, re-exports the
/// two below-29 cases whose sentence it changes, and adds the v28/ gates.
/// Every invalid case here is SHAPE_BLIND: the for text and an item header's if
/// are strings to the schema, so only the validator sees what they name.
/// </summary>
public partial class ConformanceFixtureExport
{
    private static void AddV29Cases(List<Case> cases)
    {
        void Add(string id, string description, (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
            => cases.Add(new Case(id, 29, description, bundle.Manifest, bundle.Files));

        (WorkflowPackageManifest, Dictionary<string, string>) Source(string source, Action<Dictionary<string, string>>? files = null) =>
            V29Package.With(root => V29Package.Node(root, "problems")["for"] = "item in " + source, files);

        (WorkflowPackageManifest, Dictionary<string, string>) ItemIf(string @if) =>
            V29Package.With(_ => { }, files => files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: @if) + "Plan.");

        // ======================================================= valid
        Add("v29-minimal", "A loop over the problems a node returns: for item in node.find_problems.value.problems, its body reading item.value.name and item.value.severity (§ 2).", V29Package.Minimal());
        Add("v29-loop-over-strings", "A loop over an array of strings — concept_list's concepts field, iterated as any array field (§ 2.1).",
            V29Package.With(
                root =>
                {
                    V29Package.Node(root, "problems")["for"] = "item in node.extract.value.concepts";
                    var nodes = root["nodes"]!.AsArray();
                    nodes.Remove(nodes.Single(n => (string?)n!["id"] == "find_problems"));
                },
                files => files["prompts/draft_problem.md"] = "Concept {{ item.id }}: {{ item.value }}."));
        Add("v29-folder-item-if-reads-a-node", "A folder item drafted only when a classifier judged the case in scope — its if reads a scalar node's value (§ 3.1).",
            ItemIf("node.scope.value == \"in_scope\""));

        // ======================================================= § 2.1: the source (SHAPE_BLIND)
        Add("invalid-loop-over-a-whole-value-at-29", "A loop over node.<id>.value itself — a format's value is an object; name the array field.", Source("node.find_problems.value"));
        Add("invalid-loop-over-a-string-field-at-29", "A loop over a field the format types as a string.", Source("node.find_problems.value.summary"));
        Add("invalid-loop-over-an-undeclared-field-at-29", "A loop over a field the format does not declare.", Source("node.find_problems.value.issues"));
        Add("invalid-loop-over-a-prose-node-at-29", "A loop over a prose node's value — prose has none.", Source("node.assessment.value.x"));
        Add("invalid-loop-over-a-nodes-content-at-29", "A loop over a node's content — a loop iterates a value.", Source("node.find_problems.content"));
        Add("invalid-loop-over-an-undeclared-node-at-29", "A loop over a node the package does not declare.", Source("node.nowhere.value.x"));
        Add("invalid-loop-over-a-flow-node-at-29", "A loop over a loop's value — nesting waits; iterate a plain node.", Source("node.sections.value"));
        Add("invalid-loop-over-a-body-node-at-29", "A loop over a node that runs inside another loop.", Source("node.section_notes.value.x"));
        Add("invalid-loop-source-reads-the-loop-at-29", "A loop whose source reads the loop — the source runs before it.",
            V29Package.With(_ => { }, files => files["prompts/find_problems.md"] = "Problems again: {{ node.problems.content }}"));
        Add("invalid-loop-item-undeclared-field-at-29", "A body reading a field the element does not carry.",
            V29Package.With(_ => { }, files => files["prompts/draft_problem.md"] = "{{ item.value.stage }}"));

        // ======================================================= § 3.1: a folder item's if (SHAPE_BLIND)
        Add("invalid-folder-item-if-reads-content-at-29", "A folder item's if reading a node's content — it reads a scalar's value.", ItemIf("node.scope.content != \"\""));
        Add("invalid-folder-item-if-reads-a-loop-at-29", "A folder item's if reading a loop — a condition decides before the loop runs.", ItemIf("node.sections.content != \"\""));

        // ======================================================= the gates below 29 (filed under v28/)
        (WorkflowPackageManifest, Dictionary<string, string>) At28((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
            (WorkflowPackageManifestJson.Read(
                JsonSerializer.Serialize(bundle.Manifest with { SpecVersion = 28 }, WorkflowPackageManifestJson.WriteOptions), "gate@v28", V28Package.Accepting28.ToList()),
             bundle.Files);

        cases.Add(new Case("invalid-loop-over-a-node-value-at-28", 28, "A loop over a node's value at 28 — it requires specVersion 29.",
            At28(V29Package.Minimal()).Item1, At28(V29Package.Minimal()).Item2));
        var gated = V28Package.WithJson(_ => { }, files => files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: "node.scope.value == \"in_scope\"") + "Plan.");
        cases.Add(new Case("invalid-folder-item-if-reads-a-node-at-28", 28, "A folder item's if reading a scalar node's value at 28 — it requires specVersion 29.", gated.Manifest, gated.Files));
    }
}
