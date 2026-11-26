using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1508 (specVersion 31, area 11; R10, ruled by the owner on #1508): <c>resolve.json</c> is canonical JSON, which sorts
/// members, so every schema object in it that has <c>properties</c> (a bundled format at any depth, an input's inline
/// format, a function parameter's) carries <c>propertyOrder</c>, the property names in written order. It is written into
/// the compiled copy alone, after <c>formatHash</c> and <c>typeHash</c> are taken from the source, so neither moves; it is
/// never sent to a model, so <c>sentFormatHash</c> does not move; and a value is validated as before.
/// </summary>
public class PropertyOrderTests
{
    private static readonly string[] Written = ["zeta", "alpha", "mid"];

    private static readonly string[] Rejects = ["format"];

    /// <summary>An object of three titled strings, written <c>zeta</c>, <c>alpha</c>, <c>mid</c>.</summary>
    private const string Three =
        "{ \"type\": \"object\", \"required\": [\"zeta\", \"alpha\", \"mid\"], \"additionalProperties\": false, "
        + "\"properties\": { \"zeta\": { \"type\": \"string\", \"title\": \"Zeta\" }, \"alpha\": { \"type\": \"string\", \"title\": \"Alpha\" }, "
        + "\"mid\": { \"type\": \"string\", \"title\": \"Mid\" } } }";

    /// <summary><see cref="Three"/> titled, as a property.</summary>
    private const string Mid =
        "{ \"title\": \"Mid\", \"type\": \"object\", \"required\": [\"zeta\", \"alpha\", \"mid\"], \"additionalProperties\": false, "
        + "\"properties\": { \"zeta\": { \"type\": \"string\", \"title\": \"Zeta\" }, \"alpha\": { \"type\": \"string\", \"title\": \"Alpha\" }, "
        + "\"mid\": { \"type\": \"string\", \"title\": \"Mid\" } } }";

    /// <summary>The package format <c>ordered</c>: <c>zeta</c> a string, <c>alpha</c> a list of <see cref="Three"/>, <c>mid</c> a <see cref="Mid"/>.</summary>
    private const string Ordered =
        "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"zeta\", \"alpha\", \"mid\"], "
        + "\"additionalProperties\": false, \"properties\": { \"zeta\": { \"type\": \"string\", \"title\": \"Zeta\" }, "
        + "\"alpha\": { \"type\": \"array\", \"title\": \"Alpha\", \"items\": " + Three + " }, \"mid\": " + Mid + " } }";

    /// <summary>An inline input format: <c>zeta</c> and <c>alpha</c> strings, <c>mid</c> a <see cref="Mid"/>.</summary>
    private const string Inline =
        "{ \"type\": \"object\", \"required\": [\"zeta\", \"alpha\", \"mid\"], \"additionalProperties\": false, "
        + "\"properties\": { \"zeta\": { \"type\": \"string\", \"title\": \"Zeta\" }, \"alpha\": { \"type\": \"string\", \"title\": \"Alpha\" }, "
        + "\"mid\": " + Mid + " } }";

    /// <summary>
    /// The minimal package with the format <c>ordered</c>, the inputs <c>details</c> (<see cref="Inline"/>) and
    /// <c>arranged</c> (<c>ordered</c>), the agent <c>arrange</c> returning an <c>ordered</c> (a custom binding) and the agent
    /// <c>terms</c> returning the catalogue's <c>concept_list</c>. Content is fictional (Dr A. Sample).
    /// </summary>
    internal static Dictionary<string, byte[]> Files() =>
        Minimal()
            .With("formats/index.json", Index(["ordered"]))
            .With("formats/ordered.json", Json(Ordered))
            .WithJson("manifest.json", m =>
            {
                var inputs = m["inputs"]!.AsObject();
                inputs["details"] = JsonNode.Parse(
                    "{ \"label\": \"Encounter details\", \"description\": null, \"format\": " + Inline + ", \"required\": false, \"channel\": null }");
                inputs["arranged"] = JsonNode.Parse(
                    "{ \"label\": \"Arranged details\", \"description\": null, \"format\": \"ordered\", \"required\": false, \"channel\": null }");
            })
            .WithAgent("arrange", "Arrange the note for Dr A. Sample.\n", "\"ordered\"")
            .WithAgent("terms", "List the clinical terms in the note for Dr A. Sample.\n", "\"concept_list\"");

    private static CompiledGoldenTests.GoldenCase Case() =>
        new("property-order", Files, "the property order of the formats", CompiledGoldenTests.EngineLimits);

    /// <summary>The compiled <c>resolve.json</c> of <see cref="Files"/>, as publish stores it (canonical: members sorted).</summary>
    private static JsonObject Resolve() => JsonNode.Parse(CompiledGoldenTests.Published(Case()).Resolve)!.AsObject();

    private static IEnumerable<string>? Order(JsonNode? schema) => (schema?["propertyOrder"] as JsonArray)?.Select(n => (string)n!).ToArray();

    [Fact]
    public void AnObjectFormat_KeepsItsPropertyOrder()
    {
        var resolve = Resolve();
        var ordered = resolve["formats"]!["$defs"]!["ordered"]!;

        // Canonical JSON sorts the properties; propertyOrder keeps the order they were written in.
        Assert.Equal(["alpha", "mid", "zeta"], ordered["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal(Written, Order(ordered));
        Assert.Equal(Written, Order(ordered["properties"]!["mid"]));
        Assert.Equal(Written, Order(ordered["properties"]!["alpha"]!["items"]));

        // An input's inline format, and its nested object.
        var details = resolve["inputs"]!["details"]!["format"]!;
        Assert.Equal(Written, Order(details));
        Assert.Equal(Written, Order(details["properties"]!["mid"]));
        Assert.Null(Order(resolve["inputs"]!["arranged"]!["format"]));

        // A catalogue format read from the published type registry, in the order its file writes.
        var concepts = resolve["formats"]!["$defs"]!["concept_list"]!;
        Assert.Equal(["concepts"], Order(concepts));
        Assert.Equal(["term", "type", "id", "isSnomedConcept", "isActive", "source", "support"], Order(concepts["properties"]!["concepts"]!["items"]));

        // A schema with no properties carries none.
        Assert.Null(Order(ordered["properties"]!["zeta"]));
        Assert.Null(Order(resolve["formats"]!["$defs"]!["plaintext"]));
    }

    [Fact]
    public void AFunctionParameterFormat_KeepsItsPropertyOrder()
    {
        // The parameter standard written inline, and the snippet folder whose items the call passes to it.
        const string Standard = "{ \"type\": \"object\", \"required\": [\"title\"], \"additionalProperties\": false, "
            + "\"properties\": { \"title\": { \"$ref\": \"plaintext\" } } }";
        var files = ResolveFixture.Functions()
            .WithJson("functions/draft_section/index.json", index => index["inputs"]!["standard"]!["format"] = JsonNode.Parse(Standard))
            .WithJson("snippets/standards/index.json", index => index["format"] = JsonNode.Parse(Standard));
        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed, CompiledGoldenTests.EngineLimits);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}")));
        var resolve = JsonNode.Parse(compiled!.Resolve)!;

        Assert.Equal(["title"], Order(resolve["functions"]!["draft_section"]!["inputs"]!["standard"]!["format"]));
        Assert.Equal(["title"], Order(resolve["snippets"]!["snippet.standards"]!["format"]));
    }

    [Fact]
    public void FormatHashTypeHashAndSentFormatHash_DoNotMove()
    {
        var resolve = Resolve();
        var (_, types, _) = ResolveFixture.Tree(Files());

        // The published formats' hashes are format-hashes.json's, as frozen before propertyOrder.
        var frozen = JsonNode.Parse(File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(V31Types.TypeFixture.RepoFile("Consultologist.sln"))!, "tests", "V31Types", "Golden", "format-hashes.json")))!;
        foreach (var key in new[] { "concept_list", "markdown_block", "plaintext" })
        {
            Assert.Equal((string?)frozen[key]!["formatHash"], (string?)resolve["formatHashes"]![key]);
            Assert.Equal((string?)frozen[key]!["typeHash"], (string?)resolve["typeHashes"]![key]);
        }

        // The package's own format, and a node returning it, hash as the source does.
        Assert.Equal(FormatTypes.FormatHash(JsonValue.Create("ordered"), types.Formats), (string?)resolve["formatHashes"]!["ordered"]);
        Assert.Equal(FormatTypes.TypeHash(JsonValue.Create("ordered"), types.Formats), (string?)resolve["typeHashes"]!["ordered"]);
        Assert.Equal((string?)resolve["formatHashes"]!["ordered"], (string?)resolve["nodes"]!["node.arrange"]!["formatHash"]);
        Assert.Equal((string?)resolve["typeHashes"]!["ordered"], (string?)resolve["nodes"]!["node.arrange"]!["typeHash"]);

        // The format the run sends, and its hash, are publish's, built from the source.
        var prepared = AgentTurn.Prepare(RunFixture.Plan(Files(), limits: CompiledGoldenTests.EngineLimits),
            AgentTurnTests.Call("node.arrange", new JsonObject()), 100_000, Rejects);
        var expected = EnvelopedFormat.Of("ordered", types.Formats, Rejects);
        Assert.Equal(CanonicalJson.Write(expected.Sent), CanonicalJson.Write(prepared.Format!.Sent));
        Assert.Equal(expected.Hash, (string)prepared.Detail["sentFormatHash"]!);
    }

    [Fact]
    public void TheSentFormat_HasNoPropertyOrder()
    {
        var prepared = AgentTurn.Prepare(RunFixture.Plan(Files(), limits: CompiledGoldenTests.EngineLimits),
            AgentTurnTests.Call("node.arrange", new JsonObject()), 100_000, Rejects);
        Assert.DoesNotContain("propertyOrder", CanonicalJson.Write(prepared.Format!.Sent), StringComparison.Ordinal);
        Assert.DoesNotContain("propertyOrder", prepared.Text, StringComparison.Ordinal);

        // A schema that carries it, as resolve.json's do, is sent without it at every depth, and hashes as one without.
        var withOrder = JsonNode.Parse(
            "{ \"type\": \"object\", \"propertyOrder\": [\"b\", \"a\"], \"required\": [\"b\", \"a\"], \"additionalProperties\": false, "
            + "\"properties\": { \"b\": { \"type\": \"string\" }, \"a\": { \"type\": \"array\", \"items\": { \"type\": \"object\", "
            + "\"propertyOrder\": [\"c\"], \"required\": [\"c\"], \"additionalProperties\": false, \"properties\": { \"c\": { \"type\": \"string\" } } } } } }")!;
        var without = JsonNode.Parse(CanonicalJson.Write(withOrder).Replace("\"propertyOrder\":[\"b\",\"a\"],", "", StringComparison.Ordinal)
            .Replace("\"propertyOrder\":[\"c\"],", "", StringComparison.Ordinal))!;
        var formats = FormatSet.ForPackage(ResolveFixture.Published(), []);

        var sent = SentFormat.Prepare(withOrder, formats, Rejects);

        Assert.DoesNotContain("propertyOrder", CanonicalJson.Write(sent.Schema), StringComparison.Ordinal);
        Assert.Empty(sent.NotSent);
        Assert.Equal(SentFormat.Prepare(without, formats, Rejects).Hash, sent.Hash);
    }

    [Fact]
    public void ASchemaWithPropertyOrder_StillValidatesValues()
    {
        var plan = RunFixture.Plan(Files(), limits: CompiledGoldenTests.EngineLimits);
        var format = JsonNode.Parse("{ \"$ref\": \"#/$defs/ordered\" }");

        // Expand keeps it, as an annotation.
        Assert.Equal(Written, Order(plan.Formats.Expand(format)));

        const string Value = "{ \"zeta\": \"z\", \"alpha\": \"a\", \"mid\": \"m\" }";
        Assert.True(plan.Formats.Validate(JsonNode.Parse("{ \"zeta\": \"Seen today.\", \"alpha\": [" + Value + "], \"mid\": " + Value + " }"), format).Valid);
        Assert.False(plan.Formats.Validate(JsonNode.Parse("{ \"zeta\": \"Seen today.\", \"alpha\": [], \"mid\": { \"zeta\": \"z\" } }"), format).Valid);
        Assert.False(plan.Formats.Validate(JsonNode.Parse("{ \"zeta\": 1, \"alpha\": [], \"mid\": " + Value + " }"), format).Valid);

        var inline = plan.Inputs["details"]["format"];
        Assert.True(plan.Formats.Validate(JsonNode.Parse("{ \"zeta\": \"z\", \"alpha\": \"a\", \"mid\": " + Value + " }"), inline).Valid);
        Assert.False(plan.Formats.Validate(JsonNode.Parse("{ \"zeta\": \"z\", \"alpha\": \"a\", \"mid\": " + Value + ", \"other\": 1 }"), inline).Valid);
    }

    [Fact]
    public void TheSchema_RequiresPropertyOrderBesideProperties()
    {
        var schema = ResolveFixture.PinnedSchema("resolve");
        var resolve = Resolve();
        Assert.True(schema.Validate(resolve.ToJsonString(), null), "the compiled document does not fit resolve.schema.json.");

        void Refused(Action<JsonObject> change, string what)
        {
            var changed = resolve.DeepClone().AsObject();
            change(changed);
            Assert.False(schema.Validate(changed.ToJsonString(), null), $"{what} still fits.");
        }

        Refused(d => d["formats"]!["$defs"]!["ordered"]!.AsObject().Remove("propertyOrder"), "a bundled format without propertyOrder");
        Refused(d => d["formats"]!["$defs"]!["ordered"]!["properties"]!["alpha"]!["items"]!.AsObject().Remove("propertyOrder"), "a bundled format's list's items without propertyOrder");
        Refused(d => d["formats"]!["$defs"]!["concept_list"]!.AsObject().Remove("propertyOrder"), "a catalogue format without propertyOrder");
        Refused(d => d["inputs"]!["details"]!["format"]!.AsObject().Remove("propertyOrder"), "an input's inline format without propertyOrder");
        Refused(d => d["inputs"]!["details"]!["format"]!["properties"]!["mid"]!.AsObject().Remove("propertyOrder"), "an inline format's nested object without propertyOrder");

        // An inline format is the dialect's plus propertyOrder, closed to anything else, at every depth.
        Refused(d => d["inputs"]!["details"]!["format"]!["properties"]!["mid"]!["x-foo"] = 1, "an unknown keyword in an inline format's nested object");
        Refused(d => d["inputs"]!["details"]!["format"]!["additionalProperties"] = true, "an inline object format with additionalProperties true");
        Refused(d => d["inputs"]!["details"]!["format"]!["$schema"] = "urn:consultologist:spec:31:dialect", "an inline format with $schema");
        Refused(d => d["inputs"]!["details"]!["format"]!["properties"]!["zeta"]!["propertyOrder"] = new JsonArray(), "propertyOrder with no properties");
    }
}
