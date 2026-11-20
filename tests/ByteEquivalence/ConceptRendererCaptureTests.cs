using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests.ByteEquivalence;

/// <summary>
/// #1498 (part 10 § 3.3): the built-in Scriban renderers must match the C# renderers (#1397).
/// <see cref="V31BuiltInRenderersTests"/> compares the two live; this freezes the C# renderers'
/// bytes in <c>renderers/concept-renderers.json</c> so the comparison outlives them. The first
/// test writes the fixture from <see cref="ConceptRenderers"/>; the second renders the registry's
/// templates and compares them with the strings read back from the fixture file, never from the C#
/// renderers. The thirteen cases are those of the live comparison; seven more pin order, non-ASCII
/// text, an empty term, a SNOMED concept without a type or an id, a newline in a support, and a long list.
/// </summary>
public class ConceptRendererCaptureTests
{
    private const string FixturePath = "renderers/concept-renderers.json";

    private static readonly IReadOnlyList<(string Name, V31BuiltInRenderersTests.Concept[] Concepts)> AllCases =
    [
        .. V31BuiltInRenderersTests.Cases.Select(c => (c.Key, c.Value)),
        ("two in order",
        [
            new("Type 2 diabetes mellitus", "disorder", "44054006", true, true, null),
            new("Asthma", "disorder", "195967001", true, true, "seasonal"),
        ]),
        ("non-ASCII term",
        [
            new("Anémie ferriprive", "disorder", "87522002", true, true, null),
            new("糖尿病", "disorder", "73211009", true, false, "長期"),
        ]),
        ("empty term", [new("", "finding", "404684003", true, true, null)]),
        ("null type on a SNOMED concept", [new("Hypertension", null, "38341003", true, true, null)]),
        ("null id on a SNOMED concept", [new("Hypertension", "disorder", null, true, true, null)]),
        ("newline in support", [new("Tamoxifen therapy", "procedure", "75367002", true, true, "adjuvant\nendocrine therapy")]),
        ("long list",
            Enumerable.Range(1, 12)
                .Select(i => new V31BuiltInRenderersTests.Concept($"Concept {i}", "finding", $"{1000 + i}", true, i % 2 == 0, i % 3 == 0 ? $"support {i}" : null))
                .ToArray()),
    ];

    public static TheoryData<string, string> CasesAndRenderers
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var name in FixtureCases().Select(c => c.Name))
            {
                data.Add(name, "concept_bullets");
                data.Add(name, "concept_context");
            }

            return data;
        }
    }

    [Fact]
    public void TheCSharpRenderers_GiveTheCapturedBytes()
    {
        var cases = new JsonArray();
        foreach (var (name, concepts) in AllCases)
        {
            var rendered = V31BuiltInRenderersTests.ToConcepts(concepts);
            cases.Add(new JsonObject
            {
                ["name"] = name,
                ["concepts"] = new JsonArray(concepts.Select(c => (JsonNode)new JsonObject
                {
                    ["term"] = c.Term,
                    ["type"] = c.Type,
                    ["id"] = c.Id,
                    ["isSnomedConcept"] = c.IsSnomedConcept,
                    ["isActive"] = c.IsActive,
                    ["support"] = c.Support,
                    ["source"] = c.Source,
                }).ToArray()),
                ["concept_bullets"] = ConceptRenderers.Bullets(rendered),
                ["concept_context"] = ConceptRenderers.Context(rendered),
            });
        }

        var document = new JsonObject { ["cases"] = cases };

        ByteEquivalenceFixtures.Matches(FixturePath, ByteEquivalenceFixtures.WriteJson(document));
    }

    [Theory]
    [MemberData(nameof(CasesAndRenderers))]
    public void TheRegistrysScribanRenderers_GiveTheCapturedBytes(string name, string rendererId)
    {
        var fixture = FixtureCases().Single(c => c.Name == name);

        Assert.Equal(fixture.Expected[rendererId], V31BuiltInRenderersTests.Render(rendererId, fixture.Concepts));
    }

    [Fact]
    public void TheFixture_NamesTheTwentyCases()
    {
        var names = FixtureCases().Select(c => c.Name).ToList();

        Assert.Equal(20, names.Count);
        Assert.Equal(AllCases.Select(c => c.Name), names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The cases as the fixture file holds them: concepts and each renderer's captured string.</summary>
    private static List<(string Name, V31BuiltInRenderersTests.Concept[] Concepts, Dictionary<string, string> Expected)> FixtureCases()
    {
        using var document = JsonDocument.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine([ByteEquivalenceFixtures.Root, .. FixturePath.Split('/')])));

        return document.RootElement.GetProperty("cases").EnumerateArray()
            .Select(c => (
                c.GetProperty("name").GetString()!,
                c.GetProperty("concepts").EnumerateArray().Select(ConceptOf).ToArray(),
                new Dictionary<string, string>
                {
                    ["concept_bullets"] = c.GetProperty("concept_bullets").GetString()!,
                    ["concept_context"] = c.GetProperty("concept_context").GetString()!,
                }))
            .ToList();
    }

    private static V31BuiltInRenderersTests.Concept ConceptOf(JsonElement c) => new(
        c.GetProperty("term").GetString()!,
        c.GetProperty("type").GetString(),
        c.GetProperty("id").GetString(),
        c.GetProperty("isSnomedConcept").GetBoolean(),
        c.GetProperty("isActive").GetBoolean(),
        c.GetProperty("support").GetString(),
        c.GetProperty("source").GetString()!);
}
