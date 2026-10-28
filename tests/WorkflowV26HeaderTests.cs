using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1105 (package-format-v26-design.md § 5): a file says what it is — the JSON
/// header a snippet, folder item or macro file opens with; the closed key set
/// per kind; one home; the index the ordered list; .label for .name; the
/// instruction readable; Scriban never seeing the header.
/// </summary>
public class WorkflowV26HeaderTests
{
    private static List<string> Errors(Action<Dictionary<string, string>> withFiles) => V26Package.FlowErrors(m => m, withFiles);

    // ----- the split ---------------------------------------------------------------

    [Fact]
    public void Split_TakesTheBracesOnTheirOwnLines_AndTheBodyAfter()
    {
        var (header, body) = WorkflowTextHeaders.Split("{\n  \"label\": \"X\"\n}\nThe text.\nMore.");

        Assert.Equal("{\n  \"label\": \"X\"\n}", header);
        Assert.Equal("The text.\nMore.", body);
    }

    [Fact]
    public void Split_ToleratesABomAndCrlf()
    {
        var (header, body) = WorkflowTextHeaders.Split("﻿{\r\n  \"label\": \"X\"\r\n}\r\nThe text.");

        Assert.Equal("{\n  \"label\": \"X\"}".Replace("}", "\n}"), header);
        Assert.Equal("The text.", body);
    }

    [Fact]
    public void Split_NeverMistakesAScribanLine_OrATextWithNoHeader()
    {
        Assert.Null(WorkflowTextHeaders.Split("{{ input.notes }}\nThe text.").HeaderJson);
        Assert.Null(WorkflowTextHeaders.Split("The text.").HeaderJson);
        Assert.Null(WorkflowTextHeaders.Split("").HeaderJson);
        Assert.Null(WorkflowTextHeaders.Split("{ \"label\": \"X\" }\nThe text.").HeaderJson);
    }

    [Fact]
    public void AHeaderOpenedAndNeverClosed_IsNotAnObject()
    {
        var errors = Errors(files => files["snippets/standards.md"] = "{\n  \"label\": \"X\"\nFollow.");

        Assert.Contains(errors, e => e.StartsWith("snippets/standards.md header is not a JSON object:", StringComparison.Ordinal));
    }

    // ----- the header per kind ---------------------------------------------------

