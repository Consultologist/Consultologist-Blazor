using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C1 (#1007, design § 13): the node pane's format picker — catalogue entries
/// plus the package's own formats — with the picked entry's parameters rendered as
/// fields writing <c>with</c>, and <c>onInvalid</c> only where the engine validates
/// the reply. Replaces the kind selector and the schema picker at 25; nothing below
/// 25 changes (the ≤24 suites hold that). Inert on the live editor until A5 moves
/// the version constants: the fixture is a 25 package the store cannot load yet.
/// </summary>
public class TemplatesV25FormatPickerTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null, bool withCatalogue = true)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V25());
        if (withCatalogue)
        {
            WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        }

        return Render<Templates>();
    }

    private IRenderedComponent<Templates> ShowNode(string nodeId, bool withCatalogue = true)
    {
        var page = RenderEditor(withCatalogue: withCatalogue);
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
        return page;
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private static JsonElement Node(WorkflowPackagePublishRequest request, string id) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == id);

    /// <summary>The composed manifest against the REAL validator and the real formats catalogue, through the A4 seam.</summary>
    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog,
            EditorFormatsCatalog.AcceptedWith25);
    }

    [Fact]
    public void TheFixture_IsValidAt25_ThroughTheSeam()
    {
        // The proof the round-trips rest on: the 25 fixture validates before any edit.
        var page = RenderEditor();
        CapturePublish();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:assessment";
        page.Render();
        page.Find("input[aria-label='Node label']").Change("Assessment (edited)");
        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void At25_TheNodePane_ShowsTheFormatPicker_NotTheOutputSelect()
    {
        var page = ShowNode("assessment");

        var picker = page.Find("fluent-select.node-format");
        Assert.Equal("prose", picker.GetAttribute("current-value"));
        var offered = picker.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        // The catalogue's five, then the package's own.
        Assert.Contains("classification", offered);
        Assert.Contains("terms-subset", offered);
        Assert.Contains("medication_list", offered);
        Assert.Empty(page.FindAll("[aria-label='Node output contract']"));

        // The identity strip reads the format and its producer.
        Assert.Contains("prose · attested", page.Find(".pane-identity").TextContent);
        // The picked format's description reads under the picker.
        Assert.Contains("Consult prose", page.Find(".node-format__description").TextContent);
    }

    [Fact]
    public void AClassificationNode_RendersItsValuesChips_AndOnInvalid()
    {
        var page = ShowNode("scope");

        var chips = page.FindAll("[data-values-for='scope'] [data-node-value]").Select(c => c.GetAttribute("data-node-value")).ToList();
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, chips);

        var onInvalid = page.Find("fluent-select.node-on-invalid");
        Assert.Equal("fail", onInvalid.GetAttribute("current-value"));
        var policies = onInvalid.QuerySelectorAll("fluent-option").Select(o => (o.GetAttribute("value"), o.TextContent.Trim())).ToList();
        Assert.Contains(("fail", "fail"), policies);
        Assert.Contains(("flag", "flag"), policies);
        Assert.Contains(policies, p => p.Item1 == "retry" && p.Item2.Contains("reserved"));

        // No ≤24 leftovers: no kind words, no check fields.
        Assert.Empty(page.FindAll(".node-check-of"));
        Assert.DoesNotContain("classifier", page.Find(".pane-identity").TextContent);
    }

    [Fact]
    public void ATermsSubsetNode_RendersOfInAndFailWith_AndNoPrompt()
    {
        var page = ShowNode("coverage");

        var of = page.Find("fluent-select.node-with[data-with='of']");
        Assert.Equal("node.extract", of.GetAttribute("current-value"));
        // Operands narrowed to the parameter's format: the concept-list node only.
        var operands = of.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).Where(v => v!.Length > 0).ToList();
        Assert.Equal(new[] { "node.extract" }, operands);
        Assert.Contains("a concept-list node", page.Find("fluent-select.node-with[data-with='of']").ParentElement!.TextContent);

        Assert.Equal("The note names problems the source does not.", page.Find("input.node-with[data-with='failWith']").GetAttribute("value"));
        Assert.Empty(page.FindAll("[aria-label='Node prompt']"));
        Assert.Empty(page.FindAll("[aria-label='Node forEach collection']"));
        Assert.Empty(page.FindAll("[aria-label='Node fail-if-empty message']"));
        Assert.Contains("terms-subset · computed", page.Find(".pane-identity").TextContent);
    }

    [Fact]
    public void AProseNode_RendersNoParameters_NoOnInvalid_ButFailIfEmpty()
    {
        var page = ShowNode("assessment");

        Assert.Empty(page.FindAll(".node-with"));
        Assert.Empty(page.FindAll(".node-on-invalid"));
        Assert.Equal("Nothing to assess.", page.Find("[aria-label='Node fail-if-empty message']").GetAttribute("value"));
        Assert.NotEmpty(page.FindAll("[aria-label='Node prompt']"));
    }

    [Fact]
    public void ACustomFormatNode_RendersOnInvalid_FromThePackagesOwnFormat()
    {
        var page = ShowNode("meds");

        Assert.Equal("medication_list", page.Find("fluent-select.node-format").GetAttribute("current-value"));
        Assert.Equal("flag", page.Find("fluent-select.node-on-invalid").GetAttribute("current-value"));
        Assert.Contains("medication_list · custom", page.Find(".pane-identity").TextContent);
    }

    [Fact]
    public void RepickingTheFormat_ResetsWithAndOnInvalid_AndComposes()
    {
        var page = ShowNode("scope");
        CapturePublish();

        page.Find("fluent-select.node-format").Change("prose");

        Assert.Empty(page.FindAll("[data-values-for='scope']"));
        Assert.Empty(page.FindAll(".node-on-invalid"));
        Assert.Contains("prose · attested", page.Find(".pane-identity").TextContent);

        // A prose node must feed the deliverable, so the assessment prompt reads it
        // — the author's second step after the re-pick.
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "prompts/assessment.md";
        page.Render();
        page.Find("fluent-text-area").Change("Assess.\n{{ input.notes }}\n{{ node.scope.text }}\n{{ for c in node.extract.value.concepts }}- {{ c }}\n{{ end }}\n{{ for m in node.meds.value.medications }}- {{ m.name }}\n{{ end }}");

        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        var scope = Node(sent!, "scope");
        Assert.Equal("prose", scope.GetProperty("format").GetString());
        Assert.False(scope.TryGetProperty("with", out _));
        Assert.False(scope.TryGetProperty("onInvalid", out _));
        Assert.False(scope.TryGetProperty("kind", out _));
        Assert.Equal("scope", scope.GetProperty("prompt").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void EditingAParameter_WritesWith_AndValidates()
    {
        var page = ShowNode("coverage");
        CapturePublish();

        page.Find("input.node-with[data-with='failWith']").Change("Uncovered terms remain.");
        page.Find("fluent-select.node-on-invalid").Change("flag");
        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        var coverage = Node(sent!, "coverage");
        var with = coverage.GetProperty("with");
        Assert.Equal("Uncovered terms remain.", with.GetProperty("failWith").GetString());
        Assert.Equal("node.extract", with.GetProperty("of").GetString());
        Assert.Equal("flag", coverage.GetProperty("onInvalid").GetString());
        Assert.False(coverage.TryGetProperty("prompt", out _));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void AddingAndRemovingAValue_WritesWithValues()
    {
        var page = ShowNode("scope");
        CapturePublish();

        page.Find("[aria-label='Add a value to node scope']").Change("unclear");
        Assert.Equal(new[] { "in_scope", "out_of_scope", "unclear" },
            page.FindAll("[data-values-for='scope'] [data-node-value]").Select(c => c.GetAttribute("data-node-value")));

        page.Find("[aria-label='Remove value out_of_scope']").Click();
        Publish(page);

        Assert.Empty(Refusals(page));
        var values = Node(sent!, "scope").GetProperty("with").GetProperty("values").EnumerateArray().Select(v => v.GetString()).ToArray();
        Assert.Equal(new[] { "in_scope", "unclear" }, values);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void AValue_MustBeSnakeCase_AndNotDeclaredTwice()
    {
        var page = ShowNode("scope");

        page.Find("[aria-label='Add a value to node scope']").Change("In Scope");
        Assert.Contains("must be snake_case", page.Find(".editor-warning").TextContent);

        page.Find("[aria-label='Add a value to node scope']").Change("in_scope");
        Assert.Contains("already declares 'in_scope'", page.Find(".editor-warning").TextContent);
    }

    [Fact]
    public void AnUnchangedEdit_LeavesNoPendingChange()
    {
        // Writing the same with back resolves to "no change" host-side (the self-removing edit rule).
        var page = ShowNode("coverage");

        page.Find("input.node-with[data-with='failWith']").Change("The note names problems the source does not.");

        Assert.DoesNotContain("●", page.Find("button.editor-nav__item--selected").TextContent);
    }

    [Fact]
    public void PlusNode_At25_OffersFormatsNotKinds()
    {
        var page = RenderEditor();
        Navigate(page, "+ Node");

        var picker = page.Find("fluent-select.new-node-format");
        var offered = picker.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Equal("", offered[0]);
        Assert.Equal("aggregator", offered[1]);
        Assert.Contains("prose", offered);
        Assert.Contains("medication_list", offered);
        Assert.DoesNotContain("check", offered);
        Assert.DoesNotContain("classifier", offered);
        Assert.Empty(page.FindAll(".new-node-check-op"));
    }

    [Fact]
    public void PlusNode_At25_RefusesWithoutAFormat()
    {
        var page = RenderEditor();
        Navigate(page, "+ Node");

        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("plan");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Plan");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create")).Click();

        Assert.Contains(page.FindAll(".fluent-messagebar-message"), bar => bar.TextContent.Contains("Choose a format — or aggregator."));
    }

    [Fact]
    public void PlusNode_At25_APromptedFormat_MintsThePrompt_AndComposesFormatNotKind()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "+ Node");

        page.Find("fluent-select.new-node-format").Change("prose");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("plan");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Plan");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create")).Click();

        // Lands on the new prompt's text, as a prompted node always has; the node's
        // pane shows the format picked.
        page.Find("fluent-text-area").Change("Plan from {{ input.notes }}.");
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:plan";
        page.Render();
        Assert.Equal("prose", page.Find("fluent-select.node-format").GetAttribute("current-value"));

        // The new node feeds the deliverable through its aggregator (the author's next step).
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:all";
        page.Render();
        page.Find("fluent-select[aria-label='Add aggregate source']").Change("node.plan");

        Publish(page);

        Assert.Empty(Refusals(page));
        var plan = Node(sent!, "plan");
        Assert.Equal("prose", plan.GetProperty("format").GetString());
        Assert.Equal("plan", plan.GetProperty("prompt").GetString());
        Assert.False(plan.TryGetProperty("kind", out _));
        Assert.False(plan.TryGetProperty("output", out _));
        Assert.Contains(
            JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("prompts").EnumerateArray(),
            p => p.GetProperty("id").GetString() == "plan");
        Assert.Equal("Plan from {{ input.notes }}.", sent.Files["prompts/plan.md"]);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void PlusNode_At25_AComputedFormat_MintsNoPrompt_AndTakesItsParameters()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "+ Node");

        page.Find("fluent-select.new-node-format").Change("terms-subset");
        Assert.Empty(page.FindAll(".new-item-fields fluent-select[aria-label='New node forEach']"));
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("coverage_two");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Second coverage");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create")).Click();

        // The pane renders the computed format's parameters; fill them.
        page.Find("fluent-select.node-with[data-with='of']").Change("node.extract");
        page.Find("fluent-select.node-with[data-with='in']").Change("node.extract");
        page.Find("input.node-with[data-with='failWith']").Change("Uncovered.");
        page.Find("fluent-select.node-on-invalid").Change("fail");
        Publish(page);

        Assert.Empty(Refusals(page));
        var node = Node(sent!, "coverage_two");
        Assert.Equal("terms-subset", node.GetProperty("format").GetString());
        Assert.False(node.TryGetProperty("prompt", out _));
        Assert.False(node.TryGetProperty("bindings", out _));
        Assert.Equal("node.extract", node.GetProperty("with").GetProperty("in").GetString());
        Assert.DoesNotContain(
            JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("prompts").EnumerateArray(),
            p => p.GetProperty("id").GetString() == "coverage_two");
        Assert.False(sent.Files.ContainsKey("prompts/coverage_two.md"));
        // The node itself is whole: the registry's only word is that no deliverable
        // names it yet — the Deliverables pane's check picker at 25 is C3's (#1009).
        var validated = Validated();
        Assert.Equal(
            new[] { "Check 'coverage_two' is not named by any deliverable; a check gates a deliverable, or it is dead weight." },
            validated.Errors);
    }

    [Fact]
    public void PlusNode_At25_AnAggregator_StillComposesAsBefore()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "+ Node");

        page.Find("fluent-select.new-node-format").Change("aggregator");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("bundle");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Bundle");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Create")).Click();

        // Lands on the aggregator's card: no format picker, the kind word as before.
        Assert.Contains("aggregator", page.Find(".pane-identity").TextContent);
        Assert.Empty(page.FindAll(".node-format"));
        Assert.NotEmpty(page.FindAll("fluent-select[aria-label='Add aggregate source']"));
    }

    [Fact]
    public void WithoutTheCatalogue_ThePickerKeepsTheNodesPick_AndOffersThePackagesOwn()
    {
        // The endpoint is unavailable: the picker offers the package's own formats and
        // the node's catalogue pick stays listed by id, so nothing is silently lost.
        var page = ShowNode("assessment", withCatalogue: false);

        var picker = page.Find("fluent-select.node-format");
        Assert.Equal("prose", picker.GetAttribute("current-value"));
        var offered = picker.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Contains("prose", offered);
        Assert.Contains("medication_list", offered);
        Assert.DoesNotContain("classification", offered);
    }

    [Fact]
    public void Below25_NothingChanges()
    {
        // A 24 package shows no picker, no with fields, no on-invalid; the output select stays.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V12());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:draft-section";
        page.Render();

        Assert.Empty(page.FindAll(".node-format"));
        Assert.Empty(page.FindAll(".node-with"));
        Assert.Empty(page.FindAll(".node-on-invalid"));
        Assert.NotEmpty(page.FindAll("[aria-label='Node output contract']"));
        WorkflowService.DidNotReceive().GetFormatsAsync();
    }

    [Fact]
    public void TheDraft_CarriesTheFormatWithAndOnInvalid_AcrossAReload()
    {
        // The draft the editor saves after an edit, fed back as the stored draft of
        // a fresh render: the pending with and onInvalid come back.
        var page = ShowNode("coverage");
        page.Find("input.node-with[data-with='failWith']").Change("Uncovered terms remain.");
        page.Find("fluent-select.node-on-invalid").Change("flag");
        page.Find("fluent-select.node-format").Change("terms-subset");

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = Render<Templates>();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:coverage";
        again.Render();

        Assert.Equal("Uncovered terms remain.", again.Find("input.node-with[data-with='failWith']").GetAttribute("value"));
        Assert.Equal("flag", again.Find("fluent-select.node-on-invalid").GetAttribute("current-value"));
        Assert.Equal("terms-subset", again.Find("fluent-select.node-format").GetAttribute("current-value"));
    }
}
