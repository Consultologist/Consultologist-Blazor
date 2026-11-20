using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): <see cref="SourceParser.Parse"/> never throws on what an upload
/// holds, only on a caller's argument that is null, and the published registry lets the full
/// fixture through.
/// </summary>
public class SourceParserRobustnessTests
{
    [Fact]
    public void AnyOneFileBroken_IsRefused_AndNeverThrows()
    {
        var random = new Random(31);
        var noise = new byte[64];
        random.NextBytes(noise);
        var full = SourceFixture.Full();

        foreach (var path in full.Keys.ToList())
        {
            var mustRefuse = path is not ("README.md" or "dag.mmd");
            var breakages = new (string Name, Action<Dictionary<string, byte[]>> Apply, bool Refuses)[]
            {
                ("removed", f => f.Remove(path), mustRefuse),
                ("empty", f => f[path] = [], mustRefuse),
                ("{}", f => f[path] = SourceFixture.Utf8("{}"), false),
                ("[]", f => f[path] = SourceFixture.Utf8("[]"), mustRefuse),
                ("null", f => f[path] = SourceFixture.Utf8("null"), mustRefuse),
                ("---", f => f[path] = SourceFixture.Utf8("---"), mustRefuse),
                ("empty header", f => f[path] = SourceFixture.Utf8("---\n{}\n---\n"), false),
                ("random bytes", f => f[path] = noise, mustRefuse),
            };

            foreach (var (name, apply, refuses) in breakages)
            {
                var files = SourceFixture.Full();
                apply(files);

                var result = SourceParser.Parse(files, SourceFixture.EmptyRegistry());

                Assert.True(result.Refusals.Count == 0 == (result.Package != null), $"{path} {name}: Package and Refusals disagree.");
                if (refuses)
                    Assert.True(result.Refusals.Count > 0, $"{path} {name}: nothing was refused.");
            }
        }
    }

    /// <summary>The values every member of Full() is given in turn by <see cref="AnyMemberMutated_IsJudged_AndNeverThrows"/>.</summary>
    private static JsonNode?[] HostileValues() =>
    [
        null, true, 0, -1, 1.5, JsonNode.Parse("1e30"), "", " ", "x", new string('x', 300),
        new JsonArray(), new JsonArray((JsonNode?)null), new JsonArray(1), new JsonObject(), new JsonObject { ["a"] = 1 },
        Enumerable.Range(0, 32).Aggregate((JsonNode)new JsonArray(), (inner, _) => new JsonArray(inner)),
    ];

    /// <summary>Every member of <paramref name="node"/> at any depth, as the steps (a name or an index) to its parent and its own name.</summary>
    private static IEnumerable<(object[] Parent, string Name)> Members(JsonNode? node, object[] at)
    {
        switch (node)
        {
            case JsonObject json:
                foreach (var (name, value) in json)
                {
                    yield return (at, name);
                    foreach (var inner in Members(value, [.. at, name]))
                        yield return inner;
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    foreach (var inner in Members(array[i], [.. at, i]))
                        yield return inner;
                }

                break;
        }
    }

    private static JsonObject Walk(JsonNode root, object[] steps) =>
        steps.Aggregate(root, (node, step) => step is string name ? node[name]! : node[(int)step]!).AsObject();

