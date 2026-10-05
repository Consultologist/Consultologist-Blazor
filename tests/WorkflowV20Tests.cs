using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v20 (#863): a deliverable's macro reference may be `slot: true` — the model
/// places it inline via a `[[slot:<id>]]` marker it emits in a section's output,
/// and the engine fills the marker at document assembly. Below 20 it is refused
/// by name; a slot names no before/after/forItem (the model owns the position);
/// a slot macro carrying {{profile:signature}} is refused (a deliverable is
/// signed once); a slot-only macro is a use, not an orphan.
/// </summary>
public static class V20Fixtures
{
    /// <summary>
    /// The standing `disclaimer` macro, referenced as a slot on the first
    /// deliverable. Baseline is v12's macro fixture bumped to 20; `text`
    /// overrides the macro file, `alsoAnchor` adds an illegal before-anchor, and
    /// `forItem` an illegal fan-item anchor, for the mutual-exclusion refusals.
    /// </summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) SlotMacro(
        int specVersion = 20, string? text = null, bool alsoAnchor = false, string? forItem = null)
    {
        var (manifest, files) = text is null
            ? V11Fixtures.WithMacro(from: V12Fixtures.Minimal())
            : V11Fixtures.WithMacro(text, from: V12Fixtures.Minimal());

        return (manifest with
        {
            SpecVersion = specVersion,
            Results = manifest.Results!.Select((r, i) => i == 0
                ? r with
                {
                    Macros = new List<WorkflowResultMacroSpec>
                    {
                        new("disclaimer", Before: alsoAnchor ? "node:section-instructions" : null, ForItem: forItem, Slot: true)
                    }
                }
                : r).ToList()
        }, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate(
        (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);
}

public class WorkflowV20SlotTests
{
    private static IReadOnlyList<string> Errors((WorkflowPackageManifest, IReadOnlyDictionary<string, string>) bundle) =>
        V20Fixtures.Validate(bundle).Errors;

    [Fact]
    public void TheValidatorAccepts20_AndTheStoreRunsIt()
    {
        Assert.Contains(20, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.Contains(20, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions);
    }

    [Fact]
    public void ASlotMacro_IsValid()
    {
        var bundle = V20Fixtures.SlotMacro();

        Assert.True(V20Fixtures.Validate(bundle).IsValid, string.Join(" | ", Errors(bundle)));
    }

    [Fact]
    public void ASlotOnlyMacro_IsNotOrphaned()
    {
        // A slot reference counts as a use — a macro referenced ONLY as a slot
        // must not trip the orphan rule.
        var bundle = V20Fixtures.SlotMacro();

        Assert.DoesNotContain(Errors(bundle), e => e.Contains("orphan") || e.Contains("no result references"));
    }

    [Fact]
    public void ASlot_BelowTwenty_IsRefusedByName()
    {
        var bundle = V20Fixtures.SlotMacro(specVersion: 19);

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("declares macro 'disclaimer' as a slot, which requires specVersion 20"));
    }

    [Fact]
    public void ASlot_WithAnAnchor_IsRefused()
    {
        var bundle = V20Fixtures.SlotMacro(alsoAnchor: true);

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("as a slot and also anchors it with before/after/forItem"));
    }

    [Fact]
    public void ASlot_WithAForItem_IsRefused()
    {
        var bundle = V20Fixtures.SlotMacro(forItem: "hpi");

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("as a slot and also anchors it with before/after/forItem"));
    }

    [Fact]
    public void ASlotMacro_CarryingTheSignatureToken_IsRefused()
    {
        var bundle = V20Fixtures.SlotMacro(text: "Sincerely,\n\n{{profile:signature}}");

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("places macro 'disclaimer' as a slot, and the macro carries")
                && e.Contains("signed once"));
    }

    [Fact]
    public void ASlot_RoundTripsThroughTheConverter()
    {
        var entry = new WorkflowResultMacroSpec("disclaimer", Slot: true);

        // The converter is registered on the type; read back the way the engine
        // does (WorkflowPackageManifestJson.ReadOptions — case-insensitive).
        var json = System.Text.Json.JsonSerializer.Serialize(entry);
        var back = System.Text.Json.JsonSerializer.Deserialize<WorkflowResultMacroSpec>(
            json, WorkflowPackageManifestJson.ReadOptions);

        Assert.Contains("\"slot\":true", json);
        Assert.True(back!.IsSlot);
        Assert.False(back.IsBare);
    }
}

