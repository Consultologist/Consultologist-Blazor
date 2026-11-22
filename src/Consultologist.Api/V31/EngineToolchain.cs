using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.V31;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 3; R4): the toolchain this process runs a 31 job with, as <c>lock.json</c>
/// names one (<see cref="Toolchain.Current"/>): the package format release this build vendors, the formats catalogue it
/// loaded, the 31 agents catalogue (read lazily, so a host that does not run 31 never reads it) and, when the job's lock
/// names one, the output contracts catalogue. Read in an activity, never in the orchestration: assembly versions can
/// differ between workers.
/// </summary>
public sealed class EngineToolchain(
    EngineAttestationResponse engine, OutputContractCatalog contracts, WorkflowFormatCatalog formats, Lazy<V31AgentsCatalogue> agents)
{
    private readonly EngineAttestationResponse _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly OutputContractCatalog _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    private readonly WorkflowFormatCatalog _formats = formats ?? throw new ArgumentNullException(nameof(formats));
    private readonly Lazy<V31AgentsCatalogue> _agents = agents ?? throw new ArgumentNullException(nameof(agents));

    /// <summary>The agents catalogue's registry name, as <c>lock.json</c>'s <c>catalogues.agents</c> writes it (<c>agents@v2026.12.1</c>).</summary>
    public const string AgentsRegistry = "agents";

    /// <summary>
    /// <c>versionsRan</c> for a job of <paramref name="lockDocument"/> (<see cref="VersionsRan.Of"/> over this process's
    /// toolchain): <c>resolver.packageFormat</c> null when this build vendors no package format release, and
    /// <c>catalogues.outputContracts</c> null when the lock names none (the package uses none).
    /// </summary>
    public JsonObject VersionsRanFor(JsonObject lockDocument)
    {
        ArgumentNullException.ThrowIfNull(lockDocument);
        var packageFormat = EngineAttestation.RefOf(EngineAttestation.PackageFormatRegistry, _engine.PackageFormat);
        var outputContracts = LockCompiler.Member(LockCompiler.Member(lockDocument, "catalogues"), "outputContracts") is null
            ? null
            : _contracts.ResolvedRef;
        var toolchain = Toolchain.Current(
            packageFormat ?? string.Empty, _formats.ResolvedRef, $"{AgentsRegistry}@{_agents.Value.Version}", outputContracts);

        var ran = VersionsRan.Of(toolchain, lockDocument);
        if (packageFormat == null)
            ran["resolver"]!["packageFormat"] = null;
        return ran;
    }
}
