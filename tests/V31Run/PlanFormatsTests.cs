using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Types;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; 05-D47(1)): values validated against <c>resolve.json</c>'s format bundle. A bundle entry
/// keeps its own <c>$id</c> while its references are <c>#/$defs/&lt;key&gt;</c> of the bundle, so the plan inlines every
/// reference (dropping the inlined entries' <c>$id</c> and <c>$schema</c>) before a value is held to it; a reference that
/// cannot be inlined gives <c>false</c>, which nothing fits. A text format's reply is the text; any other is JSON.
/// </summary>
public class PlanFormatsTests
{
    private static readonly Lazy<PlanFormats> Formats = new(() => RunFixture.Plan("functions").Formats);

    private static JsonNode Ref(string key) => new JsonObject { ["$ref"] = $"#/$defs/{key}" };

    private static JsonNode CheckResult(bool withPass)
    {
        var result = new JsonObject
        {
            ["message"] = null,
            ["passed"] = 2,
            ["total"] = 2,
            ["uncovered"] = null,
            ["untested"] = null,
        };
        if (withPass)
            result["pass"] = true;
        return result;
    }

    [Fact]
    public void Expand_InlinesTheBundle()
    {
        var expanded = Formats.Value.Expand(Ref("check_result"));

        var text = expanded.ToJsonString();
        Assert.DoesNotContain("$ref", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$id", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$schema", text, StringComparison.Ordinal);
        Assert.Equal("object", (string?)expanded["type"]);

        var list = Formats.Value.Expand(new JsonObject { ["type"] = "array", ["items"] = Ref("plaintext") });
        Assert.Equal("string", (string?)list["items"]!["type"]);
        Assert.DoesNotContain("$ref", list.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_HoldsAValueToItsFormat()
    {
        Assert.True(Formats.Value.Validate(CheckResult(true), Ref("check_result")).Valid);

        var missing = Formats.Value.Validate(CheckResult(false), Ref("check_result"));
        Assert.False(missing.Valid);
        Assert.Contains(missing.Faults, f => f.Keyword == "required");

        var listOfText = new JsonObject { ["type"] = "array", ["items"] = Ref("plaintext") };
        Assert.True(Formats.Value.Validate(new JsonArray("a"), listOfText).Valid);
        Assert.False(Formats.Value.Validate(new JsonArray(1), listOfText).Valid);
    }

    [Fact]
    public void ValidateExpanded_ValidatesASelfContainedSchema()
    {
        var validator = new Consultologist.PackageFormat.V31.CorvusFormatValueValidator();
        Assert.True(validator.ValidateExpanded(JsonValue.Create("Margaret Okafor"), JsonNode.Parse("""{"type":"string"}""")!).Valid);
        Assert.False(validator.ValidateExpanded(JsonValue.Create(3), JsonNode.Parse("""{"type":"string"}""")!).Valid);
        Assert.False(validator.ValidateExpanded(JsonValue.Create(3), JsonValue.Create(false)).Valid);
        Assert.True(validator.ValidateExpanded(JsonValue.Create(3), JsonValue.Create(true)).Valid);
    }

    [Fact]
    public void IsText_IsTheThreeTextFormats()
    {
        Assert.True(Formats.Value.IsText(Ref("plaintext")));
        Assert.True(Formats.Value.IsText(Ref("markdown_block")));
        Assert.False(Formats.Value.IsText(Ref("concept_list")));
        Assert.False(Formats.Value.IsText(Ref("check_result")));
        Assert.False(Formats.Value.IsText(new JsonObject { ["type"] = "array", ["items"] = Ref("plaintext") }));
        Assert.False(Formats.Value.IsText(Ref("not_there")));
        Assert.False(Formats.Value.IsText(null));
    }

    [Fact]
    public void Read_IsTheTextOrTheJson()
    {
        var text = Formats.Value.Read("{\"a\":1}", Ref("plaintext"));
        Assert.True(text.Parsed);
        Assert.Equal("{\"a\":1}", text.Value!.GetValue<string>());

        var json = Formats.Value.Read("{\"concepts\":[]}", Ref("concept_list"));
        Assert.True(json.Parsed);
        Assert.Equal("{\"concepts\":[]}", json.Value!.ToJsonString());

        Assert.False(Formats.Value.Read("not json", Ref("concept_list")).Parsed);
        Assert.False(Formats.Value.Read(new string('[', 600) + new string(']', 600), Ref("concept_list")).Parsed);
        Assert.False(Formats.Value.Read("{\"a\":1,\"a\":2}", Ref("concept_list")).Parsed);
    }

    /// <summary>A record of <paramref name="fields"/> text fields (<c>f0</c>, <c>f1</c>, …), as a format file writes it.</summary>
    private static JsonObject Record(int fields)
    {
        var properties = new JsonObject();
        for (var i = 0; i < fields; i++)
            properties[$"f{i}"] = new JsonObject { ["type"] = "string", ["maxLength"] = 80 };
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray("f0") };
    }

    /// <summary>Whether publish builds the full form of <paramref name="schema"/>, a package format file, as its check of the files does (else A03-011).</summary>
    private static bool Publishes(JsonObject schema)
    {
        var file = new SourceFormat("record", "formats/record.json", (JsonObject)schema.DeepClone());
        return FormatTypes.Expands(file.Schema, FormatSet.ForPackage(TypeFixture.Published(), [file]));
    }

    private static PlanFormats BundleOf(JsonObject record) =>
        new(new JsonObject { ["$defs"] = new JsonObject { ["record"] = record } });

    [Fact]
    public void AFormatPublishAccepts_ExpandsAtRun_UpToTheSameBudget()
    {
        // The largest record A03-011 lets through: itself and 9,999 fields are FormatTypes.SchemaBudget schemas.
        var largest = Record(FormatTypes.SchemaBudget - 1);
        Assert.True(Publishes(largest));
        var formats = BundleOf(largest);

        var expanded = Assert.IsType<JsonObject>(formats.Expand(Ref("record")));
        Assert.Equal(FormatTypes.SchemaBudget - 1, (expanded["properties"] as JsonObject)?.Count);
        Assert.True(formats.Validate(new JsonObject { ["f0"] = "Margaret Okafor" }, Ref("record")).Valid);
        Assert.False(formats.Validate(new JsonObject { ["f0"] = 3 }, Ref("record")).Valid);

        // A site's own schema around it is counted apart from the format it refers to.
        var list = new JsonObject { ["type"] = "array", ["items"] = Ref("record") };
        Assert.True(formats.Validate(new JsonArray(new JsonObject { ["f0"] = "Margaret Okafor" }), list).Valid);

        // One field more is past the budget at publish (A03-011) and at run alike.
        var tooLarge = Record(FormatTypes.SchemaBudget);
        Assert.False(Publishes(tooLarge));
        Assert.Equal("false", BundleOf(tooLarge).Expand(Ref("record")).ToJsonString());
    }

    [Fact]
    public void AFormatThatDoublesAtEveryLevel_ExpandsToFalse()
    {
        // Each entry refers to the next twice, once where a schema is and once where one is not: 2^20 copies if written, 40 levels deep.
        var defs = new JsonObject { ["d20"] = new JsonObject { ["type"] = "string" } };
        for (var i = 0; i < 20; i++)
        {
            defs[$"d{i}"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["a"] = Ref($"d{i + 1}") },
                ["x-note"] = new JsonArray(Ref($"d{i + 1}")),
            };
        }

        var formats = new PlanFormats(new JsonObject { ["$defs"] = defs });

        Assert.Equal("false", formats.Expand(Ref("d0")).ToJsonString());
        Assert.False(formats.Validate(new JsonObject(), Ref("d0")).Valid);
    }

    [Fact]
    public void ASelfReference_ExpandsToFalse_AndValidatesNothing()
    {
        var bundle = new JsonObject
        {
            ["$defs"] = new JsonObject
            {
                ["chain"] = new JsonObject
                {
                    ["$id"] = "urn:consultologist:package:example@v2026.12.1:format:chain",
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["next"] = Ref("chain") },
                },
                ["plaintext"] = new JsonObject { ["type"] = "string" },
            },
        };
        var formats = new PlanFormats(bundle);

        var expanded = formats.Expand(Ref("chain"));
        Assert.Equal("false", expanded.ToJsonString());
        Assert.False(formats.Validate(new JsonObject(), Ref("chain")).Valid);
        Assert.False(formats.Validate(null, Ref("chain")).Valid);

        Assert.Equal("false", formats.Expand(Ref("not_there")).ToJsonString());
        Assert.False(formats.Validate(JsonValue.Create("Dr A. Sample"), Ref("not_there")).Valid);
        Assert.True(formats.Validate(JsonValue.Create("Dr A. Sample"), Ref("plaintext")).Valid);
    }
}
