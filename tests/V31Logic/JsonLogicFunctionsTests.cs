using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Logic;

/// <summary>#1501: what each operation computes (part 03 §§ 5.1 to 5.5, D18 to D21), with its null rules.</summary>
public class JsonLogicFunctionsTests
{
    private static string Run(string operation, string operandsJson, bool plainConverts = true)
    {
        var operands = JsonNode.Parse(operandsJson)!.AsArray().Select(n => n).ToList();
        return CanonicalJson.Write(JsonLogicFunctions.Apply(operation, operands, plainConverts));
    }

    private static string RunRaw(string operation, string operandsJson) =>
        JsonLogicFunctions.Apply(operation, JsonNode.Parse(operandsJson)!.AsArray().Select(n => n).ToList())!.ToJsonString();

    private static void Expect(string operation, string operands, string expected) =>
        Assert.Equal(CanonicalJson.Write(JsonNode.Parse(expected)), Run(operation, operands));

    [Theory]
    [InlineData("size", """["héllo"]""", "5")]
    [InlineData("size", """["👩‍⚕"]""", "3")]
    [InlineData("size", """["👩‍⚕️"]""", "4")]
    [InlineData("size", "[[1,2,3]]", "3")]
    [InlineData("size", """[""]""", "0")]
    [InlineData("blank", """[""]""", "true")]
    [InlineData("blank", """["  \t  "]""", "true")]
    [InlineData("blank", """["a"]""", "false")]
    [InlineData("words", """["  chest   pain\nworse "]""", "3")]
    [InlineData("words", """[""]""", "0")]
    [InlineData("lines", """[""]""", "0")]
    [InlineData("lines", """["a"]""", "1")]
    [InlineData("lines", """["a\n"]""", "1")]
    [InlineData("lines", """["a\nb"]""", "2")]
    [InlineData("lines", """["a\r\nb\rc"]""", "3")]
    [InlineData("lines", """["\n"]""", "1")]
    [InlineData("lines", """["\n\n"]""", "2")]
    [InlineData("trim", """["  pain  \n"]""", "\"pain\"")]
    [InlineData("starts_with", """["Dear Dr Sample","Dear"]""", "true")]
    [InlineData("starts_with", """["dear","Dear"]""", "false")]
    [InlineData("ends_with", """["note.","."]""", "true")]
    [InlineData("in", """["pain","chest pain"]""", "true")]
    [InlineData("in", """["Pain","chest pain"]""", "false")]
    [InlineData("in", """["","abc"]""", "true")]
    public void Text(string operation, string operands, string expected) => Expect(operation, operands, expected);

    [Fact]
    public void Lower_UsesTheInvariantCulture()
    {
        const string text = "LETROZOLE Ω İ";
        Assert.Equal(CanonicalJson.Write(JsonValue.Create(text.ToLowerInvariant())), Run("lower", $"[\"{text}\"]"));
    }

    [Theory]
    [InlineData("size", """["a😀b"]""", "3")]
    [InlineData("has_word", """["𝒳pain","pain"]""", "false")]
    [InlineData("has_word", """["😀pain😀","pain"]""", "true")]
    [InlineData("trim", """[" 😀 "]""", "\"😀\"")]
    [InlineData("trim", """["  x "]""", "\"x\"")]
    [InlineData("blank", """["  "]""", "true")]
    public void Text_IsCountedInCodePoints(string operation, string operands, string expected) => Expect(operation, operands, expected);

    [Theory]
    [InlineData("painful", "pain", false)]
    [InlineData("Spain", "pain", false)]
    [InlineData("pain.", "pain", true)]
    [InlineData("chest pain today", "chest pain", true)]
    [InlineData("pain_scale", "pain", false)]
    [InlineData("pain2", "pain", false)]
    [InlineData("paín pain", "pain", true)]
    [InlineData("painpain pain", "pain", true)]
    [InlineData("Pain", "pain", false)]
    public void HasWord(string text, string word, bool expected) =>
        Assert.Equal(expected, JsonLogicFunctions.Apply("has_word", [JsonValue.Create(text), JsonValue.Create(word)])!.GetValue<bool>());

