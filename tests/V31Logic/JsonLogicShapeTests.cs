using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: the written form of a rule (part 03 §§ 2, 5, 11.1), judged with no types: objects, operations,
/// operand counts, paths and literal operands. It never throws on any tree.
/// </summary>
public class JsonLogicShapeTests
{
    private static JsonNode? Rule(string json) => JsonNode.Parse(json);

    public static TheoryData<string> Canonical => new()
    {
        """{ "or": [ { "!==": [ { "var": "item.id" }, "billing" ] }, { "===": [ { "var": "input.include_billing.value" }, true ] } ] }""",
        """{ "in": [ "letrozole", { "lower": [ { "var": "node.draft.content" } ] } ] }""",
        """{ "terms_subset": [ { "var": "node.extract_input_terms.value" }, { "var": "node.extract_note_terms.value" }, "The note does not cover every clinical term found in the referral." ] }""",
        """{ ">=": [ { "years_between": [ { "var": "input.date_of_birth.value" }, { "var": "run.date" } ] }, 65 ] }""",
        """{ "!": [ { ">": [ { "var": "input.age.value" }, 65 ] } ] }""",
        """{ "and": [ { "!==": [ { "var": "input.age.value" }, null ] }, { "<=": [ { "var": "input.age.value" }, 65 ] } ] }""",
        """{"missing":["input.age.value"]}""",
        """{"if":[true,1,2]}""",
        """{"if":[true,1,false,2,3]}""",
        """{"-":[1]}""",
        """{"object":{"title":{"var":"item.id"},"n":1}}""",
        """[1,{"var":"item.id"}]""",
        "null",
        "\"text\"",
        """{"count_pass":[{"var":"check.c.items"},0,"Too few."]}""",
    };

    [Theory]
    [MemberData(nameof(Canonical))]
    public void CanonicalRules_HaveNoFault(string json) =>
        Assert.Empty(JsonLogicShape.Check(Rule(json)));

