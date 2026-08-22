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

    public static WorkflowPackageContentResponse Package(string manifestJson, int specVersion) =>
        new(
            "acct-1234567890ab",
            "v2026.07.1",
            specVersion,
            JsonDocument.Parse(manifestJson).RootElement.Clone(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PromptFile] = "Draft {{ section_name }} from {{ consult_draft }}.",
                [StandardsIndex] = """
                    { "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "History", "file": "hpi.md" } ] }
                    """,
                ["data/standards/hpi.md"] = "Document the presenting illness."
            });

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
