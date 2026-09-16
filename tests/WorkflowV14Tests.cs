using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v14 (#729): a slot may declare a union of accepted value types — `type:
/// [text, array]`. The baseline is v13's plus the version line; the slot's one
/// items/fields/values serves the single sub-declaring arm.
/// </summary>
public static class V14Fixtures
{
    public static WorkflowPackageManifest Minimal() => V13Fixtures.Minimal() with { SpecVersion = 14 };

    public static WorkflowPackageManifest WithInput(WorkflowInputSpec input)
    {
        var inputs = new List<WorkflowInputSpec>(Minimal().Inputs!);
        var index = inputs.FindIndex(i => i.Id == input.Id);
        if (index < 0) inputs.Add(input); else inputs[index] = input;
        return Minimal() with { Inputs = inputs };
    }

    public static WorkflowTypeSet Union(params string[] types) => new(types);

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);
}

public class WorkflowV14UnionTests
{
    private static IReadOnlyList<string> Errors(WorkflowPackageManifest manifest) => V14Fixtures.Validate(manifest).Errors;

    [Fact]
    public void ATextOrArrayUnion_IsValid()
    {
        var manifest = V14Fixtures.WithInput(new WorkflowInputSpec(
            "notes", "Notes", Required: false,
            Type: V14Fixtures.Union(WorkflowInputTypes.Text, WorkflowInputTypes.Array),
            Items: WorkflowInputTypes.Text));

        Assert.True(V14Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void AnEnumOrTextUnion_IsValid()
    {
        var manifest = V14Fixtures.WithInput(new WorkflowInputSpec(
            "code", "Code", Required: false,
            Type: V14Fixtures.Union(WorkflowInputTypes.Enum, WorkflowInputTypes.Text),
            Values: new List<string> { "a", "b" }));

        Assert.True(V14Fixtures.Validate(manifest).IsValid, string.Join(" | ", Errors(manifest)));
    }

    [Fact]
    public void AUnion_BelowFourteen_IsRefusedByVersion()
    {
        var manifest = V14Fixtures.WithInput(new WorkflowInputSpec(
            "notes", "Notes", Required: false,
            Type: V14Fixtures.Union(WorkflowInputTypes.Text, WorkflowInputTypes.Number))) with { SpecVersion = 13 };

        Assert.Contains(
            "Input 'notes' declares a union type, which requires specVersion 14.",
            Errors(manifest));
    }

    [Fact]
    public void AUnknownArm_IsRefusedByName()
    {
        var manifest = V14Fixtures.WithInput(new WorkflowInputSpec(
            "notes", "Notes", Required: false,
            Type: V14Fixtures.Union(WorkflowInputTypes.Text, "timestamp")));

        Assert.Contains(Errors(manifest), e => e.Contains("declares unknown type 'timestamp'"));
    }

    [Fact]
    public void TwoStructuredArms_AreRefused()
    {
        var manifest = V14Fixtures.WithInput(new WorkflowInputSpec(
            "either", "Either", Required: false,
            Type: V14Fixtures.Union(WorkflowInputTypes.Array, WorkflowInputTypes.Object),
            Items: WorkflowInputTypes.Text,
            Fields: new List<WorkflowFieldSpec> { new("a", "A") }));

        Assert.Contains(Errors(manifest), e =>
            e.Contains("a union carries at most one type that needs a sub-declaration"));
    }

    [Fact]
    public void ExpectedContent_OnAUnion_IsRefused()
    {
        var manifest = V14Fixtures.WithInput(new WorkflowInputSpec(
            "notes", "Notes", Required: false,
            Type: V14Fixtures.Union(WorkflowInputTypes.Text, WorkflowInputTypes.Array),
            Items: WorkflowInputTypes.Text,
            ExpectedContent: WorkflowExpectedContent.Transcript));

        Assert.Contains(Errors(manifest), e =>
            e.Contains("declares expectedContent on a union type"));
    }

    [Fact]
    public void ARepeatedArm_IsRefused()
    {
        var manifest = V14Fixtures.WithInput(new WorkflowInputSpec(
            "notes", "Notes", Required: false,
            Type: V14Fixtures.Union(WorkflowInputTypes.Text, WorkflowInputTypes.Text)));

        Assert.Contains(Errors(manifest), e => e.Contains("names a type more than once"));
    }
}
