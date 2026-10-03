using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v21 (package-format-v21.md, #923): a declared output schema may be a
/// REFERENCE to a published catalog output-contract by name (schema id →
/// contract id) instead of an inline body carried in the bundle. A referenced
/// schema has no file and skips the canonical match — it resolves by the named
/// contract id. Below 21 it is refused by name; a reference naming a contract
/// the catalog does not know is refused; a schema id declared both inline and
/// as a reference is refused. The inline-body path (schemas) is unchanged.
/// </summary>
public static class V21Fixtures
{
    /// <summary>
    /// The chained minimal (which declares the <c>concept-list</c> schema inline
    /// and has nodes whose output is it), bumped to <paramref name="specVersion"/>
    /// with that schema moved from an inline body to a reference. <paramref
    /// name="contract"/> overrides the contract the reference names (for the
    /// unknown-contract refusal), and <paramref name="alsoInline"/> keeps the
    /// inline body too (for the declared-both refusal).
    /// </summary>
    public static (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) SchemaRef(
        int specVersion = 21, string contract = "concept-list", bool alsoInline = false)
    {
        var baseManifest = V12Fixtures.Minimal() with { SpecVersion = specVersion };
        var files = new Dictionary<string, string>(V6Fixtures.Files(baseManifest), StringComparer.Ordinal);

        var schemas = new Dictionary<string, string>(baseManifest.Schemas!, StringComparer.Ordinal);
        var schemaPath = schemas["concept-list"];
        if (!alsoInline)
        {
            schemas.Remove("concept-list");
            files.Remove(schemaPath);
        }

        var manifest = baseManifest with
        {
            Schemas = schemas.Count == 0 ? null : schemas,
            SchemaRefs = new Dictionary<string, string>(StringComparer.Ordinal) { ["concept-list"] = contract }
        };

        return (manifest, files);
    }

    public static WorkflowPackageValidator.ValidationResult Validate(
        (WorkflowPackageManifest Manifest, IReadOnlyDictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas);
}

public class WorkflowV21SchemaRefTests
{
    private static IReadOnlyList<string> Errors(
        (WorkflowPackageManifest, IReadOnlyDictionary<string, string>) bundle) =>
        V21Fixtures.Validate(bundle).Errors;

    [Fact]
    public void TheValidatorAccepts21_AndTheStoreRunsIt()
    {
        Assert.Contains(21, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.Contains(21, Consultologist.Api.Workflow.WorkflowPackageStore.SupportedSpecVersions);
    }

    [Fact]
    public void ASchemaReference_IsValid()
    {
        var bundle = V21Fixtures.SchemaRef();

        Assert.True(V21Fixtures.Validate(bundle).IsValid, string.Join(" | ", Errors(bundle)));
    }

    [Fact]
    public void ASchemaReference_NeedsNoInlineBodyFile()
    {
        // The whole point: a reference carries no schema file. The bundle's files
        // no longer include the concept-list body, and that is valid.
        var (_, files) = V21Fixtures.SchemaRef();

        Assert.DoesNotContain(files.Keys, k => k.Contains("concept-list.json"));
    }

    [Fact]
    public void SchemaRefs_BelowTwentyOne_IsRefusedByName()
    {
        var bundle = V21Fixtures.SchemaRef(specVersion: 20);

        Assert.Contains(Errors(bundle), e => e.Contains("schemaRefs requires specVersion 21"));
    }

    [Fact]
    public void AReferenceNamingAnUnknownContract_IsRefused()
    {
        var bundle = V21Fixtures.SchemaRef(contract: "no-such-contract");

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("names contract 'no-such-contract', which is not a catalog output contract"));
    }

    [Fact]
    public void ASchemaDeclaredBothInlineAndAsAReference_IsRefused()
    {
        var bundle = V21Fixtures.SchemaRef(alsoInline: true);

        Assert.Contains(
            Errors(bundle),
            e => e.Contains("is declared both inline and as a reference"));
    }
}
