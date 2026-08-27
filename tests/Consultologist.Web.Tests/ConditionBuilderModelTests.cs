using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// v10 step (g), PR 3 (#498): the builder's tree reads from the format's
/// parser and writes through its writer. What it holds round-trips byte for
/// byte; what it cannot hold is verbatim.
/// </summary>
public class ConditionBuilderModelTests
{
    [Theory]
    [InlineData("patient.age >= 65")]
    [InlineData("billable")]
    [InlineData("count(prior_notes) > 1")]
    [InlineData("node:scope == in_scope")]
    [InlineData("patient.age >= 65 and patient.sex == female")]
    [InlineData("patient.age >= 65 and patient.sex == female and billable")]
    [InlineData("patient.age >= 65 or billable")]
    [InlineData("patient.age >= 65 and (patient.sex == female or count(prior_notes) > 1)")]
    [InlineData("(patient.age >= 65 or billable) and not patient.sex == female")]
    [InlineData("not (patient.age >= 65 and billable)")]
    [InlineData("count(prior_notes) + patient.age > 70")]
    [InlineData("count(prior_notes) * 2 > 3")]
    [InlineData("seen_on - 30 >= 2026-01-01")]
    public void WhatTheTreeHolds_RoundTripsByteForByte(string when)
    {
        Assert.True(ConditionBuilderModel.TryFromText(when, out var root, out var verbatim), verbatim);
        Assert.True(ConditionBuilderModel.IsComplete(root));
        Assert.Equal(when, ConditionBuilderModel.ToText(root));
    }

    [Fact]
    public void AChainOfOneJoin_IsOneGroup_AndAMixedChain_Nests()
    {
        ConditionBuilderModel.TryFromText("a == x and b == y and c == z", out var chain, out _);
        Assert.Equal(3, chain.Items.Count);
        Assert.Equal(ConditionGroup.And, chain.Join);
        Assert.All(chain.Items, item => Assert.IsType<ConditionClause>(item));

        ConditionBuilderModel.TryFromText("a == x and (b == y or c == z)", out var mixed, out _);
        Assert.Equal(2, mixed.Items.Count);
        var nested = Assert.IsType<ConditionGroup>(mixed.Items[1]);
        Assert.Equal(ConditionGroup.Or, nested.Join);
        Assert.Equal(2, nested.Items.Count);
    }

    [Fact]
    public void Not_SitsOnAClauseOrAGroup()
    {
        ConditionBuilderModel.TryFromText("not billable", out var clause, out _);
        Assert.True(Assert.IsType<ConditionClause>(clause.Items[0]).Not);

        // A negated group at the top is the root itself, negated.
        ConditionBuilderModel.TryFromText("not (a == x or b == y)", out var group, out _);
        Assert.True(group.Not);
        Assert.Equal(ConditionGroup.Or, group.Join);
        Assert.Equal(2, group.Items.Count);

        ConditionBuilderModel.TryFromText("c == z and not (a == x or b == y)", out var nested, out _);
        Assert.True(Assert.IsType<ConditionGroup>(nested.Items[1]).Not);
    }

    [Fact]
    public void Arithmetic_IsOneTermOnTheLeft()
    {
        ConditionBuilderModel.TryFromText("count(prior_notes) + patient.age > 70", out var root, out _);
        var clause = Assert.IsType<ConditionClause>(root.Items[0]);
        Assert.Equal("count(prior_notes)", clause.Operand);
        Assert.Equal(new ConditionArithmetic('+', "patient.age", false), clause.Arithmetic);
        Assert.Equal(">", clause.Operator);
        Assert.Equal("70", clause.Literal);
    }

    [Theory]
    [InlineData("not not billable")]
    [InlineData("a + b + c > 1")]
    [InlineData("1 + a > 2")]
    [InlineData("a > b + 1")]
    [InlineData("- a > 1")]
    [InlineData("patient.age >= 65 and")]
    [InlineData("(patient.age >= 65")]
    public void WhatTheTreeCannotHold_IsVerbatim(string when)
    {
        Assert.False(ConditionBuilderModel.TryFromText(when, out _, out var verbatim));
        Assert.Equal(when, verbatim);
    }