    [Theory]
    [InlineData("""{"==":[1,1]}""", ShapeFaultKind.LooseEquality, "==", "", 0)]
    [InlineData("""{"!=":[1,1]}""", ShapeFaultKind.LooseInequality, "!=", "", 0)]
    [InlineData("""{"substr":["a",1]}""", ShapeFaultKind.Substr, "substr", "", 0)]
    [InlineData("""{"map":[[1],1]}""", ShapeFaultKind.UnknownOperation, "map", "", 0)]
    [InlineData("""{"!!":[true]}""", ShapeFaultKind.UnknownOperation, "!!", "", 0)]
    [InlineData("""{"asDouble":[1]}""", ShapeFaultKind.UnknownOperation, "asDouble", "", 0)]
    [InlineData("""{"!":true}""", ShapeFaultKind.BareOperand, "!", "", 0)]
    [InlineData("""{"<":[1,2,3]}""", ShapeFaultKind.Arity, "<", "2", 3)]
    [InlineData("""{"!":[]}""", ShapeFaultKind.Arity, "!", "1", 0)]
    [InlineData("""{"if":[true,1]}""", ShapeFaultKind.IfWithoutElse, "if", "", 0)]
    [InlineData("""{"if":[true]}""", ShapeFaultKind.Arity, "if", "3 or more", 1)]
    [InlineData("""{"missing":[]}""", ShapeFaultKind.Arity, "missing", "1 or more", 0)]
    [InlineData("""{"var":["input.a.value",0]}""", ShapeFaultKind.VarDefault, "var", "", 0)]
    [InlineData("""{"var":5}""", ShapeFaultKind.VarNotText, "var", "a number", 0)]
    [InlineData("""{"var":""}""", ShapeFaultKind.PathNotAPath, "", "", 0)]
    [InlineData("""{"var":"input..value"}""", ShapeFaultKind.PathNotAPath, "", "input..value", 0)]
    [InlineData("""{"var":"node.rows.value.0"}""", ShapeFaultKind.PathPosition, "", "node.rows.value.0", 0)]
    [InlineData("""{"missing":["node.rows.value.0"]}""", ShapeFaultKind.PathPosition, "", "node.rows.value.0", 0)]
    [InlineData("""{"missing":[{"var":"a.b"}]}""", ShapeFaultKind.ComputedLiteral, "missing", "path", 0)]
    [InlineData("""{"missing":[5]}""", ShapeFaultKind.LiteralNotText, "missing", "path", 0)]
    [InlineData("""{"object":[1]}""", ShapeFaultKind.ObjectOperand, "object", "a list", 0)]
    [InlineData("{}", ShapeFaultKind.NotARule, "", "", 0)]
    [InlineData("""{"a":1,"b":2}""", ShapeFaultKind.NotARule, "", "", 2)]
    [InlineData("""{"matches":[{"var":"a.b"},{"var":"c.d"}]}""", ShapeFaultKind.ComputedLiteral, "matches", "pattern", 0)]
    [InlineData("""{"matches":[{"var":"a.b"},5]}""", ShapeFaultKind.LiteralNotText, "matches", "pattern", 0)]
    [InlineData("""{"matches":[{"var":"a.b"},"\\d"]}""", ShapeFaultKind.PatternNotIRegexp, "", "\\d", 0)]
    [InlineData("""{"matches":[{"var":"a.b"},"a{101}"]}""", ShapeFaultKind.PatternRepeats, "", "a{101}", 101)]
    [InlineData("""{"has_word":[{"var":"a.b"},"  "]}""", ShapeFaultKind.BlankWord, "", "", 0)]
    [InlineData("""{"has_word":[{"var":"a.b"},{"var":"c.d"}]}""", ShapeFaultKind.ComputedLiteral, "has_word", "word", 0)]
    [InlineData("""{"all_pass":[{"var":"a.b"},{"cat":["a","b"]}]}""", ShapeFaultKind.ComputedLiteral, "all_pass", "failWith", 0)]
    [InlineData("""{"count_pass":[{"var":"a.b"},{"var":"c.d"},"x"]}""", ShapeFaultKind.ComputedLiteral, "count_pass", "min", 0)]
    [InlineData("""{"count_pass":[{"var":"a.b"},-1,"x"]}""", ShapeFaultKind.CountPassMin, "", "-1", 0)]
    [InlineData("""{"count_pass":[{"var":"a.b"},1.5,"x"]}""", ShapeFaultKind.CountPassMin, "", "1.5", 0)]
    [InlineData("""{"count_pass":[{"var":"a.b"},"3","x"]}""", ShapeFaultKind.CountPassMin, "", "\"3\"", 0)]
    [InlineData("""{"count_pass":[{"var":"a.b"},2147483648,"x"]}""", ShapeFaultKind.CountPassMin, "", "2147483648", 0)]
    [InlineData("""{"terms_subset":[{"var":"a.b"},{"var":"c.d"},5]}""", ShapeFaultKind.LiteralNotText, "terms_subset", "failWith", 0)]
    public void EachFault(string json, ShapeFaultKind kind, string operation, string text, int count)
    {
        var fault = Assert.Single(JsonLogicShape.Check(Rule(json)));

        Assert.Equal(kind, fault.Kind);
        Assert.Equal(operation, fault.Operation);
        Assert.Equal(text, fault.Text);
        Assert.Equal(count, fault.Count);
    }

    [Fact]
    public void LongPattern_IsTooLong_AndCarriesItsLength()
    {
        var rule = Rule($$"""{"matches":[{"var":"a.b"},"{{new string('a', 201)}}"]}""");

        var fault = Assert.Single(JsonLogicShape.Check(rule));

        Assert.Equal(ShapeFaultKind.PatternTooLong, fault.Kind);
        Assert.Equal(201, fault.Count);
    }

