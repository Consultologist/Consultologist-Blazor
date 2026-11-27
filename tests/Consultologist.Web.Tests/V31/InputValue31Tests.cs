using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Web.Services.Workflow.V31;
using Consultologist.Web.Shared.Create31;

namespace Consultologist.Web.Tests.V31;

/// <summary>Task 9 (#1509): a 31 input's value as the Create page holds it, and what it sends (D3).</summary>
public class InputValue31Tests
{
    private static InputShape Shape(string json) => InputShapes.Of(JsonDocument.Parse(json).RootElement.Clone(), null, null);

    private const string Medicine =
        """{"type":"object","properties":{"name":{"type":"string","title":"Name"},"dose":{"type":["number","null"],"title":"Dose"},"daily":{"type":"boolean","title":"Daily"}},"required":["name","dose","daily"]}""";

    [Fact]
    public void ABlankScalar_IsNull_NeverEmptyText()
    {
        var value = InputValue31.For(Shape("""{"type":"string"}"""));
        Assert.Null(value.ToJson());
        value.Text = "   ";
        Assert.Null(value.ToJson());
        value.Text = "Dr A. Sample";
        Assert.Equal("\"Dr A. Sample\"", value.ToJson()!.ToJsonString());
    }

    [Fact]
    public void AGroup_WritesEveryProperty_InFieldOrder_WithNullForABlank()
    {
        var shape = Shape(Medicine);
        var value = InputValue31.For(shape);
        Assert.Null(value.ToJson());

        value.Fields[0].Text = "Letrozole";
        Assert.Equal("""{"name":"Letrozole","dose":null,"daily":null}""", value.ToJson()!.ToJsonString());
        Assert.Equal("Daily: Required.", value.Problem(shape, required: false));

        value.Fields[2].Flag = false;
        value.Fields[1].Text = "2.50";
        Assert.Equal("""{"name":"Letrozole","dose":2.50,"daily":false}""", value.ToJson()!.ToJsonString());
        Assert.Null(value.Problem(shape, required: true));
    }

    [Fact]
    public void ABlankRequiredValue_SaysRequired_AndABlankOptionalOneSaysNothing()
    {
        var shape = Shape(Medicine);
        Assert.Equal("Required.", InputValue31.For(shape).Problem(shape, required: true));
        Assert.Null(InputValue31.For(shape).Problem(shape, required: false));
    }

    [Fact]
    public void Rows_AreAnArray_AndAnEmptyRowIsAProblem()
    {
        var shape = Shape("""{"type":"array","items":{"type":"integer"}}""");
        var value = InputValue31.For(shape);
        Assert.Null(value.ToJson());

        value.AddRow().Text = "3";
        value.AddRow();
        Assert.Equal("Row 2 is empty; fill it in or remove it.", value.Problem(shape, required: true));
        value.Rows[1].Text = "1.5";
        Assert.Equal("Row 2: Must be a whole number, like 12.", value.Problem(shape, required: true));
        value.Rows[1].Text = "4";
        value.MoveRow(1, -1);
        Assert.Equal("[4,3]", value.ToJson()!.ToJsonString());
        value.RemoveRow(0);
        Assert.Equal("[3]", value.ToJson()!.ToJsonString());
    }

    [Fact]
    public void Restore_ReadsBackWhatToJsonWrote()
    {
        var shape = Shape("""{"type":"array","items":""" + Medicine + "}");
        var value = InputValue31.For(shape);
        value.Restore(JsonNode.Parse("""[{"name":"Letrozole","dose":null,"daily":true},{"name":"Anastrozole","dose":1,"daily":false}]"""));

        Assert.Equal("""[{"name":"Letrozole","dose":null,"daily":true},{"name":"Anastrozole","dose":1,"daily":false}]""", value.ToJson()!.ToJsonString());
        value.Clear();
        Assert.Null(value.ToJson());
    }

    [Fact]
    public void AShapeWithAnUnsupportedPart_IsNotSupported()
    {
        Assert.True(InputValue31.IsSupported(Shape(Medicine)));
        Assert.False(InputValue31.IsSupported(Shape("""{"type":"object","properties":{"a":{"$ref":"urn:nowhere"}},"required":["a"]}""")));
        Assert.False(InputValue31.IsSupported(Shape("""{"type":"array","items":{"type":["string","number"]}}""")));
    }
}
