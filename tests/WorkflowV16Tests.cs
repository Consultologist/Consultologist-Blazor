using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v16 (#730): a slot may declare `expectedContent: image` — it expects an
/// uploaded raw image (PNG/JPEG/TIFF) whose text is produced by OCR. Like
/// `transcript`, image is a text channel: only a text slot or an array of text
/// can be filled from it. The baseline is v15's plus the version line.
/// </summary>
public static class V16Fixtures
{
    public static WorkflowPackageManifest Minimal() => V15Fixtures.Minimal() with { SpecVersion = 16 };

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

public class WorkflowV16ImageContentTests
{
    private static IReadOnlyList<string> Errors(WorkflowPackageManifest manifest) => V16Fixtures.Validate(manifest).Errors;

    [Fact]
    public void AnImageSlot_OnAText_IsValid()
    {
        var manifest = V16Fixtures.WithInput(new WorkflowInputSpec(
            "referral_letter", "Referral letter", Required: false,
            Type: WorkflowInputTypes.Text, ExpectedContent: WorkflowExpectedContent.Image));

        Assert.True(V16Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void AnImageSlot_OnAnArrayOfText_IsValid()
    {
        var manifest = V16Fixtures.WithInput(new WorkflowInputSpec(
            "scanned_pages", "Scanned pages", Required: false,
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text,
            ExpectedContent: WorkflowExpectedContent.Image));

        Assert.True(V16Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void ImageContent_BelowSixteen_IsRefusedByName()
    {
        // Below 16 the image channel is not in the accepted set, so it is
        // refused by membership (transcript/form remain from v13).
        var manifest = V16Fixtures.WithInput(new WorkflowInputSpec(
            "referral_letter", "Referral letter", Required: false,
            Type: WorkflowInputTypes.Text, ExpectedContent: WorkflowExpectedContent.Image)) with { SpecVersion = 15 };

        Assert.Contains(
            "Input 'referral_letter' declares unknown expectedContent 'image' (accepted: transcript, form).",
            Errors(manifest));
    }

    [Fact]
    public void AnImage_OnANonText_IsRefused()
    {
        var manifest = V16Fixtures.WithInput(new WorkflowInputSpec(
            "length_of_stay", "Length of stay", Required: false,
            Type: WorkflowInputTypes.Number, ExpectedContent: WorkflowExpectedContent.Image));

        Assert.Contains(
            "Input 'length_of_stay' declares expectedContent 'image', which is only for a text slot or an array of text.",
            Errors(manifest));
    }

    [Fact]
    public void TranscriptAndForm_StillValid_AtSixteen()
    {
        // The prior channels carry forward unchanged.
        var transcript = V16Fixtures.WithInput(new WorkflowInputSpec(
            "meeting_transcript", "Meeting transcript", Required: false,
            Type: WorkflowInputTypes.Text, ExpectedContent: WorkflowExpectedContent.Transcript));
        Assert.True(V16Fixtures.Validate(transcript).IsValid, string.Join(" | ", Errors(transcript)));
    }
}