    /// <summary>
    /// Every member, at any depth, of every JSON file and header of Full() is given each hostile value,
    /// deleted, and renamed. The whole product is about 10,000 parses (about ten seconds), so each member
    /// gets half of its 18 mutations, drawn with <c>new Random(31)</c>: every member and every mutation
    /// is still tried, and the run is the same every time. The full product passed once when this test
    /// was written.
    /// </summary>
    [Fact]
    public void AnyMemberMutated_IsJudged_AndNeverThrows()
    {
        var full = SourceFixture.Full();
        var registry = SourceFixture.EmptyRegistry();
        var random = new Random(31);
        var runs = 0;

        foreach (var path in full.Keys.Order(StringComparer.Ordinal).ToList())
        {
            var text = SourceFixture.Text(full, path);
            var isHeader = path.EndsWith(".md", StringComparison.Ordinal);
            var split = isHeader ? FrontMatter.Split(text) : null;
            if (isHeader && split == null)
                continue; // README.md: no JSON in it
            var root = JsonNode.Parse(split?.Header ?? text)!;

            foreach (var (parent, name) in Members(root, []).ToList())
            {
                var mutations = HostileValues()
                    .Select(value => ((string, Action<JsonObject>))($"= {value?.ToJsonString() ?? "null"}", json => json[name] = value))
                    .Append(("deleted", json => json.Remove(name)))
                    .Append(("renamed", json =>
                    {
                        var value = json[name]?.DeepClone();
                        json.Remove(name);
                        json[name + "_renamed"] = value;
                    }))
                    .OrderBy(_ => random.Next())
                    .Take(9);

                foreach (var (what, mutate) in mutations)
                {
                    var copy = root.DeepClone();
                    mutate(Walk(copy, parent));
                    var json = copy.ToJsonString();
                    var files = new Dictionary<string, byte[]>(full)
                    {
                        [path] = SourceFixture.Utf8(split != null ? SourceFixture.Md(json, split.Body) : json + "\n"),
                    };

                    var where = $"{path} {string.Join('.', parent.Append(name))} {what}";
                    var result = SourceParser.Parse(files, registry);
                    runs++;

                    Assert.True(result.Refusals.Count == 0 == (result.Package != null), $"{where}: Package and Refusals disagree.");
                }
            }
        }

        Assert.True(runs > 4000, $"only {runs} mutations ran");
    }

    [Fact]
    public void AnExpressionSite_OfAnyType_NeverThrows()
    {
        JsonNode?[] values = [null, 1, "x", true, new JsonObject(), new JsonArray(1, null, new JsonObject()), new JsonObject { ["parts"] = 1 }];
        string[] files =
        [
            "nodes/plan.md", "nodes/section_count.md", "checks/all_sections.md", "nodes/sections/index.json", "nodes/refine/index.json",
            "nodes/billing_gate/index.json", "snippets/closing_line.md", "macros/phrases/index.json", "macros/phrases/thanks.md",
            "deliverables/emr/consult.md",
        ];
        foreach (var file in files)
        {
            foreach (var member in new[] { "if", "expression", "parts", "until", "while" })
            {
                foreach (var value in values)
                {
                    var result = SourceFixture.Full().WithJson(file, json => json[member] = value?.DeepClone()).Parse();

                    Assert.Equal(result.Refusals.Count == 0, result.Package != null);
                }
            }
        }

        // What a part holds is a later area's: a part that is not an object is only not counted.
        var parts = SourceFixture.Full().WithJson("nodes/plan.md", json => json["parts"] = new JsonObject { ["tone"] = 1, ["voice"] = null }).Parse();
        Assert.Equal(parts.Refusals.Count == 0, parts.Package != null);
    }

    [Fact]
    public void AnEmptyUpload_IsRefused()
    {
        var result = SourceParser.Parse(new Dictionary<string, byte[]>(), SourceFixture.EmptyRegistry());

        Assert.Null(result.Package);
        Assert.Equal(1, result.Refusals.Count(r => r.Id == "A02-003"));
        Assert.Equal(9, result.Refusals.Count(r => r.Id == "S01-008"));
        Assert.Equal(10, result.Refusals.Count);
        foreach (var refusal in result.Refusals)
            V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void ANullUploadOrRegistry_IsTheCallersError()
    {
        Assert.Throws<ArgumentNullException>(() => SourceParser.Parse(null!, SourceFixture.EmptyRegistry()));
        Assert.Throws<ArgumentNullException>(() => SourceParser.Parse(SourceFixture.Full(), null!));
    }

    [Fact]
    public void Full_Parses_WithThePublishedRegistry()
    {
        var registry = V31Contract.PublishedRegistry();

        Assert.Contains("plaintext", registry.BuiltInFormats);
        Assert.Contains("concept_list", registry.CatalogueFormats);
        Assert.NotEmpty(registry.BuiltInRenderers);

        var result = SourceFixture.Full().Parse(registry);

        Assert.Empty(result.Refusals);
        Assert.NotNull(result.Package);
    }
}
