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
    public async Task ARenameNoConditionReads_ComposesNoCondition()
    {
        // The composed half only: the cascade must not invent a when clause
        // for documents that never had one. Whether it marks them *pending*
        // is invisible here — the manifest is identical either way — so
        // TemplatesV8AuthoringTests asserts that separately.
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

    [Fact]
    public async Task V8FieldsOnAV7Package_AreCaughtBeforePublishing()
    {
        // #347's dead end. The editor offers the v8 controls whatever the
        // package is, so a v7 fork can compose a manifest the registry refuses
        // — and before this it only found out after pressing Publish.
        //
        // The server rule stays the authority and is pinned in WorkflowV8Tests;
        // this asserts the editor says it first, and names the way out.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());

        var page = Render<Templates>();
        Navigate(page, "Inputs");
        page.FindAll("select.declared-row__type")[1].Change("enum");
        page.Find("li.declared-row__values input").Change("new_patient");
        page.Find("li.declared-row__values input").Change("follow_up");

        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("requires specVersion 8", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Upgrade to specVersion 8", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameFieldsWithAPendingUpgrade_AreAccepted()
    {
        // The other half, and the whole issue: the identical edit, published
        // with the migration pending, is a manifest the registry accepts.
        var (result, sent) = await PublishAndCaptureAsync(async page =>
        {
            Navigate(page, "Inputs");
            page.FindAll("select.declared-row__type")[1].Change("enum");
            page.Find("li.declared-row__values input").Change("new_patient");
            page.Find("li.declared-row__values input").Change("follow_up");

            Upgrade(page);
            await Task.CompletedTask;
        }, EditorFixtures.V7());

        var root = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement;

        Assert.Equal(8, root.GetProperty("specVersion").GetInt32());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void APackageAlreadyAtTheNewestVersion_IsNotOfferedAnUpgrade()
    {
        // "Upward only" is the issue's first design rule, and here it is
        // enforced by the control simply not existing rather than by a refusal
        // — so it needs asserting, or the rule lives only in a comment.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V8());

        var page = Render<Templates>();

        Assert.DoesNotContain("Upgrade to specVersion", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AV7Package_IsOfferedTheUpgrade()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());

        var page = Render<Templates>();

        Assert.Contains("Upgrade to specVersion 8", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUpgradeAlone_ChangesOnlyTheSpecVersion()
    {
        // The proving migration, in the editor: package-format-v8.md's own
        // argument is that changing specVersion and nothing else is what makes
        // a migration provable — any difference afterwards is the engine.
        var (result, sent) = await PublishAndCaptureAsync(
            page => { Upgrade(page); return Task.CompletedTask; },
            EditorFixtures.V7());

        var before = JsonDocument.Parse(EditorFixtures.V7().Manifest.GetRawText()).RootElement;
        var after = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement;

        Assert.Equal(7, before.GetProperty("specVersion").GetInt32());
        Assert.Equal(8, after.GetProperty("specVersion").GetInt32());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));

        // Every other top-level property unchanged. Compared semantically
        // rather than byte for byte: composing re-serializes through JsonNode,
        // which normalises the whitespace of the whole document. Harmless, but
        // it means an upgrade rewrites formatting it did not mean to touch.
        static string Canonical(JsonElement value) =>
            JsonSerializer.Serialize(JsonDocument.Parse(value.GetRawText()).RootElement);

        foreach (var property in before.EnumerateObject().Where(p => p.Name != "specVersion"))
        {
            Assert.Equal(
                Canonical(property.Value),
                Canonical(after.GetProperty(property.Name)));
        }
    }

    /// <summary>
    /// #404: every upgrade test above loads V7, where IsV7 is already true, so
    /// the path that breaks was never exercised. A v6 fork is offered the same
    /// Upgrade button, and the editor keeps showing it the surface the LOADED
    /// version had rather than the one the publish will carry.
    /// </summary>
    [Fact]
    public void AV6ForkWithAPendingUpgrade_CanReachWhatItUpgradedFor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6());
        var page = Render<Templates>();

        Assert.DoesNotContain("Inputs", NavLabels(page));

        Upgrade(page);

        Assert.Contains("Inputs", NavLabels(page));
        Assert.Contains("Documents", NavLabels(page));
    }

    [Fact]
    public async Task AV6ForkUpgradedWithNothingDeclared_IsStoppedAtTheDesk()
    {
        // The sharpest half. DeclaredSectionErrors returns early when the
        // LOADED version is below 7, so a bumped v6 fork skips every pre-flight
        // check and composes a manifest stamped 8 with no inputs — which the
        // server refuses outright. The pre-flight exists precisely so a refusal
        // arrives before a version is minted.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6());
        var page = Render<Templates>();

        Upgrade(page);
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish")).Click();

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains("inputs is required in specVersion 7", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AV6ForkUpgraded_CanDeclareAnInputAndPublishItAsV8()
    {
        // End to end through the real server validator: adopting a newer
        // format's features is what #347 was for, and until this the author had
        // to publish a bare bump and reload before they could author anything.
        var (result, sent) = await PublishAndCaptureAsync(async page =>
        {
            Upgrade(page);
            Navigate(page, "Inputs");
            page.Find(".add-variable__form input").Change("consult_draft");
            page.FindAll("button.variable-chips__add").First(b => b.TextContent.Contains("+ Input")).Click();
            await Task.CompletedTask;
        }, EditorFixtures.V6());

        var root = JsonDocument.Parse(sent.Manifest.GetRawText()).RootElement;

        Assert.Equal(8, root.GetProperty("specVersion").GetInt32());
        Assert.Equal("consult_draft", root.GetProperty("inputs")[0].GetProperty("id").GetString());
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    /// <summary>
    /// A second defect, found while fixing the first and fixed with it. The
    /// prompt selector and the aggregator node kind were gated on
    /// `SpecVersion == 6` — exactly 6 — but package-format-v7.md says in its own
    /// opening that "everything not stated here is unchanged from
    /// package-format-v6.md: aggregator nodes … prompt sharing". So v7 and v8
    /// packages have always been denied both.
    ///
    /// The aggregator half is the one that dead-ends an author: aggregators are
    /// the only legal v7/v8 deliverable node, and AddResultAsync refuses a
    /// second document with "add an aggregator node first" — advice that could
    /// not be followed, because the option to add one did not render.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromptSharingAndAggregators_SurviveTheVersionTheyWereIntroducedIn(bool v7)
    {
        WorkflowService.GetCurrentPackageContentAsync()
            .Returns(v7 ? EditorFixtures.V7() : EditorFixtures.V6());
        var page = Render<Templates>();
        Navigate(page, "+ Node");

        Assert.Contains("aggregator", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AV6ForkWithAPendingUpgrade_KeepsTheAggregatorItStillNeeds()
    {
        // Under a naive "read the effective version" sweep this would be the
        // regression: == 6 becomes false the moment the bump lands. The gate is
        // wrong in the other direction, so it becomes >= 6 and the control stays.
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6());
        var page = Render<Templates>();

        Upgrade(page);
        Navigate(page, "+ Node");

        Assert.Contains("aggregator", page.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The silent half. Draft restore dropped pending inputs and results when
    /// the LOADED package was below v7 — even with a bump to 8 already
    /// restored, two lines earlier. No error, no badge, no console line: the
    /// slices simply vanished, and the next persist rewrote localStorage
    /// without them, so the work was gone from the browser too.
    ///
    /// bUnit returns null from localStorage.getItem by default, which is why
    /// restore has never had coverage. Stubbing the one call is enough.
    /// </summary>
    [Fact]
    public void ADraftCarryingInputs_SurvivesAReloadWithABumpPending()
    {
        var package = EditorFixtures.V6();
        WorkflowService.GetCurrentPackageContentAsync().Returns(package);

        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult("""
                {
                  "Version": 11,
                  "SpecVersionBump": 8,
                  "Inputs": [ { "Id": "prior_notes", "Label": "Prior notes", "Required": false } ]
                }
                """);

        var page = Render<Templates>();

        Navigate(page, "Inputs");
        Assert.Contains("prior_notes", page.Markup, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> NavLabels(IRenderedComponent<Templates> page) =>
        page.FindAll("button.editor-nav__item")
            .Select(b => b.TextContent.Replace("\u25CF", string.Empty).Trim())
            .ToList();

    private static void Upgrade(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button")
            .First(button => button.TextContent.Contains("Upgrade to specVersion", StringComparison.Ordinal))
            .Click();
}
