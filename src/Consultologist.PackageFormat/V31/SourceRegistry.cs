namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): the names a package may not reuse, given to the reader by its
/// caller: the built-in and catalogue format names (S01-034, part 02 § 6) and the built-in
/// renderer names (S01-035, part 01 § 9.1). The reader holds no list of its own, so the names are
/// the deployment's registry, read the same at publish and import.
/// </summary>
public sealed record SourceRegistry(
    IReadOnlySet<string> BuiltInFormats,
    IReadOnlySet<string> CatalogueFormats,
    IReadOnlySet<string> BuiltInRenderers);
