using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>#1500 (specVersion 31, area 03): <see cref="SentFormat"/>, what a model receives (part 02 § 3.5; D3, D8, D22, D23).</summary>
public class SentFormatTests
{
    private static readonly TypeRegistry Registry = TypeFixture.Published();
    private static readonly FormatSet Formats = FormatSet.ForPackage(Registry, []);
    private static readonly string[] Format = ["format"];

    private static SourceFormat Package(string id, string schema) => new(id, $"formats/{id}.json", TypeFixture.Schema(schema));

    private static string Compact(JsonNode? node) => CanonicalJson.Write(node);

    private static string Obj(string properties, params string[] required) =>
        $$"""{ "type": "object", "properties": { {{properties}} }, "required": [{{string.Join(",", required.Select(r => $"\"{r}\""))}}], "additionalProperties": false }""";

    private static string Chain(int depth)
    {
        var inner = """{ "type": "string" }""";
        for (var i = depth - 1; i >= 0; i--)
        {
            inner = Obj($"\"p{i}\": {inner}", $"p{i}");
        }

        return inner;
    }

    [Fact]
    public void Judgement_IsSentAsPlainJsonSchema()
    {
        var sent = SentFormat.Prepare("judgement", Formats, Format);
        var expected = JsonNode.Parse("""
            { "type": "object",
              "description": "A yes-or-no answer to the question asked, with the reason for it.",
              "required": ["answer", "reason"],
              "additionalProperties": false,
              "properties": {
                "answer": { "type": "boolean", "description": "true for yes, false for no." },
                "reason": { "type": "string", "description": "Plain text, with no markup.\nWhy, in a sentence or two, from the text given." } } }
            """);
        Assert.Equal(Compact(expected), Compact(sent.Schema));
        Assert.Empty(sent.NotSent);
        Assert.Equal(2, sent.Properties);
        Assert.Equal(1, sent.Levels);
        Assert.Null(sent.DeepestPath);
    }

    [Fact]
    public void ALabelledEnum_BecomesAPlainEnum()
    {
        var kind = JsonNode.Parse("""
            { "type": "string", "oneOf": [
              { "const": "narrative", "title": "Narrative" },
              { "const": "table", "title": "Table" },
              { "const": "omit", "title": "Leave out" } ] }
            """);
        var sent = SentFormat.Prepare(kind, Formats, []);
        Assert.Equal("""{"enum":["narrative","table","omit"],"type":"string"}""", Compact(sent.Schema));

        var nullable = JsonNode.Parse("""
            { "type": ["string", "null"], "oneOf": [
              { "const": "a", "title": "A" }, { "const": null, "title": "None" }, { "const": "b", "title": "B" } ] }
            """);
        Assert.Equal("""{"enum":["a",null,"b"],"type":["string","null"]}""", Compact(SentFormat.Prepare(nullable, Formats, []).Schema));
    }

    [Fact]
    public void ADate_LosesFormat_AndSaysSo()
    {
        var visit = JsonNode.Parse(Obj("\"onset\": { \"type\": \"string\", \"format\": \"date\" }", "onset"));
        var sent = SentFormat.Prepare(visit, Formats, Format);
        Assert.Equal("""{"type":"string"}""", Compact(sent.Schema["properties"]!["onset"]));
        Assert.Equal(["/properties/onset/format"], sent.NotSent);

        // A property that is called "format" is not the keyword.
        var named = JsonNode.Parse(Obj("\"format\": { \"type\": \"string\", \"title\": \"T\", \"x-note\": 1 }, \"title\": { \"type\": \"string\" }", "format", "title"));
        var kept = SentFormat.Prepare(named, Formats, Format);
        Assert.Empty(kept.NotSent);
        Assert.Equal("""{"type":"string"}""", Compact(kept.Schema["properties"]!["format"]));
        Assert.NotNull(kept.Schema["properties"]!["title"]);

        // The date built-in, by name, reaches the same place.
        var byName = SentFormat.Prepare(JsonNode.Parse(Obj("\"onset\": { \"$ref\": \"date\" }", "onset")), Formats, Format);
        Assert.Equal(["/properties/onset/format"], byName.NotSent);
        Assert.Empty(SentFormat.Prepare(visit, Formats, []).NotSent);
    }