    [Fact]
    public void TheMinimal_IsValid_WithItsHeaders()
    {
        var result = V26Package.Validate(V26Package.Minimal());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void NoHeader_IsRefusedNamingTheKind()
    {
        Assert.Contains("snippets/standards.md has no header; a snippet file opens with { \"label\": … }.",
            Errors(files => files["snippets/standards.md"] = "Follow the local standards."));
        Assert.Contains("snippets/sections/plan.md has no header; a snippet file opens with { \"label\": … }.",
            Errors(files => files["snippets/sections/plan.md"] = "Plan."));
        Assert.Contains("macros/closing.md has no header; a macro file opens with { \"label\": … }.",
            Errors(files => files["macros/closing.md"] = "Closing."));
    }

    [Fact]
    public void AHeaderOnAPrompt_IsRefused()
    {
        var errors = Errors(files => files["prompts/scope.md"] = V26Package.Header("Scope") + "Is this in scope? {{ input.notes }}");

        Assert.Contains("Node 'scope' prompt 'prompts/scope.md' carries a header; a prompt file has no label, instruction or gate — the node's fields are in the manifest.", errors);
    }

    [Fact]
    public void TheKeySet_IsClosed_PerKind()
    {
        Assert.Contains("snippets/standards.md header declares 'lable', which is not a header key (a snippet file's header carries label, if).",
            Errors(files => files["snippets/standards.md"] = "{\n  \"label\": \"X\",\n  \"lable\": \"Y\"\n}\nFollow."));
        Assert.Contains("snippets/standards.md header declares 'instruction', which is not a header key (a snippet file's header carries label, if).",
            Errors(files => files["snippets/standards.md"] = V26Package.Header("X", instruction: "no") + "Follow."));
        Assert.Contains("macros/closing.md header declares 'colour', which is not a header key (a macro file's header carries label, instruction, optional, default, if).",
            Errors(files => files["macros/closing.md"] = "{\n  \"label\": \"X\",\n  \"instruction\": \"Y\",\n  \"colour\": \"blue\"\n}\nClosing."));
    }

    [Fact]
    public void AReservedName_IsRefusedByName()
    {
        foreach (var key in new[] { "id", "file", "content", "value", "prompt" })
        {
            Assert.Contains($"snippets/standards.md header declares '{key}', a reserved name; a header carries label, if, instruction, optional and default.",
                Errors(files => files["snippets/standards.md"] = "{\n  \"label\": \"X\",\n  \"" + key + "\": \"y\"\n}\nFollow."));
        }
    }

    [Fact]
    public void ALabel_IsRequired_AndTyped()
    {
        Assert.Contains("snippets/standards.md header declares no label; a snippet file's header names its label.",
            Errors(files => files["snippets/standards.md"] = V26Package.Header(null, @if: "input.kind == \"new\"") + "Follow."));
        Assert.Contains("macros/closing.md header declares no label; a macro file's header names its label.",
            Errors(files => files["macros/closing.md"] = "{\n  \"instruction\": \"Y\"\n}\nClosing."));
        Assert.Contains("snippets/standards.md header 'label' must be a string.",
            Errors(files => files["snippets/standards.md"] = "{\n  \"label\": 3\n}\nFollow."));
        Assert.Contains("macros/closing.md header 'optional' must be a boolean.",
            Errors(files => files["macros/closing.md"] = "{\n  \"label\": \"X\",\n  \"instruction\": \"Y\",\n  \"optional\": \"yes\"\n}\nClosing."));
    }

    [Fact]
    public void AMacrosInstruction_IsRequired_WhereReadInline_AndOptionalWhereNot()
    {
        // #1096 (§ 2.5): closing is placed after assessment and read inline by nothing — optional.
        Assert.Empty(Errors(files => files["macros/closing.md"] = V26Package.Header("Closing") + "Closing."));

        // Read inline by the assessment prompt — required, the reader named.
        Assert.Contains("Macro 'closing' is read inline by node 'assessment' and declares no instruction; the model is told nothing (macros/closing.md).",
            Errors(files =>
            {
                files["macros/closing.md"] = V26Package.Header("Closing") + "Closing.";
                files["prompts/assessment.md"] = "Assess. {{ macro.closing.prompt }}";
            }));

        // Read inline through a snippet the assessment prompt reads — the node named, not the snippet.
        Assert.Contains("Macro 'closing' is read inline by node 'assessment' and declares no instruction; the model is told nothing (macros/closing.md).",
            Errors(files =>
            {
                files["macros/closing.md"] = V26Package.Header("Closing") + "Closing.";
                files["snippets/standards.md"] = V26Package.Header("Local standards") + "Follow. {{ macro.closing.prompt }}";
            }));

        // Read inline with an instruction — as ever; an instruction on a placed-only macro — kept, no warning.
        Assert.Empty(Errors(files => files["prompts/assessment.md"] = "Assess. {{ macro.closing.prompt }}"));
        var placedOnly = V26Package.Validate(V26Package.Minimal());
        Assert.True(placedOnly.IsValid);
        Assert.Empty(placedOnly.Warnings);
    }

    [Fact]
    public void EveryKey_IsCarried_IntoTheEffectiveManifest_AndTheFilesAreBodies()
    {
        var (manifest, files) = V26Package.Minimal();
        files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Say goodbye.", @if: "input.kind == \"new\"", optional: true, @default: false) + "Closing: {{ profile.name }}.";

        var errors = new List<string>();
        var texts = WorkflowTextHeaders.Resolve(manifest, files, errors);

        Assert.Empty(errors);
        var macro = Assert.Single(texts.Manifest.Macros!);
        Assert.Equal(("Closing", "Say goodbye.", true, false), (macro.Label, macro.Instruction, macro.Optional, macro.Default));
        Assert.Equal("input.kind == \"new\"", texts.Headers["macros/closing.md"].If);
        Assert.Equal("Closing: {{ profile.name }}.", texts.Files["macros/closing.md"]);
        Assert.Equal("Follow the local standards.", texts.Files["snippets/standards.md"]);
        Assert.Equal("HPI", texts.Headers["snippets/sections/hpi.md"].Label);
        Assert.Equal(files["prompts/assessment.md"], texts.Files["prompts/assessment.md"]);
        Assert.True(V26Package.Validate((manifest, files)).IsValid);
    }

