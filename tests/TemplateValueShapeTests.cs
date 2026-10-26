using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// A2 (#997): one shape over an input's declaration and a format's schema, so
/// the walker types <c>input.x.a.b</c> and <c>node.x.value.a.b</c> paths the
/// same way.
/// </summary>
public class TemplateValueShapeTests
{
    [Fact]
    public void FromSchema_FoldsObjectsArraysAndScalars()
    {
        var schema = JsonNode.Parse("""
            { "type": "object", "additionalProperties": false, "required": ["concepts", "pass"],
              "properties": {
                "concepts": { "type": "array", "items": { "type": "object", "properties": { "term": { "type": "string" }, "count": { "type": "integer" } } } },
                "pass": { "type": "boolean" },
                "note": { "type": ["string", "null"] },
                "kind": { "enum": ["a", "b"] },
                "anything": {} } }
            """);

        var shape = TemplateValueShape.FromSchema(schema);

        Assert.True(shape.IsObject);
        Assert.Equal(5, shape.Fields!.Count);
        var concepts = shape.Fields["concepts"];
        Assert.True(concepts.IsArray);
        Assert.True(concepts.Items!.IsObject);
        Assert.Equal("text", concepts.Items.Fields!["term"].Kind);
        Assert.Equal("number", concepts.Items.Fields["count"].Kind);
        Assert.Equal("boolean", shape.Fields["pass"].Kind);
        Assert.Equal("text", shape.Fields["note"].Kind);
        Assert.Equal("enum", shape.Fields["kind"].Kind);
        Assert.Equal(new[] { "a", "b" }, shape.Fields["kind"].Values);
        Assert.True(shape.Fields["anything"].IsUnknown);
    }

    [Fact]
    public void FromSchema_WithoutAType_InfersFromStructure()
    {
        Assert.True(TemplateValueShape.FromSchema(JsonNode.Parse("""{ "properties": { "a": {} } }""")).IsObject);
        Assert.True(TemplateValueShape.FromSchema(JsonNode.Parse("""{ "items": { "type": "string" } }""")).IsArray);
        Assert.True(TemplateValueShape.FromSchema(JsonNode.Parse("{}")).IsUnknown);
        Assert.True(TemplateValueShape.FromSchema(null).IsUnknown);
    }

    [Fact]
    public void FromDeclaration_MirrorsTheInputTree()
    {
        var input = new WorkflowInputSpec("history", "History", true, "array",
            Items: new WorkflowElementSpec("object"),
            Fields: new List<WorkflowFieldSpec> { new("event", "Event", true, "text"), new("year", "Year", false, "number") });

        var shape = TemplateValueShape.FromDeclaration(WorkflowDeclarationNode.Of(input));

        Assert.True(shape.IsArray);
        Assert.True(shape.Items!.IsObject);
        Assert.Equal("text", shape.Items.Fields!["event"].Kind);
        Assert.Equal("number", shape.Items.Fields["year"].Kind);
        Assert.Equal("an array of object", shape.Describe());
    }

    [Theory]
    [InlineData("text", "a text")]
    [InlineData("object", "an object")]
    [InlineData("enum", "an enum")]
    [InlineData("unknown", "an untyped value")]
    public void Describe_NamesTheKind(string kind, string sentence)
    {
        Assert.Equal(sentence, new TemplateValueShape(kind, null, null, null).Describe());
    }

    [Theory]
    [InlineData("input", true)]
    [InlineData("node", true)]
    [InlineData("snippet", true)]
    [InlineData("item", true)]
    [InlineData("macro", true)]
    [InlineData("deliverable", true)] // #1101
    [InlineData("data", true)]
    [InlineData("bindings", true)]
    [InlineData("variables", true)]
    [InlineData("string", false)]
    [InlineData("s", false)]
    [InlineData(null, false)]
    public void ReservedObjects_AreTheFive_AndTheRetiredRoots(string? name, bool reserved)
    {
        Assert.Equal(reserved, TemplateObjects.IsReserved(name));
    }

    [Theory]
    [InlineData("text", false)]
    [InlineData("object", true)]
    [InlineData("enum", true)]
    [InlineData("array", true)]
    public void ANodeHasAValue_UnlessItsFormatIsText(string type, bool hasValue)
    {
        Assert.Equal(hasValue, new TemplateNodeShape("f", type).HasValue);
    }
}
