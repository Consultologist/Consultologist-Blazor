using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>v10 step (d) (#495): one answer from a declared set, and how it reaches the engine.</summary>
public class ClassificationOutputContractTests
{
    private const string Sentinel = "SENTINEL-CLINICAL-CONTENT-0f1e2d";
    private static readonly string[] Values = { "in_scope", "out_of_scope" };

    [Theory]
    [InlineData("""{"value":"in_scope"}""", "in_scope")]
    [InlineData("""{"value":" Out_Of_Scope "}""", "out_of_scope")]
    [InlineData("""{"VALUE":"in_scope"}""", "in_scope")]
    public void TheAnswer_IsTrimmedLowerCased_AndOneOfTheValues(string json, string expected)
    {
        Assert.Equal(expected, ClassificationOutputContract.Normalize(json, Values, "scope"));
    }

    [Fact]
    public void AnAnswerOutsideTheValues_IsRefused_AndNeverPrinted()
    {
        var ex = Assert.Throws<ClassificationOutputContractException>(() =>
            ClassificationOutputContract.Normalize($$"""{"value":"{{Sentinel}}"}""", Values, "scope"));

        Assert.Equal("Classifier 'scope' answered outside its values.", ex.Message);
        Assert.DoesNotContain(Sentinel, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingValue_AndMalformedJson_AreNamedByPositionOnly()
    {
        Assert.Equal("Classifier 'scope' answered without a value.",
            Assert.Throws<ClassificationOutputContractException>(() => ClassificationOutputContract.Normalize("{}", Values, "scope")).Message);

        var ex = Assert.Throws<ClassificationOutputContractException>(() =>
            ClassificationOutputContract.Normalize($$"""{"value": {{Sentinel}}""", Values, "scope"));
        Assert.DoesNotContain(Sentinel, ex.Message, StringComparison.Ordinal);
        Assert.Contains("line ", ex.Message);
    }

    [Fact]
    public void TheTrailer_ListsTheValuesInDeclaredOrder()
    {
        Assert.Equal("\n\nAnswer with exactly one of: in_scope, out_of_scope.", ClassificationOutputContract.Trailer(Values));
    }

    [Fact]
    public void TheException_IsRetryable_NotAConfigurationError()
    {
        Assert.False(typeof(InvalidOperationException).IsAssignableFrom(typeof(ClassificationOutputContractException)));
    }

    [Fact]
    public void DescribeNode_ImpliesTheContract_AndCarriesTheValues()
    {
        var classifier = V10Fixtures.Classifier();

        var descriptor = ConsultGenerationJobStarter.DescribeNode(classifier, null);

        Assert.Equal(OutputContracts.Classification, descriptor.OutputContract);
        Assert.Equal(Values, descriptor.Values);
        Assert.Null(descriptor.FailIfEmpty);

        var prompt = ConsultGenerationJobStarter.DescribeNode(V9Fixtures.Minimal().Nodes!.First(n => n.Aggregate is null && n.Output is null), null);
        Assert.Null(prompt.Values);
    }

    [Fact]
    public void APromptNodeBindingAClassifier_RendersItsValue()
    {
        var classifier = new ConsultNodeDescriptor("scope", "Scope", "classify", OutputContract: OutputContracts.Classification, Values: Values);
        var reader = new ConsultNodeDescriptor("letter", "Letter", "letter",
            Bindings: new Dictionary<string, ConsultNodeBindingDescriptor> { ["scope"] = new("node:scope") });
        var outputs = new Dictionary<string, NodeRunResult>
        {
            ["scope"] = new("""{"value":"in_scope"}""", null, "in", "out", 5, Classification: "in_scope")
        };

        var variables = ConsultNodeVariableResolver.Resolve(
            reader, new Dictionary<string, string>(), null, null,
            new Dictionary<string, ConsultNodeDescriptor> { ["scope"] = classifier, ["letter"] = reader }, outputs);

        Assert.Equal("in_scope", variables["scope"]);
    }

    [Fact]
    public void ARecordedResultFromBefore_ReplaysWithNoClassification()
    {
        var stored = """{"RawOutput":"x","Concepts":null,"InputHash":"a","OutputHash":"b","HashVersion":5}""";
        var result = System.Text.Json.JsonSerializer.Deserialize<NodeRunResult>(stored)!;
        Assert.Null(result.Classification);
    }
}
