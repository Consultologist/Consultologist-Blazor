using System.Reflection;
using System.Text.Json;
using ApiValue = Consultologist.PackageFormat.ConsultInputValue;
using WebValue = Consultologist.Web.Services.AI.ConsultInputValue;

using Consultologist.PackageFormat;
namespace Consultologist.Web.Tests;

/// <summary>
/// #421: the client mirrors the API's input-value converter by hand — the
/// fourth hand-mirrored fact, after the spec floor, the spec ceiling and the
/// account prefix (SpecVersionMirrorTests). This project references both
/// assemblies, which is what makes the mirror provable.
///
/// One table, both converters: every row must be accepted or refused by
/// both, with the same reason and the same bytes back. A drift here is a
/// client that refuses what the API accepts, or sends what the API refuses.
/// </summary>
public class ConsultInputValueMirrorTests
{
    public static TheoryData<string> TheWireTable => new()
    {
        // v8's two kinds, and the top-level null that reads as blank text
        """{"v":"text"}""",
        """{"v":true}""",
        """{"v":false}""",
        """{"v":null}""",
        // v9 § 4: numbers
        """{"v":1.50}""",
        """{"v":-2}""",
        """{"v":"3"}""",
        """{"v":1e3}""",
        """{"v":-0}""",
        """{"v":79228162514264337593543950336}""",
        """{"v":+3}""",
        """{"v":007}""",
        // objects
        """{"v":{"b":1,"a":"x","c":true}}""",
        """{"v":{"a":1,"a":2}}""",
        """{"v":{"a":{"b":1}}}""",
        """{"v":{"a":[1]}}""",
        """{"v":{"k":null}}""",
        // arrays
        """{"v":[]}""",
        """{"v":["a",null]}""",
        """{"v":[["secret"]]}""",
        """{"v":[{"k":1},{"k":2}]}""",
        """{"v":[{"a":{"b":1}}]}""",
        """{"v":[1.50,"a",null,{"k":false,"n":-2.5}]}"""
    };

    [Theory]
    [MemberData(nameof(TheWireTable))]
    public void TheTwoConvertersAgree(string json)
    {
        Assert.Equal(Outcome<ApiValue>(json), Outcome<WebValue>(json));
    }

    [Fact]
    public void CanonicalIsUnrepresentableForStructure_OnBothSides()
    {
        const string json = """{"v":["a"]}""";

        Assert.Equal(CanonicalOutcome<ApiValue>(json), CanonicalOutcome<WebValue>(json));
        Assert.StartsWith("threw InvalidOperationException", CanonicalOutcome<ApiValue>(json));
    }

    [Fact]
    public void TheCarrierForm_AgreesOnBothSides()
    {
        // #423: AsJson is the string-map carrier between the starter and the
        // renderer. The client never sends one, but the two copies must stay
        // identical, and this is the member a later edit is likeliest to miss.
        const string json = """{"v":[1.50,"a",null,{"z":false,"a":"x"}]}""";

        var api = JsonSerializer.Deserialize<Dictionary<string, ApiValue>>(json)!["v"]!.AsJson();
        var web = JsonSerializer.Deserialize<Dictionary<string, WebValue>>(json)!["v"]!.AsJson();

        Assert.Equal(api, web);
        Assert.Equal("""[1.50,"a",null,{"z":false,"a":"x"}]""", api);
    }

    /// <summary>
    /// What one converter did with a row, as a sentence the other's can be
    /// compared to: accepted — with the kind, the flags and the bytes written
    /// back — or refused, with the exception type and its message.
    /// </summary>
    private static string Outcome<T>(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<Dictionary<string, T>>(json)!["v"]!;

            string Read(string property) =>
                typeof(T).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!
                    .GetValue(value)?.ToString() ?? "null";

            return $"accepted kind={Read("Kind")} blank={Read("IsBlank")} boolean={Read("IsBoolean")} "
                 + $"canonical={Read("HasCanonical")} length={Read("TextLength")} "
                 + $"bytes={JsonSerializer.Serialize(new Dictionary<string, T> { ["v"] = value })}";
        }
        catch (JsonException exception)
        {
            return $"refused {exception.GetType().Name}: {exception.Message}";
        }
    }

    private static string CanonicalOutcome<T>(string json)
    {
        var value = JsonSerializer.Deserialize<Dictionary<string, T>>(json)!["v"]!;

        try
        {
            return "returned " + typeof(T).GetProperty("Canonical")!.GetValue(value);
        }
        catch (TargetInvocationException exception)
        {
            return $"threw {exception.InnerException!.GetType().Name}";
        }
    }
}
