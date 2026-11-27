namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #1509 (specVersion 31, area 12, Task 25): the bodies the 31 scenario answers with, written as the Api sends them
/// (records in camelCase, a 31 input's format a self-contained schema, a record row as the engine's writers write it).
/// Fictional people and text only (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
/// </summary>
internal static class Fixtures31
{
    public const string PackageRef = "example-nested-note@v2026.12.1";

    /// <summary>S06-008 and S06-031, as the start checks give them.</summary>
    public const string TimeZoneRefusal = "Set your time zone in your account settings before running a workflow.";
    public const string SignatureRefusal = "This workflow needs your signature. Choose one in Profile.";

    public const string JobId = "31e2e31e2e31e2e31e2e31e2e31e2e31";

    public const string ReleasedText = "Dear Dr A. Sample, Margaret Okafor was seen today at Sample Oncology Clinic.";
    public const string RefusedText = "Refused draft: Margaret Okafor should stop letrozole.";

    /// <summary>
    /// <c>WorkflowPackages/Current</c> at 31: a boolean, a labelled choice, an array of objects (rows), and a form-channel
    /// group whose rows sit inside the Create page's own <c>&lt;label&gt;</c> slot (AGENTS.md's trap). All optional, so
    /// nothing but the start checks holds Start.
    /// </summary>
    public const string Package = """
        {
          "name": "example-nested-note",
          "version": "v2026.12.1",
          "specVersion": 31,
          "title": "Nested note",
          "blocks": [ { "id": "node.plan", "name": "Planning the sections" }, { "id": "node.sections", "name": "Sections" } ],
          "results": [ { "id": "deliverable.note", "label": "Note" } ],
          "macros": [],
          "inputs": [
            { "id": "include_billing", "label": "Include the billing section", "required": false,
              "format": { "type": "boolean" } },
            { "id": "visit_type", "label": "Visit type", "required": false,
              "format": { "type": "string", "oneOf": [ { "const": "new_patient", "title": "New patient" }, { "const": "follow_up", "title": "Follow-up" } ] } },
            { "id": "medicines", "label": "Medicines", "required": false,
              "format": { "type": "array", "items": { "type": "object", "additionalProperties": false,
                "properties": { "drug": { "type": "string", "title": "Drug" }, "dose": { "type": ["number", "null"], "title": "Dose" } },
                "required": ["drug", "dose"] } } },
            { "id": "intake", "label": "Intake questionnaire", "required": false,
              "type": "object", "expectedContent": "form", "channel": "form",
              "fields": [ { "id": "allergies", "label": "Allergies", "required": false, "type": "array" } ],
              "format": { "type": "object", "additionalProperties": false,
                "properties": { "allergies": { "type": "array", "title": "Allergies", "items": { "type": "object", "additionalProperties": false,
                  "properties": { "agent": { "type": "string", "title": "Agent" } }, "required": ["agent"] } } },
                "required": ["allergies"] } }
          ]
        }
        """;

    public const string NoRefusals = """{ "refusals": [], "needsProfile": [] }""";

    public const string TwoRefusals = """
        { "refusals": [ "Set your time zone in your account settings before running a workflow.", "This workflow needs your signature. Choose one in Profile." ],
          "needsProfile": [ "signature" ] }
        """;

    /// <summary><c>WorkflowPackages/Current/Content</c> at 31: a small package's source files and its evidence.</summary>
    public const string Content = """
        {
          "name": "example-nested-note",
          "version": "v2026.12.1",
          "specVersion": 31,
          "manifest": { "specVersion": 31, "name": "example-nested-note", "title": "Nested note" },
          "files": {
            "nodes/plan.md": "Plan the sections of the letter for Margaret Okafor.",
            "deliverables/note.md": "{{ node.plan.content }}"
          },
          "resolve": { "inputs": { "include_billing": { "description": ["Adds the billing section to the letter."] } },
                       "inputOrder": ["include_billing", "visit_type", "medicines", "intake"] },
          "lock": { "lockVersion": 1 }
        }
        """;

    /// <summary><c>Account/Me</c>: the signed-in clinician, so the Profile page draws its cards.</summary>
    public const string AccountMe = """
        {
          "appUserId": "u31",
          "displayName": "Dr A. Sample",
          "email": "a.sample@example.org",
          "status": "Active",
          "currentIdentity": { "provider": "microsoft", "issuer": "https://login.example.org", "subject": "s31",
                               "linkedAt": "2026-09-01T10:00:00Z", "lastSeenAt": "2026-11-16T10:00:00Z" },
          "linkedIdentities": []
        }
        """;

