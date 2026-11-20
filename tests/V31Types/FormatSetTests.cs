using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="FormatSet"/>, the lookup of a format name
/// (part 02 § 4, § 6.3) and the loops through named references (S02-032).
/// </summary>
public class FormatSetTests
{
    private static readonly TypeRegistry Registry = TypeFixture.Published();

    private static SourceFormat Format(string id, string schema) =>
        new(id, $"formats/{id}.json", TypeFixture.Schema(schema));

    private static string Obj(string properties, string required) =>
        $$"""{ "type": "object", "properties": { {{properties}} }, "required": [{{required}}], "additionalProperties": false }""";

    private static string Ref(string name) => $$"""{ "$ref": "{{name}}" }""";

    [Fact]
    public void AName_IsFoundPackageFirst_ThenCatalogue_ThenBuiltIn()
    {
        var set = FormatSet.ForPackage(Registry, [Format("plaintext", """{ "type": "string" }"""), Format("verdict", Obj("", ""))]);

        Assert.Equal(FormatOrigin.Package, set.Find("plaintext")!.Origin);
        Assert.Equal(FormatOrigin.Package, set.Find("verdict")!.Origin);
        Assert.Equal(FormatOrigin.Catalogue, set.Find("classification")!.Origin);
        Assert.Equal(FormatOrigin.BuiltIn, set.Find("markdown_block")!.Origin);
        Assert.Equal("markdown_block", set.Find("markdown_block")!.Id);
        Assert.Null(set.Find("nothing"));
        Assert.Null(FormatSet.ForPackage(Registry, []).Find("verdict"));
        Assert.Equal(FormatOrigin.BuiltIn, FormatSet.ForPackage(Registry, []).Find("plaintext")!.Origin);
    }

    [Fact]
    public void AFunction_SeesItsOwnFormats_ThenWhatItUses_NeverTheRest()
    {
        var package = FormatSet.ForPackage(Registry, [Format("verdict", Obj("", "")), Format("outline", Obj("", ""))]);
        var function = package.ForFunction([Format("local", Obj("", "")), Format("verdict", Obj("", ""))], ["verdict"]);

        Assert.Equal(FormatOrigin.Function, function.Find("local")!.Origin);
        Assert.Equal(FormatOrigin.Function, function.Find("verdict")!.Origin);
        Assert.Null(function.Find("outline"));
        Assert.Equal(FormatOrigin.BuiltIn, function.Find("plaintext")!.Origin);
        Assert.Equal(FormatOrigin.Catalogue, function.Find("concept_list")!.Origin);

        var usesPackage = package.ForFunction([], ["verdict"]);
        Assert.Equal(FormatOrigin.Package, usesPackage.Find("verdict")!.Origin);
        Assert.Null(usesPackage.Find("outline"));
    }

    [Fact]
    public void AnUnknownName_IsRefusedOncePerName()
    {
        var set = FormatSet.ForPackage(Registry, [Format("verdict", Obj("", ""))]);
        var format = TypeFixture.Schema(Obj(
            $"\"a\": {Ref("vrdict")}, \"b\": \"vrdict\", \"c\": {{ \"type\": \"array\", \"items\": {Ref("other")} }}, \"d\": {Ref("plaintext")}, \"e\": {Ref("verdict")}, \"f\": {Ref("#/$defs/x")}",
            "\"a\", \"b\", \"c\", \"d\", \"e\", \"f\""));

        var refusals = set.CheckReferences(format, "Input 'x'", "manifest.json");

        Assert.Equal(["S02-017", "S02-017"], refusals.Select(r => r.Id));
        Assert.Equal("Format 'vrdict' is not a built-in, catalogue or package format.", refusals[0].Sentence);
        Assert.Equal("Format 'other' is not a built-in, catalogue or package format.", refusals[1].Sentence);
        Assert.All(refusals, r => Assert.Equal("manifest.json", r.Path));
        Assert.All(refusals, V31Contract.AssertSentence);

        var bare = set.CheckReferences(JsonValue.Create("vrdict"), "Input 'x'", "manifest.json");
        Assert.Single(bare);
        Assert.Empty(set.CheckReferences(JsonValue.Create("plaintext"), "Input 'x'", "manifest.json"));
        Assert.Empty(set.CheckReferences(null, "Input 'x'", "manifest.json"));
    }

    [Fact]
    public void ALoopThroughTwoFiles_IsRefusedOncePerFormat()
    {
        var set = FormatSet.ForPackage(Registry,
        [
            Format("a", Obj($"\"next\": {Ref("b")}", "\"next\"")),
            Format("b", Obj($"\"back\": {Ref("a")}", "\"back\"")),
            Format("c", Obj($"\"to\": {Ref("a")}", "\"to\"")),
        ]);

        var refusals = set.CheckCycles();

        Assert.Equal(2, refusals.Count);
        Assert.Equal("Format 'a' at /properties/next refers to itself; recursive formats aren't supported.", refusals[0].Sentence);
        Assert.Equal("formats/a.json", refusals[0].Path);
        Assert.Equal("Format 'b' at /properties/back refers to itself; recursive formats aren't supported.", refusals[1].Sentence);
        Assert.Equal("formats/b.json", refusals[1].Path);
        Assert.All(refusals, V31Contract.AssertSentence);
        Assert.True(set.IsCyclic("a"));
        Assert.True(set.IsCyclic("b"));
        Assert.False(set.IsCyclic("c"));
        Assert.False(set.IsCyclic("plaintext"));
        Assert.False(set.IsCyclic("nothing"));
    }

