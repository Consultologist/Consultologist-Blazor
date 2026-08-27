using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v10 step (f) (#497): the intake form as a tree. An object field renders as
/// a group inside a row and an array field as rows inside a row, each level
/// with its own "+ Add entry" and ordinals; the explicit-initialisation rule
/// holds at every level; a sentence about a problem spells the path to it.
/// </summary>
public class ConsultsNestedIntakeTests : ClientRenderTestContext
{
    private static WorkflowPackageElementResponse Relative() =>
        new(WorkflowInputTypes.Object, Fields: new[]
        {
            new WorkflowPackageFieldResponse("relative", "Relative", true),
            new WorkflowPackageFieldResponse("conditions", "Conditions", false, WorkflowInputTypes.Array,
                Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Text)),
            new WorkflowPackageFieldResponse("contact", "Contact", false, WorkflowInputTypes.Object, Fields: new[]
            {
                new WorkflowPackageFieldResponse("phone", "Phone", true),
                new WorkflowPackageFieldResponse("preferred", "Preferred", false, WorkflowInputTypes.Enum, new[] { "phone", "email" })
            })
        });

    private static WorkflowPackageInputResponse[] WithFamilyHistory(bool required = false) => new[]
    {
        new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
        new WorkflowPackageInputResponse("family_history", "Family history", required, WorkflowInputTypes.Array, Items: Relative())
    };

    private static WorkflowPackageInputResponse[] WithGrid() => new[]
    {
        new WorkflowPackageInputResponse("consult_draft", "Consult draft", true),
        new WorkflowPackageInputResponse("grid", "Grid", false, WorkflowInputTypes.Array,
            Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Array, Items: new WorkflowPackageElementResponse(WorkflowInputTypes.Number)))
    };

    private IReadOnlyDictionary<string, ConsultInputValue>? sentInputs;

    private void CaptureSubmit() =>
        AIService.StartConsultGenerationJobAsync(
                Arg.Do<IReadOnlyDictionary<string, ConsultInputValue>>(value => sentInputs = value),
                Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>?>())
            .Returns(new ConsultGenerationJobStartResponse("0123456789abcdef0123456789abcdef", "Scheduled"));

    private static IElement Submit(IRenderedComponent<Consults> page) => page.FindAll("fluent-button").Last();

    // A nested array's add button sits inside its row, so it precedes the
    // outer one in document order; select each by where it lives.
    private static IElement InnerAdd(IRenderedComponent<Consults> page) => page.Find(".input-field__group--nested .input-field__add");

    private static string? Error(IRenderedComponent<Consults> page) =>
        page.FindAll(".input-field__error").SingleOrDefault()?.TextContent.Trim();

    [Fact]
    public void AnObjectInARow_IsAGroup_AndAnArrayInARow_HasItsOwnAddEntryAndOrdinals()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithFamilyHistory(), specVersion: 10);
        var page = Render<Consults>();

        // Nothing until the clinician adds a row — at either level.
        Assert.Single(page.FindAll(".input-field__add"));
        page.Find(".input-field__add").Click();

        // One outer row, whose object is drawn as members: Relative, the
        // Conditions rows (with their own add button) and the Contact group.
        Assert.Single(page.FindAll(".input-field__row"));
        Assert.Equal(2, page.FindAll(".input-field__group--nested").Count);
        Assert.Equal(new[] { "Relative", "Conditions", "Contact", "Phone", "Preferred" },
            page.FindAll("label.input-field__member > span").Select(span => span.TextContent.Trim().Replace(" (optional)", "").Replace("(optional)", "").Trim()));
        Assert.Equal(2, page.FindAll(".input-field__add").Count);

        // The inner add adds to the inner array, numbered from 1 on its own.
        InnerAdd(page).Click();
        InnerAdd(page).Click();
        Assert.Equal(3, page.FindAll(".input-field__row").Count);
        Assert.Equal(2, page.FindAll(".input-field__group--nested .input-field__row").Count);
        Assert.Equal(new[] { "1.", "1.", "2." }, page.FindAll(".input-field__row-number").Select(n => n.TextContent.Trim()));
    }

    [Fact]
    public void ExplicitInitialisation_HoldsAtEveryLevel_AndAProblemSpellsThePath()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithFamilyHistory(), specVersion: 10);
        CaptureSubmit();
        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        page.Find(".input-field__add").Click();

        // An empty row names itself; a filled one with its nested structure
        // untouched is complete — the untouched optional group is absent.
        Assert.Equal("Family history row 1 is empty; fill it in or remove it.", Error(page));
        page.FindAll("fluent-text-area")[1].Change("Mother");
        Assert.Null(Error(page));

        // Touching the nested group makes its required field due, and the
        // sentence walks down to it.
        page.Find("select.node-field__input").Change("email");
        Assert.Equal("Family history row 1: Contact: Phone is required.", Error(page));
        Assert.True(Submit(page).HasAttribute("disabled"));
        page.FindAll("fluent-text-area")[2].Change("555-0100");
        Assert.Null(Error(page));

        // A nested empty row holds the run by its own path.
        InnerAdd(page).Click();
        Assert.Equal("Family history row 1: Conditions row 1 is empty; fill it in or remove it.", Error(page));
        page.FindAll("fluent-text-area")[2].Change("Diabetes");
        Assert.Null(Error(page));

        Submit(page).Click();

        var family = sentInputs!["family_history"];
        var relative = Assert.Single(family.Elements!);
        Assert.Equal(new[] { "relative", "conditions", "contact" }, relative.Fields!.Select(entry => entry.Id));
        Assert.Equal("Diabetes", Assert.Single(relative.Fields[1].Value.Elements!).Canonical);
        Assert.Equal(new[] { "phone", "preferred" }, relative.Fields[2].Value.Fields!.Select(entry => entry.Id));
        Assert.Equal("email", relative.Fields[2].Value.Fields![1].Value.Canonical);
    }

    [Fact]
    public void AnUntouchedNestedStructure_IsOmitted_NotSentEmpty()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithFamilyHistory(), specVersion: 10);
        CaptureSubmit();
        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        page.Find(".input-field__add").Click();
        page.FindAll("fluent-text-area")[1].Change("Mother");

        Submit(page).Click();

        var relative = Assert.Single(sentInputs!["family_history"].Elements!);
        Assert.Equal(new[] { "relative" }, relative.Fields!.Select(entry => entry.Id));
    }

    [Fact]
    public void AnArrayOfArrays_NestsItsRows_AndTravelsAsNestedElements()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithGrid(), specVersion: 10);
        CaptureSubmit();
        var page = Render<Consults>();
        page.FindAll("fluent-text-area")[0].Change("Referral.");
        page.Find(".input-field__add").Click();
        Assert.Equal("Grid row 1 is empty; fill it in or remove it.", Error(page));

        InnerAdd(page).Click();
        InnerAdd(page).Click();
        Assert.Equal("Grid row 1 row 1 is empty; fill it in or remove it.", Error(page));
        page.FindAll("input[inputmode=decimal]")[0].Change("1.50");
        page.FindAll("input[inputmode=decimal]")[1].Change("x");
        Assert.Equal("Grid row 1 row 2 must be a plain decimal number, like 12 or 1.50.", Error(page));
        page.FindAll("input[inputmode=decimal]")[1].Change("2");

        Submit(page).Click();

        var row = Assert.Single(sentInputs!["grid"].Elements!);
        Assert.Equal(new[] { "1.50", "2" }, row.Elements!.Select(cell => cell.Canonical));
    }

    [Fact]
    public void ReattachingANestedRun_RestoresTheTree()
    {
        const string JobId = "0123456789abcdef0123456789abcdef";
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithFamilyHistory(), specVersion: 10);
        JobSession.Current = new ConsultJobMemento(
            JobId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["consult_draft"] = "Referral.", ["family_history"] = "Relative: Mother" },
            new[] { new ConsultJobBlock("s:hpi", "History") },
            new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = ConsultInputValue.OfText("Referral."),
                ["family_history"] = ConsultInputValue.OfArray(new[]
                {
                    ConsultInputValue.OfObject(new[]
                    {
                        new ConsultInputEntry("relative", ConsultInputValue.OfText("Mother")),
                        new ConsultInputEntry("conditions", ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("Diabetes"), ConsultInputValue.OfText("Asthma") })),
                        new ConsultInputEntry("contact", ConsultInputValue.OfObject(new[] { new ConsultInputEntry("phone", ConsultInputValue.OfText("555-0100")), new ConsultInputEntry("preferred", ConsultInputValue.OfText("email")) }))
                    })
                })
            });
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed", TotalBlockCount: 1, CompletedBlockCount: 1, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string> { ["s:hpi"] = "Section prose." },
            FailedBlocks: new Dictionary<string, string>(), Success: true, AssembledDocument: "The note."));

        var page = Render<Consults>();
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Edit inputs")).Click();

        Assert.Equal(3, page.FindAll(".input-field__row").Count);
        Assert.Equal(2, page.FindAll(".input-field__group--nested .input-field__row").Count);
        Assert.Equal(new[] { "Referral.", "Mother", "Diabetes", "Asthma", "555-0100" },
            page.FindAll("fluent-text-area").Select(area => area.GetAttribute("current-value") ?? area.GetAttribute("value")));
        Assert.Equal("email", page.Find("select.node-field__input").GetAttribute("value"));
    }

    [Fact]
    public async Task SwitchingPackages_CarriesTheTree_OnlyWhenTheShapeMatches()
    {
        WithPinnedPackage(blocks: new[] { Block("s:hpi", "History") }, inputs: WithFamilyHistory(), specVersion: 10);
        var page = Render<Consults>();
        page.Find(".input-field__add").Click();
        page.FindAll("fluent-text-area")[1].Change("Mother");

        // The same shape carries the row.
        WorkflowService.GetCurrentPackageAsync().Returns(new WorkflowPackageResponse(
            "general", "v2026.08.2", 10, new[] { Block("s:plan", "Plan") }, WithFamilyHistory(required: true), null));
        await Repin(page);
        Assert.Single(page.FindAll(".input-field__row"));

        // A different shape below the top level starts empty: the same id,
        // the same array of objects, but the contact group lost a field.
        var narrowed = Relative() with
        {
            Fields = Relative().Fields!.Take(2).Append(new WorkflowPackageFieldResponse("contact", "Contact", false, WorkflowInputTypes.Object,
                Fields: new[] { new WorkflowPackageFieldResponse("phone", "Phone", true) })).ToList()
        };
        WorkflowService.GetCurrentPackageAsync().Returns(new WorkflowPackageResponse(
            "general", "v2026.08.3", 10, new[] { Block("s:plan", "Plan") },
            new[] { WithFamilyHistory()[0], new WorkflowPackageInputResponse("family_history", "Family history", false, WorkflowInputTypes.Array, Items: narrowed) }, null));
        await Repin(page);
        Assert.Empty(page.FindAll(".input-field__row"));
    }

    private static Task Repin(IRenderedComponent<Consults> page)
    {
        var picker = page.FindComponent<Consultologist.Web.Shared.WorkflowEditor.WorkflowPackagePicker>();
        return page.InvokeAsync(() => picker.Instance.OnPinned.InvokeAsync());
    }
}
