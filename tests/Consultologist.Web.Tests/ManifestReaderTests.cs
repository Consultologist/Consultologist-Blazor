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
}
