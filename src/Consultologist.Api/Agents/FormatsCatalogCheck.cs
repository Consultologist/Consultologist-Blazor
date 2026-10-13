using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Agents;

/// <summary>
/// The pre-publish check of a formats catalogue version (package-format-v25-
/// design.md § 3.3, B1 #1000): the agents repo's CI runs it over its
/// <c>agents/</c> directory before anything is uploaded, through
/// <c>scripts/check-formats-catalog.cs</c>. It is the engine's own reader —
/// <see cref="WorkflowFormatCatalogIndexJson"/>, <see cref="WorkflowFormatCatalog"/>
/// — plus the one rule only the registry can check: that each attested
/// entry's agent yaml declares the entry's shape as its response format, at
/// the version the index names. One definition, so CI and the engine agree
/// by construction.
/// </summary>
public static class FormatsCatalogCheck
{
    /// <summary>The classification agent answers one object, <c>{ value }</c>; an enum format's agent must declare exactly that.</summary>
    public const string EnumAnswerSchema = """{ "type": "object", "additionalProperties": false, "required": ["value"], "properties": { "value": { "type": "string" } } }""";

    public sealed record Result(WorkflowFormatCatalog? Catalog, IReadOnlyList<string> Errors)
    {
        public bool IsValid => Errors.Count == 0 && Catalog is not null;
    }

    /// <summary>Runs every check over <paramref name="agentsDir"/> (the directory holding <c>formats.json</c>, <c>formats/</c> and the agent yamls).</summary>
    public static Result Run(string agentsDir)
    {
        var errors = new List<string>();
        var indexPath = Path.Combine(agentsDir, WorkflowFormatRegistry.IndexFileName);

        if (!File.Exists(indexPath))
        {
            errors.Add($"No {WorkflowFormatRegistry.IndexFileName} in {Path.GetFullPath(agentsDir)}.");
            return new Result(null, errors);
        }

        if (!WorkflowFormatCatalogIndexJson.TryRead(File.ReadAllText(indexPath), "Format catalogue", errors, out var index))
        {
            return new Result(null, errors);
        }

        var catalog = WorkflowFormatCatalog.Build(
            index!,
            path =>
            {
                var file = Path.Combine(agentsDir, path);
                return File.Exists(file) ? File.ReadAllText(file) : null;
            },
            errors);

        if (catalog is null)
        {
            return new Result(null, errors);
        }

        foreach (var (id, format) in catalog.Formats.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (format.Entry.AgentName is null)
            {
                continue;
            }

            var manifest = LoadAgent(agentsDir, format.Entry.AgentName, $"Format '{id}'", errors);

            if (manifest is null)
            {
                continue;
            }

            CheckVersion($"Format '{id}'", manifest, format.Entry.AgentVersion!.Value, errors);
            CheckResponseFormat(id, format.Spec, manifest, errors);
        }

        var custom = catalog.CustomProducer;
        var customManifest = LoadAgent(agentsDir, custom.AgentName, "producers.custom", errors);

        if (customManifest is not null)
        {
            CheckVersion("producers.custom", customManifest, custom.AgentVersion, errors);

            var type = customManifest.Definition.Text?.Format?.Type;

            if (!string.Equals(type, "text", StringComparison.Ordinal))
            {
                errors.Add($"producers.custom agent '{custom.AgentName}' declares text.format.type '{type ?? "(none)"}'; the generic structured agent answers text the engine parses and validates against the package's schema.");
            }
        }

        return new Result(errors.Count == 0 ? catalog : null, errors);
    }

