using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v17 (#673): a slot may declare `expectedContent: ambient-note` — it expects
/// an uploaded clinical note produced by an ambient scribe (Dragon Copilot).
/// Like `transcript`, ambient-note is a text channel and a submitter assertion:
/// only a text slot or an array of text can be filled from it. The baseline is
/// v16's plus the version line.
/// </summary>
public static class V17Fixtures
{
    public static WorkflowPackageManifest Minimal() => V16Fixtures.Minimal() with { SpecVersion = 17 };

    public static WorkflowPackageManifest WithInput(WorkflowInputSpec input)
    {
        var inputs = new List<WorkflowInputSpec>(Minimal().Inputs!);
        var index = inputs.FindIndex(i => i.Id == input.Id);
        if (index < 0) inputs.Add(input); else inputs[index] = input;
        return Minimal() with { Inputs = inputs };
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);
}

public class WorkflowV17AmbientNoteContentTests
{
    private static IReadOnlyList<string> Errors(WorkflowPackageManifest manifest) => V17Fixtures.Validate(manifest).Errors;

    [Fact]
    public void AnAmbientNoteSlot_OnAText_IsValid()
    {
        var manifest = V17Fixtures.WithInput(new WorkflowInputSpec(
            "encounter_note", "Encounter note", Required: false,
            Type: WorkflowInputTypes.Text, ExpectedContent: WorkflowExpectedContent.AmbientNote));

        Assert.True(V17Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void AnAmbientNoteSlot_OnAnArrayOfText_IsValid()
    {
        var manifest = V17Fixtures.WithInput(new WorkflowInputSpec(
            "encounter_notes", "Encounter notes", Required: false,
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text,
            ExpectedContent: WorkflowExpectedContent.AmbientNote));

        Assert.True(V17Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void AmbientNoteContent_BelowSeventeen_IsRefusedByName()
    {
        // Below 17 the ambient-note channel is not in the accepted set, so it is
        // refused by membership (transcript/form/image remain from v13/v16).
        var manifest = V17Fixtures.WithInput(new WorkflowInputSpec(
            "encounter_note", "Encounter note", Required: false,
            Type: WorkflowInputTypes.Text, ExpectedContent: WorkflowExpectedContent.AmbientNote)) with { SpecVersion = 16 };

        Assert.Contains(
            "Input 'encounter_note' declares unknown expectedContent 'ambient-note' (accepted: transcript, form, image).",
            Errors(manifest));
    }

    [Fact]
    public void AnAmbientNote_OnANonText_IsRefused()
    {
        var manifest = V17Fixtures.WithInput(new WorkflowInputSpec(
            "length_of_stay", "Length of stay", Required: false,
            Type: WorkflowInputTypes.Number, ExpectedContent: WorkflowExpectedContent.AmbientNote));

        Assert.Contains(
            "Input 'length_of_stay' declares expectedContent 'ambient-note', which is only for a text slot or an array of text.",
            Errors(manifest));
    }

    [Fact]
    public void TheEarlierChannels_StillValid_AtSeventeen()
    {
        // transcript/form/image carry forward unchanged.
        var image = V17Fixtures.WithInput(new WorkflowInputSpec(
            "referral_letter", "Referral letter", Required: false,
            Type: WorkflowInputTypes.Text, ExpectedContent: WorkflowExpectedContent.Image));
        Assert.True(V17Fixtures.Validate(image).IsValid, string.Join(" | ", Errors(image)));
    }
}
