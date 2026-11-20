using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>#1500 (specVersion 31, area 03): <see cref="CorvusFormatValueValidator"/>, values against the full form (part 02 § 3.4; D1).</summary>
public class FormatValuesTests
{
    private static readonly TypeRegistry Registry = TypeFixture.Published();
    private static readonly FormatSet Formats = FormatSet.ForPackage(Registry, []);

    private static FormatValueResult Check(string? value, string format, FormatSet? formats = null, IFormatValueValidator? validator = null) =>
        (validator ?? new CorvusFormatValueValidator()).Validate(
            value is null ? null : JsonNode.Parse(value), JsonNode.Parse(format), formats ?? Formats);

    private static string Faults(FormatValueResult r) => string.Join(" ", r.Faults.Select(f => $"{f.Pointer}|{f.Keyword}"));

    private const string Judgement = "\"judgement\"";

    [Fact]
    public void AValueOfTheFormat_IsValid()
    {
        Assert.True(Check("""{ "answer": true, "reason": "Margaret Okafor has a cough." }""", Judgement).Valid);
        var r24 = Check("""{ "concepts": [ { "term": "Cough", "type": "finding", "id": null, "isSnomedConcept": false, "isActive": true, "support": null } ] }""", "\"concept_list\"");
        Assert.True(r24.Valid, Faults(r24));
        Assert.True(Check("\"Some **text**.\"", "\"markdown_block\"").Valid);
        Assert.True(Check("""["a", "b"]""", """{ "type": "array", "items": { "$ref": "plaintext" } }""").Valid);
        Assert.Empty(Check("\"x\"", "\"plaintext\"").Faults);
    }

    [Fact]
    public void AMissingProperty_AWrongType_AnExtraProperty_AreFaults()
    {
        var missing = Check("""{ "answer": true }""", Judgement);
        Assert.False(missing.Valid);
        Assert.Equal("|required", Faults(missing));

        var wrong = Check("""{ "answer": "yes", "reason": "r" }""", Judgement);
        Assert.Equal("/answer|type", Faults(wrong));

        var extra = Check("""{ "answer": true, "reason": "r", "mood": 1 }""", Judgement);
        Assert.Equal("|additionalProperties", Faults(extra));
    }

    [Fact]
    public void ANullableField_TakesNull_ARequiredOneDoesNot()
    {
        const string format = """
            { "type": "object", "additionalProperties": false, "required": ["a", "b"],
              "properties": { "a": { "type": ["string", "null"] }, "b": { "type": "string" } } }
            """;
        Assert.True(Check("""{ "a": null, "b": "x" }""", format).Valid);
        Assert.Equal("/b|type", Faults(Check("""{ "a": null, "b": null }""", format)));
        Assert.True(Check(null, """{ "anyOf": [ { "$ref": "plaintext" }, { "type": "null" } ] }""").Valid);
        Assert.False(Check("5", """{ "anyOf": [ { "$ref": "plaintext" }, { "type": "null" } ] }""").Valid);
    }

