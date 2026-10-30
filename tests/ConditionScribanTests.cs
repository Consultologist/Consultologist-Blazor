using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// C6 (#1012): the ≤24 condition grammar as a Scriban condition — the spelling the
/// 24→25 migration writes around a prompt-side macro read. The grammar is unchanged
/// at 25 for when; this is the template's spelling only.
/// </summary>
public class ConditionScribanTests
{
    private static string Spell(string when, Func<string, string?>? types = null)
    {
        Assert.True(WorkflowResultConditions.TryParseExpression(when, out var expression, out var error), error);
        return ConditionScriban.Spell(expression!, types);
    }

    [Theory]
    [InlineData("node:scope == in_scope", "node.scope.value == \"in_scope\"")]
    [InlineData("node:scope != out_of_scope", "node.scope.value != \"out_of_scope\"")]
    [InlineData("kind == follow_up", "input.kind == \"follow_up\"")]
    [InlineData("billable", "input.billable")]
    [InlineData("not billable", "!(input.billable)")]
    [InlineData("patient.age >= 65", "input.patient.age >= 65")]
    [InlineData("count(prior_notes) > 0", "(input.prior_notes | array.size) > 0")]
    [InlineData("item:id == hpi", "item.id == \"hpi\"")]
    [InlineData("seen_on >= 2026-01-01", "input.seen_on >= date.parse \"2026-01-01\"")]
    [InlineData("urgent == true", "input.urgent == true")]
    [InlineData("kind == new and patient.age >= 65", "(input.kind == \"new\" && input.patient.age >= 65)")]
    [InlineData("kind == new or kind == follow_up", "(input.kind == \"new\" || input.kind == \"follow_up\")")]
    [InlineData("not (kind == new)", "!(input.kind == \"new\")")]
    public void Spell_IsTheObjectsSpelling(string when, string expected)
    {
        Assert.Equal(expected, Spell(when));
    }

    [Fact]
    public void ATextOrEnumInput_ComparesAsAString_HoweverTheLiteralLooks()
    {
        Assert.Equal("input.code == \"42\"", Spell("code == 42", id => id == "code" ? WorkflowInputTypes.Text : null));
        Assert.Equal("input.kind == \"true\"", Spell("kind == true", id => id == "kind" ? WorkflowInputTypes.Enum : null));
        Assert.Equal("input.age == 42", Spell("age == 42", id => id == "age" ? WorkflowInputTypes.Number : null));
    }

    [Fact]
    public void Arithmetic_IsParenthesised()
    {
        var spelled = Spell("patient.age + 5 >= 70");
        Assert.Equal("(input.patient.age + 5) >= 70", spelled);
    }

    [Fact]
    public void TheRenamedCanonicalNodes_KeepTheirConceptSource()
    {
        var sources = WorkflowNodeDefaults.WellKnownConceptSources;
        Assert.Equal(sources["identify-problem"], sources["identify_problem"]);
        Assert.Equal(sources["extract-patient-concepts"], sources["extract_patient_concepts"]);
        Assert.Equal(sources["create-typical-trajectory"], sources["create_typical_trajectory"]);
        Assert.Equal(sources["create-patient-trajectory"], sources["create_patient_trajectory"]);
    }

    [Fact]
    public void AnArrayElementsField_IsItemValueField_InBothSpellings()
    {
        // #1236 (v27 design § 1.2): the builder's item:value.<field> is the template's item.value.<field>.
        Assert.Equal("item.value.kind == \"table\"", Spell("item:value.kind == table"));

        Assert.True(ConditionScriban.TryUnspell("item.value.kind == \"table\"", out var expression, out var error), error);
        var clause = expression!.SingleClause!;
        Assert.Equal(("value.kind", "table", true), (clause.ItemField, clause.Literal, clause.IsItemValue));

        Assert.False(ConditionScriban.TryUnspell("item.value.kind.deeper == \"x\"", out _, out var deeper));
        Assert.Contains("item.value.<field>", deeper);
    }
}