    [Fact]
    public void ATextRoot_IsSentAsAString()
    {
        var sent = SentFormat.Prepare("markdown_block", Formats, Format);
        Assert.Equal(
            """{"description":"Markdown text that stands on its own lines (a paragraph, a list or a table).","type":"string"}""",
            Compact(sent.Schema));
        Assert.Equal(0, sent.Levels);
    }

    [Fact]
    public void TheHash_IsOfWhatIsSent()
    {
        var sent = SentFormat.Prepare("judgement", Formats, Format);
        Assert.Equal(CanonicalJson.Hash(sent.Schema), sent.Hash);
        Assert.NotEqual(FormatTypes.FormatHash("judgement", Formats), sent.Hash);

        var a = SentFormat.Prepare(JsonNode.Parse("""{ "type": "string", "title": "One" }"""), Formats, Format);
        var b = SentFormat.Prepare(JsonNode.Parse("""{ "type": "string", "title": "Two" }"""), Formats, Format);
        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void Limits_AreCountedOnTheSentCopy()
    {
        string Wide(int n) => Obj(string.Join(",", Enumerable.Range(0, n).Select(i => $"\"p{i}\": {{ \"type\": \"string\" }}")), Enumerable.Range(0, n).Select(i => $"p{i}").ToArray());

        Assert.Empty(SentFormat.CheckLimits("wide", SentFormat.Prepare(JsonNode.Parse(Wide(100)), Formats, Format), "formats/wide.json"));
        var over = SentFormat.CheckLimits("wide", SentFormat.Prepare(JsonNode.Parse(Wide(101)), Formats, Format), "formats/wide.json");
        var refusal = Assert.Single(over);
        Assert.Equal("S02-055", refusal.Id);
        Assert.Equal("Format 'wide' declares 101 properties; a format sent to a model declares at most 100.", refusal.Sentence);
        Assert.Equal("formats/wide.json", refusal.Path);
        V31Contract.AssertSentence(refusal);

        Assert.Empty(SentFormat.CheckLimits("deep", SentFormat.Prepare(JsonNode.Parse(Chain(5)), Formats, Format), "formats/deep.json"));
        var deep = SentFormat.Prepare(JsonNode.Parse(Chain(6)), Formats, Format);
        Assert.Equal(6, deep.Levels);
        var tooDeep = Assert.Single(SentFormat.CheckLimits("deep", deep, "formats/deep.json"));
        Assert.Equal("S02-056", tooDeep.Id);
        Assert.Equal("Format 'deep' nests 6 levels deep at /properties/p0/properties/p1/properties/p2/properties/p3/properties/p4; a format sent to a model nests at most 5.", tooDeep.Sentence);
        V31Contract.AssertSentence(tooDeep);
    }

    [Fact]
    public void ListsAndUnions_AddNoLevel()
    {
        var wrapped = """{ "type": "array", "items": { "anyOf": [ ITEM, { "type": "null" } ] } }""";
        var format = JsonNode.Parse(Obj("\"a\": " + wrapped.Replace("ITEM", Obj("\"b\": " + wrapped.Replace("ITEM", Chain(3)), "b")), "a"));
        var sent = SentFormat.Prepare(format, Formats, Format);
        Assert.Equal(5, sent.Levels);
    }

    [Fact]
    public void AReferencedFormat_CountsWhereverItIsReferenced()
    {
        var props = string.Join(",", Enumerable.Range(0, 60).Select(i => $"\"q{i}\": {{ \"type\": \"string\" }}"));
        var names = Enumerable.Range(0, 60).Select(i => $"q{i}").ToArray();
        var set = FormatSet.ForPackage(Registry, [Package("big", Obj(props, names))]);
        var holder = JsonNode.Parse(Obj("\"first\": { \"$ref\": \"big\" }, \"second\": { \"$ref\": \"big\" }", "first", "second"));

        var sent = SentFormat.Prepare(holder, set, Format);
        Assert.Equal(122, sent.Properties);
        Assert.Equal(2, sent.Levels);
        Assert.Single(SentFormat.CheckLimits("holder", sent, "formats/holder.json"));
    }

    [Fact]
    public void AFormatThatDoesNotExpand_IsStillPrepared()
    {
        // Meaningful only for a format that expands; one that does not still gives a copy, the loop left as written.
        var set = FormatSet.ForPackage(Registry, [Package("a", Obj("\"x\": { \"$ref\": \"a\" }", "x"))]);
        var sent = SentFormat.Prepare("a", set, Format);
        Assert.Equal(
            """{"additionalProperties":false,"properties":{"x":{"$ref":"a"}},"required":["x"],"type":"object"}""",
            Compact(sent.Schema));
        Assert.Equal("{}", Compact(SentFormat.Prepare(null, set, Format).Schema));
        Assert.Equal("{}", Compact(SentFormat.Prepare(JsonNode.Parse("5"), set, Format).Schema));
    }

    [Fact]
    public void PastItsDepthCap_TheCopyIsEmpty_NeverUnstripped()
    {
        // 300 lists deep, each with a title: the cleaned part has no title, and the part past the cap is {}.
        JsonNode inner = new JsonObject { ["type"] = "string", ["title"] = "Deepest", ["x-note"] = 1 };
        for (var i = 0; i < 300; i++)
            inner = new JsonObject { ["type"] = "array", ["title"] = $"Level {i}", ["items"] = inner };

        var sent = SentFormat.Prepare(inner, Formats, Format);

        JsonNode? node = sent.Schema;
        var depth = 0;
        while (node is JsonObject { Count: 2 } list && list["items"] is { } items)
        {
            Assert.Equal("array", (string)list["type"]!);
            node = items;
            depth++;
        }

        Assert.Equal("{}", Compact(node));
        Assert.InRange(depth, 100, 299);
        Assert.DoesNotContain("title", sent.Schema.ToJsonString(new System.Text.Json.JsonSerializerOptions { MaxDepth = 1000 }));
    }

    [Fact]
    public void NotSent_PointsThroughListsUnionsAndEscapedNames()
    {
        // Through items, and through a nullable date, under a name that needs escaping.
        var format = JsonNode.Parse(Obj(
            "\"a/b\": { \"type\": \"array\", \"items\": { \"$ref\": \"date\" } }, "
            + "\"x\": { \"anyOf\": [ { \"$ref\": \"date\" }, { \"type\": \"null\" } ] }",
            "a/b", "x"));
        var sent = SentFormat.Prepare(format, Formats, Format);
        Assert.Equal(["/properties/a~1b/items/format", "/properties/x/anyOf/0/format"], sent.NotSent);
        Assert.Equal("string", (string)sent.Schema["properties"]!["x"]!["anyOf"]![0]!["type"]!);
        Assert.Null(sent.Schema["properties"]!["x"]!["anyOf"]![0]!["format"]);

        // A format referenced twice gives two pointers.
        var set = FormatSet.ForPackage(Registry, [Package("visit", Obj("\"on\": { \"$ref\": \"date\" }", "on"))]);
        var twice = SentFormat.Prepare(JsonNode.Parse(Obj("\"first\": { \"$ref\": \"visit\" }, \"second\": { \"$ref\": \"visit\" }", "first", "second")), set, Format);
        Assert.Equal(["/properties/first/properties/on/format", "/properties/second/properties/on/format"], twice.NotSent);
    }

    [Fact]
    public void PropertiesNamedLikeKeywords_Survive_WithTheirExactShape()
    {
        string[] names = ["$id", "enum", "x-foo", "contentMediaType", "title", "format"];
        var properties = string.Join(", ", names.Select((n, i) => $"\"{n}\": {{ \"type\": \"number\", \"description\": \"d{i}\" }}"));
        var sent = SentFormat.Prepare(JsonNode.Parse(Obj(properties, names)), Formats, Format);

        Assert.Empty(sent.NotSent);
        var kept = sent.Schema["properties"]!.AsObject();
        Assert.Equal(names, kept.Select(p => p.Key));
        for (var i = 0; i < names.Length; i++)
            Assert.Equal($$"""{"description":"d{{i}}","type":"number"}""", Compact(kept[names[i]]));
        Assert.Equal(names, sent.Schema["required"]!.AsArray().Select(n => (string)n!));
    }
}
