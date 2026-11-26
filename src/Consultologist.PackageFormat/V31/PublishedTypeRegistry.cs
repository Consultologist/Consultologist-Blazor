namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1508 (specVersion 31, area 11; 2026-11-25 item 11; R20): the one loader of the published 31 type registry
/// (<see cref="TypeRegistry"/>) from its two folders as they are published: the package format's <c>v31/</c> (its
/// <c>formats/</c> and <c>renderers/index.json</c>) and the agents catalogue's <c>agents/v31/</c> (its <c>formats/</c>,
/// <c>bindings.json</c> and <c>agents.json</c>). The Api's bundled copy, the ingestor, the scripts and the tests' contract
/// fixture all read it through <see cref="Read"/>, so each reads the same files the same way.
/// </summary>
public static class PublishedTypeRegistry
{
    /// <summary>
    /// The registry under <paramref name="packageFormatV31Folder"/> (the package format's <c>v31/</c>) and
    /// <paramref name="agentsV31Folder"/> (the agents catalogue's <c>agents/v31/</c>).
    /// </summary>
    /// <exception cref="FormatException">A folder or file is missing, is not JSON, or is not what it should be.</exception>
    public static TypeRegistry Read(string packageFormatV31Folder, string agentsV31Folder)
    {
        ArgumentNullException.ThrowIfNull(packageFormatV31Folder);
        ArgumentNullException.ThrowIfNull(agentsV31Folder);
        if (!Directory.Exists(packageFormatV31Folder))
            throw new FormatException($"Registry folder '{packageFormatV31Folder}' is missing.");
        if (!Directory.Exists(agentsV31Folder))
            throw new FormatException($"Registry folder '{agentsV31Folder}' is missing.");

        return TypeRegistry.Read(
            Path.Combine(packageFormatV31Folder, "formats"),
            Path.Combine(agentsV31Folder, "formats"),
            Path.Combine(packageFormatV31Folder, "renderers", "index.json"),
            Path.Combine(agentsV31Folder, "bindings.json"),
            Path.Combine(agentsV31Folder, "agents.json"));
    }
}
