using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v22 (package-format-v22.md, #760): a declared output schema may be a CUSTOM,
/// user-defined shape — schema id → an inline package file holding a JSON body
/// that is NOT a catalog contract and does NOT canonically match one. A custom
/// schema carries a file (like schemas) but skips the canonical match (like a
/// reference), resolves to the reserved <c>custom</c> contract, and is run by a
/// content-addressed generic no-tool agent (its output is unattested). Below 22
/// it is refused by name; a body outside the strict structured-output subset is
/// refused; a schema id declared in more than one of schemas/schemaRefs/
/// customSchemas is refused.
/// </summary>
public static class V22Fixtures
{
    public const string CustomSchemaPath = "schemas/my-note.json";

    // A strict-structured-output-subset body: an object with
    // additionalProperties:false and every property in required.
    public const string ValidCustomBody = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary"],
          "properties": {
            "summary": { "type": "string" }
          }
        }
        """;

    /// <summary>
    /// The v5 minimal, bumped to <paramref name="specVersion"/>, with the
    /// <c>create-typical-trajectory</c> node (consumed downstream without a concept
    /// renderer) repointed from the inline concept-list schema to a custom schema
    /// <c>my-note</c>. <paramref name="body"/> overrides the custom body (for the
    /// subset refusal); <paramref name="alsoInline"/> also declares the id inline
    /// (for the declared-in-two-maps refusal).
    /// </summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) Custom(
        int specVersion = 22, string? body = null, bool alsoInline = false)
    {
        var baseManifest = V12Fixtures.Minimal() with { SpecVersion = specVersion };
        var files = new Dictionary<string, string>(V6Fixtures.Files(baseManifest), StringComparer.Ordinal);
        files[CustomSchemaPath] = body ?? ValidCustomBody;

        var nodes = baseManifest.Nodes!
            .Select(node => node.Id == "create-typical-trajectory"
                ? node with { Output = new WorkflowNodeOutputSpec("my-note") }
                : node)
            .ToList();

        var schemas = new Dictionary<string, string>(baseManifest.Schemas!, StringComparer.Ordinal);
        if (alsoInline)
        {
            schemas["my-note"] = V5Fixtures.SchemaPath;
        }

        var manifest = baseManifest with
        {
            Nodes = nodes,
            Schemas = schemas,
            CustomSchemas = new Dictionary<string, string>(StringComparer.Ordinal) { ["my-note"] = CustomSchemaPath }
        };

        return (manifest, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate(
        (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);
}

public class WorkflowV22CustomSchemaTests
{
    private static IReadOnlyList<string> Errors(
        (WorkflowPackageManifest, IReadOnlyDictionary<string, string>) bundle) =>
        V22Fixtures.Validate(bundle).Errors;

    [Fact]
    public void TheValidatorAccepts22_AndTheStoreSupportsIt()
    {
        Assert.Contains(22, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.Contains(22, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions);
    }

    [Fact]
    public void ACustomSchema_IsValid()
    {
        var bundle = V22Fixtures.Custom();

        Assert.True(V22Fixtures.Validate(bundle).IsValid, string.Join(" | ", Errors(bundle)));
    }

    [Fact]
    public void CustomSchemas_BelowTwentyTwo_IsRefusedByName()
    {
        var bundle = V22Fixtures.Custom(specVersion: 21);

        Assert.Contains(Errors(bundle), e => e.Contains("customSchemas requires specVersion 22"));
    }

    [Fact]
    public void ACustomSchemaDeclaredInTwoMaps_IsRefused()
    {
        var bundle = V22Fixtures.Custom(alsoInline: true);

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("is declared both as custom and elsewhere"));
    }

    [Fact]
    public void ACustomBodyMissingAdditionalPropertiesFalse_IsRefused()
    {
        var bundle = V22Fixtures.Custom(body: """
            { "type": "object", "required": ["summary"], "properties": { "summary": { "type": "string" } } }
            """);

        Assert.Contains(Errors(bundle), e => e.Contains("must set additionalProperties:false"));
    }

    [Fact]
    public void ACustomBodyWithAnUnrequiredProperty_IsRefused()
    {
        // "note" is a property but not listed in required — optionality must be a
        // nullable type, not omission (the strict structured-output subset).
        var bundle = V22Fixtures.Custom(body: """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["summary"],
              "properties": { "summary": { "type": "string" }, "note": { "type": "string" } }
            }
            """);

        Assert.Contains(Errors(bundle), e => e.Contains("must be listed in required"));
    }

    [Fact]
    public void ACustomBodyThatIsNotJson_IsRefused()
    {
        var bundle = V22Fixtures.Custom(body: "{ not valid json");

        Assert.Contains(Errors(bundle), e => e.Contains("is not valid JSON"));
    }
}
