namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): where a format a scope can name comes from: the built-in
/// formats the spec publishes, the agents catalogue's, the package's own <c>formats/</c>, or a
/// function's. Later tasks give the set its lookup; the origin is what the sentences name.
/// </summary>
public enum FormatOrigin
{
    /// <summary>One of the nine formats of <c>v31/formats/</c>.</summary>
    BuiltIn,

    /// <summary>A format of the agents catalogue (<c>classification</c>, <c>concept_list</c>).</summary>
    Catalogue,

    /// <summary>A file of the package's <c>formats/</c> folder.</summary>
    Package,

    /// <summary>A file of one function's <c>formats/</c> folder.</summary>
    Function,
}
