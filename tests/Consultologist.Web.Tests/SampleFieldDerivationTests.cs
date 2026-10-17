using System.Text.Json;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// #991 (B): an object input's fields derived from a sample response — the
/// inverse of the coercion table, optional and never an enum, existing fields
/// kept as they are, refusals naming keys and deriving nothing.
/// </summary>
public class SampleFieldDerivationTests
{
    private static JsonElement Sample(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static readonly IReadOnlyList<WorkflowManifestReader.FieldView> None = Array.Empty<WorkflowManifestReader.FieldView>();

    [Fact]
    public void AnEmptyOwner_DerivesEveryKey_OptionalTypedFromTheSample()
    {
        var outcome = SampleFieldDerivation.DeriveFields(Sample("""
            { "name": "A", "dob": "1961-03-04", "age": 65, "smoker": false, "allergies": ["latex"], "visit": { "date": "2026-10-01" } }
            """), None);

        Assert.Empty(outcome.Refusals);
        Assert.Equal(new[] { "name", "dob", "age", "smoker", "allergies", "visit" }, outcome.Added);
        var fields = outcome.Fields.ToDictionary(field => field.Id);
        Assert.Null(fields["name"].Type);
        Assert.Equal("date", fields["dob"].Type);
        Assert.Equal("number", fields["age"].Type);
        Assert.Equal("boolean", fields["smoker"].Type);
        Assert.Equal("array", fields["allergies"].Type);
        Assert.Equal("text", fields["allergies"].Items!.Type);
        Assert.Equal("object", fields["visit"].Type);
        Assert.Equal("date", Assert.Single(fields["visit"].Fields!).Type);
        Assert.All(outcome.Fields, field => Assert.False(field.Required));
        Assert.All(outcome.Fields, field => Assert.Equal(field.Id, field.Label));
    }

    [Fact]
    public void ExistingFields_AreKeptAndNeverRetyped()
    {
        var existing = new[] { new WorkflowManifestReader.FieldView("age", "Age", true) };

        var outcome = SampleFieldDerivation.DeriveFields(Sample("""{ "age": 65, "smoker": true }"""), existing);

        Assert.Equal(new[] { "smoker" }, outcome.Added);
        Assert.Equal(new[] { "age", "smoker" }, outcome.Fields.Select(field => field.Id));
        Assert.Null(outcome.Fields[0].Type);
        Assert.True(outcome.Fields[0].Required);
    }

    [Fact]
    public void AnArrayOfObjects_PutsTheFieldsOnTheField()
    {
        var outcome = SampleFieldDerivation.DeriveFields(Sample("""{ "meds": [ { "name": "x", "dose": 5 } ] }"""), None);

        var meds = Assert.Single(outcome.Fields);
        Assert.Equal("array", meds.Type);
        Assert.Equal("object", meds.Items!.Type);
        Assert.Null(meds.Items.Fields);
        Assert.Equal(new[] { "name", "dose" }, meds.Fields!.Select(field => field.Id));
        Assert.Equal("number", meds.Fields[1].Type);
    }

    [Fact]
    public void AnArrayOfArrays_NestsTheElement()
    {
        var outcome = SampleFieldDerivation.DeriveFields(Sample("""{ "grid": [[1]] }"""), None);

        var grid = Assert.Single(outcome.Fields);
        Assert.Equal("array", grid.Items!.Type);
        Assert.Equal("number", grid.Items.Items!.Type);
    }

    [Fact]
    public void NonSnakeCaseKeys_RefuseByName_AndDeriveNothing()
    {
        var existing = new[] { new WorkflowManifestReader.FieldView("ok", "ok", false) };

        var outcome = SampleFieldDerivation.DeriveFields(Sample("""{ "onLetrozole": true, "ok": 1, "Patient Age": 2 }"""), existing);

        Assert.Equal(new[]
        {
            "Key 'onLetrozole' is not snake_case (a lowercase letter, then lowercase letters, digits, or underscores); rename it in the sample.",
            "Key 'Patient Age' is not snake_case (a lowercase letter, then lowercase letters, digits, or underscores); rename it in the sample.",
        }, outcome.Refusals);
        Assert.Empty(outcome.Added);
        Assert.Equal(existing, outcome.Fields);
    }

    [Fact]
    public void NullsAndEmptyStructures_RefuseByName()
    {
        var outcome = SampleFieldDerivation.DeriveFields(Sample("""{ "a": null, "b": [], "c": {} }"""), None);

        Assert.Equal(new[]
        {
            "Key 'a' is null; give it a sample value.",
            "Key 'b' is an empty array; give it one sample element.",
            "Key 'c' is an empty object; give it one sample member.",
        }, outcome.Refusals);
        Assert.Empty(outcome.Fields);
    }

    [Fact]
    public void ANonObjectSample_IsRefused()
    {
        var outcome = SampleFieldDerivation.DeriveFields(Sample("[1]"), None);

        Assert.Equal(new[] { "The sample must be a JSON object." }, outcome.Refusals);
    }

    [Fact]
    public void StructureBelowTen_IsRefusedByName()
    {
        var outcome = SampleFieldDerivation.DeriveFields(Sample("""{ "visit": { "date": "2026-10-01" }, "age": 1 }"""), None, allowStructure: false);

        Assert.Equal(new[] { "Key 'visit' is structure (an object or an array); fields below specVersion 10 are scalars." }, outcome.Refusals);
        Assert.Empty(outcome.Added);
    }

    [Fact]
    public void ANestedRefusal_NamesThePath()
    {
        var outcome = SampleFieldDerivation.DeriveFields(Sample("""{ "visit": { "Bad Key": 1 }, "meds": [ { "x": null } ] }"""), None);

        Assert.Contains("Key 'visit.Bad Key' is not snake_case (a lowercase letter, then lowercase letters, digits, or underscores); rename it in the sample.", outcome.Refusals);
        Assert.Contains("Key 'meds[].x' is null; give it a sample value.", outcome.Refusals);
    }
}
