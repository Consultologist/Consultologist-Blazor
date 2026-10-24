using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1098 (Milestone 31): the Renderers nav group at 25 — the engine's renderer pipes
/// (concept_context, concept_bullets) as read-only entries after Formats, each opening a
/// pane keyed renderer:&lt;name&gt; that names the format it renders, the spelling to insert
/// (one per node of that format, else the generic one), a Copy button with transient
/// feedback, and "Used by" rows from the walk — the template piping a node through the
/// pipe and the node, each a link. Nothing is added, edited or discarded there; a ≤24
/// package shows no group.
/// </summary>
public class TemplatesV25RenderersPaneTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static string SelectedNav(IRenderedComponent<Templates> page) =>
        page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim();

    private static IReadOnlyList<string> Groups(IRenderedComponent<Templates> page) =>
        page.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()).ToList();

    private WorkflowEditorSession Session => Services.GetRequiredService<WorkflowEditorSession>();

    private IRenderedComponent<Templates> OpenRenderer(string pipe, WorkflowPackageContentResponse? fixture = null)
    {
        var page = RenderEditor(fixture);
        Session.SelectedKey = $"renderer:{pipe}";
        page.Render();
        return page;
    }

    /// <summary>The V25 fixture with its assessment prompt piping the concept_list node through concept_context — reshaped here alone.</summary>
    private static WorkflowPackageContentResponse V25WithARenderedRead()
    {
        var package = EditorFixtures.V25();
        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal);
        files["prompts/assessment.md"] = files["prompts/assessment.md"] + "\n{{ node.extract.content | concept_context }}";
        return package with { Files = files };
    }

    /// <summary>The V25 fixture with no node of format concept_list — extract made prose.</summary>
    private static WorkflowPackageContentResponse V25WithoutAConceptListNode()
    {
        var package = EditorFixtures.V25();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        var extract = root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "extract")!.AsObject();
        extract["format"] = "prose";
        // coverage reads node.extract as a concept_list; drop it and the deliverable's check so the walk stays quiet.
        var nodes = root["nodes"]!.AsArray();
        var coverage = nodes.First(n => n!["id"]!.GetValue<string>() == "coverage");
        nodes.Remove(coverage);
        root["deliverables"]!.AsArray()[0]!.AsObject().Remove("check");
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    /// <summary>#1138: the V25 fixture with a second concept_list node, extract_more, that nothing pipes — plus assessment piping extract.</summary>
    private static WorkflowPackageContentResponse V25WithTwoConceptListNodes()
    {
        var package = V25WithARenderedRead();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["nodes"]!.AsArray().Add(JsonNode.Parse("""{ "id": "extract_more", "label": "More concepts", "prompt": "extract", "format": "concept_list", "onInvalid": "fail" }"""));
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    // ----- #1138 § 1: what it produces -----

    [Fact]
    public void WhatItProduces_RendersTheSample_ThroughTheLibrary_WithTheFirstNodesSource()
    {
        var page = OpenRenderer("concept_context");

        var sample = page.Find("pre.renderer-sample");
        Assert.Equal(Consultologist.PackageFormat.TemplatePipes.SpecOf("concept_context")!.Render(Consultologist.PackageFormat.ConceptSamples.ConceptListJson, "extract"), sample.TextContent);
        Assert.Contains("source: extract", sample.TextContent);
        Assert.Contains("active: False", sample.TextContent);
        Assert.Contains("extract", page.Find(".renderer-sample__caption code").TextContent);

        Session.SelectedKey = "renderer:concept_bullets";
        page.Render();
        var bullets = page.Find("pre.renderer-sample").TextContent;
        Assert.Contains("[not active SNOMED concept]", bullets);
        Assert.Contains("[not SNOMED concept]", bullets);
        Assert.DoesNotContain("source:", bullets);
    }

    [Fact]
    public void WhatItProduces_UsesThePlaceholder_WhenNoNodeHasTheFormat()
    {
        var page = OpenRenderer("concept_context", V25WithoutAConceptListNode());

        Assert.Contains("source: <id>", page.Find("pre.renderer-sample").TextContent);
        Assert.Contains("<id>", page.Find(".renderer-sample__caption").TextContent);
    }

    // ----- #1138 § 2: what it renders -----

    [Fact]
    public void WhatItRenders_ListsTheSchemasSixFields_AndOpensTheFormatPane()
    {
        var page = OpenRenderer("concept_context");

        Assert.Contains("served by concept-extraction@1, attested", page.Find(".renderer-format").TextContent);
        var fields = page.FindAll(".renderer-field");
        Assert.Equal(
            new[] { "concepts[].term", "concepts[].type", "concepts[].id", "concepts[].isSnomedConcept", "concepts[].isActive", "concepts[].support" },
            fields.Select(f => f.GetAttribute("data-renderer-field")));
        Assert.Contains("string | null", page.Find(".renderer-field[data-renderer-field='concepts[].id']").TextContent);
        Assert.Contains("boolean", page.Find(".renderer-field[data-renderer-field='concepts[].isActive']").TextContent);
        Assert.All(fields, f => Assert.Contains("required", f.TextContent));

        page.Find("fluent-button[aria-label='Open format concept_list']").Click();
        Assert.Equal("catalogue:concept_list", Session.SelectedKey);
    }

    [Fact]
    public void Candidates_AreMarkedPipedOrNot_WithSpellingAndCopy()
    {
        var page = OpenRenderer("concept_context", V25WithTwoConceptListNodes());

        var piped = page.Find(".renderer-spelling[data-renderer-node='extract']");
        Assert.Equal("true", piped.GetAttribute("data-renderer-piped"));
        Assert.Contains("piped by Prompt 'assessment'", piped.TextContent);
        var candidate = page.Find(".renderer-spelling[data-renderer-node='extract_more']");
        Assert.Equal("false", candidate.GetAttribute("data-renderer-piped"));
        Assert.Contains("not yet piped", candidate.TextContent);
        Assert.Equal("{{ node.extract_more.content | concept_context }}", candidate.QuerySelector("code")!.TextContent.Trim());
        Assert.NotNull(candidate.QuerySelector("fluent-button[aria-label='Copy the spelling for node extract_more']"));
        Assert.Contains("2 nodes · 1 piped", page.Find(".renderer-nodes__count").TextContent);
    }

    /// <summary>#1138: the V25 fixture with assessment piping extract inside an if — the guard a Used by row shows.</summary>
    private static WorkflowPackageContentResponse V25WithAGuardedRenderedRead()
    {
        var package = EditorFixtures.V25();
        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal);
        files["prompts/assessment.md"] = files["prompts/assessment.md"] + "\n{{ if input.kind == \"new\" }}{{ node.extract.content | concept_context }}{{ end }}";
        return package with { Files = files };
    }

    /// <summary>#1138: the V25 fixture with the scope prompt piping extract — scope's node reaches no deliverable.</summary>
    private static WorkflowPackageContentResponse V25WithARenderedReadNoDeliverableCarries()
    {
        var package = EditorFixtures.V25();
        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal);
        files["prompts/scope.md"] = files["prompts/scope.md"] + "\n{{ node.extract.content | concept_bullets }}";
        return package with { Files = files };
    }

    // ----- #1138 § 3: attestation -----

    [Fact]
    public void Attestation_NamesTheAgent_TheDeterminism_AndOnlyWhatTheStampRecords()
    {
        var page = OpenRenderer("concept_context");

        var text = page.Find(".renderer-attestation").TextContent;
        Assert.Contains("concept-extraction@1", text);
        Assert.Contains("attested", text);
        Assert.Contains("same bytes", text);
        Assert.Contains("SNOMED CT", text);
        Assert.Contains("not a separate stamp field", text);
        Assert.Contains("source: …", text);

        Session.SelectedKey = "renderer:concept_bullets";
        page.Render();
        var bullets = page.Find(".renderer-attestation").TextContent;
        Assert.DoesNotContain("source: …", bullets);
        Assert.Contains("these bytes carry none", bullets);
    }

    // ----- #1138 § 4: Used by, deeper -----

    [Fact]
    public void UsedBy_ShowsTheGuard_AndTheCarryingDeliverables()
    {
        var page = OpenRenderer("concept_context", V25WithAGuardedRenderedRead());

        var row = Assert.Single(page.FindAll(".renderer-user"));
        var guard = row.QuerySelector(".renderer-user__guard")!;
        Assert.Contains("included when", guard.TextContent);
        Assert.Equal("input.kind == \"new\"", guard.QuerySelector("code")!.TextContent);

        // assessment is aggregated by all, whose deliverable is consult.
        var carried = Assert.Single(row.QuerySelectorAll(".renderer-user__deliverable"));
        Assert.Equal("Consultation note", carried.TextContent.Trim());
        Assert.Empty(row.QuerySelectorAll(".renderer-user__none"));

        page.Find(".renderer-user fluent-button[aria-label='Open deliverable consult']").Click();
        Assert.Equal("results:consult", Session.SelectedKey);
    }

    [Fact]
    public void UsedBy_HasNoGuard_WhenTheReadIsBare_AndSaysWhenNoDeliverableCarriesIt()
    {
        var page = OpenRenderer("concept_context", V25WithARenderedRead());
        var row = Assert.Single(page.FindAll(".renderer-user"));
        Assert.Empty(row.QuerySelectorAll(".renderer-user__guard"));
        Assert.Single(row.QuerySelectorAll(".renderer-user__deliverable"));

        page = OpenRenderer("concept_bullets", V25WithARenderedReadNoDeliverableCarries());
        row = Assert.Single(page.FindAll(".renderer-user"));
        Assert.Equal("Prompt 'scope'", row.QuerySelector(".renderer-user__template")!.TextContent.Trim());
        Assert.Empty(row.QuerySelectorAll(".renderer-user__deliverable"));
        Assert.Contains("no deliverable yet", row.QuerySelector(".renderer-user__none")!.TextContent);
    }

    [Fact]
    public void TheGroup_ListsThePipes_At25_AfterFormats_AndIsAbsentBelow()
    {
        var page = RenderEditor();

        var groups = Groups(page);
        Assert.Equal(groups.ToList().IndexOf("Formats") + 1, groups.ToList().IndexOf("Renderers"));

        var entries = page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Replace("●", string.Empty).Trim()).ToList();
        Assert.Contains("concept_context", entries);
        Assert.Contains("concept_bullets", entries);
        Assert.DoesNotContain(entries, e => e.StartsWith("+ Renderer", StringComparison.Ordinal));

        var below = RenderEditor(EditorFixtures.V12());
        Assert.DoesNotContain("Renderers", Groups(below));
    }

    [Fact]
    public void TheKey_OpensThePane_NamingTheFormatItRenders()
    {
        var page = OpenRenderer("concept_context");

        Assert.Equal("concept_context", SelectedNav(page));
        Assert.Equal("concept_context", page.Find("h3.panel-title").TextContent.Trim());
        var identity = page.Find(".pane-identity").TextContent;
        Assert.Contains("renders concept_list", identity);
        Assert.Contains("the engine's", identity);
        Assert.Empty(page.FindAll("textarea"));
    }

    [Fact]
    public void TheNavEntry_OpensThePane()
    {
        var page = RenderEditor();
        Navigate(page, "concept_bullets");

        Assert.Equal("renderer:concept_bullets", Session.SelectedKey);
        Assert.Equal("concept_bullets", page.Find("h3.panel-title").TextContent.Trim());
    }

    [Fact]
    public void ThePane_NamesTheSpelling_PerConceptListNode_WithALinkToTheNode()
    {
        var page = OpenRenderer("concept_context");

        var spelling = page.Find(".renderer-spelling[data-renderer-node='extract']");
        Assert.Equal("{{ node.extract.content | concept_context }}", spelling.QuerySelector("code")!.TextContent);
        Assert.Empty(page.FindAll(".renderer-spelling__none"));

        page.Find(".renderer-spelling[data-renderer-node='extract'] fluent-button[aria-label='Open node extract']").Click();
        Assert.Equal("node:extract", Session.SelectedKey);
        Assert.Equal("Extract concepts", SelectedNav(page));
    }

    [Fact]
    public void ThePane_FallsBackToTheGenericSpelling_WhenNoNodeProducesTheFormat()
    {
        var page = OpenRenderer("concept_bullets", V25WithoutAConceptListNode());

        var spelling = Assert.Single(page.FindAll(".renderer-spelling"));
        Assert.Equal("{{ node.<id>.content | concept_bullets }}", spelling.QuerySelector("code")!.TextContent);
        Assert.Contains("No node produces concept_list yet", page.Find(".renderer-spelling__none").TextContent);
    }

    [Fact]
    public void UsedBy_ListsTheTemplate_AndTheNode_AsLinks()
    {
        var page = OpenRenderer("concept_context", V25WithARenderedRead());

        var row = Assert.Single(page.FindAll(".renderer-user"));
        Assert.Equal("extract", row.GetAttribute("data-renderer-user-node"));
        Assert.Equal("Prompt 'assessment'", row.QuerySelector(".renderer-user__template")!.TextContent.Trim());
        Assert.Equal("Extract concepts", row.QuerySelector(".renderer-user__node")!.TextContent.Trim());
        Assert.Empty(page.FindAll(".renderer-users__empty"));

        page.Find(".renderer-user .renderer-user__template").Click();
        Assert.Equal("prompts/assessment.md", Session.SelectedKey);

        Session.SelectedKey = "renderer:concept_context";
        page.Render();
        page.Find(".renderer-user .renderer-user__node").Click();
        Assert.Equal("node:extract", Session.SelectedKey);
    }

    [Fact]
    public void UsedBy_IsPerPipe_AndEmptyOtherwise()
    {
        var page = OpenRenderer("concept_bullets", V25WithARenderedRead());

        Assert.Empty(page.FindAll(".renderer-user"));
        Assert.Contains("No template pipes a node through concept_bullets yet", page.Find(".renderer-users__empty").TextContent);

        var plain = OpenRenderer("concept_context");
        Assert.Empty(plain.FindAll(".renderer-user"));
        Assert.NotNull(plain.Find(".renderer-users__empty"));
    }

    [Fact]
    public void TheCopyButton_WritesTheSpelling_AndFlipsItsLabel()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetVoidResult();
        var page = OpenRenderer("concept_context");

        var copy = page.Find("fluent-button[aria-label='Copy the spelling for node extract']");
        Assert.Equal("Copy", copy.TextContent.Trim());
        copy.Click();

        Assert.Equal("{{ node.extract.content | concept_context }}", JSInterop.Invocations["navigator.clipboard.writeText"].Last().Arguments[0]);
        page.WaitForAssertion(() => Assert.Equal("Copied ✓", page.Find("fluent-button[aria-label='Copy the spelling for node extract']").TextContent.Trim()));
    }

    [Fact]
    public void ThePane_HasTheHelpExpander_AtItsFoot()
    {
        var page = OpenRenderer("concept_context");

        var help = page.Find("details.pane-help");
        Assert.Contains("How renderers work", help.TextContent);
        Assert.Contains("The engine's", help.TextContent);
    }

    [Fact]
    public void ThePane_SurvivesLeavingAndReturning()
    {
        var page = OpenRenderer("concept_bullets");
        Navigate(page, "Graph");
        Assert.Equal("Graph", SelectedNav(page));

        Navigate(page, "concept_bullets");
        Assert.Equal("concept_bullets", SelectedNav(page));
        Assert.Equal("concept_bullets", page.Find("h3.panel-title").TextContent.Trim());
    }

    /// <summary>#1126: an instruction may pipe a renderer too — listed by its subject, opening the macro's pane.</summary>
    [Fact]
    public void UsedBy_ListsAnInstruction_ThatPipesARenderer_OpeningTheMacro()
    {
        var page = RenderEditor(V25WithARenderedRead());
        Navigate(page, "closing");
        page.Find("fluent-text-area[aria-label='Instruction for macro closing']").Change("Note {{ node.extract.content | concept_context }} briefly.");

        Session.SelectedKey = "renderer:concept_context";
        page.Render();

        var rows = page.FindAll(".renderer-user");
        Assert.Equal(new[] { "Macro 'closing' instruction", "Prompt 'assessment'" }, rows.Select(r => r.QuerySelector(".renderer-user__template")!.TextContent.Trim()));
        Assert.All(rows, r => Assert.Equal("extract", r.GetAttribute("data-renderer-user-node")));

        rows[0].QuerySelector(".renderer-user__template")!.Click();
        Assert.Equal("macros/closing.md", Session.SelectedKey);
    }
}
