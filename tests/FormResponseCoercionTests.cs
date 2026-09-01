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
    public void StructuresAreAbsentFromTheTable_ByDesign()
    {
        var (obj, objMisfit) = FormResponseCoercion.Coerce(Node("object"), "{\"a\":1}");
        Assert.Null(obj);
        Assert.Equal("cannot be filled from a form answer", objMisfit);

        var (arr, arrMisfit) = FormResponseCoercion.Coerce(Node("array", itemsType: "object"), "[\"x\"]");
        Assert.Null(arr);
        Assert.Equal("cannot be filled from a form answer", arrMisfit);
    }
}
