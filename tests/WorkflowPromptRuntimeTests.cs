using Consultologist.Api.Models;
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

/// <summary>
/// #425 (v9 layer 5): structure enters the template as structure — an array
/// to iterate, an object to reach into, a number to compare — and absence
/// renders as an empty, FALSY shape. The renderer's one rule of its own:
/// an empty array is falsy, so {{ if notes }}, {{ if notes.size > 0 }},
/// {{ for }} and {{ patient.age }} behave alike on absence and on emptiness.
/// Scriban's own rule — only null, EmptyScriptObject and false are falsy —
/// would have made the format's prescribed idiom throw on the one job where
/// the slot was left empty.
/// </summary>
public class StructuredRenderingTests
{
    private static WorkflowPromptTemplate T(string text, params string[] variables) => new("structured", text, variables, null);

    private static readonly IReadOnlyList<WorkflowFieldSpec> PatientFields = new List<WorkflowFieldSpec>
    {
        new("family_name", "Family name"),
        new("age", "Age", Type: WorkflowInputTypes.Number),
        new("seen_on", "Seen on", Required: false, Type: WorkflowInputTypes.Date)
    };

    private static readonly IReadOnlyDictionary<string, WorkflowInputSpec> Declarations = new Dictionary<string, WorkflowInputSpec>
    {
        ["patient"] = new("patient", "Patient", Type: WorkflowInputTypes.Object, Fields: PatientFields.ToList()),
        ["meds"] = new("medications", "Medications", Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Object, Fields: PatientFields.ToList())
    };

    private static string Render(string text, string name, string value, string type,
        IReadOnlyDictionary<string, WorkflowInputSpec>? declarations = null) =>
        PromptTemplateRenderer.Render(
            T(text, name),
            new Dictionary<string, string> { [name] = value },
            new Dictionary<string, string> { [name] = type },
            declarations);

    private static string Notes(params string[] notes) =>
        ConsultInputValue.OfArray(notes.Select(ConsultInputValue.OfText)).AsJson();

    private static string Patient(params (string Id, ConsultInputValue Value)[] fields) =>
        ConsultInputValue.OfObject(fields.Select(f => new ConsultInputEntry(f.Id, f.Value))).AsJson();

    [Fact]
    public void AnArray_IteratesInTheCallersOrder()
    {
        Assert.Equal(
            "- b\n- a\n",
            Render("{{ for n in notes }}- {{ n }}\n{{ end }}", "notes", Notes("b", "a"), WorkflowInputTypes.Array));
    }

    [Fact]
    public void AnArray_HasASize()
    {
        Assert.Equal("2", Render("{{ notes.size }}", "notes", Notes("b", "a"), WorkflowInputTypes.Array));
    }

    [Fact]
    public void AnObject_IsReachedInto()
    {
        Assert.Equal(
            "Smith, 40",
            Render("{{ patient.family_name }}, {{ patient.age }}", "patient",
                Patient(("family_name", ConsultInputValue.OfText("Smith")), ("age", ConsultInputValue.OfNumber("40"))),
                WorkflowInputTypes.Object));
    }

    [Fact]
    public void ADateField_FormatsAsADate_WhenTheDeclarationIsInHand()
    {
        // The activity has the package, so fields render as their own types —
        // what the publish-time probe already checks a template against.
        var carrier = Patient(("family_name", ConsultInputValue.OfText("Smith")), ("seen_on", ConsultInputValue.OfText("2026-08-10")));

        Assert.Equal(
            "Seen 10 August 2026",
            Render("Seen {{ patient.seen_on | date.to_string \"%d %B %Y\" }}", "patient", carrier, WorkflowInputTypes.Object, Declarations));
        // Without the declaration the field is the ISO string it arrived as.
        Assert.Equal("2026-08-10", Render("{{ patient.seen_on }}", "patient", carrier, WorkflowInputTypes.Object));
    }