    // ----- the optional/default pair, judged over the header (#1224) ----------------

    private static List<string> ClosingHeaderErrors(bool? optional, bool? @default)
    {
        var (manifest, files) = V26Package.Minimal();
        files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Say goodbye.", optional: optional, @default: @default) + "Closing: {{ profile.name }}.";
        return V26Package.Errors((manifest, files));
    }

    [Fact]
    public void OptionalWithoutDefault_IsRefusedAt26_InTheV12Words()
    {
        Assert.Contains(
            "Macro 'closing' is optional and declares no default; an optional macro must say what a run that makes no choice does.",
            ClosingHeaderErrors(optional: true, @default: null));
    }

    [Fact]
    public void DefaultWithoutOptional_IsRefusedAt26_InTheV12Words()
    {
        Assert.Contains(
            "Macro 'closing' declares default but is not optional; only optional: true takes a per-run choice.",
            ClosingHeaderErrors(optional: null, @default: false));
    }

    [Fact]
    public void TheOptionalPair_PublishesAt26_AndNeitherAloneSentenceAppears()
    {
        var errors = ClosingHeaderErrors(optional: true, @default: false);

        Assert.Empty(errors);
        Assert.DoesNotContain(errors, e => e.Contains("is optional and declares no default") || e.Contains("declares default but is not optional"));
    }

    // ----- one home -----------------------------------------------------------------

    [Fact]
    public void AMacroField_InTheManifest_IsRetiredNamingTheHeader()
    {
        var errors = V26Package.FlowErrors(m => m with
        {
            Macros = new List<WorkflowMacroSpec> { new("closing", "Closing", "macros/closing.md", Optional: true, Default: false, Instruction: "Say goodbye.") },
        });

        foreach (var key in new[] { "label", "instruction", "optional", "default" })
        {
            Assert.Contains($"Macro 'closing' declares {key} in the manifest, which is retired at specVersion 26; the macro file's header carries it (macros/closing.md).", errors);
        }
    }

    [Fact]
    public void TheIndex_IsTheOrderedList_AndItsItemsCarryIdAndFileOnly()
    {
        Assert.Contains("Snippet folder 'sections' index.json at specVersion 26 is an ordered list [{ id, file }]; name and when live in the item file's header.",
            Errors(files => files["snippets/sections/index.json"] = """{ "fields": ["id", "name", "content"], "items": [ { "id": "hpi", "name": "HPI", "file": "hpi.md" } ] }"""));
        Assert.Contains("Snippet folder 'sections' item 'hpi' declares name, which is retired at specVersion 26; the item file's header carries label.",
            Errors(files => files["snippets/sections/index.json"] = """[ { "id": "hpi", "file": "hpi.md", "name": "HPI" }, { "id": "plan", "file": "plan.md" } ]"""));
        Assert.Contains("Snippet folder 'sections' item 'hpi' declares 'label', which is not an index key at specVersion 26 (an item is { id, file }; the rest is the item file's header).",
            Errors(files => files["snippets/sections/index.json"] = """[ { "id": "hpi", "file": "hpi.md", "label": "HPI" }, { "id": "plan", "file": "plan.md" } ]"""));
        Assert.Contains("Snippet collection 'sections' has duplicate item id 'hpi'.",
            Errors(files => files["snippets/sections/index.json"] = """[ { "id": "hpi", "file": "hpi.md" }, { "id": "hpi", "file": "plan.md" } ]"""));
        Assert.Contains("Snippet collection 'sections' item 'plan' file 'snippets/sections/nope.md' is missing from the package.",
            Errors(files => files["snippets/sections/index.json"] = """[ { "id": "hpi", "file": "hpi.md" }, { "id": "plan", "file": "nope.md" } ]"""));
    }

    [Fact]
    public void TheOrder_IsTheIndexs()
    {
        var (manifest, files) = V26Package.Minimal();
        files["snippets/sections/index.json"] = """[ { "id": "plan", "file": "plan.md" }, { "id": "hpi", "file": "hpi.md" } ]""";

        WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out _);

