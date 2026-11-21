using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): the formats bundle of <c>resolve.json</c> (part 07 § 2.4; part 02 §§ 2.3, 6.2,
/// 6.3, 7; D5): every format the package uses, from its three homes, with its <c>$id</c>, its <c>$schema</c> and its
/// annotations, references rewritten to <c>#/$defs/…</c>; each format's two hashes, as the type services give them;
/// and the bindings of the agent nodes, by format and producer.
/// </summary>
public class CompiledFormatsTests
{
    private const string Plain = "\"plaintext\"";

    private const string Dialect = "urn:consultologist:spec:31:dialect";

    /// <summary>
    /// The function <c>draft_section</c>'s own format <c>excerpt</c> (a <c>text</c> of <c>plaintext</c>), returned by its
    /// node <c>standard_draft</c>.
    /// </summary>
    private const string Excerpt =
        "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"text\"], "
        + "\"additionalProperties\": false, \"properties\": { \"text\": { \"$ref\": \"plaintext\" } } }";

    private static (SourcePackage Package, PackageTypes Types, CompiledFormats Formats) Compile(Dictionary<string, byte[]> files)
    {
        var (package, types, _) = ResolveFixture.Tree(files);
        return (package, types, CompiledFormats.Build(package, types, ResolveFixture.Published()));
    }

    private static string[] Keys(JsonObject map) => map.Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();

    /// <summary><see cref="ResolveFixture.Functions"/> with <see cref="Excerpt"/> in <c>draft_section</c>'s own <c>formats/</c>.</summary>
    private static Dictionary<string, byte[]> WithOwnFormat() =>
        ResolveFixture.Functions()
            .With("functions/draft_section/formats/index.json", Index(["excerpt"]))
            .With("functions/draft_section/formats/excerpt.json", Json(Excerpt))
            .WithJson("functions/draft_section/standard_draft.md", h => h["format"] = "excerpt");

    [Fact]
    public void Bundle_HoldsOnlyWhatIsUsed()
    {
        var (_, _, formats) = Compile(ResolveFixture.NestedNote());

        string[] used = ["markdown_block", "number", "outline", "plaintext", "section_plan", "verdict"];
        Assert.Empty(formats.Refusals);
        Assert.Equal(["$defs"], formats.Bundle.Select(p => p.Key));
        Assert.Equal(used, Keys(formats.Bundle["$defs"]!.AsObject()));
        Assert.Equal(used, Keys(formats.FormatHashes));
        Assert.Equal(used, Keys(formats.TypeHashes));
        Assert.DoesNotContain("concept_list", Keys(formats.Bundle["$defs"]!.AsObject()));
    }

    [Fact]
    public void PackageFormats_GetTheirUrn()
    {
        var files = ResolveFixture.NestedNote().WithJson("manifest.json", m => m["name"] = "example-nested-note");
        var (_, _, formats) = Compile(files);
        var defs = formats.Bundle["$defs"]!.AsObject();

        // Part 07 § 2.4's example, and part 02 § 6.2's: a package format's $id is derived, its $schema kept.
        Assert.Equal("urn:consultologist:package:example-nested-note@v2026.12.1:format:verdict", (string?)defs["verdict"]!["$id"]);
        Assert.Equal(Dialect, (string?)defs["verdict"]!["$schema"]);

        // A built-in keeps its $id, $schema and annotations as published.
        var registry = ResolveFixture.Published();
        Assert.True(JsonNode.DeepEquals(registry.BuiltInFormats["plaintext"], defs["plaintext"]));
        Assert.True(JsonNode.DeepEquals(registry.BuiltInFormats["markdown_block"], defs["markdown_block"]));
        Assert.Equal("urn:consultologist:spec:31:format:markdown_block", (string?)defs["markdown_block"]!["$id"]);
    }

    [Fact]
    public void PackageFormats_WithNoSchema_GetTheDialect()
    {
        // D5: $schema is added to a package format when absent (the dialect checker refuses that first; Build still writes one).
        var (package, types, _) = ResolveFixture.Tree(ResolveFixture.NestedNote());
        var verdict = package.Definitions.Formats.Items.Single(f => f.Id == "verdict");
        verdict.Schema.Remove("$schema");

        var formats = CompiledFormats.Build(package, types, ResolveFixture.Published());

        Assert.Equal(Dialect, (string?)formats.Bundle["$defs"]!["verdict"]!["$schema"]);
    }

    [Fact]
    public void References_AreRewritten()
    {
        // A package format naming another: section_note names section_plan, which names plaintext.
        var files = ResolveFixture.NestedNote()
            .With("formats/index.json", Index(["section_plan", "outline", "verdict", "section_note"]))
            .With("formats/section_note.json", Json(
                "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"plan\"], "
                + "\"additionalProperties\": false, \"properties\": { \"plan\": { \"$ref\": \"section_plan\", \"title\": \"Plan\" } } }"))
            .WithJson("nodes/index.json", i => i["items"]!.AsArray().Add("review"))
            .With("nodes/review.md", Agent("Reviewing the plan", "\"section_note\"", "Review the plan of the note.\n"));
        var (package, types, formats) = Compile(files);
        var defs = formats.Bundle["$defs"]!.AsObject();

        Assert.Equal("#/$defs/section_plan", (string?)defs["section_note"]!["properties"]!["plan"]!["$ref"]);
        Assert.Equal("Plan", (string?)defs["section_note"]!["properties"]!["plan"]!["title"]);
        Assert.Equal("#/$defs/plaintext",
            (string?)defs["section_plan"]!["properties"]!["sections"]!["items"]!["properties"]!["title"]!["$ref"]);

        // The source is untouched: the bundle holds clones.
        var source = package.Definitions.Formats.Items.Single(f => f.Id == "section_note").Schema;
        Assert.Equal("section_plan", (string?)source["properties"]!["plan"]!["$ref"]);

        // An inline schema, and a name.
        var inline = JsonNode.Parse("{ \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }");
        var rewritten = CompiledFormats.Rewrite(inline, types.Formats, null);
        Assert.Equal("{\"items\":{\"$ref\":\"#/$defs/plaintext\"},\"type\":\"array\"}", CanonicalJson.Write(rewritten));
        Assert.Equal("plaintext", (string?)inline!["items"]!["$ref"]);
        Assert.Equal("{\"$ref\":\"#/$defs/verdict\"}", CanonicalJson.Write(CompiledFormats.Rewrite(JsonValue.Create("verdict"), types.Formats, null)));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("\"\"")]
    [InlineData("\"no_such_format\"")]
    [InlineData("{ \"$ref\": 5 }")]
    [InlineData("{ \"$ref\": \"#/$defs/local\", \"$defs\": { \"local\": { \"$ref\": \"plaintext\" } } }")]
    [InlineData("{ \"type\": \"object\", \"properties\": 3, \"items\": [1], \"anyOf\": {} }")]
    public void Rewrite_OfAnyJson_NeverThrows(string json)
    {
        var (_, types, _) = Compile(ResolveFixture.NestedNote());
        var format = JsonNode.Parse(json);
        var before = CanonicalJson.Write(format);

        var rewritten = CompiledFormats.Rewrite(format, types.Formats, null);

        Assert.Equal(before, CanonicalJson.Write(format));
        if (format is JsonObject && rewritten != null)
            Assert.NotSame(format, rewritten);
        Assert.DoesNotContain("\"$ref\":\"plaintext\"", CanonicalJson.Write(rewritten), StringComparison.Ordinal);
    }

    [Fact]
    public void Hashes_AreTheTypeServices()
    {
        var (_, types, formats) = Compile(ResolveFixture.NestedNote());

        foreach (var (key, _) in formats.Bundle["$defs"]!.AsObject())
        {
            Assert.Equal(FormatTypes.FormatHash(JsonValue.Create(key), types.Formats), (string?)formats.FormatHashes[key]);
            Assert.Equal(FormatTypes.TypeHash(JsonValue.Create(key), types.Formats), (string?)formats.TypeHashes[key]);
        }
    }

    [Fact]
    public void Bindings_AreByFormatAndProducer()
    {
        var (_, _, formats) = Compile(ResolveFixture.NestedNote());

        // Part 07 § 2.9's: a built-in bound in bindings.json through formats, a package format through custom.
        Assert.Equal(["outline", "plaintext", "section_plan", "verdict"], Keys(formats.Bindings));
        Assert.Equal("{\"generated\":{\"agentName\":\"test-json\",\"agentVersion\":48,\"via\":\"format\"}}",
            CanonicalJson.Write(formats.Bindings["plaintext"]));
        Assert.Equal("{\"generated\":{\"agentName\":\"custom-json\",\"agentVersion\":1,\"via\":\"custom\"}}",
            CanonicalJson.Write(formats.Bindings["section_plan"]));
        Assert.Equal("custom", (string?)formats.Bindings["verdict"]!["generated"]!["via"]);
    }

    [Fact]
    public void Bindings_OfAPackageWithNoAgent_AreEmpty()
    {
        var files = Minimal().With("nodes/plan.md", Md(
            SourceFixture.Node("rendered", "Planning the sections", Plain, true, false, "\"if\": null"),
            "Sections for Margaret Okafor's note: history, assessment, plan.\n"));
        var (_, _, formats) = Compile(files);

        Assert.Empty(formats.Bindings);
        Assert.Equal(["markdown_block", "plaintext"], Keys(formats.Bundle["$defs"]!.AsObject()));
    }

    [Fact]
    public void AFunctionsOwnFormat_IsKeyedByItsFunction()
    {
        var (package, types, formats) = Compile(WithOwnFormat());
        var defs = formats.Bundle["$defs"]!.AsObject();
        var function = package.Functions.Items.Single();
        var scope = types.FunctionFormats["draft_section"];

        // D5: <function>__<format>, with the function in its $id; the package format it uses keeps its own key.
        Assert.Empty(formats.Refusals);
        Assert.Contains("draft_section__excerpt", Keys(defs));
        Assert.DoesNotContain("excerpt", Keys(defs));
        Assert.Contains("standard_section", Keys(defs));
        Assert.Equal("urn:consultologist:package:example-minimal@v2026.12.1:function:draft_section:format:excerpt",
            (string?)defs["draft_section__excerpt"]!["$id"]);
        Assert.Equal("#/$defs/plaintext", (string?)defs["draft_section__excerpt"]!["properties"]!["text"]!["$ref"]);

        Assert.Equal(FormatTypes.FormatHash(JsonValue.Create("excerpt"), scope), (string?)formats.FormatHashes["draft_section__excerpt"]);
        Assert.Equal(FormatTypes.TypeHash(JsonValue.Create("excerpt"), scope), (string?)formats.TypeHashes["draft_section__excerpt"]);
        Assert.Equal("custom", (string?)formats.Bindings["draft_section__excerpt"]!["generated"]!["via"]);

        Assert.Equal("{\"$ref\":\"#/$defs/draft_section__excerpt\"}",
            CanonicalJson.Write(CompiledFormats.Rewrite(JsonValue.Create("excerpt"), scope, function)));
        Assert.Equal("{\"$ref\":\"#/$defs/standard_section\"}",
            CanonicalJson.Write(CompiledFormats.Rewrite(JsonValue.Create("standard_section"), scope, function)));
    }

    [Fact]
    public void AFunctionsOwnFormat_Colliding_IsA05004()
    {
        // The package's own draft_section__excerpt, returned by plan, takes the key the function's excerpt would have.
        var files = WithOwnFormat()
            .With("formats/index.json", Index(["standard_section", "draft_section__excerpt"]))
            .With("formats/draft_section__excerpt.json", Json(Excerpt))
            .WithJson("nodes/plan.md", h => h["format"] = "draft_section__excerpt");
        var (_, _, formats) = Compile(files);

        var refusal = Assert.Single(formats.Refusals);
        V31Contract.AssertSentence(refusal);
        Assert.Equal(("A05-004",
                "Function 'draft_section' format 'excerpt' is compiled as 'draft_section__excerpt', which the package already uses: rename one.",
                "functions/draft_section/formats/excerpt.json"),
            (refusal.Id, refusal.Sentence, refusal.Path));
        Assert.Equal("urn:consultologist:package:example-minimal@v2026.12.1:format:draft_section__excerpt",
            (string?)formats.Bundle["$defs"]!["draft_section__excerpt"]!["$id"]);
    }

    [Fact]
    public void ACyclicFormat_NeverReachesTheBundle()
    {
        // outline's points are outlines: S02-032 refuses it before anything is compiled.
        var files = ResolveFixture.NestedNote().WithJson("formats/outline.json",
            o => o["properties"]!["points"]!["items"] = JsonNode.Parse("{ \"$ref\": \"outline\" }"));
        var checkedSource = ResolveFixture.Run(files);
        Assert.Contains(checkedSource.Refusals, r => r.Id == "S02-032");
        Assert.Null(checkedSource.Types);

        // Build on the raw package still returns, the loop rewritten and not followed.
        var registry = ResolveFixture.Published();
        var package = SourceParser.Parse(files, registry.Names).Package!;
        var types = new PackageTypes(
            FormatSet.ForPackage(registry, package.Definitions.Formats.Items),
            new Dictionary<string, FormatSet>(), new Dictionary<string, JsonNode>(), new Dictionary<string, AgentBinding>());
        var formats = CompiledFormats.Build(package, types, registry);

        Assert.Equal("#/$defs/outline", (string?)formats.Bundle["$defs"]!["outline"]!["properties"]!["points"]!["items"]!["$ref"]);
        Assert.Contains("outline", Keys(formats.FormatHashes));
    }
}
