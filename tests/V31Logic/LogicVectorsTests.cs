using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: the suite's goldens and vectors of JSON Logic (D29). Every operation has its golden file, its printer row
/// and its null vectors; every case runs; and the suite pinned in <c>external/consultologist-package-format</c> is,
/// byte for byte, what <see cref="LogicVectors.Render"/> gives. The pinned test fails, never skips, when the folder
/// is missing.
/// </summary>
public class LogicVectorsTests
{
    private static readonly CorvusJsonLogicEvaluator Evaluator = new();

    [Fact]
    public void EveryOperation_HasAGolden_APrinterRow_AndItsNullVectors()
    {
        var files = LogicVectors.All();
        var goldens = files.Where(f => f.Family == LogicVectors.OperationGoldens).Select(f => f.Name).Order(StringComparer.Ordinal);
        Assert.Equal(JsonLogicOperations.All.Where(o => o.Custom).Select(o => o.Name).Order(StringComparer.Ordinal), goldens);

        var printed = OperationsOf(Named(files, LogicVectors.PrinterGoldens, "operations"));
        var nulls = files.Where(f => f.Family == LogicVectors.NullVectors)
            .SelectMany(f => f.Cases)
            .SelectMany(c => Operations(JsonNode.Parse(c.Rule)))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var operation in JsonLogicOperations.All)
        {
            Assert.True(printed.Contains(operation.Name), $"printer-goldens/operations.json does not print {operation.Name}.");
            Assert.True(nulls.Contains(operation.Name), $"no null vector names {operation.Name}.");
        }

        // Each golden file holds at least four cases, each about its own operation.
        foreach (var golden in files.Where(f => f.Family == LogicVectors.OperationGoldens))
        {
            Assert.True(golden.Cases.Count >= 4, $"operation-goldens/{golden.Name}.json has {golden.Cases.Count} cases.");
            Assert.All(golden.Cases, c => Assert.Contains(golden.Name, Operations(JsonNode.Parse(c.Rule))));
        }
    }

    [Fact]
    public void EveryCase_Runs()
    {
        var files = LogicVectors.All();
        Assert.Equal(files.Count, files.Select(f => (f.Family, f.Name)).Distinct().Count());
        Assert.All(files, f => Assert.Contains(f.Family, LogicVectors.Families));

        foreach (var file in files)
        {
            Assert.False(string.IsNullOrWhiteSpace(file.About), $"{file.Family}/{file.Name} has no about.");
            Assert.NotEmpty(file.Cases);
            Assert.Equal(file.Cases.Count, file.Cases.Select(c => c.About).Distinct(StringComparer.Ordinal).Count());
            foreach (var logicCase in file.Cases)
            {
                var where = $"{file.Family}/{file.Name}: {logicCase.About}";
                Assert.False(string.IsNullOrWhiteSpace(logicCase.About), where);
                var rule = JsonNode.Parse(logicCase.Rule);
                Assert.True(JsonLogicShape.Check(rule).Count == 0, $"{where}: a fault of the written form.");
                Assert.True(JsonNode.Parse(logicCase.Data) is JsonObject, $"{where}: the data is not an object.");

                var compiled = Evaluator.Compile(rule);
                Assert.Null(compiled.Declined);
                var value = compiled.Evaluate(JsonNode.Parse(logicCase.Data));
                Assert.True(value.Failure is null, $"{where}: {value.Failure?.Detail}");

                if (file.Family == LogicVectors.PrinterGoldens)
                {
                    var text = JsonLogicPrinter.Print(rule);
                    Assert.True(JsonLogicPrinter.TryParse(text, out var parsed), $"{where}: '{text}' does not parse.");
                    Assert.Equal(CanonicalJson.Write(rule), CanonicalJson.Write(parsed));
                }
            }
        }
    }

    [Fact]
    public void Render_FollowsTheConventions()
    {
        foreach (var file in LogicVectors.All())
        {
            var text = LogicVectors.Render(file);
            Assert.EndsWith("}\n", text);
            Assert.DoesNotContain('\r', text);
            Assert.StartsWith("{\n  \"about\": ", text);

            var cases = JsonNode.Parse(text)!["cases"]!.AsArray();
            string[] members = file.Family switch
            {
                LogicVectors.PrinterGoldens => ["about", "rule", "text"],
                LogicVectors.OperationGoldens => ["about", "rule", "data", "output"],
                _ => ["about", "rule", "data", "result"],
            };
            Assert.All(cases, c => Assert.Equal(members, c!.AsObject().Select(m => m.Key)));
        }

        var lower = LogicVectors.Render(Named(LogicVectors.All(), LogicVectors.TextVectors, "lower"));
        Assert.Contains("𐐀", lower);
        Assert.Contains("Ω", lower);
        Assert.DoesNotContain("\\u", lower);
        Assert.Contains("\\u000B", LogicVectors.Render(Named(LogicVectors.All(), LogicVectors.TextVectors, "trim")));
    }

    [Fact]
    public void ThePinnedSuite_IsWhatTheCodeGives()
    {
        var files = LogicVectors.All();
        var index = V31Contract.Json("conformance", "index.json")["families"]!.AsArray();
        foreach (var family in LogicVectors.Families)
        {
            var names = files.Where(f => f.Family == family).Select(f => f.Name + ".json").Order(StringComparer.Ordinal).ToList();
            var entry = index.Single(f => f!["id"]!.GetValue<string>() == family)!;
            Assert.Equal(names, entry["cases"]!.AsArray().Select(c => c!.GetValue<string>()).ToList());

            var folder = Path.GetDirectoryName(V31Contract.Path("conformance", family, names[0]))!;
            Assert.Equal(names, Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToList());
        }

        foreach (var file in files)
        {
            var pinned = File.ReadAllText(V31Contract.Path("conformance", file.Family, file.Name + ".json"), Encoding.UTF8);
            Assert.True(pinned == LogicVectors.Render(file), $"{file.Family}/{file.Name}.json is not what the code gives.");
        }
    }

    private static LogicFile Named(IReadOnlyList<LogicFile> files, string family, string name) =>
        files.Single(f => f.Family == family && f.Name == name);

    private static HashSet<string> OperationsOf(LogicFile file) =>
        file.Cases.SelectMany(c => Operations(JsonNode.Parse(c.Rule))).ToHashSet(StringComparer.Ordinal);

    /// <summary>The operations a rule names, at any depth: <c>var</c>'s path and <c>object</c>'s field names are not operations.</summary>
    private static IEnumerable<string> Operations(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var name in array.SelectMany(Operations))
                    yield return name;
                break;
            case JsonObject obj when obj.Count == 1:
                var (operation, operands) = obj.First();
                yield return operation;
                if (operation == "var")
                    break;
                var children = operation == "object" && operands is JsonObject fields ? fields.Select(f => f.Value) : [operands];
                foreach (var name in children.SelectMany(Operations))
                    yield return name;
                break;
        }
    }
}
