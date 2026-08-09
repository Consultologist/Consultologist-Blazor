using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// Pins the editor's existing behavior before the v7 authoring work (#218)
/// changes it. Templates.razor is the largest component in the app and had no
/// render coverage; these cover the surfaces that must survive untouched.
/// </summary>
public class TemplatesEditorTests : ClientRenderTestContext
{
    /// <summary>
    /// The editor now opens on Graph, but these click through to it anyway:
    /// a test should assert from the pane it chose, not the one it inherited.
    /// </summary>
    private IRenderedComponent<Templates> RenderEditor(bool v7 = false)
    {
        WorkflowService.GetCurrentPackageContentAsync()
            .Returns(v7 ? EditorFixtures.V7() : EditorFixtures.V6());

        var page = Render<Templates>();

        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("\u25CF", string.Empty).Trim() == "Graph")
            .Click();

        return page;
    }

    [Fact]
    public void TheEditorOpensOnGraph()
    {
        // Graph is the overview. The old default was the first standard, which
        // is only wherever the collection happened to start — and a refresh
        // while typing into "+ Data value" landed there too, which is how this
        // surfaced.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6());

        var page = Render<Templates>();

        var selected = page.FindAll("button.editor-nav__item--selected")
            .Select(button => button.TextContent.Replace("●", string.Empty).Trim())
            .ToList();

        Assert.Equal(new[] { "Graph" }, selected);
        // The first standard's pane would bring a text area with it.
        Assert.Empty(page.FindAll("fluent-text-area"));
    }

    [Fact]
    public void LeavingTemplatesAndReturning_ReopensTheSamePane()
    {
        // Reported from real use: pick a pane, visit another page, come back,
        // and you were on Graph again. The component is rebuilt on navigation,
        // so the pane has to live in the tab session — the shape ConsultJobSession
        // (#207) already uses for a run you navigated away from.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6WithValue());

        var first = Render<Templates>();
        Navigate(first, "specialty");
        Assert.Contains("data/specialty.txt", first.Markup);

        // A second render is the same tab arriving back at /templates: new
        // component, same session.
        var second = Render<Templates>();

        Assert.Contains("data/specialty.txt", second.Markup);
    }

    [Fact]
    public void ARememberedPaneTheNewPackageLacks_FallsBackToGraph()
    {
        // Remembering the pane across page switches means it can now outlive a
        // package switch too, so the staleness guard started mattering where it
        // never had to before: before this, a rebuilt component always began
        // with no selection at all.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6WithValue());

        var first = Render<Templates>();
        Navigate(first, "specialty");
        Assert.Contains("data/specialty.txt", first.Markup);

        // v6 carries no values, so the remembered key names nothing.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6());
        var second = Render<Templates>();

        Assert.DoesNotContain("data/specialty.txt", second.Markup);
        Assert.Equal(
            new[] { "Graph" },
            second.FindAll("button.editor-nav__item--selected")
                .Select(button => button.TextContent.Replace("●", string.Empty).Trim())
                .ToList());
    }

    private static string NavLabel(IElement button) =>
        button.TextContent.Replace("●", string.Empty).Trim();

    private static string? SelectedPane(IRenderedComponent<Templates> page) =>
        page.FindAll("button.editor-nav__item--selected").Select(NavLabel).FirstOrDefault();

    [Fact]
    public void EveryNavPane_SurvivesLeavingAndReturning()
    {
        // Derived from the nav rather than restated: whatever the editor offers
        // as a pane has to come back. The guard used to keep two of eleven
        // kinds — a published file and a published node — so the declaration
        // panes, the "+" forms and anything pending all dropped to Graph.
        //
        // Listing the kinds in the test would make a second list to keep in
        // step with PaneStillExists, which is the failure #326 is about. The
        // rendered nav is the only honest source.
        // Pending panes are out of reach here rather than out of scope: a
        // second render restores its pending state from the draft, and bUnit's
        // localStorage returns nothing, so a pending value would legitimately
        // read as gone. PaneStillExists covers them through EffectiveScalars,
        // EffectiveNodes and addedItems; the browser is what proves it.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());

        var page = Render<Templates>();
        var labels = page.FindAll("button.editor-nav__item").Select(NavLabel).Distinct().ToList();
        Assert.True(labels.Count >= 8, $"only {labels.Count} panes in the nav: {string.Join(", ", labels)}");

        var checkedPanes = 0;

        foreach (var label in labels)
        {
            Navigate(page, label);

            if (SelectedPane(page) != label)
            {
                // Not every nav button selects a pane of its own.
                continue;
            }

            // A second render is the same tab arriving back at /templates.
            page = Render<Templates>();
            Assert.Equal(label, SelectedPane(page));
            checkedPanes++;
        }

        Assert.True(checkedPanes >= 8, $"only {checkedPanes} panes actually round-tripped");
    }

    [Fact]
    public void NodeCards_RenderOnePerManifestNode()
    {
        var page = RenderEditor();

        var labels = page.FindAll(".template-section .node-summary__title, .template-section h3")
            .Select(element => element.TextContent)
            .ToList();

        Assert.Contains(labels, label => label.Contains("Drafting section"));
        Assert.Contains(labels, label => label.Contains("Assembling note"));
    }

    [Fact]
    public void DeliverableSelector_ListsAggregatorsAndMarksTheCurrentOne()
    {
        var page = RenderEditor();

        var select = page.Find(".result-selector select");
        var options = select.QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToArray();

        // v6: aggregators are the candidates; the fan node is not one.
        Assert.Equal(new[] { "node:assemble-note" }, options);
        Assert.Equal("node:assemble-note", select.GetAttribute("value"));
    }

    [Fact]
    public void BindingRows_OfferTheFrozenInputAndSiblingNodes()
    {
        var page = RenderEditor();

        // The fan node binds consult_draft; its source select must offer the
        // frozen input plus the item fields a forEach node can read.
        var options = page.FindAll(".binding-row__select option")
            .Select(option => option.GetAttribute("value"))
            .ToList();

        Assert.Contains("input:consult_draft", options);
        Assert.Contains("item:name", options);
    }

    [Fact]
    public void FreshlyLoaded_HasNoPendingEdits()
    {
        var page = RenderEditor();

        // The editor diffs against the manifest rather than counting
        // interactions, so re-selecting the current deliverable is not a change.
        page.Find(".result-selector select").Change("node:assemble-note");

        Assert.Empty(page.FindAll(".binding-row__pending"));
        Assert.All(
            page.FindAll("fluent-button").Where(b => b.TextContent.Contains("Publish") || b.TextContent.Contains("Discard")),
            button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void V7Package_LoadsWithoutError()
    {
        // Before #218 this renders, but the deliverable selector is empty and
        // the candidates are wrong — the tests below the repair commits assert
        // the corrected behavior.
        var page = RenderEditor(v7: true);

        Assert.Contains("Assembling note", page.Markup);
    }

    // #309: single-value data entries. A value is one file with no items, so
    // it sits flat in the Data group rather than as an empty folder.

    private IRenderedComponent<Templates> RenderWithValue()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6WithValue());
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    [Fact]
    public void PublishedValue_AppearsInTheNavAndOpensItsText()
    {
        var page = RenderWithValue();

        Navigate(page, "specialty");

        Assert.Contains("data/specialty.txt", page.Markup);
        Assert.Equal("oncology", page.Find("fluent-text-area").GetAttribute("current-value"));
        // The hazard is named rather than guarded — the value goes into
        // prompts exactly as typed.
        Assert.Contains("exactly as", page.Markup);
    }

    [Fact]
    public void AValueBoundByANode_IsNotFlaggedUnused()
    {
        // The fixture's fan node binds data:specialty, and the collection
        // beside it is forEached — different edge kinds, same question.
        var page = RenderWithValue();

        Assert.DoesNotContain("not bound by any workflow node yet", page.Markup);
    }

    [Fact]
    public void AddedValue_AppearsPendingAndUnbound()
    {
        var page = RenderWithValue();

        Navigate(page, "+ Data value");
        page.Find(".new-item-fields fluent-text-field").Change("note_type");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create value")).Click();

        Assert.Contains("note_type", page.Markup);
        Assert.Contains("not bound by any workflow node yet", page.Markup);
        Assert.Contains("+1 value", page.Markup);
    }

    [Fact]
    public void AddedValue_CanBeRemovedWhilePending()
    {
        var page = RenderWithValue();

        Navigate(page, "+ Data value");
        page.Find(".new-item-fields fluent-text-field").Change("note_type");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create value")).Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Remove").Click();

        Assert.DoesNotContain("+1 value", page.Markup);
        // The published one is untouched: removal is pending-only, which is
        // the same deal collections get.
        Assert.Contains("specialty", page.Markup);
    }

    [Fact]
    public void AValueCannotTakeTheNameOfAnExistingDataEntry()
    {
        var page = RenderWithValue();

        Navigate(page, "+ Data value");
        page.Find(".new-item-fields fluent-text-field").Change("standards");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create value")).Click();

        // "standards" is a collection in this fixture; one data map cannot
        // hold both shapes under one key.
        Assert.Contains("already exists", page.Markup);
    }

    [Fact]
    public void APendingValue_CanBeBoundBeforeItIsPublished()
    {
        // Found in real use: authoring a value took TWO publishes, because the
        // binding dropdown offered published values only. A pending folder can
        // be forEached the moment it exists (CollectionIds is the effective
        // list), and a pending value has to be pickable the same way.
        var page = RenderWithValue();

        Navigate(page, "+ Data value");
        page.Find(".new-item-fields fluent-text-field").Change("urgency");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create value")).Click();

        Navigate(page, "Graph");

        var options = page.FindAll("select.binding-row__select option")
            .Select(option => option.GetAttribute("value"))
            .ToList();

        Assert.Contains("data:urgency", options);
        // The published one is still there — this widens the list, not swaps it.
        Assert.Contains("data:specialty", options);
    }

    [Fact]
    public void AFolderCannotTakeAPendingValuesName()
    {
        // The mirror of the check the value form already makes. Both sides
        // have to see pending state, or the collision only shows up at
        // publish as two data entries fighting over one key.
        var page = RenderWithValue();

        Navigate(page, "+ Data value");
        page.Find(".new-item-fields fluent-text-field").Change("urgency");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create value")).Click();

        Navigate(page, "+ Data folder");
        page.Find(".new-item-fields fluent-text-field").Change("urgency");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create folder")).Click();

        Assert.Contains("already exists", page.Markup);
    }

    // #323: an empty value published to a real fork, and nothing objected —
    // the validator accepts any string for a scalar, "" included, so this
    // client check is the only one there is.

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

    private static void CreateValue(IRenderedComponent<Templates> page, string id)
    {
        Navigate(page, "+ Data value");
        page.Find(".new-item-fields fluent-text-field").Change(id);
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create value")).Click();
    }

    [Fact]
    public void AnEmptyAddedValue_BlocksPublishAndNamesItself()
    {
        var page = RenderWithValue();

        CreateValue(page, "urgency");
        Publish(page);

        Assert.Contains("Publish rejected", page.Markup);
        Assert.Contains("Data value 'urgency' has no text yet", page.Markup);
    }

    [Fact]
    public void AValueWithText_DoesNotBlockPublish()
    {
        var page = RenderWithValue();

        CreateValue(page, "urgency");
        page.Find("fluent-text-area").Change("routine");
        Publish(page);

        Assert.DoesNotContain("has no text yet", page.Markup);
    }

    [Fact]
    public void WhitespaceCountsAsEmpty()
    {
        // It counts in the rendered prompt, so it counts here.
        var page = RenderWithValue();

        CreateValue(page, "urgency");
        page.Find("fluent-text-area").Change("   ");
        Publish(page);

        Assert.Contains("Data value 'urgency' has no text yet", page.Markup);
    }

    [Fact]
    public void APublishedValueEmptiedByTheAuthor_BlocksPublish()
    {
        var page = RenderWithValue();

        Navigate(page, "specialty");
        page.Find("fluent-text-area").Change(string.Empty);
        Publish(page);

        Assert.Contains("Data value 'specialty' has no text yet", page.Markup);
    }

    [Fact]
    public void AnInheritedEmptyValue_DoesNotBlockUnrelatedWork()
    {
        // The scoping decision: being unable to publish a prompt fix because
        // the package you forked carries a bad value would be a worse trap
        // than the one this closes. Only what this author did is checked.
        var package = EditorFixtures.V6WithValue();
        var files = new Dictionary<string, string>(package.Files, StringComparer.Ordinal)
        {
            ["data/specialty.txt"] = string.Empty
        };
        WorkflowService.GetCurrentPackageContentAsync().Returns(package with { Files = files });

        var page = Render<Templates>();
        Navigate(page, "History");
        page.Find("fluent-text-area").Change("Document the presenting illness, chronologically.");
        Publish(page);

        Assert.DoesNotContain("has no text yet", page.Markup);
    }

    [Fact]
    public void PublishWarnings_AreShownAlongsideSuccess()
    {
        // #310's warning arrives *with* a successful publish, and the version
        // it describes is already immutable — so this render is the only
        // moment it can be read. Without it the warning dies in the browser.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6WithValue());
        WorkflowService.PublishPackageAsync(Arg.Any<Consultologist.Web.Services.Workflow.WorkflowPackagePublishRequest>())
            .Returns(new Consultologist.Web.Services.Workflow.WorkflowPublishOutcome(
                new Consultologist.Web.Services.Workflow.WorkflowPackagePublishResponse(
                    "acct-1234567890ab", "v2026.07.2", "acct-1234567890ab@v2026.07.2",
                    new List<string> { "Value 'specialty' changed but no data collection did." }),
                Array.Empty<string>()));

        var page = Render<Templates>();
        Navigate(page, "History");
        page.Find("fluent-text-area").Change("Document the presenting illness, chronologically.");
        Publish(page);

        Assert.Contains("Published, with something worth checking", page.Markup);
        Assert.Contains("no data collection did", page.Markup);
        // Advisory, not a rejection — the success banner stands too.
        Assert.Contains("Published acct-1234567890ab@v2026.07.2", page.Markup);
    }

    // #321: deleting a *published* value. Two halves — the file leaves through
    // removedFiles, the data-map entry leaves through ComposeManifest — and a
    // guard, because a value a node still binds cannot be deleted into a
    // package the server will accept.

    private IRenderedComponent<Templates> RenderWithUnusedValue()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6WithUnusedValue());
        return Render<Templates>();
    }

    private static IElement RemoveButton(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Remove");

    [Fact]
    public void AnUnboundPublishedValue_RemovesAndRestores()
    {
        var page = RenderWithUnusedValue();

        Navigate(page, "urgency");
        RemoveButton(page).Click();

        // Struck through with a way back, the same deal a removed item file
        // gets — the deletion is not final until it publishes.
        Assert.Contains("editor-nav__strike", page.Markup);
        Assert.Contains("(restore)", page.Markup);
        Assert.Contains("−1", page.Markup);

        page.FindAll("button.editor-nav__restore").First().Click();

        Assert.DoesNotContain("(restore)", page.Markup);
        Assert.DoesNotContain("−1", page.Markup);
    }

    [Fact]
    public void ABoundValue_CannotBeRemovedAndTheReasonNamesTheNode()
    {
        var page = RenderWithUnusedValue();

        Navigate(page, "specialty");

        Assert.True(RemoveButton(page).HasAttribute("disabled"));
        Assert.Contains("draft-section", page.Markup);
        Assert.Contains("rebind or remove", page.Markup);
    }

    [Fact]
    public void APendingRebindOntoAValue_MakesItUndeletable()
    {
        // The false-allow the published-only predicate would have given:
        // binding urgency here and deleting it in the same session publishes a
        // package the server rejects with "unknown data entry".
        var page = RenderWithUnusedValue();

        Navigate(page, "Graph");
        // binding-row__select is shared with the deliverable, prompt and
        // forEach selects; the aria-label is what distinguishes a source row.
        page.FindAll("select.binding-row__select")
            .First(s => s.GetAttribute("aria-label") == "Source for section_name")
            .Change("data:urgency");

        Navigate(page, "urgency");

        Assert.True(RemoveButton(page).HasAttribute("disabled"));
        // One predicate: the shipped hint answers the same question the guard
        // does, so a pending binding clears it without publishing.
        Assert.DoesNotContain("not bound by any workflow node yet", page.Markup);
    }

    [Fact]
    public void PendingRemovalOfTheOnlyBindingNode_MakesTheValueDeletable()
    {
        // The false-refuse, the same mistake mirrored: nothing binds specialty
        // in the version about to publish, so nothing should stop its deletion.
        var page = RenderWithUnusedValue();

        Navigate(page, "Graph");
        // draft-section is the only removable node — the deliverable's root
        // never offers it.
        page.FindAll("button").First(b => b.TextContent.Contains("Remove node")).Click();

        Navigate(page, "specialty");

        Assert.False(RemoveButton(page).HasAttribute("disabled"));
    }

    [Fact]
    public void ADeletedValue_IsNoLongerOfferedAsABindingSource()
    {
        // The guard's mirror image: binding to a value just deleted builds the
        // same broken package from the other direction.
        var page = RenderWithUnusedValue();

        Navigate(page, "urgency");
        RemoveButton(page).Click();
        Navigate(page, "Graph");

        var options = page.FindAll("select.binding-row__select option")
            .Select(option => option.GetAttribute("value"))
            .ToList();

        Assert.DoesNotContain("data:urgency", options);
        Assert.Contains("data:specialty", options);
    }

    [Fact]
    public void AnEditAfterPublishing_DropsTheStaleSuccessBanner()
    {
        // #326: 22 handlers each cleared this by hand, and the 23rd could
        // forget. The invariant now lives in PendingChangedAsync, which every
        // edit already calls — a banner describing a published version must
        // not stand over work that version does not contain.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6WithValue());
        WorkflowService.PublishPackageAsync(Arg.Any<Consultologist.Web.Services.Workflow.WorkflowPackagePublishRequest>())
            .Returns(new Consultologist.Web.Services.Workflow.WorkflowPublishOutcome(
                new Consultologist.Web.Services.Workflow.WorkflowPackagePublishResponse(
                    "acct-1234567890ab", "v2026.07.2", "acct-1234567890ab@v2026.07.2", null),
                Array.Empty<string>()));

        var page = Render<Templates>();
        Navigate(page, "History");
        page.Find("fluent-text-area").Change("Document the presenting illness, chronologically.");
        Publish(page);

        Assert.Contains("Published acct-1234567890ab@v2026.07.2", page.Markup);

        page.Find("fluent-text-area").Change("A different revision entirely.");

        Assert.DoesNotContain("Published acct-1234567890ab@v2026.07.2", page.Markup);
    }

    [Fact]
    public void AValueIdMayContainAnUnderscore()
    {
        // note_type is published and running. CollectionIdPattern forbids '_'
        // because a directory segment does; a value is a file, so refusing it
        // here would reject an id that already exists in production.
        var page = RenderWithValue();

        Navigate(page, "+ Data value");
        page.Find(".new-item-fields fluent-text-field").Change("note_type");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create value")).Click();

        Assert.DoesNotContain("must be lowercase letters", page.Markup);
        Assert.Contains("+1 value", page.Markup);
    }
}
