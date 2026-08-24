using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// What the publication stamp (#433) asks of the output-contract catalog:
/// which contract a schema canonically matches, under which catalog ref.
/// The format defines the question and the stamp's bytes; the engine's
/// catalog — loaded from a registry the format knows nothing about —
/// answers it (#450).
/// </summary>
public interface IOutputContractResolver
{
    /// <summary>The concrete catalog ref, output-contracts@vYYYY.MM.N.</summary>
    string ResolvedRef { get; }

    /// <summary>
    /// The contract whose schema canonically matches (sorted keys,
    /// title/description stripped — <see cref="WorkflowPackageValidator.CanonicalizeSchema"/>).
    /// </summary>
    bool TryResolveContract(JsonNode? schema, out string contractId);
}

/// <summary>The output-contract registry's name, as a catalog ref and a container both spell it.</summary>
public static class OutputContractRegistry
{
    public const string Name = "output-contracts";
}
