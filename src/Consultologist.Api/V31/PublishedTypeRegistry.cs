using Consultologist.Api.Agents;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.V31;

/// <summary>
/// #1507 (specVersion 31, area 10; R11, D22): the deployment's 31 type registry (<see cref="TypeRegistry"/>), read from
/// the files <c>Consultologist.Api.csproj</c> bundles: the package format's built-in formats
/// (<c>package-format/v31/formats/</c>) and renderers (<c>package-format/v31/renderers/index.json</c>), and the 31 agents
/// catalogue's formats, <c>bindings.json</c> and <c>agents.json</c> (<c>agents/v31/</c>, under
/// <see cref="OutputContractCatalog.ResolveDirectory"/>). It is read once, on first use of <see cref="Registry"/>, so a
/// host that never migrates or publishes a 31 package never reads it; a read that fails throws at that use and is tried
/// again at the next (a missing or malformed file is the deployment's fault). #1508 (R20): it holds what the library's one
/// loader reads (<see cref="Consultologist.PackageFormat.V31.PublishedTypeRegistry.Read"/>), and the ingestor takes it.
/// The weekly summary's migration line reads it (<see cref="OutstandingHandEdits"/>); area 11's publish path reuses it.
/// </summary>
public sealed class PublishedTypeRegistry
{
    /// <summary>The package format's 31 files under the app base, as the csproj lands them.</summary>
    public const string PackageFormatFolder = "package-format/v31";

    private readonly Lazy<TypeRegistry> _registry;

    /// <summary>The registry under the app base and the bundled agents directory.</summary>
    public PublishedTypeRegistry()
        : this(AppContext.BaseDirectory, OutputContractCatalog.ResolveDirectory())
    {
    }

    /// <summary>The registry under <paramref name="baseDirectory"/> (the package format's files) and <paramref name="agentsDirectory"/> (the agents catalogue's).</summary>
    public PublishedTypeRegistry(string baseDirectory, string agentsDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(agentsDirectory);
        _registry = new Lazy<TypeRegistry>(() => Read(baseDirectory, agentsDirectory), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>The registry, read on first use.</summary>
    /// <exception cref="FormatException">A file is missing, is not JSON, or is not what it should be.</exception>
    public TypeRegistry Registry => _registry.Value;

    private static TypeRegistry Read(string baseDirectory, string agentsDirectory) =>
        Consultologist.PackageFormat.V31.PublishedTypeRegistry.Read(
            Path.Combine(baseDirectory, PackageFormatFolder), Path.Combine(agentsDirectory, V31AgentsCatalogue.Folder));
}
