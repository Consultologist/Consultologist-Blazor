using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Types;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>#1501: the evaluator behind <see cref="IJsonLogicEvaluator"/> (part 03 § 9, D14 to D17, D22), our operations over Corvus's.</summary>
public class JsonLogicEvaluatorTests
{
    private static readonly CorvusJsonLogicEvaluator Evaluator = new();

    private static JsonLogicValue Run(string ruleJson, string dataJson, IReadOnlyList<bool>? plainConverts = null) =>
        Evaluator.Compile(JsonNode.Parse(ruleJson), plainConverts).Evaluate(JsonNode.Parse(dataJson));

    private static string Eval(string ruleJson, string dataJson, IReadOnlyList<bool>? plainConverts = null)
    {
        var value = Run(ruleJson, dataJson, plainConverts);
        Assert.Null(value.Failure);
        return value.Value?.ToJsonString() ?? "null";
    }

    private static string Canonical(string json) => CanonicalJson.Write(JsonNode.Parse(json));

    private static string Text(int bytes) => new('a', bytes);

    private static string DataWithText(string text) => new JsonObject { ["t"] = text }.ToJsonString();

    [Fact]
    public void Pins()
    {
        var props = File.ReadAllText(TypeFixture.RepoFile("Directory.Packages.props"));
        Assert.Contains("<PackageVersion Include=\"Corvus.Text.Json.JsonLogic\" Version=\"5.8.12\" />", props);
        Assert.DoesNotMatch(@"Corvus\.Text\.Json\.JsonLogic""[^>]*Version=""[^""]*[*\[\(,]", props);

        var loaded = typeof(Corvus.Text.Json.JsonLogic.JsonLogicEvaluator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.StartsWith("5.8.12", loaded);
        Assert.Equal(JsonLogicOperations.EvaluatorVersion, Evaluator.Version);
    }

    private const string Billing =
        """{"or":[{"!==":[{"var":"item.id"},"billing"]},{"===":[{"var":"input.include_billing.value"},true]}]}""";

    private const string Screening =
        """{">=":[{"years_between":[{"var":"input.date_of_birth.value"},{"var":"run.date"}]},65]}""";

    [Theory]
    [InlineData(Billing, """{"item":{"id":"billing"},"input":{"include_billing":{"value":true}}}""", "true")]
    [InlineData(Billing, """{"item":{"id":"billing"},"input":{"include_billing":{"value":false}}}""", "false")]
    [InlineData(Billing, """{"item":{"id":"hpi"},"input":{"include_billing":{"value":false}}}""", "true")]
    [InlineData("""{"in":["letrozole",{"lower":[{"var":"node.draft.content"}]}]}""",
        """{"node":{"draft":{"content":"Margaret Okafor continues Letrozole 2.5 mg daily."}}}""", "true")]
    [InlineData(Screening, """{"input":{"date_of_birth":{"value":"1960-06-15"}},"run":{"date":"2026-06-15"}}""", "true")]
    [InlineData(Screening, """{"input":{"date_of_birth":{"value":"1960-06-15"}},"run":{"date":"2026-06-14"}}""", "true")]
    [InlineData(Screening, """{"input":{"date_of_birth":{"value":"1962-01-01"}},"run":{"date":"2026-06-15"}}""", "false")]
    [InlineData("""{"!":[{">":[{"var":"input.age.value"},65]}]}""", """{"input":{}}""", "true")]
    [InlineData("""{"and":[{"!==":[{"var":"input.age.value"},null]},{"<=":[{"var":"input.age.value"},65]}]}""", """{"input":{}}""", "false")]
    public void TheSpecsExamples(string rule, string data, string expected) => Assert.Equal(expected, Eval(rule, data));

    [Theory]
    [InlineData("""{"var":"item.id"}""", """{"item":{"id":"hpi"}}""", "\"hpi\"")]
    [InlineData("""{"var":"item.id"}""", """{"item":{"id":null}}""", "null")]
    [InlineData("""{"var":"item.id"}""", """{"item":{}}""", "null")]
    [InlineData("""{"===":[{"var":"input.nope.value"},null]}""", "{}", "true")]
    [InlineData("""{"===":[1,1.0]}""", "{}", "true")]
    [InlineData("""{"===":["a","a"]}""", "{}", "true")]
    [InlineData("""{"!==":[null,null]}""", "{}", "false")]
    [InlineData("""{"!":[null]}""", "{}", "true")]
    [InlineData("""{"and":[true,null]}""", "{}", "false")]
    [InlineData("""{"and":[true,true]}""", "{}", "true")]
    [InlineData("""{"or":[null,false]}""", "{}", "false")]
    [InlineData("""{"or":[false,true]}""", "{}", "true")]
    [InlineData("""{"if":[null,1,2]}""", "{}", "2")]
    [InlineData("""{"if":[false,1,true,2,3]}""", "{}", "2")]
    [InlineData("""{"missing":["input.age.value","input.name.value"]}""",
        """{"input":{"age":{"value":null},"name":{"value":""}}}""", """["input.age.value","input.name.value"]""")]
    [InlineData("""[1,{"var":"item.id"},"s"]""", """{"item":{"id":"hpi"}}""", """[1,"hpi","s"]""")]
    [InlineData("""{"in":["consult",["consult","follow_up"]]}""", "{}", "true")]
    public void Standard(string rule, string data, string expected) => Assert.Equal(Canonical(expected), Canonical(Eval(rule, data)));

    [Fact]
    public void Lazy()
    {
        const string matches = """{"matches":[{"var":"t"},"a"]}""";
        var data = DataWithText(Text(1_048_577));

        Assert.Equal("false", Eval($$"""{"and":[false,{{matches}}]}""", data));
        Assert.Equal("true", Eval($$"""{"or":[true,{{matches}}]}""", data));
        Assert.Equal("1", Eval($$"""{"if":[true,1,{{matches}}]}""", data));
    }

    [Fact]
    public void Object()
    {
        Assert.Equal(
            """{"title":"hpi","n":3,"inner":{"ok":true}}""",
            Eval("""{"object":{"title":{"var":"item.id"},"n":{"+":[1,2]},"inner":{"object":{"ok":true}}}}""", """{"item":{"id":"hpi"}}"""));
        Assert.Equal("{}", Eval("""{"object":{}}""", "{}"));
    }

    private const string ConceptsData = """{"x":{"concepts":[]},"y":{"concepts":[]}}""";

    /// <summary>One rule per operation of § 5 on literal operands (and the rule written to read <c>d.a</c>, <c>d.b</c>, <c>d.c</c>).</summary>
    private static readonly (string Name, string Literal, string Reads)[] EveryOperation =
    [
        ("var", """{"var":"x"}""", """{"var":"d.a"}"""),
        ("missing", """{"missing":["x","nope"]}""", """{"missing":["d.a","d.b"]}"""),
        ("if", """{"if":[true,1,2]}""", """{"if":[{"var":"d.a"},{"var":"d.b"},{"var":"d.c"}]}"""),
        ("===", """{"===":[1,1]}""", """{"===":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("!==", """{"!==":[1,2]}""", """{"!==":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("<", """{"<":[1,2]}""", """{"<":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("<=", """{"<=":[1,2]}""", """{"<=":[{"var":"d.a"},{"var":"d.b"}]}"""),
        (">", """{">":[1,2]}""", """{">":[{"var":"d.a"},{"var":"d.b"}]}"""),
        (">=", """{">=":[1,2]}""", """{">=":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("and", """{"and":[true,false]}""", """{"and":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("or", """{"or":[true,false]}""", """{"or":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("!", """{"!":[true]}""", """{"!":[{"var":"d.a"}]}"""),
        ("in", """{"in":["a","abc"]}""", """{"in":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("cat", """{"cat":["a",1.5]}""", """{"cat":[{"var":"d.a"},{"var":"d.b"},{"var":"d.c"}]}"""),
        ("+", """{"+":[1,2]}""", """{"+":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("-", """{"-":[3,1]}""", """{"-":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("*", """{"*":[2,3]}""", """{"*":[{"var":"d.a"},{"var":"d.b"},{"var":"d.c"}]}"""),
        ("/", """{"/":[4,2]}""", """{"/":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("%", """{"%":[5,2]}""", """{"%":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("min", """{"min":[1,2]}""", """{"min":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("max", """{"max":[1,2]}""", """{"max":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("size", """{"size":["Some **text**"]}""", """{"size":[{"var":"d.a"}]}"""),
        ("blank", """{"blank":[" "]}""", """{"blank":[{"var":"d.a"}]}"""),
        ("words", """{"words":["two words"]}""", """{"words":[{"var":"d.a"}]}"""),
        ("lines", """{"lines":["a\nb"]}""", """{"lines":[{"var":"d.a"}]}"""),
        ("lower", """{"lower":["ABC"]}""", """{"lower":[{"var":"d.a"}]}"""),
        ("trim", """{"trim":[" a "]}""", """{"trim":[{"var":"d.a"}]}"""),
        ("starts_with", """{"starts_with":["abc","a"]}""", """{"starts_with":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("ends_with", """{"ends_with":["abc","c"]}""", """{"ends_with":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("plain", """{"plain":["Some **text**"]}""", """{"plain":[{"var":"d.a"}]}"""),
        ("object", """{"object":{"a":1,"b":"x"}}""", """{"object":{"a":{"var":"d.a"},"b":{"var":"d.b"},"c":{"var":"d.c"}}}"""),
        ("matches", """{"matches":["abc","a.c"]}""", """{"matches":[{"var":"d.a"},"a.c"]}"""),
        ("has_word", """{"has_word":["a b","a"]}""", """{"has_word":[{"var":"d.a"},"a"]}"""),
        ("terms_subset", """{"terms_subset":[{"var":"x"},{"var":"y"},"Not covered."]}""", """{"terms_subset":[{"var":"d.a"},{"var":"d.b"},"Not covered."]}"""),
        ("all_pass", """{"all_pass":[[1],"Not all passed."]}""", """{"all_pass":[{"var":"d.a"},"Not all passed."]}"""),
        ("count_pass", """{"count_pass":[[1],1,"Too few passed."]}""", """{"count_pass":[{"var":"d.a"},1,"Too few passed."]}"""),
        ("days_between", """{"days_between":["2026-01-01","2026-01-31"]}""", """{"days_between":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("years_between", """{"years_between":["2000-01-01","2026-01-01"]}""", """{"years_between":[{"var":"d.a"},{"var":"d.b"}]}"""),
        ("add_days", """{"add_days":["2026-01-01",1]}""", """{"add_days":[{"var":"d.a"},{"var":"d.b"}]}"""),
    ];

    public static TheoryData<string> OperationNames()
    {
        var data = new TheoryData<string>();
        foreach (var operation in JsonLogicOperations.All)
            data.Add(operation.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public void EveryOperationRuns(string name)
    {
        var row = Assert.Single(EveryOperation, r => r.Name == name);
        Assert.Empty(JsonLogicShape.Check(JsonNode.Parse(row.Literal)));
        Assert.Empty(JsonLogicShape.Check(JsonNode.Parse(row.Reads)));
        Assert.Null(Run(row.Literal, ConceptsData).Failure);
    }

    [Fact]
    public void Plain_FollowsTheList()
    {
        const string rule = """{"cat":[{"plain":[{"var":"a"}]},"|",{"plain":[{"var":"b"}]}]}""";
        const string data = """{"a":"**x**","b":"**x**"}""";

        Assert.Equal("\"x|x\"", Eval(rule, data));
        Assert.Equal("\"x|**x**\"", Eval(rule, data, [true, false]));
        Assert.Equal("\"**x**|x\"", Eval(rule, data, [false, true]));
    }

    private static string Nested(int depth) => string.Concat(Enumerable.Repeat("""{"!":[""", depth)) + "true" + new string(']', depth).Replace("]", "]}");

    public static TheoryData<string, string> OutsideTheFormat() => new()
    {
        { """{"map":[[1,2],{"var":""}]}""", "it uses map, which is not an operation of this format" },
        { """{"==":[1,"1"]}""", "it uses ==, which is not an operation of this format" },
        { """{"!!":[1]}""", "it uses !!, which is not an operation of this format" },
        { """{"var":["a",1]}""", "it is not a rule of this format" },
        { """{"<":[1,2,3]}""", "it is not a rule of this format" },
        { """{"if":[true,1]}""", "it is not a rule of this format" },
        { "{}", "it is not a rule of this format" },
        { """{"a":1,"b":2}""", "it is not a rule of this format" },
        { Nested(80), "it is not a rule of this format" },
    };

    [Theory]
    [MemberData(nameof(OutsideTheFormat))]
    public void ARuleOutsideTheFormat_IsDeclined_NeverRun(string rule, string detail)
    {
        var compiled = Evaluator.Compile(JsonNode.Parse(rule, documentOptions: new() { MaxDepth = 512 }));

        var declined = compiled.Declined;
        Assert.NotNull(declined);
        Assert.Equal(JsonLogicFailureKind.Declined, declined.Kind);
        Assert.Equal(detail, declined.Detail);

        var value = compiled.Evaluate(JsonNode.Parse("{}"));
        Assert.Null(value.Value);
        Assert.Equal(declined, value.Failure);
        Assert.Equal(new JsonLogicDecision(false, declined), compiled.Decide(JsonNode.Parse("{}")));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    [InlineData("[]", false)]
    [InlineData("""{"missing":["input.a.value"]}""", true)]
    [InlineData("""{"missing":["x"]}""", false)]
    public void Decide(string rule, bool expected) =>
        Assert.Equal(new JsonLogicDecision(expected, null), Evaluator.Compile(JsonNode.Parse(rule)).Decide(JsonNode.Parse("""{"x":1}""")));

    [Theory]
    [InlineData("""{"+":[2,3]}""", "a number")]
    [InlineData("\"x\"", "text")]
    [InlineData("""{"object":{"a":1}}""", "an object")]
    public void Decide_FailsOnAValueThatIsNoBoolean(string rule, string kind)
    {
        var decision = Evaluator.Compile(JsonNode.Parse(rule)).Decide(JsonNode.Parse("{}"));
        Assert.False(decision.Result);
        Assert.Equal(new JsonLogicFailure(JsonLogicFailureKind.NotABoolean, kind), decision.Failure);
    }

    [Fact]
    public void Matches_FailsOverOneMebibyte_AndNeverQuotesTheText()
    {
        var text = Text(1_048_576) + "l";
        Assert.Equal(1_048_577, Encoding.UTF8.GetByteCount(text));
        var rule = Evaluator.Compile(JsonNode.Parse("""{"matches":[{"var":"t"},"letrozole"]}"""));
        var data = JsonNode.Parse(DataWithText(text));

        var value = rule.Evaluate(data);
        Assert.Null(value.Value);
        Assert.Equal(new JsonLogicFailure(JsonLogicFailureKind.MatchesTextTooLong, "1.1"), value.Failure);
        Assert.Equal(new JsonLogicDecision(false, value.Failure), rule.Decide(data));
        Assert.All(value.Failure!.Detail, c => Assert.True(c is '.' or (>= '0' and <= '9')));

        Assert.Equal("false", Eval("""{"matches":[{"var":"t"},"letrozole"]}""", DataWithText(Text(1_048_576))));
    }

    public static TheoryData<string> AnyData() =>
    [
        "null", "5", "\"x\"", "[]", "{}", """{"d":null}""",
        """{"d":{"a":{"x":1},"b":[1],"c":"2026-02-30"}}""",
        """{"d":{"a":1e308,"b":1e308,"c":true}}""",
    ];

    [Theory]
    [MemberData(nameof(AnyData))]
    public void Evaluate_NeverThrows_OnAnyData(string data)
    {
        foreach (var (name, _, reads) in EveryOperation)
        {
            var rule = Evaluator.Compile(JsonNode.Parse(reads));
            Assert.Null(rule.Declined);
            var value = rule.Evaluate(JsonNode.Parse(data));
            Assert.NotNull(value);
            Assert.True(value.Failure is null, $"{name} on {data}: {value.Failure}");
            Assert.NotNull(rule.Decide(JsonNode.Parse(data)));
        }
    }

    [Fact]
    public void Evaluate_DataThatCannotBeWritten_IsAFault()
    {
        var data = new JsonObject { ["d"] = new JsonObject { ["a"] = double.NaN } };
        var value = Evaluator.Compile(JsonNode.Parse("""{"var":"d.a"}""")).Evaluate(data);
        Assert.Equal(new JsonLogicFailure(JsonLogicFailureKind.Fault, "the evaluator failed"), value.Failure);
    }

    private static readonly System.Text.Json.JsonDocumentOptions Deep = new() { MaxDepth = 1024 };

    [Fact]
    public void ARuleAsDeepAsTheFormatAllows_Runs_AndOneDeeper_IsDeclined()
    {
        var deepest = JsonNode.Parse(Nested(JsonLogicShape.MaxDepth), documentOptions: Deep);
        var tooDeep = JsonNode.Parse(Nested(JsonLogicShape.MaxDepth + 1), documentOptions: Deep);
        Assert.Empty(JsonLogicShape.Check(deepest));
        Assert.NotEmpty(JsonLogicShape.Check(tooDeep));

        var rule = Evaluator.Compile(deepest);
        Assert.Null(rule.Declined);
        var value = rule.Evaluate(null);
        Assert.Null(value.Failure);
        Assert.Equal("true", value.Value!.ToJsonString()); // 64 negations of true
        Assert.Equal("false", Evaluator.Compile(JsonNode.Parse(Nested(JsonLogicShape.MaxDepth - 1), documentOptions: Deep)).Evaluate(null).Value!.ToJsonString());

        var declined = Evaluator.Compile(tooDeep);
        Assert.Equal(new JsonLogicFailure(JsonLogicFailureKind.Declined, "it is not a rule of this format"), declined.Declined);
    }

    private static JsonNode DeepData(int levels)
    {
        JsonNode node = JsonValue.Create(5);
        for (var i = 0; i < levels; i++)
            node = new JsonObject { ["a"] = node };
        return node;
    }

    [Fact]
    public void DataNestedDeep_IsRead_AndDeeperThanTheLimit_IsAFault()
    {
        var path = string.Join('.', Enumerable.Repeat("a", 100));
        var rule = Evaluator.Compile(JsonNode.Parse($$"""{"var":"{{path}}"}"""));

        var value = rule.Evaluate(DeepData(100));
        Assert.Null(value.Failure);
        Assert.Equal("5", value.Value!.ToJsonString());

        var whole = Evaluator.Compile(JsonNode.Parse("""{"var":"a"}""")).Evaluate(DeepData(100));
        Assert.Null(whole.Failure);
        Assert.Equal(DeepData(99).ToJsonString(new System.Text.Json.JsonSerializerOptions { MaxDepth = 1024 }),
            whole.Value!.ToJsonString(new System.Text.Json.JsonSerializerOptions { MaxDepth = 1024 }));

        Assert.Equal(new JsonLogicFailure(JsonLogicFailureKind.Fault, "the evaluator failed"), rule.Evaluate(DeepData(600)).Failure);
    }

    [Fact]
    public void CompiledOnce()
    {
        const string rule = """{"+":[{"var":"n"},1]}""";
        Assert.Same(Evaluator.Compile(JsonNode.Parse(rule)), Evaluator.Compile(JsonNode.Parse(rule)));
        Assert.NotSame(Evaluator.Compile(JsonNode.Parse(rule)), Evaluator.Compile(JsonNode.Parse(rule), [true]));

        var ab = Evaluator.Compile(JsonNode.Parse("""{"object":{"a":1,"b":2}}"""));
        var ba = Evaluator.Compile(JsonNode.Parse("""{"object":{"b":2,"a":1}}"""));
        Assert.NotSame(ab, ba);
        Assert.Equal("""{"a":1,"b":2}""", ab.Evaluate(null).Value!.ToJsonString());
        Assert.Equal("""{"b":2,"a":1}""", ba.Evaluate(null).Value!.ToJsonString());
    }

    [Fact]
    public void SharedBetweenThreads()
    {
        var evaluator = new CorvusJsonLogicEvaluator();
        string[] rules =
        [
            """{"+":[{"var":"n"},1]}""",
            """{"cat":["t",{"var":"s"}]}""",
            """{"object":{"n":{"var":"n"},"s":{"var":"s"}}}""",
            """{"if":[{">":[{"var":"n"},100]},"big","small"]}""",
        ];
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
        {
            for (var i = 0; i < 2_000; i++)
            {
                var n = t * 10_000 + i;
                var s = $"{t}-{i}";
                var data = new JsonObject { ["n"] = n, ["s"] = s };
                string[] expected =
                [
                    (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"\"t{s}\"",
                    $$"""{"n":{{n}},"s":"{{s}}"}""",
                    n > 100 ? "\"big\"" : "\"small\"",
                ];
                for (var r = 0; r < rules.Length; r++)
                {
                    var value = evaluator.Compile(JsonNode.Parse(rules[r])).Evaluate(data);
                    var got = value.Value?.ToJsonString() ?? "null";
                    if (value.Failure is not null || got != expected[r])
                        errors.Add($"thread {t}, {i}, rule {r}: {got} {value.Failure}");
                }
            }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        Assert.Empty(errors);
    }

    public static TheoryData<string, string> NullOperandRows()
    {
        var data = new TheoryData<string, string>();
        var method = typeof(JsonLogicFunctionsTests).GetMethod(nameof(JsonLogicFunctionsTests.NullOperands_FollowEachRow))!;
        foreach (var row in method.GetCustomAttributes<InlineDataAttribute>().SelectMany(a => a.GetData(method)))
            data.Add((string)row[0], (string)row[1]);
        return data;
    }

    [Theory]
    [MemberData(nameof(NullOperandRows))]
    public void NoCorvusDefaultIsReachable(string operation, string operands)
    {
        var rule = new JsonObject { [operation] = JsonNode.Parse(operands) };
        var expected = JsonLogicFunctions.Apply(operation, JsonNode.Parse(operands)!.AsArray().ToList());

        Assert.Equal(CanonicalJson.Write(expected), Canonical(Eval(rule.ToJsonString(), "{}")));
    }
}
