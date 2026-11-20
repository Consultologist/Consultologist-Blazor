using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>
/// #1501: the printer of JSON Logic and the parser of its text (part 03 § 8, D23). In the rows, <c>@a</c>,
/// <c>@b</c> and <c>@c</c> stand for the reads of <c>x.a</c>, <c>x.b</c> and <c>x.c</c>.
/// </summary>
public class JsonLogicPrinterTests
{
    private static JsonNode? Rule(string json) => JsonNode.Parse(
        json.Replace("@a", """{"var":"x.a"}""").Replace("@b", """{"var":"x.b"}""").Replace("@c", """{"var":"x.c"}"""));

    public static TheoryData<string, string> Rows => new()
    {
        { """{ "or": [ { "!==": [ { "var": "item.id" }, "billing" ] }, { "===": [ { "var": "input.include_billing.value" }, true ] } ] }""", """item.id ≠ "billing" or input.include_billing.value = true""" },
        { """{ ">=": [ { "years_between": [ { "var": "input.date_of_birth.value" }, { "var": "run.date" } ] }, 65 ] }""", "years_between(input.date_of_birth.value, run.date) ≥ 65" },
        { """{ "in": [ "letrozole", { "lower": [ { "var": "node.draft.content" } ] } ] }""", "\"letrozole\" in lower(node.draft.content)" },
        { """{"===":[{"var":"input.specialty.title"},"Medical oncology"]}""", "input.specialty.title = \"Medical oncology\"" },
        { "65", "65" },
        { "0.5", "0.5" },
        { "1e21", "1e+21" },
        { "-1", "-1" },
        { "true", "true" },
        { "false", "false" },
        { "null", "null" },
        { "\"table\"", "\"table\"" },
        { "\"a\\\"b\\n≠\"", "\"a\\\"b\\n≠\"" },
        { """[1,{"var":"a.b"},"c"]""", """[1, a.b, "c"]""" },
        { "[]", "[]" },
        { """{"<":[{"var":"a.b"},2]}""", "a.b < 2" },
        { """{"<=":[{"var":"a.b"},2]}""", "a.b ≤ 2" },
        { """{">=":[{"var":"a.b"},2]}""", "a.b ≥ 2" },
        { """{">":[{"var":"a.b"},2]}""", "a.b > 2" },
        { """{"and":[@a,@b,@c]}""", "x.a and x.b and x.c" },
        { """{"and":[{"and":[@a,@b]},@c]}""", "(x.a and x.b) and x.c" },
        { """{"or":[{"and":[@a,@b]},@c]}""", "x.a and x.b or x.c" },
        { """{"and":[{"or":[@a,@b]},@c]}""", "(x.a or x.b) and x.c" },
        { """{"!":[@a]}""", "not x.a" },
        { """{"!":[{"and":[@a,@b]}]}""", "not (x.a and x.b)" },
        { """{"!":[{"!":[@a]}]}""", "not (not x.a)" },
        { """{"+":[1,2,3]}""", "1 + 2 + 3" },
        { """{"*":[{"+":[1,2]},3]}""", "(1 + 2) * 3" },
        { """{"-":[@a,{"-":[@b,@c]}]}""", "x.a - (x.b - x.c)" },
        { """{"-":[{"-":[@a,@b]},@c]}""", "(x.a - x.b) - x.c" },
        { """{"-":[@a]}""", "-x.a" },
        { """{"-":[1]}""", "-(1)" },
        { """{"-":[@a,-1]}""", "x.a - -1" },
        { """{"/":[@a,2]}""", "x.a / 2" },
        { """{"%":[@a,2]}""", "x.a % 2" },
        { """{"in":["a",["a","b"]]}""", "\"a\" in [\"a\", \"b\"]" },
        { """{"if":[@a,1,2]}""", "if x.a then 1 else 2" },
        { """{"if":[@a,1,@b,2,3]}""", "if x.a then 1 else if x.b then 2 else 3" },
        { """{"if":[@a,1,{"if":[@b,2,3]}]}""", "if x.a then 1 else (if x.b then 2 else 3)" },
        { """{"+":[1,{"if":[@a,1,2]}]}""", "1 + (if x.a then 1 else 2)" },
        { """{"object":{"title":@a,"content":@b}}""", "{ title: x.a, content: x.b }" },
        { """{"object":{}}""", "{}" },
        { """{"object":{"my field":1}}""", """{ "my field": 1 }""" },
        { """{"size":[{"var":"input.prior_notes.value"}]}""", "size(input.prior_notes.value)" },
        { """{"missing":["input.age.value"]}""", """missing(["input.age.value"])""" },
        { """{"missing":["a.b","c.d"]}""", """missing(["a.b", "c.d"])""" },
        { """{"min":[1,{"if":[@a,1,2]}]}""", "min(1, if x.a then 1 else 2)" },
        { """{"terms_subset":[@a,@b,"The note does not cover every term."]}""", """terms_subset(x.a, x.b, "The note does not cover every term.")""" },
        { """{"var":"true"}""", """var("true")""" },
        { """{"var":"a b"}""", """var("a b")""" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Prints(string rule, string text)
    {
        Assert.Equal(text, JsonLogicPrinter.Print(Rule(rule)));
        Assert.True(JsonLogicPrinter.TryParse(text, out var parsed));
        Assert.Equal(CanonicalJson.Write(Rule(rule)), CanonicalJson.Write(parsed));
    }

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("""{"a":1,"b":2}""", """{"a":1,"b":2}""")]
    [InlineData("""{"!":true}""", "!(true)")]
    [InlineData("""{"<":[1,2,3]}""", "<(1, 2, 3)")]
    [InlineData("""{"map":[[1],2]}""", "map([1], 2)")]
    [InlineData("""{"var":5}""", "var(5)")]
    [InlineData("""{"==":[1,2]}""", "==(1, 2)")]
    public void Print_IsTotal(string rule, string text) =>
        Assert.Equal(text, JsonLogicPrinter.Print(Rule(rule)));

    [Fact]
    public void Print_IsTotal_ForALoneSurrogateInAStringLiteral()
    {
        var rule = new JsonObject { ["=="] = new JsonArray { new JsonObject { ["var"] = "x.a" }, JsonValue.Create("\ud800") } };
        Assert.False(string.IsNullOrEmpty(JsonLogicPrinter.Print(rule)));
        Assert.False(string.IsNullOrEmpty(JsonLogicPrinter.Print(JsonValue.Create("\ud800"))));
    }

    [Fact]
    public void Print_IsTotal_PastTheDepthLimit()
    {
        JsonNode? rule = JsonValue.Create(1);
        for (var i = 0; i < 200; i++)
            rule = new JsonObject { ["!"] = new JsonArray { rule } };
        var text = JsonLogicPrinter.Print(rule);
        Assert.Contains("…", text);
        Assert.False(JsonLogicPrinter.TryParse(text, out _));
        Assert.False(JsonLogicPrinter.TryParse(new string('(', 500) + "1" + new string(')', 500), out _));
    }

    [Fact]
    public void Reason_IsTheRowOfTheSentences()
    {
        var published = V31Contract.Json("sentences.json")["sentences"]!.AsArray()
            .Single(s => s!["id"]!.GetValue<string>() == "S03-001")!["sentence"]!.GetValue<string>();
        var rule = Rule("""{"===":[{"var":"input.kind.value"},"table"]}""");
        Assert.Equal(published, JsonLogicPrinter.Reason("skipped", rule, false));
        Assert.Equal("1.0.0", JsonLogicPrinter.Version);
    }

    [Theory]
    [InlineData("-1", """{"-":[1]}""")]
    [InlineData("""{"and":[@a,@b,@c]}""", """{"and":[{"and":[@a,@b]},@c]}""")]
    [InlineData("""{"if":[@a,1,@b,2,3]}""", """{"if":[@a,1,{"if":[@b,2,3]}]}""")]
    [InlineData("""{"-":[@a,{"-":[@b,@c]}]}""", """{"-":[{"-":[@a,@b]},@c]}""")]
    [InlineData("\"x.a and x.b\"", """{"and":[@a,@b]}""")]
    [InlineData("""{"var":"true"}""", "true")]
    [InlineData("""{"var":"a.b"}""", "\"a.b\"")]
    [InlineData("""{"+":[1,2,3]}""", """{"+":[{"+":[1,2]},3]}""")]
    public void DifferentTrees_PrintDifferently(string one, string other) =>
        Assert.NotEqual(JsonLogicPrinter.Print(Rule(one)), JsonLogicPrinter.Print(Rule(other)));

    [Theory]
    [InlineData("x.a + x.b - x.c")]
    [InlineData("x.a and")]
    [InlineData("(x.a")]
    [InlineData("x.a = = 1")]
    [InlineData("\"unterminated")]
    [InlineData("")]
    [InlineData("x.a = x.b = x.c")]
    [InlineData("x.a - x.b - x.c")]
    [InlineData("x.a and x.b or x.c and")]
    [InlineData("x.a  and x.b")]
    [InlineData("(x.a)")]
    [InlineData("x.a <= 1")]
    [InlineData("1.0")]
    [InlineData("if x.a then 1")]
    [InlineData("1 + if x.a then 1 else 2")]
    [InlineData("not not x.a")]
    [InlineData("x.a @ 1")]
    [InlineData("\"\\ud800\"")]
    public void TryParse_RefusesWhatThePrinterNeverWrites(string text)
    {
        Assert.False(JsonLogicPrinter.TryParse(text, out var rule));
        Assert.Null(rule);
    }

    [Fact]
    public void TryParse_RefusesNull() => Assert.False(JsonLogicPrinter.TryParse(null!, out _));

    private static readonly string[] Paths = ["x.a", "input.age.value", "item.id", "run.date", "node.n.value", "a b", "true", "in.x", "title", "x.y_z"];

    private static readonly string[] Names = ["title", "content", "my field", "a1", "in"];

    private static JsonNode? Literal(Random random) => random.Next(12) switch
    {
        0 => null,
        1 => JsonValue.Create(true),
        2 => JsonValue.Create(false),
        3 => JsonValue.Create(0),
        4 => JsonValue.Create(-1),
        5 => JsonValue.Create(65),
        6 => JsonValue.Create(0.5),
        7 => JsonValue.Create(1e21),
        8 => JsonValue.Create(""),
        9 => JsonValue.Create("table"),
        10 => JsonValue.Create("a\"b"),
        _ => JsonValue.Create(random.Next(2) == 0 ? " and " : "≠"),
    };

    private static JsonNode? Leaf(Random random)
    {
        switch (random.Next(3))
        {
            case 0:
                return new JsonObject { ["var"] = Paths[random.Next(Paths.Length)] };
            case 1:
                var array = new JsonArray();
                for (var i = random.Next(3); i > 0; i--)
                    array.Add(random.Next(2) == 0 ? Literal(random) : new JsonObject { ["var"] = Paths[random.Next(Paths.Length)] });
                return array;
            default:
                return Literal(random);
        }
    }

    private static JsonNode? Generate(Random random, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
            return Leaf(random);

        var operation = JsonLogicOperations.All[random.Next(JsonLogicOperations.All.Count)];
        JsonNode? Operand() => Generate(random, depth - 1);

        switch (operation.Name)
        {
            case "var":
                return Leaf(new Random(random.Next())) is JsonObject v && v.ContainsKey("var") ? v : new JsonObject { ["var"] = Paths[random.Next(Paths.Length)] };
            case "missing":
            {
                var paths = new JsonArray();
                for (var i = 1 + random.Next(3); i > 0; i--)
                    paths.Add(Paths[random.Next(Paths.Length)]);
                return new JsonObject { ["missing"] = paths };
            }
            case "object":
            {
                var fields = new JsonObject();
                for (var i = random.Next(4); i > 0; i--)
                    fields[Names[random.Next(Names.Length)]] = Operand();
                return new JsonObject { ["object"] = fields };
            }
        }

        var max = Math.Min(operation.Max ?? 4, 4);
        var count = operation.Min + random.Next(max - operation.Min + 1);
        if (operation.Name == "if" && count % 2 == 0)
            count = count == 4 ? 3 : count + 1;
        var operands = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            operands.Add(operation.Name switch
            {
                "matches" when i == 1 => JsonValue.Create(random.Next(2) == 0 ? "a+" : "[a-z]*"),
                "has_word" when i == 1 => JsonValue.Create(random.Next(2) == 0 ? "letrozole" : "a b"),
                "terms_subset" when i == 2 => JsonValue.Create("Not every term."),
                "all_pass" when i == 1 => JsonValue.Create("A check failed."),
                "count_pass" when i == 1 => JsonNode.Parse(random.Next(3).ToString()),
                "count_pass" when i == 2 => JsonValue.Create("Too few."),
                _ => Operand(),
            });
        }

        return new JsonObject { [operation.Name] = operands };
    }

    [Fact]
    public void EveryGeneratedRule_RoundTrips()
    {
        var random = new Random(31);
        var names = new HashSet<string>();
        for (var n = 0; n < 3000; n++)
        {
            var rule = Generate(random, 4);
            var json = rule?.ToJsonString() ?? "null";
            if (rule is JsonObject o)
                names.Add(o.First().Key);
            Assert.Empty(JsonLogicShape.Check(rule));
            var text = JsonLogicPrinter.Print(rule);
            Assert.True(JsonLogicPrinter.TryParse(text, out var parsed), $"{text}  <=  {json}");
            Assert.Equal(json, parsed?.ToJsonString() ?? "null");
        }

        Assert.True(names.Count >= 30, "the generator reaches most operations at the root");
    }
}
