using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the member tables against the published schemas, and the
/// published conformance examples against the member rules. A valid example that does not pass is a
/// real disagreement between a table and the contract, so no example is skipped to make it pass.
/// </summary>
public class SourceShapesTests
{
    private static string SchemasDir() =>
        System.IO.Path.GetDirectoryName(V31Contract.Path("schemas", "common.schema.json"))!;

    private static string ExamplesDir() =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(V31Contract.Path("schemas", "common.schema.json"))!, "..", "conformance", "schemas");

    private static IEnumerable<string> KnownSchemas() =>
        Directory.GetFiles(SchemasDir(), "*.schema.json")
            .Select(f => System.IO.Path.GetFileName(f)[..^".schema.json".Length])
            .Where(n => SourceShapes.ForSchema(n) != null)
            .OrderBy(n => n, StringComparer.Ordinal);

    public static IEnumerable<object[]> SchemaNames() => KnownSchemas().Select(n => new object[] { n });

    private static IEnumerable<string> Examples(string prefix) =>
        KnownSchemas()
            .Select(n => System.IO.Path.Combine(ExamplesDir(), n))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, prefix + "*.json"))
            .Select(f => System.IO.Path.GetRelativePath(ExamplesDir(), f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal);

    public static IEnumerable<object[]> ValidExamples() => Examples("valid-").Select(f => new object[] { f });

    public static IEnumerable<object[]> MissingAndUnknownExamples() =>
        Examples("invalid-missing-").Concat(Examples("invalid-unknown-member")).Select(f => new object[] { f });

    private static JsonObject Example(string file) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(ExamplesDir(), file)))!;

    private static string SchemaOf(string file) => file[..file.IndexOf('/')];

    /// <summary>Checks the instance with the schema's shape, then walks into the nested declarations the library does not walk yet.</summary>
    private static List<SourceRefusal> CheckDeep(string file, JsonObject example)
    {
        var refusals = new List<SourceRefusal>();
        var instance = example["instance"] as JsonObject;
        var shape = SourceShapes.ForSchema(SchemaOf(file))!;
        if (instance is null)
            return refusals;

        SourceMembers.Check(instance, shape, "Thing", file, refusals);
        void Nested(JsonNode? node, DeclarationShape nestedShape)
        {
            if (node is JsonObject o)
                SourceMembers.Check(o, nestedShape, "Nested", file, refusals);
        }

        switch (shape.Name)
        {
            case "Manifest":
                foreach (var input in (instance["inputs"] as JsonObject)?.Select(p => p.Value) ?? [])
                    Nested(input, SourceShapes.Input);
                break;
            case "FlowIf":
                Nested(instance["then"], SourceShapes.Body);
                Nested(instance["else"], SourceShapes.Body);
                break;
            case "FlowCase":
                foreach (var branch in (instance["branches"] as JsonArray) ?? [])
                    Nested(branch, SourceShapes.Branch);
                Nested(instance["else"], SourceShapes.Body);
                break;
            case "FunctionIndex":
                foreach (var parameter in (instance["inputs"] as JsonObject)?.Select(p => p.Value) ?? [])
                    Nested(parameter, SourceShapes.Parameter);
                Nested(instance["uses"], SourceShapes.Uses);
                break;
        }

        return refusals;
    }

    [Theory]
    [MemberData(nameof(SchemaNames))]
    public void Shape_ListsExactlyTheSchemasRequiredMembers(string schema)
    {
        var required = JsonNode.Parse(File.ReadAllText(V31Contract.Path("schemas", schema + ".schema.json")))!["required"]!
            .AsArray().Select(n => (string)n!).ToHashSet();
        var shape = SourceShapes.ForSchema(schema)!;
        Assert.Equal(required, shape.Members.Select(m => m.Name).ToHashSet());
        Assert.Equal(shape.Members.Count, shape.Members.Select(m => m.Name).Distinct().Count());
    }

    [Fact]
    public void NestedShapes_ListTheirSchemasRequiredMembers()
    {
        var common = V31Contract.Json("schemas", "manifest.schema.json")["properties"]!["inputs"]!["additionalProperties"]!["required"]!;
        Assert.Equal(common.AsArray().Select(n => (string)n!), SourceShapes.Input.Members.Select(m => m.Name));
        var flowIf = V31Contract.Json("schemas", "flow-if.schema.json")["$defs"]!["body"]!["required"]!;
        Assert.Equal(flowIf.AsArray().Select(n => (string)n!), SourceShapes.Body.Members.Select(m => m.Name));
        var branch = V31Contract.Json("schemas", "flow-case.schema.json")["properties"]!["branches"]!["items"]!["required"]!;
        Assert.Equal(branch.AsArray().Select(n => (string)n!), SourceShapes.Branch.Members.Select(m => m.Name));
        var function = V31Contract.Json("schemas", "function-index.schema.json")["properties"]!;
        Assert.Equal(function["inputs"]!["additionalProperties"]!["required"]!.AsArray().Select(n => (string)n!), SourceShapes.Parameter.Members.Select(m => m.Name));
        Assert.Equal(function["uses"]!["required"]!.AsArray().Select(n => (string)n!), SourceShapes.Uses.Members.Select(m => m.Name));
    }

    [Fact]
    public void ForSchema_KnowsNoneOfTheOthers() => Assert.Null(SourceShapes.ForSchema("dialect"));

    [Fact]
    public void Shapes_CoverTheSchemas() => Assert.True(KnownSchemas().Count() >= 20);

    [Theory]
    [MemberData(nameof(ValidExamples))]
    public void EveryValidExample_PassesTheMemberRules(string file)
    {
        var example = Example(file);
        if (example["against"] != null)
            return; // judges a part of a schema, not the root shape
        var refusals = CheckDeep(file, example);
        Assert.True(refusals.Count == 0, file + ": " + string.Join(" | ", refusals.Select(r => r.Sentence)));
    }

    [Theory]
    [MemberData(nameof(MissingAndUnknownExamples))]
    public void EveryMissingOrUnknownExample_IsRefused(string file)
    {
        var example = Example(file);
        if (example["against"] != null)
            return;
        var refusals = CheckDeep(file, example);
        Assert.NotEmpty(refusals);
        var name = System.IO.Path.GetFileNameWithoutExtension(file);
        if (name.StartsWith("invalid-missing-", StringComparison.Ordinal))
        {
            var member = name["invalid-missing-".Length..];
            Assert.Contains(refusals, r => r.Id == "S01-021" && r.Sentence.Contains($"'{member}'"));
        }
        else
        {
            Assert.Contains(refusals, r => r.Id == "S01-023");
        }
    }
}
