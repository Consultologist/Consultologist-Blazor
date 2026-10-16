using System.Text.Json;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// Manifests for the editor tests, in the camelCase the content repo authors
/// (the reader also tolerates the PascalCase the worker serializer can emit).
/// Small but structurally real: a fan, an aggregator, and a deliverable.
/// </summary>
public static class EditorFixtures
{
    public const string PromptFile = "prompts/draft-section.md";
    public const string StandardsIndex = "data/standards/index.json";

    public static WorkflowPackageContentResponse Package(string manifestJson, int specVersion, params (string Path, string Text)[] extraFiles)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PromptFile] = "Draft {{ section_name }} from {{ consult_draft }}.",
            [StandardsIndex] = """
                { "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "History", "file": "hpi.md" } ] }
                """,
            ["data/standards/hpi.md"] = "Document the presenting illness."
        };
        foreach (var (path, text) in extraFiles)
        {
            files[path] = text;
        }

        return new("acct-1234567890ab", "v2026.07.1", specVersion, JsonDocument.Parse(manifestJson).RootElement.Clone(), files);
    }

    /// <summary>
    /// A repo-owned package loaded into the editor — what the picker gives you
    /// when you select `general` to look at it. Rewrites the embedded manifest
    /// name as well as the record's, the way V8() rewrites specVersion in both:
    /// a fixture that disagrees with itself invites a test that passes for the
    /// wrong reason.
    /// </summary>
    public static WorkflowPackageContentResponse NotMine(string name = "general")
    {
        var mine = V7();
        var json = mine.Manifest.GetRawText().Replace("\"acct-1234567890ab\"", $"\"{name}\"");

        return mine with
        {
            Name = name,
            Manifest = JsonDocument.Parse(json).RootElement.Clone()
        };
    }

    /// <summary>The v6 shape: a string result naming an aggregator.</summary>
    public static WorkflowPackageContentResponse V6() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.07.1",
          "specVersion": 6,
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "result": "node:assemble-note",
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 6);

    /// <summary>
    /// v6 plus a published single-value data entry (#309): a data path with no
    /// trailing slash, bound by the fan node as data:specialty. The value file
    /// carries no trailing newline, because the value is inserted mid-sentence.
    /// </summary>
    public static WorkflowPackageContentResponse V6WithValue()
    {
        var package = Package("""
            {
              "name": "acct-1234567890ab",
              "version": "v2026.07.1",
              "specVersion": 6,
              "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
              "data": { "standards": "data/standards/", "specialty": "data/specialty.txt" },
              "prompts": [
                { "id": "draft-section", "file": "prompts/draft-section.md",
                  "variables": ["section_name", "consult_draft", "specialty"] }
              ],
              "result": "node:assemble-note",
              "nodes": [
                { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
                  "prompt": "draft-section",
                  "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft",
                                "specialty": "data:specialty" } },
                { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
              ]
            }
            """, 6);

        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal)
        {
            ["data/specialty.txt"] = "oncology"
        };

        return package with { Files = files };
    }

    /// <summary>
    /// v6 with two published values: specialty, which the fan node binds, and
    /// urgency, which nothing binds (#321). Deleting a published value is only
    /// offered for the unbound case, so the two have to coexist — and
    /// V6WithValue cannot grow an unbound one without breaking the test that
    /// asserts nothing is flagged unused there.
    /// </summary>
    public static WorkflowPackageContentResponse V6WithUnusedValue()
    {
        var package = Package("""
            {
              "name": "acct-1234567890ab",
              "version": "v2026.07.1",
              "specVersion": 6,
              "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
              "data": { "standards": "data/standards/", "specialty": "data/specialty.txt",
                        "urgency": "data/urgency.txt" },
              "prompts": [
                { "id": "draft-section", "file": "prompts/draft-section.md",
                  "variables": ["section_name", "consult_draft", "specialty"] }
              ],
              "result": "node:assemble-note",
              "nodes": [
                { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
                  "prompt": "draft-section",
                  "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft",
                                "specialty": "data:specialty" } },
                { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
              ]
            }
            """, 6);

        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal)
        {
            ["data/specialty.txt"] = "oncology",
            ["data/urgency.txt"] = "routine"
        };

        return package with { Files = files };
    }

    /// <summary>
    /// The v7 package at specVersion 8 — the manifest JSON itself, not just the
    /// response record, because the validator reads the manifest. Nothing else
    /// changes: both v8 additions are optional over a v7 declaration.
    /// </summary>
    public static WorkflowPackageContentResponse V8()
    {
        var v7 = V7();
        var json = v7.Manifest.GetRawText().Replace("\"specVersion\": 7", "\"specVersion\": 8");

        return v7 with
        {
            SpecVersion = 8,
            Manifest = JsonDocument.Parse(json).RootElement.Clone()
        };
    }

    /// <summary>
    /// #427: a v9 package whose condition the editor can read but not yet
    /// write — an ordering over a number. The editor composes only the v8
    /// forms (#429); what it must not do is refuse the package at the desk.
    /// </summary>
    public static WorkflowPackageContentResponse V9Conditional() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 9,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "length_of_stay", "label": "Length of stay (days)", "required": false, "type": "number" }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" },
            { "id": "discharge_summary", "node": "node:assemble-summary", "label": "Discharge summary",
              "when": "length_of_stay > 7" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] },
            { "id": "assemble-summary", "label": "Assembling summary", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 9);

    /// <summary>
    /// #429: a v9 package declaring the structured shapes — an array of text,
    /// an object with a required number and an optional enum, and an array of
    /// objects. Nothing binds the structured inputs; the prompt reads
    /// consult_draft, so the package validates as V7 does.
    /// </summary>
    public static WorkflowPackageContentResponse V9Structured() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 9,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "prior_notes", "label": "Prior notes", "required": false, "type": "array", "items": "text" },
            { "id": "patient", "label": "Patient", "required": true, "type": "object",
              "fields": [
                { "id": "age", "label": "Age", "required": true, "type": "number" },
                { "id": "sex", "label": "Sex", "required": false, "type": "enum", "values": ["female", "male"] }
              ] },
            { "id": "labs", "label": "Labs", "required": false, "type": "array", "items": "object",
              "fields": [
                { "id": "name", "label": "Test", "required": true },
                { "id": "value", "label": "Value", "required": true, "type": "number" }
              ] }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 9);

    /// <summary>
    /// #745: a v17 package with an input carrying a content channel
    /// (`expectedContent: ambient-note`) and a union-typed input
    /// (`type: [text, array]`) — the two input features the editor used to drop
    /// on any inputs edit. The first input is the one tests relabel to prove the
    /// others survive untouched.
    /// </summary>
    public static WorkflowPackageContentResponse V17ChannelAndUnion() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 17,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "encounter_note", "label": "Encounter note", "required": false, "type": "text", "expectedContent": "ambient-note" },
            { "id": "notes", "label": "Notes", "required": false, "type": ["text", "array"], "items": "text" }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 17);

    /// <summary>
    /// v10 (#498): V9Structured at 10 with structure below one level — an
    /// array of objects whose fields are an array and an object, and an array
    /// of arrays written as a spec.
    /// </summary>
    public static WorkflowPackageContentResponse V10Nested() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 10,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "family_history", "label": "Family history", "required": false, "type": "array", "items": "object",
              "fields": [
                { "id": "relative", "label": "Relative", "required": true },
                { "id": "conditions", "label": "Conditions", "required": false, "type": "array", "items": "text" },
                { "id": "contact", "label": "Contact", "required": false, "type": "object",
                  "fields": [
                    { "id": "phone", "label": "Phone", "required": true },
                    { "id": "preferred", "label": "Preferred", "required": false, "type": "enum", "values": ["phone", "email"] }
                  ] }
              ] },
            { "id": "grid", "label": "Grid", "required": false, "type": "array",
              "items": { "type": "array", "items": "number" } }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 10);

    /// <summary>
    /// v10 (#498): array elements nested past a scalar — an array of arrays of
    /// objects, a three-level array of numbers, and an array of arrays of enums.
    /// The shapes the editor must author beyond array-of-array-of-scalar (#748).
    /// </summary>
    public static WorkflowPackageContentResponse V10DeepArrays() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 10,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "matrix", "label": "Matrix", "required": false, "type": "array",
              "items": { "type": "array", "items": { "type": "object",
                "fields": [
                  { "id": "name", "label": "Name", "required": true },
                  { "id": "scores", "label": "Scores", "required": false, "type": "array", "items": "number" }
                ] } } },
            { "id": "cube", "label": "Cube", "required": false, "type": "array",
              "items": { "type": "array", "items": { "type": "array", "items": "number" } } },
            { "id": "labels", "label": "Labels", "required": false, "type": "array",
              "items": { "type": "array", "items": { "type": "enum", "values": ["low", "high"] } } }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 10);

    /// <summary>
    /// v10 (#498): V9Structured at 10 with a classifier over the draft —
    /// "scope" answering in_scope or out_of_scope — and a document conditioned
    /// on its answer.
    /// </summary>
    public static WorkflowPackageContentResponse V10Classifier() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 10,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "patient", "label": "Patient", "required": true, "type": "object",
              "fields": [
                { "id": "age", "label": "Age", "required": true, "type": "number" },
                { "id": "sex", "label": "Sex", "required": false, "type": "enum", "values": ["female", "male"] }
              ] }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "classify", "file": "prompts/classify.md", "variables": ["referral"] },
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note", "when": "node:scope == in_scope" }
          ],
          "nodes": [
            { "id": "scope", "label": "Is the referral in scope?", "prompt": "classify", "kind": "classifier",
              "values": ["in_scope", "out_of_scope"],
              "bindings": { "referral": "input:consult_draft" } },
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 10, ("prompts/classify.md", "Is this in scope? {{ referral }}"));

    /// <summary>
    /// v10 (#498/#754): an object input with a nested object holding testable
    /// scalars (patient.contact.preferred) and an array field (meds) whose
    /// element fields are not read by path. The shape a deep-path condition
    /// reads; its result has no `when` so a test composes one.
    /// </summary>
    public static WorkflowPackageContentResponse V10DeepConditionObject() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 10,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "patient", "label": "Patient", "required": true, "type": "object",
              "fields": [
                { "id": "age", "label": "Age", "required": true, "type": "number" },
                { "id": "contact", "label": "Contact", "required": false, "type": "object",
                  "fields": [
                    { "id": "preferred", "label": "Preferred", "required": false, "type": "enum", "values": ["phone", "email"] },
                    { "id": "zone", "label": "Zone", "required": false, "type": "enum", "values": ["north", "south"] }
                  ] },
                { "id": "meds", "label": "Meds", "required": false, "type": "array", "items": "text" }
              ] }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 10);

    /// <summary>
    /// #746: a package that declares an output-contract schema no node uses — an
    /// orphan the Schemas pane can remove. The body is the catalog concept-list.
    /// </summary>
    public static WorkflowPackageContentResponse V10OrphanSchema() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 10,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "schemas": { "report": "schemas/report.json" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 10, ("schemas/report.json", EditorCatalogSchemas.ConceptListSchema));

    /// <summary>v11 (#564): the classifier package at 11 with a macro wired end to end — declared, referenced, signed, and a reproducible classifier.</summary>
    public static WorkflowPackageContentResponse V11Macro() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 11,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true }
          ],
          "data": { "standards": "data/standards/", "intro": "data/intro.md" },
          "prompts": [
            { "id": "classify", "file": "prompts/classify.md", "variables": ["referral"] },
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "macros": [
            { "id": "disclaimer", "label": "Standing disclaimer", "file": "macros/disclaimer.md" }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note", "when": "node:scope == in_scope",
              "macros": ["disclaimer"], "signature": true }
          ],
          "nodes": [
            { "id": "scope", "label": "Is the referral in scope?", "prompt": "classify", "kind": "classifier",
              "values": ["in_scope", "out_of_scope"], "reproducible": true,
              "bindings": { "referral": "input:consult_draft" } },
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 11,
        ("prompts/classify.md", "Is this in scope? {{ referral }}"),
        ("macros/disclaimer.md", "By {{profile:name}} on {{run:date}}: {{input:consult_draft}} — {{data:intro}} ({{classification:scope}})"),
        ("data/intro.md", "A scalar the macro reads."));

    /// <summary>v11 (#564): the classifier package with only the version raised — no v11 shape used (the control's sibling).</summary>
    public static WorkflowPackageContentResponse V11() 
    {
        var package = V10Classifier();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["specVersion"] = 11;

        return package with { SpecVersion = 11, Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    /// <summary>v11 (#564): the macro package with one optional input added — the help panel's annotation case.</summary>
    public static WorkflowPackageContentResponse V11Macros_WithOptionalInput()
    {
        var package = V11Macro();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["inputs"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = "length_of_stay",
            ["label"] = "Length of stay",
            ["required"] = false
        });

        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    /// <summary>
    /// v12 rung (e) (#621): the macro package at 12 carrying every v12 shape
    /// — an optional macro with its default, a placed-and-gated entry, a
    /// check node named by the result, and a template node — so the carriage
    /// tests can prove no editor pass erases any of it.
    /// </summary>
    public static WorkflowPackageContentResponse V12Full() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 12,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true }
          ],
          "data": { "standards": "data/standards/", "intro": "data/intro.md" },
          "schemas": { "concept-list": "schemas/concept-list.json" },
          "prompts": [
            { "id": "classify", "file": "prompts/classify.md", "variables": ["referral"] },
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] },
            { "id": "extract-terms", "file": "prompts/extract-terms.md", "variables": ["text"] },
            { "id": "header", "file": "prompts/header.md", "variables": ["consult_draft"] }
          ],
          "macros": [
            { "id": "disclaimer", "label": "Standing disclaimer", "file": "macros/disclaimer.md" },
            { "id": "closing", "label": "Closing paragraph", "file": "macros/closing.md", "optional": true, "default": true }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note", "when": "node:scope == in_scope",
              "macros": [ { "id": "disclaimer", "after": "node:draft-section", "when": "node:scope == in_scope" }, "closing" ],
              "signature": true,
              "check": "node:coverage" }
          ],
          "nodes": [
            { "id": "scope", "label": "Is the referral in scope?", "prompt": "classify", "kind": "classifier",
              "values": ["in_scope", "out_of_scope"],
              "bindings": { "referral": "input:consult_draft" } },
            { "id": "patient-header", "label": "Patient header", "prompt": "header", "kind": "template",
              "bindings": { "consult_draft": "input:consult_draft" } },
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "extract-input-terms", "label": "Extracting input terms", "prompt": "extract-terms",
              "bindings": { "text": "input:consult_draft" }, "output": { "schema": "concept-list" } },
            { "id": "extract-note-terms", "label": "Extracting note terms", "prompt": "extract-terms",
              "bindings": { "text": "node:assemble-note" }, "output": { "schema": "concept-list" } },
            { "id": "coverage", "label": "Coverage check", "kind": "check", "op": "terms-subset",
              "of": "node:extract-input-terms", "in": "node:extract-note-terms",
              "failWith": "The note does not cover every clinical term found in the referral." },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:patient-header", "node:draft-section"] }
          ]
        }
        """, 12,
        ("prompts/classify.md", "Is this in scope? {{ referral }}"),
        ("prompts/extract-terms.md", "Extract the clinical terms: {{ text }}"),
        ("prompts/header.md", "Header for {{ consult_draft }}."),
        ("macros/disclaimer.md", "By {{profile:name}} on {{run:date}}."),
        ("macros/closing.md", "Thank you for this referral."),
        ("data/intro.md", "A scalar."),
        ("schemas/concept-list.json", EditorCatalogSchemas.ConceptListSchema));

    /// <summary>v12 (#621): the macro package with only the version raised — no v12 shape used (the control).</summary>
    public static WorkflowPackageContentResponse V12()
    {
        var package = V11Macro();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["specVersion"] = 12;

        return package with { SpecVersion = 12, Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    /// <summary>#453: the same package carrying these tags.</summary>
    public static WorkflowPackageContentResponse WithTags(WorkflowPackageContentResponse package, params string[] tags)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["tags"] = new System.Text.Json.Nodes.JsonArray(tags.Select(tag => (System.Text.Json.Nodes.JsonNode?)tag).ToArray());

        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    /// <summary>#432: the same package carrying a title (and, optionally, a description).</summary>
    public static WorkflowPackageContentResponse WithTitle(WorkflowPackageContentResponse package, string title, string? description = null)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["title"] = title;
        if (description != null)
        {
            root["description"] = description;
        }

        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    /// <summary>The v7 shape: declared inputs and a results list.</summary>
    /// <summary>
    /// v15 (#731): a package with a variable-free `disclaimer` prompt rendered
    /// by a template node — the shape the raw toggle is offered on — beside the
    /// ordinary variable-bearing `draft-section` prompt (which it is not).
    /// </summary>
    public static WorkflowPackageContentResponse V15Raw() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.07.1",
          "specVersion": 15,
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "tags": [],
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] },
            { "id": "disclaimer", "file": "prompts/disclaimer.md", "variables": [] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "disclaimer-block", "label": "Disclaimer", "kind": "template",
              "prompt": "disclaimer", "bindings": {} },
            { "id": "assemble-note", "label": "Assembling note",
              "aggregate": ["node:draft-section", "node:disclaimer-block"] }
          ]
        }
        """, 15, ("prompts/disclaimer.md", "Not medical advice."));

    public static WorkflowPackageContentResponse V7() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.07.1",
          "specVersion": 7,
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "prior_notes", "label": "Prior notes", "required": false }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 7);

    /// <summary>
    /// #747: a package with a preludes map — `guidance` referenced by the
    /// draft-section prompt and `unused` an orphan — for the preludes pane.
    /// </summary>
    public static WorkflowPackageContentResponse V7Preludes() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.07.1",
          "specVersion": 7,
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "preludes": {
            "guidance": "preludes/guidance.md",
            "unused": "preludes/unused.md"
          },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true }
          ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md",
              "variables": ["section_name", "consult_draft"], "prelude": "guidance" }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section",
              "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """, 7,
        ("preludes/guidance.md", "Use short SNOMED search terms."),
        ("preludes/unused.md", "An unused prelude."));

    // ----- v25 (C1 #1007) -----

    public const string MedicationListFormatFile = "formats/medication_list.json";

    /// <summary>A package-local custom format: an object shape the engine validates; the custom-json agent writes it.</summary>
    public const string MedicationListFormat = """
        {
          "type": "object",
          "producer": "custom",
          "description": "The medications the referral names, each with its dose.",
          "schema": {
            "type": "object",
            "additionalProperties": false,
            "required": ["medications"],
            "properties": {
              "medications": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["name", "dose"], "properties": { "name": { "type": "string" }, "dose": { "type": ["string", "null"] } } } }
            }
          }
        }
        """;

    /// <summary>
    /// The 25 shape (design § 3): a concept_list node, a classification taking its
    /// values through with, a prose node with a top-level failIfEmpty, a node on a
    /// package-local custom format, a terms_subset check over the concept_list node,
    /// a node fanning a snippet folder (two items, one gated on the kind input — C2
    /// #1008), a single snippet read by the assessment prompt, two macros (C3 #1009:
    /// closing after the assessment and prepended to the deliverable when kind is new;
    /// tool_note before the hpi section item and read inline by the assessment
    /// prompt), an aggregator, and one deliverable naming
    /// the aggregator and the check. No kind, no output, no schemas; formats[]
    /// declares the local format. Every node feeds the deliverable: the prose
    /// node reads the concept_list and the medications; the classifier is exempt.
    /// </summary>
    /// <summary>
    /// C6 (#1012): a 24 package carrying every row of the § 14 migration table — the
    /// editor twin of the library's V24Package: bound variables of every source kind
    /// (a concept node with and without a renderer, a classifier, a data scalar, fan
    /// items), a classifier, a check and its deliverable's check, a template node, a
    /// schemaRefs node, a customSchemas node, a canonical-body schemas node, a raw
    /// prompt, a data scalar with a literal brace and a fanned collection, prompt-side
    /// node macros (bare, gated, forItem), deliverable macros (bare, node-anchored with
    /// a when, slot), hyphenated node ids.
    /// </summary>
    public static WorkflowPackageContentResponse V24Full() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.10.1",
          "specVersion": 24,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "notes", "label": "Referral notes", "required": true },
            { "id": "kind", "label": "Kind", "required": true, "type": "enum", "values": ["new", "follow_up"] }
          ],
          "data": { "house_standards": "data/house_standards.txt", "sections": "data/sections/" },
          "schemas": { "concepts": "schemas/concepts.json" },
          "schemaRefs": { "problems": "concept-list" },
          "customSchemas": { "plan-shape": "schemas/plan-shape.json" },
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
              "bindings": { "notes": "input:notes", "problems": { "from": "node:identify-problem", "as": "concept-context" }, "scope_value": "node:scope", "standard": "data:house_standards", "plan": "node:plan" },
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
        """, 24,
        ("prompts/extract.md", "Extract the concepts from:\n{{ notes }}"),
        ("prompts/problem.md", "Problems in {{ notes }} given:\n{{ concepts }}"),
        ("prompts/scope.md", "Is this in scope? {{ notes }}"),
        ("prompts/plan.md", "Plan steps for {{ notes }}."),
        ("prompts/assessment.md", "Assess {{ notes }}.\nProblems:\n{{ problems }}\nScope: {{ scope_value }}\nStandard: {{ standard }}\nPlan: {{ plan }}\nHandoff: [[slot:handoff]]"),
        ("prompts/section.md", "{{ section_name }}: {{ section_text }}"),
        ("prompts/summary.md", "Summary of {{ notes }}"),
        ("prompts/raw_note.md", "Literal {{ braces }} stay."),
        ("data/house_standards.txt", "Follow the local standards. A literal {{ brace }}."),
        ("data/sections/index.json", """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md" }, { "id": "plan", "name": "Plan", "file": "plan.md" } ] }"""),
        ("data/sections/hpi.md", "History of presenting illness."),
        ("data/sections/plan.md", "The plan {{ in braces }}."),
        ("macros/tool_guidance.md", "Use short terms for {{input:notes}} on {{run:date}}."),
        ("macros/closing.md", "Closing: {{profile:name}}, {{classification:scope}}."),
        ("macros/disclaimer.md", "Disclaimer for {{item:name}} ({{item:content}})."),
        ("macros/handoff.md", "Handoff from {{data:house_standards}}."),
        ("macros/footer.md", "Footer on {{run:date}}."),
        ("schemas/concepts.json", EditorCatalogSchemas.ConceptListSchema),
        ("schemas/plan-shape.json", """{ "type": "object", "description": "The plan as steps.", "additionalProperties": false, "required": ["steps"], "properties": { "steps": { "type": "array", "items": { "type": "string" } } } }"""));

    public static WorkflowPackageContentResponse V25() => Package("""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.10.1",
          "specVersion": 25,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "notes", "label": "Referral notes", "required": true },
            { "id": "kind", "label": "Kind", "required": true, "type": "enum", "values": ["new", "follow_up"] },
            { "id": "intake", "label": "Intake form", "required": false, "type": "object", "expectedContent": "form",
              "fields": [ { "id": "allergies", "label": "Allergies", "required": false }, { "id": "on_letrozole", "label": "On letrozole", "required": false, "type": "boolean" } ] }
          ],
          "formats": [ { "id": "medication_list", "file": "formats/medication_list.json" } ],
          "macros": [
            { "id": "closing", "label": "Closing", "file": "macros/closing.md", "instruction": "Add a closing line if the plan changed." },
            { "id": "tool_note", "label": "Tool note", "file": "macros/tool_note.md", "instruction": "Note the tools used when a procedure is described." }
          ],
          "prompts": [
            { "id": "extract", "file": "prompts/extract.md" },
            { "id": "scope", "file": "prompts/scope.md" },
            { "id": "assessment", "file": "prompts/assessment.md" },
            { "id": "meds", "file": "prompts/meds.md" },
            { "id": "section", "file": "prompts/section.md" }
          ],
          "snippets": { "standards": "snippets/standards.md", "sections": "snippets/sections/" },
          "nodes": [
            { "id": "extract", "label": "Extract concepts", "prompt": "extract", "format": "concept_list", "onInvalid": "fail" },
            { "id": "scope", "label": "Scope", "prompt": "scope", "format": "classification", "onInvalid": "fail", "with": { "values": ["in_scope", "out_of_scope"] } },
            { "id": "assessment", "label": "Assessment", "prompt": "assessment", "format": "prose", "failIfEmpty": "Nothing to assess.", "macros": [ { "id": "closing", "at": "after" } ] },
            { "id": "meds", "label": "Medications", "prompt": "meds", "format": "medication_list", "onInvalid": "flag" },
            { "id": "coverage", "label": "Coverage", "format": "terms_subset", "onInvalid": "fail", "with": { "of": "node.extract", "in": "node.extract", "failWith": "The note names problems the source does not." } },
            { "id": "section_notes", "label": "Section notes", "prompt": "section", "format": "prose", "forEach": "snippet.sections", "macros": [ { "id": "tool_note", "at": "before", "forItem": "hpi" } ] },
            { "id": "all", "label": "Everything", "aggregate": ["node.assessment", "node.section_notes"] }
          ],
          "deliverables": [ { "id": "consult", "node": "node.all", "label": "Consultation note", "check": "node.coverage", "macros": [ { "id": "closing", "at": "prepend", "when": "input.kind == \"new\"" } ] } ]
        }
        """, 25,
        ("prompts/extract.md", "Extract the concepts from:\n{{ input.notes }}"),
        ("prompts/scope.md", "Is this in scope? {{ input.notes }}"),
        ("prompts/assessment.md", "Assess.\n{{ snippet.standards.content }}\n{{ macro.tool_note.prompt }}\n{{ input.notes }}\nAllergies: {{ input.intake.allergies }}\n{{ for c in node.extract.value.concepts }}- {{ c }}\n{{ end }}\n{{ for m in node.meds.value.medications }}- {{ m.name }}\n{{ end }}"),
        ("prompts/meds.md", "List the medications in:\n{{ input.notes }}"),
        ("prompts/section.md", "{{ item.name }}: {{ item.content }}"),
        ("macros/closing.md", "Closing: {{ profile.name }}."),
        ("macros/tool_note.md", "Tools used: see the procedure note."),
        ("snippets/standards.md", "Follow the local standards."),
        ("snippets/sections/index.json", """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md" }, { "id": "plan", "name": "Plan", "file": "plan.md", "when": "input.kind == \"new\"" } ] }"""),
        ("snippets/sections/hpi.md", "History: {{ input.notes }}"),
        ("snippets/sections/plan.md", "Plan."),
        (MedicationListFormatFile, MedicationListFormat));
}