/// <summary>
/// #863: the assembly-time slot fill (ConsultSlotFiller) over the composed
/// document. The marker survives in the section node's output hash; here it is
/// replaced (or dropped) and attributed as a Slot appended entry.
/// </summary>
public class WorkflowV20SlotFillTests
{
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["side_effects"] = "Common side effects include hot flashes and arthralgia."
    };

    private static Consultologist.Api.Jobs.ConsultMacroExpander.RunFacts Facts() =>
        new(new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc), "0123456789abcdef", "general@v2026.09.11", "east.ca.api.consultologist.ai", "Taylor Reyes");

    private static (string Text, IReadOnlyList<Consultologist.Api.Models.ConsultAppendedEntry>? Entries) Fill(
        string composed, params string[] authorized) =>
        Consultologist.Api.Jobs.ConsultSlotFiller.Fill(
            composed,
            authorized.ToHashSet(StringComparer.Ordinal),
            Texts, NoValues, null, NoValues, Facts());

    [Fact]
    public void AnAuthorizedMarker_IsReplacedWithTheMacroText_AndAttributed()
    {
        var (text, entries) = Fill(
            "Assessment and plan.\n\n[[slot:side_effects]]\n\nFollow up in three months.",
            "side_effects");

        Assert.Equal(
            "Assessment and plan.\n\nCommon side effects include hot flashes and arthralgia.\n\nFollow up in three months.",
            text);
        Assert.NotNull(entries);
        Assert.Equal(
            new[] { (Consultologist.Api.Models.ConsultAppendedKinds.Slot, "side_effects") },
            entries!.Select(e => (e.Kind, e.Id)).ToArray());
    }

    [Fact]
    public void AnUnauthorizedMarker_IsRemoved_NoEntry()
    {
        // The model emitted a marker this deliverable never sanctioned (or one
        // its gate excluded) — the marker never leaks into the document.
        var (text, entries) = Fill(
            "Plan.\n\n[[slot:side_effects]]", /* nothing authorized */ Array.Empty<string>());

        Assert.Equal("Plan.\n\n", text);
        Assert.Null(entries);
    }

    [Fact]
    public void AnUnknownMarkerId_IsRemoved_EvenWhenAnotherSlotIsAuthorized()
    {
        var (text, entries) = Fill(
            "A [[slot:ghost]] B [[slot:side_effects]] C",
            "side_effects");

        Assert.Equal("A  B Common side effects include hot flashes and arthralgia. C", text);
        Assert.Single(entries!);
    }

    [Fact]
    public void NoMarker_IsByteIdentical_NoEntries()
    {
        var (text, entries) = Fill("A plain assembled document.", "side_effects");

        Assert.Equal("A plain assembled document.", text);
        Assert.Null(entries);
    }
}

/// <summary>
/// #942: a slot macro only fills where the model emits its [[slot:id]] marker,
/// which a prompt/standard/prelude must instruct. A slot no text names, and a
/// marker naming no slot macro, are both silent — so the validator warns, never
/// blocks.
/// </summary>
public class WorkflowV20SlotInstructionWarningTests
{
    private static (WorkflowPackageManifest, Dictionary<string, string>) WithPromptText(
        (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle, string appended)
    {
        var key = bundle.Files.Keys.First(k => k.StartsWith("prompts/", StringComparison.Ordinal));
        var files = new Dictionary<string, string>(bundle.Files) { [key] = bundle.Files[key] + "\n" + appended };
        return (bundle.Manifest, files);
    }

    private static WorkflowPackageValidator.ValidationResult Validate(
        (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Item1, bundle.Item2, TestOutputContracts.CatalogSchemas);

    [Fact]
    public void AnUninstructedSlot_Warns_ButStaysValid()
    {
        // The baseline slot fixture names [[slot:disclaimer]] in no text.
        var result = V20Fixtures.Validate(V20Fixtures.SlotMacro());

        Assert.Contains(result.Warnings, w => w.Contains("Slot macro 'disclaimer' is never instructed"));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AnInstructedSlot_DoesNotWarn()
    {
        var result = Validate(WithPromptText(V20Fixtures.SlotMacro(), "Run [[slot:disclaimer]] verbatim."));

        Assert.DoesNotContain(result.Warnings, w => w.Contains("never instructed"));
    }

    [Fact]
    public void AStrayMarker_NamingNoSlotMacro_Warns()
    {
        // disclaimer is now instructed; ghost names no slot macro and is dropped.
        var result = Validate(WithPromptText(V20Fixtures.SlotMacro(), "[[slot:disclaimer]] and [[slot:ghost]]."));

        Assert.Contains(result.Warnings, w => w.Contains("[[slot:ghost]]") && w.Contains("not a slot macro"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("never instructed"));
    }

    [Fact]
    public void AnAppendedMacro_IsNotASlot_AndNeverWarns()
    {
        // A non-slot (appended) macro on a v20 package: no slot warnings at all.
        var (manifest, files) = V11Fixtures.WithMacro(from: V12Fixtures.Minimal());
        var result = WorkflowPackageValidator.Validate(manifest with { SpecVersion = 20 }, files, TestOutputContracts.CatalogSchemas);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("never instructed") || w.Contains("not a slot macro"));
    }
}
