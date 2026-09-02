using System.Text.Json;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// The reader's v7 sections (#218). Case tolerance matters because the worker
/// serializer can emit PascalCase while repo manifests are camelCase.
/// </summary>
public class ManifestReaderTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ReadInputs_ReadsIdLabelAndRequired()
    {
        var inputs = WorkflowManifestReader.ReadInputs(Parse("""
            { "inputs": [
                { "id": "consult_draft", "label": "Consult draft", "required": true },
                { "id": "prior_notes", "label": "Prior notes", "required": false }
            ] }
            """));

        Assert.Equal(
            new[] { ("consult_draft", "Consult draft", true), ("prior_notes", "Prior notes", false) },
            inputs.Select(i => (i.Id, i.Label, i.Required)).ToArray());
    }

    [Fact]
    public void ReadInputs_DefaultsRequiredToTrueWhenAbsent()
    {
        var input = Assert.Single(WorkflowManifestReader.ReadInputs(
            Parse("""{ "inputs": [ { "id": "consult_draft", "label": "Consult draft" } ] }""")));

        Assert.True(input.Required);
    }

    [Fact]
    public void ReadInputs_ReadsItemsAndFields()
    {
        // #429: the composer rebuilds the inputs list from these records, so
        // what the reader does not carry, a publish silently drops.
        var inputs = WorkflowManifestReader.ReadInputs(Parse("""
            { "inputs": [
                { "id": "prior_notes", "label": "Prior notes", "required": false, "type": "array", "items": "text" },
                { "id": "labs", "label": "Labs", "type": "array", "items": "object",
                  "fields": [
                    { "id": "name", "label": "Test" },
                    { "id": "value", "label": "Value", "required": false, "type": "number" },
                    { "id": "unit", "label": "Unit", "type": "enum", "values": ["mg", "mmol"] }
                  ] }
            ] }
            """));

        Assert.Equal("text", inputs[0].Items!.Type);
        Assert.Null(inputs[0].Fields);
        Assert.Equal("object", inputs[1].Items!.Type);
        Assert.Equal(
            new[] { ("name", "Test", true, (string?)null), ("value", "Value", false, "number"), ("unit", "Unit", true, "enum") },
            inputs[1].Fields!.Select(f => (f.Id, f.Label, f.Required, f.Type)).ToArray());
        Assert.Equal(new[] { "mg", "mmol" }, inputs[1].Fields![2].Values);
    }

    [Fact]
    public void ReadInputs_ToleratesPascalCaseItemsAndFields()
    {
        var input = Assert.Single(WorkflowManifestReader.ReadInputs(Parse("""
            { "Inputs": [ { "Id": "labs", "Label": "Labs", "Type": "array", "Items": "object",
                            "Fields": [ { "Id": "name", "Label": "Test", "Required": false } ] } ] }
            """)));

        Assert.Equal("object", input.Items!.Type);
        var field = Assert.Single(input.Fields!);
        Assert.Equal(("name", "Test", false), (field.Id, field.Label, field.Required));
    }

    [Fact]
    public void ReadInputs_ReadsASpecFormElement_ToAnyDepth()
    {
        // v10 (#498): an element that is an array keeps its own structure; a
        // field carries items and fields of its own.
        var inputs = WorkflowManifestReader.ReadInputs(Parse("""
            { "inputs": [
                { "id": "grid", "label": "Grid", "type": "array",
                  "items": { "type": "array", "items": { "type": "enum", "values": ["x", "y"] } } },
                { "id": "family_history", "label": "Family history", "type": "array", "items": "object",
                  "fields": [
                    { "id": "relative", "label": "Relative" },
                    { "id": "conditions", "label": "Conditions", "required": false, "type": "array", "items": "text" },
                    { "id": "contact", "label": "Contact", "required": false, "type": "object",
                      "fields": [ { "id": "phone", "label": "Phone" } ] }
                  ] }
            ] }
            """));

        var grid = inputs[0].Items!;
        Assert.Equal("array", grid.Type);
        Assert.Equal("enum", grid.Items!.Type);
        Assert.Equal(new[] { "x", "y" }, grid.Items.Values);
        Assert.False(grid.IsBare);

        var fields = inputs[1].Fields!;
        Assert.Equal("text", fields[1].Items!.Type);
        Assert.True(fields[1].Items!.IsBare);
        Assert.Equal("phone", Assert.Single(fields[2].Fields!).Id);
        Assert.Null(fields[0].Items);
        Assert.Null(fields[0].Fields);
    }

    [Fact]
    public void ReadInputs_HoistsASpecFormObjectOrEnumElement_ToTheV9Form()
    {
        // One place holds an array-of-object's fields: the input, as v9 wrote
        // them. A spec that says the same thing reads to the same view.
        var inputs = WorkflowManifestReader.ReadInputs(Parse("""
            { "inputs": [
                { "id": "labs", "label": "Labs", "type": "array",
                  "items": { "type": "object", "fields": [ { "id": "name", "label": "Test" } ] } },
                { "id": "sites", "label": "Sites", "type": "array",
                  "items": { "type": "enum", "values": ["clinic", "ward"] } }
            ] }
            """));

        Assert.True(inputs[0].Items!.IsBare);
        Assert.Equal("object", inputs[0].Items!.Type);
        Assert.Equal("name", Assert.Single(inputs[0].Fields!).Id);
        Assert.True(inputs[1].Items!.IsBare);
        Assert.Equal(new[] { "clinic", "ward" }, inputs[1].Values);
    }

    [Fact]
    public void ReadNodes_ReadsAClassifiersKindAndValues()
    {
        // v10 (#498): a classifier is a node of kind "classifier" with the
        // values it may answer; every other node reads null for both.
        var nodes = WorkflowManifestReader.ReadNodes(Parse("""
            { "nodes": [
                { "id": "scope", "label": "Scope", "prompt": "classify", "kind": "classifier", "values": ["in_scope", "out_of_scope"],
                  "bindings": { "referral": "input:consult_draft" } },
                { "id": "draft-section", "label": "Draft", "prompt": "draft-section", "bindings": {} }
            ] }
            """));

        Assert.True(nodes[0].IsClassifier);
        Assert.Equal("classifier", nodes[0].Kind);
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, nodes[0].Values);
        Assert.False(nodes[1].IsClassifier);
        Assert.Null(nodes[1].Kind);
        Assert.Null(nodes[1].Values);
    }

    [Fact]
    public void ReadInputs_LeavesItemsAndFieldsNullWhenAbsent()
    {
        var input = Assert.Single(WorkflowManifestReader.ReadInputs(
            Parse("""{ "inputs": [ { "id": "consult_draft", "label": "Consult draft", "type": "enum", "values": ["a", "b"] } ] }""")));

        Assert.Null(input.Items);
        Assert.Null(input.Fields);
    }

    [Fact]
    public void ReadInputs_ToleratesPascalCase()
    {
        var input = Assert.Single(WorkflowManifestReader.ReadInputs(
            Parse("""{ "Inputs": [ { "Id": "consult_draft", "Label": "Consult draft", "Required": false } ] }""")));

        Assert.Equal("consult_draft", input.Id);
        Assert.False(input.Required);
    }

    [Fact]
    public void ReadResults_ReadsTheDeclaredSet()
    {
        var results = WorkflowManifestReader.ReadResults(Parse("""
            { "results": [
                { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" },
                { "id": "patient_letter", "node": "node:assemble-letter", "label": "Patient letter" }
            ] }
            """));

        Assert.Equal(
            new[] { ("consult_note", "node:assemble-note"), ("patient_letter", "node:assemble-letter") },
            results.Select(r => (r.Id, r.Node)).ToArray());
    }

    [Fact]
    public void ReadTags_ReadsTheArray_EmptyIsStated_AbsentIsNull()
    {
        // #453: the pane and the foreign notice read this; [] and absence are
        // different answers.
        Assert.Equal(new[] { "oncology", "Breast" }, WorkflowManifestReader.ReadTags(Parse("""{ "specVersion": 9, "tags": ["oncology", "Breast"] }""")));
        Assert.Empty(WorkflowManifestReader.ReadTags(Parse("""{ "specVersion": 9, "tags": [] }"""))!);
        Assert.Null(WorkflowManifestReader.ReadTags(Parse("""{ "specVersion": 8 }""")));
        Assert.Equal(new[] { "oncology" }, WorkflowManifestReader.ReadTags(Parse("""{ "SpecVersion": 9, "Tags": ["oncology"] }""")));
    }

    [Fact]
    public void LegacyManifest_ReadsNeitherSection()
    {
        var manifest = Parse("""{ "result": "node:assemble-note" }""");

        Assert.Empty(WorkflowManifestReader.ReadInputs(manifest));
        Assert.Empty(WorkflowManifestReader.ReadResults(manifest));
        Assert.Equal("node:assemble-note", WorkflowManifestReader.ReadResultRef(manifest));
    }

    // The trailing slash is the whole discriminator: WorkflowDataResolver reads
    // a path ending in '/' as a collection and anything else as a single value.
    // The editor writes these paths, so the reader has to split them the same
    // way the engine does or the two disagree about what a package contains.

    [Fact]
    public void ReadScalarEntries_TakesOnlyTheNonDirectoryPaths()
    {
        var entries = WorkflowManifestReader.ReadScalarEntries(Parse("""
            { "data": {
                "standards": "data/standards/",
                "specialty": "data/specialty.txt"
            } }
            """));

        var entry = Assert.Single(entries);
        Assert.Equal("specialty", entry.Id);
        // The path, not just the id — a value is the file it points at, so
        // this is what lets the editor load and write it.
        Assert.Equal("data/specialty.txt", entry.Path);
    }

    [Fact]
    public void ReadScalars_StillReturnsIdsForTheBindingPicker()
    {
        // BindingSourceEditor offers "data:{id}" and needs nothing else, so
        // the id list survives the richer read rather than every caller
        // learning about paths.
        var manifest = Parse("""
            { "data": { "standards": "data/standards/", "specialty": "data/specialty.txt" } }
            """);

        Assert.Equal(new[] { "specialty" }, WorkflowManifestReader.ReadScalars(manifest).ToArray());
    }

    [Fact]
    public void ReadScalarEntries_ToleratesPascalCase()
    {
        var entry = Assert.Single(WorkflowManifestReader.ReadScalarEntries(
            Parse("""{ "Data": { "specialty": "data/specialty.txt" } }""")));

        Assert.Equal("specialty", entry.Id);
    }

    [Fact]
    public void ReadScalarEntries_IsEmptyWhenEveryEntryIsACollection()
    {
        // The shape every shipped package has today, which is why nothing has
        // exercised the value path in production.
        Assert.Empty(WorkflowManifestReader.ReadScalarEntries(
            Parse("""{ "data": { "standards": "data/standards/" } }""")));
    }

    // ----- v11 #564: macros, the deliverable's list and signed flag, reproducible -----

    [Fact]
    public void ReadMacros_ReadsTheDeclaredList_InOrder_WithLabelDefaulting()
    {
        var macros = WorkflowManifestReader.ReadMacros(Parse("""
            { "macros": [
                { "id": "disclaimer", "label": "Standing disclaimer", "file": "macros/disclaimer.md" },
                { "id": "closing", "file": "macros/closing.md" }
            ] }
            """));

        Assert.Equal(new[] { "disclaimer", "closing" }, macros.Select(m => m.Id));
        Assert.Equal("Standing disclaimer", macros[0].Label);
        Assert.Equal("closing", macros[1].Label);
        Assert.Equal("macros/closing.md", macros[1].File);
    }

    [Fact]
    public void ReadMacros_IsEmpty_WhenTheKeyIsAbsent()
    {
        Assert.Empty(WorkflowManifestReader.ReadMacros(Parse("""{ "specVersion": 10 }""")));
    }

    [Fact]
    public void ReadResults_CarriesTheMacroList_InOrder_AndTheTriStateSignature()
    {
        var results = WorkflowManifestReader.ReadResults(Parse("""
            { "results": [
                { "id": "letter", "node": "node:assemble-letter", "label": "Letter", "macros": ["disclaimer", "closing"], "signature": true },
                { "id": "summary", "node": "node:assemble-summary", "label": "Summary", "signature": false },
                { "id": "note", "node": "node:assemble-note", "label": "Note" }
            ] }
            """));

        Assert.Equal(new[] { "disclaimer", "closing" }, results[0].Macros!.Select(e => e.Id));
        Assert.True(results[0].Signature);
        // Presence, not truth, is refused below 11: an authored false reads
        // as false, and absence as null — the carried-as-read discipline.
        Assert.False(results[1].Signature);
        Assert.Null(results[1].Macros);
        Assert.Null(results[2].Signature);
    }

    [Fact]
    public void ReadNodes_ReadsReproducible_FalseWhenAbsent()
    {
        var nodes = WorkflowManifestReader.ReadNodes(Parse("""
            { "nodes": [
                { "id": "extract", "label": "Extract", "prompt": "extract", "bindings": {}, "reproducible": true },
                { "id": "draft", "label": "Draft", "prompt": "draft", "bindings": {} }
            ] }
            """));

        Assert.True(nodes[0].Reproducible);
        Assert.False(nodes[1].Reproducible);
    }

    [Fact]
    public void TheV11Keys_TolerateTheServersPascalCasing()
    {
        var macros = WorkflowManifestReader.ReadMacros(Parse("""
            { "Macros": [ { "Id": "closing", "Label": "Closing", "File": "macros/closing.md" } ] }
            """));
        Assert.Equal("closing", Assert.Single(macros).Id);

        var results = WorkflowManifestReader.ReadResults(Parse("""
            { "Results": [ { "Id": "letter", "Node": "node:a", "Label": "L", "Macros": ["closing"], "Signature": true } ] }
            """));
        Assert.Equal(new[] { "closing" }, results[0].Macros!.Select(e => e.Id));
        Assert.True(results[0].Signature);

        var nodes = WorkflowManifestReader.ReadNodes(Parse("""
            { "Nodes": [ { "Id": "extract", "Label": "E", "Prompt": "p", "Bindings": {}, "Reproducible": true } ] }
            """));
        Assert.True(nodes[0].Reproducible);
    }
}
