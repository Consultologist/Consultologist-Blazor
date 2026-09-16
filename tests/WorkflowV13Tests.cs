using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v13 (#728): a slot may declare the content channel it expects —
/// `expectedContent: transcript` or `form` — beside its value type. The
/// baseline is v12's plus the version line; the fixtures add one declared
/// channel at a time so a refusal names exactly one rule.
/// </summary>
public static class V13Fixtures
{
    public static WorkflowPackageManifest Minimal() => V12Fixtures.Minimal() with { SpecVersion = 13 };

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

public class WorkflowV13ExpectedContentTests
{
    private static IReadOnlyList<string> Errors(WorkflowPackageManifest manifest) => V13Fixtures.Validate(manifest).Errors;

    [Fact]
    public void ATranscriptSlot_OnAText_IsValid()
    {
        var manifest = V13Fixtures.WithInput(new WorkflowInputSpec(
            "meeting_transcript", "Meeting transcript", Required: false,
            Type: WorkflowInputTypes.Text, ExpectedContent: WorkflowExpectedContent.Transcript));

        Assert.True(V13Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void ATranscriptSlot_OnAnArrayOfText_IsValid()
    {
        var manifest = V13Fixtures.WithInput(new WorkflowInputSpec(
            "transcripts", "Transcripts", Required: false,
            Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text,
            ExpectedContent: WorkflowExpectedContent.Transcript));

        Assert.True(V13Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void AFormSlot_OnAScalar_IsValid()
    {
        var manifest = V13Fixtures.WithInput(new WorkflowInputSpec(
            "encounter_kind", "Encounter kind",
            Type: WorkflowInputTypes.Enum, Values: new List<string> { "new_patient", "follow_up" },
            ExpectedContent: WorkflowExpectedContent.Form));

        Assert.True(V13Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void ExpectedContent_BelowThirteen_IsRefusedByVersion()
    {
        var manifest = V13Fixtures.WithInput(new WorkflowInputSpec(
            "meeting_transcript", "Meeting transcript", Required: false,
            Type: WorkflowInputTypes.Text, ExpectedContent: WorkflowExpectedContent.Transcript)) with { SpecVersion = 12 };

        Assert.Contains(
            "Input 'meeting_transcript' declares expectedContent, which requires specVersion 13.",
            Errors(manifest));
    }

    [Fact]
    public void AnUnknownChannel_IsRefusedByName()
    {
        var manifest = V13Fixtures.WithInput(new WorkflowInputSpec(
            "voicemail", "Voicemail", Required: false,
            Type: WorkflowInputTypes.Text, ExpectedContent: "voicemail"));

        Assert.Contains(
            "Input 'voicemail' declares unknown expectedContent 'voicemail' (accepted: transcript, form).",
            Errors(manifest));
    }

    [Fact]
    public void ATranscript_OnANonText_IsRefused()
    {
        var manifest = V13Fixtures.WithInput(new WorkflowInputSpec(
            "length_of_stay", "Length of stay", Required: false,
            Type: WorkflowInputTypes.Number, ExpectedContent: WorkflowExpectedContent.Transcript));

        Assert.Contains(
            "Input 'length_of_stay' declares expectedContent 'transcript', which is only for a text slot or an array of text.",
            Errors(manifest));
    }

    [Fact]
    public void AForm_OnAnObject_IsRefused()
    {
        var manifest = V13Fixtures.WithInput(new WorkflowInputSpec(
            "patient", "Patient", Required: false, Type: WorkflowInputTypes.Object,
            Fields: new List<WorkflowFieldSpec> { new("age", "Age") },
            ExpectedContent: WorkflowExpectedContent.Form));

        Assert.Contains(
            "Input 'patient' declares expectedContent 'form', which a slot of type 'object' cannot be filled from.",
            Errors(manifest));
    }
}
