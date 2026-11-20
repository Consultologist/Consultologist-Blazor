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
