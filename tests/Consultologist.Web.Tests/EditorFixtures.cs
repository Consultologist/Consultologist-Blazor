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
}
