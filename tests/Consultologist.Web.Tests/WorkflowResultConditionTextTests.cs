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
    [InlineData("billable", "billable", null, false, false, false)]
    [InlineData("encounter_kind == follow_up", "encounter_kind", null, false, false, false)]
    [InlineData("length_of_stay > 7", "length_of_stay", null, false, true, true)]
    [InlineData("patient.age >= 65", "patient.age", "age", false, true, true)]
    [InlineData("patient.sex == female", "patient.sex", "sex", false, false, true)]
    [InlineData("count(prior_notes) > 1", "count(prior_notes)", null, true, true, true)]
    [InlineData("prior_notes", "prior_notes", null, false, false, false)]
    [InlineData("", null, null, false, false, false)]
    public void TheForms_AreReadApart(string when, string? operand, string? field, bool isCount, bool isOrdered, bool isV9)
    {
        // #429: the desk names a v9 form on a v8 publish, and the operand
        // picker shows a loaded condition as itself.
        Assert.Equal(operand, WorkflowResultConditionText.OperandOf(when));
        Assert.Equal(field, WorkflowResultConditionText.FieldOf(when));
        Assert.Equal(isCount, WorkflowResultConditionText.IsCount(when));
        Assert.Equal(isOrdered, WorkflowResultConditionText.IsOrdered(when));
        Assert.Equal(isV9, WorkflowResultConditionText.IsV9Form(when));
    }

    [Theory]
    [InlineData("patient.age >= 65", "patient", "age", "years", "patient.years >= 65")]
    [InlineData("patient.age", "patient", "age", "years", "patient.years")]
    [InlineData("patient.sex == female", "patient", "age", "years", "patient.sex == female")]
    [InlineData("visit.age >= 65", "patient", "age", "years", "visit.age >= 65")]
    [InlineData("count(patient) > 1", "patient", "age", "years", "count(patient) > 1")]
    public void RenameField_TouchesOnlyTheConditionReadingThatField(string when, string input, string oldField, string newField, string expected)
    {
        Assert.Equal(expected, WorkflowResultConditionText.RenameField(when, input, oldField, newField));
    }

    [Theory]
    [InlineData("patient.age", ">=", "65", "patient.age >= 65")]
    [InlineData("prior_notes", null, null, "prior_notes")]
    [InlineData("count(prior_notes)", ">", "", "count(prior_notes) >")]
    [InlineData("encounter_kind", "==", "follow_up", "encounter_kind == follow_up")]
    public void Compose_WritesTheWholeGrammar(string operand, string? op, string? literal, string expected)
    {
        // An empty literal is kept rather than folded to the bare form, so the
        // desk can say "compares against nothing".
        Assert.Equal(expected, WorkflowResultConditionText.Compose(operand, op, literal));
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

    // #494: an expression the editor does not author yet, recognised so the
    // desk names its version and otherwise steps aside.
    [Theory]
    [InlineData("length_of_stay > 7 and billable", true)]
    [InlineData("billable or length_of_stay > 7", true)]
    [InlineData("not billable", true)]
    [InlineData("(billable)", true)]
    [InlineData("length_of_stay - 2 > 5", true)]
    [InlineData("node:scope == in_scope", true)]
    [InlineData("patient.contact.phone == x", true)]
    [InlineData("count(prior_notes) > -1", false)]
    [InlineData("seen_on > 2026-1-1", false)]
    [InlineData("patient.age >= 65", false)]
    [InlineData("count(prior_notes) > 1", false)]
    [InlineData("billable", false)]
    [InlineData(null, false)]
    public void IsV10Form_ReadsTheExpressionForms(string? when, bool expected)
    {
        Assert.Equal(expected, WorkflowResultConditionText.IsV10Form(when));
    }
}
