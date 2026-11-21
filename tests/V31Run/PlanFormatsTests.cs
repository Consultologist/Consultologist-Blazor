using System.Text.Json.Nodes;
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
