using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>B3 #1002: the 25 render — seven objects with closed members, snippets and macro texts rendered when read, pipes, strictness.</summary>
public class TemplateRendererTests
{
    private static readonly TemplateRunFacts Run = new("2026-10-13", "abcdef12", "test/general@v2026.10.8", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");

    private static TemplateRenderScope Scope(
        IReadOnlyDictionary<string, string>? inputs = null,
        IReadOnlyDictionary<string, string>? inputTypes = null,
        IReadOnlyDictionary<string, WorkflowInputSpec>? declarations = null,
        IReadOnlyDictionary<string, TemplateNodeOutput>? nodes = null,
        IReadOnlyDictionary<string, string>? snippets = null,
        IReadOnlyDictionary<string, IReadOnlyList<TemplateFolderItem>>? folders = null,
        TemplateFanItem? item = null,
        IReadOnlyDictionary<string, TemplateMacroSource>? macros = null) =>
        new(
            inputs ?? new Dictionary<string, string>(),
            inputTypes,
            declarations,
            nodes ?? new Dictionary<string, TemplateNodeOutput>(),
            snippets ?? new Dictionary<string, string>(),
            folders ?? new Dictionary<string, IReadOnlyList<TemplateFolderItem>>(),
            item,
            macros ?? new Dictionary<string, TemplateMacroSource>(),
            Run,
            Profile);

    private static string Render(string text, TemplateRenderScope scope, IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes = null) =>
        TemplateRenderer.Render("Prompt 'p'", text, scope, pipes);

    [Fact]
    public void Input_IsTypedAsTheFlatRendererTypedIt()
    {
        var scope = Scope(
            inputs: new Dictionary<string, string> { ["notes"] = "Referral.", ["visits"] = "3", ["seen"] = "", ["list"] = """["a","b"]""" },
            inputTypes: new Dictionary<string, string> { ["visits"] = "number", ["seen"] = "boolean", ["list"] = "array" });

        Assert.Equal("Referral. 4 no 2", Render("{{ input.notes }} {{ input.visits + 1 }} {{ if input.seen }}yes{{ else }}no{{ end }} {{ input.list.size }}", scope));
    }

    [Fact]
    public void Node_Text_IsTheComposedOutput_AndValue_IsTheTextOnATextOrEnumFormat()
    {
        var scope = Scope(nodes: new Dictionary<string, TemplateNodeOutput>
        {
            ["assessment"] = new("The plan.", "text"),
            ["scope"] = new("in_scope", "enum"),
        });

        Assert.Equal("The plan. in_scope in_scope", Render("{{ node.assessment.text }} {{ node.scope.text }} {{ node.scope.value }}", scope));
    }

    [Fact]
    public void Node_Value_IsTheOutputParsed_OnAStructuredFormat()
    {
        var scope = Scope(nodes: new Dictionary<string, TemplateNodeOutput>
        {
            ["extract"] = new("""{ "concepts": [ { "term": "Asthma", "id": "195967001", "n": 2.5, "active": true }, { "term": "Cough", "id": null } ] }""", "object"),
            ["terms"] = new("""[ "a", "b" ]""", "array"),
        });

        Assert.Equal(
            "- Asthma (195967001) 3.5 yes\n- Cough ()  \n2",
            Render("{{ for c in node.extract.value.concepts }}- {{ c.term }} ({{ c.id }}) {{ if c.n }}{{ c.n + 1 }}{{ end }} {{ if c.active }}yes{{ end }}\n{{ end }}{{ node.terms.value.size }}", scope));
    }

    [Fact]
    public void Node_Value_ThatIsNotJson_FailsWithTheSubject()
    {
        var scope = Scope(nodes: new Dictionary<string, TemplateNodeOutput> { ["extract"] = new("not json", "object") });

        var ex = Assert.Throws<InvalidOperationException>(() => Render("{{ node.extract.value }}", scope));

        Assert.StartsWith("Prompt 'p' reads node.extract.value, but the node's output is not JSON:", ex.Message);
    }

    [Fact]
    public void Snippet_RendersOverTheSameScope_WhenRead()
    {
        var scope = Scope(
            inputs: new Dictionary<string, string> { ["notes"] = "N" },
            nodes: new Dictionary<string, TemplateNodeOutput> { ["scope"] = new("in_scope", "enum") },
            snippets: new Dictionary<string, string>
            {
                ["standards"] = "Standards for {{ node.scope.text }} ({{ snippet.intro.text }})",
                ["intro"] = "intro:{{ input.notes }}",
            });

        Assert.Equal("standards|standards|Standards for in_scope (intro:N)", Render("{{ snippet.standards.id }}|{{ snippet.standards.name }}|{{ snippet.standards.text }}", scope));
    }

    [Fact]
    public void SnippetFolder_IteratesTheStartTimeItems_EachRendered()
    {
        var scope = Scope(
            inputs: new Dictionary<string, string> { ["notes"] = "N" },
            folders: new Dictionary<string, IReadOnlyList<TemplateFolderItem>>
            {
                ["sections"] = new List<TemplateFolderItem> { new("hpi", "HPI", "History: {{ input.notes }}"), new("plan", "Plan", "Plan.") },
            });

        Assert.Equal("2;hpi/HPI/History: N;plan/Plan/Plan.;", Render("{{ snippet.sections.size }};{{ for s in snippet.sections }}{{ s.id }}/{{ s.name }}/{{ s.text }};{{ end }}", scope));
    }

    [Fact]
    public void Item_OnASnippetFan_IsTheItemRendered()
    {
        var scope = Scope(
            inputs: new Dictionary<string, string> { ["notes"] = "N" },
            item: new TemplateFanItem("hpi", "HPI", Text: "History: {{ input.notes }}"));

        Assert.Equal("hpi HPI History: N", Render("{{ item.id }} {{ item.name }} {{ item.text }}", scope));
    }

    [Fact]
    public void Item_OnAnInputFan_IsTheElementTyped()
    {
        var element = new WorkflowDeclarationNode("object", "Visit", false, null,
            new List<WorkflowDeclarationNode> { new("date", "Date", false, null, null, null, "on"), new("number", "Count", false, null, null, null, "count") }, null);
        var scope = Scope(item: new TemplateFanItem("0", "Visit 1", Value: """{"on":"2026-01-02","count":2}""", Element: element));

        Assert.Equal("0 Visit 1 2026-01-02 3", Render("{{ item.id }} {{ item.name }} {{ item.value.on | date.to_string '%Y-%m-%d' }} {{ item.value.count + 1 }}", scope));

        var text = Scope(item: new TemplateFanItem("1", "Note 2", Value: "plain", Element: new WorkflowDeclarationNode("text", "Note", false, null, null, null)));
        Assert.Equal("plain", Render("{{ item.value }}", text));
    }

    [Fact]
    public void Macro_Text_Renders_AndPrompt_IsTheInstructionWithTheMechanicsSentence()
    {
        var scope = Scope(macros: new Dictionary<string, TemplateMacroSource>
        {
            ["closing"] = new("closing", "Closing", "Closing: {{ profile.name }} on {{ run.date }}.", "Add a closing line if the plan changed."),
        });

        Assert.Equal("closing|Closing|Closing: Dr Example on 2026-10-13.", Render("{{ macro.closing.id }}|{{ macro.closing.name }}|{{ macro.closing.text }}", scope));
        Assert.Equal(WorkflowMacroMechanics.Prompt("closing", "Add a closing line if the plan changed."), Render("{{ macro.closing.prompt }}", scope));
    }

    [Fact]
    public void Run_AndProfile_AreTheFacts()
    {
        Assert.Equal(
            "2026-10-13 abcdef12 test/general@v2026.10.8 api.example.test Dr Example Dr Example, FRCPC",
            Render("{{ run.date }} {{ run.job }} {{ run.package }} {{ run.host }} {{ profile.name }} {{ profile.signature }}", Scope()));
    }

    [Fact]
    public void Pipes_ApplyToText()
    {
        var scope = Scope(nodes: new Dictionary<string, TemplateNodeOutput> { ["extract"] = new("""{"concepts":[]}""", "object", ConceptSource: "clinical-note") });
        var pipes = new Dictionary<string, Func<TemplateNodeOutput, string>>
        {
            [TemplatePipes.ConceptContext] = output => $"ctx({output.Text}|{output.ConceptSource})",
            [TemplatePipes.ConceptBullets] = output => $"bul({output.Text})",
        };

        Assert.Equal("""ctx({"concepts":[]}|clinical-note) bul({"concepts":[]})""", Render("{{ node.extract.text | concept_context }} {{ node.extract.text | concept_bullets }}", scope, pipes));
    }

    [Fact]
    public void APipeOverAValue_IsRefused_AndAMissingPipeSaysSo()
    {
        var scope = Scope(nodes: new Dictionary<string, TemplateNodeOutput> { ["extract"] = new("""{"concepts":[]}""", "object") });
        var pipes = new Dictionary<string, Func<TemplateNodeOutput, string>> { [TemplatePipes.ConceptContext] = output => output.Text };

        var value = Assert.Throws<InvalidOperationException>(() => Render("{{ node.extract.value | concept_context }}", scope, pipes));
        Assert.Equal("Prompt 'p' pipes a object through concept_context; the pipe renders a node's text (node.<id>.text).", value.Message);

        var literal = Assert.Throws<InvalidOperationException>(() => Render("{{ 'free text' | concept_context }}", scope, pipes));
        Assert.Equal("Prompt 'p' pipes text that is not a node's output through concept_context; the pipe renders a node's text (node.<id>.text).", literal.Message);

        var missing = Assert.Throws<InvalidOperationException>(() => Render("{{ node.extract.text | concept_bullets }}", scope, pipes));
        Assert.Equal("Prompt 'p' pipes through concept_bullets, which this renderer does not provide.", missing.Message);
    }

    [Fact]
    public void AnUndeclaredMember_OrObject_FailsStrict()
    {
        var scope = Scope(nodes: new Dictionary<string, TemplateNodeOutput> { ["extract"] = new("x", "text") });

        Assert.Equal("Prompt 'p' reads node.extract.nope, which this render has no value for.", Assert.Throws<InvalidOperationException>(() => Render("{{ node.extract.nope }}", scope)).Message);
        Assert.Equal("Prompt 'p' reads node.other.text, which this render has no value for.".Replace(".text", string.Empty), Assert.Throws<InvalidOperationException>(() => Render("{{ node.other.text }}", scope)).Message);
        Assert.Equal("Prompt 'p' reads input.nope, which this render has no value for.", Assert.Throws<InvalidOperationException>(() => Render("{{ input.nope }}", scope)).Message);
        Assert.Throws<InvalidOperationException>(() => Render("{{ data.x }}", scope));
        Assert.Throws<InvalidOperationException>(() => Render("{{ item.id }}", scope));
    }

    [Fact]
    public void ASnippetThatDoesNotParse_NamesItself()
    {
        var scope = Scope(snippets: new Dictionary<string, string> { ["bad"] = "{{ if }}" });

        var ex = Assert.Throws<InvalidOperationException>(() => Render("{{ snippet.bad.text }}", scope));

        Assert.StartsWith("Snippet 'bad'", ex.Message);
    }

    [Fact]
    public void AnEmptyArrayValue_IsFalsy_AsTheFlatRendererMadeIt()
    {
        var scope = Scope(nodes: new Dictionary<string, TemplateNodeOutput> { ["terms"] = new("[]", "array") });

        Assert.Equal("none", Render("{{ if node.terms.value }}some{{ else }}none{{ end }}", scope));
    }
}
