using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// A2 (#997): the second edge source — a computed or attested node's `with`,
/// typed by the format's parameter declarations (design §§ 2, 3.6).
/// </summary>
public class TemplateParameterWalkerTests
{
    private static readonly IReadOnlyDictionary<string, WorkflowFormatParameterSpec> TermsSubset = new Dictionary<string, WorkflowFormatParameterSpec>(StringComparer.Ordinal)
    {
        ["of"] = new("node", "concept_list"),
        ["in"] = new("node", "concept_list"),
        ["failWith"] = new("text"),
    };

    private static readonly IReadOnlyDictionary<string, WorkflowFormatParameterSpec> Classification = new Dictionary<string, WorkflowFormatParameterSpec>(StringComparer.Ordinal)
    {
        ["values"] = new("values"),
    };

    private static IReadOnlyDictionary<string, JsonNode?> With(string json) =>
        ((JsonObject)JsonNode.Parse(json)!).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static TemplateReferences Walk(string with, IReadOnlyDictionary<string, WorkflowFormatParameterSpec> parameters) =>
        TemplateReferenceWalker.WalkParameters("Node 'coverage'", With(with), parameters, TemplateFixtures.Context());

    private static void Refused(string with, IReadOnlyDictionary<string, WorkflowFormatParameterSpec> parameters, string sentence)
    {
        var refs = Walk(with, parameters);
        Assert.Contains(refs.Errors, e => e.StartsWith("Node 'coverage' ", StringComparison.Ordinal) && e.Contains(sentence));
    }

    [Fact]
    public void TheCheckOperands_AreEdges_AndTheSentenceIsALiteral()
    {
        var refs = Walk("""{ "of": "node.extract", "in": "node.meds", "failWith": "The note names problems the source does not." }""",
            new Dictionary<string, WorkflowFormatParameterSpec>(StringComparer.Ordinal) { ["of"] = new("node", "concept_list"), ["in"] = new("node"), ["failWith"] = new("text") });

        Assert.True(refs.IsValid, string.Join(" | ", refs.Errors));
        Assert.Equal(new[] { "extract", "meds" }, refs.Edges.Order());
        Assert.Equal(new TemplateNodeRead(false, true), refs.Nodes["extract"]);
        Assert.Empty(refs.Inputs);
    }

    [Fact]
    public void ATextParameter_MayReadATextInput()
    {
        var refs = Walk("""{ "of": "node.extract", "in": "node.extract", "failWith": "input.notes" }""", TermsSubset);

        Assert.True(refs.IsValid, string.Join(" | ", refs.Errors));
        Assert.Equal(new[] { "notes" }, refs.Inputs.ToArray());
    }

    [Fact]
    public void AValuesParameter_TakesAList_OrAnEnumInput()
    {
        Assert.True(Walk("""{ "values": ["in_scope", "out_of_scope"] }""", Classification).IsValid);

        var refs = Walk("""{ "values": "input.kind" }""", Classification);
        Assert.True(refs.IsValid, string.Join(" | ", refs.Errors));
        Assert.Equal(new[] { "kind" }, refs.Inputs.ToArray());
    }