        Assert.Equal(new[] { "plan", "hpi" }, snippets.Collections["sections"].Items.Select(i => i.Id));
        Assert.Equal(new[] { "id", "label", "content" }, snippets.Collections["sections"].Fields);
        Assert.Equal("Plan", snippets.Collections["sections"].Items[0].Fields["label"]);
        Assert.Equal("Plan.", snippets.Collections["sections"].Items[0].Fields["content"]);
    }

    // ----- the reads ------------------------------------------------------------------

    [Fact]
    public void Name_IsRefusedWithLabel_OnEveryObject()
    {
        Assert.Contains("Node 'assessment' prompt reads snippet.standards.name; at specVersion 26 the member is .label.",
            Errors(files => files["prompts/assessment.md"] = "Assess. {{ snippet.standards.name }}"));
        Assert.Contains("Node 'assessment' prompt reads macro.closing.name; at specVersion 26 the member is .label.",
            Errors(files => files["prompts/assessment.md"] = "Assess. {{ macro.closing.name }}"));
        Assert.Contains("Node 'section_notes' prompt reads item.name; at specVersion 26 the member is .label.",
            Errors(files => files["prompts/section.md"] = "{{ item.name }}: {{ item.content }}"));
        Assert.Contains("Node 'assessment' prompt reads e.item.name; at specVersion 26 the member is .label.",
            Errors(files => files["prompts/assessment.md"] = "Assess. {{ for e in node.sections }}{{ e.item.name }}{{ end }}"));
        Assert.Contains("Snippet 'standards' reads item.name; at specVersion 26 the member is .label.",
            Errors(files => files["snippets/standards.md"] = V26Package.Header("Local standards") + "{{ item.name }}"));
    }

    [Fact]
    public void Label_AndInstruction_AreReadable()
    {
        Assert.Empty(Errors(files => files["prompts/assessment.md"] = "Assess. {{ snippet.standards.label }} {{ macro.closing.label }} {{ macro.closing.instruction }} {{ for s in snippet.sections }}{{ s.label }}{{ end }}"));

        var (manifest, files) = V26Package.Minimal();
        files["prompts/assessment.md"] = "Assess. {{ macro.closing.instruction }}";
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        var prompt = Assert.Single(walk.Templates, t => t.Kind == TemplateKind.Prompt && t.Id == "assessment");
        Assert.Contains("closing", prompt.References.MacroPrompts);
        Assert.Contains("Node 'assessment' prompt reads macro.closing.colour, which is not a member (a macro has .id, .label, .content, .prompt and .instruction).",
            Errors(f => f["prompts/assessment.md"] = "Assess. {{ macro.closing.colour }}"));
    }

    [Fact]
    public void AFolderItemsIf_IsWalked_AtItsSite()
    {
        var bad = Errors(files => files["snippets/sections/hpi.md"] = V26Package.Header("HPI", @if: "input.kind == \"maybe\"") + "History.");
        Assert.Contains(bad, e => e.StartsWith("Snippet folder 'sections' item 'hpi' if", StringComparison.Ordinal) && e.Contains("'maybe'", StringComparison.Ordinal));

        Assert.Contains(Errors(files => files["snippets/sections/hpi.md"] = V26Package.Header("HPI", @if: "node.scope.value == \"in_scope\"") + "History."),
            e => e.StartsWith("Snippet folder 'sections' item 'hpi' if reads node.scope", StringComparison.Ordinal));

        Assert.Empty(Errors(files => files["snippets/sections/hpi.md"] = V26Package.Header("HPI", @if: "input.kind == \"new\"") + "History."));
    }

    [Fact]
    public void ScribanNeverSeesTheHeader()
    {
        // A header value that would be a bad read is a string, not a template; the body is what is walked.
        Assert.Empty(Errors(files => files["snippets/standards.md"] = V26Package.Header("{{ node.nope.content }}") + "Follow the local standards."));

        var (manifest, files) = V26Package.Minimal();
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        var macro = Assert.Single(walk.Templates, t => t.Kind == TemplateKind.Macro && t.Id == "closing");
        Assert.Equal("Closing: {{ profile.name }}.", macro.Text);
        Assert.Equal("Follow the local standards.", snippets.Scalars["standards"]);
    }

    [Fact]
    public void TheMinimal_Below26_IsUntouched()
    {
        // The 25 minimal carries no headers and its index the 25 object — the 25 path reads it as ever.
        var result = WorkflowPackageValidator.Validate(V25Package.Minimal().Manifest, V25Package.Minimal().Files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}
