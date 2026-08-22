using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// #427: the editor reads the whole v9 condition grammar, though it composes
/// only the v8 forms. Before this, "patient.age >= 65" read as an input named
/// "patient.age >= 65" — refused at the desk, and rewritten to nothing by a
/// rename.
/// </summary>
public class WorkflowResultConditionTextTests
{
    [Theory]
    // v8, unchanged.
    [InlineData("billable", "billable")]
    [InlineData("encounter_kind == follow_up", "encounter_kind")]
    [InlineData("encounter_kind != follow_up", "encounter_kind")]
    [InlineData("  encounter_kind  ==  follow_up  ", "encounter_kind")]
    // v9.
    [InlineData("length_of_stay > 7", "length_of_stay")]
    [InlineData("length_of_stay>=7", "length_of_stay")]
    [InlineData("seen_on <= 2026-01-01", "seen_on")]
    [InlineData("patient.age >= 65", "patient")]
    [InlineData("patient.sex == female", "patient")]
    [InlineData("count(prior_notes) > 1", "prior_notes")]
    [InlineData("count( prior_notes ) == 0", "prior_notes")]
    [InlineData("prior_notes", "prior_notes")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("== follow_up", null)]
    public void InputOf_NamesTheInputUnderEveryForm(string? when, string? expected)
    {
        Assert.Equal(expected, WorkflowResultConditionText.InputOf(when));
    }

    [Theory]
    [InlineData("billable", null)]
    [InlineData("encounter_kind == follow_up", "follow_up")]
    [InlineData("consult_draft == \"urgent\"", "urgent")]
    [InlineData("length_of_stay > 7", "7")]
    [InlineData("length_of_stay >= 7.5", "7.5")]
    [InlineData("seen_on < 2026-01-01", "2026-01-01")]
    [InlineData("patient.age >= 65", "65")]
    [InlineData("count(prior_notes) > 1", "1")]
    [InlineData("encounter_kind ==", "")]
    public void LiteralOf_ReadsPastAnyOperator(string when, string? expected)
    {
        Assert.Equal(expected, WorkflowResultConditionText.LiteralOf(when));
    }

    [Theory]
    [InlineData("billable", null)]
    [InlineData("encounter_kind == follow_up", "==")]
    [InlineData("encounter_kind != follow_up", "!=")]
    [InlineData("length_of_stay > 7", ">")]
    [InlineData("length_of_stay >= 7", ">=")]
    [InlineData("length_of_stay < 7", "<")]
    [InlineData("length_of_stay <= 7", "<=")]
    public void OperatorOf_ReadsTwoCharactersBeforeOne(string when, string? expected)
    {
        // ">=" must not read as ">" with the literal "= 7".
        Assert.Equal(expected, WorkflowResultConditionText.OperatorOf(when));
        Assert.Equal(expected == "!=", WorkflowResultConditionText.IsNegated(when));
    }

    [Theory]
    [InlineData("billable", "paid")]
    [InlineData("encounter_kind == follow_up", "visit == follow_up")]
    [InlineData("encounter_kind != follow_up", "visit != follow_up")]
    [InlineData("length_of_stay > 7", "stay > 7")]
    [InlineData("patient.age >= 65", "person.age >= 65")]
    [InlineData("count(prior_notes) > 1", "count(notes) > 1")]
    [InlineData("prior_notes", "notes")]
    public void Rename_CarriesTheRestAsWritten(string when, string expected)
    {
        var newId = expected.Contains("count(") ? "notes"
            : expected.Split(new[] { ' ', '.' })[0];

        Assert.Equal(expected, WorkflowResultConditionText.Rename(when, newId));
    }

    [Theory]
    [InlineData("encounter_kind == follow_up")]
    [InlineData("encounter_kind != follow_up")]
    [InlineData("billable")]
    public void Rename_AgreesWithComposeOnTheV8Forms(string when)
    {
        // The rename cascade used Compose until #427; what it wrote for a v8
        // condition is what Rename writes.
        var composed = WorkflowResultConditionText.Compose(
            "renamed",
            WorkflowResultConditionText.IsNegated(when),
            WorkflowResultConditionText.LiteralOf(when));

        Assert.Equal(composed, WorkflowResultConditionText.Rename(when, "renamed"));
    }
}
