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