    private static AttestedAgentManifest? LoadAgent(string agentsDir, string agentName, string subject, List<string> errors)
    {
        var path = Path.Combine(agentsDir, agentName + ".yaml");

        if (!File.Exists(path))
        {
            errors.Add($"{subject} names agent '{agentName}', but {agentName}.yaml is not in the registry.");
            return null;
        }

        try
        {
            var manifest = AttestedAgentManifest.Load(File.ReadAllText(path));

            if (!string.Equals(manifest.Name, agentName, StringComparison.Ordinal))
            {
                errors.Add($"{subject} names agent '{agentName}', but {agentName}.yaml declares name '{manifest.Name}'.");
                return null;
            }

            return manifest;
        }
        catch (Exception ex)
        {
            errors.Add($"{subject} agent '{agentName}' yaml does not load: {ex.Message}");
            return null;
        }
    }

    internal static void CheckVersion(string subject, AttestedAgentManifest manifest, int expected, List<string> errors)
    {
        if (!int.TryParse(manifest.Version, out var declared) || declared != expected)
        {
            errors.Add($"{subject} binds agent '{manifest.Name}' at version {expected}, but {manifest.Name}.yaml declares version '{manifest.Version}'; the index and the yaml must agree.");
        }
    }

    /// <summary>
    /// A text format's agent answers text; an object format's agent declares
    /// the format's schema, strictly, as its json_schema response format; an
    /// enum format's agent declares the one-value answer object.
    /// </summary>
    internal static void CheckResponseFormat(string id, WorkflowFormatSpec spec, AttestedAgentManifest manifest, List<string> errors)
    {
        var format = manifest.Definition.Text?.Format;
        var type = format?.Type ?? "(none)";
        var subject = $"Format '{id}' agent '{manifest.Name}'";

        switch (spec.Type)
        {
            case WorkflowInputTypes.Text:
                if (!string.Equals(type, "text", StringComparison.Ordinal))
                {
                    errors.Add($"{subject} declares text.format.type '{type}', but the format is text.");
                }

                break;

            case WorkflowInputTypes.Object:
                if (!string.Equals(type, "json_schema", StringComparison.Ordinal))
                {
                    errors.Add($"{subject} declares text.format.type '{type}', but the format is an object; the agent must declare the format's schema as a strict json_schema response format.");
                    return;
                }

                CompareSchema(subject, spec.Schema, format!, errors);
                break;

            case WorkflowInputTypes.Enum:
                if (!string.Equals(type, "json_schema", StringComparison.Ordinal))
                {
                    errors.Add($"{subject} declares text.format.type '{type}', but the format is an enum; the agent must answer the one-value object {{ value }} as a strict json_schema response format.");
                    return;
                }

                CompareSchema(subject, JsonNode.Parse(EnumAnswerSchema), format!, errors);
                break;

            default:
                errors.Add($"{subject}: a {spec.Type} format has no attested response-format rule yet.");
                break;
        }
    }

    private static void CompareSchema(string subject, JsonNode? expected, AttestedAgentManifest.AttestedTextFormat format, List<string> errors)
    {
        if (!string.Equals(format.Strict, "true", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{subject} json_schema response format is not strict.");
        }

        JsonNode? declared;

        try
        {
            declared = string.IsNullOrWhiteSpace(format.Schema) ? null : JsonNode.Parse(format.Schema);
        }
        catch (Exception ex)
        {
            errors.Add($"{subject} text.format.schema is not valid JSON: {ex.Message}");
            return;
        }

        if (AttestedAgentManifest.CanonicalJson(expected) != AttestedAgentManifest.CanonicalJson(declared))
        {
            errors.Add($"{subject} text.format.schema differs from the format's schema; the agent must declare exactly the shape the catalogue publishes.");
        }
    }

    /// <summary>One line per binding, for the CI log.</summary>
    public static IEnumerable<string> Describe(WorkflowFormatCatalog catalog)
    {
        foreach (var (id, format) in catalog.Formats.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var binding = format.Entry.AgentDisplay ?? (format.Entry.Op is { } op ? $"op {op}" : "no agent (rendered)");
            yield return $"  {id} ({format.Spec.Type}, {format.Spec.Producer}) -> {binding}";
        }

        yield return $"  custom -> {catalog.CustomProducer.AgentDisplay}";
    }
}