    public const string Jobs = """
        {
          "jobs": [ { "jobId": "31e2e31e2e31e2e31e2e31e2e31e2e31", "status": "Completed",
                      "createdAtUtc": "2026-11-16T14:00:00Z", "startedAtUtc": "2026-11-16T14:00:01Z", "completedAtUtc": "2026-11-16T14:03:00Z",
                      "totalBlockCount": 2, "completedBlockCount": 2, "failedBlockCount": 0 } ],
          "continuationToken": null
        }
        """;

    /// <summary>The job at record storage 12, finished.</summary>
    public const string Job = """
        {
          "jobId": "31e2e31e2e31e2e31e2e31e2e31e2e31", "appUserId": "u31", "status": "Completed",
          "totalBlockCount": 2, "completedBlockCount": 2, "failedBlockCount": 0,
          "generatedBlocks": {}, "failedBlocks": {}, "success": true,
          "schemaVersion": 12, "workflowPackage": "example-nested-note@v2026.12.1", "packageSpecVersion": 31,
          "createdAtUtc": "2026-11-16T14:00:00Z", "startedAtUtc": "2026-11-16T14:00:01Z", "completedAtUtc": "2026-11-16T14:03:00Z",
          "resolveHash": "sha256:9b41", "lockHash": "sha256:07c2", "recordHash": "sha256:cd01", "rows": 4, "decisions": 0
        }
        """;

    private const string PlanRow = """
        { "key": "node.plan", "section": "node", "row": {
          "nodeId": "node.plan", "itemId": null, "kind": "agent", "status": "Completed", "stateReason": null, "outputHash": "sha256:aa",
          "label": "Plan for Margaret Okafor", "producer": "generated", "inputHash": "sha256:a0", "hashVersion": 7,
          "tokens": { "input": 1840, "output": 412, "total": 2252 }, "notSent": [], "itemName": null, "positionName": false,
          "runDependent": false, "modelDerived": true, "profileCarrying": false, "runNeutralHash": null, "runSpots": [],
          "renders": [], "macros": [], "units": [], "invalid": null, "retries": [], "profileReads": [], "function": null, "depth": 0 } }
        """;

    private const string GateRow = """
        { "key": "check.gate", "section": "check", "row": {
          "key": "check.gate", "kind": "computed", "label": "Gate", "status": "Completed", "alignedWith": null,
          "checked": { "ref": "deliverable.refused", "hash": "sha256:b3" }, "reads": [], "outputHash": "sha256:ab",
          "producer": "computed", "modelDerived": false, "runDependent": false, "profileCarrying": false,
          "pass": false, "failWith": "The plan is not complete.", "effect": "blocked", "stateReason": null } }
        """;

    private const string NoteRow = """
        { "key": "deliverable.note", "section": "deliverable", "row": {
          "key": "deliverable.note", "label": "Consultation note", "templateHash": null, "format": "plaintext",
          "state": "released", "stateReason": null, "composedHash": "sha256:b1", "hashVersion": 4, "delivery": [], "check": null,
          "sites": [], "substitutions": [], "renders": [], "runNeutralHash": null, "runSpots": [], "units": [], "profileReads": [],
          "modelDerived": false, "runDependent": false, "profileCarrying": false } }
        """;

    private const string RefusedRow = """
        { "key": "deliverable.refused", "section": "deliverable", "row": {
          "key": "deliverable.refused", "label": "Refused note", "templateHash": null, "format": null,
          "state": "blocked", "stateReason": "blocked: check 'gate' failed (The plan is not complete.)", "composedHash": "sha256:b3",
          "hashVersion": 4, "delivery": [], "check": "check.gate", "sites": [], "substitutions": [], "renders": [],
          "runNeutralHash": null, "runSpots": [], "units": [], "profileReads": [],
          "modelDerived": false, "runDependent": false, "profileCarrying": false } }
        """;

    /// <summary><c>ConsultGenerationJobs/{id}/Rows</c>: every row, no text.</summary>
    public static string Rows => "{ \"rows\": [" + string.Join(",", PlanRow, GateRow, NoteRow, RefusedRow) + "] }";

    /// <summary><c>Rows?key=</c>: one row with the text it is still owed; null for a key the job has no row for (404).</summary>
    public static string? RowText(string key) => key switch
    {
        "deliverable.note" => WithText(NoteRow, ReleasedText),
        "deliverable.refused" => WithText(RefusedRow, RefusedText),
        "node.plan" => WithText(PlanRow, "Plan: history, examination, plan."),
        "check.gate" => WithText(GateRow, null),
        _ => null,
    };

    private static string WithText(string row, string? content)
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse(row)!.AsObject();
        body["text"] = content is null ? null : new System.Text.Json.Nodes.JsonObject { ["content"] = content };
        body["textDeleted"] = false;
        return body.ToJsonString();
    }
}
