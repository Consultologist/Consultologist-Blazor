using System.Text.Json;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Services.Workflow.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>Task 8 (#1509): an input's widget is read from its format (part 02 § 8.1, part 09 § 5.1).</summary>
public class InputShapeTests
{
    private const string P = "urn:consultologist:spec:31:format:";
    private const string Pkg = "urn:consultologist:package:example@v1:format:";

    private static readonly string PlainDef = """{"$id":"@P@plaintext","contentMediaType":"text/plain","type":"string"}""";
    private static readonly string MarkdownDef = """{"$id":"@P@markdown_block","contentMediaType":"text/markdown","type":"string","x-markdown":"block"}""";
    private static readonly string DateDef = """{"$id":"@P@date","format":"date","type":"string"}""";

    private static JsonElement Json(string json) => JsonDocument.Parse(Expand(json)).RootElement.Clone();

    private static string Expand(string json) => json
        .Replace("@PlainDef@", PlainDef).Replace("@MarkdownDef@", MarkdownDef).Replace("@DateDef@", DateDef)
        .Replace("@Pkg@", Pkg).Replace("@P@", P);

    private static InputShape Of(string json, IReadOnlyList<WorkflowPackageFieldResponse>? fields = null, WorkflowPackageElementResponse? items = null)
        => InputShapes.Of(Json(json), fields, items);

    private static string Ref(string id) => "{\"$ref\":\"" + id + "\"}";

    [Fact]
    public void Plaintext_IsAnArea() =>
        Assert.Equal(InputWidget.Area, Of("""{"$ref":"@P@plaintext","$defs":{"plaintext":@PlainDef@}}""").Widget);

    [Fact]
    public void MarkdownBlock_IsMarkdown() =>
        Assert.Equal(InputWidget.Markdown, Of("""{"$ref":"@P@markdown_block","$defs":{"m":@MarkdownDef@}}""").Widget);

    [Fact]
    public void Date_IsADate() =>
        Assert.Equal(InputWidget.Date, Of("""{"$ref":"@P@date","$defs":{"d":@DateDef@}}""").Widget);

    [Fact]
    public void ALabelledChoice_KeepsWrittenOrder()
    {
        var shape = Of("""{"oneOf":[{"const":"oral","title":"By mouth"},{"const":"iv","title":"Intravenous"},{"const":"im","title":"Injection"}],"type":"string"}""");
        Assert.Equal(InputWidget.Select, shape.Widget);
        Assert.Equal(
            [new InputChoice("oral", "By mouth"), new InputChoice("iv", "Intravenous"), new InputChoice("im", "Injection")],
            shape.Choices);
    }

    [Fact]
    public void AnEnum_IsASelectWithBareValues()
    {
        var shape = Of("""{"enum":["left","right"],"type":"string"}""");
        Assert.Equal(InputWidget.Select, shape.Widget);
        Assert.Equal([new InputChoice("left", "left"), new InputChoice("right", "right")], shape.Choices);
    }

    [Fact]
    public void ANullableEnum_DropsTheNullChoice()
    {
        var shape = Of("""{"enum":["left","right",null],"type":["string","null"]}""");
        Assert.True(shape.Nullable);
        Assert.Equal(["left", "right"], shape.Choices.Select(c => c.Value));
    }

    [Fact]
    public void OtherStrings_AreALine()
    {
        Assert.Equal(InputWidget.Line, Of("""{"type":"string"}""").Widget);
        Assert.Equal(InputWidget.Line, Of("""{"$ref":"@P@string","$defs":{"s":{"$id":"@P@string","type":"string"}}}""").Widget);
    }

    [Theory]
    [InlineData("boolean", InputWidget.Boolean)]
    [InlineData("number", InputWidget.Number)]
    [InlineData("integer", InputWidget.Integer)]
    public void Scalars_AreTheirWidget(string type, InputWidget widget) =>
        Assert.Equal(widget, Of("{\"type\":\"" + type + "\"}").Widget);

    [Fact]
    public void ATitle_IsKept() => Assert.Equal("Visit", Of("""{"type":"string","title":"Visit"}""").Title);

    [Fact]
    public void ANullableType_SetsNullable_AndIsReadAsItsOtherType()
    {
        var shape = Of("""{"type":["number","null"]}""");
        Assert.Equal(InputWidget.Number, shape.Widget);
        Assert.True(shape.Nullable);
        Assert.False(Of("""{"type":"number"}""").Nullable);
    }

    [Fact]
    public void AGroup_TakesItsFieldsFromFields_InWrittenOrder_WithLabels()
    {
        // Properties are sorted (canonical); fields carry the written order.
        const string schema = """
            {"type":"object","properties":{"alpha":{"type":"string"},"beta":{"type":"number"}},"required":["alpha","beta"]}
            """;
        var fields = new[]
        {
            new WorkflowPackageFieldResponse("beta", "Second one", true, "number"),
            new WorkflowPackageFieldResponse("alpha", "First one", true, "text"),
        };
        var shape = Of(schema, fields);
        Assert.Equal(InputWidget.Group, shape.Widget);
        Assert.Equal(["beta", "alpha"], shape.Fields.Select(f => f.Id));
        Assert.Equal(["Second one", "First one"], shape.Fields.Select(f => f.Label));
        Assert.Equal([InputWidget.Number, InputWidget.Line], shape.Fields.Select(f => f.Shape.Widget));
    }

    [Fact]
    public void ANullableField_IsNotRequired()
    {
        const string schema = """{"type":"object","properties":{"dose":{"type":["number","null"]},"name":{"type":"string"}},"required":["dose","name"]}""";
        var fields = new[]
        {
            new WorkflowPackageFieldResponse("dose", "Dose", false, "number"),
            new WorkflowPackageFieldResponse("name", "Name", true, "text"),
        };
        var shape = Of(schema, fields);
        Assert.False(shape.Fields[0].Required);
        Assert.True(shape.Fields[0].Shape.Nullable);
        Assert.True(shape.Fields[1].Required);
    }

    [Fact]
    public void AnArray_IsRows_WithItsItemShape()
    {
        var shape = Of("""{"type":"array","items":{"$ref":"@P@date"},"$defs":{"d":@DateDef@}}""");
        Assert.Equal(InputWidget.Rows, shape.Widget);
        Assert.Equal(InputWidget.Date, shape.Items!.Widget);
    }

    [Fact]
    public void AReference_IsResolvedByItsId_AtAnyDepth()
    {
        var json = """
            {"$ref":"@Pkg@visit","$defs":{
              "visit":{"$id":"@Pkg@visit","type":"object","properties":{"seen":{"$ref":"@P@date","title":"Date seen"},"notes":{"$ref":"@P@plaintext"}},"required":["seen","notes"]},
              "date":@DateDef@,"plaintext":@PlainDef@}}
            """;
        var fields = new[]
        {
            new WorkflowPackageFieldResponse("seen", "Date seen", true, "date"),
            new WorkflowPackageFieldResponse("notes", "notes", true, "text"),
        };
        var shape = Of(json, fields);
        Assert.Equal(InputWidget.Group, shape.Widget);
        Assert.Equal([InputWidget.Date, InputWidget.Area], shape.Fields.Select(f => f.Shape.Widget));
        Assert.Equal("Date seen", shape.Fields[0].Shape.Title);
    }

    [Fact]
    public void AGroupInsideRowsInsideAGroup_IsRead()
    {
        var json = """
            {"type":"object","properties":{"meds":{"type":"array","title":"Medicines","items":{"type":"object","properties":{"name":{"$ref":"@P@plaintext"},"dose":{"type":["number","null"]}},"required":["name","dose"]}}},
             "required":["meds"],"$defs":{"p":@PlainDef@}}
            """;
        var inner = new[]
        {
            new WorkflowPackageFieldResponse("name", "Drug", true, "text"),
            new WorkflowPackageFieldResponse("dose", "Dose", false, "number"),
        };
        var fields = new[]
        {
            new WorkflowPackageFieldResponse("meds", "Medicines", true, "array", Items: new WorkflowPackageElementResponse("object", Fields: inner)),
        };
        var shape = Of(json, fields);
        var rows = shape.Fields.Single().Shape;
        Assert.Equal(InputWidget.Rows, rows.Widget);
        var item = rows.Items!;
        Assert.Equal(InputWidget.Group, item.Widget);
        Assert.Equal(["name", "dose"], item.Fields.Select(f => f.Id));
        Assert.Equal(["Drug", "Dose"], item.Fields.Select(f => f.Label));
        Assert.Equal([InputWidget.Area, InputWidget.Number], item.Fields.Select(f => f.Shape.Widget));
        Assert.True(item.Fields[1].Shape.Nullable);
    }

    [Fact]
    public void AnUnresolvableReference_IsUnsupported_AndNeverThrows()
    {
        Assert.Equal(InputWidget.Unsupported, Of(Ref("urn:nowhere:format:x")).Widget);
        Assert.Equal(InputWidget.Unsupported, Of("""{"$ref":"urn:nowhere","$defs":{"a":{"$id":"@P@date","type":"string"}}}""").Widget);
        Assert.Equal(InputWidget.Unsupported, Of("""{"$ref":5}""").Widget);
        Assert.Equal(InputWidget.Unsupported, Of("""[1,2]""").Widget);
        Assert.Equal(InputWidget.Unsupported, Of("""{"type":"object","properties":{"a":{"$ref":"urn:x"}},"required":["a"]}""",
            [new WorkflowPackageFieldResponse("a", "A", true)]).Fields.Single().Shape.Widget);
    }

    [Fact]
    public void ARecursiveFormat_IsUnsupported()
    {
        Assert.Equal(InputWidget.Unsupported, Of("false").Widget);
        // A reference cycle is cut by the depth cap rather than looping.
        var loop = """{"$ref":"@Pkg@a","$defs":{"a":{"$id":"@Pkg@a","$ref":"@Pkg@a"}}}""";
        Assert.Equal(InputWidget.Unsupported, Of(loop).Widget);
    }

    [Fact]
    public void ATopLevelAnyOf_AndOtherShapes_AreUnsupported()
    {
        Assert.Equal(InputWidget.Unsupported, Of("""{"anyOf":[{"type":"string"},{"type":"number"}]}""").Widget);
        Assert.Equal(InputWidget.Unsupported, Of("""{"type":["string","number"]}""").Widget);
        Assert.Equal(InputWidget.Unsupported, Of("""{"type":"null"}""").Widget);
        Assert.Equal(InputWidget.Unsupported, Of("""{}""").Widget);
    }

    // Task 9: part 02 § 3.2 makes a reference or an object nullable with anyOf it and null, and a labelled enum with a
    // { "const": null } arm; the spec's own example (part 02 § 8.4, "notes") is such a field.
    [Fact]
    public void ANullableReference_IsItsTargetsWidget_Nullable_WithItsOwnTitle()
    {
        var shape = Of("""{"anyOf":[{"$ref":"@P@markdown_block"},{"type":"null"}],"title":"Notes for this section","$defs":{"m":@MarkdownDef@}}""");
        Assert.Equal(InputWidget.Markdown, shape.Widget);
        Assert.True(shape.Nullable);
        Assert.Equal("Notes for this section", shape.Title);

        var group = Of("""{"anyOf":[{"type":"object","properties":{"a":{"type":"string"}},"required":["a"]},{"type":"null"}]}""");
        Assert.Equal(InputWidget.Group, group.Widget);
        Assert.True(group.Nullable);
    }

    [Fact]
    public void ANullableDate_WrittenWithAnyOf_IsADate_Nullable()
    {
        var shape = Of("""{"anyOf":[{"$ref":"@P@date"},{"type":"null"}],"$defs":{"d":@DateDef@}}""");
        Assert.Equal(InputWidget.Date, shape.Widget);
        Assert.True(shape.Nullable);
    }

    [Fact]
    public void ANullableFieldWrittenWithAnyOf_IsNotRequired()
    {
        const string schema = """{"type":"object","properties":{"notes":{"anyOf":[{"$ref":"@P@plaintext"},{"type":"null"}]}},"required":["notes"],"$defs":{"p":@PlainDef@}}""";
        var field = Of(schema, [new WorkflowPackageFieldResponse("notes", "Notes", true, "text")]).Fields.Single();
        Assert.False(field.Required);
        Assert.Equal(InputWidget.Area, field.Shape.Widget);
    }

    [Fact]
    public void ANullableLabelledChoice_DropsItsNullArm()
    {
        var shape = Of("""{"type":["string","null"],"oneOf":[{"const":"oral","title":"By mouth"},{"const":"iv","title":"Intravenous"},{"const":null}]}""");
        Assert.Equal(InputWidget.Select, shape.Widget);
        Assert.True(shape.Nullable);
        Assert.Equal(["oral", "iv"], shape.Choices.Select(c => c.Value));
    }
}