    [Fact]
    public void Faults_ComeInTreeOrder_AndReachInsideARefusedOperation()
    {
        var faults = JsonLogicShape.Check(Rule("""{"and":[{"==":[1,1]},{"map":[{"!=":[1,2]}]}]}"""));

        Assert.Equal(
            [ShapeFaultKind.LooseEquality, ShapeFaultKind.UnknownOperation, ShapeFaultKind.LooseInequality],
            faults.Select(f => f.Kind));
    }

    private static JsonNode Nested(int times)
    {
        JsonNode node = JsonValue.Create(true);
        for (var i = 0; i < times; i++)
            node = new JsonObject { ["!"] = new JsonArray(node) };
        return node;
    }

    [Fact]
    public void NestedTooDeep_IsOneFault()
    {
        var fault = Assert.Single(JsonLogicShape.Check(Nested(80)));

        Assert.Equal(ShapeFaultKind.TooDeep, fault.Kind);
        Assert.Empty(JsonLogicShape.Check(Nested(60)));
        Assert.Empty(JsonLogicShape.Paths(Nested(80)));
    }

    [Fact]
    public void Paths_ListsEveryRead()
    {
        var rule = Rule("""{"and":[{"===":[{"var":"input.a.value"},1]},{"missing":["run.date","input.a.value"]}]}""");

        Assert.Equal(["input.a.value", "run.date"], JsonLogicShape.Paths(rule));
    }

    [Theory]
    [InlineData("a.b", true, false)]
    [InlineData("a", true, false)]
    [InlineData("a.0", true, true)]
    [InlineData("a.b0", true, false)]
    [InlineData("", false, false)]
    [InlineData("a.", false, false)]
    [InlineData(".a", false, false)]
    public void PathWords(string path, bool isPath, bool isPosition)
    {
        Assert.Equal(isPath, JsonLogicShape.IsPath(path));
        Assert.Equal(isPosition, JsonLogicShape.IsPosition(path));
    }

    private static IEnumerable<JsonNode?> Replacements() =>
    [
        null,
        JsonValue.Create(true),
        JsonValue.Create(5),
        JsonValue.Create("x"),
        new JsonArray(),
        new JsonObject(),
        new JsonObject { ["a"] = 1, ["b"] = 2 },
        new JsonObject { ["var"] = null },
        new JsonObject { [""] = new JsonArray() },
    ];

    private static void Positions(JsonNode? node, List<object> path, List<List<object>> into)
    {
        into.Add([.. path]);
        switch (node)
        {
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    path.Add(i);
                    Positions(array[i], path, into);
                    path.RemoveAt(path.Count - 1);
                }
                break;
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    path.Add(key);
                    Positions(value, path, into);
                    path.RemoveAt(path.Count - 1);
                }
                break;
        }
    }

    private static JsonNode? Replace(JsonNode? root, List<object> path, JsonNode? replacement)
    {
        if (path.Count == 0)
            return replacement?.DeepClone();
        var copy = root!.DeepClone();
        var parent = copy;
        for (var i = 0; i < path.Count - 1; i++)
            parent = path[i] is int index ? parent![index] : parent![(string)path[i]];
        if (path[^1] is int last)
            parent![last] = replacement?.DeepClone();
        else
            parent![(string)path[^1]] = replacement?.DeepClone();
        return copy;
    }

    [Theory]
    [MemberData(nameof(Canonical))]
    public void AnyMutationOfAValidRule_NeverThrows(string json)
    {
        var rule = Rule(json);
        var positions = new List<List<object>>();
        Positions(rule, [], positions);

        foreach (var position in positions)
            foreach (var replacement in Replacements())
            {
                var mutant = Replace(rule, position, replacement);

                var faults = JsonLogicShape.Check(mutant);
                var paths = JsonLogicShape.Paths(mutant);

                Assert.NotNull(faults);
                Assert.NotNull(paths);
            }
    }
}
