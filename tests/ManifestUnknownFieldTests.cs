using System.Text.Json;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #416: the format says "a section the version does not have is never a
/// silently ignored field" (package-format-v8.md § 2). The engine accepted them
/// anyway — and the publisher re-serialises the typed record, so a field written
/// in the content repo vanished on the next editor publish rather than being
/// refused (#398).
///
/// Raw JSON strings on purpose. The conformance suite cannot cover this: its
/// manifests are generated from the same record that does the rejecting, so it
/// can never produce a property the record lacks.
/// </summary>
public class ManifestUnknownFieldTests
{
    private const string MinimalV8 = """
        {
          "name": "general",
          "version": "v2026.08.1",
          "specVersion": 8,
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "nodes": [ { "id": "assemble", "label": "Assembling", "aggregate": ["node:draft"] } ],
          "inputs": [ { "id": "consult_draft", "label": "Consult draft" } ],
          "result": "node:assemble"
        }
        """;

    private static string WithExtra(string property) =>
        MinimalV8.TrimEnd().TrimEnd('}') + $",\n  {property}\n}}";

    [Fact]
    public void AManifestTheFormatAllows_StillReads()
    {
        var manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(
            MinimalV8, WorkflowPackageManifestJson.ReadOptions);

        Assert.NotNull(manifest);
        Assert.Equal(8, manifest!.SpecVersion);
    }

    [Theory]
    // The field #398 traced: written in the content repo, silently dropped by
    // the publisher on the next fork.
    [InlineData("\"formatRef\": \"package-format@v2026.08.3\"")]
    // A typo. Case-insensitivity forgives derivedfrom; it cannot forgive this,
    // and before now the package simply had no lineage and validated anyway.
    [InlineData("\"derived_from\": \"general@v2026.07.10\"")]
    // Retired vocabulary, which is what a pre-v5 manifest is made of.
    [InlineData("\"sectionSteps\": []")]
    public void AFieldTheFormatDoesNotHave_IsRefused(string extra)
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<WorkflowPackageManifest>(
                WithExtra(extra), WorkflowPackageManifestJson.ReadOptions));

        // The property, not merely "malformed" — an author has to know which
        // one to remove.
        Assert.Contains("$.", WorkflowPackageManifestJson.Describe(exception));
    }

    [Fact]
    public void TitleAndDescription_AreMembersTheReaderKnows()
    {
        // #432: typed on the record, so the strict reader accepts them on any
        // version. Refusing them below 9 is the validator's, by name.
        var manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(
            WithExtra("\"title\": \"Breast oncology consults\", \"description\": \"Referral triage.\""),
            WorkflowPackageManifestJson.ReadOptions);

        Assert.Equal("Breast oncology consults", manifest!.Title);
        Assert.Equal("Referral triage.", manifest.Description);
    }

    [Fact]
    public void TheDescription_NamesThePropertyAndNotADotNetType()
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<WorkflowPackageManifest>(
                WithExtra("\"formatRef\": \"x\""), WorkflowPackageManifestJson.ReadOptions));

        var described = WorkflowPackageManifestJson.Describe(exception);

        Assert.Equal("Manifest property '$.formatRef' is not part of the package format.", described);
        // The exception's own message names WorkflowPackageManifest, which is an
        // implementation detail an author has no use for.
        Assert.DoesNotContain("Consultologist.Api", described, StringComparison.Ordinal);
    }

    [Fact]
    public void ANestedUnknownField_IsRefusedToo()
    {
        // The declared sections are where a version-gated field would be typo'd,
        // and the path has to survive to be useful.
        var manifest = MinimalV8.Replace(
            "\"inputs\": [ { \"id\": \"consult_draft\", \"label\": \"Consult draft\" } ]",
            "\"inputs\": [ { \"id\": \"consult_draft\", \"label\": \"Consult draft\", \"kind\": \"date\" } ]");

        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<WorkflowPackageManifest>(
                manifest, WorkflowPackageManifestJson.ReadOptions));

        Assert.Equal("Manifest property '$.inputs[0].kind' is not part of the package format.", WorkflowPackageManifestJson.Describe(exception));
    }

    [Fact]
    public void TheVersionIsReadable_WithoutImposingTheCurrentShape()
    {
        // The ordering the store depends on: a pre-v5 manifest made of retired
        // vocabulary must still say which version it is, so it can be refused
        // for being archived rather than for being unparseable.
        var archived = """
            {
              "name": "general",
              "version": "v2026.07.4",
              "specVersion": 3,
              "sectionSteps": [ { "id": "hpi" } ]
            }
            """;

        Assert.True(WorkflowPackageManifestJson.TryReadSpecVersion(archived, out var specVersion));
        Assert.Equal(3, specVersion);

        // And the strict read of that same manifest does fail — which is why the
        // version has to be read first.
        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<WorkflowPackageManifest>(archived, WorkflowPackageManifestJson.ReadOptions));
    }

    [Fact]
    public void AnArchivedPackage_IsRefusedForBeingArchived_NotForItsVocabulary()
    {
        // The ordering, asserted. general@v2026.07.4 is a real registry version:
        // specVersion 3, carrying the retired sectionSteps. Reading the shape
        // before the version would refuse it as a parse error naming a field,
        // in place of the sentence that says what it actually is — and a mutant
        // that reversed the order passed the entire suite before this existed.
        var archived = """
            {
              "name": "general",
              "version": "v2026.07.4",
              "specVersion": 3,
              "sectionSteps": [ { "id": "hpi" } ]
            }
            """;

        var exception = Assert.Throws<WorkflowPackageSpecVersionException>(() =>
            WorkflowPackageManifestJson.Read(archived, "general@v2026.07.4", WorkflowPackageStore.SupportedSpecVersions));

        Assert.Equal(3, exception.SpecVersion);
        Assert.Contains("archived and not executable", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sectionSteps", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASupportedPackageWithAStrayField_IsAContentFailure()
    {
        // The other side of the same branch: once the version is one the engine
        // runs, an unknown property is the package's content being wrong, which
        // is a 422 with the detail rather than a registry outage.
        var exception = Assert.Throws<WorkflowPackageContentException>(() =>
            WorkflowPackageManifestJson.Read(
                WithExtra("\"formatRef\": \"x\""), "general@v2026.08.1", WorkflowPackageStore.SupportedSpecVersions));

        Assert.Contains("$.formatRef", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASupportedPackageThatIsClean_Reads()
    {
        var manifest = WorkflowPackageManifestJson.Read(
            MinimalV8, "general@v2026.08.1", WorkflowPackageStore.SupportedSpecVersions);

        Assert.Equal(8, manifest.SpecVersion);
        Assert.Equal("general", manifest.Name);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{ \"name\": \"general\" }")]
    [InlineData("{ \"specVersion\": \"8\" }")]
    public void AVersionThatCannotBeRead_SaysSoRatherThanGuessing(string json)
    {
        Assert.False(WorkflowPackageManifestJson.TryReadSpecVersion(json, out var specVersion));
        Assert.Equal(0, specVersion);
    }
}
