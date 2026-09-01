using ApiCoercion = Consultologist.PackageFormat.FormResponseCoercion;
using ApiNode = Consultologist.PackageFormat.WorkflowDeclarationNode;
using WebCoercion = Consultologist.Web.Services.AI.FormResponseCoercion;

namespace Consultologist.Web.Tests;

/// <summary>
/// #540: the client fills the setup form from a held answer; the server
/// re-runs the same coercion at start to verify the origin — a disagreement
/// would make the server refuse what the client filled. This wire table
/// runs every row through both hand-written copies and holds them identical:
/// same accept/refuse, same misfit phrase, same canonical wire JSON.
/// </summary>
public class FormResponseCoercionMirrorTests
{
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
        { "object", null, null, "{\"a\":1}" },
        { "text", null, null, "" },
        { "boolean", null, null, "   " },
    };

    [Theory]
    [MemberData(nameof(WireTable))]
    public void BothCopies_AgreeOnEveryRow(string type, string[]? values, string? itemsType, string held)
    {
        var apiNode = new ApiNode(type, "Input", true,
            itemsType == null ? null : new ApiNode(itemsType, "Element", false, null, null, null),
            null, values, "input-1");

        var (apiValue, apiMisfit) = ApiCoercion.Coerce(apiNode, held);
        var (webValue, webMisfit) = WebCoercion.Coerce(type, values, itemsType, held);

        Assert.Equal(apiMisfit, webMisfit);
        Assert.Equal(apiValue is null, webValue is null);

        if (apiValue != null)
        {
            // The wire form is the arbiter: each side serializes through its
            // own converter, and the bytes must agree.
            Assert.Equal(
                System.Text.Json.JsonSerializer.Serialize(apiValue),
                System.Text.Json.JsonSerializer.Serialize(webValue));
        }
    }
}