    [Theory]
    [InlineData("""{ "of": "node.extract", "in": "node.extract", "failWith": "x", "extra": 1 }""", "with 'extra' names no parameter of the format (parameters: of, in, failWith)")]
    [InlineData("""{ "of": "node.extract", "failWith": "x" }""", "with must supply parameter 'in'")]
    [InlineData("""{ "of": "extract", "in": "node.extract", "failWith": "x" }""", "with 'of' must be a bare node reference (node.<id>); got \"extract\"")]
    [InlineData("""{ "of": 3, "in": "node.extract", "failWith": "x" }""", "with 'of' must be a bare node reference (node.<id>); got 3")]
    [InlineData("""{ "of": "input.notes", "in": "node.extract", "failWith": "x" }""", "with 'of' must be a bare node reference (node.<id>); got \"input.notes\"")]
    [InlineData("""{ "of": "node.extract.value", "in": "node.extract", "failWith": "x" }""", "with 'of' must name the node bare (node.extract), not node.extract.value; the parameter reads the node's value")]
    [InlineData("""{ "of": "node.nope", "in": "node.extract", "failWith": "x" }""", "with 'of' names node 'nope', which the package does not declare")]
    [InlineData("""{ "of": "node.assessment", "in": "node.extract", "failWith": "x" }""", "with 'of' names node 'assessment' of format 'prose', but the parameter requires format 'concept_list'")]
    [InlineData("""{ "of": "node.extract", "in": "node.extract", "failWith": 7 }""", "with 'failWith' must be a text literal or an input.<id> reference to a text input; got 7")]
    [InlineData("""{ "of": "node.extract", "in": "node.extract", "failWith": "input.kind" }""", "with 'failWith' names input 'kind', which is an enum, not a text")]
    [InlineData("""{ "of": "node.extract", "in": "node.extract", "failWith": "input.nope" }""", "with 'failWith' names input 'nope', which the package does not declare")]
    [InlineData("""{ "of": "node.extract", "in": "node.extract", "failWith": "input.patient.name" }""", "with 'failWith' must name the input bare (input.patient), not input.patient.name")]
    [InlineData("""{ "of": "node.extract", "in": "node.extract", "failWith": "node.assessment" }""", "with 'failWith' must be a literal or an input.<id> reference; got node.assessment (a node is not a parameter value here)")]
    public void TermsSubsetDefects_AreRefusedByName(string with, string sentence)
    {
        Refused(with, TermsSubset, sentence);
    }

    [Theory]
    [InlineData("""{ "values": ["only"] }""", "with 'values' declares one enum value; an enum with one value is a constant, not a choice")]
    [InlineData("""{ "values": ["Yes", "no"] }""", "with 'values' enum value 'Yes' must be snake_case")]
    [InlineData("""{ "values": ["a", "a"] }""", "with 'values' declares duplicate enum value 'a'")]
    [InlineData("""{ "values": ["a", 1] }""", "with 'values' must be a list of snake_case values; 1 is not a value")]
    [InlineData("""{ "values": "yes, no" }""", "with 'values' must be a list of snake_case values or an input.<id> reference to an enum input; got \"yes, no\"")]
    [InlineData("""{ "values": 5 }""", "with 'values' must be a list of snake_case values or an input.<id> reference to an enum input; got 5")]
    [InlineData("""{ "values": "input.notes" }""", "with 'values' names input 'notes', which is a text, not an enum")]
    [InlineData("""{ "values": "node.scope" }""", "with 'values' must be a literal or an input.<id> reference; got node.scope")]
    public void ClassificationDefects_AreRefusedByName(string with, string sentence)
    {
        Refused(with, Classification, sentence);
    }

    [Fact]
    public void AHyphenatedNodeId_IsFineInWith()
    {
        var context = TemplateFixtures.Context();
        var nodes = new Dictionary<string, TemplateNodeShape>(context.Nodes, StringComparer.Ordinal) { ["identify-problem"] = new("concept_list", "object", TemplateFixtures.ConceptListSchema) };
        var refs = TemplateReferenceWalker.WalkParameters("Node 'c'", With("""{ "of": "node.identify-problem", "in": "node.extract", "failWith": "x" }"""), TermsSubset, context with { Nodes = nodes });

        Assert.True(refs.IsValid, string.Join(" | ", refs.Errors));
        Assert.Contains("identify-problem", refs.Edges);
    }

    [Fact]
    public void ALiteralThatLooksLikeNothing_IsALiteral()
    {
        Assert.True(Walk("""{ "of": "node.extract", "in": "node.extract", "failWith": "Input. See notes." }""", TermsSubset).IsValid);
    }
}
