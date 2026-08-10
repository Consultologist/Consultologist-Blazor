using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>
/// specVersion-8 fixtures: the v7 minimal package at 8 with nothing else
/// changed — the migration #317 proves — and a typed variant declaring one of
/// each type (package-format-v8-design.md § 4).
/// </summary>
public static class V8Fixtures
{
    /// <summary>
    /// The whole migration story in one fixture: v7's declaration, specVersion
    /// 8, no types. If this ever fails to validate, `type` has stopped being
    /// optional and minimal-v8 is no longer a one-line change.
    /// </summary>
    public static WorkflowPackageManifest Minimal()
        => V7Fixtures.Minimal() with { SpecVersion = 8 };

    public static WorkflowPackageManifest Typed()
        => Minimal() with
        {
            Inputs = new List<WorkflowInputSpec>
            {
                new("consult_draft", "Consult draft"),
                new("seen_on", "Date seen", Type: WorkflowInputTypes.Date),
                new("encounter_kind", "Encounter kind", Type: WorkflowInputTypes.Enum,
                    Values: new List<string> { "new_patient", "follow_up" }),
                new("billable", "Billable encounter", Required: false, Type: WorkflowInputTypes.Boolean)
            }
        };

    /// <summary>The typed fixture with one input's declaration replaced.</summary>
    public static WorkflowPackageManifest WithInput(WorkflowInputSpec input)
    {
        var inputs = new List<WorkflowInputSpec>(Typed().Inputs!);
        var index = inputs.FindIndex(i => i.Id == input.Id);
        inputs[index] = input;
        return Typed() with { Inputs = inputs };
    }

    public static WorkflowPackageValidator.ValidationResult Validate(WorkflowPackageManifest manifest)
        => WorkflowPackageValidator.Validate(manifest, V6Fixtures.Files(manifest), TestOutputContracts.CatalogSchemas);
}

public class WorkflowV8ValidationTests
{
    [Fact]
    public void MinimalV8_IsValid_WithNoTypesDeclared()
    {
        // The proving migration (#317): specVersion 8 and nothing else. `type`
        // defaults to text precisely so this holds.
        var result = V8Fixtures.Validate(V8Fixtures.Minimal());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void EveryDeclaredType_IsAccepted()
    {
        var result = V8Fixtures.Validate(V8Fixtures.Typed());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void AnUnknownType_IsRejected()
    {
        var manifest = V8Fixtures.WithInput(new("seen_on", "Date seen", Type: "datetime"));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("unknown type 'datetime'") && e.Contains("seen_on"));
    }

    [Fact]
    public void AnEnumWithoutValues_IsRejected()
    {
        var manifest = V8Fixtures.WithInput(new("encounter_kind", "Encounter kind", Type: WorkflowInputTypes.Enum));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("type 'enum' and must declare values"));
    }

    [Fact]
    public void ValuesOnANonEnum_IsRejected()
    {
        // The mirror: values belong to enum and to nothing else, so a date
        // carrying them is a mistake worth naming rather than ignoring.
        var manifest = V8Fixtures.WithInput(new("seen_on", "Date seen",
            Type: WorkflowInputTypes.Date, Values: new List<string> { "a", "b" }));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("is type 'date' and may not declare values"));
    }

    [Fact]
    public void AnEnumWithOneValue_IsRejected()
    {
        var manifest = V8Fixtures.WithInput(new("encounter_kind", "Encounter kind",
            Type: WorkflowInputTypes.Enum, Values: new List<string> { "only" }));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("a constant, not a choice"));
    }

    [Fact]
    public void DuplicateEnumValues_AreRejected()
    {
        var manifest = V8Fixtures.WithInput(new("encounter_kind", "Encounter kind",
            Type: WorkflowInputTypes.Enum, Values: new List<string> { "follow_up", "follow_up" }));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("duplicate enum value 'follow_up'"));
    }

    [Fact]
    public void AnEnumValueBreakingTheIdRule_IsRejected()
    {
        // Enum values share the declared-id rule, which is what makes them safe
        // wherever result ids are — authored content, never patient data.
        var manifest = V8Fixtures.WithInput(new("encounter_kind", "Encounter kind",
            Type: WorkflowInputTypes.Enum, Values: new List<string> { "new_patient", "Follow Up" }));

        Assert.Contains(V8Fixtures.Validate(manifest).Errors,
            e => e.Contains("enum value 'Follow Up'") && e.Contains("snake_case"));
    }

    [Fact]
    public void ATypeOnAV7Manifest_IsRejected()
    {
        // Same posture as "inputs requires specVersion 7": a section the
        // version does not have is an error, never a silently ignored field.
        var manifest = V7Fixtures.Minimal() with
        {
            Inputs = new List<WorkflowInputSpec> { new("consult_draft", "Consult draft", Type: WorkflowInputTypes.Date) }
        };

        Assert.Contains(V7Fixtures.Validate(manifest).Errors,
            e => e.Contains("declares a type, which requires specVersion 8"));
    }

    [Fact]
    public void TheEngineDoesNotRunV8Yet()
    {
        // The sequencing, pinned rather than left to a comment: the validator
        // accepts 8 (so v8 publishes) while the engine still refuses, which is
        // what makes the intermediate state honest. #317 moves this line and
        // this assertion together — if it changes early, a half-built v8
        // becomes runnable and the >= 7 gates would treat it as v7.
        Assert.Equal(new[] { 5, 6, 7 }, WorkflowPackageStore.SupportedSpecVersions);
    }

    [Fact]
    public void SpecVersion8_IsAcceptedByTheValidatorGate()
    {
        // The validator's gate moves before the engine's: v8 publishes here and
        // refuses to run until SupportedSpecVersions catches up.
        Assert.DoesNotContain(V8Fixtures.Validate(V8Fixtures.Minimal()).Errors,
            e => e.Contains("is not supported"));

        Assert.Contains(
            V8Fixtures.Validate(V8Fixtures.Minimal() with { SpecVersion = 9 }).Errors,
            e => e.Contains("accepts specVersion 5, 6, 7 or 8"));
    }
}
