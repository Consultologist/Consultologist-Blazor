using System.Text.Json;
using Consultologist.PackageFormat.V31;
using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 20 (#1509; part 08 § 10, part 09 § 6.4 "Deliverables"): a deliverable's reads grouped by its template's sites, in
/// source order, each with its substitutions per pass; a substitution's text cut from the composed text by position, in
/// runes; and the template shown from the pinned version only when its hash is the row's <c>templateHash</c>.
/// </summary>
public class TemplateReads31Tests
{
    private static Row31 Deliverable(string json) => new("deliverable.emr.note", "deliverable", JsonDocument.Parse(json).RootElement.Clone());

    [Fact]
    public void Reads_AreGroupedBySite_InSourceOrder()
    {
        var row = Deliverable("""
            { "sites": [ { "site": 1, "at": "1:1", "expr": "node.plan.content", "loop": null },
                         { "site": 2, "at": "3:3", "expr": "item.title", "loop": "node.sections.items" },
                         { "site": 3, "at": "5:1", "expr": "profile.signature", "loop": null } ],
              "substitutions": [ { "site": 1, "pass": null, "reads": "node.plan.content", "hash": "sha256:s1", "start": 0, "size": 12, "renders": null },
                                 { "site": 2, "pass": [1], "reads": "item.title", "hash": "sha256:s2", "start": 14, "size": 4, "renders": null },
                                 { "site": 2, "pass": [2], "reads": "item.title", "hash": "sha256:s3", "start": 19, "size": 5, "renders": "renderer.bullets" },
                                 { "site": 3, "pass": null, "reads": "profile.signature", "hash": "sha256:s4", "start": 26, "size": 0, "renders": null } ] }
            """);

        var sites = TemplateReads31.Group(row);

        Assert.Equal([1, 2, 3], sites.Select(site => site.Site));
        Assert.Equal(new SiteReads31(1, "1:1", "node.plan.content", null, sites[0].Passes), sites[0]);
        Assert.Equal("node.sections.items", sites[1].Loop);
        Assert.Equal(2, sites[1].Passes.Count);
        Assert.Equal([1], sites[1].Passes[0].Pass!);
        Assert.Equal([2], sites[1].Passes[1].Pass!);
        Assert.Equal("renderer.bullets", sites[1].Passes[1].Renders);
        Assert.Equal(19, sites[1].Passes[1].Start);
        Assert.Equal(5, sites[1].Passes[1].Size);
        Assert.Null(sites[0].Passes[0].Pass);
        Assert.Equal(0, sites[2].Passes[0].Size);
    }

    [Fact]
    public void ASiteKeepsTheOrderTheRowGives_AndASubstitutionWithNoSiteListed_IsKeptAfter()
    {
        var row = Deliverable("""
            { "sites": [ { "site": 2, "at": "1:1", "expr": "b", "loop": null }, { "site": 1, "at": "2:1", "expr": "a", "loop": null } ],
              "substitutions": [ { "site": 1, "pass": null, "reads": "a", "hash": "sha256:a", "start": 0, "size": 1, "renders": null },
                                 { "site": 9, "pass": null, "reads": "z", "hash": "sha256:z", "start": 2, "size": 1, "renders": null },
                                 { "site": 2, "pass": null, "reads": "b", "hash": "sha256:b", "start": 1, "size": 1, "renders": null } ] }
            """);

        var sites = TemplateReads31.Group(row);

        Assert.Equal([2, 1, 9], sites.Select(site => site.Site));
        Assert.Equal("", sites[2].At);
        Assert.Equal("z", sites[2].Passes.Single().Reads);
    }

    [Fact]
    public void ARowWithNoSitesOrSubstitutions_HasNoReads_AndNothingThrows()
    {
        Assert.Empty(TemplateReads31.Group(Deliverable("""{ "sites": null }""")));
        Assert.Empty(TemplateReads31.Group(Deliverable("""{ "sites": [ 3, "x" ], "substitutions": [ { "site": "one" } ] }""")));
    }

    [Fact]
    public void Slice_CutsTheComposedText_ByPositionInRunes()
    {
        // "Dr 😀 A. Sample": the emoji is one rune and two UTF-16 units
        const string composed = "Dr 😀 A. Sample";

        Assert.Equal("😀", TemplateReads31.Slice(composed, 3, 1));
        Assert.Equal("A. Sample", TemplateReads31.Slice(composed, 5, 9));
        Assert.Equal("", TemplateReads31.Slice(composed, 2, 0));
        Assert.Null(TemplateReads31.Slice(composed, 10, 9));
        Assert.Null(TemplateReads31.Slice(composed, -1, 1));
        Assert.Null(TemplateReads31.Slice(null, 0, 1));
    }

    private const string Body = "## Sections\n\n{{ node.standards.content }}\n";

    private static JsonElement Resolve(string body) =>
        JsonSerializer.SerializeToElement(new { deliverables = new Dictionary<string, object> { ["deliverable.emr.note"] = new { body, bodyHash = "sha256:whatever" } } });

    [Fact]
    public void TheTemplate_IsThePinnedBody_WhenItsHashIsTheRowsTemplateHash()
    {
        var row = Deliverable($$"""{ "templateHash": "{{CanonicalJson.HashText(Body)}}" }""");

        Assert.Equal(Body, TemplateReads31.Template(Resolve(Body), row));
    }

    [Fact]
    public void TheTemplate_IsNotShown_WhenTheHashDiffers_OrTheVersionWasNotRead()
    {
        var row = Deliverable($$"""{ "templateHash": "{{CanonicalJson.HashText(Body)}}" }""");

        Assert.Null(TemplateReads31.Template(Resolve(Body + "changed"), row));
        Assert.Null(TemplateReads31.Template(null, row));
        Assert.Null(TemplateReads31.Template(Resolve(Body), Deliverable("{}")));
        Assert.Null(TemplateReads31.Template(JsonSerializer.SerializeToElement(new { deliverables = new { } }), row));
    }

    [Fact]
    public void TheBundle_IsTheEntrysBundle()
    {
        var resolve = JsonSerializer.SerializeToElement(new { deliverables = new Dictionary<string, object>
        {
            ["deliverable.referral_pack.gp_letter"] = new { bundle = "zip" },
            ["deliverable.consult"] = new { bundle = (string?)null },
        } });

        Assert.Equal("zip", TemplateReads31.Bundle(resolve, "deliverable.referral_pack.gp_letter"));
        Assert.Null(TemplateReads31.Bundle(resolve, "deliverable.consult"));
        Assert.Null(TemplateReads31.Bundle(null, "deliverable.consult"));
    }
}
