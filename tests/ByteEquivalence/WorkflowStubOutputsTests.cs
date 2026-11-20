using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498: the deterministic stubs of part 10 § 3.1 — the same seed gives the same stub, the
/// stated sentence and members are what come out, a schema's bounds are honoured, and every
/// structured format the engine can meet (the bundled catalogue's, each vendored package's)
/// is stubbed into a value today's validator accepts.
/// </summary>
public sealed class WorkflowStubOutputsTests
{
    private static readonly string[] Seeds = Enumerable.Range(0, 50).Select(i => $"s{i}").ToArray();

    [Fact]
    public void TheSameSeed_GivesTheSameStub()
    {
        var schema = JsonNode.Parse("""{"type":"object","properties":{"a":{"type":"string"},"b":{"type":"array","items":{"type":"integer"}}}}""");

        Assert.Equal(WorkflowStubOutputs.Text("draft:2/1"), WorkflowStubOutputs.Text("draft:2/1"));
        Assert.Equal(WorkflowStubOutputs.FromSchema(schema, "x")!.ToJsonString(), WorkflowStubOutputs.FromSchema(schema, "x")!.ToJsonString());
    }

    [Fact]
    public void ADifferentSeed_GivesADifferentText() =>
        Assert.NotEqual(WorkflowStubOutputs.Text("draft:2/1"), WorkflowStubOutputs.Text("draft:2/2"));

    [Fact]
    public void Text_IsTheStatedSentence()
    {
        var text = WorkflowStubOutputs.Text("draft:2/1");

        Assert.StartsWith("Stub reply for draft:2/1 (", text);
        Assert.EndsWith(").", text);
        Assert.Matches("^Stub reply for draft:2/1 \\([0-9a-f]{8}\\)\\.$", text);
    }

    [Fact]
    public void Member_IsOneOfTheValues_AndEveryValueIsReachable()
    {
        string[] values = ["a", "b", "c"];
        var results = Enumerable.Range(0, 64).Select(i => WorkflowStubOutputs.Member(values, $"n{i}")).ToList();

        Assert.All(results, result => Assert.Contains(result, values));
        Assert.Equal(values.Order(), results.Distinct().Order());
    }

    [Theory]
    [InlineData("""{"type":"array","items":{"type":"string"}}""", 1, 3)]
    [InlineData("""{"type":"array","minItems":4,"items":{"type":"string"}}""", 4, 4)]
    [InlineData("""{"type":"array","maxItems":1,"items":{"type":"string"}}""", 1, 1)]
    public void AnArray_HoldsOneToThreeItems_WithinItsBounds(string schemaJson, int least, int most)
    {
        var schema = JsonNode.Parse(schemaJson);

        foreach (var seed in Seeds)
        {
            var count = Assert.IsType<JsonArray>(WorkflowStubOutputs.FromSchema(schema, seed)).Count;

            Assert.InRange(count, least, most);
        }
    }

    [Fact]
    public void ANullableType_IsStubbedWithItsValue()
    {
        var stub = WorkflowStubOutputs.FromSchema(JsonNode.Parse("""{"type":["string","null"]}"""), "s0");

        Assert.Equal(JsonValueKind.String, Assert.IsAssignableFrom<JsonValue>(stub).GetValueKind());
    }

    [Fact]
    public void EveryBundledStructuredFormat_IsStubbedValid()
    {
        var schemas = TestFormats.Catalog.Formats.Values.Where(f => f.Spec.Schema is not null).ToList();

        Assert.NotEmpty(schemas);

        foreach (var format in schemas)
        {
            AssertStubbedValid(format.Id, format.Spec.Schema!);
        }
    }

    [Fact]
    public void EveryVendoredPackageFormat_IsStubbedValid()
    {
        var checkedFormats = 0;

        foreach (var package in ByteEquivalenceFixtures.Vendored)
        {
            var (_, files) = ByteEquivalenceFixtures.ReadPackage(package.Name);

            foreach (var (path, json) in files.Where(f => f.Key.StartsWith("formats/", StringComparison.Ordinal) && f.Key.EndsWith(".json", StringComparison.Ordinal)))
            {
                if (JsonNode.Parse(json)?["schema"] is { } schema)
                {
                    AssertStubbedValid($"{package.Name}/{path}", schema);
                    checkedFormats++;
                }
            }
        }

        Assert.True(checkedFormats > 0);
    }

    private static void AssertStubbedValid(string label, JsonNode schema)
    {
        foreach (var seed in Seeds)
        {
            var stub = WorkflowStubOutputs.FromSchema(schema, seed);
            var errors = JsonSchemaSubsetValidator.Validate(JsonNode.Parse(stub!.ToJsonString()), schema);

            Assert.True(errors.Count == 0, $"{label} seed {seed}: {string.Join("; ", errors)}");
        }
    }
}
