namespace Consultologist.PackageFormat;

/// <summary>
/// A well-formed package this engine will not run: pre-v5 (archived), or a
/// version validated and published ahead of the engine accepting it — which is
/// how v8 lands, validator gate first (package-format-v8-design.md § 8).
///
/// Distinct from a registry failure on purpose. Both used to arrive as a bare
/// InvalidOperationException, so the starter reported "the registry is
/// unavailable" for a package that was sitting there perfectly readable, and
/// logged it as an error. SpecVersionNotYetExecutable already existed for this
/// and was raised nowhere.
/// </summary>
public sealed class WorkflowPackageSpecVersionException : Exception
{
    public WorkflowPackageSpecVersionException(string packageRef, int specVersion, IReadOnlyList<int> supported)
        : base($"Workflow package {packageRef} is specVersion {specVersion}; this engine runs specVersion {string.Join(" or ", supported)}. Pre-v5 packages are archived and not executable.")
    {
        SpecVersion = specVersion;
    }

    public int SpecVersion { get; }
}

/// <summary>
/// A package that resolved and downloaded cleanly, and whose CONTENT this
/// engine will not accept: it fails validation, or a declared schema matches no
/// contract in the loaded catalog.
///
/// The second case is the one worth naming (#374). A published version is
/// immutable, but the schema-to-catalog match is re-evaluated on every load, so
/// a catalog change can strand a package that was valid when it was published —
/// nothing about the package having changed. Reported as "the registry is
/// unavailable" it sent an operator to look at storage, which is fine, for a
/// package which is also fine.
///
/// Same reasoning as WorkflowPackageSpecVersionException above, and the same
/// remedy: say what is actually wrong.
/// </summary>
public sealed class WorkflowPackageContentException : Exception
{
    public WorkflowPackageContentException(string message) : base(message)
    {
    }

    /// <summary>
    /// The stranding sentence. It names the CATALOG version because that is the
    /// thing that moved — the package cannot have, and saying so is the
    /// difference between an operator checking the pin and an operator checking
    /// storage that is working.
    /// </summary>
    public static WorkflowPackageContentException SchemaUnmatched(
        string packageRef, string schemaId, string catalogRef)
        => new($"Workflow package {packageRef} schema '{schemaId}' does not canonically match any contract in "
            + $"{catalogRef}. The package is unchanged and immutable; the catalog moved.");

    /// <summary>
    /// #433: the stamped stranding sentence. A stamped package is stranded only
    /// when a contract it was published under is gone — it names both catalogs,
    /// so the reader sees the whole distance the catalog moved.
    /// </summary>
    public static WorkflowPackageContentException StampedContractUnknown(
        string packageRef, string schemaId, string contractId, string stampedCatalogRef, string catalogRef)
        => new($"Workflow package {packageRef} schema '{schemaId}' was published as contract '{contractId}' under "
            + $"{stampedCatalogRef}, which {catalogRef} no longer carries. The package is unchanged and immutable; the catalog moved.");

    /// <summary>#433: a stamp that does not cover a declared schema — written at publish, so a publisher defect.</summary>
    public static WorkflowPackageContentException StampIncomplete(
        string packageRef, string schemaId, string stampedCatalogRef)
        => new($"Workflow package {packageRef} schema '{schemaId}' has no contract in its publication stamp "
            + $"({stampedCatalogRef}). The stamp was written at publish and is incomplete.");
}