    [Theory]
    [InlineData("started Femara", false)]
    [InlineData("started femara", true)]
    public void Matches(string text, bool expected) =>
        Assert.Equal(expected, JsonLogicFunctions.Apply("matches", [JsonValue.Create(text), JsonValue.Create("letrozole|femara")])!.GetValue<bool>());

    [Fact]
    public void Matches_InvalidPattern_IsFalse_NotAThrow() =>
        Assert.Equal("false", Run("matches", """["abc","(a"]"""));

    [Theory]
    [InlineData("+", "[1,2,3]", "6")]
    [InlineData("+", "[0.1,0.2]", "0.30000000000000004")]
    [InlineData("-", "[5]", "-5")]
    [InlineData("-", "[5,3]", "2")]
    [InlineData("*", "[2,3,4]", "24")]
    [InlineData("/", "[1,4]", "0.25")]
    [InlineData("/", "[1,0]", "null")]
    [InlineData("%", "[7,3]", "1")]
    [InlineData("%", "[-7,3]", "-1")]
    [InlineData("%", "[1,0]", "null")]
    [InlineData("min", "[3,1,2]", "1")]
    [InlineData("max", "[3]", "3")]
    [InlineData("*", "[1e308,10]", "null")]
    [InlineData("<", "[1,2]", "true")]
    [InlineData("<=", "[2,2]", "true")]
    [InlineData(">", "[1,2]", "false")]
    [InlineData("in", "[2,[1,2.0]]", "true")]
    [InlineData("in", """["a",["b"]]""", "false")]
    [InlineData("cat", """["a",1.50,"b"]""", "\"a1.5b\"")]
    [InlineData("cat", """["n=",1e21]""", "\"n=1e+21\"")]
    public void Numbers(string operation, string operands, string expected) => Expect(operation, operands, expected);

    [Fact]
    public void Numbers_BuiltInCodeAgreeWithParsed()
    {
        JsonNode?[] built = [JsonValue.Create(1), JsonValue.Create(2L), JsonValue.Create(0.5), JsonValue.Create(1.25m)];
        Assert.Equal("4.75", CanonicalJson.Write(JsonLogicFunctions.Apply("+", built)));
        Assert.Equal("a1b", JsonLogicFunctions.Apply("cat", [JsonValue.Create("a"), JsonValue.Create(1), JsonValue.Create("b")])!.GetValue<string>());
        Assert.Equal("true", CanonicalJson.Write(JsonLogicFunctions.Apply("in", [JsonValue.Create(2), new JsonArray(1, 2L)])));
    }

    [Theory]
    [InlineData("days_between", """["2026-01-01","2026-01-31"]""", "30")]
    [InlineData("days_between", """["2026-01-31","2026-01-01"]""", "-30")]
    [InlineData("days_between", """["2024-02-28","2024-03-01"]""", "2")]
    [InlineData("years_between", """["2008-02-29","2009-02-28"]""", "0")]
    [InlineData("years_between", """["2008-02-29","2009-03-01"]""", "1")]
    [InlineData("years_between", """["2008-02-29","2012-02-28"]""", "3")]
    [InlineData("years_between", """["2008-02-29","2012-02-29"]""", "4")]
    [InlineData("years_between", """["1960-06-15","2026-06-14"]""", "65")]
    [InlineData("years_between", """["1960-06-15","2026-06-15"]""", "66")]
    [InlineData("years_between", """["2026-06-15","1960-06-15"]""", "-66")]
    [InlineData("years_between", """["2026-06-14","1960-06-15"]""", "-65")]
    [InlineData("add_days", """["2026-12-31",1]""", "\"2027-01-01\"")]
    [InlineData("add_days", """["2024-02-28",2]""", "\"2024-03-01\"")]
    [InlineData("add_days", """["2026-01-01",-1]""", "\"2025-12-31\"")]
    [InlineData("add_days", """["2026-01-01",1.5]""", "null")]
    [InlineData("add_days", """["9999-12-31",1]""", "null")]
    [InlineData("add_days", """["0001-01-01",-1]""", "null")]
    [InlineData("add_days", """["2026-01-01",1e300]""", "null")]
    [InlineData("<", """["2026-01-02","2026-01-10"]""", "true")]
    [InlineData(">=", """["2026-01-02","2026-01-02"]""", "true")]
    public void Dates(string operation, string operands, string expected) => Expect(operation, operands, expected);