    [Fact]
    public void AnArrayOfObjects_ReachesIntoEachElement()
    {
        var carrier = ConsultInputValue.OfArray(new[]
        {
            ConsultInputValue.FromJson(Patient(("family_name", ConsultInputValue.OfText("metformin")), ("age", ConsultInputValue.OfNumber("500")))),
            ConsultInputValue.FromJson(Patient(("family_name", ConsultInputValue.OfText("ramipril")), ("age", ConsultInputValue.OfNumber("2.5"))))
        }).AsJson();

        Assert.Equal(
            "metformin 500; ramipril 2.5; ",
            Render("{{ for m in meds }}{{ m.family_name }} {{ m.age }}; {{ end }}", "meds", carrier, WorkflowInputTypes.Array, Declarations));
    }

    [Fact]
    public void ANumber_RendersItsSpelling_AndCompares()
    {
        Assert.Equal("1.50", Render("{{ n }}", "n", "1.50", WorkflowInputTypes.Number));
        Assert.Equal("long", Render("{{ if n > 7 }}long{{ else }}short{{ end }}", "n", "10", WorkflowInputTypes.Number));
        Assert.Equal("short", Render("{{ if n > 7 }}long{{ else }}short{{ end }}", "n", "3", WorkflowInputTypes.Number));
    }

    [Fact]
    public void AnAbsentOptionalNumber_IsNull()
    {
        Assert.Equal("[]", Render("[{{ n }}]", "n", "", WorkflowInputTypes.Number));
        Assert.Equal("none", Render("{{ if n }}some{{ else }}none{{ end }}", "n", "", WorkflowInputTypes.Number));
    }

    [Fact]
    public void AnEmptyArray_IsFalsy_ByTheRenderersRule()
    {
        // Scriban calls an empty array true. This renderer does not: the
        // asymmetry the design record first documented is removed at the one
        // method truthiness lives in. Pinned as the rule, so a change is
        // deliberate.
        Assert.Equal("none", Render("{{ if notes }}some{{ else }}none{{ end }}", "notes", Notes(), WorkflowInputTypes.Array));
        Assert.Equal("some", Render("{{ if notes }}some{{ else }}none{{ end }}", "notes", Notes("a"), WorkflowInputTypes.Array));
    }

    [Theory]
    [InlineData("{{ if notes }}some{{ else }}none{{ end }}", "none")]
    [InlineData("{{ if notes.size > 0 }}some{{ else }}none{{ end }}", "none")]
    [InlineData("[{{ for n in notes }}{{ n }}{{ end }}]", "[]")]
    [InlineData("{{ notes.size }}", "0")]
    public void AnAbsentOptionalArray_IsAnEmptyOne_AndEveryIdiomHolds(string template, string expected)
    {
        // Absent renders as empty rather than null, because Scriban refuses a
        // member of null: the prescribed {{ if notes.size > 0 }} would have
        // thrown on exactly the job where the slot was left empty.
        Assert.Equal(expected, Render(template, "notes", "", WorkflowInputTypes.Array));
    }

    [Theory]
    [InlineData("{{ if patient }}some{{ else }}none{{ end }}", "none")]
    [InlineData("[{{ patient.family_name }}]", "[]")]
    public void AnAbsentOptionalObject_IsAnEmptyOne_ReachableWithoutThrowing(string template, string expected)
    {
        Assert.Equal(expected, Render(template, "patient", "", WorkflowInputTypes.Object, Declarations));
    }

    [Fact]
    public void AV8Template_IsByteIdentical_WithAndWithoutDeclarations()
    {
        // The context change touches only empty arrays, which no v8 job
        // carries; the declarations touch only typed structure.
        var template = T("Seen {{ seen_on }}{{ if billable }} — bill it{{ end }}", "seen_on", "billable");
        var variables = new Dictionary<string, string> { ["seen_on"] = "2026-08-10", ["billable"] = "true" };
        var types = new Dictionary<string, string> { ["seen_on"] = WorkflowInputTypes.Date, ["billable"] = WorkflowInputTypes.Boolean };

        Assert.Equal(
            PromptTemplateRenderer.Render(template, variables, types),
            PromptTemplateRenderer.Render(template, variables, types, Declarations));
        Assert.Equal("Seen 2026-08-10 — bill it", PromptTemplateRenderer.Render(template, variables, types));
    }

    [Fact]
    public void PlainTextUnderAStructuredType_StaysText()
    {
        // Defensive, for a replay that is not a carrier: the string it always was.
        Assert.Equal("not json", Render("{{ notes }}", "notes", "not json", WorkflowInputTypes.Array));
    }
}
