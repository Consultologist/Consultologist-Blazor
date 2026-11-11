using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1343 B4 (package-format-v30-design.md § 4.2): a let's value at run time — the
/// expression evaluated over a render scope, stored as its type says (a text bare,
/// the rest canonical JSON) and checked against its type; a number or boolean let
/// read back by <c>node.&lt;let&gt;.value</c> as a number or a boolean.
/// </summary>
public class WorkflowV30LetValueTests
{
    private const string Subject = "Let 'problem_count'";

    private static TemplateRenderScope Scope(IReadOnlyDictionary<string, TemplateNodeOutput> nodes) =>
        new(
            new Dictionary<string, string>(), null, null, nodes, new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<TemplateFolderItem>>(), null, new Dictionary<string, TemplateMacroSource>(),
            new TemplateRunFacts("2026-11-11", "abc", "p@v1", ""), new TemplateProfileFacts("Dr", "Dr"));

    private static readonly IReadOnlyDictionary<string, TemplateNodeOutput> Problems = new Dictionary<string, TemplateNodeOutput>
    {
        ["find_problems"] = new("""{"problems":[{"name":"Anaemia","weight":2.50},{"name":"Gout","weight":1}]}""", WorkflowInputTypes.Object),
    };

    [Fact]
    public void ANumber_IsEvaluated_AndStoredCanonically()
    {
        var value = WorkflowLetAt30.Evaluate(Subject, "node.find_problems.value.problems | array.size", Scope(Problems));
        var (stored, errors) = WorkflowLetAt30.ValueOf(Subject, WorkflowLetAt30.Number, value);

        Assert.Equal("2", stored);
        Assert.Empty(errors);
    }

    [Fact]
    public void ANumber_HasNoTrailingZeros()
    {
        var value = WorkflowLetAt30.Evaluate(Subject, "node.find_problems.value.problems[0].weight", Scope(Problems));

        Assert.Equal("2.5", WorkflowLetAt30.ValueOf(Subject, WorkflowLetAt30.Number, value).Stored);
        Assert.Equal("4", WorkflowLetAt30.ValueOf(Subject, WorkflowLetAt30.Number, 4.000m).Stored);
    }

    [Fact]
    public void AText_IsStoredBare()
    {
        var value = WorkflowLetAt30.Evaluate(Subject, "node.find_problems.value.problems[1].name | string.upcase", Scope(Problems));

        Assert.Equal(("GOUT", 0), (WorkflowLetAt30.ValueOf(Subject, WorkflowLetAt30.Text, value).Stored, WorkflowLetAt30.ValueOf(Subject, WorkflowLetAt30.Text, value).Errors.Count));
    }

    [Fact]
    public void ABoolean_IsStoredAsJson()
    {
        var value = WorkflowLetAt30.Evaluate(Subject, "(node.find_problems.value.problems | array.size) > 1", Scope(Problems));

        Assert.Equal("true", WorkflowLetAt30.ValueOf(Subject, WorkflowLetAt30.Boolean, value).Stored);
    }

    [Fact]
    public void AMismatch_IsStored_AndNamed()
    {
        var (stored, errors) = WorkflowLetAt30.ValueOf(Subject, WorkflowLetAt30.Number, "three");

        Assert.Equal("three", stored);
        Assert.Equal(new[] { "Let 'problem_count' yielded a text; it declares type number." }, errors);
    }

    [Fact]
    public void AFormatsObject_IsCanonical_AndCheckedAgainstItsSchema()
    {
        var value = WorkflowLetAt30.Evaluate(Subject, "node.find_problems.value", Scope(Problems));
        var schema = JsonNode.Parse("""{"type":"object","required":["problems","summary"],"properties":{"problems":{"type":"array"},"summary":{"type":"string"}}}""");

        var (stored, errors) = WorkflowLetAt30.ValueOf(Subject, "problem_list", value, schema);

        Assert.Equal("""{"problems":[{"name":"Anaemia","weight":2.5},{"name":"Gout","weight":1}]}""", stored);
        Assert.Single(errors);
        Assert.StartsWith("Let 'problem_count''s value does not match format problem_list:", errors[0]);
    }

    [Fact]
    public void AList_IsNotAFormatsObject()
    {
        var value = WorkflowLetAt30.Evaluate(Subject, "node.find_problems.value.problems", Scope(Problems));

        Assert.Equal(new[] { "Let 'problem_count' yielded a list; it declares type problem_list." }, WorkflowLetAt30.ValueOf(Subject, "problem_list", value).Errors);
    }

    [Fact]
    public void ANumberOrBooleanNode_ReadsAsItsType_WhenItCarriesItsJson()
    {
        var nodes = new Dictionary<string, TemplateNodeOutput>
        {
            ["count"] = new("2", WorkflowInputTypes.Number, ValueJson: "2"),
            ["many"] = new("true", WorkflowInputTypes.Boolean, ValueJson: "true"),
            // A model node under a number format carries no ValueJson: its text, as ever.
            ["model"] = new("2", WorkflowInputTypes.Number),
        };

        var rendered = TemplateRenderer.Render("Prompt 'p'", "{{ node.count.value + 1 }}|{{ node.many.value == true }}|{{ node.model.value + 1 }}", Scope(nodes), null, new TemplateRenderer.TemplateRenderTrace());

        Assert.Equal("3|true|21", rendered);
    }
}