    [Theory]
    [InlineData("<", "[null,1]", "false")]
    [InlineData(">", "[null,1]", "false")]
    [InlineData("<=", "[null,1]", "false")]
    [InlineData(">=", "[null,1]", "false")]
    [InlineData("<", "[1,null]", "false")]
    [InlineData(">", "[1,null]", "false")]
    [InlineData("<=", "[1,null]", "false")]
    [InlineData(">=", "[1,null]", "false")]
    [InlineData("+", "[1,null]", "null")]
    [InlineData("*", "[null,2]", "null")]
    [InlineData("-", "[null]", "null")]
    [InlineData("-", "[1,null]", "null")]
    [InlineData("/", "[null,1]", "null")]
    [InlineData("%", "[1,null]", "null")]
    [InlineData("min", "[1,null]", "null")]
    [InlineData("max", "[null]", "null")]
    [InlineData("in", "[null,[1,null]]", "true")]
    [InlineData("in", "[null,[1]]", "false")]
    [InlineData("in", "[1,null]", "false")]
    [InlineData("in", """[null,"abc"]""", "false")]
    [InlineData("in", """["a",null]""", "false")]
    [InlineData("cat", """["a",null,"b"]""", "\"ab\"")]
    [InlineData("size", "[null]", "null")]
    [InlineData("words", "[null]", "null")]
    [InlineData("lines", "[null]", "null")]
    [InlineData("lower", "[null]", "null")]
    [InlineData("trim", "[null]", "null")]
    [InlineData("plain", "[null]", "null")]
    [InlineData("blank", "[null]", "true")]
    [InlineData("starts_with", """[null,"a"]""", "false")]
    [InlineData("starts_with", """["a",null]""", "false")]
    [InlineData("ends_with", """[null,"a"]""", "false")]
    [InlineData("ends_with", """["a",null]""", "false")]
    [InlineData("matches", """[null,"a"]""", "false")]
    [InlineData("has_word", """[null,"a"]""", "false")]
    [InlineData("days_between", """[null,"2026-01-01"]""", "null")]
    [InlineData("years_between", """["2026-01-01",null]""", "null")]
    [InlineData("add_days", "[null,1]", "null")]
    [InlineData("add_days", """["2026-01-01",null]""", "null")]
    public void NullOperands_FollowEachRow(string operation, string operands, string expected) => Expect(operation, operands, expected);

    [Theory]
    [InlineData("+", """["3",1]""", "null")]
    [InlineData("lower", "[5]", "null")]
    [InlineData("size", """[{"a":1}]""", "null")]
    [InlineData("size", "[5]", "null")]
    [InlineData("<", """[5,"a"]""", "false")]
    [InlineData("<", """["a","b"]""", "false")]
    [InlineData("<", """["2026-02-30","2026-03-01"]""", "false")]
    [InlineData("days_between", """["2026-2-3","2026-02-04"]""", "null")]
    [InlineData("years_between", """["2026-02-30","2027-01-01"]""", "null")]
    [InlineData("cat", """["a",true,{"x":1}]""", "\"a\"")]
    [InlineData("blank", "[5]", "true")]
    [InlineData("starts_with", """[5,"5"]""", "false")]
    [InlineData("add_days", """["2026-01-01","1"]""", "null")]
    public void OperandsOfTheWrongKind_ReadAsNull(string operation, string operands, string expected) => Expect(operation, operands, expected);

    private const string Concepts =
        """{"concepts":[{"term":"hot flushes","type":"finding","id":"1","isSnomedConcept":true,"isActive":true,"support":null},""" +
        """{"term":"joint pain","type":"finding","id":"2","isSnomedConcept":true,"isActive":true,"support":null},""" +
        """{"term":"old term","type":"finding","id":"3","isSnomedConcept":true,"isActive":false,"support":null},""" +
        """{"term":"free text","type":"finding","id":null,"isSnomedConcept":true,"isActive":true,"support":null}]}""";

