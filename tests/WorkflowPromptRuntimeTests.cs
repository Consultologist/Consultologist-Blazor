using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

public class PromptTemplateRendererTests
{
    private static WorkflowPromptTemplate Template(string text, string? prelude = null) =>
        new("extract-patient-concepts", text, new[] { "consult_draft" }, prelude);

    [Fact]
    public void Render_InterpolatesVariables()
    {
        var result = PromptTemplateRenderer.Render(
            Template("Draft:\n{{ consult_draft }}"),
            new Dictionary<string, string> { ["consult_draft"] = "Patient draft text." });

        Assert.Equal("Draft:\nPatient draft text.", result);
    }

    // #313 (v8): a typed input reaches Scriban as its own type, so a template
    // can format a date and branch on a boolean. This is the author-visible
    // half of typing inputs — without it, typed JSON would change the spelling
    // of one value and nothing else.

    private static WorkflowPromptTemplate TypedTemplate(string text, params string[] variables) =>
        new("typed", text, variables, null);

    [Fact]
    public void Render_FormatsATypedDate()
    {
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("Seen {{ seen_on | date.to_string \"%d %B %Y\" }}", "seen_on"),
            new Dictionary<string, string> { ["seen_on"] = "2026-08-10" },
            new Dictionary<string, string> { ["seen_on"] = WorkflowInputTypes.Date });