    [Fact]
    public void AnEnumValue_MustBeListed_PlainOrLabelled()
    {
        const string plain = """{ "type": "string", "enum": ["mild", "severe"] }""";
        const string labelled = """{ "type": "string", "oneOf": [ { "const": "mild", "title": "Mild" }, { "const": "severe", "title": "Severe" } ] }""";
        foreach (var format in new[] { plain, labelled })
        {
            Assert.True(Check("\"mild\"", format).Valid);
            Assert.False(Check("\"moderate\"", format).Valid);
            Assert.NotEmpty(Check("\"moderate\"", format).Faults);
        }

        Assert.Equal("|enum", Faults(Check("\"moderate\"", plain)));
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1e2", true)]
    [InlineData("1.5", false)]
    [InlineData("3", true)]
    public void NumbersAtTheEdges(string value, bool valid) =>
        Assert.Equal(valid, Check(value, """{ "type": "integer" }""").Valid);

    [Theory]
    [InlineData("2026-02-28", true)]
    [InlineData("2026-02-30", false)]
    [InlineData("2026-2-3", false)]
    [InlineData("28/02/2026", false)]
    public void NumbersAndDates_AtTheEdges(string value, bool valid)
    {
        var result = Check($"\"{value}\"", "\"date\"");
        Assert.Equal(valid, result.Valid);
        if (!valid)
        {
            Assert.Equal("|format", Faults(result));
        }
    }

    [Fact]
    public void AReply_IsValidatedAgainstTheFullFormat_NotTheSentOne()
    {
        const string format = """
            { "type": "object", "additionalProperties": false, "required": ["onset"],
              "properties": { "onset": { "$ref": "date" } } }
            """;
        var sent = SentFormat.Prepare(JsonNode.Parse(format), Formats, ["format"]);
        Assert.DoesNotContain("\"format\"", CanonicalJson.Write(sent.Schema));
        Assert.Equal("/onset|format", Faults(Check("""{ "onset": "soon" }""", format)));
        Assert.True(Check("""{ "onset": "2026-03-01" }""", format).Valid);
    }

    [Fact]
    public void AFieldNamedLikeAUnionKeyword_IsAField()
    {
        foreach (var name in new[] { "anyOf", "oneOf" })
        {
            var format = $$"""{ "type": "object", "additionalProperties": false, "required": ["{{name}}"], "properties": { "{{name}}": { "type": "integer" } } }""";
            Assert.Equal($"/{name}|type", Faults(Check($$"""{ "{{name}}": "three" }""", format)));
        }
    }

    [Fact]
    public void Faults_NeverCarryTheValue()
    {
        var text = string.Concat(Enumerable.Repeat("Margaret Okafor has had a cough since March. ", 50_000));
        Assert.True(text.Length > 2_000_000);
        var value = new JsonObject { ["answer"] = text, ["reason"] = "r", ["Margaret"] = 1 };
        var result = new CorvusFormatValueValidator().Validate(value, JsonNode.Parse(Judgement), Formats);
        Assert.False(result.Valid);
        Assert.Equal([new FormatValueFault("", "additionalProperties"), new FormatValueFault("/answer", "type")], result.Faults);
        foreach (var fault in result.Faults)
        {
            Assert.DoesNotContain("Margaret", fault.Pointer + fault.Keyword);
            Assert.DoesNotContain("Okafor", fault.Pointer + fault.Keyword);
            Assert.DoesNotContain("cough", fault.Pointer + fault.Keyword);
            Assert.True(fault.Pointer.Length < 100 && fault.Keyword.Length < 40);
        }

        var wrongRoot = Check("\"Margaret Okafor\"", Judgement);
        Assert.Equal("|type", Faults(wrongRoot));
    }

    [Fact]
    public void AnUnknownOrCyclicFormat_IsInvalid_AndDoesNotThrow()
    {
        var unknown = Check("1", "\"nonesuch\"");
        Assert.False(unknown.Valid);
        Assert.Equal("|$ref", Faults(unknown));

        var cyclic = FormatSet.ForPackage(Registry,
        [
            new SourceFormat("a", "formats/a.json", TypeFixture.Schema("""{ "type": "array", "items": { "$ref": "b" } }""")),
            new SourceFormat("b", "formats/b.json", TypeFixture.Schema("""{ "type": "array", "items": { "$ref": "a" } }""")),
        ]);
        Assert.Equal("|$ref", Faults(Check("[]", "\"a\"", cyclic)));
        Assert.Equal("|$ref", Faults(Check("[]", "5")));
        Assert.Equal("|$ref", Faults(Check("[]", "null")));
    }

    [Fact]
    public void ADifferentFormat_NeverSharesACompiledSchema()
    {
        var one = new CorvusFormatValueValidator();
        Assert.True(Check("\"x\"", """{ "type": "string" }""", validator: one).Valid);
        Assert.False(Check("\"x\"", """{ "type": "string", "enum": ["y"] }""", validator: one).Valid);
        Assert.False(Check("\"x\"", """{ "type": "integer" }""", validator: one).Valid);
    }

    [Fact]
    public void ManyThreads_ValidatingFreshFormatsAtOnce_AllGetTheRightAnswer()
    {
        // Each iteration a format no other has used (its own enum value) and the same format named again,
        // on two validators shared by every thread: every result right, nothing thrown.
        var a = new CorvusFormatValueValidator();
        var b = new CorvusFormatValueValidator();
        var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var validator = i % 2 == 0 ? a : b;
            var format = $$"""{ "type": "object", "additionalProperties": false, "required": ["v"], "properties": { "v": { "type": "string", "enum": ["v{{i}}"] } } }""";
            var good = Check($$"""{ "v": "v{{i}}" }""", format, validator: validator);
            var bad = Check($$"""{ "v": "v{{i + 1}}" }""", format, validator: validator);
            var named = Check($$"""{ "answer": {{(i % 3 == 0 ? "true" : "\"no\"")}}, "reason": "r" }""", Judgement, validator: validator);
            if (!good.Valid || Faults(bad) != "/v|enum" || named.Valid != (i % 3 == 0))
                wrong.Add($"{i}: {good.Valid} {Faults(bad)} {named.Valid}");
        });

        Assert.Empty(wrong);
    }

    [Fact]
    public void FaultsAreSorted_AndDeduplicated()
    {
        var result = Check("""{ "z": 1, "y": 2 }""", Judgement);
        var keys = result.Faults.Select(f => $"{f.Pointer}|{f.Keyword}").ToList();
        Assert.Equal(keys.Distinct().Count(), keys.Count);
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).ToList(), keys.ToList());
    }
}
