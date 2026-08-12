using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using NSubstitute;

// Both assemblies define package records; the client's are what the editor
// sends, the server's are what validates them.
using ClientWorkflow = Consultologist.Web.Services.Workflow;
using ServerWorkflow = Consultologist.Api.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// The contract that actually broke for v7 (#218): what the editor composes
/// must satisfy the validator the registry runs at publish. This captures the
/// real publish payload and feeds it to the server's own validator, rather
/// than asserting on JSON shape and hoping the two agree.
/// </summary>
public class EditorPublishRoundTripTests : ClientRenderTestContext
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private async Task<ServerWorkflow.WorkflowPackageValidator.ValidationResult> PublishAndValidateAsync(
        Func<IRenderedComponent<Templates>, Task> edit,
        bool v7 = true)
    {
        var (result, _) = await PublishAndCaptureAsync(edit, v7 ? EditorFixtures.V7() : EditorFixtures.V6());
        return result;
    }

    /// <summary>
    /// The same round trip, also handing back what was sent — for assertions
    /// the validator cannot make, like whether a data path kept its trailing
    /// slash.
    /// </summary>
    private async Task<(ServerWorkflow.WorkflowPackageValidator.ValidationResult Result,
                        ClientWorkflow.WorkflowPackagePublishRequest Sent)> PublishAndCaptureAsync(
        Func<IRenderedComponent<Templates>, Task> edit,
        ClientWorkflow.WorkflowPackageContentResponse package)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(package);

        ClientWorkflow.WorkflowPackagePublishRequest? sent = null;
        WorkflowService
            .PublishPackageAsync(Arg.Do<ClientWorkflow.WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new ClientWorkflow.WorkflowPublishOutcome(
                new ClientWorkflow.WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.07.2", "acct-1234567890ab@v2026.07.2"),
                Array.Empty<string>()));

        var page = Render<Templates>();
        await edit(page);

        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

        Assert.NotNull(sent);

        var manifest = JsonSerializer.Deserialize<ServerWorkflow.WorkflowPackageManifest>(sent!.Manifest.GetRawText(), JsonOptions)!;
        var files = sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return (
            ServerWorkflow.WorkflowPackageValidator.Validate(manifest, files, new Dictionary<string, string>(StringComparer.Ordinal)),
            sent);
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    [Fact]
    public async Task V7Package_EditedInputs_ComposesAValidManifest()
    {
        var result = await PublishAndValidateAsync(page =>
        {
            Navigate(page, "Inputs");
            page.Find(".add-variable__form input.node-field__input").Change("labs");
            page.Find(".add-variable__form button").Click();
            return Task.CompletedTask;
        });

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task V7Package_RenamedInput_ComposesAValidManifest()
    {
        // The cascade's whole purpose: after a rename the bindings still name
        // a declared slot, so the package validates.
        var result = await PublishAndValidateAsync(page =>
        {
            Navigate(page, "Inputs");
            page.FindAll("li.declared-row")[0].QuerySelector("input.declared-row__id")!.Change("referral");
            return Task.CompletedTask;
        });

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task V7Package_RelabelledDocument_KeepsTheResultsFormOnly()
    {
        var result = await PublishAndValidateAsync(page =>
        {
            Navigate(page, "Documents");
            page.FindAll("li.declared-row")[0]
                .QuerySelector("input.node-field__input:not(.declared-row__id)")!
                .Change("Consult letter");
            return Task.CompletedTask;
        });

        // Before the repair this failed with "Declare result or results, not
        // both" — the composer wrote the string result unconditionally.
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task V6Package_StillComposesAValidManifest()
    {
        // Publish is gated on pending edits, so this needs a real one — hence
        // the trip to a standard, since the editor opens on Graph.
        var result = await PublishAndValidateAsync(page =>
        {
            Navigate(page, "History");
            page.Find("fluent-text-area").Change("Document the presenting illness, chronologically.");
            return Task.CompletedTask;
        }, v7: false);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    // #309: a value is a data path with NO trailing slash. That one character
    // decides whether the resolver reads a scalar or hunts for an index.json,
    // and the composition path is shared with collections.

    [Fact]
    public async Task AddedValue_ComposesADataPathWithoutATrailingSlash()
    {
        var (result, sent) = await PublishAndCaptureAsync(async page =>
        {
            Navigate(page, "+ Data value");
            page.Find(".new-item-fields fluent-text-field").Change("note_type");
            page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create value")).Click();
            page.Find("fluent-text-area").Change("consult note");
            await Task.CompletedTask;
        }, EditorFixtures.V6());

        var data = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement.GetProperty("data");
        Assert.Equal("data/note_type.txt", data.GetProperty("note_type").GetString());
        Assert.Equal("consult note", sent.Files["data/note_type.txt"]);

        // The collection beside it keeps its slash — the two shapes coexist.
        Assert.Equal("data/standards/", data.GetProperty("standards").GetString());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task CollectionsOnlyPackage_RoundTripsItsDataMapUnchanged()
    {
        // The regression guard: the composition path is shared, so a stray
        // slash either way would silently reclassify an entry in a package
        // that never asked for values at all. Publish is gated on pending
        // edits, so this makes one that has nothing to do with the data map.
        var (result, sent) = await PublishAndCaptureAsync(page =>
        {
            Navigate(page, "History");
            page.Find("fluent-text-area").Change("Document the presenting illness, chronologically.");
            return Task.CompletedTask;
        }, EditorFixtures.V6());

        var data = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement.GetProperty("data");
        Assert.Equal(new[] { "standards" }, data.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("data/standards/", data.GetProperty("standards").GetString());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task EditedPublishedValue_SendsTheNewTextAtItsDeclaredPath()
    {
        var (result, sent) = await PublishAndCaptureAsync(page =>
        {
            Navigate(page, "specialty");
            page.Find("fluent-text-area").Change("cardiology");
            return Task.CompletedTask;
        }, EditorFixtures.V6WithValue());

        Assert.Equal("cardiology", sent.Files["data/specialty.txt"]);

        // Editing a value must not disturb the entry that points at it.
        var data = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement.GetProperty("data");
        Assert.Equal("data/specialty.txt", data.GetProperty("specialty").GetString());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task DeletedPublishedValue_LosesBothItsFileAndItsDataMapEntry()
    {
        // #321 needs both halves, and this is the only place that can prove
        // it: the validator resolves every data entry against the files, so a
        // map still naming the deleted value fails here with "is missing from
        // the package" — the composed manifest looks fine on its own.
        var (result, sent) = await PublishAndCaptureAsync(page =>
        {
            Navigate(page, "urgency");
            page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Remove").Click();
            return Task.CompletedTask;
        }, EditorFixtures.V6WithUnusedValue());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.DoesNotContain("data/urgency.txt", sent.Files.Keys);

        // One key leaves, not the map: the bound value and the collection stay.
        var data = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement.GetProperty("data");
        Assert.Equal(new[] { "standards", "specialty" }, data.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task V8Authoring_ComposesAManifestTheValidatorAccepts()
    {
        // #316: the point of doing this here rather than asserting on JSON is
        // that the composed payload goes through the SERVER's validator — the
        // difference between "the JSON looks right" and "the registry would
        // accept this".
        var (result, sent) = await PublishAndCaptureAsync(page =>
        {
            Navigate(page, "Inputs");
            page.FindAll("select.declared-row__type")[1].Change("enum");
            page.Find("li.declared-row__values input").Change("new_patient");
            page.Find("li.declared-row__values input").Change("follow_up");

            Navigate(page, "Documents");
            page.Find("li.declared-row__when select").Change("prior_notes");
            return Task.CompletedTask;
        }, EditorFixtures.V8());

        var root = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement;

        var typed = root.GetProperty("inputs").EnumerateArray()
            .First(i => i.GetProperty("id").GetString() == "prior_notes");
        Assert.Equal("enum", typed.GetProperty("type").GetString());
        Assert.Equal(
            new[] { "new_patient", "follow_up" },
            typed.GetProperty("values").EnumerateArray().Select(v => v.GetString()).ToArray());

        var conditional = root.GetProperty("results").EnumerateArray()
            .First(r => r.GetProperty("when").ValueKind != JsonValueKind.Undefined);
        Assert.Contains("prior_notes", conditional.GetProperty("when").GetString());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    // #350: the three edits that change a condition's operands after it
    // exists. Every other v8 authoring test builds the condition last and
    // never touches it again, which is why none of these was reachable.

    private static void ConditionOnABoolean(IRenderedComponent<Templates> page)
    {
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change("boolean");

        Navigate(page, "Documents");
        page.Find("li.declared-row__when select").Change("prior_notes");
    }

    [Fact]
    public async Task RenamingATestedInput_CarriesTheConditionWithIt()
    {
        // The cascade already followed bindings; a condition names its input
        // the same way, and did not move — the composed package then read an
        // input that no longer existed.
        var (result, sent) = await PublishAndCaptureAsync(page =>
        {
            ConditionOnABoolean(page);

            Navigate(page, "Inputs");
            page.FindAll("li.declared-row")[1].QuerySelector("input.declared-row__id")!.Change("billable");
            return Task.CompletedTask;
        }, EditorFixtures.V8());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));

        var when = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement
            .GetProperty("results").EnumerateArray()
            .Select(r => r.TryGetProperty("when", out var value) ? value.GetString() : null)
            .First(value => value != null);

        Assert.Contains("billable", when!, StringComparison.Ordinal);
        Assert.DoesNotContain("prior_notes", when!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARenameNoConditionReads_LeavesTheDocumentsAlone()
    {
        // The guard on the guard: touching MutableResults() unconditionally
        // would mark every document pending for a rename that has nothing to
        // do with them, and publish two changes where the author made one.
        var (result, sent) = await PublishAndCaptureAsync(page =>
        {
            Navigate(page, "Inputs");
            page.FindAll("li.declared-row")[1].QuerySelector("input.declared-row__id")!.Change("referral");
            return Task.CompletedTask;
        }, EditorFixtures.V8());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));

        Assert.All(
            JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement.GetProperty("results").EnumerateArray(),
            deliverable => Assert.False(deliverable.TryGetProperty("when", out _)));
    }

    [Fact]
    public async Task AV7Package_ComposesTheSameBytesItAlwaysDid()
    {
        // type omitted means text, so an untouched v7 declaration must not
        // gain the word — the migration story depends on this.
        var (result, sent) = await PublishAndCaptureAsync(page =>
        {
            Navigate(page, "Inputs");
            page.FindAll("li.declared-row input")[1].Change("Referral letter");
            return Task.CompletedTask;
        }, EditorFixtures.V7());

        var inputs = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement.GetProperty("inputs");

        Assert.All(inputs.EnumerateArray(), input =>
        {
            Assert.False(input.TryGetProperty("type", out _));
            Assert.False(input.TryGetProperty("values", out _));
        });
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }
}
