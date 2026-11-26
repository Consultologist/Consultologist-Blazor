using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Tests.V31;

/// <summary>A fixture job: its header, and its rows as <c>GET ConsultGenerationJobs/{jobId}/Rows</c> lists them.</summary>
public sealed record Record31Job(ConsultGenerationJobResponse Header, IReadOnlyList<RecordRowRef> Rows);

/// <summary>
/// Task 13 (#1509): a storage 12 record for the History tests. Every row is written by the engine's own writers
/// (<see cref="RunRow.ToJson"/>, <see cref="RunDecision.ToJson"/>, <see cref="DeliveryRow.ToJson"/>, which call
/// <c>RecordRows</c>); only each row's <c>Detail</c> (what the runner knows) is given here, in part 08's member names.
/// The rows are listed in an order a table gives them back, not in key order. Fictional text only.
/// </summary>
internal static class Record31Fixtures
{
    private static readonly DateTime At = new(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc);

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static RecordRowRef Ref(string key, string section, JsonObject json) =>
        new(key, section, JsonSerializer.SerializeToElement(json));

    private static RecordRowRef Row(string key, PlanKind kind, RowState state, string? reason = null, string? hash = "sha256:aa", string detail = "{}")
    {
        var row = new RunRow(key, RowKeyEntry(key), kind, state, reason, hash, Obj(detail));
        return Ref(key, row.Section, row.ToJson());
    }

    private static string RowKeyEntry(string key)
    {
        var cut = key.IndexOfAny([':', '#']);
        return cut < 0 ? key : key[..cut];
    }

    private static RecordRowRef Decision(string owner, string site, string condition, bool? result, string? branch = null) =>
        Ref($"{owner}/if:{site}", "decision",
            new RunDecision(owner, site, condition, "sha256:ee", result, branch,
                [new RunRead("input.kind.value", "sha256:ff")], false, true, At).ToJson());

    private static RecordRowRef Delivery(string deliverable, string kind, string result, string? reason, params string[] outcomes) =>
        Ref(DeliveryPlan.Key(deliverable, kind), "delivery",
            new DeliveryRow(DeliveryPlan.Key(deliverable, kind), deliverable, kind, result, reason,
                result == "sent" ? At : null, outcomes.Select(o => new DeliveryAttempt(At, o)).ToList()).ToJson());

    public static ConsultGenerationJobResponse Header() => new(
        "job-1", null, "Completed", 6, 6, 0, new Dictionary<string, string>(), new Dictionary<string, string>(), true,
        SchemaVersion: 12, WorkflowPackage: "test/general@v2026.12.1", PackageSpecVersion: 31,
        ResolveHash: "sha256:9b41", LockHash: "sha256:07c2", RecordHash: "sha256:cd01", Rows: 40, Decisions: 4);