    private const string Flushes =
        """{"concepts":[{"term":"hot flushes","type":"finding","id":"1","isSnomedConcept":true,"isActive":true,"support":null}]}""";

    private static string Subset(string of, string inList) =>
        RunRaw("terms_subset", $"[{of},{inList},\"Not every term is covered.\"]");

    [Fact]
    public void TermsSubset()
    {
        var empty = Subset("""{"concepts":[]}""", "null");
        Assert.Equal("""{"pass":true,"message":null,"uncovered":null,"untested":null,"passed":null,"total":null}""", empty);

        Assert.Equal(
            """{"pass":true,"message":null,"uncovered":null,"untested":["old term","free text"],"passed":null,"total":null}""",
            Subset(Flushes, Concepts));

        // one codable term missing from the document
        Assert.Equal(
            """{"pass":false,"message":"Not every term is covered.","uncovered":["joint pain"],"untested":["old term","free text"],"passed":null,"total":null}""",
            Subset(Concepts, Flushes));

        // untested: from both lists, distinct
        var both = JsonNode.Parse(Subset(Flushes, Concepts))!;
        Assert.Equal(["old term", "free text"], both["untested"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.True(both["pass"]!.GetValue<bool>());

        // a term repeated in `of` is listed once
        var twice = Concepts.Replace("{\"term\":\"old term\"", "{\"term\":\"free text\",\"type\":\"x\",\"id\":null,\"isSnomedConcept\":true,\"isActive\":true,\"support\":null},{\"term\":\"old term\"");
        var repeated = JsonNode.Parse(Subset(twice, "null"))!;
        Assert.Equal(1, repeated["untested"]!.AsArray().Count(n => n!.GetValue<string>() == "free text"));

        // null `in`: every codable term uncovered
        var none = JsonNode.Parse(Subset(Concepts, "null"))!;
        Assert.Equal(["hot flushes", "joint pain"], none["uncovered"]!.AsArray().Select(n => n!.GetValue<string>()));

        Assert.Equal(empty, Subset("null", Flushes));
        Assert.Equal(empty, Subset("5", Flushes));
    }

    private const string Pass = """{"pass":true,"message":null,"uncovered":null,"untested":null,"passed":null,"total":null}""";
    private const string Fail = """{"pass":false,"message":"x","uncovered":null,"untested":null,"passed":null,"total":null}""";

    private static string Result(bool pass, int passed, int total, string? message) =>
        $$"""{"pass":{{(pass ? "true" : "false")}},"message":{{(message is null ? "null" : $"\"{message}\"")}},"uncovered":null,"untested":null,"passed":{{passed}},"total":{{total}}}""";

    [Fact]
    public void AllPass_AndCountPass()
    {
        Assert.Equal(Result(true, 2, 2, null), RunRaw("all_pass", $"[[{Pass},{Pass}],\"No.\"]"));
        Assert.Equal(Result(false, 1, 2, "No."), RunRaw("all_pass", $"[[{Pass},{Fail}],\"No.\"]"));
        Assert.Equal(Result(false, 0, 0, "No."), RunRaw("all_pass", """[[],"No."]"""));
        Assert.Equal(Result(false, 0, 0, "No."), RunRaw("all_pass", """[null,"No."]"""));

        var items = $$"""[{"state":"completed","value":{{Pass}}},{"state":"skipped","value":null},{"state":"not_run","value":null}]""";
        Assert.Equal(Result(true, 1, 1, null), RunRaw("all_pass", $"[{items},\"No.\"]"));

        Assert.Equal(Result(true, 1, 1, null), RunRaw("all_pass", $"[[{Pass},5,{{\"state\":\"completed\",\"value\":{{\"x\":1}}}}],\"No.\"]"));

        Assert.Equal(Result(true, 0, 0, null), RunRaw("count_pass", """[null,0,"No."]"""));
        Assert.Equal(Result(false, 1, 1, "No."), RunRaw("count_pass", $"[[{Pass}],2,\"No.\"]"));
        Assert.Equal(Result(true, 1, 1, null), RunRaw("count_pass", $"[[{Pass}],1,\"No.\"]"));
    }

    [Fact]
    public void CheckResults_KeepTheFormatsMemberOrder()
    {
        var order = new[] { "pass", "message", "uncovered", "untested", "passed", "total" };
        var node = JsonLogicFunctions.Apply("all_pass", [new JsonArray(), JsonValue.Create("No.")])!.AsObject();
        Assert.Equal(order, node.Select(m => m.Key));
        var subset = JsonLogicFunctions.Apply("terms_subset", [null, null, JsonValue.Create("No.")])!.AsObject();
        Assert.Equal(order, subset.Select(m => m.Key));
    }

    [Fact]
    public void Matches_TextOverTheLimit_Fails()
    {
        var over = new string('a', IRegexp.MaxTextBytes + 1);
        var thrown = Assert.Throws<JsonLogicFailureException>(() => JsonLogicFunctions.Apply("matches", [JsonValue.Create(over), JsonValue.Create("a")]));
        Assert.Equal(JsonLogicFailureKind.MatchesTextTooLong, thrown.Failure.Kind);
        Assert.Equal("1.1", thrown.Failure.Detail);

        var exact = new string('a', IRegexp.MaxTextBytes);
        Assert.True(JsonLogicFunctions.Apply("matches", [JsonValue.Create(exact), JsonValue.Create("a")])!.GetValue<bool>());
        Assert.Equal(IRegexp.MaxTextBytes, Encoding.UTF8.GetByteCount(exact));
    }

    [Fact]
    public void Plain_ConvertsOnlyWhenTold()
    {
        Assert.Equal("a", JsonLogicFunctions.Apply("plain", [JsonValue.Create("**a**")])!.GetValue<string>());
        Assert.Equal("**a**", JsonLogicFunctions.Apply("plain", [JsonValue.Create("**a**")], plainConverts: false)!.GetValue<string>());
    }

    [Fact]
    public void Apply_ReturnsNewNodes_AndRefusesOtherNames()
    {
        var text = JsonValue.Create("abc");
        Assert.NotSame(text, JsonLogicFunctions.Apply("plain", [text], plainConverts: false));
        Assert.Throws<ArgumentException>(() => JsonLogicFunctions.Apply("var", [text]));
        Assert.Throws<ArgumentException>(() => JsonLogicFunctions.Apply("nope", [text]));
    }

    private static readonly JsonNode Data = JsonNode.Parse(
        """{"input":{"age":{"value":null},"name":{"value":""},"kind":{"value":"table"}},"list":[1]}""")!;

    [Fact]
    public void Read_AndMissing()
    {
        Assert.Equal("table", JsonLogicFunctions.Read(Data, "input.kind.value")!.GetValue<string>());
        Assert.Null(JsonLogicFunctions.Read(Data, "input.nope.value"));
        Assert.Null(JsonLogicFunctions.Read(Data, "input.kind.value.x"));
        Assert.Null(JsonLogicFunctions.Read(Data, "list.0"));
        Assert.Same(Data["input"], JsonLogicFunctions.Read(Data, "input"));

        var missing = JsonLogicFunctions.Missing(
            ["input.age.value", "input.name.value", "input.kind.value", "input.nope.value"],
            path => JsonLogicFunctions.Read(Data, path));
        Assert.Equal(["input.age.value", "input.name.value", "input.nope.value"], missing.Select(n => n!.GetValue<string>()));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("[1]", true)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    [InlineData("[]", false)]
    [InlineData("0", false)]
    [InlineData("\"\"", false)]
    [InlineData("1", true)]
    [InlineData("\"a\"", true)]
    [InlineData("{}", true)]
    public void Truthy(string json, bool expected) =>
        Assert.Equal(expected, JsonLogicFunctions.Truthy(JsonNode.Parse(json)));
}
