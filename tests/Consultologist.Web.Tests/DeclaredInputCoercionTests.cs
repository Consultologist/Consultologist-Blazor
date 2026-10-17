using System.Text.Json;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #991: the client's fill is the library's coercion over the declaration the
/// setup form was served — there is one copy now, so the old mirror table
/// (#540) proves the adapter instead of a hand copy: same accept/refuse, same
/// misfit phrase, same wire bytes as the library's own call.
/// </summary>
public class DeclaredInputCoercionTests
{
    private static readonly WorkflowPackageInputResponse Intake = new(
        "intake", "Intake", false, WorkflowInputTypes.Object,
        Fields: new[]
        {
            new WorkflowPackageFieldResponse("allergies", "Allergies", false, WorkflowInputTypes.Array, Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Text)),
            new WorkflowPackageFieldResponse("on_letrozole", "On letrozole", true, WorkflowInputTypes.Boolean),
        },
        ExpectedContent: WorkflowExpectedContent.Form);

    public static TheoryData<string, string[]?, string?, string> WireTable => new()
    {
        { "text", null, null, "The referral text." },
        { "date", null, null, "2026-08-29" },
        { "date", null, null, "not a date" },
        { "enum", new[] { "Routine", "Urgent" }, null, "Urgent" },
        { "enum", new[] { "Routine", "Urgent" }, null, "As soon as the family arrives" },
        { "enum", new[] { "Routine", "Urgent" }, null, "urgent" },
        { "boolean", null, null, "Yes" },
        { "boolean", null, null, "FALSE" },
        { "boolean", null, null, "probably" },
        { "number", null, null, "1.50" },
        { "number", null, null, "007" },
        { "array", null, "text", "[\"A\",\"C\"]" },
        { "array", null, "text", "just one answer" },
        { "array", null, "text", "[unfinished" },
        { "array", null, "text", "[]" },
        { "array", null, "object", "[\"x\"]" },
        { "text", null, null, "" },
        { "boolean", null, null, "   " },
    };

    [Theory]
    [MemberData(nameof(WireTable))]
    public void TheStringTable_StillAnswersEveryWireRow(string type, string[]? values, string? itemsType, string held)
    {
        var input = new WorkflowPackageInputResponse("input_1", "Input", true, type == "text" ? null : type,
            Values: values,
            Items: itemsType is null ? null : new WorkflowPackageElementResponse(itemsType));
        var node = new PF.WorkflowDeclarationNode(type, "Input", true,
            itemsType == null ? null : new PF.WorkflowDeclarationNode(itemsType, "Element", false, null, null, null),
            null, values, "input_1");

        var (libraryValue, libraryMisfit) = PF.FormResponseCoercion.Coerce(node, held);
        var (webValue, webMisfit) = DeclaredInputCoercion.CoerceHeldText(input, held);

        Assert.Equal(libraryMisfit, webMisfit);
        Assert.Equal(libraryValue is null, webValue is null);
        if (libraryValue != null)
        {
            Assert.Equal(JsonSerializer.Serialize(libraryValue), JsonSerializer.Serialize(webValue));
        }
    }

    [Fact]
    public void SpecOf_CarriesTheDeclarationWhole()
    {
        var input = new WorkflowPackageInputResponse("visits", "Visits", false, WorkflowInputTypes.Array,
            Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Object, Fields: new[]
            {
                new WorkflowPackageFieldResponse("date", "Date", true, WorkflowInputTypes.Date),
                new WorkflowPackageFieldResponse("kind", "Kind", false, WorkflowInputTypes.Enum, new[] { "clinic", "ward" }),
            }),
            ExpectedContent: WorkflowExpectedContent.Form,
            Types: new[] { WorkflowInputTypes.Array, WorkflowInputTypes.Text });

        var spec = DeclaredInputCoercion.SpecOf(input);

        Assert.Equal("visits", spec.Id);
        Assert.Equal(new[] { "array", "text" }, spec.Type!.Types);
        Assert.Equal("object", spec.Items!.Type);
        Assert.Equal(new[] { "date", "kind" }, spec.Items.Fields!.Select(field => field.Id));
        Assert.Equal(new[] { "clinic", "ward" }, spec.Items.Fields[1].Values);
        Assert.False(spec.Items.Fields[1].Required);
        Assert.Equal(WorkflowExpectedContent.Form, spec.ExpectedContent);
    }

    [Fact]
    public void AnObjectInput_FillsFromItsHeldObjectText()
    {
        // The forms endpoint flattens a version-2 object value to its raw JSON;
        // the client reads it back as the object the server will verify.
        var (value, misfit) = DeclaredInputCoercion.CoerceHeldText(Intake, """{ "on_letrozole": true, "allergies": ["latex"] }""");

        Assert.Null(misfit);
        Assert.Equal("""{"allergies":["latex"],"on_letrozole":true}""", JsonSerializer.Serialize(value));

        var (none, stringMisfit) = DeclaredInputCoercion.CoerceHeldText(Intake, "{ not json");
        Assert.Null(none);
        Assert.Equal("cannot be filled from a form answer", stringMisfit);
    }

    [Theory]
    [InlineData("{ not json", "is not JSON")]
    [InlineData("[1, 2]", "must be a JSON object; got a JSON array")]
    [InlineData("\"text\"", "must be a JSON object; got a JSON string")]
    [InlineData("""{ "allergies": ["latex"] }""", "is missing required field 'on_letrozole'")]
    [InlineData("""{ "allergies": ["latex"], "on_letrozole": true, "smoker": false }""", "has a field 'smoker' it does not declare (fields: allergies, on_letrozole)")]
    public void CoerceFile_RefusesByPhrase(string file, string misfit)
    {
        var (value, actual) = DeclaredInputCoercion.CoerceFile(Intake, System.Text.Encoding.UTF8.GetBytes(file));

        Assert.Null(value);
        Assert.Equal(misfit, actual);
    }

    [Fact]
    public void CoerceFile_ReadsABomPrefixedFile_AndTheValueIsTheLibrarysOnTheWire()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("""{ "on_letrozole": true, "allergies": ["latex"] }""")).ToArray();

        var (value, misfit) = DeclaredInputCoercion.CoerceFile(Intake, bytes);

        Assert.Null(misfit);
        var library = PF.FormResponseCoercion.CoerceDeclared(DeclaredInputCoercion.SpecOf(Intake), JsonDocument.Parse("""{ "on_letrozole": true, "allergies": ["latex"] }""").RootElement).Value;
        Assert.Equal(JsonSerializer.Serialize(library), JsonSerializer.Serialize(value));
        Assert.Equal(2, value!.Fields!.Count);
    }
}
