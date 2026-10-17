using System.Text.Json;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #540, spike § 4.2: the coercion table — every held answer either becomes
/// the typed value the setup form would have produced, or is named, never
/// filled with something the declaration refuses. E2's lesson: a choice
/// answer is untrusted text.
/// </summary>
public class FormResponseCoercionTests
{
    private static WorkflowDeclarationNode Node(string type, IReadOnlyList<string>? values = null, string? itemsType = null) =>
        new(type, "Input", Required: true,
            itemsType == null ? null : new WorkflowDeclarationNode(itemsType, "Element", false, null, null, null),
            Fields: null, Values: values, Id: "input-1");

    [Theory]
    [InlineData("text", "The referral text.")]
    [InlineData("date", "2026-08-29")]
    public void TextAndDate_LandAsTheString(string type, string held)
    {
        var (value, misfit) = FormResponseCoercion.Coerce(Node(type), held);

        Assert.Null(misfit);
        Assert.Equal(held, value!.Text);
    }

    [Fact]
    public void AnEnumAnswer_FillsOnlyWhenAmongTheDeclaredValues()
    {
        var node = Node("enum", new[] { "Routine", "Urgent" });

        var (value, misfit) = FormResponseCoercion.Coerce(node, "Urgent");
        Assert.Null(misfit);
        Assert.Equal("Urgent", value!.Text);

        // E2: an *Other* answer arrives as free text indistinguishable from
        // an option — named, not filled.
        var (other, otherMisfit) = FormResponseCoercion.Coerce(node, "As soon as the family arrives");
        Assert.Null(other);
        Assert.Equal("is not one of the declared values", otherMisfit);

        // Membership is ordinal, the starter's own comparison — case matters.
        var (cased, casedMisfit) = FormResponseCoercion.Coerce(node, "urgent");
        Assert.Null(cased);
        Assert.NotNull(casedMisfit);
    }

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("yes", true)]
    [InlineData("TRUE", true)]
    [InlineData("No", false)]
    [InlineData("false", false)]
    public void ABoolean_ReadsTheFourWords_CaseInsensitively(string held, bool expected)
    {
        var (value, misfit) = FormResponseCoercion.Coerce(Node("boolean"), held);

        Assert.Null(misfit);
        Assert.Equal(expected, value!.Flag);
    }

    [Fact]
    public void ABoolean_RefusesAnyOtherWord_ByName()
    {
        var (value, misfit) = FormResponseCoercion.Coerce(Node("boolean"), "probably");

        Assert.Null(value);
        Assert.Equal("is not Yes, No, true or false", misfit);
    }

    [Fact]
    public void ANumber_KeepsItsSpelling_OrIsNamed()
    {
        var (value, misfit) = FormResponseCoercion.Coerce(Node("number"), "1.50");
        Assert.Null(misfit);
        Assert.Equal("1.50", value!.Number);

        var (bad, badMisfit) = FormResponseCoercion.Coerce(Node("number"), "forty-two");
        Assert.Null(bad);
        Assert.Equal("is not a plain decimal number", badMisfit);
    }

    [Fact]
    public void AnArrayOfText_ReadsTheJsonArrayString_OrASingleStringAsOneElement()
    {
        var node = Node("array", itemsType: "text");

        // E2's multiple-choice wire form.
        var (value, misfit) = FormResponseCoercion.Coerce(node, "[\"A\",\"C\"]");
        Assert.Null(misfit);
        Assert.Equal(new[] { "A", "C" }, value!.Elements!.Select(e => e.Text));

        var (single, singleMisfit) = FormResponseCoercion.Coerce(node, "just one answer");
        Assert.Null(singleMisfit);
        Assert.Equal(new[] { "just one answer" }, single!.Elements!.Select(e => e.Text));

        // Not-quite-JSON falls back to one element, never an error.
        var (bracket, bracketMisfit) = FormResponseCoercion.Coerce(node, "[unfinished");
        Assert.Null(bracketMisfit);
        Assert.Single(bracket!.Elements!);
    }

    [Fact]
    public void AnEmptyAnswer_MeansNotSupplied_NeverAMisfit()
    {
        Assert.Equal((null, null), FormResponseCoercion.Coerce(Node("text"), ""));
        Assert.Equal((null, null), FormResponseCoercion.Coerce(Node("boolean"), "  "));
        // An empty JSON array likewise: nothing chosen is nothing supplied.
        Assert.Equal((null, null), FormResponseCoercion.Coerce(Node("array", itemsType: "text"), "[]"));
    }

    [Fact]
    public void StructuresAreAbsentFromTheStringTable_ByDesign()
    {
        // The STRING table (a flow's answer is text) fills no structure; a
        // structure fills from a JSON value of a payload v2 answer (below).
        var (obj, objMisfit) = FormResponseCoercion.Coerce(Node("object"), "{\"a\":1}");
        Assert.Null(obj);
        Assert.Equal("cannot be filled from a form answer", objMisfit);

        var (arr, arrMisfit) = FormResponseCoercion.Coerce(Node("array", itemsType: "object"), "[\"x\"]");
        Assert.Null(arr);
        Assert.Equal("cannot be filled from a form answer", arrMisfit);
    }

    // ----- v25 (B6 #1005): a JSON answer, the payload v2 shape -----

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static WorkflowDeclarationNode Intake() => new("object", "Intake", true, null,
        new[]
        {
            new WorkflowDeclarationNode("array", "Allergies", true, new WorkflowDeclarationNode("text", "Allergy", false, null, null, null), null, null, "allergies"),
            new WorkflowDeclarationNode("boolean", "On letrozole", true, null, null, null, "on_letrozole"),
            new WorkflowDeclarationNode("date", "Last visit", false, null, null, null, "last_visit"),
            new WorkflowDeclarationNode("object", "Address", false, null,
                new[] { new WorkflowDeclarationNode("text", "City", true, null, null, null, "city") }, null, "address"),
        }, null, "intake");

    [Fact]
    public void AJsonString_GoesThroughTheStringTable()
    {
        var node = Node("enum", new[] { "Routine", "Urgent" });

        Assert.Equal("Urgent", FormResponseCoercion.Coerce(node, Json("\"Urgent\"")).Value!.Text);
        Assert.Equal("is not one of the declared values", FormResponseCoercion.Coerce(node, Json("\"urgent\"")).Misfit);
        Assert.Equal((null, null), FormResponseCoercion.Coerce(Node("text"), Json("\"\"")));
        Assert.Equal((null, null), FormResponseCoercion.Coerce(Node("text"), Json("null")));
    }

    [Fact]
    public void AJsonNumber_FillsANumber_OrLandsAsItsSpellingInText()
    {
        Assert.Equal("1.50", JsonSerializer.Serialize(FormResponseCoercion.Coerce(Node("number"), Json("1.50")).Value).Trim('"'));
        Assert.Equal("7", FormResponseCoercion.Coerce(Node("text"), Json("7")).Value!.Text);
        Assert.Equal("2026", FormResponseCoercion.Coerce(Node("date"), Json("2026")).Value!.Text);
        Assert.Equal("is a JSON number; the input is an object", FormResponseCoercion.Coerce(Intake(), Json("7")).Misfit);
    }

    [Fact]
    public void AJsonBoolean_FillsABoolean_AndIsNamedElsewhere()
    {
        Assert.True(FormResponseCoercion.Coerce(Node("boolean"), Json("true")).Value!.Flag);
        Assert.Equal("false", FormResponseCoercion.Coerce(Node("text"), Json("false")).Value!.Text);
        Assert.Equal("is not a plain decimal number", FormResponseCoercion.Coerce(Node("number"), Json("true")).Misfit);
        Assert.Equal("is not Yes, No, true or false", FormResponseCoercion.Coerce(Node("boolean"), Json("1")).Misfit);
        Assert.Equal("is a JSON boolean; the input is an array", FormResponseCoercion.Coerce(Node("array", itemsType: "text"), Json("true")).Misfit);
    }

    [Fact]
    public void AJsonArray_FillsElementByElement_AgainstTheItems()
    {
        var numbers = FormResponseCoercion.Coerce(Node("array", itemsType: "number"), Json("[1, 2.5]"));
        Assert.Null(numbers.Misfit);
        Assert.Equal(2, numbers.Value!.Elements!.Count);

        Assert.Equal("element 1 is not a plain decimal number", FormResponseCoercion.Coerce(Node("array", itemsType: "number"), Json("[1, \"x\"]")).Misfit);
        Assert.Equal("element 0 is null", FormResponseCoercion.Coerce(Node("array", itemsType: "text"), Json("[null]")).Misfit);
        Assert.Equal((null, null), FormResponseCoercion.Coerce(Node("array", itemsType: "text"), Json("[]")));
        Assert.Equal("is a JSON array; the input is a text", FormResponseCoercion.Coerce(Node("text"), Json("[\"a\"]")).Misfit);
    }

    [Fact]
    public void AJsonObject_FillsTheDeclaredFields_InDeclaredOrder()
    {
        var (value, misfit) = FormResponseCoercion.Coerce(Intake(), Json("""{ "on_letrozole": true, "allergies": ["penicillin", "latex"], "address": { "city": "Halifax" } }"""));

        Assert.Null(misfit);
        Assert.Equal(new[] { "allergies", "on_letrozole", "address" }, value!.Fields!.Select(f => f.Id));
        Assert.Equal("""{"allergies":["penicillin","latex"],"on_letrozole":true,"address":{"city":"Halifax"}}""", value.AsJson());
    }

    [Fact]
    public void AMissingRequiredField_IsNamed_AndABlankOptionalOne_IsOmitted()
    {
        Assert.Equal("is missing required field 'on_letrozole'", FormResponseCoercion.Coerce(Intake(), Json("""{ "allergies": ["latex"] }""")).Misfit);

        var (value, misfit) = FormResponseCoercion.Coerce(Intake(), Json("""{ "allergies": ["latex"], "on_letrozole": "No", "last_visit": "" }"""));
        Assert.Null(misfit);
        Assert.Equal(new[] { "allergies", "on_letrozole" }, value!.Fields!.Select(f => f.Id));
    }

    [Fact]
    public void AnUndeclaredMember_ANullMember_AndAMisfitField_AreNamed()
    {
        Assert.Equal("has a field 'weight' it does not declare (fields: allergies, on_letrozole, last_visit, address)",
            FormResponseCoercion.Coerce(Intake(), Json("""{ "weight": 70 }""")).Misfit);
        Assert.Equal("field 'last_visit' is null; omit an optional field instead",
            FormResponseCoercion.Coerce(Intake(), Json("""{ "allergies": ["x"], "on_letrozole": true, "last_visit": null }""")).Misfit);
        Assert.Equal("field 'on_letrozole' is not Yes, No, true or false",
            FormResponseCoercion.Coerce(Intake(), Json("""{ "allergies": ["x"], "on_letrozole": "maybe" }""")).Misfit);
        Assert.Equal("field 'address' is missing required field 'city'",
            FormResponseCoercion.Coerce(Intake(), Json("""{ "allergies": ["x"], "on_letrozole": true, "address": {} }""")).Misfit);
        Assert.Equal("is a JSON object; the input is a text", FormResponseCoercion.Coerce(Node("text"), Json("{}")).Misfit);
    }

    [Fact]
    public void CoerceDeclared_TriesEachUnionArm()
    {
        var union = new WorkflowInputSpec("when", "When", Type: new WorkflowTypeSet(new[] { "date", "text" }));

        Assert.Equal("2026-08-29", FormResponseCoercion.CoerceDeclared(union, Json("\"2026-08-29\"")).Value!.Text);
        Assert.Equal("is a JSON array; the input is a text", FormResponseCoercion.CoerceDeclared(union, Json("[1]")).Misfit);

        var single = new WorkflowInputSpec("n", "N", Type: new WorkflowTypeSet(new[] { "number" }));
        Assert.Equal("is not a plain decimal number", FormResponseCoercion.CoerceDeclared(single, Json("\"x\"")).Misfit);
    }
}
