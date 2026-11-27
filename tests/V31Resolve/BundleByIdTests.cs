using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Templates.MacroFixture;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1508 (specVersion 31, area 11; R2, R6): <c>resolve.json</c>'s <c>formats</c> is a JSON Schema 2020-12 compound
/// document: a reference inside a bundled format is the target's <c>$id</c>, while an input's and an entry's
/// <c>format</c> stays <c>{ "$ref": "#/$defs/&lt;id&gt;" }</c>; the plan expands it, a standard validator resolves it,
/// and neither hash moves. And <c>requires.features</c> lists <c>run-spots</c> only for a placed unit or a body.
/// Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class BundleByIdTests
{
    private const string Package = "urn:consultologist:package:example-minimal@v2026.12.1";

    private const string PlaintextId = "urn:consultologist:spec:31:format:plaintext";

    /// <summary>
    /// The nested note with the package format <c>section_note</c>, naming <c>section_plan</c> (which names
    /// <c>plaintext</c>), returned by the agent <c>review</c>.
    /// </summary>
    private static Dictionary<string, byte[]> Noted() =>
        ResolveFixture.NestedNote()
            .With("formats/index.json", Index(["section_plan", "outline", "verdict", "section_note"]))
            .With("formats/section_note.json", Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"plan\"], "
                + "\"additionalProperties\": false, \"properties\": { \"plan\": { \"$ref\": \"section_plan\", \"title\": \"Plan\" } } }"))
            .WithJson("nodes/index.json", i => i["items"]!.AsArray().Add("review"))
            .With("nodes/review.md", Agent("Reviewing the plan", "\"section_note\"", "Review the plan of the note.\n"));

    private static JsonObject Resolve(Dictionary<string, byte[]> files)
    {
        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}")));
        Assert.True(compiled!.Complete);
        return JsonNode.Parse(compiled.Resolve)!.AsObject();
    }

    private static string[] Features(Dictionary<string, byte[]> files) =>
        Resolve(files)["requires"]!["features"]!.AsArray().Select(f => (string)f!).ToArray();

    private static JsonObject Valid() => JsonNode.Parse(
        "{ \"plan\": { \"sections\": [ { \"title\": \"History of Margaret Okafor\", \"kind\": \"narrative\" } ] } }")!.AsObject();

    private static JsonObject Invalid() => JsonNode.Parse(
        "{ \"plan\": { \"sections\": [ { \"title\": \"History of Margaret Okafor\", \"kind\": \"poem\" } ] } }")!.AsObject();

    [Fact]
    public void AFormatThatNamesAnother_ReferencesItByItsId()
    {
        var document = Resolve(Noted());
        var defs = document["formats"]!["$defs"]!.AsObject();

        // R2: inside the bundle a reference is the target's $id, a package format's and a built-in's alike.
        Assert.Equal($"{Package}:format:section_plan", (string?)defs["section_plan"]!["$id"]);
        Assert.Equal($"{Package}:format:section_plan", (string?)defs["section_note"]!["properties"]!["plan"]!["$ref"]);
        Assert.Equal("Plan", (string?)defs["section_note"]!["properties"]!["plan"]!["title"]);
        Assert.Equal(PlaintextId, (string?)defs["section_plan"]!["properties"]!["sections"]!["items"]!["properties"]!["title"]!["$ref"]);
        Assert.Equal(PlaintextId, (string?)defs["plaintext"]!["$id"]);
        Assert.DoesNotContain("\"#/$defs/", document["formats"]!.ToJsonString(), StringComparison.Ordinal);

        // An input's and an entry's format still name the bundle's key.
        Assert.Equal("{\"$ref\":\"#/$defs/markdown_block\"}", CanonicalJson.Write(document["inputs"]!["consult_draft"]!["format"]));
        Assert.Equal("{\"$ref\":\"#/$defs/section_note\"}", CanonicalJson.Write(document["nodes"]!["node.review"]!["format"]));

        // A function's own format references by $id too, and is referenced by its own.
        var functions = Resolve(ResolveFixture.Functions()
            .With("functions/draft_section/formats/index.json", Index(["excerpt"]))
            .With("functions/draft_section/formats/excerpt.json", Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"text\"], "
                + "\"additionalProperties\": false, \"properties\": { \"text\": { \"$ref\": \"plaintext\" } } }"))
            .WithJson("functions/draft_section/standard_draft.md", h => h["format"] = "excerpt"));
        var excerpt = functions["formats"]!["$defs"]!["draft_section__excerpt"]!;
        Assert.Equal($"{Package}:function:draft_section:format:excerpt", (string?)excerpt["$id"]);
        Assert.Equal(PlaintextId, (string?)excerpt["properties"]!["text"]!["$ref"]);
        Assert.Equal("{\"$ref\":\"#/$defs/draft_section__excerpt\"}",
            CanonicalJson.Write(functions["functions"]!["draft_section"]!["nodes"]!["node.standard_draft"]!["format"]));
    }

    [Fact]
    public void TheBundle_ValidatesAsOneCompoundDocument()
    {
        // A standard 2020-12 validator (Corvus, as the library's own validator runs it), given the bundle and a $ref to
        // one of its $ids, resolves every reference inside it by $id: no rewriting, no inlining.
        var formats = Resolve(Noted())["formats"]!.AsObject();
        var compound = new JsonObject
        {
            ["$defs"] = formats["$defs"]!.DeepClone(),
            ["$ref"] = $"{Package}:format:section_note",
        };
        var validator = new CorvusFormatValueValidator();

        Assert.True(validator.ValidateExpanded(Valid(), compound).Valid);
        var result = validator.ValidateExpanded(Invalid(), compound);
        Assert.False(result.Valid);
        Assert.Contains(new FormatValueFault("/plan/sections/0/kind", "enum"), result.Faults);
    }

    [Fact]
    public void ThePlan_ExpandsAUrnReference()
    {
        var formats = new PlanFormats(Resolve(Noted())["formats"]);
        var site = JsonNode.Parse("{ \"$ref\": \"#/$defs/section_note\" }");

        var expanded = formats.Expand(site);

        // Every reference inlined: section_plan's members under plan (beside its title), plaintext's under each title.
        Assert.DoesNotContain("$ref", expanded.ToJsonString(), StringComparison.Ordinal);
        var plan = expanded["properties"]!["plan"]!;
        Assert.Equal("Plan", (string?)plan["title"]);
        Assert.Equal("text/plain",
            (string?)plan["allOf"]![0]!["properties"]!["sections"]!["items"]!["properties"]!["title"]!["contentMediaType"]);
        Assert.True(formats.Validate(Valid(), site).Valid);
        Assert.Equal([new FormatValueFault("/plan/sections/0/kind", "enum")], formats.Validate(Invalid(), site).Faults);
        Assert.True(formats.IsObject(site));

        // An entry's reference to another entry's $id is that entry, wherever the bundle keys it.
        var bundle = JsonNode.Parse($$"""
            { "$defs": {
              "a": { "$id": "{{Package}}:format:a", "type": "object", "properties": { "b": { "$ref": "{{Package}}:format:other" } } },
              "b": { "$id": "{{Package}}:format:other", "type": "string", "maxLength": 3 } } }
            """);
        Assert.Equal("{\"properties\":{\"b\":{\"maxLength\":3,\"type\":\"string\"}},\"type\":\"object\"}",
            CanonicalJson.Write(new PlanFormats(bundle).Expand(JsonNode.Parse("{ \"$ref\": \"#/$defs/a\" }"))));

        // A cycle through $ids (a to b to a) is the schema nothing fits.
        var cycle = JsonNode.Parse($$"""
            { "$defs": {
              "a": { "$id": "{{Package}}:format:a", "type": "object", "properties": { "b": { "$ref": "{{Package}}:format:b" } } },
              "b": { "$id": "{{Package}}:format:b", "type": "array", "items": { "$ref": "{{Package}}:format:a" } } } }
            """);
        Assert.Equal("false", new PlanFormats(cycle).Expand(JsonNode.Parse("{ \"$ref\": \"#/$defs/a\" }")).ToJsonString());

        // A $ref to an $id no entry has is the schema nothing fits.
        var unknown = new JsonObject { ["$defs"] = new JsonObject { ["a"] = JsonNode.Parse(
            $"{{ \"type\": \"object\", \"properties\": {{ \"b\": {{ \"$ref\": \"{Package}:format:missing\" }} }} }}") } };
        Assert.Equal("false", new PlanFormats(unknown).Expand(JsonNode.Parse("{ \"$ref\": \"#/$defs/a\" }")).ToJsonString());
    }

    [Fact]
    public void FormatHashesAndTypeHashes_DoNotMove()
    {
        // R2, "published formats keep their bytes": both hashes are over the format in its scope (full: everything
        // dereferenced; type: references by the format's name), so writing a reference as an $id moves neither. These
        // are nested-note's, as published before the bundle referenced by $id. markdown_block's formatHash is that of
        // its description as #1510's G-D12 wrote it (its typeHash did not move).
        var golden = CompiledGoldenTests.Cases().Single(c => c.Name == "nested-note");
        var document = JsonNode.Parse(CompiledGoldenTests.Published(golden).Resolve)!;

        Assert.Equal(
            "{\"markdown_block\":\"sha256:448546798808119491b86587a11cf2b8e975df7976978bc8e5f55768d743c0a1\","
            + "\"number\":\"sha256:022c36612bbb1a825d84551f250176f2d737aaa723cd7b117729bd3d23c0a64c\","
            + "\"outline\":\"sha256:0349e6084c044acc6cadf0e91f533ad305085605433300d5592aed54a0f5f9f3\","
            + "\"plaintext\":\"sha256:657c017a5c11998ed8cbb8c6774e14c3b98a6ac9eefeaf2c2101413de8e3e4ea\","
            + "\"section_plan\":\"sha256:30b6a0db3ac71c7a8ef6f6ee48d8714da18e217d2fc8663a19678a2b5437fa4a\","
            + "\"verdict\":\"sha256:c7d1fbcc1a0e51105b6f5507ff2b2abf32514d5ccf5e9ba66b34eb509c946750\"}",
            CanonicalJson.Write(document["formatHashes"]));
        Assert.Equal(
            "{\"markdown_block\":\"sha256:58c76228066bb97086313c812f807ebcfb90e04012b7782910d9d7c23ead6d5d\","
            + "\"number\":\"sha256:cd4a3ab837b5f825b6d5307134044c372031e93c3628a33e8bdc5bcdcc5ba7bc\","
            + "\"outline\":\"sha256:6b6ebc502055a23dacad757bebec06a18c371182fad292d41aabf703db1f36f8\","
            + "\"plaintext\":\"sha256:7ebaaa39ccd27449a5f552e624980bca750a8ee83f48621b85cfb6e229178b56\","
            + "\"section_plan\":\"sha256:0c5a884b29f52fc96dfca25e8fecf669a7096806470b37b84b25e9e46a48b5f5\","
            + "\"verdict\":\"sha256:08ea57ba6077ecaa2ae0ba3bcd7575693a24b4c5baddc669a339e0a4775ceb76\"}",
            CanonicalJson.Write(document["typeHashes"]));
    }

    [Fact]
    public void AnUnplacedSnippetThatWritesRun_DoesNotTurnOnRunSpots()
    {
        // R6 (area 07's D15): only what is placed counts; the snippet still has its own run spot and warning.
        var document = Resolve(TemplateFixture.Package().WithSnippet("when", "Run {{ run.id }}\n"));

        Assert.Empty(document["requires"]!["features"]!.AsArray());
        Assert.Single(document["snippets"]!["snippet.when"]!["runSpots"]!.AsArray());
    }

    [Fact]
    public void APlacedOne_Does()
    {
        var files = TemplateFixture.Package()
            .WithSnippet("when", "Run {{ run.id }}\n")
            .WithAgent("draft", "Draft the note for Dr A. Sample.\n{{~ snippet.when.content }}\n")
            .RunDependent("nodes/draft.md");

        Assert.Equal(["run-spots"], Features(files));

        // A body's own run spot counts as before.
        Assert.Equal(["run-spots"], Features(TemplateFixture.Package()
            .WithRendered("stamp", "Seen {{ run.date | date.to_string \"%d %B %Y\" }}\n").RunDependent("nodes/stamp.md")));
    }

    [Fact]
    public void APlacedMacroThatWritesRun_HasNoRunSpot_AsBefore()
    {
        // A macro's body is substituted into the reply: its run.* is filled in directly, with no spot in the placing body
        // and none of its own (part 08: the macro row carries its own runNeutralHash), so it has never turned run-spots
        // on, before this change or after.
        var document = Resolve(TemplateFixture.Package()
            .WithMacroOf("stamp", "Seen on {{ run.date | date.to_string \"%d %B %Y\" }}.\n", ["Close with {{ macro.stamp.content }}"])
            .WithAgent("draft", "Draft the note for Dr A. Sample.\n{{~ macro.stamp }}\n")
            .RunDependent("nodes/draft.md"));

        Assert.Empty(document["macros"]!["macro.stamp"]!["runSpots"]!.AsArray());
        Assert.Empty(document["nodes"]!["node.draft"]!["runSpots"]!.AsArray());
        Assert.Equal(["macro-markers"], document["requires"]!["features"]!.AsArray().Select(f => (string)f!));
    }

    [Fact]
    public void AnUnplacedMacroThatWritesRun_DoesNot()
    {
        var document = Resolve(TemplateFixture.Package()
            .WithMacroOf("stamp", "Seen on {{ run.date | date.to_string \"%d %B %Y\" }}.\n", ["Close with {{ macro.stamp.content }}"]));

        Assert.Empty(document["requires"]!["features"]!.AsArray());
    }
}