    [Fact]
    public void Blank_IsARootWithOneEmptyClause_AndWritesNothing()
    {
        Assert.True(ConditionBuilderModel.TryFromText(null, out var root, out _));
        var clause = Assert.IsType<ConditionClause>(Assert.Single(root.Items));
        Assert.True(clause.IsEmpty);
        Assert.Null(ConditionBuilderModel.ToText(root));
    }

    [Fact]
    public void AnEmptyClause_HoldsTheTree()
    {
        ConditionBuilderModel.TryFromText("billable", out var root, out _);
        var appended = ConditionBuilderModel.Append(root, Array.Empty<int>(), ConditionClause.Empty);

        Assert.False(ConditionBuilderModel.IsComplete(appended));
        Assert.Null(ConditionBuilderModel.ToText(appended));
    }

    [Fact]
    public void Replace_RemovesANodeByPath_AndAnEmptiedGroupWithIt()
    {
        ConditionBuilderModel.TryFromText("a == x and (b == y or c == z)", out var root, out _);

        var withoutC = ConditionBuilderModel.Replace(root, new[] { 1, 1 }, null);
        Assert.Equal("a == x and b == y", ConditionBuilderModel.ToText(withoutC));

        var withoutGroup = ConditionBuilderModel.Replace(withoutC, new[] { 1, 0 }, null);
        Assert.Equal("a == x", ConditionBuilderModel.ToText(withoutGroup));

        var negated = ConditionBuilderModel.Replace(root, new[] { 1 }, ((ConditionGroup)ConditionBuilderModel.At(root, new[] { 1 })!) with { Not = true });
        Assert.Equal("a == x and not (b == y or c == z)", ConditionBuilderModel.ToText(negated));
    }

    [Fact]
    public void TheTextHelpers_ReadAnyClause()
    {
        const string When = "patient.age >= 65 and (count(prior_notes) > 1 or node:scope == in_scope)";
        Assert.True(ConditionText.ReadsInput(When, "patient"));
        Assert.True(ConditionText.ReadsInput(When, "prior_notes"));
        Assert.False(ConditionText.ReadsInput(When, "scope"));
        Assert.True(ConditionText.ReadsNode(When, "scope"));
        Assert.True(ConditionText.ReadsField(When, "patient", "age"));
        Assert.False(ConditionText.ReadsField(When, "patient", "sex"));
        Assert.True(ConditionText.TestsValue("patient.sex == female or billable", "patient", "sex", "female"));
        Assert.True(ConditionText.TestsValue("encounter_kind == follow_up", "encounter_kind", null, "follow_up"));
        Assert.False(ConditionText.TestsValue("encounter_kind == follow_up", "encounter_kind", null, "new_patient"));
        // An operand inside arithmetic is read too.
        Assert.True(ConditionText.ReadsInput("count(prior_notes) + patient.age > 70", "patient"));
    }

    [Fact]
    public void Rename_ReachesEveryClause_AndRespellsNothingElse()
    {
        Assert.Equal("stay >= 7 and (count(notes) > 1 or node:scope == in_scope)",
            ConditionText.RenameInput(ConditionText.RenameInput("length_of_stay >= 7 and (count(notes) > 1 or node:scope == in_scope)", "length_of_stay", "stay"), "prior_notes", "notes"));
        Assert.Equal("patient.years >= 65 and patient.years + 1 > 66",
            ConditionText.RenameField("patient.age >= 65 and patient.age + 1 > 66", "patient", "age", "years"));
        Assert.Equal("patient.contact.phone == x", ConditionText.RenameField("patient.contact.phone == x", "patient", "contact", "contact"));
        Assert.Equal("billable", ConditionText.RenameInput("billable", "other", "x"));
    }
}
