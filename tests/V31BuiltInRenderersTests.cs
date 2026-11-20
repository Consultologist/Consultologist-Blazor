using System.Globalization;
using Consultologist.PackageFormat;
using Scriban;
using Scriban.Runtime;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1497 (specVersion 31, part 05 § 5.3): the two built-in renderers are Scriban templates in the
/// package-format registry (<c>v31/renderers/&lt;id&gt;.md</c>), and the C# renderers stay as the
/// byte-equivalence reference. Each template is rendered the way the 31 engine will render it
/// (Scriban 7.2.5, the invariant culture, the JSON member names, <c>value</c> the piped value) and
/// compared byte for byte with <see cref="ConceptRenderers"/> on the same concepts.
///
/// <c>concept_context</c> prints each concept's <c>source</c>, which the stored concept list carries
/// (ConceptOutputContract.Deserialize stamps it from the producing node) though the concept_list
/// format does not declare it. Each case holds the source per concept, and the template and the C#
/// renderer both see that same source.
/// </summary>
public class V31BuiltInRenderersTests
{
    private sealed record Concept(string Term, string? Type, string? Id, bool IsSnomedConcept, bool IsActive, string? Support, string Source = ConceptSamples.Source);

    private static readonly IReadOnlyDictionary<string, Concept[]> Cases = new Dictionary<string, Concept[]>
    {
        ["empty"] = [],
        ["one active SNOMED"] = [new("Type 2 diabetes mellitus", "disorder", "44054006", true, true, null)],
        ["one not SNOMED"] = [new("Lives alone", "finding", null, false, false, null)],
        ["one inactive"] = [new("Diabetes mellitus", "disorder", "73211009", true, false, null)],
        ["with support"] = [new("Tamoxifen therapy", "procedure", "75367002", true, true, "adjuvant endocrine therapy")],
        ["without support"] = [new("Tamoxifen therapy", "procedure", "75367002", true, true, null)],
        ["empty support"] = [new("Tamoxifen therapy", "procedure", "75367002", true, true, "")],
        ["whitespace support"] = [new("Tamoxifen therapy", "procedure", "75367002", true, true, " \t ")],
        ["tab and newline only support"] = [new("Tamoxifen therapy", "procedure", "75367002", true, true, "\t\r\n\t")],
        ["different source"] = [new("Tamoxifen therapy", "procedure", "75367002", true, true, "adjuvant", "typical")],
        ["empty source"] = [new("Tamoxifen therapy", "procedure", "75367002", true, true, null, "")],
        ["three mixed"] =
        [
            new("Malignant neoplasm of breast", "disorder", "254837009", true, true, "stage II"),
            new("Diabetes mellitus", "disorder", "73211009", true, false, "  long-standing  ", "draft"),
            new("Family support strong", null, null, false, false, null),
        ],
        ["markdown characters"] = [new("*Bold* _term_ `code` [link](x) # | <b>", "dis*order*", "123", true, true, "> quote {{ not a tag }}")],
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ConceptBullets_MatchesTheCSharpRenderer_ByteForByte(string name) =>
        Assert.Equal(ConceptRenderers.Bullets(ToConcepts(Cases[name])), Render("concept_bullets", Cases[name]));

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ConceptContext_MatchesTheCSharpRenderer_ByteForByte(string name) =>
        Assert.Equal(ConceptRenderers.Context(ToConcepts(Cases[name])), Render("concept_context", Cases[name]));

    private static List<ClinicalConcept> ToConcepts(Concept[] concepts) => concepts
        .Select(c => new ClinicalConcept(c.Term, c.Type ?? "", c.Id ?? "", c.IsSnomedConcept, c.IsActive, c.Source, c.Support))
        .ToList();

    private static string Render(string rendererId, Concept[] concepts)
    {
        var list = new ScriptArray();
        foreach (var c in concepts)
        {
            list.Add(new ScriptObject
            {
                ["term"] = c.Term,
                ["type"] = c.Type,
                ["id"] = c.Id,
                ["isSnomedConcept"] = c.IsSnomedConcept,
                ["isActive"] = c.IsActive,
                ["support"] = c.Support,
                ["source"] = c.Source,
            });
        }

        var value = new ScriptObject { ["concepts"] = list };
        var globals = new ScriptObject { ["value"] = value };
        var context = new TemplateContext { MemberRenamer = member => member.Name, StrictVariables = true };
        context.PushCulture(CultureInfo.InvariantCulture);
        context.PushGlobal(globals);

        var template = Template.Parse(BodyOf(rendererId));
        Assert.False(template.HasErrors, string.Join("; ", template.Messages));
        return template.Render(context);
    }

    /// <summary>The text after the second <c>---</c> line (and that line's newline).</summary>
    private static string BodyOf(string rendererId)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(
            dir!.FullName, "external", "consultologist-package-format", "v31", "renderers", rendererId + ".md");

        Assert.True(
            File.Exists(path),
            $"{path} is missing — the submodule is not checked out (git submodule update --init).");

        var lines = File.ReadAllText(path).Split('\n');
        var fences = 0;
        var body = new List<string>();
        foreach (var line in lines)
        {
            if (fences >= 2)
            {
                body.Add(line);
            }
            else if (line.TrimEnd('\r') == "---")
            {
                fences++;
            }
        }

        return string.Join('\n', body);
    }
}