    public static Record31Job Job()
    {
        var rows = new List<RecordRowRef>
        {
            // an agent node: a macro row with a classifier and one without, and a renderer chain in a template loop (pass [2])
            Row("node.plan", PlanKind.Agent, RowState.Completed, detail: """
                { "label": "Plan for Margaret Okafor", "producer": "generated", "modelDerived": true, "formatHash": "sha256:f0", "typeHash": "sha256:f1",
                  "sentFormatHash": "sha256:f2", "tokens": { "input": 1840, "output": 412, "total": 2252 },
                  "agent": { "name": "consult-prose", "version": 4, "binding": { "format": "markdown_block", "producer": "generated", "via": "format" } },
                  "macros": [
                    { "id": "node.plan/macro:letrozole_text", "macro": "macro.letrozole_text", "offered": true,
                      "decisions": ["node.plan/if:macro.letrozole_text"], "classifier": null, "placed": true, "form": "block",
                      "contentHash": "sha256:c1", "runNeutralHash": null, "runSpots": [] },
                    { "id": "node.plan/macro:follow_up_text", "macro": "macro.follow_up_text", "offered": true,
                      "decisions": ["node.plan/if:macro.follow_up_text"], "classifier": "node.classify", "placed": true, "form": "inline",
                      "contentHash": "sha256:c2", "runNeutralHash": null, "runSpots": [] } ],
                  "renders": [
                    { "id": "node.plan/render:1", "renderer": "renderer.concept_list", "templateHash": "sha256:t1", "source": "node.extract.value",
                      "inputHash": "sha256:i1", "pass": [2], "inputSize": "14 concepts", "outputHash": "sha256:01", "outputLength": 612, "runDependent": false },
                    { "id": "node.plan/render:2", "renderer": "renderer.bullets", "templateHash": "sha256:t2", "source": "node.plan/render:1",
                      "inputHash": "sha256:01", "pass": [2], "inputSize": null, "outputHash": "sha256:02", "outputLength": 590, "runDependent": false } ] }
                """),
            Decision("node.plan", "macro.letrozole_text", "node.classify.value.answer = true", true),
            Decision("node.plan", "macro.follow_up_text", "node.classify.value.answer = true", true),

            // an agent node with run spots, a unit not sent, retries, keywords not sent, an invalid outcome and profile reads
            Row("node.billing", PlanKind.Agent, RowState.Completed, detail: """
                { "label": "Billing note", "producer": "generated", "modelDerived": true, "runDependent": true, "profileCarrying": true,
                  "runNeutralHash": "sha256:n1",
                  "runSpots": [ { "id": "node.billing/spot:1", "site": 2, "pass": null, "start": 14, "length": 16, "reads": "run.date", "valueHash": "sha256:v1",
                                  "output": "16 November 2026", "outputHash": "sha256:o1" } ],
                  "units": [ { "site": "snippet.clinic_header", "hash": "sha256:u1", "sent": true }, { "site": "snippet.macro_body", "hash": "sha256:u2", "sent": false } ],
                  "retries": [ { "atUtc": "2026-11-16T14:00:05Z", "waitMs": 2000, "kind": "capacity", "error": null },
                               { "atUtc": "2026-11-16T14:00:09Z", "waitMs": 4000, "kind": "service", "error": "timeout" } ],
                  "notSent": [ "/properties/onset/format" ],
                  "invalid": { "reason": "macro letrozole_text: missing", "action": "retry", "attempts": 2 },
                  "profileReads": [ { "field": "profile.signature", "hash": "sha256:p1" } ] }
                """),

            // a for with two items, one NotRun, and a decision of its until
            Row("node.sections", PlanKind.For, RowState.Completed, detail: """{ "label": "Sections", "count": 2, "stopped": "bound", "parallel": false, "maxParallel": null, "itemLabel": "title" }"""),
            Decision("node.sections", "until.1", "node.sections.items.1.state = \"completed\"", false),
            Row("node.sections:1", PlanKind.For, RowState.Completed, detail: """{ "label": "Sections" }"""),
            Row("node.sections:2", PlanKind.For, RowState.NotRun, "not run: beyond max 1", detail: """{ "label": "Sections" }"""),
            Row("node.sections.draft:1", PlanKind.Agent, RowState.Completed),
            Row("node.sections.draft:2", PlanKind.Agent, RowState.NotRun, "not run: beyond max 1"),
            Row("node.sections.draft:10", PlanKind.Agent, RowState.NotRun, "not run: beyond max 1"),
            // a loop nested in item 1: its own row, its item row, and the node inside
            Row("node.sections.paragraphs:1", PlanKind.For, RowState.Completed, detail: """{ "label": "Paragraphs", "count": 1 }"""),
            Row("node.sections.paragraphs:1/1", PlanKind.For, RowState.Completed, detail: """{ "label": "Paragraphs" }"""),
            Row("node.sections.paragraphs.draft_paragraph:1/1", PlanKind.Agent, RowState.Completed),

            // a while with two passes, the pass rows, the nodes in them, and a for inside pass 2
            Row("node.refine", PlanKind.While, RowState.Completed, detail: """{ "label": "Refine", "passes": 2, "stopped": "check", "max": 3, "checkFirst": false }"""),
            Row("node.refine#1", PlanKind.While, RowState.Completed, detail: """{ "label": "Refine", "pass": 1 }"""),
            Row("node.refine#2", PlanKind.While, RowState.Completed, detail: """{ "label": "Refine", "pass": 2 }"""),
            Row("node.refine.judge#1", PlanKind.Agent, RowState.Completed),
            Row("node.refine.judge#2", PlanKind.Agent, RowState.Completed),
            Row("node.refine.judge#10", PlanKind.Agent, RowState.NotRun),
            Row("node.refine.sections#2", PlanKind.For, RowState.Completed, detail: """{ "label": "Sections", "count": 3 }"""),
            Row("node.refine.sections#2:3", PlanKind.For, RowState.Completed, detail: """{ "label": "Sections" }"""),
            Decision("node.refine", "pass.1", "node.refine.judge.value.ok = false", true),
            Decision("node.refine", "pass.2", "node.refine.judge.value.ok = false", false),

            // a call and the node inside it
            Row("node.hpi_section", PlanKind.Call, RowState.Completed, detail: """{ "function": "hpi", "arguments": [ { "param": "history", "hash": "sha256:03" } ], "depth": 0 }"""),
            Row("node.hpi_section.standard_draft", PlanKind.Agent, RowState.Completed, detail: """{ "function": "hpi", "depth": 1 }"""),

            // a case decision with result "2"
            Row("node.route", PlanKind.Case, RowState.Completed),
            Decision("node.route", "case", "input.kind.value", null, "2"),

            // a skipped node, with its decision
            Row("node.addendum", PlanKind.Agent, RowState.Skipped, "skipped: input.kind.value = \"table\" is false", hash: null),
            Decision("node.addendum", "self", "input.kind.value = \"table\"", false),

            // a node with decisions and no row
            Decision("node.never_ran", "self", "input.kind.value = \"letter\"", false),
            Decision("node.never_ran", "part.intro", "input.kind.value = \"letter\"", false),

            // checks: a loop aligned with the for (its own row, an item row per item, a check inside each item), and three kinds of effect
            Row("check.part_coverage", PlanKind.For, RowState.Completed, detail: """{ "label": "Part coverage", "count": 2 }"""),
            Row("check.part_coverage:1", PlanKind.For, RowState.Completed,
                detail: """{ "label": "Part coverage", "alignedWith": "node.sections:1", "pass": true, "passed": 2, "total": 2, "effect": "read by check.gate" }"""),
            Row("check.part_coverage:2", PlanKind.For, RowState.Completed,
                detail: """{ "label": "Part coverage", "alignedWith": "node.sections:2", "pass": false, "passed": 1, "total": 2, "effect": "read by check.gate" }"""),
            Row("check.part_coverage.coverage:1", PlanKind.Computed, RowState.Completed,
                detail: """{ "label": "Coverage", "alignedWith": "node.sections:1", "checked": { "ref": "node.sections:1", "hash": "sha256:aa" }, "reads": [ { "path": "input.parts", "hash": "sha256:ab" } ], "pass": true, "passed": 2, "total": 2 }"""),
            Row("check.part_coverage.coverage:2", PlanKind.Computed, RowState.Completed,
                detail: """{ "label": "Coverage", "alignedWith": "node.sections:2", "pass": false, "failWith": "Sample Oncology Clinic needs every part covered.", "passed": 1, "total": 2, "uncoveredHash": "sha256:ac", "uncoveredSize": 1 }"""),
            // a gate of one deliverable: the bare state word; a gate of several: <word> <key> joined by "; "
            Row("check.gate", PlanKind.Computed, RowState.Completed,
                detail: """{ "label": "Gate", "checked": { "ref": "deliverable.refused", "hash": "sha256:b3" }, "pass": false, "failWith": "The plan is not complete.", "effect": "blocked" }"""),
            Row("check.gate_many", PlanKind.Computed, RowState.Completed,
                detail: """{ "label": "Both notes", "pass": true, "effect": "released deliverable.emr.note; held deliverable.held_summary" }"""),
            Row("check.seen", PlanKind.Computed, RowState.Skipped, "skipped: input.kind.value = \"letter\" is false", hash: null,
                detail: """{ "label": "Seen" }"""),

            // deliverables: released (with sites, a substitution in a template loop, hashes), held, blocked, skipped, failed
            Row("deliverable.emr.note", PlanKind.Deliverable, RowState.Released, hash: "sha256:b1",
                detail: """
                { "label": "EMR note", "format": "markdown_block", "templateHash": "sha256:d1", "check": "check.gate_many",
                  "sites": [ { "site": 1, "at": "1:1", "expr": "node.plan.content", "loop": null }, { "site": 2, "at": "3:1", "expr": "item.title", "loop": "node.sections.items" } ],
                  "substitutions": [ { "site": 1, "pass": null, "reads": "node.plan.content", "hash": "sha256:s1", "start": 0, "size": 612, "renders": null },
                                     { "site": 2, "pass": [1], "reads": "item.title", "hash": "sha256:s2", "start": 614, "size": 8, "renders": null } ],
                  "delivery": ["delivery:deliverable.emr.note/email", "delivery:deliverable.emr.note/emr", "delivery:deliverable.emr.note/pdf"] }
                """),
            Row("deliverable.held_summary", PlanKind.Deliverable, RowState.Held, "held: empty", hash: "sha256:b2", detail: """{ "check": "check.gate_many" }"""),
            Row("deliverable.refused", PlanKind.Deliverable, RowState.Blocked, "blocked: check 'gate' failed (The plan is not complete.)", hash: "sha256:b3",
                detail: """{ "label": "Refused note", "check": "check.gate" }"""),
            Row("deliverable.skipped_letter", PlanKind.Deliverable, RowState.Skipped, "skipped: input.kind.value = \"letter\" is false", hash: null),
            Row("deliverable.failed_note", PlanKind.Deliverable, RowState.Failed, "failed: the model service's limits were reached", hash: null),

            // deliveries: sent, not sent, failed (listed out of order)
            Delivery("deliverable.emr.note", "pdf", "failed", "failed: timeout", "failed: timeout", "failed: timeout"),
            Delivery("deliverable.emr.note", "email", "sent", null, "sent"),
            Delivery("deliverable.emr.note", "emr", "not sent", "not sent: EMR isn't set up for this account"),
        };

        return new Record31Job(Header(), rows);
    }
}
