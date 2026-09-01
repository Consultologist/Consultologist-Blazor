using System.Text.Json;
using Consultologist.Api.Forms;

namespace Consultologist.Api.Tests;

/// <summary>
/// #539: the intake door's pure seams — every refusal names the field and
/// the rule; values are strings as sent, never validated against
/// declarations; the wire's numeric responseId lands as its invariant text.
/// </summary>
public class FormsIntakeTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static FormsIntake.FormResponseSubmission Valid(Dictionary<string, string>? inputs = null) =>
        new("triage-intake", Json("17"), new DateTimeOffset(2026, 9, 1, 14, 2, 11, TimeSpan.Zero),
            inputs ?? new Dictionary<string, string> { ["consult_draft"] = "The referral text.", ["urgent"] = "Yes" });

    [Fact]
    public void TheWireResponseId_LandsAsNumberOrString_AndNothingElse()
    {
        Assert.Equal("17", FormsIntake.NormalizeResponseId(Json("17")));
        Assert.Equal("17", FormsIntake.NormalizeResponseId(Json("\"17\"")));
        Assert.Null(FormsIntake.NormalizeResponseId(Json("true")));
        Assert.Null(FormsIntake.NormalizeResponseId(Json("{}")));
        Assert.Null(FormsIntake.NormalizeResponseId(null));
    }

    [Fact]
    public void AValidSubmission_Passes()
    {
        Assert.Null(FormsIntake.ValidateSubmission(Valid(), "17"));
    }

    [Theory]
    [InlineData(null, "17", "formId is required.")]
    [InlineData("", "17", "formId is required.")]
    [InlineData("has/slash", "17", "formId may carry letters, digits, '.', '_' and '-' only.")]
    [InlineData("triage-intake", null, "responseId is required.")]
    public void IdRefusals_NameTheFieldAndTheRule(string? formId, string? responseId, string expected)
    {
        var submission = Valid() with { FormId = formId };

        Assert.Equal(expected, FormsIntake.ValidateSubmission(submission, responseId));
    }

    [Fact]
    public void AnOverlongId_IsRefusedByLength()
    {
        var submission = Valid() with { FormId = new string('a', FormsIntake.MaxIdLength + 1) };

        Assert.Equal($"formId is longer than {FormsIntake.MaxIdLength} characters.", FormsIntake.ValidateSubmission(submission, "17"));
    }

    [Fact]
    public void MissingParts_AreRefusedByName()
    {
        Assert.Equal("Request body is required.", FormsIntake.ValidateSubmission(null, null));
        Assert.Equal("submittedAtUtc is required, as an ISO-8601 instant.",
            FormsIntake.ValidateSubmission(Valid() with { SubmittedAtUtc = null }, "17"));
        Assert.Equal("inputs is required and must carry at least one value.",
            FormsIntake.ValidateSubmission(Valid() with { Inputs = new Dictionary<string, string>() }, "17"));
    }

    [Fact]
    public void InputCaps_RefuseNeverTruncate()
    {
        var tooMany = Enumerable.Range(0, FormsIntake.MaxInputs + 1)
            .ToDictionary(i => $"input_{i}", _ => "x");
        Assert.Equal($"inputs may carry at most {FormsIntake.MaxInputs} values.",
            FormsIntake.ValidateSubmission(Valid(tooMany), "17"));

        var tooLong = new Dictionary<string, string> { ["consult_draft"] = new string('x', FormsIntake.MaxValueLength + 1) };
        Assert.Equal($"input 'consult_draft' is longer than {FormsIntake.MaxValueLength} characters.",
            FormsIntake.ValidateSubmission(Valid(tooLong), "17"));

        var badId = new Dictionary<string, string> { ["bad id"] = "x" };
        Assert.Equal("an input id may carry letters, digits, '.', '_' and '-' only.",
            FormsIntake.ValidateSubmission(Valid(badId), "17"));
    }

    [Fact]
    public void FreeTextValues_AreNeverValidatedAgainstDeclarations()
    {
        // E2: an "Other" answer arrives as free text, indistinguishable from
        // a declared option — any string is a value.
        var freeText = new Dictionary<string, string> { ["urgent"] = "Something the form never declared" };

        Assert.Null(FormsIntake.ValidateSubmission(Valid(freeText), "17"));
    }

    [Fact]
    public void TheListRow_CarriesIdsAndDays_NeverAValue()
    {
        var row = new FormResponseRow(
            "user-1", "triage-intake", "17", new DateTimeOffset(2026, 9, 1, 14, 2, 11, TimeSpan.Zero),
            new[] { "consult_draft", "urgent" }, "org-form-responses", "user-1/triage-intake-17.json",
            DeletedAtUtc: null);

        var serialized = JsonSerializer.Serialize(FormsIntake.ResponseOf(row));

        Assert.Contains("\"triage-intake\"", serialized);
        Assert.Contains("\"consult_draft\"", serialized);
        // The pointer and the account never reach the wire.
        Assert.DoesNotContain("org-form-responses", serialized);
        Assert.DoesNotContain("user-1", serialized);
    }
}