    [Fact]
    public void ASelfReferenceByName_IsALoop()
    {
        var set = FormatSet.ForPackage(Registry, [Format("a", Obj($"\"me\": {Ref("a")}", "\"me\""))]);

        var refusal = Assert.Single(set.CheckCycles());
        Assert.Equal("S02-032", refusal.Id);
    }

    [Fact]
    public void ALoopThroughAListOrAUnion_IsFound()
    {
        var set = FormatSet.ForPackage(Registry,
        [
            Format("a", Obj($"\"rows\": {{ \"type\": \"array\", \"items\": {Ref("b")} }}", "\"rows\"")),
            Format("b", Obj($"\"either\": {{ \"anyOf\": [ {Ref("plaintext")}, {Ref("a")} ] }}", "\"either\"")),
            Format("d", """{ "type": "object", "properties": {}, "required": [], "additionalProperties": false, "$defs": { "x": { "type": "array", "items": { "$ref": "d" } } } }"""),
        ]);

        var refusals = set.CheckCycles();

        Assert.Equal(["a", "b", "d"], refusals.Select(r => r.Path).Select(p => Path.GetFileNameWithoutExtension(p)));
        Assert.Equal("Format 'a' at /properties/rows/items refers to itself; recursive formats aren't supported.", refusals[0].Sentence);
        Assert.Equal("Format 'b' at /properties/either/anyOf/1 refers to itself; recursive formats aren't supported.", refusals[1].Sentence);
        Assert.Equal("Format 'd' at /$defs/x/items refers to itself; recursive formats aren't supported.", refusals[2].Sentence);
    }

    [Fact]
    public void AChainThatEnds_IsNotALoop()
    {
        var set = FormatSet.ForPackage(Registry,
        [
            Format("a", Obj($"\"x\": {Ref("b")}", "\"x\"")),
            Format("b", Obj("\"y\": \"plaintext\"", "\"y\"")),
        ]);

        Assert.Empty(set.CheckCycles());
        Assert.False(set.IsCyclic("a"));
    }

    [Fact]
    public void AFunctionSet_ReportsLoopsAmongItsOwnFormatsAndTheOnesItUses()
    {
        var package = FormatSet.ForPackage(Registry,
        [
            Format("p", Obj($"\"q\": {Ref("f")}", "\"q\"")),
            Format("unused", Obj($"\"u\": {Ref("unused")}", "\"u\"")),
        ]);
        var function = package.ForFunction([Format("f", Obj($"\"back\": {Ref("p")}", "\"back\""))], ["p"]);

        var refusals = function.CheckCycles();

        Assert.Equal(["formats/f.json", "formats/p.json"], refusals.Select(r => r.Path).Order(StringComparer.Ordinal));
        Assert.Single(package.CheckCycles());
    }

    [Fact]
    public void Resolve_And_NameOf_ReadBothWrittenForms()
    {
        var set = FormatSet.ForPackage(Registry, []);
        var plaintext = Registry.BuiltInFormats["plaintext"];

        Assert.Equal("plaintext", FormatSet.NameOf(JsonValue.Create("plaintext")));
        Assert.Equal("plaintext", FormatSet.NameOf(TypeFixture.Schema("""{ "$ref": "plaintext" }""")));
        Assert.Equal("plaintext", FormatSet.NameOf(TypeFixture.Schema("""{ "$ref": "plaintext", "title": "Notes", "description": "d" }""")));
        Assert.Null(FormatSet.NameOf(TypeFixture.Schema("""{ "type": "number" }""")));
        Assert.Null(FormatSet.NameOf(TypeFixture.Schema("""{ "$ref": "plaintext", "type": "string" }""")));
        Assert.Null(FormatSet.NameOf(TypeFixture.Schema("""{ "$ref": "#/$defs/x" }""")));
        Assert.Null(FormatSet.NameOf(TypeFixture.Schema("""{ "$ref": 5 }""")));
        Assert.Null(FormatSet.NameOf(null));

        Assert.Same(plaintext, set.Resolve(JsonValue.Create("plaintext")));
        Assert.Same(plaintext, set.Resolve(TypeFixture.Schema("""{ "$ref": "plaintext" }""")));
        Assert.Same(plaintext, set.Resolve(TypeFixture.Schema("""{ "$ref": "plaintext", "title": "Notes" }""")));
        var inline = TypeFixture.Schema("""{ "type": "number" }""");
        Assert.Same(inline, set.Resolve(inline));
        Assert.Null(set.Resolve(JsonValue.Create("nothing")));
        Assert.Null(set.Resolve(JsonValue.Create(5)));
        Assert.Null(set.Resolve(null));
    }

    [Fact]
    public void Garbage_NeverThrows()
    {
        var deep = "{ \"type\": \"string\" }";
        for (var i = 0; i < 40; i++)
        {
            deep = $$"""{ "type": "array", "items": {{deep}} }""";
        }

        var set = FormatSet.ForPackage(Registry,
        [
            Format("a", """{ "$ref": 5, "properties": [1, 2], "items": [], "anyOf": 3, "$defs": [] }"""),
            Format("b", """{ "properties": { "x": null, "y": 5, "z": true }, "anyOf": [null, 1, "b"], "$defs": { "q": 7 } }"""),
            Format("c", deep),
        ]);

        Assert.Single(set.CheckCycles());
        Assert.Empty(set.CheckReferences(set.Find("a")!.Schema, "Format 'a'", "formats/a.json"));
        Assert.Empty(set.CheckReferences(TypeFixture.Schema(deep), "Format 'c'", "formats/c.json"));
        Assert.Empty(set.CheckReferences(JsonNode.Parse("[1, 2]"), "Format 'c'", "formats/c.json"));
        Assert.Null(set.Resolve(JsonNode.Parse("[1]")));
        Assert.Throws<ArgumentNullException>(() => set.Find(null!));
    }
}