        Assert.Equal("Seen 10 August 2026", result);
    }

    [Fact]
    public void Render_BranchesOnATypedBoolean()
    {
        var template = TypedTemplate("{{ if billable }}Bill it.{{ else }}No charge.{{ end }}", "billable");

        Assert.Equal("Bill it.", PromptTemplateRenderer.Render(
            template,
            new Dictionary<string, string> { ["billable"] = "true" },
            new Dictionary<string, string> { ["billable"] = WorkflowInputTypes.Boolean }));

        Assert.Equal("No charge.", PromptTemplateRenderer.Render(
            template,
            new Dictionary<string, string> { ["billable"] = "false" },
            new Dictionary<string, string> { ["billable"] = WorkflowInputTypes.Boolean }));
    }

    // #357: a package could not render an ISO date at all. Bare interpolation
    // reformatted it through Scriban's default %d %b %Y, and the documented
    // escape hatch — date.to_string — could not publish, because the
    // validator's probe renders every variable as a string.

    [Fact]
    public void Render_ABareTypedDate_IsTheIsoWireForm()
    {
        // The value is rejected rather than normalised on the way in — 2026-8-1
        // is a 422 — so silently reformatting it on the way out was the odd half.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("Seen {{ seen_on }}", "seen_on"),
            new Dictionary<string, string> { ["seen_on"] = "2026-08-10" },
            new Dictionary<string, string> { ["seen_on"] = WorkflowInputTypes.Date });

        Assert.Equal("Seen 2026-08-10", result);
    }

    [Fact]
    public void Render_AnExplicitFormat_StillWins()
    {
        // The default is a default, not a ceiling: a letter that wants prose
        // still asks for it. Render_FormatsATypedDate above covers the same
        // filter; this one exists to prove the new default did not remove it.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("Seen {{ seen_on | date.to_string \"%d %B %Y\" }}", "seen_on"),
            new Dictionary<string, string> { ["seen_on"] = "2026-08-10" },
            new Dictionary<string, string> { ["seen_on"] = WorkflowInputTypes.Date });

        Assert.Equal("Seen 10 August 2026", result);
    }

    [Fact]
    public void Render_AnUntypedDateLikeString_IsUntouched()
    {
        // The format applies to DateTime rendering and nothing else, so a v5-v7
        // job — which carries no types — is byte-identical.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("Seen {{ seen_on }}", "seen_on"),
            new Dictionary<string, string> { ["seen_on"] = "10 August 2026" });

        Assert.Equal("Seen 10 August 2026", result);
    }

    [Fact]
    public void Render_WithoutTypes_IsUnchanged()
    {
        // The replay-safety case: a v5-v7 job carries no VariableTypes, so
        // every variable stays the string it always was. "false" is a
        // non-empty string and therefore truthy in Scriban — which is exactly
        // the old behaviour, and the reason typing had to be explicit.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("{{ if billable }}Bill it.{{ else }}No charge.{{ end }}", "billable"),
            new Dictionary<string, string> { ["billable"] = "false" });

        Assert.Equal("Bill it.", result);
    }

    // #358: an unanswered optional is *absent*, and the resolver map spells
    // absence as the empty string. Scriban's only falsy values are null,
    // EmptyScriptObject.Default and bool false — so the empty string was
    // truthy, and every emailed job took the true branch of every boolean it
    // could not answer. A boolean cannot be supplied by email at all.

    private const string Absent = "";

    private static Dictionary<string, string> Typed(string name, string type) =>
        new(StringComparer.Ordinal) { [name] = type };

    [Fact]
    public void Render_AnAbsentOptionalBoolean_IsFalsy()
    {
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("{{ if billable }}Bill it.{{ else }}No charge.{{ end }}", "billable"),
            new Dictionary<string, string> { ["billable"] = Absent },
            Typed("billable", WorkflowInputTypes.Boolean));

        Assert.Equal("No charge.", result);
    }

    [Fact]
    public void Render_AnAbsentOptionalBoolean_RendersNothing()
    {
        // Why null rather than false: false is falsy too, but it renders five
        // characters wherever the variable is interpolated bare, putting a word
        // nobody supplied into a prompt. An absent optional renders empty
        // (package-format-v7.md § 3), and null is the only falsy value that
        // keeps that promise.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("[{{ billable }}]", "billable"),
            new Dictionary<string, string> { ["billable"] = Absent },
            Typed("billable", WorkflowInputTypes.Boolean));

        Assert.Equal("[]", result);
    }

    [Fact]
    public void Render_ASuppliedFalse_IsFalsyAndStillPrints()
    {
        // The distinction the fix turns on: false is an answer. It branches the
        // same way absence does and renders differently, which is what lets a
        // template tell them apart at all.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("{{ if billable }}Bill it.{{ else }}No charge.{{ end }} [{{ billable }}]", "billable"),
            new Dictionary<string, string> { ["billable"] = "false" },
            Typed("billable", WorkflowInputTypes.Boolean));

        Assert.Equal("No charge. [false]", result);
    }

    [Fact]
    public void Render_AnAbsentOptionalBoolean_MatchesNeitherLiteral()
    {
        // The == true workaround keeps working, and so does its mirror:
        // absence answers neither question. An author who needs the three-way
        // distinction tests the value rather than negating it.
        var variables = new Dictionary<string, string> { ["billable"] = Absent };
        var types = Typed("billable", WorkflowInputTypes.Boolean);

        Assert.Equal(string.Empty, PromptTemplateRenderer.Render(
            TypedTemplate("{{ if billable == true }}yes{{ end }}", "billable"), variables, types));
        Assert.Equal(string.Empty, PromptTemplateRenderer.Render(
            TypedTemplate("{{ if billable == false }}no{{ end }}", "billable"), variables, types));
    }

    [Fact]
    public void Render_AnAbsentOptionalBoolean_SatisfiesTheNegatedForm()
    {
        // Pinned deliberately, because it is the one place a template and a
        // `when` condition disagree and cannot be reconciled:
        // WorkflowResultConditions.Holds is three-valued, so
        // `billable != true` does NOT hold on absence, while Scriban has one
        // falsy null and no third value to offer.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("{{ if !billable }}Not billed.{{ end }}", "billable"),
            new Dictionary<string, string> { ["billable"] = Absent },
            Typed("billable", WorkflowInputTypes.Boolean));

        Assert.Equal("Not billed.", result);
    }

    [Fact]
    public void Render_AnAbsentOptionalDate_IsFalsyAndRendersNothing()
    {
        // The same defect one type over: a DateTime is truthy too, including
        // DateTime.MinValue, which would also render a year-1 date.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("[{{ seen_on }}]{{ if seen_on }} seen{{ end }}", "seen_on"),
            new Dictionary<string, string> { ["seen_on"] = Absent },
            Typed("seen_on", WorkflowInputTypes.Date));

        Assert.Equal("[]", result);
    }

    [Theory]
    [InlineData(WorkflowInputTypes.Enum)]
    [InlineData(WorkflowInputTypes.Text)]
    public void Render_AnAbsentOptionalStringType_IsStillTheEmptyString(string type)
    {
        // enum and text are JSON strings on the wire and stay strings here, so
        // the `== ""` idiom both published packages use to test absence keeps
        // working. Only the two converted types change.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate(
                "{{ if (encounter_kind | string.strip) == \"\" }}unset{{ else }}{{ encounter_kind }}{{ end }}",
                "encounter_kind"),
            new Dictionary<string, string> { ["encounter_kind"] = Absent },
            Typed("encounter_kind", type));

        Assert.Equal("unset", result);
    }

    [Fact]
    public void Render_WithoutTypes_AnAbsentOptionalIsStillTruthy()
    {
        // Replay safety as an assertion: a v5-v7 job carries no VariableTypes,
        // so #358 must not reach it. The empty string stays the empty string
        // and stays truthy, exactly as the recorded run rendered.
        var result = PromptTemplateRenderer.Render(
            TypedTemplate("{{ if billable }}Bill it.{{ else }}No charge.{{ end }}", "billable"),
            new Dictionary<string, string> { ["billable"] = Absent });

        Assert.Equal("Bill it.", result);
    }

    [Fact]
    public void Render_PrependsPreludeWithBlankLine()
    {
        var result = PromptTemplateRenderer.Render(
            Template("{{ consult_draft }}", prelude: "Guidance text.\n"),
            new Dictionary<string, string> { ["consult_draft"] = "x" });

        Assert.Equal("Guidance text.\n\nx", result);
    }

    [Fact]
    public void Render_ThrowsWhenSuppliedVariablesDoNotMatchDeclared()
    {
        var extra = new Dictionary<string, string> { ["consult_draft"] = "x", ["section_name"] = "HPI" };
        var missing = new Dictionary<string, string>();

        Assert.Throws<InvalidOperationException>(() => PromptTemplateRenderer.Render(Template("{{ consult_draft }}"), extra));
        Assert.Throws<InvalidOperationException>(() => PromptTemplateRenderer.Render(Template("{{ consult_draft }}"), missing));
    }

    [Fact]
    public void Render_ThrowsOnUndeclaredVariableAccess_StrictMode()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PromptTemplateRenderer.Render(
            Template("{{ consult_draft }} {{ not_declared }}"),
            new Dictionary<string, string> { ["consult_draft"] = "x" }));

        Assert.Contains("failed to render", ex.Message);
    }
}
